using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Membership;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// The read surface, the mark renderer and the page, asserted together because
// the claim the checkpoint owes runs through all three: the page draws the
// fixture's sessions, the drawn candle count matches the stored row count, and
// the API computes nothing.
public class ReadSurface
{
    // What this reaches, declared in the check itself. The three screens claims
    // are claims about a surface, so they are reached by a check that renders
    // the surface and reads it back, and never by a declaration.
    internal static CheckReach Reach => new(
        "read-surface",
        ["fixtures/membership-2026-09-05"],
        [
            CheckReach.Key("15.4 The two surfaces", "The app"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, candles"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, a volume pane"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, the moving averages"),
            CheckReach.Key("15.5 The mark vocabulary", "Volume profile"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, the level bands"),
            CheckReach.Key("15.5 The mark vocabulary", "Momentum panel"),
            CheckReach.Key(Scope.FailureTable, "Fewer than 200 bars for a new index member, nn bars"),
            CheckReach.Key("15.9 Name", "The chart"),
        ]);

    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";
    const string Name = "AAPL";

    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // The fixture's year in a temporary store. Nothing here reaches data/.
    static async Task<TemporaryStore> Populated()
    {
        var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json")),
            clock,
            store.DatabaseFile).LoadAsync(Index, "run-0");

        await new Backfill(
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile).RunAsync(Index, "run-1");

        return store;
    }

    static ReadApi Api(TemporaryStore store) =>
        new(store.DatabaseFile, FixedClock.At(Instant, SessionZones.UnitedStates));

    // The stored rows for one name, read by a path that is not the API, so the
    // comparison below is against the store and not against itself.
    static IReadOnlyList<string> StoredRows(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT session_date, open, high, low, close, volume FROM bar " +
            "WHERE ticker = $ticker ORDER BY session_date;";
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join(
                "|",
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt64(5)));
        }

        return rows;
    }

    [Fact]
    public async Task TheApiHandsBackEveryStoredValueUnchanged()
    {
        // The done condition's third clause, carried by behaviour rather than
        // by a source scan. If the API derived any value, adjusted any price or
        // filled any gap, one of these strings would differ. A scan for
        // arithmetic would report a pattern; this reports the values.
        using var store = await Populated();

        var stored = StoredRows(store, Name);
        var served = await Api(store).BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        Assert.True(stored.Count >= 250, $"The fixture holds {stored.Count} sessions for {Name}, expected at least 250.");
        Assert.Equal(stored.Count, served.Count);

        // Rendered back into the storage form, so the comparison is against
        // what the store holds rather than against a parse of it.
        var round = served.Select(bar => string.Join(
            "|",
            bar.SessionDate.ToString("yyyy-MM-dd"),
            EquityBrief.Data.Money.ToStorage(bar.Open),
            EquityBrief.Data.Money.ToStorage(bar.High),
            EquityBrief.Data.Money.ToStorage(bar.Low),
            EquityBrief.Data.Money.ToStorage(bar.Close),
            bar.Volume));

        Assert.Equal(stored, round);
    }

    [Fact]
    public async Task TheApiServesOnlyTheNameAndRangeItIsAsked()
    {
        using var store = await Populated();
        var api = Api(store);

        var all = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var first = all[0].SessionDate;
        var window = await api.BarsAsync(Name, first, first.AddDays(30));

        Assert.NotEmpty(window);
        Assert.True(window.Count < all.Count, "A thirty-day window returned the whole year.");
        Assert.All(window, bar => Assert.Equal(Name, bar.Ticker));
        Assert.All(window, bar => Assert.True(bar.SessionDate <= first.AddDays(30)));

        // A name with no stored series is an empty answer and never an
        // invention, which is the failure a read surface that computes would
        // produce here.
        Assert.Empty(await api.BarsAsync("XRAY", DateOnly.MinValue, DateOnly.MaxValue));
    }

    [Fact]
    public async Task TheApiAppendsOneRunLogRowWhenTheSurfaceStarts()
    {
        // The W in its matrix row, exercised rather than only declared. The
        // grain is one row per run per stage, so the same run id twice is a
        // primary key violation and not a second row.
        using var store = await Populated();

        await Api(store).RecordStartAsync("read-api-1", "the read surface started");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM run_log WHERE stage = $stage;";
        command.Parameters.AddWithValue("$stage", ReadApi.Stage);

        Assert.Equal(1L, (long)command.ExecuteScalar()!);

        await Assert.ThrowsAsync<SqliteException>(
            () => Api(store).RecordStartAsync("read-api-1", "the same run again"));
    }

    [Fact]
    public async Task TheDrawnCandleCountIsTheStoredRowCount()
    {
        // The done condition's second clause. Counted off the rendered markup
        // rather than off the list handed to the renderer, because the claim is
        // about what the page draws and a renderer that dropped every third bar
        // would pass a count of its input.
        using var store = await Populated();

        var served = await Api(store).BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var svg = new MarkRenderer().LevelChart(Name, Bars(served));

        var candles = Regex.Matches(svg, "class=\"candle\"").Count;
        var volumes = Regex.Matches(svg, "class=\"volume\"").Count;

        Assert.Equal(StoredRows(store, Name).Count, candles);
        Assert.Equal(candles, volumes);
        Assert.True(candles >= 250, $"Drew {candles} candles, expected at least 250.");
    }

    [Fact]
    public async Task EverySessionIsDrawnAndNoneIsInvented()
    {
        // Stronger than the count, and it is the assertion that survives a
        // renderer drawing the right number of the wrong days.
        using var store = await Populated();

        var served = await Api(store).BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var svg = new MarkRenderer().LevelChart(Name, Bars(served));

        var drawn = Regex.Matches(svg, "<g class=\"candle\" data-session=\"([0-9-]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(served.Select(bar => bar.SessionDate.ToString("yyyy-MM-dd")), drawn);
    }

    // ---- the moving averages, the third of the level chart's four elements ----

    static async Task<TemporaryStore> WithIndicators()
    {
        var store = await Populated();

        await new IndicatorEngine(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("run-2");

        return store;
    }

    // The averages as the endpoint assembles them: aligned to the drawn bars by
    // session, so a line is one value per candle and a session the store has no
    // indicator for is a break rather than a shift.
    static ChartAverage[] Averages(IReadOnlyList<IndicatorRow> rows, ChartBar[] bars, params string[] names)
    {
        var bySession = rows
            .GroupBy(row => row.Name)
            .ToDictionary(group => group.Key, group => group.ToDictionary(row => row.SessionDate, row => row.Value));

        return [.. names
            .Where(bySession.ContainsKey)
            .Select(name => new ChartAverage(
                name,
                bars.Select(bar => bySession[name].GetValueOrDefault(bar.SessionDate)).ToArray()))];
    }

    [Fact]
    public async Task TheChartDrawsTheAveragesOnTheCandlesOwnScale()
    {
        // 3.1's done condition. Counted off the rendered markup rather than off
        // the values handed to the renderer, for the reason the candle count is:
        // a renderer that took the averages and drew nothing would pass a count
        // of its input.
        using var store = await WithIndicators();

        var api = Api(store);
        var served = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var bars = Bars(served);
        var rows = await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        var averages = Averages(rows, bars, "sma20", "sma50", "sma200");

        Assert.Equal(3, averages.Length);

        var svg = new MarkRenderer().LevelChart(Name, bars, averages);

        var groups = Regex.Matches(svg, "<g class=\"moving-average\" data-average=\"([a-z0-9_]+)\" data-values=\"([0-9]+)\"")
            .Select(match => (Name: match.Groups[1].Value, Values: int.Parse(match.Groups[2].Value)))
            .ToArray();

        Assert.Equal(["sma20", "sma50", "sma200"], groups.Select(group => group.Name));

        // Each line draws as many points as the store has values for it, and
        // the counts are the store's rather than a figure written here.
        foreach (var group in groups)
        {
            var stored = rows.Count(row => row.Name == group.Name && row.Value is not null);

            Assert.Equal(stored, group.Values);
            Assert.True(stored > 0, $"{group.Name} has no stored value, so the line would be vacuously right.");
        }

        // The long average is the one the done condition is about: it has fewer
        // drawn points than the short one, because it has no value until two
        // hundred bars sit behind the session.
        Assert.True(
            groups.Single(group => group.Name == "sma200").Values
                < groups.Single(group => group.Name == "sma20").Values,
            "The 200-day average drew as many points as the 20-day one, so its warm-up was not honoured.");

        // Drawn on the candles' own scale. Every path coordinate sits inside the
        // price pane, which is what a second scale would break.
        var points = Regex.Matches(svg, "<path d=\"([^\"]+)\"")
            .SelectMany(match => Regex.Matches(match.Groups[1].Value, @"[ML]([0-9.]+) ([0-9.]+)"))
            .Select(match => double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.NotEmpty(points);
        Assert.All(points, y => Assert.InRange(y, 0, 340));
    }

    [Fact]
    public async Task AnAverageIsBrokenWhereItHasNoValueRatherThanJoinedAcrossIt()
    {
        // A 200-day average has no value for the first 199 sessions of a stored
        // year. A single path from the first value it does have would claim the
        // average was flat over sessions it did not exist for, which is a
        // drawing of something that never happened.
        using var store = await WithIndicators();

        var api = Api(store);
        var bars = Bars(await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue));
        var rows = await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        var svg = new MarkRenderer().LevelChart(Name, bars, Averages(rows, bars, "sma200"));

        var group = Regex.Match(svg, "<g class=\"moving-average\"[^>]*>(.*?)</g>", RegexOptions.Singleline).Groups[1].Value;
        var paths = Regex.Matches(group, "<path ").Count;

        // One run of values, so one path, and the run starts where the warm-up
        // ends rather than at the first session.
        Assert.Equal(1, paths);

        var first = Regex.Match(group, @"d=""M([0-9.]+) ").Groups[1].Value;

        Assert.True(
            double.Parse(first, CultureInfo.InvariantCulture) > 700,
            $"The 200-day average starts at x={first}, which is near the left edge, so it was drawn from a session it has no value for.");
    }

    [Fact]
    public void AnAverageOfTheWrongLengthIsRefusedRatherThanDrawnAgainstTheWrongDates()
    {
        // The failure that would report green. A line one session short draws
        // every point one slot to the left and looks entirely plausible, so it
        // is refused at the door rather than checked by eye.
        var bars = new ChartBar[]
        {
            new(new DateOnly(2026, 9, 1), 10m, 12m, 9m, 11m, 100),
            new(new DateOnly(2026, 9, 2), 11m, 12m, 8m, 9m, 120),
        };

        var refusal = Assert.Throws<ArgumentException>(() =>
            new MarkRenderer().LevelChart("TEST", bars, [new ChartAverage("sma20", [10.5])]));

        Assert.Contains("1 values against 2 sessions", refusal.Message, StringComparison.Ordinal);

        // And the control: the right length draws.
        var svg = new MarkRenderer().LevelChart("TEST", bars, [new ChartAverage("sma20", [10.5, 10.0])]);

        Assert.Contains("class=\"moving-average\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void ACandleIsHollowWhenItRoseAndFilledWhenItFell()
    {
        // Neutral ink either way. Section 15.6 forbids the two hues here by
        // name, so the assertion is that neither appears rather than only that
        // the fill differs.
        var svg = new MarkRenderer().LevelChart("TEST",
        [
            new ChartBar(new DateOnly(2026, 9, 1), 10m, 12m, 9m, 11m, 100),
            new ChartBar(new DateOnly(2026, 9, 2), 11m, 12m, 8m, 9m, 120),
        ]);

        var bodies = Regex.Matches(svg, "<rect x=\"[^\"]+\" y=\"[^\"]+\" width=\"[^\"]+\" height=\"[^\"]+\" fill=\"([^\"]+)\"")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        Assert.Equal(2, bodies.Length);
        Assert.Equal("none", bodies[0]);
        Assert.Equal("var(--ink, #1c1c1c)", bodies[1]);

        Assert.DoesNotContain("green", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orange", svg, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AChartOverTooFewBarsStatesItsBarCountRatherThanDrawing()
    {
        // Section 15.5's degradation rule. A sparse series must never look like
        // a quiet one, so the mark says what it has.
        var renderer = new MarkRenderer();

        var none = renderer.LevelChart("TEST", []);
        var one = renderer.LevelChart("TEST", [new ChartBar(new DateOnly(2026, 9, 1), 10m, 12m, 9m, 11m, 100)]);

        Assert.DoesNotContain("<svg", none, StringComparison.Ordinal);
        Assert.Contains("0 stored sessions", none, StringComparison.Ordinal);
        Assert.Contains("1 stored session,", one, StringComparison.Ordinal);
        Assert.Contains($"at least {MarkRenderer.FewestBars}", one, StringComparison.Ordinal);
    }

    [Fact]
    public void AFlatSeriesDrawsRatherThanDividingByZero()
    {
        // A name that traded at one price for every session in the window is a
        // real series and the obvious scaling divides by its range.
        var svg = new MarkRenderer().LevelChart("TEST",
        [
            new ChartBar(new DateOnly(2026, 9, 1), 10m, 10m, 10m, 10m, 0),
            new ChartBar(new DateOnly(2026, 9, 2), 10m, 10m, 10m, 10m, 0),
        ]);

        Assert.Equal(2, Regex.Matches(svg, "class=\"candle\"").Count);
        Assert.DoesNotContain("NaN", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("Infinity", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellRoutesOnTheHashAndDrawsNothingItself()
    {
        // Section 15.4 puts the shell and the marks on the server. What the
        // page may do is ask for a mark; what it may not do is build one, so
        // the assertion is that no drawing element appears in the shell.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains(SinglePageApp.NameRoute, shell, StringComparison.Ordinal);

        // The region rather than one mark. 4.1 composes the name screen's chart
        // region on the server, because the profile is drawn against the chart's
        // own price axis and a second request would be a second axis.
        Assert.Contains("/screens/name/", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", shell, StringComparison.Ordinal);
        Assert.DoesNotContain("<rect", shell, StringComparison.Ordinal);
    }

    // ---- 3.3, the volume profile mark ----

    // Bands well inside the chart's own range, so the two scales are
    // distinguishable. A profile whose bands spanned the chart's whole range
    // would sit correctly under either rule and prove nothing.
    static ProfileBand[] Bands() =>
    [
        new(20m, 22m, 300, 0.3),
        new(22m, 24m, 500, 0.5),
        new(24m, 26m, 200, 0.2),
    ];

    static ChartBar[] Wide() =>
    [
        new(new DateOnly(2026, 9, 1), 12m, 40m, 10m, 30m, 100),
        new(new DateOnly(2026, 9, 2), 30m, 38m, 11m, 20m, 120),
    ];

    [Fact]
    public void TheVolumeProfileIsDrawnAgainstTheChartsPriceAxisAndNotItsOwn()
    {
        // Section 15.5 says the profile is drawn against the same price axis as
        // the chart beside it. That is a claim about two pictures agreeing, and
        // it is asserted three ways: the two marks declare the same axis, the
        // topmost band is not flush to the top of the pane where its own scale
        // would put it, and handing the mark a different axis moves it.
        var renderer = new MarkRenderer();
        var bars = Wide();
        var axis = renderer.AxisFor(bars);

        var chart = renderer.LevelChart("TEST", bars);
        var profile = renderer.VolumeProfile("TEST", Bands(), axis);

        var chartAxis = Regex.Match(chart, @"data-axis-low=""([^""]+)"" data-axis-high=""([^""]+)""");
        var profileAxis = Regex.Match(profile, @"data-axis-low=""([^""]+)"" data-axis-high=""([^""]+)""");

        Assert.True(chartAxis.Success && profileAxis.Success);
        Assert.Equal(chartAxis.Groups[1].Value, profileAxis.Groups[1].Value);
        Assert.Equal(chartAxis.Groups[2].Value, profileAxis.Groups[2].Value);

        // The chart's range is 10 to 40 and the bands run 20 to 26, so the top
        // band sits around the middle of the pane. Its own scale would put it at
        // the top margin, which is the picture this is written to refuse.
        var tops = Regex.Matches(profile, @"y=""([0-9.]+)""")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(3, tops.Length);
        Assert.True(tops.Min() > 100, $"The topmost band sits at y={tops.Min()}, which is where its own axis would put it.");

        // And the same bands against a different axis are drawn somewhere else,
        // so the axis is used rather than carried.
        var elsewhere = renderer.VolumeProfile("TEST", Bands(), new PriceAxis(19, 27));
        var moved = Regex.Matches(elsewhere, @"y=""([0-9.]+)""")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.NotEqual(tops, moved);
    }

    [Fact]
    public void EveryBandIsDrawnWithItsSharesAndItsShareOfThePeriod()
    {
        // The mark carries both numbers, because the count alone says nothing
        // without the period it is a share of, and the share is what section
        // 17's shelf threshold is read against.
        var renderer = new MarkRenderer();
        var profile = renderer.VolumeProfile("TEST", Bands(), renderer.AxisFor(Wide()));

        Assert.Equal(3, Regex.Matches(profile, "class=\"band\"").Count);
        Assert.Contains("data-shares=\"500\"", profile, StringComparison.Ordinal);
        Assert.Contains("data-share-of-period=\"0.5\"", profile, StringComparison.Ordinal);
        Assert.Contains("data-band-low=\"22\" data-band-high=\"24\"", profile, StringComparison.Ordinal);

        // Widths are relative to the busiest band rather than to the period, so
        // a name whose volume is evenly spread draws twenty full rows rather
        // than twenty stubs. The busiest is the full width less the margins.
        var widths = Regex.Matches(profile, @"width=""([0-9.]+)""")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(widths.Max(), widths[1]);
        Assert.True(widths[0] / widths[1] is > 0.59 and < 0.61, $"The 300 share band is {widths[0] / widths[1]:0.###} of the 500 share band.");

        // Neutral ink. The two hues belong to support and resistance, and a
        // volume band is a magnitude.
        // see: Support and resistance own two hues and nothing else uses them
        Assert.DoesNotContain("green", profile, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("orange", profile, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AProfileWithNoBandsSaysSoAndABandWithNoHeightIsRefused()
    {
        var renderer = new MarkRenderer();

        // The degradation, which is what a name with fewer than sixty sessions
        // gets. It states the case rather than drawing an empty box.
        var none = renderer.VolumeProfile("TEST", [], new PriceAxis(10, 40));

        Assert.DoesNotContain("<svg", none, StringComparison.Ordinal);
        Assert.Contains("no volume profile", none, StringComparison.Ordinal);

        // And a band that is not a band. A row of no height draws nothing and
        // takes its share of the period with it, which is a band silently
        // missing from a picture whose whole point is where the volume is.
        var refusal = Assert.Throws<ArgumentException>(() =>
            renderer.VolumeProfile("TEST", [new ProfileBand(22m, 22m, 100, 0.1)], new PriceAxis(10, 40)));

        Assert.Contains("which is not a band", refusal.Message, StringComparison.Ordinal);
    }

    // ---- 3.4, the level bands on the chart ----

    static ChartBand[] Shading() =>
    [
        new(20m, 22m, "support", false, 3),
        new(26m, 26m, "support", true, 9),
        new(34m, 36m, "resistance", true, 5),
    ];

    [Fact]
    public void TheBandsAreDrawnBehindTheCandlesAndNotOverThem()
    {
        // Section 15.5's fourth level chart element, and the reason the bands
        // are the point of the mark: a table of levels is a list of numbers, and
        // the same levels drawn behind the price show which ones the price has
        // respected.
        //
        // Behind is asserted by position in the markup, because SVG paints in
        // document order and there is nowhere else the answer lives. A band
        // drawn last covers the candles it is a statement about.
        var svg = new MarkRenderer().LevelChart("TEST", Wide(), null, Shading());

        var bands = svg.IndexOf("class=\"level-bands\"", StringComparison.Ordinal);
        var candles = svg.IndexOf("class=\"candle\"", StringComparison.Ordinal);
        var volume = svg.IndexOf("class=\"volume-pane\"", StringComparison.Ordinal);

        Assert.True(bands > 0 && candles > 0);
        Assert.True(bands < candles, "The level bands are drawn after the candles, so they cover the price.");
        Assert.True(bands < volume);

        Assert.Equal(3, Regex.Matches(svg, "class=\"level-band\"").Count);

        // Each band carries its role in words as well as in hue, so a reader who
        // cannot separate the two colours still reads it, and the drawn count is
        // stated in the description a reader who cannot see the picture gets.
        Assert.Contains("data-role=\"support\"", svg, StringComparison.Ordinal);
        Assert.Contains("data-role=\"resistance\"", svg, StringComparison.Ordinal);
        Assert.Contains("3 level band(s) shaded behind them", svg, StringComparison.Ordinal);
        Assert.Contains("data-strength=\"9\"", svg, StringComparison.Ordinal);

        // A band of one price is a real band and becomes a rule rather than a
        // rectangle of no height, which draws nothing.
        var heights = Regex.Matches(svg, "class=\"level-band\"[^/]*height=\"([0-9.]+)\"")
            .Select(match => double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(3, heights.Length);
        Assert.All(heights, height => Assert.True(height >= 1, $"A band was drawn {height} high, which is nothing."));
    }

    [Fact]
    public void SupportAndResistanceAreTheOnlyTwoHuesAndTheImmediateBandIsStronger()
    {
        // The one place in the system those hues appear.
        // see: Support and resistance own two hues and nothing else uses them
        var svg = new MarkRenderer().LevelChart("TEST", Wide(), null, Shading());

        var fills = Regex.Matches(svg, "class=\"level-band\" data-role=\"([a-z]+)\"[^/]*fill=\"([^\"]+)\" fill-opacity=\"([0-9.]+)\"")
            .Select(match => (
                Role: match.Groups[1].Value,
                Hue: match.Groups[2].Value,
                Opacity: double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture)))
            .ToArray();

        Assert.Equal(3, fills.Length);
        Assert.Equal(2, fills.Select(fill => fill.Hue).Distinct(StringComparer.Ordinal).Count());

        // owes: The band hue mapping asserted, and not only the two hues
        //
        // Which band got which hue, and not only that both hues appear. The test
        // asserted that three fills carry exactly two distinct hues and that one
        // contains the support token and one the resistance token, and never
        // that a support band got the support hue: swapping the two constants
        // drew every support band in the resistance hue and left the suite
        // green. Green is a level below the price and orange is one above it, so
        // the mapping is the claim rather than the palette.
        // see: Support and resistance own two hues and nothing else uses them
        Assert.All(fills, fill => Assert.Contains(
            "--" + fill.Role,
            fill.Hue,
            StringComparison.Ordinal));

        // Both roles are present, so the assertion above is over a population
        // that can fail rather than over three bands of one kind.
        Assert.Contains(fills, fill => fill.Role == "support");
        Assert.Contains(fills, fill => fill.Role == "resistance");

        // The immediate band on each side reads stronger, and it is a second
        // channel rather than a second hue.
        Assert.True(fills[1].Opacity > fills[0].Opacity, "The immediate band is not drawn stronger than the one beyond it.");

        // And a band given inverted edges is refused rather than drawn as a
        // rectangle of negative height, which renders as nothing at all.
        var refusal = Assert.Throws<ArgumentException>(() =>
            new MarkRenderer().LevelChart("TEST", Wide(), null, [new ChartBand(30m, 20m, "support", false, 1)]));

        Assert.Contains("which is inverted", refusal.Message, StringComparison.Ordinal);
    }

    // ---- 3.5, the momentum panel and the level summary table ----

    static async Task<TemporaryStore> WithLadders()
    {
        var store = await WithBands();

        await new LadderBuilder(FixedClock.At(Instant, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, "run-ladders");

        return store;
    }

    static async Task<TemporaryStore> WithBands()
    {
        var store = await WithIndicators();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new SwingFinder(clock, store.DatabaseFile).RunAsync("run-swings");
        await new VolumeProfileBuilder(clock, store.DatabaseFile).RunAsync("run-profile");
        await new LevelBuilder(clock, store.DatabaseFile).RunAsync("run-levels");

        return store;
    }

    [Fact]
    public async Task TheNameRegionDrawsEveryMarkSectionFifteenPutsInItAndTheTrendState()
    {
        // 4.1's visible output, and the repair for what 4.0 found: every mark
        // this region names has existed since 3.5 and the app served none of
        // them. The region is composed by the shipped composer rather than by
        // this test, because a test that assembled it would prove only that it
        // agrees with itself.
        using var store = await WithLadders();

        var api = Api(store);

        var region = NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            Name,
            await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(Name),
            await api.ProfileAsync(Name),
            await api.LadderAsync(Name),
            await api.NextEventAsync(Name, DateOnly.MinValue));

        // The four marks section 15.9 lists for this region, each named and each
        // asserted, rather than a count of svg elements which two of one kind
        // would satisfy.
        Assert.Contains("class=\"level-chart\"", region, StringComparison.Ordinal);
        Assert.Contains("class=\"volume-profile\"", region, StringComparison.Ordinal);
        Assert.Contains("class=\"momentum-panel\"", region, StringComparison.Ordinal);
        Assert.Contains("class=\"level-summary\"", region, StringComparison.Ordinal);

        // The bands are drawn, which is what the chart route did not do. Counted
        // against the store rather than asserted to be present, because one band
        // drawn out of five is a chart that looks right.
        var levels = await api.LevelsAsync(Name);

        Assert.Equal(
            levels.Count,
            Regex.Matches(region, "class=\"level-band\"").Count);

        // And the profile is drawn against the chart's own price axis rather
        // than one of its own, which is what composing the region in one place
        // buys. The two marks carry the axis they used, so the assertion is
        // against the values rather than against the arrangement.
        var chartAxis = Regex.Match(region, "class=\"level-chart\"[^>]*data-axis-low=\"([^\"]+)\" data-axis-high=\"([^\"]+)\"");
        var profileAxis = Regex.Match(region, "class=\"volume-profile\"[^>]*data-axis-low=\"([^\"]+)\" data-axis-high=\"([^\"]+)\"");

        Assert.True(chartAxis.Success, "the level chart does not carry the axis it drew against.");
        Assert.True(profileAxis.Success, "the volume profile does not carry the axis it drew against.");
        Assert.Equal(chartAxis.Groups[1].Value, profileAxis.Groups[1].Value);
        Assert.Equal(chartAxis.Groups[2].Value, profileAxis.Groups[2].Value);

        // The trend state, in a word, read off the ladder row the night wrote
        // and matched against the store rather than against a literal.
        var ladder = await api.LadderAsync(Name);

        Assert.NotNull(ladder);
        Assert.Contains($"data-trend-state=\"{ladder!.TrendState}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-as-of=\"{ladder.AsOf:yyyy-MM-dd}\"", region, StringComparison.Ordinal);

        // A name with no ladder row says so rather than drawing nothing, which
        // is the same rule the absent average follows.
        var missing = NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            "NOSUCH",
            await api.BarsAsync("NOSUCH", DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync("NOSUCH", DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync("NOSUCH"),
            await api.ProfileAsync("NOSUCH"),
            await api.LadderAsync("NOSUCH"),
            await api.NextEventAsync("NOSUCH", DateOnly.MinValue));

        Assert.Contains("data-trend-state=\"none\"", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheNightWritesTheTablesTheNameRegionDrawsFrom()
    {
        // The other half of 4.1, and the half 4.0 found missing. The three
        // components were called only from the suite, so every phase 3
        // assertion held over stores the tests built and a store the shipped
        // night wrote held no swing, no profile and no band.
        //
        // This runs the night's own entry point over the fixture and reads the
        // tables back, so what is asserted is what an evening produces rather
        // than what a test can assemble.
        using var store = new TemporaryStore().Migrated();

        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            FixtureFolder(),
            Index,

            // The session the captured bulk file carries, not the fixture's
            // as-of date. A night told a different session refuses the payload,
            // which is 2.3's wrong-session row doing its job.
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            output,
            error,
            "run-name-region");

        Assert.True(code == 0, $"the night exited {code}. {error} {output}");

        foreach (var table in new[] { "swing", "volume_profile", "level", "ladder" })
        {
            Assert.True(
                Count(store, table) > 0,
                $"a night wrote no rows to {table}, so the page draws it from nothing.");
        }

        // The ladder row count is the index size, which is this fixture's
        // current members, and it is read from the expectation rather than
        // written here.
        Assert.Equal(FixtureExpectation.CurrentMembers.Length, Count(store, "ladder"));
    }

    static long Count(TemporaryStore store, string table)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";

        return Convert.ToInt64(command.ExecuteScalar());
    }

    [Fact]
    public async Task TheLevelSurfaceServesTheLatestNightAloneWhenTwoAreStored()
    {
        // owes: The level read surface asserted over two stored as-of dates
        //
        // The query binds as_of to the maximum and the comment beside it says
        // why: a page holding two nights of bands is a page holding two maps.
        // Deleting that clause left the whole suite green, because the fixture
        // holds one as-of date per name and the two queries cannot differ over
        // it. The fault first appears on the second night rather than in any
        // fixture, which is what makes a constructed second night the only way
        // to assert it.
        //
        // The two-date store is also what 4.2's retention test is asserted
        // against, so it is built once here rather than twice.
        using var store = await WithBands();

        var api = Api(store);
        var tonight = await api.LevelsAsync(Name);

        Assert.NotEmpty(tonight);

        var asOf = tonight[0].AsOf;
        var yesterday = asOf.AddDays(-1);

        // Yesterday's night, at prices nothing tonight carries, so a row from it
        // is unmistakable in the answer.
        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = @"
                INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members)
                VALUES ($ticker, $as_of, '1.0000', '2.0000', 'support', 0, 1, 1, '[]');
            ";

            command.Parameters.AddWithValue("$ticker", Name);
            command.Parameters.AddWithValue("$as_of", yesterday.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await command.ExecuteNonQueryAsync();
        }

        // The store now holds two nights, which is the population that makes the
        // clause observable. Stated rather than assumed, because an insert that
        // silently did nothing would leave this asserting over one night again.
        var stored = new List<string>();

        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText = "SELECT DISTINCT as_of FROM level WHERE ticker = $ticker ORDER BY as_of;";
            command.Parameters.AddWithValue("$ticker", Name);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                stored.Add(reader.GetString(0));
            }
        }

        Assert.Equal(2, stored.Count);

        var served = await api.LevelsAsync(Name);

        Assert.Equal(tonight.Count, served.Count);
        Assert.All(served, row => Assert.Equal(asOf, row.AsOf));
        Assert.DoesNotContain(served, row => row.LowEdge == 1.0000m);
    }

    static MomentumReading[] Readings(IReadOnlyList<IndicatorRow> rows, int sessions) =>
    [
        .. IndicatorSeries.Momentum.Select(name =>
        {
            var bounds = IndicatorSeries.BoundsOf(name);

            return new MomentumReading(
                name,
                [.. rows.Where(row => row.Name == name).OrderBy(row => row.SessionDate).Select(row => row.Value)],
                IndicatorSeries.NeutralOf(name),
                bounds?.Floor,
                bounds?.Ceiling);
        }),
    ];

    [Fact]
    public async Task EveryMomentumReadingDrawsItsNeutralRule()
    {
        // 3.5's second done condition. A number like 53 means nothing without
        // the band it sits in, so the rule is the mark rather than decoration,
        // and it is counted off the rendered markup rather than off the input.
        using var store = await WithIndicators();

        var api = Api(store);
        var bars = await api.BarsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);
        var rows = await api.IndicatorsAsync(Name, DateOnly.MinValue, DateOnly.MaxValue);

        var svg = new MarkRenderer().MomentumPanel(Name, Readings(rows, bars.Count));

        var groups = Regex.Matches(svg, "<g class=\"reading\" data-name=\"([^\"]+)\" data-neutral=\"([^\"]+)\"")
            .Select(match => (Name: match.Groups[1].Value, Neutral: match.Groups[2].Value))
            .ToArray();

        // owes: The momentum panel's reading set asserted independently of the constant it is drawn from
        //
        // The four are named here rather than read back out of the constant the
        // panel is drawn from. Asserted against `IndicatorSeries.Momentum` alone,
        // both sides of the comparison were the same value: dropping macd_hist
        // drew three readings and left the suite green, while adding atr14
        // turned it red only because NeutralOf throws on a reading with no
        // neutral rule. The test was sensitive to the set through an exception
        // and never through membership, which is the asymmetry that identified
        // it as a tautology in the phase 3 sign-off's sweep.
        //
        // The document enumerates the four nowhere, so a literal here is the
        // independent statement rather than a second copy of one.
        string[] theFourReadings = ["rsi14", "macd", "macd_signal", "macd_hist"];

        Assert.Equal(theFourReadings, groups.Select(group => group.Name).ToArray());

        // And the constant agrees with the literal, so the two are reconciled
        // once rather than the panel being free to drift from what is drawn.
        Assert.Equal(theFourReadings, IndicatorSeries.Momentum.ToArray());
        Assert.Equal(IndicatorSeries.Momentum.Count, groups.Length);

        // One rule per reading, and its value is the one the arithmetic states
        // rather than one the mark chose.
        Assert.Equal(groups.Length, Regex.Matches(svg, "class=\"neutral-rule\"").Count);

        foreach (var group in groups)
        {
            Assert.Equal(
                IndicatorSeries.NeutralOf(group.Name).ToString("0.##", CultureInfo.InvariantCulture),
                group.Neutral);
        }

        // The RSI rule sits at 50 on an axis running 0 to 100, so it is halfway
        // down its own pane whatever the stock did. That is what a fixed range
        // buys and it is asserted rather than assumed: a reading scaled to its
        // own values would put the rule wherever the week happened to end.
        var rsi = Regex.Match(svg, "data-name=\"rsi14\".*?class=\"neutral-rule\" x1=\"[0-9]+\" y1=\"([0-9.]+)\"", RegexOptions.Singleline);

        Assert.True(rsi.Success);
        Assert.Equal(32, double.Parse(rsi.Groups[1].Value, CultureInfo.InvariantCulture), 1);
    }

    [Fact]
    public void AReadingHasNoNeutralRuleUnlessItIsAMomentumReading()
    {
        // The counter-test. An average is a price and a volume ratio is a
        // quantity, and neither is read against a rule, so asking for one is
        // refused rather than answered with a zero that would draw a line
        // through the middle of a price.
        Assert.Equal(50, IndicatorSeries.NeutralOf(IndicatorSeries.Rsi14));
        Assert.Equal(0, IndicatorSeries.NeutralOf(IndicatorSeries.MacdHist));

        Assert.Throws<ArgumentOutOfRangeException>(() => IndicatorSeries.NeutralOf(IndicatorSeries.Sma200));
        Assert.Throws<ArgumentOutOfRangeException>(() => IndicatorSeries.NeutralOf(IndicatorSeries.VolAvg20));

        Assert.Equal((0d, 100d), IndicatorSeries.BoundsOf(IndicatorSeries.Rsi14));
        Assert.Null(IndicatorSeries.BoundsOf(IndicatorSeries.Macd));

        // And a panel with nothing to draw states that rather than drawing an
        // empty box.
        var none = new MarkRenderer().MomentumPanel("TEST", []);

        Assert.DoesNotContain("<svg", none, StringComparison.Ordinal);
        Assert.Contains("no momentum readings", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryBandInTheTableNamesItsMembersWithTheirDates()
    {
        // 3.5's first done condition, read off the rendered table rather than
        // off the rows handed to it, because the claim is about what a person
        // sees and a table that dropped every member would pass a count of its
        // input.
        using var store = await WithBands();

        var bands = await Api(store).LevelsAsync(Name);

        Assert.NotEmpty(bands);

        var summary = new MarkRenderer().LevelSummary(Name, Summarised(bands));

        Assert.Equal(bands.Count, Regex.Matches(summary, "class=\"band\"").Count);

        var members = Regex.Matches(summary, "class=\"member\" data-kind=\"([^\"]+)\" data-date=\"([^\"]+)\"")
            .Select(match => (Kind: match.Groups[1].Value, Date: match.Groups[2].Value))
            .ToArray();

        // Every member of every band, and every one of them dated. The stored
        // count is read from the JSON the level builder wrote, so this compares
        // the surface against the store rather than against itself.
        var stored = bands.Sum(band => JsonDocument.Parse(band.Members).RootElement.GetArrayLength());

        Assert.Equal(stored, members.Length);
        Assert.True(members.Length >= 20, $"The table drew {members.Length} members, expected at least 20.");
        Assert.All(members, member => Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", member.Date));
        Assert.All(members, member => Assert.NotEmpty(member.Kind));

        // The immediate band on each side says so in words, because a row a
        // reader scans should carry its own meaning.
        Assert.Contains("support, immediate", summary, StringComparison.Ordinal);
        Assert.Contains("resistance, immediate", summary, StringComparison.Ordinal);

        // A band of one price reads as one price rather than as a range from a
        // number to itself.
        var single = Summarised(bands).FirstOrDefault(band => band.LowEdge == band.HighEdge);

        if (single is not null)
        {
            Assert.DoesNotContain($"{single.LowEdge.ToString(CultureInfo.InvariantCulture)} to {single.LowEdge.ToString(CultureInfo.InvariantCulture)}", summary, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AnAverageWithNoValueSaysSoWithTheBarCountThatExplainsIt()
    {
        // Section 18's row, on the surface a person reads it on. The stored half
        // passes at 3.1 by fixture-expectations; this is the other element, and
        // it is here because an average with no value cannot be a band member,
        // so the table would otherwise not mention it at all.
        var bands = new SummaryBand[]
        {
            new(100m, 102m, "support", true, 4, true,
                [new SummaryMember("swing low", 100m, new DateOnly(2026, 8, 3))]),
        };

        var summary = new MarkRenderer().LevelSummary(
            "TEST",
            bands,
            [new AbsentAverage(IndicatorSeries.Sma200, 60)]);

        Assert.Contains("not available, 60 bars", summary, StringComparison.Ordinal);
        Assert.Contains("data-name=\"sma200\"", summary, StringComparison.Ordinal);
        Assert.Contains("data-bar-count=\"60\"", summary, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(summary, "class=\"absent-average\""));

        // And with nothing absent there is no footer at all, so the row appears
        // when it is true and not as a permanent caveat.
        var complete = new MarkRenderer().LevelSummary("TEST", bands);

        Assert.DoesNotContain("not available", complete, StringComparison.Ordinal);
        Assert.DoesNotContain("<tfoot", complete, StringComparison.Ordinal);

        // A name with no bands says so rather than drawing an empty table.
        var none = new MarkRenderer().LevelSummary("TEST", []);

        Assert.DoesNotContain("<table", none, StringComparison.Ordinal);
        Assert.Contains("no level bands stored", none, StringComparison.Ordinal);
    }

    static SummaryBand[] Summarised(IReadOnlyList<LevelRow> bands) =>
    [
        .. bands.Select(band => new SummaryBand(
            band.LowEdge,
            band.HighEdge,
            band.Role,
            band.Immediate,
            band.Strength,
            band.HasNonAverageAnchor,
            [
                .. JsonDocument.Parse(band.Members).RootElement.EnumerateArray().Select(member => new SummaryMember(
                    member.GetProperty("kind").GetString()!,
                    decimal.Parse(member.GetProperty("price").GetString()!, CultureInfo.InvariantCulture),
                    DateOnly.ParseExact(member.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))),
            ])),
    ];

    static ChartBar[] Bars(IReadOnlyList<BarRow> served) =>
        [.. served.Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))];
}
