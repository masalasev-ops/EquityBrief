using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Membership;

// 1.1's done condition, exercised through the loader's own surface rather than
// asserted from a source scan. A scan finding a pattern is not evidence the
// behaviour exists; this runs the path.
public class MembershipLoaderTests
{
    const string Index = "GSPC";
    const string Fixture = "membership-2026-09-05";

    static string CapturedResponse() =>
        Path.Combine(Repository.Root, "fixtures", Fixture, "index-constituents.json");

    static (MembershipLoader Loader, RecordedIndexMembershipFeed Feed) Loader(TemporaryStore store)
    {
        var feed = RecordedIndexMembershipFeed.FromFile(CapturedResponse());

        return (new MembershipLoader(feed, FixedClock.At(Instant, SessionZones.UnitedStates), store.DatabaseFile), feed);
    }

    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task ANameThatLeftTheIndexCarriesItsLeaveDate()
    {
        using var store = new TemporaryStore().Migrated();
        var (loader, _) = Loader(store);

        await loader.LoadAsync(Index, "run-1");

        var left = Rows(store, @"SELECT ticker, ""left"" FROM membership WHERE ""left"" IS NOT NULL;");

        // The fixture carries one name that left, and the date is the provider's
        // rather than the night's, so a name that went in March is not recorded
        // as having gone in September.
        Assert.Equal(("XRAY", "2026-03-21"), Assert.Single(left));

        var current = Rows(store, @"SELECT ticker, ""left"" FROM membership WHERE ""left"" IS NULL;");

        Assert.Equal(3, current.Count);
    }

    [Fact]
    public async Task AMembershipQueryForAPastDateAnswersWithThatDatesSet()
    {
        using var store = new TemporaryStore().Migrated();
        var (loader, _) = Loader(store);

        await loader.LoadAsync(Index, "run-1");

        // Tonight: the three still in the index.
        var tonight = await loader.MembersOnAsync(Index, new DateOnly(2026, 9, 5));

        Assert.Equal(["AAPL", "KEYS", "MSFT"], tonight);

        // Before the leave date: four, including the one that has since gone.
        // This is the whole reason membership carries spans, and a query that
        // returned tonight's set for a past date would show a name as absent
        // from a window it was part of.
        var before = await loader.MembersOnAsync(Index, new DateOnly(2026, 3, 20));

        Assert.Equal(["AAPL", "KEYS", "MSFT", "XRAY"], before);

        // On the leave date itself the name is already out: the date is the day
        // it stopped being a member, so the comparison is strict on that edge.
        var onTheDay = await loader.MembersOnAsync(Index, new DateOnly(2026, 3, 21));

        Assert.DoesNotContain("XRAY", onTheDay);

        // And before a name joined, it is not a member either, which is the
        // other edge and the one a query keyed on the leave date alone misses.
        var longBefore = await loader.MembersOnAsync(Index, new DateOnly(2010, 1, 4));

        Assert.Equal(["AAPL", "MSFT", "XRAY"], longBefore);
    }

    [Fact]
    public async Task ASecondRunOnTheSameNightWritesTheSameStateAndCallsTheFeedOnce()
    {
        using var store = new TemporaryStore().Migrated();
        var (loader, feed) = Loader(store);

        var first = await loader.LoadAsync(Index, "run-1");
        var after = Rows(store, @"SELECT ticker, ""left"" FROM membership ORDER BY ticker;");

        // A second run is a second run, with its own id. The run log's grain is
        // one row per run per stage and SCHEMA gives it no updater and no
        // deleter, so re-running a night appends a row rather than rewriting
        // one. Idempotency is a claim about the stored data, not about the log:
        // the night's own record of having run twice is the thing the run page
        // exists to show.
        var second = await loader.LoadAsync(Index, "run-2");
        var again = Rows(store, @"SELECT ticker, ""left"" FROM membership ORDER BY ticker;");

        Assert.Equal(first, second);
        Assert.Equal(after, again);
        Assert.Equal(4, after.Count);

        var logged = Rows(store, "SELECT run_id, stage FROM run_log ORDER BY run_id;");

        Assert.Equal([("run-1", MembershipLoader.Stage), ("run-2", MembershipLoader.Stage)], logged);

        // One feed call per run for the whole index, not one per name. This is
        // the figure the zero-per-name limit is asserted against, and it is a
        // count rather than a source scan.
        Assert.Equal(2, feed.Calls);
    }

    [Fact]
    public async Task TheStageAppendsItsOwnRunLogRowWithTheCountMeasuredFromTheStore()
    {
        using var store = new TemporaryStore().Migrated();
        var (loader, _) = Loader(store);

        await loader.LoadAsync(Index, "run-1");

        var rows = Rows(store, "SELECT stage, outcome FROM run_log WHERE run_id = 'run-1';");

        Assert.Equal((MembershipLoader.Stage, "ok"), Assert.Single(rows));

        var measured = Rows(store, "SELECT CAST(rows_written AS TEXT), CAST(network_requests AS TEXT) FROM run_log WHERE run_id = 'run-1';");

        // Four rows written and one request, and the count is read back from the
        // store rather than taken from what the stage said it did.
        Assert.Equal(("4", "1"), Assert.Single(measured));

        var free = Rows(store, "SELECT CAST(model_calls AS TEXT), spend FROM run_log WHERE run_id = 'run-1';");

        Assert.Equal(("0", "0"), Assert.Single(free));
    }

    [Fact]
    public void TheRecordedFeedRefusesAPayloadItCannotParse()
    {
        // A feed that answered with an empty index would write a leave date onto
        // every name in the store, so an unreadable payload fails instead.
        Assert.Throws<FormatException>(() => RecordedIndexMembershipFeed.Parse("{}", Index));
        Assert.Throws<FormatException>(() => RecordedIndexMembershipFeed.Parse(@"{""Components"":{}}", Index));

        var parsed = RecordedIndexMembershipFeed.Parse(File.ReadAllText(CapturedResponse()), Index);

        Assert.Equal(4, parsed.Count);
        Assert.Contains(parsed, constituent => constituent.Ticker == "XRAY" && constituent.Left is not null);
        Assert.Contains(parsed, constituent => constituent.Ticker == "AAPL" && constituent.Left is null);
    }

    static IReadOnlyList<(string, string)> Rows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<(string, string)>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        return rows;
    }
}
