using System.Globalization;
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

        var left = Rows(store, @"SELECT ticker, ""left"" FROM membership WHERE ""left"" IS NOT NULL ORDER BY ticker;");

        // Two names that left, on different dates, and both dates are the
        // provider's rather than the night's, so a name that went in 2024 is not
        // recorded as having gone on the night the fixture was captured.
        //
        // Two rather than one, and asserted as a set rather than through
        // Assert.Single. A fixture with a single departed name makes every
        // statement about departure a statement about one row, and anything that
        // mishandles the second is invisible in it.
        Assert.Equal([("AAL", "2024-09-23"), ("XRAY", "2024-04-03")], left);

        var current = Rows(store, @"SELECT ticker, ""left"" FROM membership WHERE ""left"" IS NULL;");

        Assert.Equal(4, current.Count);
    }

    [Fact]
    public async Task AMembershipQueryForAPastDateAnswersWithThatDatesSet()
    {
        using var store = new TemporaryStore().Migrated();
        var (loader, _) = Loader(store);

        await loader.LoadAsync(Index, "run-1");

        // The spans the fixture carries, which are the provider's:
        //
        //   AAPL  1982-11-30 ..            MSFT  1994-06-01 ..
        //   XRAY  2008-11-14 .. 2024-04-03  AAL   2015-03-23 .. 2024-09-23
        //   KEYS  2018-11-06 ..
        //
        // and the query is joined <= on AND (left IS NULL OR left > on). Each
        // date is named for the distinction it draws rather than for where it
        // sits, because a date chosen to sit beside a boundary stops drawing its
        // distinction the moment the boundary moves. These were chosen around a
        // leave date of 2026-03-21 that the hand-built fixture had invented, and
        // the real one is two years earlier.
        //
        // How many dates and how many distinct answers is derived at the end
        // rather than stated here. Stating it is how this comment came to say
        // six over a test that asks seven questions.

        // Before any name joined: nothing, rather than everything. A query that
        // ignored the date would answer with the whole table here, and every
        // other assertion below would still pass.
        var beforeAnyJoin = await loader.MembersOnAsync(Index, new DateOnly(1982, 11, 29));

        Assert.Empty(beforeAnyJoin);

        // A date where two of the five had not joined yet. This is the join half
        // of the span, and the one a query keyed on the leave date alone misses:
        // it would answer with KEYS and AAL here, years before either was in the
        // index.
        var beforeTwoJoined = await loader.MembersOnAsync(Index, new DateOnly(2010, 1, 4));

        Assert.Equal(["AAPL", "MSFT", "XRAY"], beforeTwoJoined);

        // The join edge, strict, asserted either side of one day. The leave edge
        // below was asserted this way and the join edge was not, which left
        // joined < on and joined <= on indistinguishable.
        var dayBeforeKeysJoined = await loader.MembersOnAsync(Index, new DateOnly(2018, 11, 5));
        var theDayKeysJoined = await loader.MembersOnAsync(Index, new DateOnly(2018, 11, 6));

        Assert.DoesNotContain("KEYS", dayBeforeKeysJoined);
        Assert.Contains("KEYS", theDayKeysJoined);

        // Between a join and a leave: all five, including both names that have
        // since gone. This is the whole reason membership carries spans, and a
        // query returning tonight's set for a past date would show a name as
        // absent from a window it was part of.
        var betweenJoinAndLeave = await loader.MembersOnAsync(Index, new DateOnly(2024, 4, 2));

        Assert.Equal(["AAL", "AAPL", "KEYS", "MSFT", "NFLX", "XRAY"], betweenJoinAndLeave);

        // After a leave and before tonight. Two things at once, and both matter.
        //
        // The leave edge is strict: the date is the day the name stopped being a
        // member rather than the last day it was one, so XRAY is already out on
        // its own leave date.
        //
        // And the answer here differs from tonight's, which is what makes this a
        // third distinction rather than a restatement of the first. With XRAY as
        // the only departed name it did not: every date from 2024-04-03 to the
        // fixture instant returned tonight's set, because no membership event
        // fell between them, so this assertion collapsed into the one below it
        // and a query answering "tonight" for any recent past date would have
        // passed. AAL left five months later and splits that stretch in two.
        var afterALeave = await loader.MembersOnAsync(Index, new DateOnly(2024, 4, 3));

        Assert.Equal(["AAL", "AAPL", "KEYS", "MSFT", "NFLX"], afterALeave);

        // Tonight: the three still in the index.
        var tonight = await loader.MembersOnAsync(Index, new DateOnly(2026, 9, 5));

        Assert.Equal(["AAPL", "KEYS", "MSFT", "NFLX"], tonight);

        // The distinctions, derived rather than stated and asserted as
        // distinctions rather than left to be read off the expectations above.
        // A set of dates that all happened to answer alike would satisfy every
        // Assert.Equal above and none of this.
        var answers = new[]
        {
            beforeAnyJoin, beforeTwoJoined, dayBeforeKeysJoined, theDayKeysJoined,
            betweenJoinAndLeave, afterALeave, tonight,
        };

        // One pair agrees, and deliberately: the day KEYS joined and the day
        // before XRAY left are the same region, because the first is there as an
        // edge against the day before it rather than as a region of its own. So
        // seven dates draw six answers, and the sixth is what this test is for.
        Assert.Equal(6, answers.Select(answer => string.Join(",", answer)).Distinct().Count());
        Assert.Equal(theDayKeysJoined, betweenJoinAndLeave);

        // Named separately because it is the one that had collapsed: with a
        // single departed name this equalled tonight, and the whole test read as
        // a claim it was not making.
        Assert.NotEqual(afterALeave, tonight);
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
        Assert.Equal(6, after.Count);

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

        Assert.Equal(6, instants.Count);
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

        // Six rows written and one request. Both are measured rather than
        // stated: the rows from the store, and the requests off the feed.
        Assert.Equal(("6", "1"), Assert.Single(measured));

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

        // The whole projection rather than a count and two Contains clauses.
        //
        // Two clauses named one of the two departed names and one of the four
        // current ones, which is two rows of six: a parser regression dropping
        // AAL's end date leaves the count where it was and both clauses true. The
        // count is the weakest half of that, since it survives every error that
        // does not add or remove a row.
        Assert.Equal(
            [
                ("AAL", "2024-09-23"),
                ("AAPL", null),
                ("KEYS", null),
                ("MSFT", null),
                ("NFLX", null),
                ("XRAY", "2024-04-03"),
            ],
            parsed.Select(constituent => (constituent.Ticker, constituent.Left?.ToString("yyyy-MM-dd"))));
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
    [InlineData(@"""EndDate"": ""04/07/2021""")]
    [InlineData(@"""EndDate"": ""2021-13-45""")]
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
    [InlineData(@"""EndDate"": 20210704")]
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
        // A slashed date is refused rather than read as a different date, which
        // is what a culture-sensitive parse would do on a machine set to a
        // day-first locale. Same class as an instant resolving against the
        // machine zone, and in shipped code rather than in the suite.
        //
        // 04/07/2021 is ambiguous rather than merely slashed: it is the fourth
        // of July read day-first and the seventh of April read month-first, so a
        // lenient parse succeeds under both cultures and returns a different
        // date under each. A day-first string with a day above twelve would be
        // refused by an invariant parse anyway, which tests the culture far less.
        //
        // The date is arbitrary and belongs to no fixture. It reads 2026-03-21
        // until 1.2, which was the leave date the hand-built fixture had
        // invented, and a synthetic payload carrying a real name's date invites
        // a reader to think the two are connected.
        // Run under a day-first culture and a month-first one, because a test
        // named for a property of the machine that sets no culture asserts
        // nothing about the machine. It set none until 1.2: it asserted an exact
        // parse, which implies locale independence without demonstrating it, and
        // would have passed just as well with the CultureInfo.InvariantCulture
        // argument removed from the parse it is about.
        var original = CultureInfo.CurrentCulture;

        try
        {
            foreach (var culture in new[] { "en-GB", "en-US", "de-DE" })
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);

                var parsed = RecordedIndexMembershipFeed.Parse(Payload(@"""EndDate"": ""2021-07-04"""), Index);

                Assert.Equal(new DateOnly(2021, 7, 4), Assert.Single(parsed).Left);
                Assert.Throws<FormatException>(
                    () => RecordedIndexMembershipFeed.Parse(Payload(@"""EndDate"": ""04/07/2021"""), Index));
            }
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }

        // And the ambiguity the refusal is about, demonstrated rather than
        // asserted in a comment. Without this the refused string could be one
        // nothing would have misread, and the test would prove only that a
        // slash is not a hyphen.
        Assert.True(DateOnly.TryParse("04/07/2021", CultureInfo.GetCultureInfo("en-GB"), DateTimeStyles.None, out var dayFirst));
        Assert.True(DateOnly.TryParse("04/07/2021", CultureInfo.GetCultureInfo("en-US"), DateTimeStyles.None, out var monthFirst));

        Assert.Equal(new DateOnly(2021, 7, 4), dayFirst);
        Assert.Equal(new DateOnly(2021, 4, 7), monthFirst);
        Assert.NotEqual(dayFirst, monthFirst);
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
