using System.Globalization;
using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data.Migrations;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// corporate-actions. An action on a current member triggers a full-year
// refetch, the replacement is atomic, and a failure of the check itself marks
// the name suspect rather than passing.
//
// The action is read from a captured provider response and not a constructed
// one, which is the obligation 1.2 filed against this checkpoint. Three
// requests settled the shapes before the parser existed, and two of them would
// have been written wrong from the endpoint's name alone: a split's value is a
// ratio in a string, and the exchange is under `exchange` where the price bulk
// file calls it `exchange_short_name`.
public class CorporateActions
{
    internal static CheckReach Reach => new(
        "corporate-actions",
        ["fixtures/membership-2026-09-05"],
        [
            // The catalogue and matrix rows are not here. Their notes describe a
            // declaration reconciled against the row, the matrix row, SCHEMA's
            // ownership and the component's own source, and this check performs
            // none of that: component-access does, and 3.0's sweep found the
            // claims naming a check whose tests could not fail on them.
            CheckReach.Key(Scope.FailureTable, "A split or dividend not caught"),
            CheckReach.Key(NightlyRunSteps.Heading, "Check splits and dividends, and refetch the full year for any name affected."),
        ]);

    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    // The session the captured action file is for. AAPL went ex-dividend on
    // this date, which is a real action inside the stored year.
    static readonly DateTimeOffset ActionNight = new(2026, 8, 10, 21, 10, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Backfilled = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static Task<TemporaryStore> Stored() => Loaded(new TemporaryStore().Migrated());

    // The index and its stored year, loaded into a store the test has already migrated.
    static async Task<TemporaryStore> Loaded(TemporaryStore store)
    {
        var clock = FixedClock.At(Backfilled, SessionZones.UnitedStates);

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

    static CorporateActionChecker Checker(TemporaryStore store, IHistoricalBarFeed? history = null) =>
        new(RecordedCorporateActionFeed.FromFolder(FixtureFolder()),
            history ?? RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            FixedClock.At(ActionNight, SessionZones.UnitedStates),
            store.DatabaseFile);

    // A feed whose first attempt fails, wrapped in the real retry.
    //
    // 2.2's third done condition. A retry over a non-idempotent write is a
    // defect that only appears once both exist, and the refetch is the only
    // non-idempotent write on the nightly path: it deletes a name's year and
    // reinserts it. If a second attempt were made after the delete, the name
    // would hold two years or half of one.
    sealed class FlakyHistoricalFeed(IHistoricalBarFeed inner, RetryPolicy policy) : IHistoricalBarFeed
    {
        readonly ProviderRequest request = new(policy, (_, _) => Task.CompletedTask);
        readonly HashSet<string> refused = [];

        public int Requests => inner.Requests;

        public int Attempts => request.Attempts;

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(
            string ticker,
            DateOnly from,
            DateOnly to,
            CancellationToken cancellationToken = default) =>
            request.SendAsync(
                token => refused.Add(ticker)
                    ? throw new ProviderRefusal($"the feed refused {ticker} once", transient: true)
                    : inner.BarsAsync(ticker, from, to, token),
                cancellationToken);
    }

    [Fact]
    public async Task ARetriedRefetchLeavesTheYearOnceRatherThanTwice()
    {
        // Two stores and one difference between them. The refetch asks for the
        // year ending on the action night rather than on the backfill date, so
        // the row count legitimately moves; what must not move is whether a
        // retry happened. Comparing the flaky run against a clean one asks that
        // question directly, where comparing against the count before the
        // refetch would have asked a different one and failed on the answer.
        using var clean = await Stored();
        using var store = await Stored();

        var cleanOutcome = await Checker(clean).RunAsync(Index, "run-clean");

        var flaky = new FlakyHistoricalFeed(
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            RetryPolicy.Standard);

        var outcome = await Checker(store, flaky).RunAsync(Index, "run-retry");

        // The retry happened, so this is not a test that passed by not
        // exercising the path. Stated rather than inferred, because a flaky feed
        // that never refused would leave every assertion below true and the
        // property untested.
        Assert.Equal(2, flaky.Attempts);
        Assert.Equal(1, outcome.Refetched);
        Assert.Equal(cleanOutcome.Refetched, outcome.Refetched);

        // And the series is what one refetch produces. Not twice that, and not
        // half of it.
        Assert.Equal(Rows(clean, "AAPL"), Rows(store, "AAPL"));
        Assert.True(Rows(store, "AAPL") > 200, $"AAPL holds {Rows(store, "AAPL")} bars, expected a year.");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var duplicates = connection.CreateCommand();
        duplicates.CommandText =
            "SELECT COUNT(*) FROM (SELECT session_date FROM bar WHERE ticker = 'AAPL' " +
            "GROUP BY session_date HAVING COUNT(*) > 1);";

        Assert.Equal(0, Convert.ToInt32(duplicates.ExecuteScalar()));
    }

    static (string State, string? Reason) StateOf(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, reason FROM series_state WHERE ticker = $t;";
        command.Parameters.AddWithValue("$t", ticker);

        using var reader = command.ExecuteReader();

        return reader.Read()
            ? (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1))
            : ("absent", null);
    }

    static int Rows(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM bar WHERE ticker = $t;";
        command.Parameters.AddWithValue("$t", ticker);

        return Convert.ToInt32(command.ExecuteScalar());
    }

    // The stored series itself rather than its size. A rollback that restored a
    // count and not the values would satisfy a count, and the atomicity test
    // below is about which bars are there.
    static IReadOnlyList<string> Series(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT session_date, close FROM bar WHERE ticker = $t ORDER BY session_date;";
        command.Parameters.AddWithValue("$t", ticker);

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add($"{reader.GetString(0)}|{reader.GetString(1)}");
        }

        return rows;
    }

    [Fact]
    public void TheCapturedActionsAreRealAndCarryNamesTheIndexDoesNotHave()
    {
        // Stated first. A fixture whose only action was for a fixture name
        // would leave the membership filter untested, and one whose action was
        // constructed would not satisfy the done condition at all.
        var folder = FixtureFolder();

        var dividends = RecordedCorporateActionFeed.Parse(
            File.ReadAllText(Directory.GetFiles(folder, "actions-dividends-*.json").Single()),
            CorporateActionChecker.Exchange,
            ActionKind.Dividend);

        var splits = RecordedCorporateActionFeed.Parse(
            File.ReadAllText(Directory.GetFiles(folder, "actions-splits-*.json").Single()),
            CorporateActionChecker.Exchange,
            ActionKind.Split);

        Assert.Contains(dividends, action => action.Ticker == "AAPL");
        Assert.True(dividends.Count > 1, "The dividends file holds only the one name, so the filter is untested.");
        Assert.NotEmpty(splits);

        // The split's value is a ratio in a string, which is the shape the
        // capture settled and the one a parser written first would have missed.
        Assert.All(splits, action => Assert.Contains('/', action.Value));
        Assert.All(dividends, action => Assert.False(string.IsNullOrWhiteSpace(action.Value)));
    }

    [Fact]
    public async Task AnActionOnACurrentMemberTriggersARefetch()
    {
        using var store = await Stored();

        var before = Rows(store, "AAPL");
        var outcome = await Checker(store).RunAsync(Index, "run-actions");

        Assert.Equal(1, outcome.Refetched);
        Assert.Empty(outcome.Suspect);
        Assert.Equal(CorporateActionChecker.Ok, StateOf(store, "AAPL").State);

        // Replaced rather than added to, and the count is what the refetch
        // window yields rather than a number written here. The action night is
        // a month before the backfill night, so the refetched year ends earlier
        // and holds fewer sessions: a store that had added rather than replaced
        // would hold more than either.
        var window = (await RecordedHistoricalBarFeed.FromFolder(FixtureFolder())
            .BarsAsync("AAPL", new DateOnly(2025, 8, 10), new DateOnly(2026, 8, 10))).Count;

        Assert.True(before > 250, $"AAPL held {before} bars before the check.");
        Assert.True(window > 200, $"The refetch window yields {window} sessions, expected more than 200.");
        Assert.True(window < before, "The refetch window is not smaller than the stored year, so replacement and addition look the same.");
        Assert.Equal(window, Rows(store, "AAPL"));

        // Every replaced bar names the refetch as its source, which is how a
        // reader tells a restated series from an original one.
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM bar WHERE ticker = 'AAPL' AND source = $s;";
        command.Parameters.AddWithValue("$s", CorporateActionChecker.Source);

        Assert.Equal((long)Rows(store, "AAPL"), (long)command.ExecuteScalar()!);
    }

    [Fact]
    public async Task ANameTheIndexDoesNotHoldIsNotRefetched()
    {
        // The counter-test. The captured file carries three names that are not
        // members, and a checker that refetched what it was sent would try to
        // fetch a series the feed has no response for.
        using var store = await Stored();

        var outcome = await Checker(store).RunAsync(Index, "run-actions");

        Assert.True(outcome.Actions > outcome.Refetched,
            $"The feed returned {outcome.Actions} actions and {outcome.Refetched} were refetched, so nothing was filtered.");

        Assert.Equal(0, Rows(store, "AAMEKX"));
        Assert.Equal("absent", StateOf(store, "AAMEKX").State);
    }

    [Fact]
    public async Task AFailedCheckMarksTheNameAndLeavesItsSeriesAsItWas()
    {
        // Contradiction C. Without somewhere to put the state, a check that
        // could not run looked exactly like a check that found nothing.
        using var store = await Stored();

        var before = Rows(store, "AAPL");

        // A history feed with no response for the affected name, which is what
        // a provider outage looks like from here.
        var outcome = await Checker(store, new RecordedHistoricalBarFeed(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            .RunAsync(Index, "run-actions");

        Assert.Equal(0, outcome.Refetched);
        Assert.Equal(["AAPL"], outcome.Suspect);

        var state = StateOf(store, "AAPL");

        Assert.Equal(CorporateActionChecker.Suspect, state.State);
        Assert.False(string.IsNullOrWhiteSpace(state.Reason), "The name is suspect and nothing says why.");

        // The replacement is atomic: the year is not half gone.
        Assert.Equal(before, Rows(store, "AAPL"));
    }

    [Fact]
    public async Task ARefetchInterruptedAfterTheDeleteLeavesTheOldYearEntire()
    {
        // The obligation the phase 1 sign-off carried here, and it asserts the
        // property rather than the construct.
        //
        // The two tests above claim atomicity and cannot observe it: both induce
        // their failure upstream of the DELETE, so the year was never dropped
        // and there is no half-replaced state for them to catch. Removing the
        // transaction left the whole suite green, which is what a scan for the
        // keyword reports and what a behavioural test has to refuse.
        //
        // So the failure is induced downstream of the DELETE and partway through
        // the inserts. The feed answers with a year whose third bar repeats the
        // second's date, which parses cleanly and violates the bar table's
        // primary key on insert. By then the delete has run and two rows are in,
        // which is precisely the half-replaced series nothing could reach before.
        using var store = await Stored();

        var before = Series(store, "AAPL");

        Assert.True(before.Count > 2, $"AAPL holds {before.Count} bars, and the interruption needs a year to replace.");

        const string RepeatsASession = """
            [
             {"date":"2026-08-05","open":10,"high":11,"low":9,"close":10.5,"adjusted_close":10.5,"volume":100},
             {"date":"2026-08-06","open":10.5,"high":12,"low":10,"close":11,"adjusted_close":11,"volume":110},
             {"date":"2026-08-06","open":10.5,"high":12,"low":10,"close":11,"adjusted_close":11,"volume":110}
            ]
            """;

        var outcome = await Checker(store, new RecordedHistoricalBarFeed(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AAPL"] = RepeatsASession }))
            .RunAsync(Index, "run-actions");

        Assert.Equal(0, outcome.Refetched);
        Assert.Equal(["AAPL"], outcome.Suspect);

        // The property. The stored series is the old one entire, session by
        // session and price by price, and not the two bars that were written
        // before the third failed. Without the transaction this holds 2 rows,
        // both of them the interrupted refetch's.
        Assert.Equal(before, Series(store, "AAPL"));

        // And named on the values, not the dates. The interrupted refetch's two
        // sessions are dates the real series also holds, so an assertion on the
        // date would have passed over a half-replaced year; what it cannot hold
        // is those sessions at the refetch's own prices.
        Assert.DoesNotContain("2026-08-05|10.5", Series(store, "AAPL"));
        Assert.DoesNotContain("2026-08-06|11", Series(store, "AAPL"));

        var state = StateOf(store, "AAPL");

        Assert.Equal(CorporateActionChecker.Suspect, state.State);
        Assert.False(string.IsNullOrWhiteSpace(state.Reason), "The name is suspect and nothing says why.");

        // And the reason is the insert failing, not a guard that fired before
        // the delete. This is the assertion that keeps the interruption where it
        // has to be: the first version of this test dated its bars outside the
        // window the refetch asks for, the feed filtered them all away, and the
        // empty-refetch guard refused upstream of the delete. It passed with the
        // transaction removed, which is how it was caught.
        Assert.Contains("UNIQUE", state.Reason!, StringComparison.OrdinalIgnoreCase);

        // The other names are untouched, which is why each name refetches in its
        // own transaction rather than the whole check in one.
        Assert.NotEmpty(Series(store, "MSFT"));
    }

    [Fact]
    public async Task AnEmptyRefetchIsRefusedRatherThanEmptyingTheYear()
    {
        // The failure this guard exists for. A provider answering with an empty
        // array is not an outage a caller notices, and dropping the year before
        // discovering it is empty would delete a year of bars in response to a
        // successful request.
        using var store = await Stored();

        var before = Rows(store, "AAPL");

        var outcome = await Checker(store, new RecordedHistoricalBarFeed(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["AAPL"] = "[]" }))
            .RunAsync(Index, "run-actions");

        Assert.Equal(["AAPL"], outcome.Suspect);
        Assert.Equal(before, Rows(store, "AAPL"));
        Assert.Contains("no bars", StateOf(store, "AAPL").Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASuspectNameBecomesOkAgainWhenItsCheckSucceeds()
    {
        // The state is a statement about now rather than a history, so it has
        // to be able to clear. A mark that only ever went one way would leave
        // every name suspect forever after one bad night.
        using var store = await Stored();

        await Checker(store, new RecordedHistoricalBarFeed(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            .RunAsync(Index, "run-bad");

        Assert.Equal(CorporateActionChecker.Suspect, StateOf(store, "AAPL").State);

        await Checker(store).RunAsync(Index, "run-good");

        Assert.Equal(CorporateActionChecker.Ok, StateOf(store, "AAPL").State);
        Assert.Null(StateOf(store, "AAPL").Reason);
    }

    // A day with no action on any name, which is most days.
    sealed class NoActionFeed : ICorporateActionFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<CorporateAction>> ActionsAsync(
            string exchange,
            DateOnly session,
            CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<CorporateAction>>([]);
        }
    }

    // One action on one name, on the sessions it is handed and on no other, so the night
    // an action lands on a suspect name is one a test chooses rather than the captured
    // day's. A dividend, being the action most members carry.
    internal sealed class ActionOnSessions(string ticker, params DateOnly[] sessions) : ICorporateActionFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<CorporateAction>> ActionsAsync(
            string exchange,
            DateOnly session,
            CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<CorporateAction>>(
                sessions.Contains(session) ? [new CorporateAction(ticker, session, ActionKind.Dividend, "0.26")] : []);
        }
    }

    // The sessions the exchange traded, from one on, read off the closure table rather
    // than counted in calendar days, because the retries are counted in nights and a
    // night on a day the exchange did not trade fetches nothing.
    internal static IReadOnlyList<DateOnly> SessionsFrom(DateOnly first, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = first; sessions.Count < count; day = day.AddDays(1))
        {
            if (EquityBrief.Core.Bars.ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return sessions;
    }

    // A night of the check at 21:10 UTC on a session, which is the night's own instant.
    internal static DateTimeOffset NightOn(DateOnly session) =>
        new(session.ToDateTime(new TimeOnly(21, 10)), TimeSpan.Zero);

    static (string State, int Retries, string CheckedAt) CountOf(TemporaryStore store, string ticker)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT state, retries, checked_at FROM series_state WHERE ticker = $t;";
        command.Parameters.AddWithValue("$t", ticker);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), $"{ticker} carries no series state row.");

        return (reader.GetString(0), reader.GetInt32(1), reader.GetString(2));
    }

    static (string Outcome, int Requests, string Detail) StageOf(TemporaryStore store, string runId)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT outcome, network_requests, detail FROM run_log WHERE run_id = $r AND stage = 'actions';";
        command.Parameters.AddWithValue("$r", runId);

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), $"{runId} wrote no actions row.");

        return (reader.GetString(0), reader.GetInt32(1), reader.GetString(2));
    }

    // A history feed holding no response for anyone, which is a refetch that fails every
    // night it is asked, and counts each ask.
    static RecordedHistoricalBarFeed Refusing() =>
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    // The run page's stale and failed region for a night's session, drawn from the run log
    // by the read surface's own projection, which is where a person reads a suspect name.
    static async Task<string> FailedRegionOn(TemporaryStore store, DateOnly session)
    {
        var api = new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, FixedClock.At(NightOn(session), SessionZones.UnitedStates));
        var failed = EquityBrief.Api.Reading.RunScreen.Failed(EquityBrief.Api.Reading.RunScreen.Stages(await api.RunLogAsync(session)));

        return System.Net.WebUtility.HtmlDecode(new EquityBrief.Web.Marks.MarkRenderer().StaleAndFailed([], failed, [], []));
    }

    [Fact]
    public async Task ANameWhoseRetriesAreSpentIsNotAskedForAndTheRunPageNamesItOnEveryNightItStaysSuspect()
    {
        // The ruling 6.0 owed and 6.11 found unwritten. A retry with no bound made one
        // per-name request on every night a failure lasted, and a bound alone would have
        // turned that into a name nothing asks for and nothing names. So this asserts both
        // halves: that the retries end, and what the name is left as where a person reads it.
        using var store = await Stored();

        var sessions = SessionsFrom(DateOnly.FromDateTime(ActionNight.UtcDateTime), CorporateActionChecker.RetryNights + 3);

        // The captured action lands on AAPL and its refetch fails.
        var marked = await Checker(store, Refusing()).RunAsync(Index, "night-0");

        Assert.Equal(["AAPL"], marked.Suspect);
        Assert.Equal(0, CountOf(store, "AAPL").Retries);

        // Named on the run page from the night that marked it, the reason held back until
        // the retries are spent, which is where the runbook sends the operator to read it.
        var markedRegion = await FailedRegionOn(store, sessions[0]);

        Assert.Contains("actions: partial.", markedRegion, StringComparison.Ordinal);
        Assert.Contains("suspect: AAPL", markedRegion, StringComparison.Ordinal);

        // Asked for again on each of the nights the limit allows, each failure counted, and
        // named on the run page on each of them.
        for (var night = 1; night <= CorporateActionChecker.RetryNights; night++)
        {
            var retry = await new CorporateActionChecker(new NoActionFeed(), Refusing(), FixedClock.At(NightOn(sessions[night]), SessionZones.UnitedStates), store.DatabaseFile)
                .RunAsync(Index, $"night-{night}");

            Assert.Equal(["AAPL"], retry.Retried ?? []);
            Assert.Equal(1, retry.RefetchRequests);
            Assert.Equal(["AAPL"], retry.Suspect);
            Assert.Empty(retry.Spent ?? []);
            Assert.Equal(("suspect", night), (CountOf(store, "AAPL").State, CountOf(store, "AAPL").Retries));

            var retryRegion = await FailedRegionOn(store, sessions[night]);

            Assert.Contains("actions: partial.", retryRegion, StringComparison.Ordinal);
            Assert.Contains("suspect: AAPL", retryRegion, StringComparison.Ordinal);
        }

        var lastAsked = CountOf(store, "AAPL").CheckedAt;
        var reason = StateOf(store, "AAPL").Reason!;

        // The nights after: nothing asked, the row as the last retry left it, and the stage
        // partial and naming the name, when it was last asked for and why, on each of them.
        for (var night = CorporateActionChecker.RetryNights + 1; night <= CorporateActionChecker.RetryNights + 2; night++)
        {
            var runId = $"night-{night}";
            var clock = FixedClock.At(NightOn(sessions[night]), SessionZones.UnitedStates);
            var quiet = await new CorporateActionChecker(new NoActionFeed(), Refusing(), clock, store.DatabaseFile).RunAsync(Index, runId);

            Assert.Equal(0, quiet.RefetchRequests);
            Assert.Empty(quiet.Retried ?? []);
            Assert.Empty(quiet.Suspect);

            var spent = Assert.Single(quiet.Spent ?? []);

            Assert.Equal(new SpentName("AAPL", lastAsked, reason), spent);
            Assert.Equal(("suspect", CorporateActionChecker.RetryNights, lastAsked), CountOf(store, "AAPL"));

            var stage = StageOf(store, runId);

            Assert.Equal("partial", stage.Outcome);
            Assert.Contains($"1 left suspect with {CorporateActionChecker.RetryNights} nightly retries spent", stage.Detail, StringComparison.Ordinal);
            Assert.Contains($"AAPL, last asked for at {lastAsked}, because {reason}", stage.Detail, StringComparison.Ordinal);

            // The surface a person reads it on, now with when it was last asked for and why.
            var region = await FailedRegionOn(store, sessions[night]);

            Assert.Contains("actions: partial.", region, StringComparison.Ordinal);
            Assert.Contains($"AAPL, last asked for at {lastAsked}, because {reason}", region, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AnActionOnANameWhoseRetriesAreSpentAsksForItAgainAndStartsItsCountAgain()
    {
        // What ends a spent name's silence on the network, being the only thing that does:
        // a new action is a new reason to ask, so the night one lands asks for the name and
        // counts from none again, and the night after asks again.
        using var store = await Stored();

        var sessions = SessionsFrom(DateOnly.FromDateTime(ActionNight.UtcDateTime), CorporateActionChecker.RetryNights + 4);

        await Checker(store, Refusing()).RunAsync(Index, "night-0");

        for (var night = 1; night <= CorporateActionChecker.RetryNights + 1; night++)
        {
            await new CorporateActionChecker(new NoActionFeed(), Refusing(), FixedClock.At(NightOn(sessions[night]), SessionZones.UnitedStates), store.DatabaseFile)
                .RunAsync(Index, $"night-{night}");
        }

        Assert.Equal(CorporateActionChecker.RetryNights, CountOf(store, "AAPL").Retries);

        var landing = sessions[CorporateActionChecker.RetryNights + 2];
        var acted = await new CorporateActionChecker(new ActionOnSessions("AAPL", landing), Refusing(), FixedClock.At(NightOn(landing), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, "night-action");

        Assert.Equal(1, acted.RefetchRequests);
        Assert.Equal(["AAPL"], acted.Suspect);
        Assert.Empty(acted.Spent ?? []);
        Assert.Equal(0, CountOf(store, "AAPL").Retries);

        var after = sessions[CorporateActionChecker.RetryNights + 3];
        var retried = await new CorporateActionChecker(new NoActionFeed(), Refusing(), FixedClock.At(NightOn(after), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, "night-after");

        Assert.Equal(["AAPL"], retried.Retried ?? []);
        Assert.Equal(1, retried.RefetchRequests);
        Assert.Equal(1, CountOf(store, "AAPL").Retries);

        // And a refetch that succeeds on a retry night clears the count with the state.
        var cleared = await new CorporateActionChecker(new NoActionFeed(), RecordedHistoricalBarFeed.FromFolder(FixtureFolder()), FixedClock.At(NightOn(after), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, "night-cleared");

        Assert.Empty(cleared.Suspect);
        Assert.Equal(("ok", 0), (CountOf(store, "AAPL").State, CountOf(store, "AAPL").Retries));
        Assert.Equal("ok", StageOf(store, "night-cleared").Outcome);
    }

    [Fact]
    public async Task ANameAlreadySuspectWhenItsCountIsAddedIsAskedForAgainOnTheNightsTheLimitAllowsFromThen()
    {
        // The migration's default. A row that exists when the count is added was counted by
        // nothing, and the rule counts from the night it lands, so such a name starts at none
        // and is asked for again on each of the limit's nights from then. Every other test
        // here migrates an empty store and has the check write each count itself, so a
        // default counting such a row as one passed all of them. Found by the 6.0 ruling's
        // sweep.
        var counted = SchemaMigrations.All.Single(migration => migration.Name == "add series_state.retries");

        using var store = new TemporaryStore();

        new MigrationRunner(SchemaMigrations.All.Where(migration => migration.Version < counted.Version).ToArray())
            .Apply(store.DatabaseFile);

        // A name a night before the count existed left suspect, in the columns that night's
        // check wrote.
        store.Execute(
            "INSERT INTO series_state (ticker, state, reason, checked_at) " +
            "VALUES ('AAPL', 'suspect', 'a refetch that failed before the count existed', '2026-08-10T21:10:00Z');");

        var migrated = MigrationRunner.Standard().Apply(store.DatabaseFile);

        Assert.Equal(counted.Version - 1, migrated.From);
        Assert.Contains(counted.Name, migrated.Applied);
        Assert.Equal(("suspect", 0), (CountOf(store, "AAPL").State, CountOf(store, "AAPL").Retries));

        await Loaded(store);

        var sessions = SessionsFrom(DateOnly.FromDateTime(ActionNight.UtcDateTime), CorporateActionChecker.RetryNights + 2);

        for (var night = 1; night <= CorporateActionChecker.RetryNights + 1; night++)
        {
            var outcome = await new CorporateActionChecker(new NoActionFeed(), Refusing(), FixedClock.At(NightOn(sessions[night]), SessionZones.UnitedStates), store.DatabaseFile)
                .RunAsync(Index, $"night-{night}");

            var asked = night <= CorporateActionChecker.RetryNights;

            Assert.Equal(asked ? 1 : 0, outcome.RefetchRequests);
            Assert.Equal(!asked, (outcome.Spent ?? []).Any(spent => spent.Ticker == "AAPL"));
        }
    }

    static string RunIdOf(DateOnly session) =>
        "night-" + session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    // One night of the check on a session, with no action unless one is handed in.
    static Task<ActionCheckOutcome> NightOf(TemporaryStore store, DateOnly session, IHistoricalBarFeed history, ICorporateActionFeed? actions = null) =>
        new CorporateActionChecker(actions ?? new NoActionFeed(), history, FixedClock.At(NightOn(session), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, RunIdOf(session));

    // AAPL marked on its captured action's session, Monday 2026-08-10, and asked for on the
    // five sessions after it, the last being Monday 2026-08-17, every refetch failing.
    static async Task SpendRetries(TemporaryStore store)
    {
        await Checker(store, Refusing()).RunAsync(Index, "night-0");

        foreach (var session in SessionsFrom(new DateOnly(2026, 8, 11), CorporateActionChecker.RetryNights))
        {
            await NightOf(store, session, Refusing());
        }

        Assert.Equal((CorporateActionChecker.RetryNights, "2026-08-17T21:10:00Z"), (CountOf(store, "AAPL").Retries, CountOf(store, "AAPL").CheckedAt));
    }

    [Fact]
    public async Task ANameWhoseRetriesAreSpentIsAskedForAgainOnTheFirstSessionAWeekOnFromTheOneItWasLastAskedFor()
    {
        // The 7.0 ruling. A spent name waited for another action to land on it, which for a
        // split on a name paying no dividend may never come, so a failure the provider had
        // since cleared stayed in the store. It is asked for again weekly: seven calendar
        // days on from the session it was last asked for, on that session or on the first
        // after it the exchange traded, counting on past the limit, until a refetch succeeds.
        using var store = await Stored();

        await SpendRetries(store);

        // Asked for on each session a week or more on and on none before. The weeks fall on
        // a Monday, on a Monday the exchange closed for Labor Day, which moves the week to the
        // Tuesday, and then on a Monday six days after that Tuesday, which is not a week.
        (DateOnly Session, bool Asked)[] nights =
        [
            (new(2026, 8, 21), false),
            (new(2026, 8, 24), true),
            (new(2026, 8, 28), false),
            (new(2026, 8, 31), true),
            (new(2026, 9, 4), false),
            (new(2026, 9, 8), true),
            (new(2026, 9, 14), false),
        ];

        var retries = CorporateActionChecker.RetryNights;

        foreach (var (session, asked) in nights)
        {
            var outcome = await NightOf(store, session, Refusing());

            Assert.True(
                (asked ? 1 : 0) == outcome.RefetchRequests,
                session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + $" asked for AAPL {outcome.RefetchRequests} time(s).");

            retries += asked ? 1 : 0;

            Assert.Equal(("suspect", retries), (CountOf(store, "AAPL").State, CountOf(store, "AAPL").Retries));
            Assert.Equal(asked, (outcome.Retried ?? []).Contains("AAPL"));
            Assert.Equal(!asked, (outcome.Spent ?? []).Any(spent => spent.Ticker == "AAPL"));
            Assert.Equal("partial", StageOf(store, RunIdOf(session)).Outcome);
        }

        // The week after Tuesday 2026-09-08 comes round on Tuesday 2026-09-15, and a refetch
        // that succeeds there clears the state and the count, after which nothing asks.
        var cleared = await NightOf(store, new DateOnly(2026, 9, 15), RecordedHistoricalBarFeed.FromFolder(FixtureFolder()));

        Assert.Equal(1, cleared.RefetchRequests);
        Assert.Empty(cleared.Suspect);
        Assert.Equal(("ok", 0), (CountOf(store, "AAPL").State, CountOf(store, "AAPL").Retries));

        var after = await NightOf(store, new DateOnly(2026, 9, 22), Refusing());

        Assert.Equal(0, after.RefetchRequests);
        Assert.Equal("ok", StageOf(store, RunIdOf(new DateOnly(2026, 9, 22))).Outcome);
    }

    [Fact]
    public async Task ASpentNamesWeekIsCountedFromTheSessionItWasLastAskedForWhenThatNightStartedAfterMidnightUtc()
    {
        // A night that starts after midnight UTC and before midnight in New York, being a
        // scheduled start the machine missed and ran late or a night run by hand in the
        // evening, reads the system clock, so the instant a name was last asked for carries
        // the next day's UTC date while belonging to the session before it. The week counts
        // from that session. Every other night here runs at 21:10 UTC, where a night run for
        // a named session puts its clock and where the two dates agree, so a week counted from
        // the instant's UTC date passed all of them while asking for such a name a day late.
        // Found by the 7.0 ruling's sweep.
        using var store = await Stored();

        await Checker(store, Refusing()).RunAsync(Index, "night-0");

        var retryNights = SessionsFrom(new DateOnly(2026, 8, 11), CorporateActionChecker.RetryNights);

        foreach (var session in retryNights.SkipLast(1))
        {
            await NightOf(store, session, Refusing());
        }

        // The last of the nightly retries is Monday 2026-08-17's night, asked at 00:42 UTC on
        // the Tuesday.
        var last = retryNights.Last();
        var pastMidnight = new DateTimeOffset(2026, 8, 18, 0, 42, 0, TimeSpan.Zero);

        await new CorporateActionChecker(new NoActionFeed(), Refusing(), FixedClock.At(pastMidnight, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(Index, RunIdOf(last));

        Assert.Equal(new DateOnly(2026, 8, 17), last);
        Assert.Equal((CorporateActionChecker.RetryNights, "2026-08-18T00:42:00Z"), (CountOf(store, "AAPL").Retries, CountOf(store, "AAPL").CheckedAt));

        // Monday 2026-08-24 is 7 days on from that session and 6 from the instant's UTC date,
        // and its night, at 21:10 UTC, asks for the name.
        var week = await NightOf(store, new DateOnly(2026, 8, 24), Refusing());

        Assert.Equal(1, week.RefetchRequests);
        Assert.Equal(["AAPL"], week.Retried ?? []);
        Assert.Empty(week.Spent ?? []);
        Assert.Equal(CorporateActionChecker.RetryNights + 1, CountOf(store, "AAPL").Retries);
    }

    [Fact]
    public async Task ASuspectNameTheIndexNoLongerHoldsIsNeitherAskedForNorNamed()
    {
        // The property the check's membership clause carries, which the phase 6 sign-off's
        // sweep found held by a source scan alone, and which the weekly retry makes a
        // behaviour worth a test: a name asked for once a week is asked for until something
        // ends it, and for a name the provider no longer serves what ends it is the index.
        // Two names, AAPL with its retries spent and one with retries left, both leaving.
        using var store = await Stored();

        await SpendRetries(store);

        var other = ScalarOf(store, "SELECT DISTINCT ticker FROM membership WHERE index_code = 'GSPC' AND \"left\" IS NULL AND ticker <> 'AAPL' ORDER BY ticker LIMIT 1;");

        store.Execute(
            "INSERT INTO series_state (ticker, state, reason, checked_at, retries) " +
            $"VALUES ('{other}', 'suspect', 'a refetch that failed', '2026-08-20T21:10:00Z', 1);");

        // While both are members, the one with retries left is asked for and the spent one,
        // whose week has not come round, is named.
        var members = await NightOf(store, new DateOnly(2026, 8, 21), Refusing());

        Assert.Equal([other], members.Retried ?? []);
        Assert.Equal(["AAPL"], (members.Spent ?? []).Select(spent => spent.Ticker));

        // Both leave on Saturday 2026-08-22, so Monday 2026-08-24, when AAPL's week comes round
        // and the other's next retry is due, asks for neither and names neither.
        store.Execute($"UPDATE membership SET \"left\" = '2026-08-22' WHERE ticker IN ('AAPL', '{other}');");

        var left = await NightOf(store, new DateOnly(2026, 8, 24), Refusing());

        Assert.Equal(0, left.RefetchRequests);
        Assert.Empty(left.Retried ?? []);
        Assert.Empty(left.Spent ?? []);
        Assert.Empty(left.Suspect);

        var stage = StageOf(store, RunIdOf(new DateOnly(2026, 8, 24)));

        Assert.Equal("ok", stage.Outcome);
        Assert.DoesNotContain("AAPL", stage.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(other, stage.Detail, StringComparison.Ordinal);
    }

    static string ScalarOf(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return (string)command.ExecuteScalar()!;
    }

    // The API over a test store, in process, for the routes the name page and the file are
    // served from.
    sealed class SurfaceHost(string root) : WebApplicationFactory<ReadApi>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.UseSetting(StoreLocation.DataRootKey, root);
    }

    [Fact]
    public async Task ASuspectNamesPageItsExportedReportAndItsRowOnTonightsListSaySoUntilARefetchSucceeds()
    {
        // The half of section 18's row the 7.0 ruling added to what a person sees: the name
        // page opens with a line saying its prices may not reflect a recent dividend or split,
        // with when the refetch was last tried and why, the exported report carries it, and
        // the name's row on tonight's list says so beside the name. Read over the row the
        // check itself wrote, through the routes the page and the file are served from and the
        // projection tonight's route draws its rows with.
        using var store = await Stored();

        await Checker(store, Refusing()).RunAsync(Index, "night-0");

        var lastTried = CountOf(store, "AAPL").CheckedAt;
        var reason = StateOf(store, "AAPL").Reason!;
        var read = new ReadApi(store.DatabaseFile, FixedClock.At(ActionNight, SessionZones.UnitedStates));

        Assert.Equal(new SuspectSeriesRow("AAPL", reason, lastTried, 0), Assert.Single(await read.SuspectSeriesAsync()));

        // One host for the whole test: the API records its start under an id taken from the
        // second, so a second host started in the same second is refused at the run log.
        using var host = new SurfaceHost(store.Root);
        using var client = host.CreateClient();

        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAPL"));
            var file = WebUtility.HtmlDecode(await client.GetStringAsync(ReportExporter.Route + "AAPL"));

            foreach (var surface in new[] { page, file })
            {
                Assert.Contains("AAPL's prices may not reflect a recent dividend or split", surface, StringComparison.Ordinal);
                Assert.Contains($"Last tried {lastTried}, because {reason}", surface, StringComparison.Ordinal);
            }

            // Above everything the page draws from those prices, the trend state first among them.
            Assert.True(
                page.IndexOf("class=\"prices-suspect\"", StringComparison.Ordinal) < page.IndexOf("class=\"trend-state\"", StringComparison.Ordinal),
                "The line is not above the figures the page draws.");
        }

        Assert.Contains("prices may not reflect a dividend or split", ListRowFor(await read.SuspectSeriesAsync()), StringComparison.Ordinal);

        // Once a refetch succeeds none of the three says so.
        await NightOf(store, new DateOnly(2026, 8, 11), RecordedHistoricalBarFeed.FromFolder(FixtureFolder()));

        Assert.Empty(await read.SuspectSeriesAsync());

        Assert.DoesNotContain("prices-suspect", await client.GetStringAsync("/screens/name/AAPL"), StringComparison.Ordinal);
        Assert.DoesNotContain("prices-suspect", await client.GetStringAsync(ReportExporter.Route + "AAPL"), StringComparison.Ordinal);

        Assert.DoesNotContain("prices-suspect", ListRowFor(await read.SuspectSeriesAsync()), StringComparison.Ordinal);
    }

    // AAPL's row on a list it fired on, drawn by tonight's projection and the list mark from
    // the suspect rows handed in.
    static string ListRowFor(IReadOnlyList<SuspectSeriesRow> suspects)
    {
        var night = DateOnly.FromDateTime(ActionNight.UtcDateTime);
        var listing = new ListingRow("AAPL", night, "[{\"name\":\"at support\",\"fired\":true,\"values\":{}}]", 1, "{}");

        var rows = TonightScreen.Rows(
            night,
            [listing],
            new Dictionary<string, int>(StringComparer.Ordinal),
            new Dictionary<string, UniverseCell>(StringComparer.Ordinal),
            [],
            suspects);

        return WebUtility.HtmlDecode(new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn));
    }

    [Fact]
    public async Task ASuspectNameIsRefetchedOnTheNextNightWithoutAnActionOfItsOwn()
    {
        // The test above clears a suspect name by running the same action night
        // again, which is not what a night does: the action is on the day it
        // lands and not after. Until the phase 5 sign-off the mark was written
        // and never read, so a name whose refetch failed stayed suspect, with its
        // stored history unadjusted for an action the provider had applied,
        // until another action happened to land on it. Found by the phase 5
        // sign-off reviewer.
        using var store = await Stored();

        await Checker(store, new RecordedHistoricalBarFeed(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)))
            .RunAsync(Index, "run-bad");

        Assert.Equal(CorporateActionChecker.Suspect, StateOf(store, "AAPL").State);

        // The next night carries no action for anyone.
        var history = RecordedHistoricalBarFeed.FromFolder(FixtureFolder());
        var outcome = await new CorporateActionChecker(
            new NoActionFeed(),
            history,
            FixedClock.At(ActionNight.AddDays(1), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-retry");

        Assert.Equal(["AAPL"], outcome.Retried ?? []);
        Assert.Equal(1, outcome.Refetched);
        Assert.Equal(1, outcome.RefetchRequests);
        Assert.Empty(outcome.Suspect);
        Assert.Equal(CorporateActionChecker.Ok, StateOf(store, "AAPL").State);

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT detail FROM run_log WHERE run_id = 'run-retry' AND stage = 'actions';";

        Assert.Contains("1 retried from an earlier night: AAPL", (string)command.ExecuteScalar()!, StringComparison.Ordinal);

        // And once it is ok, the night after asks nothing for it.
        var quiet = await new CorporateActionChecker(
            new NoActionFeed(),
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            FixedClock.At(ActionNight.AddDays(2), SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-quiet");

        Assert.Empty(quiet.Retried ?? []);
        Assert.Equal(0, quiet.RefetchRequests);
    }

    [Fact]
    public async Task TheCheckCostsTwoRequestsAndOnePerRefetchedNameAndTheRunLogSaysSo()
    {
        // One per kind, and neither grows with the universe. The refetch is a
        // per-name call bounded by the day's actions rather than by the index,
        // which the limits row carves out in so many words.
        //
        // And it is counted. This test was "costs two requests" until the phase
        // 5 sign-off and asserted the stage reported two while the fixture
        // refetches AAPL, so it held the undercount in place: the run log row
        // left out exactly the per-name request the carve-out is about, and the
        // third by-hand night of 2026-09-09 recorded two where it made eleven.
        using var store = await Stored();

        var feed = RecordedCorporateActionFeed.FromFolder(FixtureFolder());
        var history = RecordedHistoricalBarFeed.FromFolder(FixtureFolder());
        var historyBefore = history.Requests;

        var outcome = await new CorporateActionChecker(
            feed,
            history,
            FixedClock.At(ActionNight, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-actions");

        Assert.Equal(2, feed.Requests);

        // One refetch per affected member, counted off the feed that made it.
        Assert.Equal(1, outcome.Refetched);
        Assert.Equal(outcome.Refetched, history.Requests - historyBefore);
        Assert.Equal(outcome.Refetched, outcome.RefetchRequests);

        // The stage's figure is both feeds together, and the row the operator
        // reads carries that figure rather than the action feed's alone.
        Assert.Equal(feed.Requests + outcome.RefetchRequests, outcome.Requests);

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT network_requests, detail FROM run_log WHERE run_id = 'run-actions' AND stage = 'actions';";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read(), "The check left no run log row.");
        Assert.Equal(3L, reader.GetInt64(0));
        Assert.Contains("1 of the 3 request(s) per name", reader.GetString(1), StringComparison.Ordinal);
    }
}
