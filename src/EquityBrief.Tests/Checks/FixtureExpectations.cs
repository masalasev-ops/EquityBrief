using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Membership;
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
