using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
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
