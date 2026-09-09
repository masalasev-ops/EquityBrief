using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Membership;
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
        Assert.Contains("/marks/level-chart/", shell, StringComparison.Ordinal);
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

    static ChartBar[] Bars(IReadOnlyList<BarRow> served) =>
        [.. served.Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))];
}
