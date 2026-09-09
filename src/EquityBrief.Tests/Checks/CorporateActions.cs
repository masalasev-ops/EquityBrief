using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
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
            CheckReach.Key(Scope.CatalogueTable, "Corporate action checker"),
            CheckReach.Key(Scope.MatrixTable, "Corporate action checker"),
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

    static async Task<TemporaryStore> Stored()
    {
        var store = new TemporaryStore().Migrated();
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

    [Fact]
    public async Task TheCheckCostsTwoRequestsWhateverTheUniverseIs()
    {
        // One per kind, and neither grows with the universe. The refetch is a
        // per-name call and is bounded by actions rather than by the index,
        // which the limits row now carves out in so many words.
        using var store = await Stored();

        var feed = RecordedCorporateActionFeed.FromFolder(FixtureFolder());
        var outcome = await new CorporateActionChecker(
            feed,
            RecordedHistoricalBarFeed.FromFolder(FixtureFolder()),
            FixedClock.At(ActionNight, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-actions");

        Assert.Equal(2, feed.Requests);
        Assert.Equal(2, outcome.Requests);
    }
}
