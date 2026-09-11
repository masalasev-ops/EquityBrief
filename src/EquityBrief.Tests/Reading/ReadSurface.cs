using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Moves;
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
            // 5.6, the run page. Every one of these is a claim about a surface,
            // which is why they are reached by a check that draws the surface
            // and reads it back rather than by a declaration.
            CheckReach.Key(Scope.LimitsTable, "Base rate"),
            CheckReach.Key("15.5 The mark vocabulary", "Reason track"),
            CheckReach.Key("15.7 Tonight", "Reasons, per row"),
            CheckReach.Key("15.7 Tonight", "Reason totals"),
            CheckReach.Key("15.10 Run", "Operational header"),
            CheckReach.Key("15.10 Run", "Reason records, the resolved count"),
            CheckReach.Key("15.10 Run", "Stale and failed"),
            CheckReach.Key("15.10 Run", "Harness"),

            // The run page's own expectation file, which 5.6 added and section
            // 19.1 did not name until the phase 5 sign-off. It is reached here
            // rather than by `fixture-expectations` because what it holds is the
            // counts a record is drawn from, computed across every night the
            // store holds, and the check that reads it is the one that draws the
            // page.
            CheckReach.Key(Scope.FixtureTable, "run page"),

            // 5.4, tonight's list.
            CheckReach.Key(Scope.LimitsTable, "List display"),
            CheckReach.Key("15.5 The mark vocabulary", "Listing strip"),
            CheckReach.Key("15.7 Tonight", "Night header"),
            CheckReach.Key("15.7 Tonight", "Watch list"),
            CheckReach.Key("15.7 Tonight", "The list"),
            CheckReach.Key("15.7 Tonight", "Selected name"),
            CheckReach.Key("15.8 Universe", "Sector strip, how many are on tonight's list"),
            CheckReach.Key("15.8 Universe", "The table, the listing strip over sixty sessions"),
            CheckReach.Key("15.9 Name", "Why it is here"),
            CheckReach.Key("15.9 Name", "Walk"),
            CheckReach.Key(Scope.FailureTable, "Bulk price feed unavailable, banner"),

            // 5.2, the move annotator.
            CheckReach.Key("15.9 Name", "How it got here, the table of the biggest moves"),

            // The universe screen, 5.1.
            CheckReach.Key("15.5 The mark vocabulary", "Distance row"),
            CheckReach.Key("15.8 Universe", "Sector strip, one line per sector"),
            CheckReach.Key("15.8 Universe", "The table, every name in the index"),
            CheckReach.Key("15.8 Universe", "Filters"),
            CheckReach.Key(Scope.FailureTable, "A name leaves the index"),

            CheckReach.Key("15.4 The two surfaces", "The app"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, candles"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, a volume pane"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, the moving averages"),
            CheckReach.Key("15.5 The mark vocabulary", "Volume profile"),
            CheckReach.Key("15.5 The mark vocabulary", "Level chart, the level bands"),
            CheckReach.Key("15.5 The mark vocabulary", "Momentum panel"),
            CheckReach.Key(Scope.FailureTable, "Fewer than 200 bars for a new index member, nn bars"),
            CheckReach.Key("15.9 Name", "The chart"),
            CheckReach.Key("15.9 Name", "The plan"),
            CheckReach.Key("15.5 The mark vocabulary", "Plan column"),
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
            bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
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

        Assert.Equal(served.Select(bar => bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), drawn);
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
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        // The calendar first, because the second book is keyed to a dated event
        // and a ladder built before it would carry none. The night runs them in
        // that order for the same reason.
        await new CalendarFetcher(
            RecordedEarningsCalendarFeed.FromFolder(FixtureFolder()),
            clock,
            store.DatabaseFile).RunAsync(Index, new DateOnly(2026, 9, 8), "run-calendar");

        await new LadderBuilder(clock, store.DatabaseFile).RunAsync(Index, "run-ladders");
        await new MoveAnnotator(clock, store.DatabaseFile).RunAsync("run-moves");

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
            await api.NextEventAsync(Name, DateOnly.MinValue),
            await api.MovesAsync(Name));

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
            await api.NextEventAsync("NOSUCH", DateOnly.MinValue),
            await api.MovesAsync("NOSUCH"));

        Assert.Contains("data-trend-state=\"none\"", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanColumnDrawsNoValueTheLadderDoesNotCarry()
    {
        // The containment property applied to pictures, which is 4.6's done
        // condition. Read off the mark's own data attributes and matched against
        // the stored plan rather than by eye, so a figure the drawing invented
        // fails rather than looking plausible.
        using var store = await WithLadders();

        var api = Api(store);
        var ladder = await api.LadderAsync(Name);

        Assert.NotNull(ladder);

        var rows = NameScreen.PlanRows(ladder);
        var svg = new MarkRenderer().PlanColumn(Name, 319.97m, rows);

        // Every price the figure draws, taken off the markup.
        var drawn = Regex.Matches(svg, "data-low-edge=\"([^\"]+)\" data-high-edge=\"([^\"]+)\"")
            .SelectMany(match => new[] { match.Groups[1].Value, match.Groups[2].Value })
            .Distinct()
            .OrderBy(price => price, StringComparer.Ordinal)
            .ToArray();

        // Every price the stored plan carries.
        using var plan = JsonDocument.Parse(ladder!.Plan);
        var stored = new List<string>();

        foreach (var tranche in plan.RootElement.GetProperty("tranches").EnumerateArray())
        {
            stored.Add(tranche.GetProperty("lowEdge").GetString()!);
            stored.Add(tranche.GetProperty("highEdge").GetString()!);

            if (tranche.GetProperty("stop").GetString() is { } stop)
            {
                stored.Add(stop);
            }
        }

        foreach (var exit in plan.RootElement.GetProperty("exits").EnumerateArray())
        {
            stored.Add(exit.GetProperty("lowEdge").GetString()!);
            stored.Add(exit.GetProperty("highEdge").GetString()!);
        }

        if (plan.RootElement.GetProperty("invalidation").GetString() is { } invalidation)
        {
            stored.Add(invalidation);
        }

        // Both directions. Nothing drawn that is not stored, which is the
        // containment property, and nothing stored that is not drawn, which is
        // the half a figure that quietly omitted a stop would pass.
        Assert.Equal(stored.Distinct().OrderBy(price => price, StringComparer.Ordinal), drawn);

        // The population carrying the property, stated: a plan with no rows
        // would satisfy the comparison above.
        Assert.True(rows.Count >= 5, $"the plan draws {rows.Count} rows, expected at least 5.");

        // The invalidation is the lowest rule of all, which is what the mark
        // exists to make legible.
        var lowest = rows.Min(row => row.LowEdge);

        Assert.Equal(lowest, Assert.Single(rows, row => row.Kind == PlanKind.Invalidation).LowEdge);

        // And it is one rule rather than two at one price. The invalidation is
        // the lowest stop, so drawing both would put two horizontal rules on the
        // same line and say two things where the figure says one.
        Assert.DoesNotContain(rows, row => row.Kind == PlanKind.Stop && row.LowEdge == lowest);

        // Everything above the marker is a sale and everything below is a
        // purchase, asserted against the close rather than against the drawing
        // order.
        Assert.All(
            rows.Where(row => row.Kind == PlanKind.Tranche),
            row => Assert.True(row.LowEdge < 319.97m, $"a tranche at {row.LowEdge} sits above the price."));

        Assert.All(
            rows.Where(row => row.Kind == PlanKind.Exit),
            row => Assert.True(row.LowEdge > 319.97m, $"an exit at {row.LowEdge} sits below the price."));

        // And a name with no plan says so rather than drawing an empty column.
        Assert.Contains(
            "has no plan to draw",
            new MarkRenderer().PlanColumn("NOSUCH", 10m, []),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanTablesCarryEveryTrancheAndEveryExitWithItsAction()
    {
        // The two tables the figure is read beside. A skipped exit is a row with
        // its reason rather than an absence, which is the rule the ladder
        // already applies and the table has to carry through.
        using var store = await WithLadders();

        var api = Api(store);

        foreach (var name in new[] { "AAPL", "MSFT", "NFLX" })
        {
            var ladder = await api.LadderAsync(name);
            var rows = NameScreen.PlanRows(ladder);
            var tables = new MarkRenderer().PlanTables(name, rows);

            using var plan = JsonDocument.Parse(ladder!.Plan);

            var tranches = plan.RootElement.GetProperty("tranches").GetArrayLength();
            var exits = plan.RootElement.GetProperty("exits").GetArrayLength();

            Assert.Contains($"class=\"tranche-table\" data-ticker=\"{name}\" data-rows=\"{tranches}\"", tables, StringComparison.Ordinal);
            Assert.Contains($"class=\"exit-table\" data-ticker=\"{name}\" data-rows=\"{exits}\"", tables, StringComparison.Ordinal);

            // Every tranche row names its condition in words and its stop, and
            // every exit row says what to do there.
            Assert.Equal(tranches, Regex.Matches(tables, "<tr data-low-edge=\"[^\"]+\"><td>").Count);
            Assert.Equal(exits, Regex.Matches(tables, "data-traded=\"(true|false)\"").Count);
        }

        // MSFT's only exit is the skipped one, so its exit table is a row saying
        // why rather than an empty table.
        var msft = NameScreen.PlanRows(await api.LadderAsync("MSFT"));
        var skipped = new MarkRenderer().PlanTables("MSFT", msft);

        Assert.Contains("data-traded=\"false\"", skipped, StringComparison.Ordinal);
        Assert.Contains("typical days", skipped, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheInvalidationRelabelsTheStopSittingAtItAndAddsARowWhereNoneDoes()
    {
        // The other half of the invariant 5.0 wrote down. The stops a plan
        // carries are strictly decreasing, which `fixture-expectations` asserts
        // over the arithmetic, so at most one stop row can sit at the
        // invalidation price. This is what that buys the projection: the lowest
        // stop is relabelled in place rather than a second rule being drawn on
        // top of it, which is what section 15.5 says the figure shows.
        //
        // Both branches are asserted, because the second is the one the
        // committed fixture cannot reach: every tranche in it has a stop.
        using var store = await WithLadders();

        var api = Api(store);

        foreach (var name in new[] { "AAPL", "MSFT", "NFLX", "KEYS" })
        {
            var ladder = await api.LadderAsync(name);
            var rows = NameScreen.PlanRows(ladder);

            using var plan = JsonDocument.Parse(ladder!.Plan);

            if (plan.RootElement.GetProperty("invalidation").GetString() is not { } price)
            {
                continue;
            }

            var at = decimal.Parse(price, CultureInfo.InvariantCulture);

            // Exactly one row carries the invalidation, and it is the stop that
            // was already there rather than a row beside it.
            var marked = Assert.Single(rows, row => row.Kind == PlanKind.Invalidation);

            Assert.Equal(at, marked.LowEdge);
            Assert.Contains("stop for the", marked.Detail, StringComparison.Ordinal);
            Assert.Contains("the whole position is wrong below this", marked.Detail, StringComparison.Ordinal);

            // And no stop row is left at that price, so the relabel moved the
            // one that was there rather than adding a second.
            Assert.DoesNotContain(rows, row => row.Kind == PlanKind.Stop && row.LowEdge == at);
        }

        // The branch the fixture holds no case for: a lowest tranche with no
        // band beneath it invalidates at its own low edge, where no stop row
        // sits, so the invalidation is a row of its own.
        var alone = new LadderRow(
            "ZZZZ",
            new DateOnly(2026, 9, 8),
            "range",
            """
            {"tranches":[{"lowEdge":"90.0000","highEdge":"92.0000","condition":"AvailableNow","stop":null}],
             "exits":[],"invalidation":"90.0000","events":[]}
            """);

        var drawn = NameScreen.PlanRows(alone);

        Assert.DoesNotContain(drawn, row => row.Kind == PlanKind.Stop);

        var added = Assert.Single(drawn, row => row.Kind == PlanKind.Invalidation);

        Assert.Equal(90m, added.LowEdge);
        Assert.Equal("the whole position is wrong below this", added.Detail);
        Assert.Contains("no stop beneath", Assert.Single(drawn, row => row.Kind == PlanKind.Tranche).Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheEventBookSaysItsFiguresAreProposals()
    {
        // A figure on a screen gets acted on, and one that looks measured and is
        // not is the failure this states its way out of. The page says so once
        // for the book and once on every row, so a reader who reads one setup
        // still reads it.
        // see: The event setups' triggers are proposals until resolved setups can score them
        using var store = await WithLadders();

        var api = Api(store);
        var written = NameScreen.EventBook(await api.LadderAsync(Name));

        Assert.Contains("data-proposal=\"true\"", written, StringComparison.Ordinal);
        Assert.Contains("is a proposal and none has been tested", written, StringComparison.Ordinal);
        Assert.Contains("run page", written, StringComparison.Ordinal);

        // One row per setup, each naming its trigger, entry, stop and target.
        var ladder = await api.LadderAsync(Name);
        using var plan = JsonDocument.Parse(ladder!.Plan);
        var setups = plan.RootElement.GetProperty("events").GetArrayLength();

        Assert.Equal(setups, Regex.Matches(written, "data-setup=\"[^\"]+\"").Count);
        Assert.Contains($"data-setups=\"{setups}\"", written, StringComparison.Ordinal);

        // And a name with no dated event says so rather than showing an empty
        // table, which is the same rule the plan column follows.
        var absent = NameScreen.EventBook(await api.LadderAsync("KEYS"));

        Assert.Contains("no dated event is on file", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("data-setup=", absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePlanSectionStatesItsArithmeticAndTheEarningsRule()
    {
        // The figures a reader acts on, drawn from the ladder row rather than
        // computed on the page: two implementations of one arithmetic disagree
        // eventually, and the report and the score would be the two.
        using var store = await WithLadders();

        var api = Api(store);
        var written = NameScreen.Arithmetic(await api.LadderAsync(Name));

        using var plan = JsonDocument.Parse((await api.LadderAsync(Name))!.Plan);
        var figures = plan.RootElement.GetProperty("arithmetic");

        // Every figure on the page is the figure the row carries, matched rather
        // than merely present.
        foreach (var key in new[] { "firstEntry", "firstRisk", "firstReward", "firstRewardToRisk", "breakEven" })
        {
            Assert.Contains(figures.GetProperty(key).GetString()!, written, StringComparison.Ordinal);
        }

        Assert.Contains("data-from=\"first\"", written, StringComparison.Ordinal);
        Assert.Contains("data-from=\"blended\"", written, StringComparison.Ordinal);

        // The break-even is stated as what this plan demands of itself rather
        // than as a benchmark, and the sizing paragraph says the sizing is the
        // reader's.
        Assert.Contains("worth taking if its first tranche", written, StringComparison.Ordinal);
        Assert.Contains("never sizes it", written, StringComparison.Ordinal);

        // The earnings rule, one row per print, with the session the timing
        // decided and the move against the stop.
        var prints = plan.RootElement.GetProperty("earningsRule").GetArrayLength();

        Assert.Equal(prints, Regex.Matches(written, "data-print=\"[^\"]+\"").Count);

        // A name whose plan has no reward to measure says so rather than showing
        // an empty table, and still shows its prints.
        var msft = NameScreen.Arithmetic(await api.LadderAsync("MSFT"));

        Assert.Contains("data-absent=\"true\"", msft, StringComparison.Ordinal);
        Assert.Contains("no exit is traded", msft, StringComparison.Ordinal);
        Assert.Contains("data-print=", msft, StringComparison.Ordinal);
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

    [Fact]
    public async Task TheHowItGotHereTableDrawsTheMovesAndStatesThatTheCauseIsAbsent()
    {
        // Section 15.9's second region, read off the markup rather than by eye.
        // Its cause column arrives at 6.5 with the pass that writes a cause, and
        // until then it is absent and said so once rather than drawn as an empty
        // cell in every row: an absence stated and an absence drawn as emptiness
        // are different things.
        using var store = await WithLadders();

        var api = Api(store);
        var moves = await api.MovesAsync(Name);
        var marks = new MarkRenderer();
        var table = marks.MovesTable(Name, [.. moves.Select(move => new MoveCell(move.SessionDate, move.Sessions, move.ChangePct, move.Rank))]);

        Assert.NotEmpty(moves);
        Assert.Contains($"data-moves=\"{moves.Count}\"", table, StringComparison.Ordinal);
        Assert.Contains("data-cause-column=\"absent\"", table, StringComparison.Ordinal);
        Assert.Contains("data-cause=\"absent\"", table, StringComparison.Ordinal);

        // No cell holds a cause, and no row carries an empty one. The second is
        // the half a blank column would satisfy.
        Assert.DoesNotContain("data-cause=\"\"", table, StringComparison.Ordinal);
        Assert.Equal(moves.Count, Regex.Matches(table, "<tr data-session-date=\"[^\"]+\"").Count);

        // Every value drawn is one the store carries, which is the containment
        // property applied to a table.
        foreach (var move in moves)
        {
            Assert.Contains($"data-session-date=\"{move.SessionDate:yyyy-MM-dd}\"", table, StringComparison.Ordinal);
            Assert.Contains($"data-rank=\"{move.Rank}\"", table, StringComparison.Ordinal);
        }

        // A five-day run reads as one rather than as a day that moved that far,
        // which is what the span column is for.
        Assert.Contains("sessions</td>", table, StringComparison.Ordinal);

        // And a name with no stored moves says so rather than drawing an empty
        // table, which a reader would read as a name that never moved.
        Assert.Contains("no moves are stored", marks.MovesTable("NOSUCH", []), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WhyItIsHereGivesEachReasonASentenceAndIsAbsentForANameNotOnTheList()
    {
        // Section 15.9's region, present only when the name is on tonight's
        // list. Each reason in a full sentence rather than a label, because a
        // label is what the list row shows and this is the page a reader acts
        // from.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var listed = listings.First(listing => listing.FiredCount > 0);

        var marks = new MarkRenderer();

        var region = NameScreen.Region(
            new SinglePageApp(),
            marks,
            listed.Ticker,
            await api.BarsAsync(listed.Ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(listed.Ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(listed.Ticker),
            await api.ProfileAsync(listed.Ticker),
            await api.LadderAsync(listed.Ticker),
            await api.NextEventAsync(listed.Ticker, DateOnly.MinValue),
            await api.MovesAsync(listed.Ticker),
            listed,
            "PREV",
            "NEXT");

        Assert.Contains($"data-reasons=\"{listed.FiredCount}\"", region, StringComparison.Ordinal);

        // Every reason that fired is a sentence rather than its stored label,
        // and the values that made it true are beside it.
        using var reasons = JsonDocument.Parse(listed.Reasons);

        foreach (var reason in reasons.RootElement.EnumerateArray().Where(reason => reason.GetProperty("fired").GetBoolean()))
        {
            var name = reason.GetProperty("name").GetString()!;

            Assert.Contains($"data-reason=\"{name}\"", region, StringComparison.Ordinal);
            Assert.Contains("data-values=\"", region, StringComparison.Ordinal);
        }

        // A sentence rather than a label: the region carries prose the stored
        // name does not contain.
        Assert.Contains("so the plan's first step is available", region, StringComparison.Ordinal);

        // A reason the mapping has no sentence for fails rather than rendering a
        // default, which is the same rule the plan column's mapping now follows.
        Assert.Throws<InvalidOperationException>(
            () => marks.WhyItIsHere("ZZZZ", [new FiredReason("a seventh reason", new Dictionary<string, string>())]));

        // And a name that fired nothing says it is not on tonight's list rather
        // than drawing an empty region.
        Assert.Contains("not on tonight's list", marks.WhyItIsHere("ZZZZ", []), StringComparison.Ordinal);
    }

    [Fact]
    public void TheWalkLinksBothNeighboursAndSaysSoAtEitherEnd()
    {
        // Section 15.9's last region, so an evening's reading is one pass
        // through with no return to the list.
        var marks = new MarkRenderer();
        var middle = marks.Walk("BBBB", "AAAA", "CCCC");

        Assert.Contains("data-previous=\"AAAA\"", middle, StringComparison.Ordinal);
        Assert.Contains("data-next=\"CCCC\"", middle, StringComparison.Ordinal);
        Assert.Contains("href=\"#/name/AAAA\"", middle, StringComparison.Ordinal);
        Assert.Contains("href=\"#/name/CCCC\"", middle, StringComparison.Ordinal);

        // Either end says so rather than linking to the other end of a list, and
        // both ends are asserted rather than one: a walk that wrapped would
        // satisfy an assertion over the first alone.
        var first = marks.Walk("AAAA", null, "BBBB");
        var last = marks.Walk("CCCC", "BBBB", null);

        Assert.Contains("no previous name", first, StringComparison.Ordinal);
        Assert.DoesNotContain("no next name", first, StringComparison.Ordinal);
        Assert.Contains("no next name", last, StringComparison.Ordinal);
        Assert.DoesNotContain("no previous name", last, StringComparison.Ordinal);

        // A name that is not on the list has neither neighbour.
        var alone = marks.Walk("ZZZZ", null, null);

        Assert.Contains("data-previous=\"none\"", alone, StringComparison.Ordinal);
        Assert.Contains("data-next=\"none\"", alone, StringComparison.Ordinal);
    }

    [Fact]
    public void ANightWithNoListShowsTheDataDateRatherThanAListBuiltFromOlderBars()
    {
        // Section 18's banner half. The bulk price feed not answering keeps last
        // night's bars, and what a reader must not be shown is tonight's list
        // built from them.
        var page = new SinglePageApp();
        var banner = page.StaleBanner(new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 8));

        Assert.Contains("data-list=\"absent\"", banner, StringComparison.Ordinal);
        Assert.Contains("data-data-date=\"2026-09-08\"", banner, StringComparison.Ordinal);
        Assert.Contains("2026-09-08", banner, StringComparison.Ordinal);
        Assert.Contains("absent rather than wrong", banner, StringComparison.Ordinal);

        // No list is drawn at all, which is the half a banner above a stale list
        // would not satisfy.
        Assert.DoesNotContain("list-table", banner, StringComparison.Ordinal);
        Assert.DoesNotContain("data-fired=", banner, StringComparison.Ordinal);

        // A store with no night at all says that rather than naming a date it
        // does not have.
        var empty = page.StaleBanner(new DateOnly(2026, 9, 9), null);

        Assert.Contains("data-data-date=\"none\"", empty, StringComparison.Ordinal);
        Assert.Contains("no night at all", empty, StringComparison.Ordinal);
    }

    // ---- 5.4, tonight's list ----

    [Fact]
    public async Task EveryPlanSentenceIsAssertedAndAnUnknownConditionFailsRatherThanRendering()
    {
        // The eleventh obligation the phase 4 sign-off created. `NameScreen`'s
        // condition-to-words mapping ended in a catch-all arm, so a sixth
        // condition, a typo or an unset value rendered as the sentence for
        // reaching the zone with nothing failing, and no test in the suite
        // asserted any plan sentence at all.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);

        // Every condition the enum carries has its own sentence, asserted one at
        // a time rather than as a set: a mapping with two arms swapped produces
        // the same set of sentences over the same plans.
        foreach (var (condition, words) in new[]
        {
            (TrancheCondition.AvailableNow, "this price now"),
            (TrancheCondition.FailedBreakdown, "a failed breakdown back into the zone"),
            (TrancheCondition.FirstCloseBackAbove, "the first close back above the zone after a dip"),
            (TrancheCondition.SecondDayAfterAShock, "the second day after a shock, once the first day's low has held"),
            (TrancheCondition.ReachesTheZone, "the price reaching the zone"),
        })
        {
            var row = NameScreen.PlanRows(Ladder(Plan(condition))).First(row => row.Kind == PlanKind.Tranche);

            Assert.Contains($"buy on {words}", row.Detail, StringComparison.Ordinal);
        }

        // And a value the mapping has no words for fails rather than rendering a
        // default. A sentence a reader acts on that was produced by a value
        // nobody wrote is what a catch-all makes invisible.
        var unknown = Assert.Throws<InvalidOperationException>(
            () => NameScreen.PlanRows(Ladder(Plan("ASixthCondition"))));

        Assert.Contains("has no words for", unknown.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => NameScreen.PlanRows(Ladder(Plan(string.Empty))));

        // The sentences reach the surface a person reads them on, which is what
        // makes this a claim about a page rather than about a function.
        var drawn = new MarkRenderer().PlanTables(Name, NameScreen.PlanRows(await api.LadderAsync(Name)));

        Assert.Contains("buy on ", drawn, StringComparison.Ordinal);
    }

    static LadderRow Ladder(string plan) => new("ZZZZ", new DateOnly(2026, 9, 8), "range", plan);

    static string Plan(TrancheCondition condition) => Plan(condition.ToString());

    static string Plan(string condition) =>
        $$"""
        {"tranches":[{"lowEdge":"90.0000","highEdge":"92.0000","condition":"{{condition}}","stop":"88.0000"}],
         "exits":[],"invalidation":"88.0000","events":[]}
        """;

    [Fact]
    public async Task TheNightHeaderStatesTheTrueFiredCountAndTheListDrawsAtMostTwenty()
    {
        // Section 17's list display. A page that shows twenty every night cannot
        // tell you how busy the night was, so the header carries the true count
        // over the whole index and the list carries what it drew.
        // see: The page shows twenty and states the true count
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = await api.NewestNightAsync();

        Assert.NotNull(night);

        var listings = await api.ListingsAsync(night!.Value);
        var universe = await api.UniverseAsync("GSPC");
        var fired = TonightScreen.Fired(listings);

        // The header's count is the fired rows over the whole index, not the
        // drawn rows, and the index size is the listing count rather than the
        // names with bars.
        Assert.Equal(listings.Count, universe.Count);
        Assert.Equal(listings.Count(listing => listing.FiredCount > 0), fired);

        var marks = new MarkRenderer();
        var header = marks.NightHeader(night.Value, universe.Count, fired, "00:00:01");

        Assert.Contains($"data-fired=\"{fired}\"", header, StringComparison.Ordinal);
        Assert.Contains($"data-index=\"{universe.Count}\"", header, StringComparison.Ordinal);
        Assert.Contains("data-prose=\"absent\"", header, StringComparison.Ordinal);

        // A night with more than twenty fired names draws twenty and states the
        // true count. The committed fixture holds four names, so the night of
        // forty is constructed: this is section 17's own asserted-by cell.
        var many = Enumerable.Range(0, 40)
            .Select(at => new ListingCell($"N{at:00}", night.Value, 3 - (at % 3), 40 - at, 100m, ["at entry zone"]))
            .ToArray();

        var list = marks.TonightList(many, SinglePageApp.TonightDrawn);

        Assert.Contains("data-fired=\"40\"", list, StringComparison.Ordinal);
        Assert.Contains("data-drawn=\"20\"", list, StringComparison.Ordinal);
        Assert.Contains("data-undrawn=\"20\"", list, StringComparison.Ordinal);
        Assert.Equal(20, Regex.Matches(list, "<tr data-ticker=\"[^\"]+\"").Count);

        // And a night where nothing fired says so rather than drawing an empty
        // table, which a reader would read as a page that failed.
        Assert.Contains("no name fired a reason tonight", marks.TonightList([], SinglePageApp.TonightDrawn), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheListOrdersOnHowManyFiredThenOnBandStrength()
    {
        // Section 15.7's order, and the strength score is the tiebreaker the
        // phase 3 sign-off measured: touches are 72 to 79 per cent of the
        // members of each name's strongest band, so the score is dominated by
        // touches and a tiebreaker dominated by touches is one about how often a
        // price has come back to a level.
        // owes: The strength score read against four names
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);

        var strengths = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var listing in listings)
        {
            var bands = await api.LevelsAsync(listing.Ticker);

            strengths[listing.Ticker] = bands.Count == 0 ? 0 : bands.Max(band => band.Strength);
        }

        var rows = TonightScreen.Rows(listings, strengths, new Dictionary<string, decimal?>(StringComparer.Ordinal));

        // Only the names that fired are drawn, which is what separates the list
        // from the universe screen.
        Assert.Equal(listings.Count(listing => listing.FiredCount > 0), rows.Count);
        Assert.All(rows, row => Assert.True(row.FiredCount > 0));

        Assert.Equal(
            [.. rows.OrderByDescending(row => row.FiredCount).ThenByDescending(row => row.Strength).ThenBy(row => row.Ticker, StringComparer.Ordinal)],
            rows);

        // The tiebreaker is asserted where it decides, over constructed rows
        // whose fired counts are equal, because four names of real bars are not
        // guaranteed to tie.
        var tied = TonightScreen.Rows(
            [
                new ListingRow("AAAA", night, Fired(1), 1, "{}"),
                new ListingRow("BBBB", night, Fired(1), 1, "{}"),
            ],
            new Dictionary<string, int>(StringComparer.Ordinal) { ["AAAA"] = 3, ["BBBB"] = 9 },
            new Dictionary<string, decimal?>(StringComparer.Ordinal));

        Assert.Equal(["BBBB", "AAAA"], [.. tied.Select(row => row.Ticker)]);
    }

    static string Fired(int count) =>
        "[" + string.Join(",", Enumerable.Range(0, count)
            .Select(at => $"{{\"name\":\"reason {at}\",\"fired\":true,\"values\":{{}}}}")) + "]";

    [Fact]
    public async Task TheListingStripDrawsOneCellPerEveningAndSaysNothingAboutMembership()
    {
        // Section 15.5's strip. Its note in 15.8 says the columns say nothing
        // about index membership, which every name in that table has by
        // definition, and that sentence is there because they were misread that
        // way once.
        using var store = await FixtureExpectations.WithListings();

        var api = Api(store);
        var history = await api.ListingsAsync(Name, 60);
        var evenings = TonightScreen.Strip(history);

        Assert.NotEmpty(evenings);

        var strip = new MarkRenderer().ListingStrip(Name, evenings);

        Assert.Contains($"data-evenings=\"{evenings.Count}\"", strip, StringComparison.Ordinal);
        Assert.Contains($"data-listed=\"{evenings.Count(listed => listed)}\"", strip, StringComparison.Ordinal);
        Assert.Equal(evenings.Count, Regex.Matches(strip, "<rect ").Count);

        // A listed evening and a quiet one are drawn differently in shape as
        // well as in ink, because hue is never the only channel that carries a
        // meaning. Asserted over constructed evenings, since the fixture holds
        // one night per name and cannot show both.
        var both = new MarkRenderer().ListingStrip("ZZZZ", [true, false]);

        Assert.Contains("data-listed=\"1\"", both, StringComparison.Ordinal);
        Assert.Contains("height=\"10\"", both, StringComparison.Ordinal);
        Assert.Contains("height=\"1\"", both, StringComparison.Ordinal);
    }

    // ---- 5.1, the universe screen ----

    [Fact]
    public async Task TheUniverseReachesEveryIndexMemberAndNotOnlyTheNamesWithBars()
    {
        // The population is the index, which is what section 15.8 means by
        // answering where everything sits including the names nothing happened
        // to. A screen over the names with bars would be a screen over the wrong
        // population, and it would look complete.
        using var store = await WithLadders();

        var api = Api(store);
        var rows = await api.UniverseAsync("GSPC");

        Assert.Equal(FixtureExpectation.CurrentMembers.Length, rows.Count);

        Assert.Equal(
            [.. FixtureExpectation.CurrentMembers],
            [.. rows.Select(row => row.Ticker)]);

        // A departed name is absent from the universe and still holds its row in
        // the store, which is section 18's row read on the surface it names: it
        // disappears from the universe and its history is kept.
        foreach (var departed in FixtureExpectation.Departed)
        {
            Assert.DoesNotContain(rows, row => row.Ticker == departed);
        }

        Assert.Equal(FixtureExpectation.Constituents, (int)Count(store, "membership"));
    }

    [Fact]
    public async Task TheUniverseIsOrderedByDistanceToTheNearestLevelWithAbsentDistancesLast()
    {
        // Section 15.8 sorts ascending so the top is what nearly fired. A name
        // the night computed nothing for has no distance, and an absent distance
        // read as zero would put every such name at the top of the screen whose
        // whole point is the top.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));

        var measured = cells.Where(cell => cell.Nearest is not null).Select(cell => cell.Nearest!.Value).ToArray();

        Assert.NotEmpty(measured);
        Assert.Equal([.. measured.OrderBy(distance => distance)], measured);

        // Every name carrying a distance sorts above every name carrying none,
        // asserted as a partition rather than by reading the first row.
        var lastMeasured = cells.Select((cell, at) => (cell, at)).Last(pair => pair.cell.Nearest is not null).at;
        var firstAbsent = cells.Select((cell, at) => (cell, at)).FirstOrDefault(pair => pair.cell.Nearest is null, (null!, cells.Count));

        Assert.True(
            lastMeasured < firstAbsent.Item2,
            $"a name with no distance sorts at {firstAbsent.Item2}, above one with a distance at {lastMeasured}.");

        // The partition above passes over a fixture where every name has a
        // distance, because there is nothing on the other side of it. 5.1's own
        // mutation found that: sorting absent distances first left the suite
        // green. So the case is constructed, since the committed fixture holds
        // four names and every one of them has bands and bars.
        var constructed = UniverseScreen.Rows(
        [
            new UniverseRow("NONE", "Technology", null, null, null, null, null),
            new UniverseRow("FAR", "Technology", 100m, "range", 80m, null, 1),
            new UniverseRow("NEAR", "Technology", 100m, "range", 99m, null, 1),
        ]);

        Assert.Equal(["NEAR", "FAR", "NONE"], [.. constructed.Select(cell => cell.Ticker)]);
        Assert.Null(constructed[^1].Nearest);

        // And a name whose typical move is zero has no distance rather than an
        // infinite one, which would sort it to the bottom for the same reason a
        // zero would sort it to the top: it is a name that has not moved, and
        // neither end of the screen is a statement about it.
        var still = UniverseScreen.Rows([new UniverseRow("STILL", "Utilities", 100m, "range", 90m, 110m, 0)]);

        Assert.Null(still.Single().Nearest);
        Assert.Null(still.Single().ToSupport);

        // The distance is in typical days' moves and is derived from the close
        // and the band edge, so it is asserted against the arithmetic rather
        // than against itself.
        foreach (var cell in cells.Where(cell => cell.ToSupport is not null))
        {
            var bands = await api.LevelsAsync(cell.Ticker);
            var support = bands.Where(band => band.Role == "support" && band.Immediate).Max(band => band.HighEdge);
            var typical = (await api.IndicatorsAsync(cell.Ticker, new DateOnly(2000, 1, 1), new DateOnly(2100, 1, 1)))
                .Where(row => row.Name == IndicatorSeries.Atr14)
                .OrderBy(row => row.SessionDate)
                .Last()
                .Value;

            Assert.Equal(
                Math.Abs((double)(cell.Close!.Value - support)) / typical!.Value,
                cell.ToSupport!.Value,
                6);
        }
    }

    [Fact]
    public async Task TheDistanceRowDrawsBothEdgesAndSaysSoWhenThereIsNeither()
    {
        // 15.5's mark for a table cell. Read off the markup rather than by eye,
        // which is the containment property applied to a picture.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));
        var marks = new MarkRenderer();

        var drawn = cells.First(cell => cell.ToSupport is not null && cell.ToResistance is not null);
        var svg = marks.DistanceRow(drawn);

        Assert.Contains($"data-ticker=\"{drawn.Ticker}\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"support-edge\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"resistance-edge\"", svg, StringComparison.Ordinal);
        Assert.Contains("typical days to support", svg, StringComparison.Ordinal);

        // The two hues are the ones support and resistance own, and which is
        // which is asserted rather than that there are two of them: the phase 3
        // sign-off found a mark drawing every support band in the resistance hue
        // while a test asserting two distinct hues stayed green.
        var support = svg[svg.IndexOf("support-edge", StringComparison.Ordinal)..];
        var resistance = svg[svg.IndexOf("resistance-edge", StringComparison.Ordinal)..];

        Assert.Contains("--support", support[..support.IndexOf("/>", StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("--resistance", resistance[..resistance.IndexOf("/>", StringComparison.Ordinal)], StringComparison.Ordinal);

        // A name with neither edge says so rather than drawing a shape at one
        // end, which a reader would read.
        var neither = new UniverseCell("ZZZZ", "not on file", null, null, null, null, null, null, null);
        var absent = marks.DistanceRow(neither);

        Assert.Contains("data-nearest=\"none\"", absent, StringComparison.Ordinal);
        Assert.Contains("no band on either side yet", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("support-edge", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("resistance-edge", absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSectorStripCountsEverySectorAndKeepsNotOnFileOutOfAllOfThem()
    {
        // The strip's lines sum to the index, and a name with no sector is
        // counted in its own line rather than folded into a real one. A falsy
        // value standing in for an absent one is the class this store has been
        // bitten by twice, and a sector count is where it would bite.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));
        var lines = UniverseScreen.Sectors(cells);

        Assert.Equal(cells.Count, lines.Sum(line => line.Names));

        var expected = FixtureExpectation.Of("membership").GetProperty("sectors");

        foreach (var line in lines.Where(line => line.Sector != UniverseScreen.SectorNotOnFile))
        {
            Assert.Equal(
                expected.EnumerateObject().Count(named => named.Value.GetString() == line.Sector),
                line.Names);
        }

        // The uptrend count is read against the ladder rows rather than against
        // itself.
        foreach (var line in lines)
        {
            Assert.Equal(
                cells.Count(cell => cell.Sector == line.Sector && cell.TrendState == "uptrend"),
                line.InUptrend);
        }

        var strip = new MarkRenderer().SectorStrip(lines);

        Assert.Contains($"data-sectors=\"{lines.Count}\"", strip, StringComparison.Ordinal);

        // The count of names on the list, which arrived at 5.4 with the store
        // that feeds it and was absent and said so until then.
        Assert.All(lines, line => Assert.Contains($"data-listed=\"{line.OnTheList}\"", strip, StringComparison.Ordinal));

        // A name with no sector never answers to a real sector's bucket,
        // asserted over a constructed row because every fixture name has one.
        var mixed = UniverseScreen.Sectors(
        [
            new UniverseCell("AAAA", "Technology", null, "uptrend", null, null, null, null, null, null, [true]),
            new UniverseCell("BBBB", UniverseScreen.SectorNotOnFile, null, "range", null, null, null, null, null, null, [false]),
        ]);

        Assert.Equal(1, mixed.Single(line => line.Sector == "Technology").Names);
        Assert.Equal(1, mixed.Single(line => line.Sector == UniverseScreen.SectorNotOnFile).Names);

        // And the on-the-list count is per sector rather than over the whole
        // index, which is the half a total would also satisfy.
        Assert.Equal(1, mixed.Single(line => line.Sector == "Technology").OnTheList);
        Assert.Equal(0, mixed.Single(line => line.Sector == UniverseScreen.SectorNotOnFile).OnTheList);
        Assert.Equal(UniverseScreen.SectorNotOnFile, mixed[^1].Sector);
    }

    [Fact]
    public async Task TheUniverseScreenDrawsEveryNameAndItsFiltersAreLinks()
    {
        // The screen reaches every name rather than the first page of them,
        // which is 5.1's done condition, and the filters are hash routes so a
        // filtered view is a link.
        using var store = await WithLadders();

        var api = Api(store);
        var cells = UniverseScreen.Rows(await api.UniverseAsync("GSPC"));
        var app = new SinglePageApp();
        var region = app.UniverseRegion(new MarkRenderer(), cells, UniverseScreen.Sectors(cells));

        Assert.Contains($"data-names=\"{cells.Count}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-shown=\"{cells.Count}\"", region, StringComparison.Ordinal);
        Assert.Contains($"data-rows=\"{cells.Count}\"", region, StringComparison.Ordinal);

        // Every name in the index has a row, counted off the markup rather than
        // trusted.
        foreach (var cell in cells)
        {
            Assert.Contains($"data-ticker=\"{cell.Ticker}\"", region, StringComparison.Ordinal);
        }

        Assert.Equal(
            cells.Count,
            Regex.Matches(region, "<tr data-ticker=\"[^\"]+\"").Count);

        // The filters, one chip per trend state and one per sector, each a link
        // carrying its value in the hash.
        Assert.Contains("data-filter=\"trend\"", region, StringComparison.Ordinal);
        Assert.Contains("data-filter=\"sector\"", region, StringComparison.Ordinal);
        Assert.Contains("href=\"#/universe?sector=", region, StringComparison.Ordinal);

        // And a filter narrows the table rather than the strip, so the strip
        // keeps saying what the whole index looks like while the table shows the
        // part being read.
        var filtered = app.UniverseRegion(
            new MarkRenderer(),
            cells,
            UniverseScreen.Sectors(cells),
            trendFilter: "uptrend");

        var uptrend = cells.Count(cell => cell.TrendState == "uptrend");

        Assert.Contains($"data-names=\"{cells.Count}\"", filtered, StringComparison.Ordinal);
        Assert.Contains($"data-shown=\"{uptrend}\"", filtered, StringComparison.Ordinal);
        Assert.Equal(uptrend, Regex.Matches(filtered, "<tr data-ticker=\"[^\"]+\"").Count);
    }

    [Fact]
    public async Task EveryStageOfANightRecordsWhenItStartedAndWhenItEnded()
    {
        // The guard 5.1 carries in place of the limit it cannot measure. A
        // night's wall clock at index size is a property of the running system
        // and is read on the run page's operational header once five scheduled
        // nights over the whole index have run; what the code carries is
        // that every stage records its own instants, so a night landing inside
        // its limit by one step doing nothing is legible rather than hidden in a
        // total.
        using var store = await WithLadders();

        Assert.Equal(
            0,
            Scalar(store, "SELECT COUNT(*) FROM run_log WHERE started_at IS NULL OR ended_at IS NULL OR ended_at < started_at;"));


    }

    static long Scalar(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToInt64(command.ExecuteScalar());
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

    // ---- 5.6, the run page ----

    // Constructed input, written straight into a throwaway store, which is what
    // the suite is exempt from writer ownership for. Nothing here reaches the
    // configured data root.
    static void Insert(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static JsonElement Expected(string stage) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FixtureFolder(), "expectations", stage + ".json"))).RootElement;

    // Constructed listing reasons, in section 11's own names, because a stored
    // reason the roster does not carry is refused rather than counted.
    static string FiredNamed(params string[] names) =>
        "[" + string.Join(",", names.Select(name =>
            $"{{\"name\":\"{name}\",\"fired\":true,\"values\":{{\"close\":\"10\"}}}}")) + "]";

    static string RunRow(string runId, string stage, string started, string ended, string outcome = "ok") =>
        "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, " +
        $"model_calls, network_requests, spend, detail) VALUES ('{runId}', '{stage}', '{started}', " +
        $"'{ended}', '{outcome}', 3, 0, 1, '0', 'constructed');";

    [Fact]
    public async Task TheOperationalHeaderDrawsEveryStageOfTheNightWithItsOwnElapsedTime()
    {
        // Section 15.10's first region. Per stage rather than in one total,
        // because a night that landed inside its limit by one step doing nothing
        // is legible only if the steps are apart.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = ((IClock)FixedClock.At(Instant, SessionZones.UnitedStates)).SessionDateAt(Instant);
        var log = await api.RunLogAsync(night);

        // The population is the store's own rows for that night, and it is
        // floored well under what the replay writes so ordinary growth never
        // moves it. The property is the per-stage split, and a header drawn over
        // one row would satisfy every assertion below by having one stage.
        Assert.True(log.Count >= 8, $"The night carried {log.Count} stages, expected at least 8.");

        var stages = RunScreen.Stages(log);
        var header = new MarkRenderer().OperationalHeader(night, stages);

        Assert.Equal(log.Count, stages.Count);
        Assert.Contains($"data-stages=\"{stages.Count}\"", header, StringComparison.Ordinal);
        Assert.Equal(stages.Count, Regex.Matches(header, "<tr data-stage=\"[^\"]+\"").Count);

        // Every stage the store recorded is on the page, named, with the counts
        // the row carries rather than counts this test supplies. The counts are
        // read back off the row they were drawn into, so a header that printed
        // a constant would fail here rather than pass by drawing the right
        // number of rows.
        foreach (var row in log)
        {
            var drawnRow = Regex.Match(
                header,
                $"<tr data-stage=\"{Regex.Escape(row.Stage)}\"(?<attributes>[^>]*)>(?<cells>.*?)</tr>",
                RegexOptions.Singleline);

            Assert.True(drawnRow.Success, $"the header drew no row for the {row.Stage} stage");

            var attributes = drawnRow.Groups["attributes"].Value;

            // The instant the stage started, which is a different question from
            // how long it took and is the one the posting hour is read against.
            // owes: The provider's posting hour for the day's bulk file, measured from live fetches
            Assert.Contains(
                $"data-started=\"{row.StartedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\"",
                attributes,
                StringComparison.Ordinal);

            Assert.Contains($"data-rows=\"{row.RowsWritten}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-model-calls=\"{row.ModelCalls}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-requests=\"{row.NetworkRequests}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-spend=\"{row.Spend}\"", attributes, StringComparison.Ordinal);
            Assert.Contains($"data-outcome=\"{row.Outcome}\"", attributes, StringComparison.Ordinal);

            // The cells and not only the attributes. A claim that something is
            // shown is a claim about the surface a person reads, and the two
            // channels are written by different expressions: the row's counts
            // can be right in the markup a machine reads and wrong in the cells
            // a person does, which is what the sweep found here.
            var cells = drawnRow.Groups["cells"].Value;

            Assert.Contains($"<td>{row.StartedAt.UtcDateTime:HH:mm:ss}</td>", cells, StringComparison.Ordinal);
            Assert.Contains($"<td>{row.RowsWritten}</td>", cells, StringComparison.Ordinal);
            Assert.Contains($"<td>{row.ModelCalls}</td><td>{row.NetworkRequests}</td>", cells, StringComparison.Ordinal);
            Assert.Contains($"<td>{row.Spend}</td><td>{row.Outcome}</td>", cells, StringComparison.Ordinal);
        }

        // The requests are the figure the cost rule is read against, so they are
        // asserted where they are not all the same number. One stage of this
        // night made a request and the rest made none, and a header printing a
        // constant would agree with the store on every row but that one.
        Assert.Contains(log, row => row.NetworkRequests > 0);
        Assert.Contains(log, row => row.NetworkRequests == 0);

        Assert.Contains("listings", header, StringComparison.Ordinal);
        Assert.Contains("forward-returns", header, StringComparison.Ordinal);

        // The elapsed time itself, over constructed rows, because every row a
        // fixed clock writes started and ended at the same instant and a night
        // of zeroes cannot show that the two columns are read apart.
        var timed = RunScreen.Stages(
        [
            new RunStageRow("run-1", "slow", Utc("2026-09-05T21:00:00Z"), Utc("2026-09-05T21:02:30Z"), "ok", 4, 0, 1, "0", "detail"),
            new RunStageRow("run-1", "quick", Utc("2026-09-05T21:02:30Z"), Utc("2026-09-05T21:02:31Z"), "ok", 0, 0, 0, "0", "detail"),
        ]);

        Assert.Equal([150d, 1d], [.. timed.Select(stage => stage.Seconds)]);

        var drawn = new MarkRenderer().OperationalHeader(night, timed);

        Assert.Contains("data-seconds=\"150\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-seconds=\"1\"", drawn, StringComparison.Ordinal);

        // The instant and the duration are drawn from different values, which is
        // what these two rows are shaped to show: the second stage starts where
        // the first ended, so a header deriving one column from the other would
        // disagree here. Asserted on the cells as well as the attributes, since
        // the instant is what a person reads off the page to bound the hour the
        // provider posts the day's file.
        Assert.Contains("data-started=\"2026-09-05T21:00:00Z\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-started=\"2026-09-05T21:02:30Z\"", drawn, StringComparison.Ordinal);
        Assert.Contains("<td>21:00:00</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("<td>21:02:30</td>", drawn, StringComparison.Ordinal);

        Assert.Equal(
            [Utc("2026-09-05T21:00:00Z"), Utc("2026-09-05T21:02:30Z")],
            [.. timed.Select(stage => stage.StartedAt)]);

        // And the order is the order they ran, which is what makes a step that
        // did nothing findable beside the one before it.
        Assert.True(
            drawn.IndexOf("data-stage=\"slow\"", StringComparison.Ordinal)
                < drawn.IndexOf("data-stage=\"quick\"", StringComparison.Ordinal),
            "the header drew a later stage above an earlier one");

        // A night the log carries nothing for says so rather than drawing an
        // empty table, which reads as a night that did nothing.
        Assert.Contains(
            "carries no stage",
            new MarkRenderer().OperationalHeader(night, []),
            StringComparison.Ordinal);
    }

    static DateTimeOffset Utc(string instant) =>
        DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture);

    [Fact]
    public async Task ANightsRunLogIsTheNightsAndTheClockDecidesWhichNightARowIsOn()
    {
        // The run log has no session column and cannot have one it would agree
        // with: the run starts after the close in New York, so the UTC date it
        // carries is the session's on some evenings and the next day's on
        // others. The clock is what decides, and this is the pair of rows that
        // shows it: one written after midnight UTC that belongs to this night,
        // and one written the evening before that does not.
        using var store = await FixtureExpectations.WithReturns();

        Insert(store, RunRow("night-late", "close", "2026-09-06T01:30:00Z", "2026-09-06T01:31:00Z"));
        Insert(store, RunRow("night-before", "listings", "2026-09-04T21:00:00Z", "2026-09-04T21:00:10Z"));

        var api = Api(store);
        var night = new DateOnly(2026, 9, 5);
        var log = await api.RunLogAsync(night);

        // Half past nine in the evening in New York is half past one the next
        // morning in UTC, and it is this night. A filter on the stored date
        // would drop it.
        Assert.Contains(log, row => row.RunId == "night-late");

        // The evening before is its own night, and it is the one that would
        // widen this night's duration if the rows were read together.
        Assert.DoesNotContain(log, row => row.RunId == "night-before");

        Assert.Equal(
            ["night-before"],
            [.. (await api.RunLogAsync(new DateOnly(2026, 9, 4))).Select(row => row.RunId).Distinct()]);

        // The duration follows from the same selection, over the run that wrote
        // that night's list. Every stage the replay wrote carries one instant
        // from the fixed clock, so this night's span is zero and the evening
        // before is ten seconds. Before 5.6 the query took every run carrying a
        // listings stage and spanned the lot, so both of these read as ten
        // minutes and the date in the parameter changed nothing.
        Assert.Equal("00:00:00", await api.NightDurationAsync(night));
        Assert.Equal("00:00:10", await api.NightDurationAsync(new DateOnly(2026, 9, 4)));
        Assert.Null(await api.NightDurationAsync(new DateOnly(2026, 9, 1)));
    }

    [Fact]
    public async Task AReasonsRecordCountsEverySetupThatFiredItAndShowsNoRateBelowTheMinimum()
    {
        // Section 15.10's reason record, in the state this build is in for its
        // first year: counts, and no rate at all.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var listings = await api.ListingsAsync();
        var returns = await api.ForwardReturnsAsync();
        var records = RunScreen.Records(listings, RunScreen.Resolved(returns));

        // The population, derived here from the stored rows rather than read
        // back from the projection, and stated in advance by the expectation
        // file rather than frozen from this run.
        var expected = Expected("run-page");

        Assert.Equal(expected.GetProperty("counts").GetProperty("reasons").GetInt32(), records.Count);
        Assert.Equal(ShortlistSeries.Reasons.Length, records.Count);
        Assert.Equal(RunScreen.MinimumResolvedSetups, expected.GetProperty("rules").GetProperty("minimumResolvedSetups").GetInt32());
        Assert.Equal(expected.GetProperty("counts").GetProperty("resolved").GetInt32(), records.Sum(record => record.Resolved));
        Assert.Equal(expected.GetProperty("counts").GetProperty("fired").GetInt32(), records.Sum(record => record.Fired));

        var fired = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var listing in listings)
        {
            foreach (var reason in JsonDocument.Parse(listing.Reasons).RootElement.EnumerateArray()
                .Where(reason => reason.GetProperty("fired").GetBoolean())
                .Select(reason => reason.GetProperty("name").GetString()!))
            {
                fired[reason] = fired.GetValueOrDefault(reason) + 1;
            }
        }

        foreach (var record in records)
        {
            Assert.Equal(fired.GetValueOrDefault(record.Reason), record.Fired);
        }

        // Nothing the committed fixture holds has matured, so every record is
        // below the minimum and carries no rate. That is the state, not a hole:
        // its listings sit on the last stored session and nothing after them
        // exists.
        Assert.All(records, record => Assert.False(record.HasEarnedAVerdict));
        Assert.Equal(0, records.Sum(record => record.Resolved));

        // A setup belongs to every reason that fired on the night it was listed,
        // and a resolved setup for a name nobody listed belongs to none. Both
        // over constructed rows, because the fixture cannot reach either.
        var night = new DateOnly(2026, 9, 4);

        var constructed = RunScreen.Records(
            [
                new ListingRow("AAAA", night, FiredNamed(ShortlistSeries.AtEntryZone, ShortlistSeries.CrossedALevel), 2, "{}"),
                new ListingRow("BBBB", night, FiredNamed(ShortlistSeries.AtEntryZone), 1, "{}"),
            ],
            [
                new ResolvedSetup("AAAA", night, ForwardReturnSeries.Win),
                new ResolvedSetup("BBBB", night, ForwardReturnSeries.Loss),
                new ResolvedSetup("CCCC", night, ForwardReturnSeries.Win),
            ]);

        var atEntry = constructed.Single(record => record.Reason == ShortlistSeries.AtEntryZone);
        var crossed = constructed.Single(record => record.Reason == ShortlistSeries.CrossedALevel);

        // AAAA fired two reasons and won, so both carry that win. BBBB fired one
        // and lost. A setup counted against one reason alone would be a record
        // about whichever reason happened to be read first.
        Assert.Equal((1, 1), (atEntry.Won, atEntry.Lost));
        Assert.Equal((1, 0), (crossed.Won, crossed.Lost));

        // The unlisted name is in nobody's record, which is what keeps a record
        // a statement about the reason that fired. Two of the three resolved
        // setups reach a record, and one of those reaches two.
        Assert.Equal(3, constructed.Sum(record => record.Resolved));
        Assert.All(
            constructed.Where(record => record.Reason != ShortlistSeries.AtEntryZone && record.Reason != ShortlistSeries.CrossedALevel),
            record => Assert.Equal(0, record.Resolved));

        // And a stored reason the roster does not carry refuses rather than
        // being counted into whichever row is read first.
        var unknown = Assert.Throws<InvalidOperationException>(
            () => RunScreen.Records([new ListingRow("AAAA", night, FiredNamed("a seventh reason"), 1, "{}")], []));

        Assert.Contains("a seventh reason", unknown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordBelowTheMinimumDrawsItsCountInADashedOutlineAndNeverARate()
    {
        // 15.11's first row, and the state the column is in for the first year.
        // The count against the minimum is what makes the absence readable: a
        // reader sees how far off a verdict is rather than only that there is
        // none.
        var marks = new MarkRenderer();

        var below = new ReasonRecord(ShortlistSeries.AtEntryZone, 400, 6, 5, 40, RunScreen.MinimumResolvedSetups);
        var above = new ReasonRecord(ShortlistSeries.CrossedALevel, 900, 150, 130, 60, RunScreen.MinimumResolvedSetups);

        Assert.False(below.HasEarnedAVerdict);
        Assert.True(above.HasEarnedAVerdict);

        var records = new[] { below, above };
        var drawn = marks.ReasonRecords(records, RunScreen.Tracks(records), Rates(1.2, 3.4), 60);

        // One row per reason with the reason track mark, which is what section
        // 15.10 states the region is. A table of counts with the picture gone
        // is a different region.
        Assert.Equal(records.Length, Regex.Matches(drawn, "<svg class=\"reason-track\"").Count);

        Assert.Contains("data-outline=\"dashed\"", drawn, StringComparison.Ordinal);
        Assert.Contains("11 of 250 resolved", drawn, StringComparison.Ordinal);
        Assert.Contains("data-verdict=\"none\"", drawn, StringComparison.Ordinal);

        // No rate anywhere on the region, for either row. The share that reached
        // target before stop and the break-even those setups demanded are the
        // other half of this row and arrive at 7.5 with the verdicts.
        Assert.DoesNotContain("%", drawn, StringComparison.Ordinal);
        Assert.Contains("7.5", drawn, StringComparison.Ordinal);

        // The nights the record stands on, which is what the three operating
        // obligations read on this page are counted in.
        Assert.Contains("data-nights=\"60\"", drawn, StringComparison.Ordinal);
        Assert.Contains("stands on 60 night(s)", drawn, StringComparison.Ordinal);

        // And the split is gated on the same minimum in the picture as in the
        // column. A win beside a loss below the minimum is the same figure
        // through a second channel, since a reader reads the ratio off it.
        var tracks = RunScreen.Tracks(records);

        Assert.Equal(0, tracks[0].Won);
        Assert.Equal(0, tracks[0].Lost);
        Assert.Equal(11, tracks[0].ResolvedUnsplit);
        Assert.Equal(150, tracks[1].Won);
        Assert.Equal(130, tracks[1].Lost);
        Assert.Equal(0, tracks[1].ResolvedUnsplit);
    }

    static IReadOnlyList<BaseRateLine> Rates(double? five, double? twentyOne) =>
    [
        new BaseRateLine(ForwardReturnSeries.FiveSessions, five),
        new BaseRateLine(ForwardReturnSeries.TwentyOneSessions, twentyOne),
    ];

    [Fact]
    public async Task NoForwardReturnFigureIsDrawnWithoutTheUniverseBaseRateBesideIt()
    {
        // Section 17's base rate limit, on the surface a person reads it on. The
        // filler computes the figure and stores it beside every return; the
        // claim is that no forward-return figure on this page is shown without
        // it, which is a claim about a surface.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var returns = await api.ForwardReturnsAsync();
        var rates = RunScreen.BaseRates(returns);

        // One line per window that has one, and the setup horizon is not among
        // them by rule rather than by absence.
        Assert.Equal(
            [ForwardReturnSeries.FiveSessions, ForwardReturnSeries.TwentyOneSessions],
            [.. rates.Select(rate => rate.Window)]);

        // The value is the stored column and not a count taken here. Nothing in
        // the committed fixture has matured, so both windows read as not yet
        // measured rather than as zero.
        foreach (var rate in rates)
        {
            Assert.Equal(
                returns.FirstOrDefault(row => row.Horizon == rate.Window && row.BaseRate is not null)?.BaseRate,
                rate.Rate);
        }

        var records = RunScreen.Records(await api.ListingsAsync(), RunScreen.Resolved(returns));
        var marks = new MarkRenderer();
        var drawn = marks.ReasonRecords(records, RunScreen.Tracks(records), rates, 1);

        Assert.Contains("data-window=\"5\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-window=\"21\"", drawn, StringComparison.Ordinal);
        Assert.Contains("is not yet measured", drawn, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(drawn, "data-pinned=\"true\"").Count);

        // The population in the same breath as the figure, which is what turns
        // a number into one a reader can check.
        Assert.Contains("every name-night the store holds", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("data-window=\"setup\" data-base-rate=\"none\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-window=\"setup\" data-base-rate=\"none by rule\"", drawn, StringComparison.Ordinal);

        // A measured window carries its value, over constructed rows because the
        // fixture's own listings sit on the last stored session and nothing
        // after them exists.
        var measured = marks.ReasonRecords(records, RunScreen.Tracks(records), Rates(41.5, 57.25), 1);

        Assert.Contains("data-base-rate=\"41.5\"", measured, StringComparison.Ordinal);
        Assert.Contains("57.25 per cent", measured, StringComparison.Ordinal);

        // And the region refuses rather than drawing the records with no base
        // rate at all. A figure that can be shown without it is one that will
        // be, on the evening somebody passes an empty list.
        var refused = Assert.Throws<InvalidOperationException>(
            () => marks.ReasonRecords(records, RunScreen.Tracks(records), [], 1));

        Assert.Contains("base rate", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReasonTrackDrawsThreeStatesOutOfOneDenominatorAndOutlinesTheUnresolved()
    {
        // Section 15.5's sixth mark. Its third state is a dashed outline and
        // never a third colour, because unresolved is not a smaller amount of
        // losing and must not read as one.
        var track = new MarkRenderer().ReasonTrack(
        [
            new ReasonTrackRow("busy", 6, 2, 2),
            new ReasonTrackRow("quiet", 2, 1, 2),
            new ReasonTrackRow("never fired", 0, 0, 0),
        ]);

        Assert.Equal(3, Regex.Matches(track, "<g data-reason=").Count);
        Assert.Contains("data-denominator=\"10\"", track, StringComparison.Ordinal);

        // One denominator across the mark rather than one per row. The busy
        // reason's won segment is three times the quiet one's, which is the point
        // of the mark: scaled per row every bar would be full width and the
        // picture would say nothing about which reason fired on more names.
        var widths = Regex.Matches(track, "data-state=\"won\" data-count=\"(\\d+)\" x=\"[\\d.]+\" y=\"\\d+\" width=\"([\\d.]+)\"")
            .Select(match => double.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture))
            .ToArray();

        Assert.Equal(2, widths.Length);
        Assert.Equal(3d, widths[0] / widths[1], 3);

        // The unresolved segment has no fill at all and carries the dashes.
        var unresolved = Regex.Matches(track, "<rect data-state=\"unresolved\"[^/]*/>");

        Assert.Equal(2, unresolved.Count);
        Assert.All(unresolved, match => Assert.Contains("fill=\"none\"", match.Value, StringComparison.Ordinal));
        Assert.All(unresolved, match => Assert.Contains("stroke-dasharray", match.Value, StringComparison.Ordinal));

        // Hue is never the only channel: every row carries its counts in words.
        Assert.Contains("6 won, 2 lost and 2 unresolved, of 10", track, StringComparison.Ordinal);

        // A reason that fired on nothing draws its rule rather than nothing, so
        // a reason that never fires is visible as one that never fires rather
        // than absent from the picture.
        Assert.Contains("no setup on this reason yet", track, StringComparison.Ordinal);
        Assert.Contains("data-reason=\"never fired\" data-total=\"0\"", track, StringComparison.Ordinal);

        // The rule itself, and not only the words on the title. A group holding
        // nothing at all is an empty space on the picture, and an empty space is
        // what a reason absent from the mark would look like.
        var groups = Regex.Matches(track, "<g data-reason=\"(?<reason>[^\"]+)\".*?</g>", RegexOptions.Singleline);
        var empty = groups.Single(group => group.Groups["reason"].Value == "never fired").Value;

        Assert.Contains("height=\"1\" fill=\"var(--rule", empty, StringComparison.Ordinal);
        Assert.DoesNotContain("data-state=", empty, StringComparison.Ordinal);

        // And a reason that did fire draws its segments rather than a rule, so
        // the two states are told apart by the picture as well as by the words.
        Assert.DoesNotContain(
            "height=\"1\" fill=\"var(--rule",
            groups.Single(group => group.Groups["reason"].Value == "busy").Value,
            StringComparison.Ordinal);

        // Below the minimum the resolved setups are one segment rather than two,
        // and the words follow the picture.
        var gated = new MarkRenderer().ReasonTrack([new ReasonTrackRow("gated", 0, 0, 40, 11)]);

        Assert.Contains("data-state=\"resolved\" data-count=\"11\"", gated, StringComparison.Ordinal);
        Assert.DoesNotContain("data-state=\"won\"", gated, StringComparison.Ordinal);
        Assert.Contains("11 resolved and 40 unresolved, of 51", gated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryReasonOnARowCarriesItsRecordAndTheValuesThatMadeItTrue()
    {
        // Section 15.7's reasons-per-row region. The record is the reason's and
        // never the name's: it says how this reason has done across every name
        // it ever fired for, and it is not a statement about the row it sits in.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);
        var universe = await api.UniverseAsync("GSPC");

        var strengths = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var row in universe)
        {
            var bands = await api.LevelsAsync(row.Ticker);

            strengths[row.Ticker] = bands.Count == 0 ? 0 : bands.Max(band => band.Strength);
        }

        var rows = TonightScreen.Rows(
            listings,
            strengths,
            universe.ToDictionary(row => row.Ticker, row => row.Close, StringComparer.Ordinal));

        Assert.NotEmpty(rows);

        var records = RunScreen.Records(await api.ListingsAsync(), RunScreen.Resolved(await api.ForwardReturnsAsync()));
        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn, records);

        // Every reason that fired on a drawn row is named on that row, with the
        // values the store holds for it rather than values this test supplies.
        foreach (var row in rows)
        {
            Assert.NotNull(row.Fired);

            foreach (var reason in row.Fired!)
            {
                Assert.Contains($"data-reason=\"{reason.Name}\"", list, StringComparison.Ordinal);

                foreach (var value in reason.Values)
                {
                    Assert.Contains($"{value.Key} {value.Value}", list, StringComparison.Ordinal);
                }
            }
        }

        // The record beside each reason, in the not-yet-measured state, carrying
        // its count against the minimum rather than a rate.
        var named = rows.SelectMany(row => row.Reasons).Distinct(StringComparer.Ordinal).Count();

        Assert.True(named >= 1, $"the drawn rows named {named} reasons, expected at least 1.");
        Assert.Equal(
            rows.Sum(row => row.Reasons.Count),
            Regex.Matches(list, "class=\"record not-measured\"").Count);

        Assert.Contains($"of {RunScreen.MinimumResolvedSetups} resolved", list, StringComparison.Ordinal);

        // And it is drawn INSIDE the reason's own span, which is the hard rule
        // rather than a detail of the markup: the record says how this reason
        // has done across every name it ever fired for, so a record in a column
        // of its own reads as a property of the ticker on that row.
        //
        // The count above cannot see this. Move every record span out of its
        // reason and into a cell on the ticker's row and the count is unchanged,
        // every data-reason is still present and no rate appears, so all three
        // assertions stay green while the rule is broken. What has teeth is the
        // nesting, asserted both ways: every record sits inside a reason, and
        // none sits outside one.
        // see: A reason's record is displayed, beside the reason and never beside the name
        var nested = Regex.Matches(
            list,
            "<span class=\"reason\"[^>]*>(?:(?!</span>).)*?<span class=\"record[^\"]*\"",
            RegexOptions.Singleline).Count;

        var all = Regex.Matches(list, "<span class=\"record[^\"]*\"").Count;

        Assert.True(all >= 1, $"the list drew {all} records, expected at least 1.");
        Assert.Equal(all, nested);

        // A row with no values stored for a reason says so rather than drawing
        // an empty hover, which reads as a reason with nothing behind it.
        var bare = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 1, 0, 10m, [ShortlistSeries.AtEntryZone])],
            SinglePageApp.TonightDrawn,
            records);

        Assert.Contains("no values stored for this reason", bare, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReasonTotalsDrawTonightsFiredNamesAndEveryOneOfThemIsUnresolved()
    {
        // Section 15.7's last region. Whether the evening is one thing happening
        // to many names or many things happening to a few, and every name on it
        // is a setup nothing has scored yet.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync(night);

        var totals = TonightScreen.Totals(listings);
        var tracks = RunScreen.Tracks(totals);
        var drawn = new MarkRenderer().ReasonTotals(tracks);

        Assert.Equal(ShortlistSeries.Reasons.Length, tracks.Count);

        // The region draws the mark and not only the table beside it. This row
        // is the reason track on tonight's screen, so a region that lost the
        // picture would be the claim gone with the table still passing.
        Assert.Contains("<svg class=\"reason-track\"", drawn, StringComparison.Ordinal);
        Assert.Contains($"data-reasons=\"{tracks.Count}\" data-denominator=", drawn, StringComparison.Ordinal);

        // The counts are the store's, per reason, and the whole of tonight's
        // track is the unresolved state.
        foreach (var total in totals)
        {
            Assert.Contains($"data-reason=\"{total.Reason}\" data-names=\"{total.Names}\"", drawn, StringComparison.Ordinal);
            Assert.Equal(total.Names, tracks.Single(track => track.Reason == total.Reason).Unresolved);
        }

        Assert.All(tracks, track => Assert.Equal(0, track.Won + track.Lost + track.ResolvedUnsplit));
        Assert.DoesNotContain("data-state=\"won\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-unresolved=\"all\"", drawn, StringComparison.Ordinal);

        // The fired count on the page is the fired count in the store, summed
        // over the reasons rather than over the names, because a name that fired
        // two reasons is in two of these.
        Assert.Equal(
            listings.Sum(listing => listing.FiredCount),
            tracks.Sum(track => track.Total));
    }

    [Fact]
    public async Task StaleAndFailedNamesTheStaleNamesAndTheStageThatFailed()
    {
        // Section 15.10's fourth region. The names are listed rather than
        // counted: a page that says four names are stale and does not say which
        // is a page nobody can act on.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);

        // The population is the one the night's closing stage counts over, which
        // is the index and not the names with bars. A member with no series at
        // all is stale in the way that matters, and this is the case the
        // committed fixture cannot reach: it is constructed by adding a member
        // the backfill never saw.
        var before = await api.StaleNamesAsync("GSPC");

        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            "VALUES ('GSPC', 'NEWW', '2026-09-01', NULL, '2026-09-05T21:10:00Z', 'Technology');");

        var stale = await api.StaleNamesAsync("GSPC");

        Assert.Equal(["NEWW"], [.. stale.Except(before, StringComparer.Ordinal)]);
        Assert.Contains("NEWW", stale);

        var failed = RunScreen.Failed(RunScreen.Stages(
        [
            new RunStageRow("run-1", "fetch", Utc("2026-09-05T21:00:00Z"), Utc("2026-09-05T21:00:05Z"), "ok", 4, 0, 1, "0", "fine"),
            new RunStageRow("run-1", "calendar", Utc("2026-09-05T21:00:05Z"), Utc("2026-09-05T21:00:06Z"), "failed", 0, 0, 3, "0", "the feed did not answer"),
        ]));

        Assert.Equal(["calendar"], [.. failed.Select(stage => stage.Stage)]);

        var region = new MarkRenderer().StaleAndFailed(stale, failed);

        Assert.Contains("data-stale=\"1\"", region, StringComparison.Ordinal);
        Assert.Contains("NEWW", region, StringComparison.Ordinal);
        Assert.Contains("data-stage=\"calendar\"", region, StringComparison.Ordinal);
        Assert.Contains("the feed did not answer", region, StringComparison.Ordinal);

        // The research halves are stated as absent rather than drawn as empty
        // lists, which would read as a night that refused nothing.
        Assert.Contains("data-research=\"absent\"", region, StringComparison.Ordinal);

        // A clean night says both things rather than showing two empty regions.
        var clean = new MarkRenderer().StaleAndFailed([], []);

        Assert.Contains("no name is carrying yesterday's bars", clean, StringComparison.Ordinal);
        Assert.Contains("no stage of this night failed", clean, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHarnessRegionCountsOutOfScopeApartFromUnexaminedAndSaysSoWithNoReport()
    {
        // Section 15.10's last region, and CLAUDE.md's rule about the two
        // counts: only one of them is a defect, and a page that summed them
        // would report a build that has not reached a claim as one that failed
        // to check it.
        var report = """
            { "summary": { "pass": 156, "fail": 0, "unexamined": 0, "outOfScope": 81 } }
            """;

        var counts = RunScreen.Harness(report);

        Assert.Equal(new HarnessCounts(156, 0, 0, 81), counts);

        var region = new MarkRenderer().HarnessVerdicts(counts);

        Assert.Contains("data-unexamined=\"0\"", region, StringComparison.Ordinal);
        Assert.Contains("data-out-of-scope=\"81\"", region, StringComparison.Ordinal);
        Assert.Contains("156 passed, 0 failed, 0 unexamined, 81 out of scope", region, StringComparison.Ordinal);

        // Never summed, on the surface as well as in the arithmetic.
        Assert.DoesNotContain("237 ", region, StringComparison.Ordinal);
        Assert.DoesNotContain("81 unexamined", region, StringComparison.Ordinal);

        // A machine with no report says so rather than showing four zeros,
        // which would read as a build nothing failed.
        Assert.Null(RunScreen.Harness(null));
        Assert.Null(RunScreen.Harness("{}"));

        var absent = new MarkRenderer().HarnessVerdicts(null);

        Assert.Contains("no phase report has been written", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("data-passed", absent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageDrawsEveryRegionSectionFifteenTenNamesAndStatesTheTwoThatAreAbsent()
    {
        // The five regions in the order that section states them, with the two
        // that need what phase 7 builds stated as absent rather than drawn
        // empty. An empty region reads as a night that produced nothing.
        using var store = await FixtureExpectations.WithReturns();

        var api = Api(store);
        var night = (await api.NewestNightAsync())!.Value;
        var listings = await api.ListingsAsync();
        var returns = await api.ForwardReturnsAsync();
        var stages = RunScreen.Stages(await api.RunLogAsync(night));
        var records = RunScreen.Records(listings, RunScreen.Resolved(returns));

        var page = new SinglePageApp().RunRegion(
            new MarkRenderer(),
            night,
            stages,
            RunScreen.Failed(stages),
            records,
            RunScreen.Tracks(records),
            RunScreen.BaseRates(returns),
            RunScreen.Nights(listings),
            await api.StaleNamesAsync("GSPC"),
            RunScreen.Harness(null));

        foreach (var region in new[] { "operational", "reason-records", "shadow-candidates", "stale-and-failed", "harness" })
        {
            Assert.Contains($"class=\"{region}\"", page, StringComparison.Ordinal);
        }

        // In that order, so the evidence page reads as section 15.10 states it.
        var at = new[] { "operational", "reason-records", "shadow-candidates", "stale-and-failed", "harness" }
            .Select(region => page.IndexOf($"class=\"{region}\"", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal([.. at.Order()], at);

        Assert.Contains("data-shadow=\"absent\"", page, StringComparison.Ordinal);
        Assert.Contains("7.4", page, StringComparison.Ordinal);

        // And the route is a link, which is what makes the page shareable.
        Assert.Contains(SinglePageApp.RunRoute, new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
        Assert.Contains("/screens/run/", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
    }

    static ChartBar[] Bars(IReadOnlyList<BarRow> served) =>
        [.. served.Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))];
}
