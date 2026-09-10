using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Moves;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Core.Volume;
using EquityBrief.Data.Swings;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Calendar;
using EquityBrief.Worker.Indicators;
using EquityBrief.Core.Facts;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Ladders;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Levels;
using EquityBrief.Worker.Membership;
using EquityBrief.Core.Returns;
using EquityBrief.Worker.News;
using EquityBrief.Worker.Nights;
using EquityBrief.Worker.Returns;
using EquityBrief.Worker.Shortlist;
using EquityBrief.Worker.Swings;
using EquityBrief.Worker.Volume;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations. Each stage's serialised output over the committed
// fixture matches what the rules produce, rather than what a run produced.
//
// The distinction is the whole point of the done condition. A figure frozen
// from a run is a regression baseline: it catches a change and cannot catch a
// mistake, because a backfill storing the wrong window would agree with a count
// taken from itself. The session count here is derived from the trading
// calendar, being the weekdays in the window less the nine named market
// closures, and the pipeline is asserted against that.
// see: The fixture is diffed on each stage's serialised output, of which the facts file is one
//
// Phase 1's stages are membership and the backfill. The seven expected outputs
// section 19.1 also names arrive with the components that compute them, and
// each is owed at its own checkpoint rather than at this one.
public class FixtureExpectations
{
    internal static CheckReach Reach => new(
        "fixture-expectations",
        ["fixtures/membership-2026-09-05", "docs/ARCHITECTURE.html"],
        [
            // 5.5, the forward returns and the news pulse.
            CheckReach.Key(Scope.FixtureTable, "forward returns"),
            CheckReach.Key(Scope.FixtureTable, "news pulse"),

            // 5.4, tonight's list.
            CheckReach.Key(Scope.FixtureTable, "listings"),

            // Section 11 whole, which its own placement names this check for:
            // every one of the six reasons is recomputed here from the tables
            // it reads.
            "11. The shortlist and its six reasons",
            CheckReach.Key(Scope.FailureTable, "Earnings date missing, the earnings reason"),

            // 5.3, the facts file.
            CheckReach.Key(Scope.FixtureTable, "facts"),

            // 5.2, the move annotator.
            CheckReach.Key(Scope.FixtureTable, "moves"),

            CheckReach.Key(Scope.FixtureTable, "bars"),
            CheckReach.Key(Scope.FixtureTable, "news"),
            CheckReach.Key(Scope.FixtureTable, "membership"),
            CheckReach.Key(Scope.FixtureTable, "fetch"),
            CheckReach.Key(Scope.FixtureTable, "series state"),
            CheckReach.Key(Scope.FixtureTable, "indicators"),
            CheckReach.Key(Scope.FixtureTable, "swings"),
            CheckReach.Key(Scope.FixtureTable, "volume profile"),
            CheckReach.Key(Scope.FixtureTable, "levels"),
            CheckReach.Key(Scope.FixtureTable, "ladder"),
            CheckReach.Key(Scope.FixtureTable, "calendar"),
            CheckReach.Key(Scope.FailureTable, "Earnings date missing, the calendar"),
            CheckReach.Key(Scope.LimitsTable, "Earnings horizon"),
            CheckReach.Key(Scope.LimitsTable, "Tranches, exits"),
            CheckReach.Key(Scope.LimitsTable, "Tranche eligibility"),
            CheckReach.Key(Scope.FailureTable, "No band is eligible to carry a tranche"),
            CheckReach.Key(Scope.FailureTable, "A name whose trend state cannot be classified"),
            CheckReach.Key(Scope.LimitsTable, "Level window"),
            CheckReach.Key(Scope.LimitsTable, "Band merge distance"),
            CheckReach.Key(Scope.LimitsTable, "Volume shelf threshold"),
            CheckReach.Key(Scope.LimitsTable, "Swing lookback"),
            CheckReach.Key(Scope.FailureTable, "Fewer than 200 bars for a new index member, 200-day average"),

            // The two flow figures, box by box. Each box is a stage this check
            // replays over the committed fixture and diffs against what the
            // rules produce. Declared per box rather than per figure because a
            // whole-figure entry is only sent by a placement naming this check,
            // and a claim source is sent by its verdicts.
            CheckReach.Key("Figure 9.1", "Collect candidates"),
            CheckReach.Key("Figure 9.1", "Merge into bands"),
            CheckReach.Key("Figure 9.1", "Add touches"),
            CheckReach.Key("Figure 9.1", "Assign roles"),
            CheckReach.Key("Figure 9.1", "Score strength"),
            CheckReach.Key("Figure 9.1", "Levels"),
            CheckReach.Key("Figure 10.1", "Read the trend state"),
            CheckReach.Key("Figure 10.1", "Place tranches"),
            CheckReach.Key("Figure 10.1", "Place stops"),
            CheckReach.Key("Figure 10.1", "Attach conditions"),
            CheckReach.Key("Figure 10.1", "Place exits"),
            CheckReach.Key("Figure 10.1", "Arithmetic"),
            CheckReach.Key("Figure 10.1", "Build the earnings trade"),
            CheckReach.Key("Figure 10.1", "Apply the earnings rule"),
            CheckReach.Key("Figure 10.1", "Ladders"),
        ]);

    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string Folder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static JsonElement Expected(string stage) =>
        JsonDocument.Parse(File.ReadAllText(
            Path.Combine(Folder(), "expectations", stage + ".json"))).RootElement;

    static async Task<TemporaryStore> Replayed()
    {
        var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(Folder(), "index-constituents.json")),
            clock,
            store.DatabaseFile).LoadAsync(Index, "replay-0");

        await new Backfill(
            RecordedHistoricalBarFeed.FromFolder(Folder()),
            clock,
            store.DatabaseFile).RunAsync(Index, "replay-1");

        return store;
    }

    // Constructed input, written straight into a throwaway store. The suite is
    // exempt from writer ownership for exactly this: it writes to temporary
    // directories and nothing here reaches the configured data root.
    static void Insert(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    static IReadOnlyList<string> Query(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount)
                .Select(field => reader.IsDBNull(field) ? "null" : reader.GetValue(field).ToString())));
        }

        return rows;
    }

    [Fact]
    public async Task TheFetchStageStoresWhatTheMembershipFilterLeaves()
    {
        // 2.1's expectation. Every figure in the file is the result of applying
        // the membership filter to the two committed inputs, so this recomputes
        // it from the same inputs rather than comparing a run against itself.
        var expected = Expected("fetch");
        var bulk = RecordedBulkPriceFeed.FromFolder(Folder());
        var rows = await bulk.RowsAsync(
            expected.GetProperty("exchange").GetString()!,
            DateOnly.ParseExact(expected.GetProperty("session").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = Expected("membership").GetProperty("currentMembers")
            .EnumerateArray().Select(one => one.GetString()!).ToArray();
        var departed = Expected("membership").GetProperty("departed")
            .EnumerateArray().Select(one => one.GetString()!).ToArray();

        Assert.Equal(expected.GetProperty("rowsForThisExchange").GetInt32(), rows.Count);
        Assert.Equal(
            expected.GetProperty("rowsForCurrentMembers").GetInt32(),
            rows.Count(row => members.Contains(row.Ticker, StringComparer.Ordinal)));
        Assert.Equal(
            expected.GetProperty("rowsForDepartedConstituents").GetInt32(),
            rows.Count(row => departed.Contains(row.Ticker, StringComparer.Ordinal)));
        Assert.Equal(
            expected.GetProperty("rowsForNamesTheIndexDoesNotHold").GetInt32(),
            rows.Count(row => !members.Contains(row.Ticker, StringComparer.Ordinal)
                && !departed.Contains(row.Ticker, StringComparer.Ordinal)));

        // Zero here, and stated rather than left to be inferred from an empty
        // result. Every one of the seven rows was hand-picked at 1.4 and every
        // one of them traded, so this figure is a property of the capture and
        // not of the exchange: the first live night found 62 in 44,362, and a
        // fixture that never exercises the path is what let that run fail.
        Assert.Equal(expected.GetProperty("notSessionsExpected").GetInt32(), bulk.NotSessions.Count);

        // And the store, which is the surface the figure is about.
        using var store = await Replayed();

        await new BarFetcher(
            RecordedBulkPriceFeed.FromFolder(Folder()),
            FixedClock.At(new DateTimeOffset(2026, 9, 8, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "replay-fetch");

        var session = expected.GetProperty("session").GetString()!;
        var stored = Query(store, $"SELECT ticker FROM bar WHERE session_date = '{session}' ORDER BY ticker;");

        Assert.Equal(expected.GetProperty("barsStoredExpected").GetInt32(), stored.Count);
        Assert.Equal(
            expected.GetProperty("namesStored").EnumerateArray().Select(one => one.GetString()!).ToArray(),
            stored);
    }

    [Fact]
    public void TheSessionCountIsDerivedFromTheCalendarAndNotFromARun()
    {
        // The independently derived expectation the done condition asks for.
        // The count is recomputed here from the same rule the file states, so
        // the file and this check are two derivations of one calendar rather
        // than a number and a copy of it.
        var expected = Expected("bars");

        var from = DateOnly.ParseExact(expected.GetProperty("window").GetProperty("from").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(expected.GetProperty("window").GetProperty("to").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var closures = expected.GetProperty("closures")
            .EnumerateArray()
            .Select(day => DateOnly.ParseExact(day.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToArray();

        var weekdays = 0;
        var sessions = 0;

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            weekdays++;

            if (!closures.Contains(day))
            {
                sessions++;
            }
        }

        Assert.Equal(9, closures.Length);
        Assert.Equal(expected.GetProperty("weekdaysInWindow").GetInt32(), weekdays);
        Assert.Equal(expected.GetProperty("sessionsExpected").GetInt32(), sessions);
        Assert.Equal(261, weekdays);
        Assert.Equal(252, sessions);
    }

    [Fact]
    public async Task TheBackfillStageMatchesTheDerivedExpectation()
    {
        using var store = await Replayed();
        var expected = Expected("bars");

        var names = expected.GetProperty("namesBackfilled").EnumerateArray().Select(n => n.GetString()!).ToArray();

        Assert.Equal(
            [.. names],
            Query(store, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;"));

        Assert.Equal(
            [expected.GetProperty("rowsExpected").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM bar;"));

        Assert.Equal(
            [$"{expected.GetProperty("firstSession").GetString()}|{expected.GetProperty("lastSession").GetString()}"],
            Query(store, "SELECT MIN(session_date), MAX(session_date) FROM bar;"));

        // Per name and not only in total, because three names of the right
        // total can be one name short and another long.
        Assert.All(
            Query(store, "SELECT ticker, COUNT(*) FROM bar GROUP BY ticker ORDER BY ticker;"),
            row => Assert.EndsWith("|" + expected.GetProperty("sessionsExpected").GetInt32(), row, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheStoredBarsCarryTheThreePropertiesTheExpectationNames()
    {
        // The expectation file names three properties in words. They are
        // asserted here rather than left as prose, because a property stated in
        // a fixture and checked nowhere is a comment.
        using var store = await Replayed();

        Assert.Equal(["0"], Query(store,
            "SELECT COUNT(*) FROM bar WHERE CAST(low AS REAL) > CAST(open AS REAL) " +
            "OR CAST(low AS REAL) > CAST(close AS REAL) " +
            "OR CAST(high AS REAL) < CAST(open AS REAL) " +
            "OR CAST(high AS REAL) < CAST(close AS REAL);"));

        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM bar WHERE raw_close IS NULL;"));

        // At least one bar was actually adjusted, so the third property is
        // exercised rather than trivially true over a year with no action.
        var adjusted = Query(store, "SELECT COUNT(*) FROM bar WHERE close <> raw_close;").Single();

        Assert.True(int.Parse(adjusted) > 0, "No stored bar was adjusted, so the one-price-set rule was not exercised.");
    }

    [Fact]
    public async Task TheMembershipStageMatchesItsExpectation()
    {
        using var store = await Replayed();
        var expected = Expected("membership");

        Assert.Equal(
            [expected.GetProperty("constituents").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM membership;"));

        Assert.Equal(
            [.. expected.GetProperty("currentMembers").EnumerateArray().Select(n => n.GetString()!)],
            Query(store, "SELECT ticker FROM membership WHERE left IS NULL ORDER BY ticker;"));

        Assert.Equal(
            [.. expected.GetProperty("departed").EnumerateArray().Select(n => n.GetString()!)],
            Query(store, "SELECT ticker FROM membership WHERE left IS NOT NULL ORDER BY ticker;"));

        // The sector, read from the snapshot object of the same payload, which
        // is where the provider puts it. Two objects read for two things, and
        // the guard refusing the snapshot as the index stands.
        var sectors = expected.GetProperty("sectors");

        foreach (var named in sectors.EnumerateObject())
        {
            Assert.Equal(
                [named.Value.GetString()!],
                Query(store, $"SELECT sector FROM membership WHERE ticker = '{named.Name}';"));
        }

        // A departed name has no sector, because the snapshot carries current
        // members alone. Null is what the provider says rather than an omission,
        // and it is asserted as null rather than as an empty string, because a
        // falsy value standing in for an absent one is the class this store has
        // already been bitten by twice.
        Assert.Equal(
            [.. expected.GetProperty("departed").EnumerateArray().Select(_ => "")],
            Query(store, "SELECT IFNULL(sector, '') FROM membership WHERE left IS NOT NULL ORDER BY ticker;"));

        Assert.Equal(
            [expected.GetProperty("departed").GetArrayLength().ToString()],
            Query(store, "SELECT COUNT(*) FROM membership WHERE sector IS NULL;"));

        // The two populations are different, which is the thing a reader is
        // most likely to conflate: five constituents and three names.
        Assert.NotEqual(
            expected.GetProperty("constituents").GetInt32(),
            expected.GetProperty("currentMembers").GetArrayLength());
    }

    [Fact]
    public void TheNewsInputHoldsWhatARepeatableResearchPassNeeds()
    {
        // 19.1's news row: the articles a research pass is allowed to read,
        // with their publish dates. What phase 1 owes is that the input exists
        // and is readable without a network; the admissibility rules that use
        // it arrive at 6.2.
        var articles = RecordedNewsFeed.Parse(File.ReadAllText(
            Directory.GetFiles(Folder(), RecordedNewsFeed.FilePrefix + "*.json").Single()));

        Assert.NotEmpty(articles);
        Assert.All(articles, article => Assert.True(article.Published.Year >= 2020, "An article carries no usable publish date."));
        Assert.All(articles, article => Assert.False(string.IsNullOrWhiteSpace(article.Text)));
    }

    // ---- the indicators, 3.1's row of section 19.1 ----
    //
    // The expectation is derived from the committed bar files by arithmetic done
    // outside this repository, not frozen from a run. An engine that averaged
    // the wrong window would agree with a figure taken from itself, which is the
    // whole difference between an expectation and a regression baseline.

    static async Task<TemporaryStore> WithIndicators()
    {
        var store = await Replayed();

        await new IndicatorEngine(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("replay-2");

        return store;
    }

    // A statistic crosses from decimal to double, and the sum of twenty doubles
    // is not the sum of twenty decimals. The tolerance is on the last few digits
    // of a double rather than on the figure, so a wrong window or a wrong price
    // fails and the crossing does not.
    const double Rounding = 1e-9;

    static double Stored(TemporaryStore store, string ticker, string session, string name) =>
        double.Parse(
            Query(store, $"SELECT value FROM indicator WHERE ticker = '{ticker}' AND session_date = '{session}' AND name = '{name}';").Single(),
            CultureInfo.InvariantCulture);

    [Fact]
    public async Task EveryNameAndSessionCarriesEveryIndicator()
    {
        var expected = Expected("indicators");
        var names = expected.GetProperty("namesComputed").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var sessions = expected.GetProperty("sessionsPerName").GetInt32();
        var indicators = expected.GetProperty("indicatorNames").EnumerateArray().Select(name => name.GetString()!).ToArray();

        using var store = await WithIndicators();

        // A row for every name, every session and every indicator, whether or
        // not there is a value. An absence is a stored fact here, not a missing
        // row a reader has to interpret.
        Assert.Equal(
            [expected.GetProperty("rowsExpected").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM indicator;"));

        Assert.Equal(
            [.. names.Select(name => $"{name}|{expected.GetProperty("rowsPerName").GetInt32()}")],
            Query(store, "SELECT ticker, COUNT(*) FROM indicator GROUP BY ticker ORDER BY ticker;"));

        // The ten SCHEMA's name column enumerates and no eleventh, which is what
        // stops a name being added in code and going unstated.
        Assert.Equal(
            [.. indicators.OrderBy(name => name, StringComparer.Ordinal)],
            Query(store, "SELECT DISTINCT name FROM indicator ORDER BY name;"));

        Assert.Equal(
            indicators.OrderBy(name => name, StringComparer.Ordinal),
            IndicatorSeries.Names.OrderBy(name => name, StringComparer.Ordinal));

        Assert.Equal(
            sessions.ToString(),
            Query(store, "SELECT COUNT(DISTINCT session_date) FROM indicator;").Single());
    }

    [Fact]
    public async Task TheAveragesMatchTheArithmeticDoneOverTheCommittedBars()
    {
        var expected = Expected("indicators");

        using var store = await WithIndicators();

        foreach (var entry in expected.GetProperty("lastSession").EnumerateObject())
        {
            var ticker = entry.Name;
            var session = entry.Value.GetProperty("lastSession").GetString()!;

            foreach (var pair in new[]
            {
                (Name: IndicatorSeries.Sma20, Key: "sma20"),
                (Name: IndicatorSeries.Sma50, Key: "sma50"),
                (Name: IndicatorSeries.Sma200, Key: "sma200"),
                (Name: IndicatorSeries.VolAvg20, Key: "volAvg20"),
            })
            {
                var stored = Stored(store, ticker, session, pair.Name);
                var derived = entry.Value.GetProperty(pair.Key).GetDouble();

                Assert.True(
                    Math.Abs(stored - derived) < Math.Abs(derived) * Rounding,
                    $"{ticker} {pair.Name} at {session} is {stored} and the arithmetic over the committed bars gives {derived}.");
            }
        }
    }

    [Fact]
    public void TheWorkedExampleAddsUpFromTheClosesItNames()
    {
        // The expectation lists the twenty closes its average is the mean of, so
        // the figure can be checked by adding them up rather than trusted. This
        // asserts the expectation against itself, which is what stops a wrong
        // number being carried in the file and agreed with by the engine.
        var worked = Expected("indicators").GetProperty("workedExample");
        var closes = worked.GetProperty("closes").EnumerateArray().Select(value => value.GetDouble()).ToArray();

        Assert.Equal(20, closes.Length);

        var mean = closes.Sum() / closes.Length;
        var stated = worked.GetProperty("sma20").GetDouble();

        Assert.True(
            Math.Abs(mean - stated) < Math.Abs(stated) * Rounding,
            $"The worked example states {stated} and its own closes average {mean}.");

        // And those closes are the fixture's, not numbers written into the
        // expectation. Read back from the committed bar file for the twenty
        // sessions ending at the named one.
        var file = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Folder(), $"bars-{worked.GetProperty("name").GetString()}.json"))).RootElement;

        var session = worked.GetProperty("session").GetString();

        var captured = file.EnumerateArray()
            .Where(bar => string.CompareOrdinal(bar.GetProperty("date").GetString(), session) <= 0)
            .TakeLast(20)
            .Select(bar => bar.GetProperty("adjusted_close").GetDouble())
            .ToArray();

        Assert.Equal(closes, captured);
    }

    [Fact]
    public async Task AnIndicatorWithoutItsWindowIsNotAvailableWithItsBarCount()
    {
        // 3.1's done condition, and the reason bar_count is a column. A name
        // with fewer than two hundred bars behind a session records its long
        // average as absent with the count that explains it, never as a number
        // computed over whatever it had.
        var expected = Expected("indicators");
        var absent = expected.GetProperty("notAvailable");

        using var store = await WithIndicators();

        Assert.Equal(
            [absent.GetProperty("total").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM indicator WHERE value IS NULL;"));

        foreach (var entry in absent.GetProperty("perIndicatorPerName").EnumerateObject())
        {
            var perName = entry.Value.GetInt32();

            Assert.Equal(
                [$"{entry.Name}|{perName * expected.GetProperty("namesComputed").GetArrayLength()}"],
                Query(store, $"SELECT name, COUNT(*) FROM indicator WHERE name = '{entry.Name}' AND value IS NULL;"));
        }

        // Every null carries a bar count below its window and every value one at
        // it. That is the property; the counts above are the population it holds
        // over.
        Assert.Equal(
            ["0"],
            Query(store, "SELECT COUNT(*) FROM indicator WHERE value IS NULL AND bar_count >= 200 AND name = 'sma200';"));

        Assert.Equal(
            ["0"],
            Query(store, "SELECT COUNT(*) FROM indicator WHERE value IS NOT NULL AND bar_count < 200 AND name = 'sma200';"));

        // At the first session every indicator has one bar behind it, whatever
        // its window, which is what makes bar_count the history and not the
        // window.
        Assert.Equal(
            [expected.GetProperty("barCount").GetProperty("atFirstSession").GetInt32().ToString()],
            Query(store, "SELECT DISTINCT bar_count FROM indicator WHERE session_date = (SELECT MIN(session_date) FROM indicator);"));

        Assert.Equal(
            [expected.GetProperty("barCount").GetProperty("sma200SessionsWithAValue").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM indicator WHERE name = 'sma200' AND value IS NOT NULL AND ticker = 'AAPL';"));
    }

    [Fact]
    public async Task ASecondRunReplacesRatherThanDuplicating()
    {
        // Recomputed nightly, so the night is idempotent in what it records: two
        // runs over one session leave one row saying the same thing. Without the
        // upsert the primary key would refuse the second night rather than the
        // night updating.
        using var store = await WithIndicators();

        var before = Query(store, "SELECT COUNT(*), SUM(bar_count) FROM indicator;");

        await new IndicatorEngine(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("replay-3");

        Assert.Equal(before, Query(store, "SELECT COUNT(*), SUM(bar_count) FROM indicator;"));
    }

    [Fact]
    public async Task TheSeriesStateIsWhatTheCapturedActionsIntersectedWithMembershipGive()
    {
        // Written at 3.1 because fixture-replay found the table populated and no
        // expectation naming it. The corporate action checker has written it
        // since 1.6 and nothing said what it should hold, which is a figure the
        // pipeline produces that nothing expected.
        //
        // Derived rather than frozen: the two captured action files carry seven
        // actions over seven distinct names, and the expectation is their
        // intersection with the membership expectation's current members. That
        // is arithmetic over two committed inputs, not a reading of the store.
        var expected = Expected("series-state");

        var refetched = expected.GetProperty("namesRefetched")
            .EnumerateArray().Select(name => name.GetString()!).ToArray();

        var members = Expected("membership").GetProperty("currentMembers")
            .EnumerateArray().Select(name => name.GetString()!).ToArray();

        var inFiles = expected.GetProperty("distinctNamesInFiles")
            .EnumerateArray().Select(name => name.GetString()!).ToArray();

        // The action count, read back off the captured files rather than taken
        // on the expectation's word. Seven actions over seven distinct names is
        // what makes the intersection below one name rather than an accident.
        var actions = new[] { "actions-dividends-2026-08-10", "actions-splits-2026-08-10" }
            .Sum(file => JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), file + ".json")))
                .RootElement.GetArrayLength());

        Assert.Equal(expected.GetProperty("actionsInFiles").GetInt32(), actions);
        Assert.Equal(inFiles.Length, actions);

        // The intersection, recomputed here from the two files rather than read
        // out of the one that states it.
        Assert.Equal(refetched, inFiles.Intersect(members, StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal));

        using var store = await Replayed();

        await new CorporateActionChecker(
            RecordedCorporateActionFeed.FromFolder(Folder()),
            RecordedHistoricalBarFeed.FromFolder(Folder()),
            FixedClock.At(new DateTimeOffset(2026, 8, 10, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "replay-actions");

        Assert.Equal(
            [expected.GetProperty("rowsExpected").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM series_state;"));

        foreach (var state in expected.GetProperty("states").EnumerateObject())
        {
            Assert.Equal(
                [$"{state.Name}|{state.Value.GetString()}|null"],
                Query(store, $"SELECT ticker, state, reason FROM series_state WHERE ticker = '{state.Name}';"));
        }

        // And nothing is suspect, which is the figure that would move first if a
        // captured action stopped parsing.
        Assert.Equal(
            [expected.GetProperty("suspectExpected").GetInt32().ToString()],
            Query(store, $"SELECT COUNT(*) FROM series_state WHERE state = '{CorporateActionChecker.Suspect}';"));
    }

    // ---- 3.2, the swings ----

    static async Task<TemporaryStore> WithSwings()
    {
        var store = await Replayed();

        await new SwingFinder(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("replay-swings");

        return store;
    }

    [Fact]
    public async Task TheSwingsMatchTheArithmeticOverTheCommittedBars()
    {
        // The whole diff, name by name, rather than a count and a sample. A
        // count agrees with a finder that marked the wrong days in the right
        // number, and a sample agrees with one that found half of them.
        var expected = Expected("swings");
        var names = expected.GetProperty("namesComputed").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var rows = expected.GetProperty("rows");

        using var store = await WithSwings();

        Assert.Equal(
            [expected.GetProperty("rowsExpected").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM swing;"));

        foreach (var ticker in names)
        {
            Assert.Equal(
                rows.GetProperty("byName").GetProperty(ticker).EnumerateArray().Select(row => row.GetString()!),
                Query(store, $"SELECT session_date, direction, price, confirmed_on FROM swing WHERE ticker = '{ticker}' ORDER BY session_date, direction;"));

            var counts = expected.GetProperty("perName").GetProperty(ticker);

            Assert.Equal(
                [$"{counts.GetProperty("highs").GetInt32()}|{counts.GetProperty("lows").GetInt32()}"],
                Query(store, $"SELECT SUM(direction = 'high'), SUM(direction = 'low') FROM swing WHERE ticker = '{ticker}';"));
        }

        // The lookback the expectation was derived at is the one the code uses.
        // Section 17 states the number and this diff is its Asserted by column,
        // so the two are read against each other here rather than left to agree
        // by coincidence.
        Assert.Equal(
            SwingSeries.Lookback,
            expected.GetProperty("lookback").GetProperty("barsEachSide").GetInt32());

        // The session count is the same population the indicators run over, so
        // a fixture whose window moved would fail here as well as there.
        Assert.Equal(
            expected.GetProperty("sessionsPerName").GetInt32().ToString(),
            Query(store, "SELECT COUNT(DISTINCT session_date) FROM bar WHERE ticker = 'AAPL';").Single());
    }

    [Fact]
    public async Task TheWorkedSwingIsAboveTheSixSessionsAroundIt()
    {
        // The expectation asserted against the store rather than trusted, the
        // way the twenty closes behind the worked average are.
        //
        // Against the store and not against the committed bar file, which is the
        // difference from that one. A swing compares highs and lows, and those
        // are adjusted before they are stored, so the file's numbers are not the
        // ones the comparison ran over. The stored series is asserted against the
        // file by the bars expectation and by bar-bounds, so reading it here is
        // reading something already held rather than assuming it.
        var worked = Expected("swings").GetProperty("workedExample");
        var ticker = worked.GetProperty("name").GetString()!;
        var session = worked.GetProperty("session").GetString()!;

        using var store = await WithSwings();

        var window = worked.GetProperty("window")
            .EnumerateArray()
            .Select(row => string.Join("|", row.EnumerateArray().Select(cell => cell.GetString())))
            .ToArray();

        Assert.Equal(SwingSeries.Lookback * 2 + 1, window.Length);

        var first = window[0].Split('|')[0];
        var last = window[^1].Split('|')[0];

        Assert.Equal(
            window,
            Query(store, $"SELECT session_date, high, low FROM bar WHERE ticker = '{ticker}' AND session_date BETWEEN '{first}' AND '{last}' ORDER BY session_date;"));

        // The middle session's high above all six others, read off the window
        // the expectation names rather than restated.
        var highs = window.Select(row => decimal.Parse(row.Split('|')[1], CultureInfo.InvariantCulture)).ToArray();
        var peak = highs[SwingSeries.Lookback];

        Assert.All(
            highs.Where((_, index) => index != SwingSeries.Lookback),
            neighbour => Assert.True(peak > neighbour, $"{session} has a high of {peak} and a neighbour at {neighbour}."));

        // And the row is in the store, with the confirmation date the seventh
        // session of the window carries.
        Assert.Equal(
            [$"{worked.GetProperty("price").GetString()}|{last}"],
            Query(store, $"SELECT price, confirmed_on FROM swing WHERE ticker = '{ticker}' AND session_date = '{session}' AND direction = '{worked.GetProperty("direction").GetString()}';"));

        Assert.Equal(worked.GetProperty("confirmedOn").GetString(), last);
    }

    [Fact]
    public async Task ASessionThatWouldBeASwingAtTwoBarsEachSideIsNotOneAtThree()
    {
        // Section 17's swing lookback, asserted rather than agreed with.
        //
        // Every test above would pass unchanged against a finder comparing two
        // bars each side or four, because they compare its output with an
        // expectation derived at three and a wrong lookback would simply make
        // both wrong together. This one names a session where the two answers
        // differ, which is the only kind of case that can tell them apart.
        var near = Expected("swings").GetProperty("notASwingAtThisLookback");
        var ticker = near.GetProperty("name").GetString()!;
        var session = near.GetProperty("session").GetString()!;

        var window = near.GetProperty("window")
            .EnumerateArray()
            .Select(row => row.EnumerateArray().Select(cell => cell.GetString()!).ToArray())
            .ToArray();

        var highs = window.Select(row => decimal.Parse(row[1], CultureInfo.InvariantCulture)).ToArray();
        var middle = SwingSeries.Lookback;

        // Highest of the five, which is what a two-bar lookback would see.
        Assert.All(
            new[] { highs[middle - 2], highs[middle - 1], highs[middle + 1], highs[middle + 2] },
            neighbour => Assert.True(highs[middle] > neighbour, $"{session} is not the highest of its five bars."));

        // And not the highest of the seven, which is what a three-bar lookback
        // sees. The outer pair is where the difference lives.
        Assert.True(
            highs[middle] < highs[middle - 3] || highs[middle] < highs[middle + 3],
            $"{session} is the highest of its seven bars, so it does not tell two bars each side from three.");

        using var store = await WithSwings();

        Assert.Equal(
            window.Select(row => string.Join("|", row)),
            Query(store, $"SELECT session_date, high, low FROM bar WHERE ticker = '{ticker}' AND session_date BETWEEN '{window[0][0]}' AND '{window[^1][0]}' ORDER BY session_date;"));

        Assert.Empty(
            Query(store, $"SELECT session_date FROM swing WHERE ticker = '{ticker}' AND session_date = '{session}' AND direction = '{near.GetProperty("direction").GetString()}';"));
    }

    [Fact]
    public async Task TheEdgesOfTheStoredSeriesCarryNoSwingAndNoSessionIsBothDirections()
    {
        var expected = Expected("swings");
        var edges = expected.GetProperty("edges");

        using var store = await WithSwings();

        foreach (var key in new[] { "firstSessionsWithNoSwing", "lastSessionsWithNoSwing" })
        {
            var sessions = edges.GetProperty(key).EnumerateArray().Select(day => day.GetString()!).ToArray();

            Assert.Equal(SwingSeries.Lookback, sessions.Length);

            // Over every name, not only the one the dates were read from. The
            // three names share a trading calendar, so the edge sessions are the
            // same three at each end for all of them.
            Assert.Empty(Query(
                store,
                $"SELECT ticker, session_date FROM swing WHERE session_date IN ({string.Join(", ", sessions.Select(day => $"'{day}'"))});"));
        }

        // A session may be a swing high and a swing low at once, and none in this
        // fixture is. The count is stated so a fourth name that has one is a
        // change somebody reads.
        Assert.Equal(
            [expected.GetProperty("bothDirections").GetProperty("count").GetInt32().ToString()],
            Query(store, "SELECT COUNT(*) FROM (SELECT ticker, session_date FROM swing GROUP BY ticker, session_date HAVING COUNT(*) > 1);"));
    }

    [Fact]
    public async Task ASwingIsNotVisibleToAReaderAsOfADateBeforeItsLookbackCompleted()
    {
        // 3.2's second done condition, and the reason confirmed_on is a column.
        //
        // The row this withholds is dated before the as-of date and confirmed
        // after it. A reader filtering on session_date returns it and gets a peak
        // nobody could have seen at the time; a reader filtering on confirmed_on
        // does not. Both queries return rows and both look right, which is why
        // the property is asserted here rather than left to the shape of a
        // WHERE clause somebody may rewrite.
        var asOf = Expected("swings").GetProperty("asOf");
        var ticker = asOf.GetProperty("name").GetString()!;
        var date = DateOnly.ParseExact(asOf.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        using var store = await WithSwings();
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        var visible = StoredSwings.AsOf(connection, ticker, date);

        Assert.Equal(asOf.GetProperty("visible").GetInt32(), visible.Count);
        Assert.All(visible, swing => Assert.True(
            swing.ConfirmedOn <= date,
            $"{swing.SessionDate:yyyy-MM-dd} was confirmed on {swing.ConfirmedOn:yyyy-MM-dd}, after the as-of date."));

        var withheld = asOf.GetProperty("withheld")
            .EnumerateArray()
            .Select(row => row.GetString()!.Split('|'))
            .ToArray();

        // The population that makes this a test rather than a tautology: at
        // least one row dated at or before the as-of date and confirmed after
        // it. Without one, filtering on either column returns the same set and
        // the assertion above holds against the wrong query.
        Assert.NotEmpty(withheld);

        foreach (var row in withheld)
        {
            var session = DateOnly.ParseExact(row[0], "yyyy-MM-dd", CultureInfo.InvariantCulture);

            Assert.True(session <= date, $"{row[0]} is not before the as-of date, so it proves nothing.");
            Assert.DoesNotContain(visible, swing => swing.SessionDate == session && swing.Direction == row[1]);

            // And it is in the store, so the reader is withholding a row rather
            // than answering about one that was never written.
            Assert.Equal(
                [string.Join("|", row)],
                Query(store, $"SELECT session_date, direction, price, confirmed_on FROM swing WHERE ticker = '{ticker}' AND session_date = '{row[0]}' AND direction = '{row[1]}';"));
        }

        // The counter-reading, in the same test rather than in a comment. As of
        // the last confirmation date the fixture holds, everything is visible,
        // so the filter is withholding by date rather than by anything else.
        Assert.Equal(
            Query(store, $"SELECT COUNT(*) FROM swing WHERE ticker = '{ticker}';").Single(),
            StoredSwings.AsOf(connection, ticker, new DateOnly(2026, 12, 31)).Count.ToString());
    }

    [Fact]
    public async Task ASecondSwingRunReplacesRatherThanDuplicating()
    {
        // The same property the indicators carry, and it matters more here:
        // `swing` has no declared deleter, so a run that inserted instead of
        // upserting would fail on the primary key and a run that wrote new rows
        // would leave the old ones with nothing able to remove them.
        using var store = await WithSwings();

        var before = Query(store, "SELECT COUNT(*), COUNT(DISTINCT confirmed_on) FROM swing;");

        await new SwingFinder(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("replay-swings-again");

        Assert.Equal(before, Query(store, "SELECT COUNT(*), COUNT(DISTINCT confirmed_on) FROM swing;"));
    }

    // ---- 3.3, the volume profile ----

    static async Task<TemporaryStore> WithProfile()
    {
        var store = await Replayed();

        await new VolumeProfileBuilder(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("replay-profile");

        return store;
    }

    [Fact]
    public async Task TheVolumeProfileMatchesTheArithmeticOverTheCommittedBars()
    {
        var expected = Expected("volume-profile");
        var names = expected.GetProperty("namesComputed").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var asOf = expected.GetProperty("asOf");
        var rows = expected.GetProperty("rows");

        using var store = await WithProfile();

        foreach (var ticker in names)
        {
            // The share of the period is REAL, so it is compared at the nine
            // places the expectation states rather than as a string. Everything
            // else in the row is exact: two prices and a count.
            Assert.Equal(
                rows.GetProperty("byName").GetProperty(ticker).EnumerateArray().Select(row => row.GetString()!),
                Query(store, $"SELECT band_low, band_high, share_count, printf('%.9f', share_of_period) FROM volume_profile WHERE ticker = '{ticker}' ORDER BY as_of, band_low;"));

            Assert.Equal(
                [$"{asOf.GetProperty(ticker).GetString()}|{expected.GetProperty("bands").GetProperty("count").GetInt32()}"],
                Query(store, $"SELECT as_of, COUNT(*) FROM volume_profile WHERE ticker = '{ticker}' GROUP BY as_of;"));
        }
    }

    [Fact]
    public async Task TheSharesInTheBandsSumToTheWindowsTotalVolume()
    {
        // 3.3's second done condition, and the reason the builder apportions
        // rather than rounds. The total is read out of the bar table over the
        // window rather than out of the expectation, so this compares the
        // profile against the store it was computed from and not against a
        // figure that travelled beside it.
        var expected = Expected("volume-profile");
        var window = expected.GetProperty("window").GetProperty("sessions").GetInt32();
        var totals = expected.GetProperty("totals").GetProperty("byName");

        using var store = await WithProfile();

        foreach (var entry in totals.EnumerateObject())
        {
            var stored = Query(
                store,
                $"SELECT SUM(volume) FROM (SELECT volume FROM bar WHERE ticker = '{entry.Name}' ORDER BY session_date DESC LIMIT {window});").Single();

            Assert.Equal(entry.Value.GetInt64().ToString(), stored);

            Assert.Equal(
                [stored],
                Query(store, $"SELECT SUM(share_count) FROM volume_profile WHERE ticker = '{entry.Name}';"));
        }

        // And the shares of the period sum to one, which is the same statement
        // read off the other column. Not exactly one: it is a sum of twenty
        // doubles and the tolerance is on the last digits rather than on the
        // figure.
        foreach (var entry in totals.EnumerateObject())
        {
            var summed = double.Parse(
                Query(store, $"SELECT SUM(share_of_period) FROM volume_profile WHERE ticker = '{entry.Name}';").Single(),
                CultureInfo.InvariantCulture);

            Assert.True(Math.Abs(summed - 1) < 1e-12, $"{entry.Name}'s shares of the period sum to {summed}.");
        }
    }

    [Fact]
    public async Task TheBandsTileTheWindowsOwnHighAndLow()
    {
        // Every band's high edge is the next band's low edge, the lowest edge is
        // the window's low and the highest is the window's high. A gap between
        // two bands is volume that went nowhere, and an overlap is volume
        // counted twice, and neither shows in a total that was apportioned to
        // match anyway.
        var expected = Expected("volume-profile");
        var extremes = expected.GetProperty("extremes");

        using var store = await WithProfile();

        foreach (var entry in extremes.EnumerateObject())
        {
            var edges = Query(store, $"SELECT band_low, band_high FROM volume_profile WHERE ticker = '{entry.Name}' ORDER BY band_low;")
                .Select(row => row.Split('|'))
                .ToArray();

            Assert.Equal(entry.Value.GetProperty("low").GetString(), edges[0][0]);
            Assert.Equal(entry.Value.GetProperty("high").GetString(), edges[^1][1]);

            for (var band = 1; band < edges.Length; band++)
            {
                Assert.Equal(edges[band - 1][1], edges[band][0]);
            }

            // And the extremes are the window's, read from the bars rather than
            // from the expectation.
            var window = expected.GetProperty("window").GetProperty("sessions").GetInt32();

            Assert.Equal(
                [$"{entry.Value.GetProperty("low").GetString()}|{entry.Value.GetProperty("high").GetString()}"],
                Query(store, $"SELECT MIN(low), MAX(high) FROM (SELECT low, high FROM bar WHERE ticker = '{entry.Name}' ORDER BY session_date DESC LIMIT {window});"));
        }
    }

    [Fact]
    public async Task ASessionsVolumeIsSpreadAcrossTheBandsItsRangeCovers()
    {
        // The spreading rule, checkable by hand. The expectation names one
        // session and the width of the overlap its range makes with each band it
        // touches, and those widths sum to the session's own range. A builder
        // placing the whole day at its close would touch one band, and the sum
        // would be the range only by accident.
        var worked = Expected("volume-profile").GetProperty("workedDay");
        var ticker = worked.GetProperty("name").GetString()!;
        var session = worked.GetProperty("session").GetString()!;

        using var store = await WithProfile();

        // The session as the store holds it, so the numbers below are the ones
        // the builder spread rather than the ones the fixture file carries.
        Assert.Equal(
            [$"{worked.GetProperty("high").GetString()}|{worked.GetProperty("low").GetString()}|{worked.GetProperty("volume").GetInt64()}"],
            Query(store, $"SELECT high, low, volume FROM bar WHERE ticker = '{ticker}' AND session_date = '{session}';"));

        var high = decimal.Parse(worked.GetProperty("high").GetString()!, CultureInfo.InvariantCulture);
        var low = decimal.Parse(worked.GetProperty("low").GetString()!, CultureInfo.InvariantCulture);

        var touched = worked.GetProperty("touches")
            .EnumerateArray()
            .Select(touch => (
                Low: decimal.Parse(touch.GetProperty("bandLow").GetString()!, CultureInfo.InvariantCulture),
                High: decimal.Parse(touch.GetProperty("bandHigh").GetString()!, CultureInfo.InvariantCulture),
                Overlap: decimal.Parse(touch.GetProperty("overlap").GetString()!, CultureInfo.InvariantCulture)))
            .ToArray();

        Assert.Equal(high - low, touched.Sum(touch => touch.Overlap));
        Assert.Equal(worked.GetProperty("range").GetString(), (high - low).ToString(CultureInfo.InvariantCulture));

        // Recomputed against the band edges the store holds, so the stated
        // overlaps are asserted against the bands the builder actually wrote.
        var bands = Query(store, $"SELECT band_low, band_high FROM volume_profile WHERE ticker = '{ticker}' ORDER BY band_low;")
            .Select(row => row.Split('|'))
            .Select(row => (
                Low: decimal.Parse(row[0], CultureInfo.InvariantCulture),
                High: decimal.Parse(row[1], CultureInfo.InvariantCulture)))
            .ToArray();

        var overlapping = bands
            .Select(band => (band.Low, band.High, Overlap: Math.Min(band.High, high) - Math.Max(band.Low, low)))
            .Where(band => band.Overlap > 0)
            .ToArray();

        Assert.Equal(touched, overlapping);

        // The population that makes this worth asserting: the session's range
        // covers more than one band. A worked day inside a single band would
        // hold for a builder that placed the whole day at its close.
        Assert.True(touched.Length > 1, $"{session} touches {touched.Length} band(s), which cannot show a spread.");
    }

    [Fact]
    public async Task AnEvenShareIsATwentiethAndTheFixturesShelvesClearTwiceIt()
    {
        // Section 17's shelf threshold read against the fixture. The threshold
        // is a multiple of an even share and an even share is one over the band
        // count, so the two numbers are asserted together: a band count changed
        // in code without the threshold being reread would move what "twice an
        // even share" means and nothing else would notice.
        var expected = Expected("volume-profile");
        var even = expected.GetProperty("evenShare");

        Assert.Equal(1d / expected.GetProperty("bands").GetProperty("count").GetInt32(), even.GetProperty("value").GetDouble());
        Assert.Equal(2 * even.GetProperty("value").GetDouble(), even.GetProperty("twice").GetDouble());
        Assert.Equal(VolumeProfileSeries.Bands, expected.GetProperty("bands").GetProperty("count").GetInt32());

        using var store = await WithProfile();

        var threshold = even.GetProperty("twice").GetDouble().ToString(CultureInfo.InvariantCulture);

        foreach (var entry in even.GetProperty("bandsAtOrAboveTwice").EnumerateObject())
        {
            var clearing = entry.Value.EnumerateArray().Select(band => band.GetString()!).ToArray();

            Assert.Equal(
                clearing,
                Query(store, $"SELECT band_low FROM volume_profile WHERE ticker = '{entry.Name}' AND share_of_period >= {threshold} ORDER BY band_low;"));

            // Each name has one, which is what makes the threshold assertable at
            // all. A fixture where nothing cleared it would agree with any
            // threshold above the largest band.
            Assert.NotEmpty(clearing);
        }
    }

    [Fact]
    public async Task TheShelfThresholdHoldsAcrossFourNamesAndItsNeighboursDoNot()
    {
        // The obligation 3.6 carries, discharged.
        // owes: Volume shelf threshold checked against four names
        //
        // Section 17 set the threshold at twice an even share from one chart,
        // and the obligation was that one chart cannot fix a threshold. So the
        // fixture was widened to four names of different character and the two
        // candidates either side of the figure are measured beside it, which is
        // what makes this a calibration rather than a number that happens to
        // work: a threshold asserted alone agrees with itself, and one asserted
        // against its neighbours has to beat them.
        var expected = Expected("volume-profile");
        var across = expected.GetProperty("thresholdAcrossFourNames");
        var sweep = across.GetProperty("sweep");
        var names = expected.GetProperty("namesComputed").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var bands = expected.GetProperty("bands").GetProperty("count").GetInt32();

        Assert.Equal(FixtureExpectation.Names.Length, names.Length);
        Assert.Equal(2, across.GetProperty("chosen").GetInt32());
        Assert.Equal(2d / bands, LevelBuilder.ShelfThreshold);

        using var store = await WithProfile();

        // Every multiple the sweep names, read off the store rather than off the
        // expectation, so the three columns are three readings of one profile.
        foreach (var multiple in sweep.EnumerateObject())
        {
            var factor = int.Parse(multiple.Name[1..], CultureInfo.InvariantCulture);
            var threshold = (factor / (double)bands).ToString(CultureInfo.InvariantCulture);

            foreach (var name in multiple.Value.EnumerateObject())
            {
                var counted = Query(
                    store,
                    $"SELECT COUNT(*), printf('%.6f', COALESCE(SUM(share_of_period), 0)) FROM volume_profile WHERE ticker = '{name.Name}' AND share_of_period >= {threshold};").Single();

                Assert.Equal(
                    $"{name.Value.GetProperty("shelves").GetInt32()}|{name.Value.GetProperty("shareOfPeriod").GetDouble():0.000000}",
                    counted);
            }
        }

        // What the three columns say, asserted as the property rather than left
        // for a reader to notice in the numbers.
        var at = sweep.GetProperty("x2");
        var below = sweep.GetProperty("x1");
        var above = sweep.GetProperty("x3");

        foreach (var name in names)
        {
            // At the chosen figure every name has a shelf, and it is a minority
            // of its bands. A threshold that named half the chart is not
            // finding where volume clusters, it is describing the chart.
            var chosen = at.GetProperty(name).GetProperty("shelves").GetInt32();

            Assert.True(chosen > 0, $"{name} has no shelf at twice an even share, so the fourth source is absent for it.");
            Assert.True(chosen <= bands / 4, $"{name} has {chosen} shelves of {bands} bands, which names too much of the chart to be a cluster.");

            // One multiple below, the count is at least three times as many and
            // more than a third of the bands.
            Assert.True(
                below.GetProperty(name).GetProperty("shelves").GetInt32() > bands / 3,
                $"{name} at an even share names {below.GetProperty(name).GetProperty("shelves").GetInt32()} bands, which would not show the threshold discriminating.");
        }

        // And one multiple above, most of the names lose their shelf entirely.
        // That is the failure in the other direction, and it is the reason the
        // figure is not simply raised until only the largest band survives.
        Assert.True(
            names.Count(name => above.GetProperty(name).GetProperty("shelves").GetInt32() == 0) >= 3,
            "At three times an even share the names still have shelves, so the upper bound is not shown.");
    }

    [Fact]
    public async Task ASecondProfileRunReplacesRatherThanDuplicating()
    {
        using var store = await WithProfile();

        var before = Query(store, "SELECT COUNT(*), SUM(share_count) FROM volume_profile;");

        await new VolumeProfileBuilder(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("replay-profile-again");

        Assert.Equal(before, Query(store, "SELECT COUNT(*), SUM(share_count) FROM volume_profile;"));
    }

    [Fact]
    public void ADaysVolumeReachesEveryBandItsRangeCoversAndNotOnlyOne()
    {
        // Written after a mutation found the test above wanting.
        //
        // Deleting the spreading outright, so every session's whole volume lands
        // in the band holding its low, left ASessionsVolumeIsSpreadAcross...
        // green. That test asserts the expectation's stated overlaps against the
        // band edges the builder wrote and asserts they sum to the session's
        // range, which are facts about the edges and the arithmetic on them, and
        // neither says the builder used them. The diff caught the mutation, but
        // the test named for the spreading did not, and a test that cannot fail
        // on the thing it is named for is the shape this corpus refuses.
        //
        // So the spreading is asserted where the answer can be computed by hand.
        // Sixty sessions from 100 to 120, one thousand shares each. The window's
        // range is twenty, twenty bands are one wide, every session covers all of
        // them, so each band takes a twentieth of each day: fifty a day, three
        // thousand over sixty days, every band the same. A builder placing the
        // day at one price puts all sixty thousand in one band.
        var series = Enumerable.Range(0, VolumeProfileSeries.Window)
            .Select(day => new ProfileBar(new DateOnly(2026, 1, 1).AddDays(day), 120m, 100m, 1_000))
            .ToArray();

        var bands = VolumeProfileSeries.For(series);

        Assert.Equal(VolumeProfileSeries.Bands, bands.Count);
        Assert.All(bands, band => Assert.Equal(3_000, band.Shares));
        Assert.All(bands, band => Assert.Equal(1m, band.High - band.Low));
        Assert.Equal(60_000, bands.Sum(band => band.Shares));

        // And the counter-case the shelf threshold needs. A name whose volume is
        // evenly spread has every band at exactly an even share and no shelf at
        // all, which is what the decision says it should lose nothing by.
        // see: A heavy volume shelf creates a band of its own and also strengthens one it coincides with
        Assert.All(bands, band => Assert.Equal(1d / VolumeProfileSeries.Bands, band.ShareOfPeriod, 12));
        Assert.DoesNotContain(bands, band => band.ShareOfPeriod >= 2d / VolumeProfileSeries.Bands);
    }

    [Fact]
    public void ANameWithFewerThanSixtySessionsGetsNoProfile()
    {
        // The rule at its source, over constructed input rather than by
        // shortening the fixture. A share of the period carries no denominator
        // on the row it is written on, so a profile over twenty sessions sits in
        // the same table as one over sixty and reads identically.
        var series = Enumerable.Range(0, VolumeProfileSeries.Window)
            .Select(day => new ProfileBar(
                new DateOnly(2026, 1, 1).AddDays(day),
                100m + day,
                90m + day,
                1_000))
            .ToArray();

        Assert.Empty(VolumeProfileSeries.For(series[..^1]));
        Assert.NotEmpty(VolumeProfileSeries.For(series));

        // And at the boundary the shares still sum, which is the property that
        // would otherwise be asserted only over the fixture's three names.
        Assert.Equal(
            series.Sum(bar => bar.Volume),
            VolumeProfileSeries.For(series).Sum(band => band.Shares));
    }

    [Fact]
    public void TheProfileBoundariesTheCommittedFixtureCannotReach()
    {
        // owes: The volume profile boundaries the committed fixture cannot reach
        //
        // Four cases the phase 3 sign-off mutated and found green, each
        // unreachable from four names of committed bars and each documented at
        // length in the source with no test able to fail on it. They are here
        // rather than at a later checkpoint because nothing after phase 3 reads
        // the profile, so no checkpoint produces this evidence and a due point
        // naming one would name a point that produces nothing.

        // One. A session that traded at one price. Its range is zero, so the
        // spreading has no width to divide by and the whole day belongs to the
        // band that holds that price. A builder dividing by the range throws or
        // silently drops the day.
        var flat = Enumerable.Range(0, VolumeProfileSeries.Window)
            .Select(day => new ProfileBar(new DateOnly(2026, 1, 1).AddDays(day), 120m, 100m, 1_000))
            .ToArray();

        flat[30] = new ProfileBar(flat[30].SessionDate, 110m, 110m, 5_000);

        var withFlat = VolumeProfileSeries.For(flat);

        Assert.Equal(VolumeProfileSeries.Bands, withFlat.Count);
        Assert.Equal(flat.Sum(bar => bar.Volume), withFlat.Sum(band => band.Shares));

        var holding = withFlat.Single(band => band.Low <= 110m && 110m < band.High);

        Assert.True(
            holding.Shares > withFlat.Where(band => band != holding).Max(band => band.Shares),
            "the one-price session's volume did not land in the band holding its price.");

        // Two. The top edge takes the window's own high, so the highest session
        // is inside the top band rather than past it. Without this the last
        // band's high is short of the window's high and the day's top slice is
        // lost, which shows up as shares that do not sum.
        Assert.Equal(120m, withFlat[^1].High);
        Assert.Equal(100m, withFlat[0].Low);

        // Three. The largest remainder tie order. Spreading gives fractional
        // shares and the whole must be preserved, so the remainders are ranked
        // and the spare shares handed out in a stated order. With every band
        // owed the same fraction every remainder ties, and the property is that
        // the total is exact rather than that any band wins.
        var odd = Enumerable.Range(0, VolumeProfileSeries.Window)
            .Select(day => new ProfileBar(new DateOnly(2026, 1, 1).AddDays(day), 120m, 100m, 1_003))
            .ToArray();

        var spread = VolumeProfileSeries.For(odd);

        Assert.Equal(odd.Sum(bar => bar.Volume), spread.Sum(band => band.Shares));
        Assert.All(spread, band => Assert.True(band.Shares > 0, "a band took no shares from a full-range day."));

        // Four. The collapsing edge guard. Consecutive edges that round to one
        // price collapse, so a window narrower than twenty times the storage
        // precision produces fewer than twenty bands rather than bands with no
        // width. At the limit, a window that traded at one price for sixty
        // sessions produces one band holding the whole period rather than twenty
        // of zero width or none at all.
        var collapsed = Enumerable.Range(0, VolumeProfileSeries.Window)
            .Select(day => new ProfileBar(new DateOnly(2026, 1, 1).AddDays(day), 100m, 100m, 1_000))
            .ToArray();

        var single = VolumeProfileSeries.For(collapsed);

        Assert.Single(single);
        Assert.Equal(100m, single[0].Low);
        Assert.Equal(100m, single[0].High);
        Assert.Equal(collapsed.Sum(bar => bar.Volume), single[0].Shares);
        Assert.Equal(1d, single[0].ShareOfPeriod, 12);

        // And between the two, a window narrow enough for some edges to collapse
        // and not all. Fewer bands than twenty, every band a real width, and the
        // shares still summing to the period, which is the property the whole
        // guard exists to keep.
        var narrow = Enumerable.Range(0, VolumeProfileSeries.Window)
            .Select(day => new ProfileBar(new DateOnly(2026, 1, 1).AddDays(day), 100.0005m, 100m, 1_000))
            .ToArray();

        var few = VolumeProfileSeries.For(narrow);

        Assert.True(
            few.Count is > 1 and < VolumeProfileSeries.Bands,
            $"a window of half a thousandth produced {few.Count} bands, expected between 2 and {VolumeProfileSeries.Bands - 1}.");

        Assert.All(few, band => Assert.True(band.High > band.Low, "a band with no width survived the collapse."));
        Assert.Equal(narrow.Sum(bar => bar.Volume), few.Sum(band => band.Shares));
    }

    [Fact]
    public void ABandAnchoredOnAnAverageAloneStaysSoWhenSessionsReachIt()
    {
        // owes: `has_non_average_anchor` asserted over a band anchored on an average alone that a session reached
        //
        // The case the committed fixture does not hold and SCHEMA calls common:
        // a short moving average follows the price, so a band anchored on one
        // sits at the price about half the time and is reached constantly. Every
        // band in this fixture anchored on an average alone happens to carry no
        // touch, so computing the column over the whole band rather than over
        // the anchors leaves the whole suite green. That is not a gap in the
        // fixture, it is a population unrepresentative in the one way that
        // matters, which is worse because it looks like coverage.
        //
        // 4.4 reads this column to decide whether a band carries a tranche, so
        // the value decides whether a purchase is placed. Constructed rather
        // than fixtured, because a band like this cannot be asked for from
        // captured bars.
        var asOf = new DateOnly(2026, 9, 4);

        // Sixty sessions whose low is the average itself, so the band is reached
        // over and over. A touch is a session high or low inside the band rather
        // than a session that traded through it, which is why the low sits on
        // the price rather than beneath it.
        var window = Enumerable.Range(0, 60)
            .Select(day => new LevelBar(asOf.AddDays(day - 59), 101m, 100m, 100.5m))
            .ToArray();

        // One candidate, and it is an average.
        LevelMember[] candidates = [new(MemberSource.Average, "sma20", 100m, asOf)];

        var bands = LevelSeries.For(window, candidates, close: 105m, mergeDistance: 1m, asOf);
        var band = Assert.Single(bands);

        // The touches arrived. This is what makes the case the one it is: a band
        // computed over its whole membership would now see sixty non-average
        // members and call itself anchored.
        var touches = band.Members.Count(member => member.Source == MemberSource.Touch);

        Assert.True(touches >= 55, $"the constructed band carries {touches} touches, expected at least 55.");
        Assert.Contains(band.Members, member => member.Source == MemberSource.Average);

        // The property. An anchor is what creates a band and a touch is not one,
        // so a band held up by an average and sixty sessions that reached it is
        // still anchored on an average alone.
        Assert.False(
            band.HasNonAverageAnchor,
            "a band whose only anchor is a moving average reported a non-average anchor because " +
            "sessions reached it. 4.4 reads this column to decide whether a tranche is placed.");

        // The counter-reading, so this is not a test that says no to everything.
        // One swing among the candidates and the same sixty sessions, and the
        // band is anchored.
        LevelMember[] withASwing =
        [
            new(MemberSource.Average, "sma20", 100m, asOf),
            new(MemberSource.Swing, "swing low", 100.2m, asOf.AddDays(-30)),
        ];

        var anchored = Assert.Single(LevelSeries.For(window, withASwing, close: 105m, mergeDistance: 1m, asOf));

        Assert.True(anchored.HasNonAverageAnchor);
    }

    // ---- 3.4, the level bands ----

    static async Task<TemporaryStore> WithLevels()
    {
        var store = await WithSwings();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new IndicatorEngine(clock, store.DatabaseFile).RunAsync("replay-indicators");
        await new VolumeProfileBuilder(clock, store.DatabaseFile).RunAsync("replay-profile");
        await new LevelBuilder(clock, store.DatabaseFile).RunAsync("replay-levels");

        return store;
    }

    [Fact]
    public async Task TheLevelBandsMatchTheArithmeticOverTheCommittedBars()
    {
        var expected = Expected("levels");
        var names = expected.GetProperty("namesComputed").EnumerateArray().Select(name => name.GetString()!).ToArray();
        var rows = expected.GetProperty("rows");
        var asOf = expected.GetProperty("asOf");
        var counts = expected.GetProperty("counts");

        using var store = await WithLevels();

        foreach (var ticker in names)
        {
            Assert.Equal(
                rows.GetProperty("byName").GetProperty(ticker).EnumerateArray().Select(row => row.GetString()!),
                Query(store, $"SELECT low_edge, high_edge, role, immediate, strength, has_non_average_anchor FROM level WHERE ticker = '{ticker}' ORDER BY as_of, low_edge;"));

            var count = counts.GetProperty(ticker);

            Assert.Equal(
                [$"{asOf.GetProperty(ticker).GetString()}|{count.GetProperty("bands").GetInt32()}|" +
                 $"{count.GetProperty("support").GetInt32()}|{count.GetProperty("resistance").GetInt32()}|" +
                 $"{count.GetProperty("immediate").GetInt32()}|{count.GetProperty("anchoredOnAnAverageAlone").GetInt32()}"],
                Query(store, $"SELECT as_of, COUNT(*), SUM(role = 'support'), SUM(role = 'resistance'), SUM(immediate), SUM(has_non_average_anchor = 0) FROM level WHERE ticker = '{ticker}' GROUP BY as_of;"));
        }

        // The close roles were assigned against, read off the bar store rather
        // than out of the expectation.
        foreach (var entry in expected.GetProperty("close").GetProperty("byName").EnumerateObject())
        {
            Assert.Equal(
                [entry.Value.GetString()!],
                Query(store, $"SELECT close FROM bar WHERE ticker = '{entry.Name}' ORDER BY session_date DESC LIMIT 1;"));
        }
    }

    [Fact]
    public async Task EveryBandCarriesTheMembersTheRulesPutInIt()
    {
        // The members column is where the evidence lives, and it is what the
        // level table in the report reads out. Diffed whole rather than counted,
        // because a count agrees with a band holding the wrong forty touches.
        var expected = Expected("levels");
        var members = expected.GetProperty("members").GetProperty("byName");

        using var store = await WithLevels();

        foreach (var entry in members.EnumerateObject())
        {
            foreach (var band in entry.Value.EnumerateObject())
            {
                var stored = JsonDocument.Parse(
                    Query(store, $"SELECT members FROM level WHERE ticker = '{entry.Name}' AND low_edge = '{band.Name}';").Single());

                Assert.Equal(
                    band.Value.EnumerateArray().Select(member => member.GetString()!),
                    stored.RootElement.EnumerateArray().Select(member =>
                        $"{member.GetProperty("kind").GetString()}|{member.GetProperty("price").GetString()}|{member.GetProperty("date").GetString()}"));
            }
        }
    }

    [Fact]
    public async Task ATouchSitsInsideItsBandAndTheEdgesAreTheAnchorsAlone()
    {
        // Figure 9.1's third step, asserted over the store: a touch strengthens
        // a band and can never create or widen one. So every band's edges are
        // the lowest and highest of its non-touch members, and every touch sits
        // between them.
        // see: Touches strengthen a band and never create one
        using var store = await WithLevels();

        var rows = Query(store, "SELECT ticker, low_edge, high_edge, members FROM level ORDER BY ticker, low_edge;");

        Assert.True(rows.Count >= 15, $"Read {rows.Count} bands, expected at least 15.");

        var touches = 0;

        foreach (var row in rows)
        {
            var parts = row.Split('|', 4);
            var low = decimal.Parse(parts[1], CultureInfo.InvariantCulture);
            var high = decimal.Parse(parts[2], CultureInfo.InvariantCulture);

            var members = JsonDocument.Parse(parts[3]).RootElement
                .EnumerateArray()
                .Select(member => (
                    Kind: member.GetProperty("kind").GetString()!,
                    Price: decimal.Parse(member.GetProperty("price").GetString()!, CultureInfo.InvariantCulture)))
                .ToArray();

            var anchors = members.Where(member => member.Kind != "touch").ToArray();

            Assert.NotEmpty(anchors);
            Assert.Equal(low, anchors.Min(anchor => anchor.Price));
            Assert.Equal(high, anchors.Max(anchor => anchor.Price));

            foreach (var touch in members.Where(member => member.Kind == "touch"))
            {
                touches++;

                Assert.InRange(touch.Price, low, high);
            }
        }

        // Stated, because a run finding no touch would satisfy every assertion
        // above by having nothing to check.
        Assert.True(touches >= 50, $"Found {touches} touches across the bands, expected at least 50.");
    }

    [Fact]
    public async Task TheWorkedBandsStrengthIsItsMembersPlusItsThreeBonuses()
    {
        // The strength score followed rather than trusted. One point per member,
        // touches included, plus one for a member whose evidence occurred in the
        // last twenty sessions, plus one where a retracement and a swing are
        // both anchors, plus one where the band holds a shelf.
        // see: A member's date is the session its evidence occurred on, and a figure recomputed nightly has none of its own
        var worked = Expected("levels").GetProperty("workedBand");
        var ticker = worked.GetProperty("name").GetString()!;
        var lowEdge = worked.GetProperty("lowEdge").GetString()!;

        using var store = await WithLevels();

        var row = Query(store, $"SELECT high_edge, role, immediate, strength, members FROM level WHERE ticker = '{ticker}' AND low_edge = '{lowEdge}';").Single();
        var parts = row.Split('|', 5);

        Assert.Equal(worked.GetProperty("highEdge").GetString(), parts[0]);
        Assert.Equal(worked.GetProperty("role").GetString(), parts[1]);
        Assert.Equal(worked.GetProperty("immediate").GetInt32().ToString(), parts[2]);
        Assert.Equal(worked.GetProperty("strength").GetInt32().ToString(), parts[3]);

        var members = JsonDocument.Parse(parts[4]).RootElement
            .EnumerateArray()
            .Select(member => member.GetProperty("kind").GetString()!)
            .ToArray();

        Assert.Equal(worked.GetProperty("memberCount").GetInt32(), members.Length);

        // The kinds the expectation names, counted off the stored members. A
        // band whose swings became touches would keep its total and change this.
        foreach (var kind in worked.GetProperty("membersByKind").EnumerateObject())
        {
            Assert.Equal(
                kind.Value.GetInt32(),
                members.Count(member => member.StartsWith(kind.Name, StringComparison.Ordinal)));
        }

        // And the score is the count plus the three bonuses, each of which this
        // band earns: it holds a shelf, it holds a retracement and a swing, and
        // it holds evidence from the last twenty sessions.
        Assert.Equal(members.Length + 3, worked.GetProperty("strength").GetInt32());
    }

    [Fact]
    public async Task ABandAnchoredOnlyByAMovingAverageIsFlaggedAndOneWithAnythingElseIsNot()
    {
        // 3.4's second done condition, asserted in both directions off the
        // stored members rather than by counting flags. The ladder builder reads
        // this on every band, and a flag that is right in one direction only is
        // a flag that says nothing.
        // see: A moving average is a level on the chart and never an anchor for a tranche
        var expected = Expected("levels").GetProperty("counts");

        using var store = await WithLevels();

        var averageOnly = 0;

        foreach (var row in Query(store, "SELECT ticker, has_non_average_anchor, members FROM level ORDER BY ticker, low_edge;"))
        {
            var parts = row.Split('|', 3);
            var flag = parts[1] == "1";

            var anchors = JsonDocument.Parse(parts[2]).RootElement
                .EnumerateArray()
                .Select(member => member.GetProperty("kind").GetString()!)
                .Where(kind => kind != "touch")
                .ToArray();

            var somethingElse = anchors.Any(kind => !kind.StartsWith("sma", StringComparison.Ordinal));

            Assert.Equal(somethingElse, flag);

            if (!flag)
            {
                averageOnly++;

                // The point of the flag: a touch is evidence and never an
                // anchor, so a band held up by an average and forty sessions
                // that reached it is still anchored on an average alone.
                Assert.All(anchors, kind => Assert.StartsWith("sma", kind, StringComparison.Ordinal));
            }
        }

        Assert.Equal(
            expected.EnumerateObject().Sum(name => name.Value.GetProperty("anchoredOnAnAverageAlone").GetInt32()),
            averageOnly);

        // Stated, because a fixture where every band had a swing in it would
        // pass the loop above having asserted the flag in one direction only.
        Assert.True(averageOnly > 0, "No band is anchored on an average alone, so the flag is untested in that direction.");
    }

    [Fact]
    public async Task TheImmediateBandIsTheNearestOnItsSideAndThereIsOnePerSide()
    {
        var expected = Expected("levels");
        var mergeDistance = expected.GetProperty("mergeDistance").GetProperty("byName");

        using var store = await WithLevels();

        foreach (var entry in mergeDistance.EnumerateObject())
        {
            var ticker = entry.Name;

            Assert.Equal(
                ["1|1"],
                Query(store, $"SELECT SUM(immediate AND role = 'support'), SUM(immediate AND role = 'resistance') FROM level WHERE ticker = '{ticker}';"));

            // The nearest support is the one with the highest low edge, and the
            // nearest resistance the one with the lowest.
            Assert.Equal(
                Query(store, $"SELECT MAX(low_edge + 0) FROM level WHERE ticker = '{ticker}' AND role = 'support';"),
                Query(store, $"SELECT low_edge + 0 FROM level WHERE ticker = '{ticker}' AND role = 'support' AND immediate = 1;"));

            Assert.Equal(
                Query(store, $"SELECT MIN(low_edge + 0) FROM level WHERE ticker = '{ticker}' AND role = 'resistance';"),
                Query(store, $"SELECT low_edge + 0 FROM level WHERE ticker = '{ticker}' AND role = 'resistance' AND immediate = 1;"));

            // And no two bands are closer than the merge distance, which is what
            // the merge step is for: two bands that close would be one band.
            var merge = decimal.Parse(entry.Value.GetString()!, CultureInfo.InvariantCulture);

            var edges = Query(store, $"SELECT low_edge, high_edge FROM level WHERE ticker = '{ticker}' ORDER BY low_edge;")
                .Select(row => row.Split('|'))
                .Select(row => (
                    Low: decimal.Parse(row[0], CultureInfo.InvariantCulture),
                    High: decimal.Parse(row[1], CultureInfo.InvariantCulture)))
                .ToArray();

            for (var band = 1; band < edges.Length; band++)
            {
                Assert.True(
                    edges[band].Low - edges[band - 1].High >= merge,
                    $"{ticker} has bands ending at {edges[band - 1].High} and starting at {edges[band].Low}, which is closer than {merge}.");
            }
        }
    }

    [Fact]
    public async Task ASecondLevelRunReplacesRatherThanDuplicating()
    {
        using var store = await WithLevels();

        var before = Query(store, "SELECT COUNT(*), SUM(strength) FROM level;");

        await new LevelBuilder(
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync("replay-levels-again");

        Assert.Equal(before, Query(store, "SELECT COUNT(*), SUM(strength) FROM level;"));
    }

    [Fact]
    public async Task TheThreeRulingsTakenAtThreePointZeroAreObservableInTheBands()
    {
        // The obligation 3.4 carries, discharged.
        // owes: Phase 3's expectations owed for 3.0's rulings
        //
        // 3.0 took three rulings and put their expectations at 3.1, where none
        // of the three was assertable: the indicator engine reads no profile, no
        // swing and no shelf. This is the first checkpoint where all three are
        // observable in one artefact.
        var rulings = Expected("levels").GetProperty("rulings");

        using var store = await WithLevels();

        // The window. A band resting on a shelf holds a member that came from
        // the profile, so the two windows are the same window or the member is a
        // price from a period no other member can see.
        // see: The volume profile accumulates over the same sixty sessions as the level window
        var sessions = rulings.GetProperty("profileWindow").GetProperty("sessions").GetInt32();

        Assert.Equal(VolumeProfileSeries.Window, sessions);

        // The retracement ends. All five land together on one band here, which
        // is what a set drawn between one high and one low looks like; a set
        // drawn between the last two swings of any kind would be a fraction of
        // the distance between two highs.
        // see: A retracement is drawn between the last swing high and the last swing low
        var drawn = rulings.GetProperty("retracementEnds").GetProperty("band");
        var kinds = drawn.GetProperty("kinds").EnumerateArray().Select(kind => kind.GetString()!).ToArray();

        Assert.Equal(LevelSeries.Retracements.Count, kinds.Length);

        var members = JsonDocument.Parse(
            Query(store, $"SELECT members FROM level WHERE ticker = '{drawn.GetProperty("name").GetString()}' AND low_edge = '{drawn.GetProperty("lowEdge").GetString()}';").Single());

        Assert.Equal(
            kinds,
            members.RootElement.EnumerateArray()
                .Select(member => member.GetProperty("kind").GetString()!)
                .Where(kind => kind.StartsWith("retracement", StringComparison.Ordinal))
                .OrderBy(kind => kind, StringComparer.Ordinal));

        // The shelf. A band whose only anchor is a shelf is a band on volume
        // alone, which is what the ruling that a shelf creates a band produces
        // and what the ruling that it only ranks would not.
        // see: A heavy volume shelf creates a band of its own and also strengthens one it coincides with
        var alone = rulings.GetProperty("shelfCreatesABand").GetProperty("bands");

        Assert.True(alone.GetArrayLength() > 0, "No band in the fixture rests on volume alone, so the ruling is not observable here.");

        foreach (var band in alone.EnumerateArray())
        {
            var anchors = JsonDocument.Parse(
                Query(store, $"SELECT members FROM level WHERE ticker = '{band.GetProperty("name").GetString()}' AND low_edge = '{band.GetProperty("lowEdge").GetString()}';").Single());

            var kindsOf = anchors.RootElement.EnumerateArray()
                .Select(member => member.GetProperty("kind").GetString()!)
                .Where(kind => kind != "touch")
                .ToArray();

            Assert.Equal(["shelf"], kindsOf);

            Assert.Equal(
                [$"{band.GetProperty("highEdge").GetString()}|{band.GetProperty("strength").GetInt32()}"],
                Query(store, $"SELECT high_edge, strength FROM level WHERE ticker = '{band.GetProperty("name").GetString()}' AND low_edge = '{band.GetProperty("lowEdge").GetString()}';"));
        }
    }

    [Fact]
    public void OnlyASessionWhoseHighOrLowLandedInABandTouchedIt()
    {
        // Written after a mutation found the test above wanting.
        //
        // Widening a band to the range of all its members, touches included,
        // left the whole suite green. That is because a touch is only added
        // where its price is already inside the anchors' range, so the two
        // ranges are equal by construction and the assertion that the edges are
        // the anchors alone cannot fail. It is a true statement and a tautology,
        // and a tautology is what a test looks like when the property it guards
        // is held by the shape of the code rather than by the line under test.
        //
        // The rule that is not a tautology is which sessions count. The document
        // says any session high or low that reached a band, so a session whose
        // range covers the band without either extreme landing in it did not
        // reach it, and one whose low landed in it did. Both are choices a later
        // session could take the other way.
        // see: Touches strengthen a band and never create one
        var band = new[]
        {
            new LevelMember(MemberSource.Swing, "swing low", 100m, new DateOnly(2026, 1, 5)),
            new LevelMember(MemberSource.Swing, "swing high", 101m, new DateOnly(2026, 1, 6)),
        };

        var window = new[]
        {
            // Straddles the band without either extreme inside it.
            new LevelBar(new DateOnly(2026, 1, 7), 120m, 80m, 110m),
            // Its low lands inside.
            new LevelBar(new DateOnly(2026, 1, 8), 130m, 100.5m, 120m),
            // Nowhere near.
            new LevelBar(new DateOnly(2026, 1, 9), 140m, 130m, 135m),
        };

        // A merge distance of two, so the two swings a point apart are one band
        // running 100 to 101 rather than two bands with nothing between them.
        var levels = LevelSeries.For(window, band, 200m, 2m, new DateOnly(2026, 1, 9));
        var touches = levels.Single(level => level.LowEdge == 100m)
            .Members
            .Where(member => member.Source == MemberSource.Touch)
            .ToArray();

        Assert.Single(touches);
        Assert.Equal(new DateOnly(2026, 1, 8), touches[0].Date);
        Assert.Equal(100.5m, touches[0].Price);

        // And the edges are where the two swings put them, with a session that
        // traded from 80 to 120 having moved neither.
        Assert.Equal(100m, levels.Single().LowEdge);
        Assert.Equal(101m, levels.Single().HighEdge);
    }

    [Fact]
    public void ARetracementIsDrawnFromTheEndTheMoveFinishedAt()
    {
        // The ruling 3.0 took, exercised where the two readings differ. The
        // fractions are not symmetric, so a move that ran up and a move that ran
        // down between the same two prices produce different levels, and taking
        // the wrong end is invisible in a band set.
        // see: A retracement is drawn between the last swing high and the last swing low
        var low = (Price: 100m, Date: new DateOnly(2026, 1, 5));
        var high = (Price: 200m, Date: new DateOnly(2026, 2, 5));

        var upward = LevelSeries.RetracementsBetween(high, low);
        var downward = LevelSeries.RetracementsBetween(high, low with { Date = new DateOnly(2026, 3, 5) });

        Assert.Equal(LevelSeries.Retracements.Count, upward.Count);

        // Up from 100 to 200: the levels sit below the high by each fraction of
        // the hundred point distance.
        Assert.Equal([176.4m, 161.8m, 150m, 138.2m, 121.4m], upward.Select(member => member.Price));

        // Down from 200 to 100: above the low by the same fractions, which is a
        // different set because 23.6 and 78.6 do not sum to one.
        Assert.Equal([123.6m, 138.2m, 150m, 161.8m, 178.6m], downward.Select(member => member.Price));

        // The date is when the move ended, which is the later of the two swings.
        Assert.All(upward, member => Assert.Equal(new DateOnly(2026, 2, 5), member.Date));
        Assert.All(downward, member => Assert.Equal(new DateOnly(2026, 3, 5), member.Date));

        // A window with only one kind of swing produces nothing rather than
        // retracements drawn from a substitute end.
        Assert.Empty(LevelSeries.RetracementsBetween(high, null));
        Assert.Empty(LevelSeries.RetracementsBetween(null, low));
    }

    // ---- the expectation sweep, carried out of the phase 1 sign-off ----

    [Fact]
    public void EveryKeyInEveryExpectationIsReadBySomeTest()
    {
        // The obligation: an expectation nobody reads is the same object as a
        // check that runs nothing, so every file under fixtures named as an
        // expectation is swept for whether a test consults it.
        //
        // Per key rather than per file. A file-level sweep reports four of four
        // and misses the defect it was filed for, because the finding was that
        // the bars expectation is read and something in it is not.
        //
        // A source scan, deliberately. Whether a test consults a key is a fact
        // about the tests rather than about a run, and this reports coverage
        // where the assertions above carry the property.
        var sources = Directory
            .EnumerateFiles(Path.Combine(Repository.Root, "src", "EquityBrief.Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .ToArray();

        Assert.True(sources.Length >= 40, $"Read {sources.Length} test sources, expected at least 40.");

        var text = string.Join("\n", sources.Select(File.ReadAllText));

        var consulted = Regex.Matches(text, @"(?:Try)?GetProperty\(""([^""]+)""")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        var files = Directory.GetFiles(Path.Combine(Folder(), "expectations"), "*.json").OrderBy(file => file).ToArray();

        Assert.True(files.Length >= 5, $"The fixture holds {files.Length} expectation files, expected at least 5.");

        var unread = new List<string>();
        var keys = 0;

        foreach (var file in files)
        {
            var stage = Path.GetFileNameWithoutExtension(file);

            // Opened at all. A file no test names is unread whatever its keys.
            Assert.Contains($"Expected(\"{stage}\")", text, StringComparison.Ordinal);

            foreach (var property in JsonDocument.Parse(File.ReadAllText(file)).RootElement.EnumerateObject())
            {
                keys++;

                if (!consulted.Contains(property.Name))
                {
                    unread.Add($"{stage}.{property.Name}");
                }
            }
        }

        // The scope carrying the property is the keys, and it is floored. The
        // file count above is context: it is a fact about how many stages exist
        // rather than about whether anything reads them.
        Assert.True(keys >= 40, $"Swept {keys} expectation keys, expected at least 40.");

        // Seven stand unread and each is named rather than counted, because a
        // number here would drift silently as keys are added. `rowsInFile` is
        // the count of rows in the captured bulk payload, which the fetch
        // expectation states so a reader can see what the membership filter cut
        // from; `index` is the index code; `facts.frozen` says that nothing in
        // that file is frozen; and the notes are sentences.
        // None is a figure the pipeline produces, which is why nothing asserts
        // them. The series state file added two keys when it landed and this
        // assertion caught both on the same run; the ladder file added its note
        // at 4.1 and it caught that too; the sector note arrived at 5.1 and it
        // caught that. Which is what it is for.
        Assert.Equal(
            ["facts.frozen", "fetch.rowsInFile", "ladder.note", "membership.index", "membership.note", "membership.sectorNote", "series-state.note"],
            unread.OrderBy(name => name, StringComparer.Ordinal));
    }

    [Fact]
    public async Task EveryNamedClosureIsAbsentFromTheStoredSessionsAndEveryOtherWeekdayIsPresent()
    {
        // What the sweep found, and it is sharper than the sweep.
        //
        // `closures` is read, so a per-key sweep reports it covered. It is read
        // only through a count: the derived session figure is the weekdays in
        // the window less the closures, and any weekday substituted for another
        // leaves that arithmetic unmoved. That is why renaming a traded session
        // as a market closure left the whole suite green at the phase 1 sign-off,
        // and it is the defect a coverage report cannot see.
        //
        // So the named dates are tied to the store rather than to a count. Each
        // one has no stored bar, and every other weekday in the window has one.
        var expected = Expected("bars");

        var from = DateOnly.ParseExact(expected.GetProperty("window").GetProperty("from").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(expected.GetProperty("window").GetProperty("to").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var closures = expected.GetProperty("closures")
            .EnumerateArray()
            .Select(day => DateOnly.ParseExact(day.GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet();

        using var store = await Replayed();

        var stored = Query(store, "SELECT DISTINCT session_date FROM bar WHERE ticker = 'AAPL';")
            .Select(day => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToHashSet();

        var traded = new List<DateOnly>();
        var missing = new List<DateOnly>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            // The last session of the window is the day after the stored series
            // ends, because the backfill asks for a year up to the fixture date
            // and the exchange had not traded it. It is excluded rather than
            // asserted either way.
            if (day > stored.Max())
            {
                continue;
            }

            if (closures.Contains(day) && stored.Contains(day))
            {
                traded.Add(day);
            }

            if (!closures.Contains(day) && !stored.Contains(day))
            {
                missing.Add(day);
            }
        }

        Assert.True(closures.Count >= 9, $"The expectation names {closures.Count} closures, expected at least 9.");
        Assert.DoesNotContain(traded, _ => true);
        Assert.DoesNotContain(missing, _ => true);
    }

    // The replayed store the retention check is asserted against, and the five
    // stages re-run over it. Exposed here rather than rebuilt there because a
    // second replay would be a second statement of what a night runs, and the
    // order is the property the night carries.
    internal static Task<TemporaryStore> ReplayedForRetention() => WithLadders();

    internal static async Task RunTheComputedStages(TemporaryStore store)
    {
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new IndicatorEngine(clock, store.DatabaseFile).RunAsync("retention-indicators");
        await new SwingFinder(clock, store.DatabaseFile).RunAsync("retention-swings");
        await new VolumeProfileBuilder(clock, store.DatabaseFile).RunAsync("retention-profile");
        await new LevelBuilder(clock, store.DatabaseFile).RunAsync("retention-levels");
        await new LadderBuilder(clock, store.DatabaseFile).RunAsync(Index, "retention-ladders");
    }

    // ---- 4.3, the calendar ----

    static readonly DateOnly CalendarSession = new(2026, 9, 8);

    static async Task<TemporaryStore> WithCalendar()
    {
        var store = await Replayed();

        await new CalendarFetcher(
            RecordedEarningsCalendarFeed.FromFolder(Folder()),
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, CalendarSession, "replay-calendar");

        return store;
    }

    [Fact]
    public async Task TheCalendarHoldsWhatTheProviderFilesForMembersAndNothingElse()
    {
        var expected = Expected("calendar");
        var counts = expected.GetProperty("counts");

        using var store = await WithCalendar();

        var stored = Query(store, "SELECT ticker, event_date, kind, timing FROM calendar ORDER BY ticker;");

        var rows = expected.GetProperty("rows").GetProperty("stored")
            .EnumerateArray().Select(row => row.GetString()!).OrderBy(row => row, StringComparer.Ordinal);

        Assert.Equal(rows, stored.OrderBy(row => row, StringComparer.Ordinal));
        Assert.Equal(counts.GetProperty("rowsWritten").GetInt32(), stored.Count);

        // The departed constituent is the one the filter has to refuse, and its
        // print is inside the window, so its absence is the filter working
        // rather than the window doing the work.
        Assert.DoesNotContain(stored, row => row.StartsWith("AAL|", StringComparison.Ordinal));

        // Two of the four names have no upcoming print, which is the explicit
        // blank section 18 promises, reached from the fixture rather than
        // constructed. All four have past prints, so what is absent is the date
        // ahead rather than the name.
        var upcoming = expected.GetProperty("detail").GetProperty("upcoming");

        foreach (var name in expected.GetProperty("absent").EnumerateObject()
                     .Where(entry => entry.Name != "note")
                     .Select(entry => entry.Name))
        {
            Assert.False(
                upcoming.TryGetProperty(name, out _),
                $"{name} is named as having no upcoming print and the expectation gives it one.");

            Assert.DoesNotContain(
                stored,
                row => row.StartsWith(name + "|2026-1", StringComparison.Ordinal));
        }

        // What the provider carries beyond the date, read back as stored.
        var detail = expected.GetProperty("detail").GetProperty("byName");

        foreach (var name in detail.EnumerateObject().Select(entry => entry.Name))
        {
            var date = expected.GetProperty("detail").GetProperty("upcoming").GetProperty(name).GetString();

            var written = JsonDocument
                .Parse(Query(store, $"SELECT detail FROM calendar WHERE ticker = '{name}' AND event_date = '{date}';").Single())
                .RootElement;

            Assert.Equal(
                detail.GetProperty(name).GetProperty("periodEnd").GetString(),
                written.GetProperty("periodEnd").GetString());

            Assert.Equal(
                detail.GetProperty(name).GetProperty("estimate").GetString(),
                written.GetProperty("estimate").GetString());

            Assert.Equal(JsonValueKind.Null, written.GetProperty("actual").ValueKind);
        }
    }

    [Fact]
    public async Task TheCalendarCostsOneRequestWhateverTheUniverseSize()
    {
        // The rule the whole design rests on, asserted on the feed's own count
        // rather than on the caller's. A fetcher that looped over members would
        // still report one if the caller wrote the figure.
        var feed = RecordedEarningsCalendarFeed.FromFolder(Folder());

        using var store = await Replayed();

        var outcome = await new CalendarFetcher(
            feed,
            FixedClock.At(Instant, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, CalendarSession, "one-request");

        Assert.Equal(1, feed.Requests);
        Assert.Equal(1, outcome.Requests);

        // And the count does not move with the population. The same feed asked
        // again over a store holding one member answers once more, not once per
        // name, which is the property a per-name path would break.
        Assert.Equal(
            Expected("calendar").GetProperty("counts").GetProperty("notMembers").GetInt32(),
            outcome.NotMembers);
    }

    [Fact]
    public void TheParserReadsTheCaptureAsTheProviderSendsIt()
    {
        // The three things a parser written from the endpoint's name would have
        // got wrong, each asserted against the captured bytes.
        var response = File.ReadAllText(
            Directory.GetFiles(Folder(), RecordedEarningsCalendarFeed.Prefix + "*.json").Single());

        var window = Expected("calendar").GetProperty("window");
        var from = DateOnly.ParseExact(window.GetProperty("from").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(window.GetProperty("to").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var expected = Expected("calendar");
        var events = RecordedEarningsCalendarFeed.Parse(response, from, to);

        Assert.Equal(expected.GetProperty("counts").GetProperty("eventsReturned").GetInt32(), events.Count);

        // The capture's own size, so the window is doing work rather than the
        // file happening to hold only what is wanted.
        Assert.Equal(
            expected.GetProperty("capture").GetProperty("rows").GetInt32(),
            JsonDocument.Parse(response).RootElement.GetProperty("earnings").GetArrayLength());

        // The window's own filter, asserted by asking the parser for a narrower
        // range than the capture holds. The capture was taken over exactly the
        // window a night asks for, so no row in it falls outside: what is under
        // test is the reader rather than the file, and a capture that happened
        // to hold a stale row would be testing the file.
        var refused = expected.GetProperty("refused");

        Assert.False(
            string.IsNullOrWhiteSpace(refused.GetProperty("outsideTheWindow").GetString()),
            "the expectation says nothing about how the window is asserted.");

        var narrow = RecordedEarningsCalendarFeed.Parse(
            response,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31));

        Assert.NotEmpty(narrow);
        Assert.True(narrow.Count < events.Count, "a narrower window returned as much as the whole one.");
        Assert.All(narrow, entry => Assert.InRange(
            entry.EventDate,
            new DateOnly(2026, 10, 1),
            new DateOnly(2026, 10, 31)));

        // And the departed constituent, which the window admits and the member
        // filter refuses, so the two refusals are told apart rather than one
        // standing for the other.
        Assert.Contains(events, entry => entry.Ticker == "AAL");
        Assert.False(string.IsNullOrWhiteSpace(refused.GetProperty("AAL").GetString()));

        // One. The ticker loses its exchange, because the store holds AAPL and
        // the provider sends AAPL.US.
        Assert.All(events, entry => Assert.DoesNotContain('.', entry.Ticker));
        Assert.Contains(events, entry => entry.Ticker == "AAPL");

        // Two. The event date is report_date and never date, which is the
        // fiscal period the report covers. They are a month apart on this
        // capture, so a parser reading the wrong one puts every print early.
        var apple = events.Last(entry => entry.Ticker == "AAPL");

        Assert.Equal(new DateOnly(2026, 10, 29), apple.EventDate);
        Assert.Equal(new DateOnly(2026, 9, 30), apple.PeriodEnd);
        Assert.NotEqual(apple.EventDate, apple.PeriodEnd!.Value);

        // Three. The rows sit under an `earnings` key rather than at the top
        // level, and a payload without it is a different answer rather than a
        // day on which nobody reports.
        Assert.Throws<InvalidOperationException>(
            () => RecordedEarningsCalendarFeed.Parse("{\"type\":\"Earnings\"}", from, to));

        // The timing, which is what the provider files where 4.0 expected a
        // confirmed flag. Both values the capture carries are read, and the
        // absent third is what an unstated row becomes.
        Assert.Equal(EventTiming.After, apple.Timing);
        Assert.Contains(events, entry => entry.Timing == EventTiming.Before);

        Assert.Equal(
            EventTiming.Unstated,
            RecordedEarningsCalendarFeed.Parse(
                "{\"earnings\":[{\"code\":\"ZZ.US\",\"report_date\":\"2026-10-01\",\"before_after_market\":null}]}",
                from,
                to).Single().Timing);
    }

    // ---- 4.1, the trend state and the ladder row ----

    static async Task<TemporaryStore> WithLadders()
    {
        var store = await WithLevels();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        // The calendar first, because the second book is keyed to a dated event
        // and a ladder built before it would carry none. The night runs them in
        // that order for the same reason.
        await new CalendarFetcher(
            RecordedEarningsCalendarFeed.FromFolder(Folder()),
            clock,
            store.DatabaseFile).RunAsync(Index, CalendarSession, "replay-calendar-for-ladders");

        await new LadderBuilder(clock, store.DatabaseFile).RunAsync(Index, "replay-ladders");
        await new MoveAnnotator(clock, store.DatabaseFile).RunAsync("replay-moves");

        return store;
    }

    // The chain through the facts file, which is the ladder chain plus the two
    // components that write and then compare it.
    static async Task<TemporaryStore> WithFacts()
    {
        var store = await WithLadders();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new FactsAssembler(clock, store.DatabaseFile).RunAsync("replay-facts");
        await new ChangeDetector(clock, store.DatabaseFile).RunAsync("replay-changes");

        return store;
    }

    // The chain through the listings, which is the facts chain plus the builder
    // that reads it. Internal because `listings-coverage` runs against the same
    // chain rather than a copy of it: two replays of one pipeline disagree
    // eventually, and the two checks would be the pair.
    internal static async Task<TemporaryStore> WithListings()
    {
        var store = await WithFacts();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new ShortlistBuilder(clock, store.DatabaseFile).RunAsync(Index, "replay-listings");

        return store;
    }

    // The chain through the forward returns and the news pulse, which is the
    // listings chain plus the two components that read them.
    internal static async Task<TemporaryStore> WithReturns()
    {
        var store = await WithListings();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        await new ForwardReturnFiller(clock, store.DatabaseFile).RunAsync("replay-returns");
        await new NewsPulseCounter(
            RecordedNewsFeed.FromFolder(Folder()),
            clock,
            store.DatabaseFile).RunAsync(Index, new DateOnly(2026, 9, 8), "replay-pulse");

        // The detector again, because its retention reads the listings and there
        // were none the first time it ran. This is the order the night takes as
        // well: the shortlist is step 12 and the facts file is step 13.
        await new ChangeDetector(clock, store.DatabaseFile).RunAsync("replay-changes-again");

        return store;
    }

    [Fact]
    public async Task TheTrendStateMatchesTheRuleOverTheCommittedBars()
    {
        var expected = Expected("ladder");
        var states = expected.GetProperty("rows").GetProperty("byName");
        var inputs = expected.GetProperty("inputs").GetProperty("byName");

        using var store = await WithLadders();

        var stored = Query(store, "SELECT ticker, as_of, trend_state FROM ladder ORDER BY ticker;");

        // The population first, and it is the claim this row carries: one row
        // per current index member, not one per name that carries a plan.
        Assert.Equal(FixtureExpectation.CurrentMembers.Length, stored.Count);
        Assert.Equal(expected.GetProperty("counts").GetProperty("rows").GetInt32(), stored.Count);

        foreach (var name in FixtureExpectation.CurrentMembers)
        {
            var asOf = expected.GetProperty("asOf").GetProperty(name).GetString();

            Assert.Contains($"{name}|{asOf}|{states.GetProperty(name).GetString()}", stored);
        }

        // The counts by state, so a run that got every label wrong in the same
        // direction is not a run that matched four strings.
        var counts = expected.GetProperty("counts");

        foreach (var state in TrendState.All)
        {
            var key = state == TrendState.NotClassified
                ? "notClassified"
                : state;

            Assert.Equal(
                counts.GetProperty(key).GetInt32(),
                stored.Count(row => row.EndsWith("|" + state, StringComparison.Ordinal)));
        }

        // The departed constituents get no row. They are in the membership
        // payload and not in the index tonight, and a row for one would be a
        // plan for a name the tool no longer covers.
        foreach (var departed in FixtureExpectation.Departed)
        {
            Assert.DoesNotContain(stored, row => row.StartsWith(departed + "|", StringComparison.Ordinal));
        }

        // And the figures the rule read, so a disagreement is traceable to an
        // input rather than to the answer. The averages are compared as the
        // store holds them, which is REAL, against the four places the
        // expectation quotes.
        foreach (var name in FixtureExpectation.CurrentMembers)
        {
            var row = inputs.GetProperty(name);
            var asOf = expected.GetProperty("asOf").GetProperty(name).GetString();

            var read = Query(
                store,
                "SELECT name, value FROM indicator " +
                $"WHERE ticker = '{name}' AND as_of_placeholder ORDER BY name;"
                    .Replace("as_of_placeholder", $"session_date = '{asOf}' AND name IN ('sma50', 'sma200')", StringComparison.Ordinal));

            Assert.Equal(2, read.Count);

            foreach (var average in new[] { "sma50", "sma200" })
            {
                var stated = decimal.Parse(row.GetProperty(average).GetString()!, CultureInfo.InvariantCulture);
                var value = decimal.Parse(read.Single(entry => entry.StartsWith(average + "|", StringComparison.Ordinal)).Split('|')[1], CultureInfo.InvariantCulture);

                Assert.Equal(stated, Math.Round(value, 4));
            }
        }
    }

    [Fact]
    public async Task EveryLadderRowCarriesAPlanAndAnEmptyOneSaysWhy()
    {
        // An empty plan stating its reason and an absent plan are different
        // objects, and only the first is readable on a screen.
        //
        // Written at 4.1 when every plan was empty, and it asserted that. From
        // 4.4 the plans carry tranches, so what it asserts is the pairing: a
        // plan with no tranche has a reason, and one with tranches has an
        // invalidation. A test left asserting emptiness would have gone green on
        // a builder that placed nothing.
        using var store = await WithLadders();

        var plans = Query(store, "SELECT ticker, plan FROM ladder ORDER BY ticker;");

        Assert.Equal(FixtureExpectation.CurrentMembers.Length, plans.Count);

        var placed = 0;

        foreach (var row in plans)
        {
            var plan = JsonDocument.Parse(row.Split('|', 2)[1]).RootElement;
            var tranches = plan.GetProperty("tranches").EnumerateArray().ToArray();
            var reason = plan.GetProperty("reason").GetString();

            Assert.False(string.IsNullOrWhiteSpace(reason), $"{row.Split('|')[0]} carries a plan with no reason.");

            if (tranches.Length == 0)
            {
                Assert.Equal(JsonValueKind.Null, plan.GetProperty("invalidation").ValueKind);

                continue;
            }

            placed++;

            // A plan with tranches has a price the whole thing is wrong below,
            // and every tranche carries a condition. A tranche with neither
            // would be a purchase with no exit and no trigger.
            Assert.Equal(JsonValueKind.String, plan.GetProperty("invalidation").ValueKind);

            Assert.All(tranches, tranche => Assert.False(
                string.IsNullOrWhiteSpace(tranche.GetProperty("condition").GetString())));
        }

        // The population that carries the property is the plans with tranches,
        // and it is stated: a run where nothing was placed would satisfy every
        // assertion above.
        Assert.Equal(FixtureExpectation.Names.Length, placed);
    }

    [Fact]
    public async Task AMemberWithNoStoredBarsStillGetsARow()
    {
        // Found by mutating, in the checkpoint that wrote the code, which is
        // what done condition 9 is for.
        //
        // Narrowing the ladder's population from the index's members to the
        // members with stored bars left the whole suite green, because every
        // current member of this fixture has a year. The two populations cannot
        // differ over it, so the rule the ladder row exists for was asserted
        // against a fixture that cannot tell it from a narrower one. That is the
        // same shape as the band anchored on an average alone with no touch: not
        // a gap, a population unrepresentative in the one way that matters.
        //
        // A member with no bars is not exotic. It is a name that joined the
        // index tonight, before the backfill has run for it, which is the case
        // the backfill exists for.
        using var store = await WithLevels();

        const string joiner = "NEWCO";

        await using (var connection = new SqliteConnection($"Data Source={store.DatabaseFile}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();

            command.CommandText =
                @"INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at)
                  VALUES ($index, $ticker, '2026-09-04', NULL, '2026-09-04T21:10:00Z');";

            command.Parameters.AddWithValue("$index", Index);
            command.Parameters.AddWithValue("$ticker", joiner);

            await command.ExecuteNonQueryAsync();
        }

        await new LadderBuilder(FixedClock.At(Instant, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, "replay-ladders-joiner");

        var rows = Query(store, "SELECT ticker, trend_state FROM ladder ORDER BY ticker;");

        // The population is the index, so the joiner is in it and the count is
        // one more than the names with bars.
        Assert.Equal(FixtureExpectation.CurrentMembers.Length + 1, rows.Count);
        Assert.Contains($"{joiner}|{TrendState.NotClassified}", rows);

        // And the row says why, rather than carrying a label the rule could not
        // reach. A name with no bars has no close to compare and no swings to
        // read, and answering range for it would answer a question nothing
        // asked.
        var plan = Query(store, $"SELECT plan FROM ladder WHERE ticker = '{joiner}';").Single();

        Assert.Contains("no stored bars", plan, StringComparison.Ordinal);
    }

    // ---- 4.4, the tranches, the stops and the invalidation ----

    [Fact]
    public async Task TheTranchesAndStopsMatchTheRulesOverTheCommittedBands()
    {
        var expected = Expected("ladder");
        var tranches = expected.GetProperty("tranches").GetProperty("byName");
        var invalidation = expected.GetProperty("invalidation").GetProperty("byName");
        var counts = expected.GetProperty("counts").GetProperty("tranches");

        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            var placed = plan.GetProperty("tranches").EnumerateArray()
                .Select(tranche => string.Join(
                    "|",
                    tranche.GetProperty("lowEdge").GetString(),
                    tranche.GetProperty("highEdge").GetString(),
                    tranche.GetProperty("condition").GetString(),
                    tranche.GetProperty("stop").GetString() ?? string.Empty))
                .ToArray();

            Assert.Equal(
                tranches.GetProperty(name).EnumerateArray().Select(row => row.GetString()).ToArray(),
                placed);

            Assert.Equal(counts.GetProperty(name).GetInt32(), placed.Length);

            Assert.Equal(
                invalidation.GetProperty(name).GetString(),
                plan.GetProperty("invalidation").GetString());
        }

        // The bands below the price that carry no tranche, named per name. AAPL's
        // 283.439 is its 200-day average: it carries no tranche and is the first
        // tranche's stop, which is the rule doing both things at once.
        var skipped = expected.GetProperty("skipped").GetProperty("byName");

        foreach (var name in FixtureExpectation.Names)
        {
            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            var edges = plan.GetProperty("tranches").EnumerateArray()
                .Select(tranche => tranche.GetProperty("lowEdge").GetString()!)
                .ToArray();

            foreach (var edge in skipped.GetProperty(name).EnumerateArray().Select(row => row.GetString()!))
            {
                var price = edge.Split(' ')[0];

                Assert.DoesNotContain(price, edges);
            }
        }

        Assert.Contains(
            "283.439",
            JsonDocument.Parse(Query(store, "SELECT plan FROM ladder WHERE ticker = 'AAPL';").Single())
                .RootElement.GetProperty("tranches").EnumerateArray().First()
                .GetProperty("stop").GetString()!,
            StringComparison.Ordinal);

        // At most three, which is section 17's count, over the two names that
        // have more eligible bands than that.
        Assert.All(
            FixtureExpectation.Names,
            name => Assert.True(counts.GetProperty(name).GetInt32() <= LadderSeries.MostTranches));
    }

    // ---- 4.5, the exits, the near-exit skip and the trailing stop ----

    [Fact]
    public async Task TheExitsAndTheNearExitSkipMatchTheRulesOverTheCommittedBands()
    {
        var expected = Expected("ladder").GetProperty("exits");
        var byName = expected.GetProperty("byName");
        var counts = Expected("ladder").GetProperty("counts");

        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            var exits = plan.GetProperty("exits").EnumerateArray()
                .Select(exit => string.Join(
                    "|",
                    exit.GetProperty("lowEdge").GetString(),
                    exit.GetProperty("highEdge").GetString(),
                    exit.GetProperty("traded").GetBoolean() ? "true" : "false",
                    exit.GetProperty("trailing").GetBoolean() ? "true" : "false",
                    exit.GetProperty("fraction").GetString()))
                .ToArray();

            Assert.Equal(
                byName.GetProperty(name).EnumerateArray().Select(row => row.GetString()).ToArray(),
                exits);

            Assert.Equal(counts.GetProperty("exits").GetProperty(name).GetInt32(), exits.Length);
            Assert.True(exits.Length <= LadderSeries.MostExits);
        }

        // Where the trailing rule bites, named per name in the expectation and
        // read back off the store. KEYS is the only tranche in this fixture
        // whose stop the trailing rule moves, and stating that is what keeps a
        // rule that changed nothing from reading as a rule that works.
        var stops = Expected("ladder").GetProperty("stops").GetProperty("whereTheTrailingRuleBites");

        Assert.Contains("293.55", stops.GetProperty("KEYS").GetString()!, StringComparison.Ordinal);

        var keys = JsonDocument
            .Parse(Query(store, "SELECT plan FROM ladder WHERE ticker = 'KEYS';").Single())
            .RootElement;

        Assert.Equal(
            "293.55",
            keys.GetProperty("tranches").EnumerateArray().First().GetProperty("stop").GetString());

        // A skipped exit is listed with its reason rather than omitted, which is
        // the half a rule that only filtered would lose.
        foreach (var name in new[] { "MSFT", "NFLX" })
        {
            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            var skipped = plan.GetProperty("exits").EnumerateArray()
                .Where(exit => !exit.GetProperty("traded").GetBoolean())
                .ToArray();

            Assert.NotEmpty(skipped);
            Assert.All(skipped, exit => Assert.Contains(
                "typical days",
                exit.GetProperty("reason").GetString()!,
                StringComparison.Ordinal));

            // And it takes none of the position, which is what listed and not
            // traded means: a row that carried a fraction would be an exit.
            Assert.All(skipped, exit => Assert.Equal("0", exit.GetProperty("fraction").GetString()));
        }

        // MSFT trades none of its exits at all, so it has no top of the ladder.
        // A plan saying take nothing here is a different object from one with a
        // trailing rule attached to nothing.
        var msft = JsonDocument
            .Parse(Query(store, "SELECT plan FROM ladder WHERE ticker = 'MSFT';").Single())
            .RootElement;

        Assert.Equal(0, counts.GetProperty("tradedExits").GetProperty("MSFT").GetInt32());
        Assert.DoesNotContain(
            msft.GetProperty("exits").EnumerateArray(),
            exit => exit.GetProperty("trailing").GetBoolean());

        // Exactly one trailing rule where anything is traded, and it is the
        // highest traded exit rather than the highest exit.
        foreach (var name in new[] { "AAPL", "KEYS", "NFLX" })
        {
            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            var traded = plan.GetProperty("exits").EnumerateArray()
                .Where(exit => exit.GetProperty("traded").GetBoolean())
                .ToArray();

            Assert.Single(traded, exit => exit.GetProperty("trailing").GetBoolean());
            Assert.True(traded[^1].GetProperty("trailing").GetBoolean());

            // The fractions are equal and sum to the whole of what is held.
            Assert.Single(traded.Select(exit => exit.GetProperty("fraction").GetString()).Distinct());
            Assert.Equal($"1/{traded.Length}", traded[0].GetProperty("fraction").GetString());
        }
    }

    // ---- 4.7, the event book ----

    [Fact]
    public async Task TheEventSetupsMatchTheRulesAndANameWithNoDateGetsNone()
    {
        var expected = Expected("ladder").GetProperty("events");
        var byName = expected.GetProperty("byName");
        var counts = Expected("ladder").GetProperty("counts").GetProperty("events");

        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            var events = plan.GetProperty("events").EnumerateArray()
                .Select(setup => string.Join(
                    "|",
                    setup.GetProperty("name").GetString(),
                    setup.GetProperty("eventDate").GetString(),
                    setup.GetProperty("entry").GetString(),
                    setup.GetProperty("stop").GetString(),
                    setup.GetProperty("target").GetString()))
                .ToArray();

            Assert.Equal(
                byName.GetProperty(name).EnumerateArray().Select(row => row.GetString()).ToArray(),
                events);

            Assert.Equal(counts.GetProperty(name).GetInt32(), events.Length);

            // Every figure in the book is a proposal and every setup says so,
            // which is what keeps twelve invented numbers from later reading as
            // measured ones.
            Assert.All(events.Length == 0 ? [] : plan.GetProperty("events").EnumerateArray(),
                setup => Assert.True(setup.GetProperty("proposal").GetBoolean()));

            // And each carries all four of trigger, entry, stop and target,
            // which is the shape figure 10.1 states and the one thing about
            // these setups that is not a proposal.
            Assert.All(plan.GetProperty("events").EnumerateArray(), setup =>
            {
                Assert.False(string.IsNullOrWhiteSpace(setup.GetProperty("trigger").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(setup.GetProperty("entry").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(setup.GetProperty("stop").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(setup.GetProperty("target").GetString()));
            });
        }

        // The two names with no dated event get no setups and the plan says why,
        // rather than producing them from a guessed date. This is the population
        // that makes the rule assertable: two of four have a date and two do not.
        foreach (var name in expected.GetProperty("absent").EnumerateObject().Select(entry => entry.Name))
        {
            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            Assert.Empty(plan.GetProperty("events").EnumerateArray());
            Assert.Contains("no dated event", plan.GetProperty("reason").GetString()!, StringComparison.Ordinal);
        }

        // The second book never merges with the position book, and what that
        // means is structural rather than a matter of prices: a setup carries
        // its own stop and its own target and takes no part in the position's
        // invalidation. Sharing a price with a tranche is expected, because both
        // read the same band, and AAPL's flush enters at the low edge its first
        // tranche sits on.
        var apple = JsonDocument
            .Parse(Query(store, "SELECT plan FROM ladder WHERE ticker = 'AAPL';").Single())
            .RootElement;

        var invalidation = apple.GetProperty("invalidation").GetString();

        Assert.All(apple.GetProperty("events").EnumerateArray(), setup =>
        {
            // A setup's own stop, which is not the position's invalidation.
            Assert.NotEqual(invalidation, setup.GetProperty("stop").GetString());

            // And a target, which no tranche has: a tranche is bought and held
            // to the exits, and a setup is a trade with an end.
            Assert.False(string.IsNullOrWhiteSpace(setup.GetProperty("target").GetString()));
        });

        Assert.All(
            apple.GetProperty("tranches").EnumerateArray(),
            tranche => Assert.False(tranche.TryGetProperty("target", out _)));
    }

    // ---- 4.8, the arithmetic and the earnings rule ----

    [Fact]
    public async Task TheArithmeticIsDerivedFromThePlanAndMatchesTheHandDerivation()
    {
        var expected = Expected("ladder").GetProperty("arithmetic").GetProperty("byName");

        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var figures = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement.GetProperty("arithmetic");

            var stated = expected.GetProperty(name);

            if (stated.TryGetProperty("absent", out var absent))
            {
                Assert.Equal(absent.GetString(), figures.GetProperty("absent").GetString());

                continue;
            }

            Assert.Equal(JsonValueKind.Null, figures.GetProperty("absent").ValueKind);

            foreach (var key in stated.EnumerateObject().Select(entry => entry.Name))
            {
                Assert.Equal(
                    stated.GetProperty(key).GetString(),
                    figures.GetProperty(key).GetString());
            }
        }

        // Nothing here is stored. Every figure is a function of prices the row
        // already carries, which is what keeps the report and the score from
        // being two implementations of one arithmetic.
        // see: Code owns every number
        var apple = JsonDocument
            .Parse(Query(store, "SELECT plan FROM ladder WHERE ticker = 'AAPL';").Single())
            .RootElement;

        var tranche = apple.GetProperty("tranches").EnumerateArray().First();
        var low = decimal.Parse(tranche.GetProperty("lowEdge").GetString()!, CultureInfo.InvariantCulture);
        var high = decimal.Parse(tranche.GetProperty("highEdge").GetString()!, CultureInfo.InvariantCulture);
        var stop = decimal.Parse(tranche.GetProperty("stop").GetString()!, CultureInfo.InvariantCulture);

        var midpoint = (low + high) / 2;

        Assert.Equal(
            midpoint.ToString(CultureInfo.InvariantCulture),
            apple.GetProperty("arithmetic").GetProperty("firstEntry").GetString());

        Assert.Equal(
            (midpoint - stop).ToString(CultureInfo.InvariantCulture),
            apple.GetProperty("arithmetic").GetProperty("firstRisk").GetString());
    }

    [Fact]
    public void TheBreakEvenIsWhatTheRatioDemandsOverWorkedCases()
    {
        // The arithmetic asserted against cases computed by hand rather than
        // against the code's own output, which is 4.8's done condition. A plan
        // risking one to make two is worth taking if it is right more than a
        // third of the time, and that is a number rather than a preference.
        // see: A condition is judged against the break-even its own plan demands
        Assert.Equal(0.5m, LadderSeries.BreakEvenOf(1m));
        Assert.Equal(1m / 3m, LadderSeries.BreakEvenOf(2m));
        Assert.Equal(0.25m, LadderSeries.BreakEvenOf(3m));

        // A plan whose reward is a fraction of its risk demands more than half,
        // which is AAPL's case in this fixture: its first tranche's ratio is
        // 0.9292 and it needs to be right 51.83 per cent of the time.
        var demanding = LadderSeries.BreakEvenOf(0.9292m);

        Assert.NotNull(demanding);
        Assert.True(demanding > 0.5m, $"a ratio below one demanded {demanding}, which is not more than half.");
        Assert.Equal(0.5183m, Math.Round(demanding!.Value, 4, MidpointRounding.AwayFromZero));

        // A ratio of nothing has no break-even rather than a break-even of one,
        // because a plan with no reward is not a plan that always loses: it is
        // one nothing can be said about.
        Assert.Null(LadderSeries.BreakEvenOf(null));
        Assert.Null(LadderSeries.BreakEvenOf(0m));

        // And the worked example section 13 states, which is the one figure in
        // this arithmetic the corpus already carries: entry near 920, stop below
        // 855, first traded target 1057, risking 65 to make 137, breaking even
        // at about 32 per cent.
        var worked = LadderSeries.BreakEvenOf(137m / 65m);

        Assert.NotNull(worked);
        Assert.Equal(0.32m, Math.Round(worked!.Value, 2, MidpointRounding.AwayFromZero));
    }

    [Fact]
    public async Task TheEarningsRuleStatesTheLastTwoPrintsAgainstTheStopDistance()
    {
        var expected = Expected("ladder").GetProperty("earningsRule");
        var byName = expected.GetProperty("byName");

        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var prints = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement.GetProperty("earningsRule").EnumerateArray()
                .Select(print => string.Join(
                    "|",
                    print.GetProperty("eventDate").GetString(),
                    print.GetProperty("session").GetString(),
                    print.GetProperty("move").GetString(),
                    print.GetProperty("shareOfStop").GetString() ?? string.Empty))
                .ToArray();

            Assert.Equal(
                byName.GetProperty(name).EnumerateArray().Select(row => row.GetString()).ToArray(),
                prints);

            // Two, which is what the rule states, for every name. All four have
            // past prints whether or not they have one ahead, so this is the
            // population the rule is about rather than a subset of it.
            Assert.Equal(LadderSeries.PrintsStated, prints.Length);
        }

        // The session is the one the print moved, which is the next session for a
        // report after the close. Every print in this fixture lands after the
        // close, and each is read off the session following its date rather than
        // its own.
        var apple = JsonDocument
            .Parse(Query(store, "SELECT plan FROM ladder WHERE ticker = 'AAPL';").Single())
            .RootElement.GetProperty("earningsRule").EnumerateArray().Last();

        Assert.Equal("2026-07-30", apple.GetProperty("eventDate").GetString());
        Assert.Equal("2026-07-31", apple.GetProperty("session").GetString());

        // And MSFT, which trades no exit and so has no reward to measure, still
        // has a stop and two prints measured against it. The two figures are
        // independent and a rule that took the stop distance from the arithmetic
        // would have left MSFT's share empty.
        var msft = JsonDocument
            .Parse(Query(store, "SELECT plan FROM ladder WHERE ticker = 'MSFT';").Single())
            .RootElement;

        Assert.NotNull(msft.GetProperty("arithmetic").GetProperty("absent").GetString());
        Assert.All(
            msft.GetProperty("earningsRule").EnumerateArray(),
            print => Assert.False(string.IsNullOrWhiteSpace(print.GetProperty("shareOfStop").GetString())));
    }

    [Fact]
    public void TheEarningsRuleReadsTheSessionTheTimingDecides()
    {
        // The timing column earning its place. A report before the open moves
        // that day's bar and one after the close moves the next, and the same
        // date with the two timings reads two different sessions.
        var start = new DateOnly(2026, 9, 1);

        LadderBar Bar(int day, decimal close) => new(start.AddDays(day), close, close, close);

        LadderBar[] sessions = [Bar(0, 100m), Bar(1, 102m), Bar(2, 110m), Bar(3, 111m)];

        var print = new DateOnly(2026, 9, 2);

        var before = LadderSeries.EarningsRuleFor([(print, EventTiming.Before)], sessions, 4m).Single();
        var after = LadderSeries.EarningsRuleFor([(print, EventTiming.After)], sessions, 4m).Single();

        // Before the open: the print's own session, 102 against the 100 before it.
        Assert.Equal(new DateOnly(2026, 9, 2), before.Session);
        Assert.Equal(2m, before.Move);

        // After the close: the next session, 110 against the 102 before it.
        Assert.Equal(new DateOnly(2026, 9, 3), after.Session);
        Assert.Equal(8m, after.Move);

        // A timing the provider left unstated reads as the print's own day,
        // which is the reading that assumes least.
        Assert.Equal(before.Session, LadderSeries.EarningsRuleFor([(print, EventTiming.Unstated)], sessions, 4m).Single().Session);

        // The move is a distance rather than a direction, because what is being
        // decided is whether the position can take it.
        LadderBar[] falling = [Bar(0, 100m), Bar(1, 102m), Bar(2, 90m), Bar(3, 91m)];

        Assert.Equal(12m, LadderSeries.EarningsRuleFor([(print, EventTiming.After)], falling, 4m).Single().Move);

        // And the share is against the stop distance, so a print that moved the
        // name further than the plan risks reads above one.
        Assert.Equal(3m, LadderSeries.EarningsRuleFor([(print, EventTiming.After)], falling, 4m).Single().ShareOfStop);

        // A plan with no stop states the move and no share, rather than a share
        // of nothing.
        Assert.Null(LadderSeries.EarningsRuleFor([(print, EventTiming.After)], falling, null).Single().ShareOfStop);
    }

    [Fact]
    public void EverySetupHasAPositiveRewardAndAStopOutsideTheNoise()
    {
        // Two arrangements of these rules did not manage this, and both were
        // found by deriving the figures before running the code rather than by
        // freezing what the code produced.
        //
        // A stop at a zero-width band's low edge is the entry, so the setup
        // could never win and would stop out on the session that triggered it.
        // And a target taken as the next band can sit below a gap-up entry,
        // which is a target the setup starts past.
        var asOf = new DateOnly(2026, 9, 4);

        // A single-price resistance band, which is what most of them are, and a
        // second one close above it.
        List<Level> bands =
        [
            new(90m, 92m, LevelSeries.Support, false, 1, true, []),
            new(101m, 101m, LevelSeries.Resistance, true, 1, true, []),
            new(102m, 102m, LevelSeries.Resistance, false, 1, true, []),
        ];

        var recent = Enumerable.Range(0, 10)
            .Select(day => new LadderBar(asOf.AddDays(day - 9), 100m, 99m, 99.5m))
            .ToArray();

        var setups = LadderSeries.EventsFor(bands, close: 99.5m, typicalMove: 2m, asOf.AddDays(30), recent);

        Assert.Equal(3, setups.Count);

        foreach (var setup in setups)
        {
            Assert.True(
                setup.Target > setup.Entry,
                $"{setup.Name} targets {setup.Target} from an entry of {setup.Entry}, which is a setup that starts past its own target.");

            Assert.True(
                setup.Stop < setup.Entry,
                $"{setup.Name} stops at {setup.Stop} from an entry of {setup.Entry}.");

            // And the stop is outside a typical day, which is the rule the
            // position book's stops already follow.
            Assert.True(
                setup.Entry - setup.Stop >= 2m,
                $"{setup.Name} risks {setup.Entry - setup.Stop} against a typical day of 2, which is inside the noise.");
        }

        // The gap up is the one whose target has to be looked for rather than
        // taken: its entry is above the band it cleared, so the band immediately
        // above that band is below its entry.
        var gap = setups.Single(setup => setup.Name.Contains("gap up", StringComparison.Ordinal));

        Assert.Equal(103m, gap.Entry);

        // No band sits above the gap entry, so the target is two typical days
        // above it rather than the band at 102, which is below the entry.
        Assert.Equal(107m, gap.Target);

        // A name with no date gets none of them, over the same bands, so the
        // absence is the date deciding rather than the bands.
        Assert.Empty(LadderSeries.EventsFor(bands, 99.5m, 2m, null, recent));
    }

    [Fact]
    public void TheTrailingStopRisesWithTheStructureAndIsNeverLooserThanTheRangeRule()
    {
        // owes: The trend-dependent stop, which trails in an uptrend rather than sitting at the next band
        //
        // The rule read literally puts the stop wherever the last swing low
        // happens to be, which on a name that has run a long way is far below
        // the band beneath and is looser protection than the range rule gives.
        // A trailing stop that can sit below the range floor is not trailing
        // anything, so the stop is the higher of the two.
        // see: The trailing stop is the higher of the band beneath and the last swing low
        var band = new Level(90m, 95m, LevelSeries.Support, false, 1, true, []);

        // A higher low has formed above the band beneath, so the stop rises to
        // it. This is KEYS's case in the fixture and the only one there.
        Assert.Equal(88m, LadderSeries.StopFor(band, beneath: 85m, TrendState.Uptrend, [70m, 88m]));

        // The last swing low is below the band beneath, so the band wins. This
        // is MSFT's third tranche, whose last swing low is a hundred points down.
        Assert.Equal(85m, LadderSeries.StopFor(band, beneath: 85m, TrendState.Uptrend, [70m]));

        // No band beneath at all, so the trailing rule gives a stop where the
        // range rule gives none. This is what AAPL's and KEYS's lowest tranches
        // take.
        Assert.Equal(70m, LadderSeries.StopFor(band, beneath: null, TrendState.Uptrend, [70m]));

        // A range takes the band whatever the swings did, which is the rule the
        // trend is deciding between.
        Assert.Equal(85m, LadderSeries.StopFor(band, beneath: 85m, TrendState.Range, [88m]));
        Assert.Null(LadderSeries.StopFor(band, beneath: null, TrendState.Range, [88m]));

        // A swing low inside the band is not beneath it and cannot be the stop:
        // a stop inside the zone being bought is not a stop, which is why this
        // was carried out of 4.4 rather than written as a line.
        Assert.Equal(85m, LadderSeries.StopFor(band, beneath: 85m, TrendState.Uptrend, [92m]));
    }

    [Fact]
    public void ABandAnchoredOnAnAverageAloneCarriesNoTrancheAndIsStillAStop()
    {
        // The column 4.0 constructed a band for, now read by the rule that
        // decides whether a purchase is placed. Both halves are asserted,
        // because the band does two things at once: it carries no tranche and it
        // is still the stop of the tranche above it.
        // see: A moving average is a level on the chart and never an anchor for a tranche
        var asOf = new DateOnly(2026, 9, 4);

        Level Band(decimal low, decimal high, bool anchored) =>
            new(low, high, LevelSeries.Support, false, 1, anchored, []);

        List<Level> bands =
        [
            Band(90m, 92m, anchored: true),
            Band(95m, 95m, anchored: false),
            Band(98m, 99m, anchored: true),
        ];

        var flat = Enumerable.Range(0, LadderSeries.ConditionLookback)
            .Select(day => new LadderBar(asOf.AddDays(day - 9), 101m, 100m, 100.5m))
            .ToArray();

        var ladder = LadderSeries.For(bands, close: 100m, typicalMove: 1m, flat, TrendState.Range);

        // Two tranches from three eligible bands, and the one missing is the
        // average-only one.
        Assert.Equal(2, ladder.Tranches.Count);
        Assert.DoesNotContain(ladder.Tranches, tranche => tranche.LowEdge == 95m);

        // And it is the stop of the tranche above it, which is the half a rule
        // that only skipped it would lose.
        Assert.Equal(95m, ladder.Tranches[0].Stop);
        Assert.Equal(98m, ladder.Tranches[0].LowEdge);

        // A name whose only eligible band is average-anchored produces no ladder
        // and says why, which is section 18's row.
        var only = LadderSeries.For([Band(95m, 95m, anchored: false)], 100m, 1m, flat, TrendState.Range);

        Assert.Empty(only.Tranches);
        Assert.Contains("moving average", only.Reason!, StringComparison.Ordinal);

        // And a name with no support band below the price at all is a different
        // absence with a different sentence, rather than one standing for both.
        var none = LadderSeries.For([Band(120m, 121m, anchored: true) with { Role = LevelSeries.Resistance }], 100m, 1m, flat, TrendState.Range);

        Assert.Empty(none.Tranches);
        Assert.Contains("no support band sits below the price", none.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactlyOneTrancheConditionMatchesAndTheOrderIsTheOneStated()
    {
        // The prefix shape the verification rules name as having arrived four
        // times: a matcher keyed on the first thing that fits answers about
        // everything that fits. So the order is asserted where two conditions
        // could both be true, in both directions, rather than left to whichever
        // branch is written first.
        var asOf = new DateOnly(2026, 9, 4);
        var band = new Level(90m, 95m, LevelSeries.Support, false, 1, true, []);

        LadderBar Bar(int day, decimal high, decimal low, decimal close) =>
            new(asOf.AddDays(day - 9), high, low, close);

        // Available now wins over a failed breakdown, which is the pair that can
        // both hold: the close is inside the band and a session closed below it.
        var both = Enumerable.Range(0, 10).Select(day => Bar(day, 96m, 85m, day == 2 ? 88m : 92m)).ToArray();

        Assert.Equal(TrancheCondition.AvailableNow, LadderSeries.ConditionFor(band, 92m, 1m, both));

        // With the close above the band the same window is a failed breakdown,
        // so the first branch was deciding rather than the window being empty.
        Assert.Equal(TrancheCondition.FailedBreakdown, LadderSeries.ConditionFor(band, 97m, 1m, both));

        // A shock landing in the band whose low holds the next day, with no
        // close below the band, is the second day after a shock.
        var shock = Enumerable.Range(0, 10)
            .Select(day => day == 5 ? Bar(day, 110m, 94m, 100m) : Bar(day, 101m, 99m, 100m))
            .ToArray();

        Assert.Equal(TrancheCondition.SecondDayAfterAShock, LadderSeries.ConditionFor(band, 100m, 1m, shock));

        // The same window with a move too small to be a shock is the first close
        // back above instead, so the multiple is deciding rather than the dip.
        Assert.Equal(TrancheCondition.FirstCloseBackAbove, LadderSeries.ConditionFor(band, 100m, 20m, shock));

        // And a window that never came near the band is none of the four.
        var away = Enumerable.Range(0, 10).Select(day => Bar(day, 120m, 118m, 119m)).ToArray();

        Assert.Equal(TrancheCondition.ReachesTheZone, LadderSeries.ConditionFor(band, 119m, 1m, away));

        // The third pair that can both hold, added at 5.0: a window carrying
        // both a close below the band and a shock whose low held. A failed
        // breakdown is tested before a shock and wins, and it is the pair the
        // committed fixture reaches from neither side, since `ladder.json`
        // carries no tranche in either condition.
        var breakdownAndShock = Enumerable.Range(0, 10)
            .Select(day => day switch
            {
                2 => Bar(day, 89m, 87m, 88m),
                5 => Bar(day, 110m, 94m, 100m),
                _ => Bar(day, 101m, 99m, 100m),
            })
            .ToArray();

        Assert.Equal(
            TrancheCondition.FailedBreakdown,
            LadderSeries.ConditionFor(band, 97m, 1m, breakdownAndShock));

        // The same window without the breakdown is the shock, so the order is
        // asserted in both directions rather than the shock being unreachable
        // from this arrangement.
        var shockAlone = breakdownAndShock
            .Select((bar, day) => day == 2 ? Bar(day, 101m, 99m, 100m) : bar)
            .ToArray();

        Assert.Equal(
            TrancheCondition.SecondDayAfterAShock,
            LadderSeries.ConditionFor(band, 97m, 1m, shockAlone));

        // Every condition the enum carries is reachable from the inputs above.
        //
        // This is a redundant assertion rather than a load-bearing one, and 5.0
        // says so rather than removing it: both sides come from production, so
        // swapping two condition kinds inside `ConditionFor` leaves the set over
        // these inputs unchanged and this passes. What catches such a swap is
        // the five assertions above, each against a literal. What this adds is
        // that no member of the enum is unreachable, which is a different
        // property and is worth keeping under its own description.
        //
        // The exclusivity the four branches rest on is asserted by the three
        // overlap pairs above, in both directions, rather than here: the
        // function returns one value, so a set of its answers can say nothing
        // about whether two predicates held at once.
        var reached = new[]
        {
            LadderSeries.ConditionFor(band, 92m, 1m, both),
            LadderSeries.ConditionFor(band, 97m, 1m, both),
            LadderSeries.ConditionFor(band, 100m, 1m, shock),
            LadderSeries.ConditionFor(band, 100m, 20m, shock),
            LadderSeries.ConditionFor(band, 119m, 1m, away),
        };

        Assert.Equal(
            Enum.GetValues<TrancheCondition>().OrderBy(value => value),
            reached.Distinct().OrderBy(value => value));
    }

    [Fact]
    public void ADowntrendAndAnUnclassifiedNameCarryNoTrancheAndSayWhy()
    {
        // The branch the fixture cannot reach: three of its four names are in an
        // uptrend and the fourth is a range, so the state that produces no plan
        // at all is unreachable from these bars.
        // see: The trailing stop is the higher of the band beneath and the last swing low
        var asOf = new DateOnly(2026, 9, 4);
        var bands = new List<Level> { new(90m, 95m, LevelSeries.Support, false, 1, true, []) };
        var flat = Enumerable.Range(0, 10)
            .Select(day => new LadderBar(asOf.AddDays(day - 9), 101m, 99m, 100m))
            .ToArray();

        var down = LadderSeries.For(bands, 100m, 1m, flat, TrendState.Downtrend);
        var unknown = LadderSeries.For(bands, 100m, 1m, flat, TrendState.NotClassified);

        Assert.Empty(down.Tranches);
        Assert.Null(down.Invalidation);
        Assert.Contains("trend is down", down.Reason!, StringComparison.Ordinal);

        Assert.Empty(unknown.Tranches);
        Assert.Contains("could not be classified", unknown.Reason!, StringComparison.Ordinal);

        // The counter-reading, over the same bands: a range places the tranche,
        // so the refusals above are the state deciding rather than the bands
        // being ineligible.
        Assert.Single(LadderSeries.For(bands, 100m, 1m, flat, TrendState.Range).Tranches);
    }

    [Fact]
    public void TheSwingBoundariesTheCommittedFixtureCannotReach()
    {
        // owes: The swing boundaries the committed fixture cannot reach
        //
        // Two cases the phase 3 sign-off mutated and found green, both
        // unreachable from four names of committed bars. They land here rather
        // than at 4.1 because the trend classifier reads swings to ask which of
        // two is later and reaches neither.
        var start = new DateOnly(2026, 9, 1);

        SwingBar Bar(int day, decimal high, decimal low) => new(start.AddDays(day), high, low);

        // One. A plateau is not a peak. Two adjacent sessions sharing a high
        // produce nothing, because marking both would put two swings at one
        // price three days apart and marking one would make the answer depend on
        // which end the scan started from.
        SwingBar[] plateau =
        [
            Bar(0, 10m, 5m), Bar(1, 10m, 5m), Bar(2, 10m, 5m),
            Bar(3, 20m, 5m), Bar(4, 20m, 4m),
            Bar(5, 10m, 5m), Bar(6, 10m, 5m), Bar(7, 10m, 5m),
        ];

        Assert.DoesNotContain(SwingSeries.For(plateau), swing => swing.Direction == SwingSeries.High);

        // The same series with one of the pair a shade lower is a peak, so the
        // strictness is deciding rather than the window being too short.
        var peak = plateau.ToArray();
        peak[4] = Bar(4, 19m, 4m);

        Assert.Contains(SwingSeries.For(peak), swing => swing.Direction == SwingSeries.High && swing.Price == 20m);

        // Two. An outside day is a peak and a trough at once, and SCHEMA's key
        // carries the direction so the two rows sit side by side rather than one
        // overwriting the other.
        SwingBar[] outside =
        [
            Bar(0, 10m, 5m), Bar(1, 10m, 5m), Bar(2, 10m, 5m),
            Bar(3, 20m, 1m),
            Bar(4, 10m, 5m), Bar(5, 10m, 5m), Bar(6, 10m, 5m),
        ];

        var both = SwingSeries.For(outside);

        Assert.Equal(2, both.Count);
        Assert.Contains(both, swing => swing.Direction == SwingSeries.High && swing.Price == 20m);
        Assert.Contains(both, swing => swing.Direction == SwingSeries.Low && swing.Price == 1m);
        Assert.Single(both.Select(swing => swing.SessionDate).Distinct());
    }

    [Fact]
    public void TheTrendRuleAnswersEachOfItsFourStatesOverConstructedInput()
    {
        // The two states the committed bars cannot reach, and the two they can,
        // over input built for the purpose. All four names hold a full year and
        // three of them are in an uptrend, so a downtrend and a name that cannot
        // be classified are unreachable from this fixture, and the branch that
        // produces no tranches at all would go unasserted.
        var asOf = new DateOnly(2026, 9, 4);

        Swing Low(int day, decimal price) => new(asOf.AddDays(-day), SwingSeries.Low, price, asOf.AddDays(-day + 3));
        Swing High(int day, decimal price) => new(asOf.AddDays(-day), SwingSeries.High, price, asOf.AddDays(-day + 3));

        // Uptrend: above the 50, the 50 above the 200, and a higher low.
        Assert.Equal(
            TrendState.Uptrend,
            TrendSeries.For(110m, 100m, 90m, [Low(40, 80m), Low(10, 85m)]).State);

        // Range from the same averages and a lower low, which is the case that
        // separates the two halves of the rule: the averages alone would call it
        // an uptrend.
        Assert.Equal(
            TrendState.Range,
            TrendSeries.For(110m, 100m, 90m, [Low(40, 85m), Low(10, 80m)]).State);

        // Downtrend: below the 50, the 50 below the 200, and a lower high.
        Assert.Equal(
            TrendState.Downtrend,
            TrendSeries.For(90m, 100m, 110m, [High(40, 120m), High(10, 115m)]).State);

        // And the averages disagreeing with each other, which is NFLX's case in
        // the fixture: neither branch holds and the answer is a range whatever
        // the swings did.
        Assert.Equal(
            TrendState.Range,
            TrendSeries.For(110m, 100m, 120m, [Low(40, 85m), Low(10, 80m)]).State);

        // Not classified, once per missing input, each naming what was missing.
        var noLong = TrendSeries.For(110m, 100m, null, [Low(40, 80m), Low(10, 85m)]);
        var noShort = TrendSeries.For(110m, null, 90m, [Low(40, 80m), Low(10, 85m)]);
        var oneLow = TrendSeries.For(110m, 100m, 90m, [Low(10, 85m)]);
        var noSwings = TrendSeries.For(110m, 100m, 90m, []);

        Assert.Equal(TrendState.NotClassified, noLong.State);
        Assert.Equal(TrendState.NotClassified, noShort.State);
        Assert.Equal(TrendState.NotClassified, oneLow.State);
        Assert.Equal(TrendState.NotClassified, noSwings.State);

        Assert.Contains("200-day", noLong.Reason!, StringComparison.Ordinal);
        Assert.Contains("50-day", noShort.Reason!, StringComparison.Ordinal);
        Assert.Contains("swing lows", oneLow.Reason!, StringComparison.Ordinal);

        // The reason is what makes the fourth state legible, so its absence on a
        // classified name is asserted too: a label carrying a reason would read
        // as one the rule could not reach.
        Assert.True(TrendSeries.For(110m, 100m, 90m, [Low(40, 80m), Low(10, 85m)]).Classified);
        Assert.False(noLong.Classified);

        // A name with two highs and no lows can be read for a downtrend and not
        // for an uptrend. Answering range for it would answer a question the
        // rule could not ask, so it is not classified with the kind named.
        var highsOnly = TrendSeries.For(110m, 100m, 90m, [High(40, 120m), High(10, 115m)]);

        Assert.Equal(TrendState.NotClassified, highsOnly.State);
        Assert.Contains("swing lows", highsOnly.Reason!, StringComparison.Ordinal);
    }

    // ---- 5.5, the forward returns and the news pulse ----

    [Fact]
    public async Task EveryListingCarriesEveryHorizonAndAnImmatureOneReadsAsNotYetMatured()
    {
        // The committed fixture's listings sit on the last stored session, so
        // nothing after them has matured. That is the state this checkpoint is
        // about: an immature row reads as not yet matured rather than as a blank
        // or a zero, and the two are different statements with only one true.
        var rules = Expected("forward-returns").GetProperty("rules");

        Assert.Equal(
            [.. rules.GetProperty("horizons").EnumerateArray().Select(horizon => horizon.GetString()!)],
            ForwardReturnSeries.Horizons);

        Assert.Equal(ForwardReturnSeries.SetupSessionCap, rules.GetProperty("setupSessionCap").GetInt32());

        using var store = await WithReturns();

        var listings = int.Parse(Query(store, "SELECT COUNT(*) FROM listing;").Single(), CultureInfo.InvariantCulture);
        var perListing = Expected("forward-returns").GetProperty("counts").GetProperty("rowsPerListing").GetInt32();

        Assert.Equal(
            [(listings * perListing).ToString(CultureInfo.InvariantCulture)],
            Query(store, "SELECT COUNT(*) FROM forward_return;"));

        // Every horizon is present for every listing, so a horizon that never
        // matures is still a row a later session can fill.
        foreach (var horizon in ForwardReturnSeries.Horizons)
        {
            Assert.Equal(
                [listings.ToString(CultureInfo.InvariantCulture)],
                Query(store, $"SELECT COUNT(*) FROM forward_return WHERE horizon = '{horizon}';"));
        }

        // Nothing has matured over this fixture, and the row says so with a null
        // outcome rather than with a zero return.
        Assert.Equal(
            [(listings * perListing).ToString(CultureInfo.InvariantCulture)],
            Query(store, "SELECT COUNT(*) FROM forward_return WHERE outcome IS NULL;"));

        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM forward_return WHERE return_pct = 0;"));
    }

    [Fact]
    public async Task AHorizonMaturesIntoAWinOrALossAndASetupResolvesOnWhicheverCameFirst()
    {
        // The arithmetic, over constructed series, because the committed fixture
        // has no session after its listings and cannot reach a matured horizon
        // at all. That is the unreachable boundary this phase has now named
        // three times.
        ReturnBar[] Rising(int count, decimal from) =>
            [.. Enumerable.Range(1, count).Select(at => new ReturnBar(new DateOnly(2026, 1, 1).AddDays(at), from + at))];

        // Five sessions of a rising series is a win, and the return is the
        // change against the close the listing was made at.
        var win = ForwardReturnSeries.Over(5, Rising(10, 100m), 100m);

        Assert.Equal(ForwardReturnSeries.Win, win.Outcome);
        Assert.Equal(5, win.ReturnPct!.Value, 6);
        Assert.Equal(new DateOnly(2026, 1, 6), win.ResolvedOn);

        // A falling series is a loss, and the return is negative rather than
        // absent: a loss is a measurement and not a missing one.
        var falling = Rising(10, 100m).Select(bar => bar with { Close = 200m - bar.Close }).ToArray();
        var loss = ForwardReturnSeries.Over(5, falling, 100m);

        Assert.Equal(ForwardReturnSeries.Loss, loss.Outcome);
        Assert.True(loss.ReturnPct < 0);

        // A horizon with fewer sessions than it needs has not matured, asserted
        // either side of the boundary.
        Assert.Null(ForwardReturnSeries.Over(21, Rising(20, 100m), 100m).Outcome);
        Assert.NotNull(ForwardReturnSeries.Over(21, Rising(21, 100m), 100m).Outcome);

        // The setup: the stop is tested before the target, so a session that
        // closed through both was stopped out before it could reach anything. A
        // rule that took the target first would score a gap through the stop as
        // a win.
        var through = new ReturnBar[] { new(new DateOnly(2026, 1, 2), 80m) };

        Assert.Equal(ForwardReturnSeries.Loss, ForwardReturnSeries.OverSetup(through, 90m, 110m).Outcome);

        var reached = new ReturnBar[] { new(new DateOnly(2026, 1, 2), 120m) };

        Assert.Equal(ForwardReturnSeries.Win, ForwardReturnSeries.OverSetup(reached, 90m, 110m).Outcome);

        // Running out of sessions is unresolved, which is a value rather than a
        // null so it is counted in its own column and never in a rate.
        // see: An unresolved setup is never a win
        var flat = Enumerable.Range(1, ForwardReturnSeries.SetupSessionCap)
            .Select(at => new ReturnBar(new DateOnly(2026, 1, 1).AddDays(at), 100m))
            .ToArray();

        Assert.Equal(ForwardReturnSeries.Unresolved, ForwardReturnSeries.OverSetup(flat, 90m, 110m).Outcome);

        // One session short of the cap is not yet matured, which is a different
        // thing from unresolved and is stated as one.
        Assert.Null(ForwardReturnSeries.OverSetup([.. flat.Take(flat.Length - 1)], 90m, 110m).Outcome);

        // A listing with no plan has no setup to resolve, which is an absence
        // rather than an unresolved setup.
        Assert.Null(ForwardReturnSeries.OverSetup(flat, null, null).Outcome);

        // The invariant the branch order rests on, asserted rather than assumed.
        // 5.5's own mutation showed that swapping the stop and the target
        // branches changes nothing any series can show: the test is on the
        // close, and one close cannot be both below the stop and at or above the
        // target while the stop is beneath the target. So what is asserted is
        // the invariant, and a plan that breaks it refuses rather than being
        // scored by whichever branch ran first.
        var broken = Assert.Throws<InvalidOperationException>(
            () => ForwardReturnSeries.OverSetup(flat, 110m, 90m));

        Assert.Contains("not a plan", broken.Message, StringComparison.Ordinal);

        Assert.Throws<InvalidOperationException>(() => ForwardReturnSeries.OverSetup(flat, 100m, 100m));

        // And the invariant holds over every plan the store carries, which is
        // where it has to hold rather than only over constructed input.
        using var stored = await WithReturns();

        foreach (var plan in Query(stored, "SELECT plan_at_listing FROM listing;"))
        {
            var root = JsonDocument.Parse(plan).RootElement;

            if (root.TryGetProperty("stop", out var stop) && stop.ValueKind == JsonValueKind.String
                && root.TryGetProperty("firstTradedTarget", out var target) && target.ValueKind == JsonValueKind.String)
            {
                Assert.True(
                    decimal.Parse(stop.GetString()!, CultureInfo.InvariantCulture)
                        < decimal.Parse(target.GetString()!, CultureInfo.InvariantCulture),
                    $"a stored plan has its stop at {stop.GetString()} and its target at {target.GetString()}.");
            }
        }
    }

    [Fact]
    public void TheBaseRateIsOverEveryNameNightAndIsAbsentWhereNothingHasMatured()
    {
        // The population is every name-night and not the rows where a reason
        // fired, because a listings row exists for every name and a figure over
        // the listed ones is a figure over the wrong population.
        // see: The base rate is over every name-night, and never over the listed ones
        Assert.Equal(50, ForwardReturnSeries.BaseRate([ForwardReturnSeries.Win, ForwardReturnSeries.Loss])!.Value, 6);
        Assert.Equal(100, ForwardReturnSeries.BaseRate([ForwardReturnSeries.Win])!.Value, 6);

        // An unresolved outcome is in neither half of the rate, which is what
        // counting it in its own column means.
        Assert.Equal(
            100,
            ForwardReturnSeries.BaseRate([ForwardReturnSeries.Win, ForwardReturnSeries.Unresolved])!.Value,
            6);

        // A window with nothing matured has no rate rather than a rate of zero.
        // A zero says every name fell; nothing says nothing has matured.
        Assert.Null(ForwardReturnSeries.BaseRate([null, null]));
        Assert.Null(ForwardReturnSeries.BaseRate([]));
    }

    [Fact]
    public async Task TheNewsPulseCountsEveryMemberFromOneDatedQueryAndKeepsTheCountOverTheIndex()
    {
        // One dated query fanned out to names in code, and the count is kept for
        // the index rather than for every symbol the market wrote about: one
        // live request reached 3,232 distinct symbols against an index of 503.
        // see: News is one dated query, paged to cover the day, and attributed to names locally
        var rules = Expected("news-pulse").GetProperty("rules");

        Assert.Equal(NewsPulseCounter.RetentionDays, rules.GetProperty("retentionDays").GetInt32());

        using var store = await WithReturns();

        var members = FixtureExpectation.CurrentMembers;

        // A row per member per date, including the members the day wrote nothing
        // about: the pulse is compared against its own baseline, and a night
        // missing from the series would read as a night nobody counted.
        Assert.Equal(
            [.. members],
            Query(store, "SELECT ticker FROM news_pulse ORDER BY ticker;"));

        // And the counts are the payload's own attribution, recomputed here from
        // the same capture rather than read back from the counter.
        var articles = RecordedNewsFeed.Parse(File.ReadAllText(
            Path.Combine(Folder(), "news-2026-09-08.json")));

        var byName = NewsAttribution.ByName(articles);

        foreach (var member in members)
        {
            Assert.Equal(
                [(byName.TryGetValue(member, out var found) ? found.Count : 0).ToString(CultureInfo.InvariantCulture)],
                Query(store, $"SELECT article_count FROM news_pulse WHERE ticker = '{member}';"));
        }

        // No symbol outside the index has a row, which is what keeps this a
        // table about the universe rather than about the market.
        Assert.True(
            byName.Count > members.Length,
            $"the capture attributes {byName.Count} symbols against {members.Length} members, so the filter is untested.");

        Assert.Equal(
            [members.Length.ToString(CultureInfo.InvariantCulture)],
            Query(store, "SELECT COUNT(*) FROM news_pulse;"));
    }

    [Fact]
    public async Task TheNewsPulseCostsOnePageWhereOnePageCoversTheDay()
    {
        // The page count is read off the feed rather than stated by the caller,
        // because a caller that wrote the figure would be recording its own
        // intention: correct today, and still reading one on the night a feed
        // starts paging.
        using var store = await WithReturns();

        var requests = Query(store, "SELECT network_requests FROM run_log WHERE stage = 'news-pulse';");

        Assert.Equal(["1"], requests);

        // And it does not grow with the population, which is the property the
        // cost rule rests on. The captured payload is one page whatever the
        // index holds, so the count is asserted against the feed's own figure
        // over two universe sizes rather than against a literal.
        var feed = RecordedNewsFeed.FromFolder(Folder());

        await feed.ArticlesAsync(new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 8));

        Assert.Equal(1, feed.Requests);

        await feed.ArticlesAsync(new DateOnly(2026, 9, 8), new DateOnly(2026, 9, 8));

        Assert.Equal(2, feed.Requests);
    }

    [Fact]
    public async Task TheClosingRowsCountsAreTakenOffTheStoreRatherThanReportedByTheStages()
    {
        // Section 14's last step. 5.5's own mutation found this unasserted: the
        // closing row could report a count of zero for the names on the list and
        // the suite stayed green.
        //
        // Every figure is recomputed here from the same tables the stage counts
        // over, so a stage that reported its own opinion of what it wrote
        // disagrees with the store rather than with a frozen number.
        using var store = await WithReturns();

        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);
        var closed = await new NightClose(clock, store.DatabaseFile).RunAsync(Index, "close-check");

        Assert.Equal(
            int.Parse(Query(store, "SELECT COUNT(*) FROM ladder WHERE as_of = (SELECT MAX(as_of) FROM ladder);").Single(), CultureInfo.InvariantCulture),
            closed.NamesComputed);

        Assert.Equal(
            int.Parse(Query(store, "SELECT COUNT(*) FROM listing WHERE fired_count > 0;").Single(), CultureInfo.InvariantCulture),
            closed.NamesOnTheList);

        Assert.Equal(
            int.Parse(Query(store, "SELECT IFNULL(SUM(fired_count), 0) FROM listing;").Single(), CultureInfo.InvariantCulture),
            closed.ReasonsFired);

        // The counts are non-zero, so the agreement above is two figures meeting
        // rather than two zeros.
        Assert.True(closed.NamesComputed > 0, "the night computed no name, so the counts agree over nothing.");
        Assert.True(closed.ReasonsFired > 0, "no reason fired, so the count agrees over nothing.");

        // Stale is over the index rather than over the names with bars, because
        // a member with none is stale in the way that matters. Every fixture
        // name ends on the same session, so the constructed member is what
        // reaches the case.
        Assert.Equal(0, closed.NamesStale);

        Insert(
            store,
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector) " +
            "VALUES ('GSPC', 'ZZZZ', '2026-01-02', NULL, '2026-09-08T00:00:00Z', 'Utilities');");

        Assert.Equal(1, (await new NightClose(clock, store.DatabaseFile).RunAsync(Index, "close-stale")).NamesStale);

        // A run with no rows of its own says so rather than reporting a duration
        // of zero, because the two read the same on a page and only one is true.
        // This run id has no stages behind it, so that is the branch it takes.
        Assert.Equal("no duration recorded", closed.Duration);

        // And a run whose stages did write rows reports the span they cover,
        // which is the other half. Asserted over a run id the replay used, so
        // the two branches are both reached rather than one of them being
        // whatever the arrangement happened to give.
        Assert.Contains(
            "second(s)",
            (await new NightClose(clock, store.DatabaseFile).RunAsync(Index, "replay-listings")).Duration,
            StringComparison.Ordinal);
    }

    // ---- 5.4, the shortlist builder ----

    [Fact]
    public void SectionElevensSixReasonsAreTheSixTheCodeEvaluates()
    {
        // Section 11's table is placed whole against this check, and covering a
        // table means covering rows nobody enumerated, so the instrument has to
        // open the document that carries them. It is read here rather than
        // restated: a list of six names beside a table of six rows is two
        // statements of one fact.
        var table = ArchitectureTables.In(Corpus.Read("docs/ARCHITECTURE.html"))
            .Single(read => read.Heading == "11. The shortlist and its six reasons");

        var named = table.Body
            .Where(row => row.Count > 1 && row[0].Length > 0)
            .Select(row => row[0].ToLowerInvariant())
            .ToArray();

        Assert.Equal(6, named.Length);
        Assert.Equal([.. ShortlistSeries.Reasons.Order(StringComparer.Ordinal)], [.. named.Order(StringComparer.Ordinal)]);

        // And the two figures the table's own cells state, read from the cells
        // rather than from a list beside them.
        var soon = table.Body.Single(row => row.Count > 1 && row[0].Equals("Earnings soon", StringComparison.OrdinalIgnoreCase));
        var unusual = table.Body.Single(row => row.Count > 1 && row[0].Equals("Unusual volume", StringComparison.OrdinalIgnoreCase));

        Assert.Contains("twenty sessions", soon[1], StringComparison.Ordinal);
        Assert.Equal(20, ShortlistSeries.EarningsHorizonSessions);

        Assert.Contains("twice", unusual[1], StringComparison.Ordinal);
        Assert.Equal(2, ShortlistSeries.UnusualVolumeMultiple);
    }

    [Fact]
    public async Task EachOfTheSixReasonsIsRecomputedFromTheTablesItReads()
    {
        // The expectation states the rules and the suite recomputes each reason
        // from the tables it reads, rather than diffing against a set frozen
        // from the builder. A frozen set would agree with a builder that
        // evaluated the wrong rule, because it would have been taken from that
        // builder.
        var rules = Expected("listings").GetProperty("rules");

        Assert.Equal(
            [.. rules.GetProperty("reasons").EnumerateArray().Select(reason => reason.GetString()!)],
            ShortlistSeries.Reasons);

        Assert.Equal(ShortlistSeries.EarningsHorizonSessions, rules.GetProperty("earningsHorizonSessions").GetInt32());
        Assert.Equal(ShortlistSeries.UnusualVolumeMultiple, rules.GetProperty("unusualVolumeMultiple").GetDouble());

        using var store = await WithListings();

        foreach (var name in FixtureExpectation.Names)
        {
            var written = JsonDocument
                .Parse(Query(store, $"SELECT reasons FROM listing WHERE ticker = '{name}';").Single())
                .RootElement.EnumerateArray()
                .ToDictionary(reason => reason.GetProperty("name").GetString()!, reason => reason.GetProperty("fired").GetBoolean(), StringComparer.Ordinal);

            // Every reason is on the row whether or not it fired.
            Assert.Equal([.. ShortlistSeries.Reasons], [.. written.Keys]);

            var close = decimal.Parse(Query(store, $"SELECT close FROM bar WHERE ticker = '{name}' ORDER BY session_date DESC LIMIT 1;").Single(), CultureInfo.InvariantCulture);
            var previous = decimal.Parse(Query(store, $"SELECT close FROM bar WHERE ticker = '{name}' ORDER BY session_date DESC LIMIT 1 OFFSET 1;").Single(), CultureInfo.InvariantCulture);
            var volume = long.Parse(Query(store, $"SELECT volume FROM bar WHERE ticker = '{name}' ORDER BY session_date DESC LIMIT 1;").Single(), CultureInfo.InvariantCulture);
            var average = double.Parse(Query(store, $"SELECT value FROM indicator WHERE ticker = '{name}' AND name = 'vol_avg50' ORDER BY session_date DESC LIMIT 1;").Single(), CultureInfo.InvariantCulture);

            // Unusual volume, recomputed from the bar and the indicator.
            Assert.Equal(volume > average * ShortlistSeries.UnusualVolumeMultiple, written[ShortlistSeries.UnusualVolume]);

            // Crossed a level, recomputed from the two closes and the band
            // edges the level rows carry.
            var edges = Query(
                store,
                $"SELECT low_edge FROM level WHERE ticker = '{name}' AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = '{name}') " +
                $"UNION ALL SELECT high_edge FROM level WHERE ticker = '{name}' AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = '{name}');")
                .Select(edge => decimal.Parse(edge, CultureInfo.InvariantCulture))
                .ToArray();

            Assert.Equal(
                edges.Any(edge => (previous < edge && close >= edge) || (previous > edge && close <= edge)),
                written[ShortlistSeries.CrossedALevel]);

            // Breakout on volume, recomputed from the resistance edges and the
            // average. Both halves of it, so a reason that fired on one is not
            // read as having fired on both.
            var resistance = Query(
                store,
                $"SELECT low_edge FROM level WHERE ticker = '{name}' AND role = 'resistance' AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = '{name}') " +
                $"UNION ALL SELECT high_edge FROM level WHERE ticker = '{name}' AND role = 'resistance' AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = '{name}');")
                .Select(edge => decimal.Parse(edge, CultureInfo.InvariantCulture))
                .ToArray();

            Assert.Equal(
                resistance.Any(edge => close > edge) && volume > average,
                written[ShortlistSeries.BreakoutOnVolume]);

            // At entry zone, recomputed from the stored plan's own tranches.
            var plan = JsonDocument.Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}' ORDER BY as_of DESC LIMIT 1;").Single()).RootElement;

            Assert.Equal(
                plan.GetProperty("tranches").EnumerateArray().Any(tranche =>
                    close >= decimal.Parse(tranche.GetProperty("lowEdge").GetString()!, CultureInfo.InvariantCulture)
                    && close <= decimal.Parse(tranche.GetProperty("highEdge").GetString()!, CultureInfo.InvariantCulture)),
                written[ShortlistSeries.AtEntryZone]);

            // Trend state changed, which needs a yesterday. The fixture holds
            // one ladder row per name, so no name has one and none fires. That
            // is the rule rather than an absence: a name with no row last night
            // has not changed.
            Assert.Equal(
                Query(store, $"SELECT COUNT(*) FROM ladder WHERE ticker = '{name}';").Single() != "1",
                written[ShortlistSeries.TrendStateChanged]);
        }

        // The fired count is counted from the reasons rather than stated beside
        // them, over every row rather than over the four names.
        foreach (var row in Query(store, "SELECT fired_count, reasons FROM listing;"))
        {
            var parts = row.Split('|', 2);

            Assert.Equal(
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                JsonDocument.Parse(parts[1]).RootElement.EnumerateArray().Count(reason => reason.GetProperty("fired").GetBoolean()));
        }
    }

    [Fact]
    public async Task TrendChangedFiresOnlyWhereThereIsAYesterdayToDifferFrom()
    {
        // The case the committed fixture cannot reach: it holds one ladder row
        // per name, so no name has a previous label and this reason fires on
        // none of them. Read from the fixture alone the rule would be
        // indistinguishable from a reason that never fires.
        var asOf = new DateOnly(2026, 9, 8);

        ReasonInputs With(string? tonight, string? lastNight) =>
            new(100m, 100m, 1000, 500, [], [], tonight, lastNight, null);

        bool Fired(ReasonInputs inputs) =>
            ShortlistSeries.For(inputs).Single(outcome => outcome.Name == ShortlistSeries.TrendStateChanged).Fired;

        Assert.True(Fired(With("uptrend", "range")));
        Assert.False(Fired(With("uptrend", "uptrend")));

        // A name with no row last night has not changed. Without the
        // every-member ladder row this would fire for every name in the index on
        // its first night, which is what that row exists to prevent.
        Assert.False(Fired(With("uptrend", null)));
        Assert.False(Fired(With(null, "uptrend")));

        // And the reason states both labels whether or not it fired, so a row
        // that did not fire still says what it compared.
        var values = ShortlistSeries.For(With("uptrend", null))
            .Single(outcome => outcome.Name == ShortlistSeries.TrendStateChanged).Values;

        Assert.Equal("uptrend", values["trend state"]);
        Assert.Equal("no row last night", values["previous trend state"]);

        Assert.Equal(new DateOnly(2026, 9, 8), asOf);
    }

    [Fact]
    public void EarningsSoonFiresInsideTheHorizonAndSaysWhenNoDateIsOnFile()
    {
        // Section 18's row: a name with no earnings date on file cannot fire the
        // earnings reason and the calendar says the date is not on file. A
        // guessed date is a wrong date.
        ReasonInputs With(int? sessions) => new(100m, 100m, 1000, 500, [], [], "range", "range", sessions);

        ReasonOutcome Reason(int? sessions) =>
            ShortlistSeries.For(With(sessions)).Single(outcome => outcome.Name == ShortlistSeries.EarningsSoon);

        Assert.True(Reason(0).Fired);
        Assert.True(Reason(ShortlistSeries.EarningsHorizonSessions).Fired);
        Assert.False(Reason(ShortlistSeries.EarningsHorizonSessions + 1).Fired);

        // The boundary is asserted either side rather than in the middle, which
        // is where an off-by-one lives.
        Assert.True(Reason(ShortlistSeries.EarningsHorizonSessions - 1).Fired);

        var absent = Reason(null);

        Assert.False(absent.Fired);
        Assert.Equal("not on file", absent.Values["sessions to the next dated event"]);
    }

    [Fact]
    public async Task ThePlanAtListingCarriesTheEntryTheStopAndTheFirstTradedTarget()
    {
        // The column the improvement loop rests on. Bars can be replayed and the
        // plan cannot, because by the time a verdict is possible the rules may
        // have changed and recomputing would score old listings under new ones.
        using var store = await WithListings();

        foreach (var name in FixtureExpectation.Names)
        {
            var stored = JsonDocument
                .Parse(Query(store, $"SELECT plan_at_listing FROM listing WHERE ticker = '{name}';").Single())
                .RootElement;

            var plan = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}' ORDER BY as_of DESC LIMIT 1;").Single())
                .RootElement;

            var first = plan.GetProperty("tranches").EnumerateArray().FirstOrDefault();

            if (first.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            // Every value is the ladder's own, so the column is a copy of what
            // stood tonight rather than a second computation of it.
            Assert.Equal(first.GetProperty("lowEdge").GetString(), stored.GetProperty("entryLow").GetString());
            Assert.Equal(first.GetProperty("highEdge").GetString(), stored.GetProperty("entryHigh").GetString());
            Assert.Equal(first.GetProperty("stop").GetString(), stored.GetProperty("stop").GetString());
            Assert.Equal(plan.GetProperty("invalidation").GetString(), stored.GetProperty("invalidation").GetString());

            var traded = plan.GetProperty("exits").EnumerateArray()
                .FirstOrDefault(exit => exit.GetProperty("traded").GetBoolean());

            Assert.Equal(
                traded.ValueKind == JsonValueKind.Object ? traded.GetProperty("lowEdge").GetString() : null,
                stored.GetProperty("firstTradedTarget").GetString());
        }
    }

    // ---- 5.3, the facts assembler and the change detector ----

    [Fact]
    public async Task TheFactsFileCarriesEveryDeclaredFactWithTheSourceThatComputedIt()
    {
        // The set of facts and the source of each are written from the rules
        // rather than frozen from a run, so a stage that wrote the wrong number
        // of them does not agree with the file.
        var expected = Expected("facts").GetProperty("factsPerName");

        using var store = await WithFacts();

        foreach (var name in FixtureExpectation.Names)
        {
            var payload = Query(store, $"SELECT payload FROM facts WHERE ticker = '{name}';").Single();
            var facts = JsonDocument.Parse(payload).RootElement.GetProperty("facts");

            var bySource = facts.EnumerateArray()
                .GroupBy(fact => fact.GetProperty("source").GetString()!)
                .ToDictionary(group => group.Key, group => group.Select(fact => fact.GetProperty("name").GetString()!).Order(StringComparer.Ordinal).ToArray(), StringComparer.Ordinal);

            foreach (var (key, source) in new[]
            {
                ("fromBar", FactsAssembler.FromBars),
                ("fromIndicator", FactsAssembler.FromIndicators),
                ("fromLevel", FactsAssembler.FromLevels),
                ("fromLadder", FactsAssembler.FromLadder),
                ("fromMove", FactsAssembler.FromMoves),
                ("fromSwing", FactsAssembler.FromSwings),
                ("fromCalendar", FactsAssembler.FromCalendar),
            })
            {
                Assert.Equal(
                    [.. expected.GetProperty(key).EnumerateArray().Select(fact => fact.GetString()!).Where(fact => fact.Length > 0)],
                    bySource.TryGetValue(source, out var written) ? written : []);
            }

            // Every fact names a source, and no fact names one the assembler
            // does not have. A fact with no provenance is a number in prose that
            // traces to nothing.
            Assert.All(facts.EnumerateArray(), fact =>
                Assert.False(string.IsNullOrWhiteSpace(fact.GetProperty("source").GetString())));

            // The payload is written in name order, so two runs over one store
            // produce the same bytes.
            var names = facts.EnumerateArray().Select(fact => fact.GetProperty("name").GetString()!).ToArray();

            Assert.Equal([.. names.Order(StringComparer.Ordinal)], names);
        }
    }

    [Fact]
    public async Task EveryValueInTheFactsFileIsTheValueTheStoreHolds()
    {
        // Nothing in the facts file is derived, which is what makes it the file
        // a written section is checked against. Asserted against the stores the
        // values were read from rather than against a copy of the payload, so a
        // stage that wrote the wrong number does not agree with itself.
        using var store = await WithFacts();

        foreach (var name in FixtureExpectation.Names)
        {
            var payload = Query(store, $"SELECT payload FROM facts WHERE ticker = '{name}';").Single();
            var facts = JsonDocument.Parse(payload).RootElement.GetProperty("facts")
                .EnumerateArray()
                .ToDictionary(fact => fact.GetProperty("name").GetString()!, fact => fact.GetProperty("value").GetString()!, StringComparer.Ordinal);

            var session = JsonDocument.Parse(payload).RootElement.GetProperty("sessionDate").GetString();

            Assert.Equal(
                Query(store, $"SELECT MAX(session_date) FROM bar WHERE ticker = '{name}';").Single(),
                session);

            Assert.Equal(
                Query(store, $"SELECT close FROM bar WHERE ticker = '{name}' AND session_date = '{session}';").Single(),
                facts["close"]);

            Assert.Equal(
                Query(store, $"SELECT volume FROM bar WHERE ticker = '{name}' AND session_date = '{session}';").Single(),
                facts["session volume"]);

            Assert.Equal(
                Query(store, $"SELECT trend_state FROM ladder WHERE ticker = '{name}' ORDER BY as_of DESC LIMIT 1;").Single(),
                facts["trend state"]);

            Assert.Equal(
                Query(store, $"SELECT COUNT(*) FROM swing WHERE ticker = '{name}';").Single(),
                facts["swings marked"]);

            Assert.Equal(
                Query(store, $"SELECT session_date FROM move WHERE ticker = '{name}' ORDER BY rank LIMIT 1;").Single(),
                facts["largest move session"]);

            // The immediate bands, read from the level rows the assembler read.
            foreach (var role in new[] { "support", "resistance" })
            {
                var edges = Query(
                    store,
                    $"SELECT low_edge FROM level WHERE ticker = '{name}' AND role = '{role}' AND immediate = 1 " +
                    $"AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = '{name}');");

                Assert.Equal(edges.Single(), facts[$"immediate {role} low edge"]);
            }
        }
    }

    [Fact]
    public async Task AFactsRerunDoesNotBlankTheChangeList()
    {
        // The per-operation split, proved rather than true by construction. The
        // two writers own disjoint columns of the same row and the grain is the
        // same, which is what permits a table with an inserter and a different
        // updater under a rule that forbids two owners for one operation.
        using var store = await WithFacts();

        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        // A change list that did not arise from a comparison, so what survives
        // is visibly this test's value rather than an empty list that would look
        // the same whether it survived or was rewritten.
        Insert(store, "UPDATE facts SET material_changes = '[\"a change nobody computed\"]';");

        var before = Query(store, "SELECT material_changes FROM facts ORDER BY ticker;");

        Assert.All(before, changes => Assert.Contains("nobody computed", changes, StringComparison.Ordinal));

        await new FactsAssembler(clock, store.DatabaseFile).RunAsync("rerun-facts");

        Assert.Equal(before, Query(store, "SELECT material_changes FROM facts ORDER BY ticker;"));

        // And the assembler's own columns are still what it wrote, so the run
        // did something rather than nothing at all.
        Assert.All(
            Query(store, "SELECT payload_hash FROM facts;"),
            hash => Assert.Equal(64, hash.Length));

        // The detector rewrites its own column and touches neither of the
        // assembler's, which is the same property from the other side.
        var payloads = Query(store, "SELECT payload || '|' || payload_hash FROM facts ORDER BY ticker;");

        await new ChangeDetector(clock, store.DatabaseFile).RunAsync("rerun-changes");

        Assert.Equal(payloads, Query(store, "SELECT payload || '|' || payload_hash FROM facts ORDER BY ticker;"));
        Assert.All(
            Query(store, "SELECT material_changes FROM facts;"),
            changes => Assert.DoesNotContain("nobody computed", changes, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheChangeListNamesWhatMovedAndIsEmptyOnAFirstNight()
    {
        // A material change is a fact that appeared, went, or took a different
        // value, compared by name so a fact added between two others is one
        // change rather than every fact after it changing.
        using var store = await WithFacts();

        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        // A first night has nothing to compare against, so the list is empty
        // rather than unknown. The two read differently on a page and only one
        // of them is true.
        Assert.All(
            Query(store, "SELECT material_changes FROM facts;"),
            changes => Assert.Equal("[]", changes));

        // A second night, constructed by writing an earlier row whose payload
        // differs in exactly one fact. The committed fixture holds one night per
        // name, so a comparison against a previous night is unreachable from it.
        var name = FixtureExpectation.Names[0];
        var payload = Query(store, $"SELECT payload FROM facts WHERE ticker = '{name}';").Single();
        var earlier = payload.Replace("\"value\":\"", "\"value\":\"9", StringComparison.Ordinal);

        Insert(
            store,
            "INSERT INTO facts (ticker, session_date, payload, payload_hash) VALUES " +
            $"('{name}', '2000-01-03', '{earlier.Replace("'", "''", StringComparison.Ordinal)}', 'earlier');");

        await new ChangeDetector(clock, store.DatabaseFile).RunAsync("second-night");

        var changed = Query(store, $"SELECT material_changes FROM facts WHERE ticker = '{name}' AND session_date != '2000-01-03';").Single();
        var named = JsonDocument.Parse(changed).RootElement.EnumerateArray().Select(item => item.GetString()!).ToArray();

        Assert.NotEmpty(named);
        Assert.Equal([.. named.Order(StringComparer.Ordinal)], named);

        // Every name in the list is a fact the payload carries, so the list
        // names what moved rather than what the comparison invented.
        var carried = JsonDocument.Parse(payload).RootElement.GetProperty("facts")
            .EnumerateArray()
            .Select(fact => fact.GetProperty("name").GetString()!)
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(named, fact => Assert.Contains(fact, carried));

        // A fact that did not move is not in the list, which is the half a list
        // of every fact would also satisfy.
        Assert.Equal(
            [.. FactsFile.Changed(earlier, payload)],
            named);
    }

    // ---- 5.2, the move annotator ----

    [Fact]
    public async Task TheMovesMatchTheRulesOverTheCommittedBars()
    {
        // The expectation was computed outside this repository from the captured
        // payloads, so a component that stored the wrong set does not agree with
        // it. A count frozen from the run would agree with a run that read the
        // wrong window, because it would have been taken from that run.
        var expected = Expected("moves");
        var byName = expected.GetProperty("byName");
        var rules = expected.GetProperty("rules");

        // The spans and the cap are read from the expectation and asserted
        // against the constants, so the file and the code cannot drift apart
        // while both look right on their own.
        Assert.Equal(
            [.. rules.GetProperty("spans").EnumerateArray().Select(span => span.GetInt32())],
            MoveSeries.Spans);

        Assert.Equal(MoveSeries.MostMoves, rules.GetProperty("mostMoves").GetInt32());

        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var rows = Query(
                store,
                $"SELECT session_date || '|' || sessions || '|' || rank FROM move WHERE ticker = '{name}' ORDER BY rank;");

            Assert.Equal(
                [.. byName.GetProperty(name).EnumerateArray().Select(move =>
                    $"{move.GetProperty("sessionDate").GetString()}|{move.GetProperty("sessions").GetInt32()}|{move.GetProperty("rank").GetInt32()}")],
                rows);

            // The change itself, to six places, which is where a percentage of a
            // price stops being the same number in two languages.
            foreach (var move in byName.GetProperty(name).EnumerateArray())
            {
                var stored = Query(
                    store,
                    $"SELECT change_pct FROM move WHERE ticker = '{name}' AND session_date = '{move.GetProperty("sessionDate").GetString()}';");

                Assert.Equal(
                    move.GetProperty("changePct").GetDouble(),
                    double.Parse(stored.Single(), CultureInfo.InvariantCulture),
                    6);
            }
        }

        Assert.Equal(
            [expected.GetProperty("counts").GetProperty("rows").GetInt32().ToString(CultureInfo.InvariantCulture)],
            Query(store, "SELECT COUNT(*) FROM move;"));
    }

    [Fact]
    public async Task AMoveIsRankedByAbsoluteSizeAndTheLongerSpanKeepsTheSession()
    {
        // The rules, asserted over the stored rows rather than against the
        // expectation, so the two say the same thing for different reasons.
        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var sizes = Query(store, $"SELECT ABS(change_pct) FROM move WHERE ticker = '{name}' ORDER BY rank;")
                .Select(value => double.Parse(value, CultureInfo.InvariantCulture))
                .ToArray();

            Assert.Equal([.. sizes.OrderByDescending(size => size)], sizes);

            // A fall counts as much as a rise, which is what ranking on the
            // absolute size means and what a table about what happened needs.
            // Asserted rather than assumed: over four names of real bars some
            // of the largest moves are down, and a rank that ordered on the
            // signed value would put every fall at the bottom.
            Assert.Contains(
                Query(store, $"SELECT change_pct FROM move WHERE ticker = '{name}';"),
                value => double.Parse(value, CultureInfo.InvariantCulture) < 0);
        }

        // One row per session, which the primary key holds, and the span on it
        // is the longer of the two that end there. Asserted over constructed
        // input, because a series where a single day is the largest mover on a
        // session a five-day run also ends on is not something four names of
        // committed bars can be relied on to hold.
        var rising = Enumerable.Range(0, 12)
            .Select(day => new MoveBar(new DateOnly(2026, 1, 1).AddDays(day), 100m + day))
            .ToArray();

        var moves = MoveSeries.For(rising);

        Assert.Equal(moves.Count, moves.Select(move => move.SessionDate).Distinct().Count());

        // A session far enough into the series for both spans to end on it
        // carries the longer, and one too near the start carries the only span
        // that reaches it. Both halves are asserted, because a rule that only
        // ever saw the first half would read as the longer span always winning.
        var first = rising[0].SessionDate;

        Assert.All(
            moves.Where(move => move.SessionDate.DayNumber - first.DayNumber >= 5),
            move => Assert.Equal(5, move.Sessions));

        Assert.All(
            moves.Where(move => move.SessionDate.DayNumber - first.DayNumber < 5),
            move => Assert.Equal(1, move.Sessions));

        Assert.Contains(moves, move => move.Sessions == 5);
        Assert.Contains(moves, move => move.Sessions == 1);

        // A series shorter than a span produces the shorter moves alone rather
        // than nothing, and a series of one session produces none: a move needs
        // two closes and there is only one.
        Assert.All(MoveSeries.For([.. rising.Take(3)]), move => Assert.Equal(1, move.Sessions));
        Assert.Empty(MoveSeries.For([.. rising.Take(1)]));

        // A close of zero or less gives no move rather than an infinite one,
        // which is the boundary the committed bars cannot reach because no
        // stored close is zero.
        var zeroed = new MoveBar[]
        {
            new(new DateOnly(2026, 1, 1), 0m),
            new(new DateOnly(2026, 1, 2), 10m),
        };

        Assert.Empty(MoveSeries.For(zeroed));
    }

    [Fact]
    public async Task TheMoveAnnotatorDropsWhatFallsOutOfTheWindowAndLeavesWhatIsInside()
    {
        // The last of the six computed tables to gain a deleter, which is what
        // 4.0 ruled and 4.2 implemented for the other five.
        // see: Every computed table's writer is its own deleter
        using var store = await WithLadders();

        var newest = Query(store, "SELECT MAX(session_date) FROM bar;").Single();
        var boundary = DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddYears(-1);

        Assert.Equal(
            ["0"],
            Query(store, $"SELECT COUNT(*) FROM move WHERE session_date < '{boundary:yyyy-MM-dd}';"));

        // And rows inside the window are untouched, which is the half a drop
        // that removed everything would also satisfy.
        Assert.NotEqual(
            ["0"],
            Query(store, $"SELECT COUNT(*) FROM move WHERE session_date >= '{boundary:yyyy-MM-dd}';"));

        // The two assertions above are vacuous on their own and 5.2's own
        // mutation showed it: every move the committed bars produce is already
        // inside the window, so a drop that did nothing satisfies both. The
        // boundary is unreachable from this fixture, which is the class 5.0
        // named, so the row is constructed.
        // Rank 1 rather than a rank past the kept count, so the row can only be
        // removed by the retention drop. A rank outside tonight's set is
        // removed by the fallen-out drop instead, and the first sweep of this
        // checkpoint showed that: the assertion passed with the retention
        // statement disabled, because the other statement was doing the work.
        Insert(store, "INSERT INTO move (ticker, session_date, sessions, change_pct, rank) " +
            $"VALUES ('AAPL', '{boundary.AddDays(-1):yyyy-MM-dd}', 1, 99.0, 1);");

        Assert.Equal(
            ["1"],
            Query(store, $"SELECT COUNT(*) FROM move WHERE session_date < '{boundary:yyyy-MM-dd}';"));

        var inside = Query(store, "SELECT COUNT(*) FROM move;").Single();

        await new MoveAnnotator(FixedClock.At(Instant, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("drop-check");

        Assert.Equal(
            ["0"],
            Query(store, $"SELECT COUNT(*) FROM move WHERE session_date < '{boundary:yyyy-MM-dd}';"));

        // One row went and no others did, so the drop is the boundary deciding
        // rather than the table being rewritten.
        Assert.Equal(
            [(int.Parse(inside, CultureInfo.InvariantCulture) - 1).ToString(CultureInfo.InvariantCulture)],
            Query(store, "SELECT COUNT(*) FROM move;"));
    }

    // ---- 5.0, the assertions the phase 4 sign-off's mutation sweep left ----
    //
    // Eleven mutations left the whole suite green at 415 of 415. Seven of the
    // classes that let them survive are closed here, in the pass before phase 5
    // writes anything, because the evidence is in hand and no phase 5 checkpoint
    // reads this code.
    //
    // Each is written against the class it closes rather than against the line
    // that was edited, which is what the mutation-choice rule now requires.

    [Fact]
    public void AnEarningsPrintOutsideTheStoredBarsProducesNoFigureRatherThanAWrongOne()
    {
        // Group B. Both of `EarningsRuleFor`'s fall-through guards produce a
        // number rather than an absence when they are gone, and a number on the
        // page is what a reader acts on.
        //
        // Without the earlier-session guard a print older than the stored bars
        // reports a one-day move equal to the price itself, since the missing
        // prior bar reads as a close of zero. Without the later-session guard a
        // print after the last stored bar reports a zero move dated 0001-01-01.
        // see: Code owns every number
        var sessions = new LadderBar[]
        {
            new(new DateOnly(2026, 9, 1), 101m, 99m, 100m),
            new(new DateOnly(2026, 9, 2), 112m, 108m, 110m),
            new(new DateOnly(2026, 9, 3), 106m, 104m, 105m),
        };

        var older = new[] { (new DateOnly(2026, 8, 1), EventTiming.Unstated) };
        var later = new[] { (new DateOnly(2026, 9, 10), EventTiming.Unstated) };

        Assert.Empty(LadderSeries.EarningsRuleFor(older, sessions, 5m));
        Assert.Empty(LadderSeries.EarningsRuleFor(later, sessions, 5m));

        // The control, so the two absences are the guards deciding rather than
        // the arithmetic being unreachable over these bars.
        var inside = new[] { (new DateOnly(2026, 9, 2), EventTiming.Unstated) };
        var stated = Assert.Single(LadderSeries.EarningsRuleFor(inside, sessions, 5m));

        Assert.Equal(new DateOnly(2026, 9, 2), stated.Session);
        Assert.Equal(10m, stated.Move);
        Assert.Equal(2m, stated.ShareOfStop);

        // And the number each guard would otherwise have produced, named so the
        // assertion fails on the value rather than only on the count. The price
        // itself for the older print, and a zero move for the later one.
        Assert.DoesNotContain(
            LadderSeries.EarningsRuleFor([.. older, .. inside], sessions, 5m),
            print => print.Move == 100m || print.Session == default);

        // The mixed case, which is what the night actually hands over: the last
        // two prints by date, one of them unreachable, and one figure stated.
        var mixed = LadderSeries.EarningsRuleFor([.. older, .. inside, .. later], sessions, 5m);

        Assert.Equal(10m, Assert.Single(mixed).Move);
    }

    [Fact]
    public void TheBlendedEntryIsTheMeanOfTheFirstTwoZonesAndTheSkipBoundaryIsMeasuredFromIt()
    {
        // Group B, and the boundary Group D named beside it. Section 17's row
        // measures the near-exit skip from the blended entry, and taking the
        // first tranche alone leaves the suite green because no fixture name has
        // two tranches far enough apart for the two readings to differ.
        //
        // Constructed so they differ: the midpoints are 99 and 89, so the mean
        // is 94 and the first alone is 99. The two resistance bands sit either
        // side of exactly two typical days' moves from 94, which pins the
        // blended entry and the boundary in one arrangement.
        Level Support(decimal low, decimal high) => new(low, high, LevelSeries.Support, false, 1, true, []);
        Level Resistance(decimal low, decimal high) => new(low, high, LevelSeries.Resistance, false, 1, true, []);

        var tranches = new Tranche[]
        {
            new(98m, 100m, TrancheCondition.AvailableNow, 90m),
            new(88m, 90m, TrancheCondition.ReachesTheZone, null),
        };

        List<Level> bands = [Support(88m, 90m), Support(98m, 100m), Resistance(95.9m, 96m), Resistance(96m, 97m)];

        var exits = LadderSeries.ExitsFor(bands, tranches, typicalMove: 1m);

        // 95.9 is 1.9 above the blended entry and is listed and not traded; 96
        // is exactly two typical days' moves above it and is traded. Under the
        // first-tranche-alone reading both are below the entry and neither is
        // traded, and under a strict comparison at the boundary the second is
        // skipped too.
        var near = exits.Single(exit => exit.LowEdge == 95.9m);
        var boundary = exits.Single(exit => exit.LowEdge == 96m);

        Assert.False(near.Traded);
        Assert.Contains("typical days", near.Reason!, StringComparison.Ordinal);
        Assert.True(boundary.Traded);
        Assert.Null(boundary.Reason);

        // One tranche is the other half of the same rule, and the mean of one is
        // that one. The bands move with it rather than the reading: 101 is two
        // typical days above 99 and 100.9 is not.
        var alone = LadderSeries.ExitsFor(
            [Support(98m, 100m), Resistance(100.9m, 101m), Resistance(101m, 102m)],
            [tranches[0]],
            typicalMove: 1m);

        Assert.False(alone.Single(exit => exit.LowEdge == 100.9m).Traded);
        Assert.True(alone.Single(exit => exit.LowEdge == 101m).Traded);
    }

    [Fact]
    public void ATrancheWithNoBandBeneathItInvalidatesAtItsOwnLowEdge()
    {
        // Group B. Every tranche in the committed fixture has a stop, the three
        // uptrend names taking one from the trailing rule and the range name
        // having a band beneath all three of its tranches, so the fallback is
        // unreachable from these bars and the ladder expectation says as much in
        // its own invalidation note.
        var asOf = new DateOnly(2026, 9, 4);
        var flat = Enumerable.Range(0, 10)
            .Select(day => new LadderBar(asOf.AddDays(day - 9), 101m, 99m, 100m))
            .ToArray();

        var lowest = new Level(90m, 92m, LevelSeries.Support, false, 1, true, []);
        var only = LadderSeries.For([lowest], 100m, 1m, flat, TrendState.Range);

        var tranche = Assert.Single(only.Tranches);

        Assert.Null(tranche.Stop);
        Assert.Equal(90m, only.Invalidation);
        Assert.Equal(tranche.LowEdge, only.Invalidation);

        // The control, over the same lowest band with one beneath it: the
        // invalidation is that band's low edge and not the tranche's own, so the
        // fallback is the absent stop deciding rather than the two coinciding.
        var beneath = new Level(80m, 82m, LevelSeries.Support, false, 1, true, []);
        var pair = LadderSeries.For([lowest, beneath], 100m, 1m, flat, TrendState.Range);

        Assert.Equal(80m, pair.Invalidation);
        Assert.NotEqual(pair.Tranches[0].LowEdge, pair.Invalidation);
    }

    [Fact]
    public async Task NoTwoTranchesShareAStopWhichIsWhatTheInvalidationRelabelRests()
    {
        // Group A, which is the class the sweep could not name until now: the
        // test is well formed and the data cannot take the shape the mutation
        // would change. `PlanRows` finds the stop row sitting at the
        // invalidation price and relabels it, and replacing the first match with
        // the last leaves the suite green because at most one stop can be there.
        //
        // The remedy for this class is not a stronger assertion at that site. It
        // is the invariant written down and asserted where it holds, which is
        // here: each stop is at or above the low edge of the band beneath its
        // tranche, and the next tranche's stop is below that tranche's own low
        // edge, so the stops are strictly decreasing.
        var asOf = new DateOnly(2026, 9, 4);
        var flat = Enumerable.Range(0, 10)
            .Select(day => new LadderBar(asOf.AddDays(day - 9), 101m, 99m, 100m))
            .ToArray();

        Level Support(decimal low, decimal high) => new(low, high, LevelSeries.Support, false, 1, true, []);

        List<Level> bands = [Support(96m, 98m), Support(90m, 92m), Support(84m, 86m), Support(78m, 80m)];

        var plan = LadderSeries.For(bands, 100m, 1m, flat, TrendState.Range);

        Assert.Equal(3, plan.Tranches.Count);

        var stops = plan.Tranches.Select(tranche => tranche.Stop!.Value).ToArray();

        Assert.Equal(stops.Length, stops.Distinct().Count());
        Assert.Equal([.. stops.OrderByDescending(stop => stop)], stops);

        for (var at = 0; at < plan.Tranches.Count; at++)
        {
            Assert.True(
                stops[at] < plan.Tranches[at].LowEdge,
                $"the stop at {stops[at]} is not below its own tranche's low edge of {plan.Tranches[at].LowEdge}.");
        }

        // The invalidation is the lowest of them and exactly one stop sits
        // there, which is the property the relabel reads.
        Assert.Equal(stops.Min(), plan.Invalidation);
        Assert.Single(stops, stop => stop == plan.Invalidation);

        // And over the population the fixture holds, so the invariant is
        // asserted on real bands rather than only on constructed ones.
        using var store = await WithLadders();

        foreach (var name in FixtureExpectation.Names)
        {
            var stored = JsonDocument
                .Parse(Query(store, $"SELECT plan FROM ladder WHERE ticker = '{name}';").Single())
                .RootElement;

            var written = stored.GetProperty("tranches").EnumerateArray()
                .Select(tranche => tranche.GetProperty("stop").GetString())
                .Where(stop => stop is not null)
                .ToArray();

            Assert.Equal(written.Length, written.Distinct(StringComparer.Ordinal).Count());

            if (stored.GetProperty("invalidation").GetString() is { } price)
            {
                Assert.True(
                    written.Count(stop => string.Equals(stop, price, StringComparison.Ordinal)) <= 1,
                    $"{name} carries more than one stop at its invalidation price of {price}.");
            }
        }
    }

    [Fact]
    public void TheShockMultipleAndItsNextDayHoldAreEachReachedOnTheirOwn()
    {
        // Group C. The pair is asymmetric: the lookback beside the multiple
        // turns a test red and the multiple itself does not, because the
        // constructed case uses a move large enough for either figure and the
        // negative case a move small enough for either. Both are stated in one
        // sentence in DECISIONS.md as proposals on the same terms, so one being
        // reached and the other not is the thing to close.
        // see: The event setups' triggers are proposals until resolved setups can score them
        var asOf = new DateOnly(2026, 9, 4);
        var band = new Level(90m, 95m, LevelSeries.Support, false, 1, true, []);

        LadderBar Bar(int day, decimal high, decimal low, decimal close) =>
            new(asOf.AddDays(day - 9), high, low, close);

        // Every bar is well formed, its close inside its own range, because a
        // window no provider could send is the class this pass has just finished
        // naming. The day after the shock is given a range of two typical days'
        // moves so it cannot be a shock itself whatever its low is.
        LadderBar[] Window(decimal high, decimal low, decimal nextLow) =>
        [
            .. Enumerable.Range(0, 10).Select(day => day switch
            {
                5 => Bar(day, high, low, (high + low) / 2),
                6 => Bar(day, nextLow + 2m, nextLow, nextLow + 1m),
                _ => Bar(day, 101m, 99m, 100m),
            }),
        ];

        // A range of exactly three typical days' moves is not a shock, and the
        // same window one hundredth wider is. The multiple is what separates
        // them, so a multiple of one would answer the first as a shock.
        Assert.Equal(
            TrancheCondition.FirstCloseBackAbove,
            LadderSeries.ConditionFor(band, 100m, 1m, Window(97m, 94m, 94m)));

        Assert.Equal(
            TrancheCondition.SecondDayAfterAShock,
            LadderSeries.ConditionFor(band, 100m, 1m, Window(97.01m, 94m, 94m)));

        // A range of two typical days' moves is not a shock at three and is at
        // one, which is the mutation the sweep left green.
        Assert.Equal(
            TrancheCondition.FirstCloseBackAbove,
            LadderSeries.ConditionFor(band, 100m, 1m, Window(96m, 94m, 94m)));

        // The next-day hold, over a window that does not hold its low. The shock
        // is a shock and the day after undercut it, so the pattern is not the
        // second day after a shock and the window falls to the condition beneath
        // it.
        Assert.Equal(
            TrancheCondition.FirstCloseBackAbove,
            LadderSeries.ConditionFor(band, 100m, 1m, Window(110m, 94m, 93.99m)));

        // The day after at exactly the shock's own low holds, which is the edge
        // the requirement is written on.
        Assert.Equal(
            TrancheCondition.SecondDayAfterAShock,
            LadderSeries.ConditionFor(band, 100m, 1m, Window(110m, 94m, 94m)));
    }

    [Fact]
    public void TheConditionWindowIsTheLastTenSessionsAndNotTheFirstTen()
    {
        // Group D, and the sharpest of the three: no test enters the branch at
        // all, so taking the first ten instead of the last ten stays green
        // because every constructed window in the suite is exactly ten sessions
        // long. A name with more than ten stored sessions is every name.
        var asOf = new DateOnly(2026, 9, 4);
        var band = new Level(90m, 95m, LevelSeries.Support, false, 1, true, []);

        LadderBar Bar(int day, decimal high, decimal low, decimal close) =>
            new(asOf.AddDays(day - 14), high, low, close);

        // Fifteen sessions. The breakdown sits in the first five, which the
        // window does not reach, and the last ten dip into the band without
        // closing below it.
        var early = Enumerable.Range(0, 15)
            .Select(day => day < 5 ? Bar(day, 89m, 87m, 88m) : Bar(day, 96.5m, 94m, 96m))
            .ToArray();

        Assert.Equal(
            TrancheCondition.FirstCloseBackAbove,
            LadderSeries.ConditionFor(band, 97m, 1m, early));

        // The same fifteen sessions with the breakdown moved inside the window,
        // which is the other direction: the slice is what decides, rather than
        // the breakdown being unreachable from this arrangement.
        var late = Enumerable.Range(0, 15)
            .Select(day => day is 7 ? Bar(day, 89m, 87m, 88m) : Bar(day, 96.5m, 94m, 96m))
            .ToArray();

        Assert.Equal(
            TrancheCondition.FailedBreakdown,
            LadderSeries.ConditionFor(band, 97m, 1m, late));

        // And the window's own boundary: a session eleven back is outside it and
        // one ten back is inside.
        var outside = Enumerable.Range(0, 15)
            .Select(day => day is 4 ? Bar(day, 89m, 87m, 88m) : Bar(day, 96.5m, 94m, 96m))
            .ToArray();

        var inside = Enumerable.Range(0, 15)
            .Select(day => day is 5 ? Bar(day, 89m, 87m, 88m) : Bar(day, 96.5m, 94m, 96m))
            .ToArray();

        Assert.Equal(TrancheCondition.FirstCloseBackAbove, LadderSeries.ConditionFor(band, 97m, 1m, outside));
        Assert.Equal(TrancheCondition.FailedBreakdown, LadderSeries.ConditionFor(band, 97m, 1m, inside));
    }

    [Fact]
    public void ABandWhoseLowEdgeEqualsTheCloseCarriesNoTranche()
    {
        // Group D. The eligibility test is a strict comparison and the fixture
        // holds no band whose low edge lands exactly on the close, so admitting
        // the equal case stays green.
        //
        // The rule is that the band is below the price, and a band whose floor
        // is the price is not below it. The near-exit boundary at exactly two
        // typical days' moves is the same class and is asserted beside the
        // blended entry, which is the arrangement that pins both.
        // see: A support band whose low edge is below the price carries a tranche, even when the band contains the price
        var asOf = new DateOnly(2026, 9, 4);
        var flat = Enumerable.Range(0, 10)
            .Select(day => new LadderBar(asOf.AddDays(day - 9), 101m, 99m, 100m))
            .ToArray();

        var at = new Level(100m, 102m, LevelSeries.Support, false, 1, true, []);
        var below = new Level(99.99m, 102m, LevelSeries.Support, false, 1, true, []);

        var equal = LadderSeries.For([at], 100m, 1m, flat, TrendState.Range);

        Assert.Empty(equal.Tranches);
        Assert.Contains("no support band sits below the price", equal.Reason!, StringComparison.Ordinal);

        // One hundredth lower and the same band carries a tranche, so the
        // refusal above is the comparison deciding rather than the band being
        // ineligible for another reason. It keeps its full width, which is the
        // second half of the same rule.
        var lower = LadderSeries.For([below], 100m, 1m, flat, TrendState.Range);
        var tranche = Assert.Single(lower.Tranches);

        Assert.Equal(99.99m, tranche.LowEdge);
        Assert.Equal(102m, tranche.HighEdge);
    }

    [Fact]
    public void EveryExpectationFileNamesWhatItWasDerivedFrom()
    {
        // A frozen figure and a derived one look identical in a JSON file, so
        // each states which it is. This is the assertion that keeps the
        // distinction from decaying into a folder of numbers nobody can place.
        var files = Directory.GetFiles(Path.Combine(Folder(), "expectations"), "*.json");

        Assert.True(files.Length >= 2, $"The fixture holds {files.Length} expectation files, expected at least 2.");

        foreach (var file in files)
        {
            var document = JsonDocument.Parse(File.ReadAllText(file)).RootElement;

            Assert.True(document.TryGetProperty("stage", out _), $"{Path.GetFileName(file)} names no stage.");
            Assert.True(document.TryGetProperty("derivedFrom", out var from), $"{Path.GetFileName(file)} does not say what it was derived from.");
            Assert.False(string.IsNullOrWhiteSpace(from.GetString()));
        }
    }
}
