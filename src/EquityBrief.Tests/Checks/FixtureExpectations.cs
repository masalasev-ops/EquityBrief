using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Core.Volume;
using EquityBrief.Data.Swings;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Membership;
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
        ["fixtures/membership-2026-09-05"],
        [
            CheckReach.Key(Scope.FixtureTable, "bars"),
            CheckReach.Key(Scope.FixtureTable, "news"),
            CheckReach.Key(Scope.FixtureTable, "indicators"),
            CheckReach.Key(Scope.FixtureTable, "swings"),
            CheckReach.Key(Scope.FixtureTable, "volume profile"),
            CheckReach.Key(Scope.LimitsTable, "Swing lookback"),
            CheckReach.Key(Scope.FailureTable, "Fewer than 200 bars for a new index member, 200-day average"),
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

        // Four stand unread and each is named rather than counted, because a
        // number here would drift silently as keys are added. `rowsInFile` is
        // the count of rows in the captured bulk payload, which the fetch
        // expectation states so a reader can see what the membership filter cut
        // from; `index` is the index code; and the two notes are sentences.
        // None is a figure the pipeline produces, which is why nothing asserts
        // them. The series state file added two keys when it landed and this
        // assertion caught both on the same run, which is what it is for.
        Assert.Equal(
            ["fetch.rowsInFile", "membership.index", "membership.note", "series-state.note"],
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
