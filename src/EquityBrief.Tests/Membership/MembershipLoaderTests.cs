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
        // rather than the night's, so a name that went in 2024 is not recorded
        // as having gone on the night the fixture was captured.
        //
        // 2024-04-03 is what the provider reports for XRAY. The hand-built
        // fixture said 2026-03-21, which was invented, and every date below was
        // chosen around it.
        Assert.Equal(("XRAY", "2024-04-03"), Assert.Single(left));

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
        var before = await loader.MembersOnAsync(Index, new DateOnly(2024, 4, 2));

        Assert.Equal(["AAPL", "KEYS", "MSFT", "XRAY"], before);

        // On the leave date itself the name is already out: the date is the day
        // it stopped being a member, so the comparison is strict on that edge.
        var onTheDay = await loader.MembersOnAsync(Index, new DateOnly(2024, 4, 3));

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
        Assert.Equal(2, feed.Requests);

        // observed_at is deliberately excluded from the comparison above, and
        // the exclusion is asserted rather than left as an omission. It is the
        // instant of the fetch, so it is expected to differ between runs, and a
        // test that simply left it out of the select would read as a claim about
        // the whole row while quietly making one about four fifths of it.
        var instants = Rows(store, "SELECT ticker, observed_at FROM membership ORDER BY ticker;");

        Assert.Equal(4, instants.Count);
        Assert.All(instants, row => Assert.NotEqual(string.Empty, row.Item2));
        Assert.Single(instants.Select(row => row.Item2).Distinct());
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

        // Four rows written and one request. Both are measured rather than
        // stated: the rows from the store, and the requests off the feed.
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
        Assert.Throws<FormatException>(() => RecordedIndexMembershipFeed.Parse(@"{""HistoricalTickerComponents"":{}}", Index));

        var parsed = RecordedIndexMembershipFeed.Parse(File.ReadAllText(CapturedResponse()), Index);

        Assert.Equal(4, parsed.Count);
        Assert.Contains(parsed, constituent => constituent.Ticker == "XRAY" && constituent.Left is not null);
        Assert.Contains(parsed, constituent => constituent.Ticker == "AAPL" && constituent.Left is null);
    }

    [Theory]
    [InlineData(@"""EndDate"": null", null)]
    [InlineData(@"""Other"": 1", null)]
    public void AnAbsentEndDateMeansACurrentMember(string tail, string? expected)
    {
        // The first of the three outcomes. Absent and JSON null both mean the
        // name is still in the index, which is what the null in the membership
        // row's left column records.
        var parsed = RecordedIndexMembershipFeed.Parse(Payload(tail), Index);

        Assert.Equal(expected, Assert.Single(parsed).Left?.ToString("yyyy-MM-dd"));
    }

    [Theory]
    [InlineData(@"""EndDate"": ""not a date""")]
    [InlineData(@"""EndDate"": ""21/03/2026""")]
    [InlineData(@"""EndDate"": ""2026-13-45""")]
    public void AnEndDateThatCannotBeReadThrowsRatherThanReadingAsAbsent(string tail)
    {
        // The second outcome, and the one that mattered. A name that left the
        // index carrying an end date the parser could not read would have been
        // stored with no leave date and read as a current member, which is the
        // one thing membership exists to prevent, with nothing failing anywhere.
        var refusal = Assert.Throws<FormatException>(() => RecordedIndexMembershipFeed.Parse(Payload(tail), Index));

        // The ticker is named, because a format failure over five hundred
        // constituents that does not say which one is useless.
        Assert.Contains("ZZZZ", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(@"""EndDate"": 20260321")]
    [InlineData(@"""EndDate"": true")]
    [InlineData(@"""EndDate"": []")]
    public void AnEndDateThatIsNotAStringThrows(string tail)
    {
        // The third outcome. A value of the wrong kind is present and unreadable
        // rather than absent, and is refused for the same reason.
        var refusal = Assert.Throws<FormatException>(() => RecordedIndexMembershipFeed.Parse(Payload(tail), Index));

        Assert.Contains("ZZZZ", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheDateParseDoesNotDependOnTheMachinesLocale()
    {
        // A day-first string is refused rather than read as a different date,
        // which is what a culture-sensitive parse would do on a machine set to a
        // day-first locale. Same class as an instant resolving against the
        // machine zone, and in shipped code rather than in the suite.
        Assert.Throws<FormatException>(() => RecordedIndexMembershipFeed.Parse(Payload(@"""EndDate"": ""21/03/2026"""), Index));

        var parsed = RecordedIndexMembershipFeed.Parse(Payload(@"""EndDate"": ""2026-03-21"""), Index);

        Assert.Equal(new DateOnly(2026, 3, 21), Assert.Single(parsed).Left);
    }

    [Fact]
    public void TheSnapshotObjectIsRefusedRatherThanReadAsTheIndex()
    {
        // The defect 1.2's capture exposed, made permanent.
        //
        // The provider sends both objects. Components is tonight's snapshot and
        // carries Sector, Industry and Weight with no dates and no departed
        // name; HistoricalTickerComponents carries the spans. This read
        // Components until 1.2, which the hand-built fixture satisfied because
        // it had been written with dates in the wrong object.
        //
        // A real snapshot payload is refused, and the refusal says which object
        // holds the spans rather than only that a field is missing.
        var snapshot = """
        { "Components": { "158": {
            "Code": "AAPL", "Exchange": "US", "Name": "Apple Inc.",
            "Sector": "Technology", "Industry": "Consumer Electronics", "Weight": 0.0708 } } }
        """;

        var refusal = Assert.Throws<FormatException>(() => RecordedIndexMembershipFeed.Parse(snapshot, Index));

        Assert.Contains("HistoricalTickerComponents", refusal.Message, StringComparison.Ordinal);

        // And the captured fixture carries both objects, so the refusal above is
        // about which one is read and not about which one is present.
        using var captured = System.Text.Json.JsonDocument.Parse(File.ReadAllText(CapturedResponse()));

        Assert.True(captured.RootElement.TryGetProperty("Components", out _));
        Assert.True(captured.RootElement.TryGetProperty("HistoricalTickerComponents", out _));
    }

    static string Payload(string tail) =>
        $$"""
        { "HistoricalTickerComponents": { "0": { "Code": "ZZZZ", "StartDate": "2020-01-02", {{tail}} } } }
        """;

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
