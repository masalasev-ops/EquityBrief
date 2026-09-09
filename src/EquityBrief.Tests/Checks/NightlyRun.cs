using System.Diagnostics;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// nightly-run. The night runs the steps that exist, in the order section 14
// states, and a failure names the step and exits non-zero.
//
// Section 14's list is a claim about the run rather than about a component, and
// it is the ordering that carries it: membership before the backfill, because a
// backfill asks which names are members; the backfill before the fetch, because
// a name with no year is a name the fetch would leave with one session.
//
// Only four of the nine steps exist and the rest are absent rather than
// stubbed, so this asserts the order of what runs rather than the length of the
// list. A step that printed "skipped" would be a step a reader counts as run.
public class NightlyRun
{
    internal static CheckReach Reach => new(
        "nightly-run",
        ["docs/ARCHITECTURE.html", "fixtures/membership-2026-09-05"],
        [
            CheckReach.Key(NightlyRunSteps.Heading, "Load index membership and record any joins and leaves."),
            CheckReach.Key(NightlyRunSteps.Heading, "Backfill one year for any member with no stored history, which on the first run is every name and afterwards is only a new joiner."),
            CheckReach.Key(NightlyRunSteps.Heading, "Fetch the day's bulk bar file, one request, and store the bars for current members."),
            CheckReach.Key(NightlyRunSteps.Heading, "Fetch the index's dated events for the horizon, one request, and store what the provider files (see: A calendar event is fetched once for the whole index, and the calendar holds provider events only)."),
            CheckReach.Key(NightlyRunSteps.Heading, "Compute the indicators for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Mark the swings for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Build the volume profile for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Build the levels for every name."),
            CheckReach.Key(NightlyRunSteps.Heading, "Classify the trend state and build the ladder for every name, writing a row whether or not it carries a tranche (see: A ladder row is written for every index member every night) (see: The trend classifier returns its label to the ladder builder)."),
            CheckReach.Key(Scope.LimitsTable, "Per-request timeout and the night's deadline"),
            CheckReach.Key(Scope.FailureTable, "Bulk price feed unavailable, run log"),
            CheckReach.Key(Scope.FailureTable, "A feed answers with a session other than the one asked for"),
            CheckReach.Key(Scope.FailureTable, "A feed answers with none of the index in it"),
        ]);

    const string Fixture = "membership-2026-09-05";

    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static async Task<(int Code, string Output, string Error)> NightAsync(
        TemporaryStore store,
        string? fixture = null,
        string? runId = null,
        IBulkPriceFeed? bulk = null,
        TimeSpan? deadline = null,
        IClock? clock = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            fixture ?? FixtureFolder(),
            "GSPC",
            clock ?? FixedClock.At(Night, SessionZones.UnitedStates),
            output,
            error,
            runId,
            bulk,
            deadline);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public void TheLimitsRowStatesTheFiguresTheCodeUses()
    {
        // The first clause of the Per-request timeout and the night's deadline
        // note. It stood in ProviderRequestTests, where it ran, passed and
        // backed no verdict, because a check's tests are the ones its carrier
        // declares. Moved rather than copied: a limit stated in a document and
        // again in code is two places holding one fact, and the same assertion
        // in two test classes is a third.
        //
        // The row is read rather than repeated, so a figure changed in either
        // place fails here.
        var row = Corpus.Read("docs/ARCHITECTURE.html");
        var at = row.IndexOf("Per-request timeout and the night's deadline", StringComparison.Ordinal);

        Assert.True(at >= 0, "Section 17 no longer carries the timeout and deadline row.");

        var cell = row[at..row.IndexOf("</tr>", at, StringComparison.Ordinal)];
        var policy = RetryPolicy.Standard;

        Assert.Contains($"at most {policy.Attempts} attempts", cell, StringComparison.Ordinal);
        Assert.Contains($"{policy.FirstWait.TotalSeconds:0} seconds and then {policy.FirstWait.TotalSeconds * 2:0}", cell, StringComparison.Ordinal);
        Assert.Contains($"bounded by {policy.Timeout.TotalSeconds:0} seconds", cell, StringComparison.Ordinal);
        Assert.Contains($"bounded by {policy.Deadline.TotalMinutes:0} minutes", cell, StringComparison.Ordinal);
    }

    // A payload that arrives and is wrong, in the two shapes section 18 now
    // carries. Both are induced against the fixture rather than described.
    //
    // A feed that did not answer and a feed that answered with something else
    // are different behaviours, and only the second can be mistaken for a night
    // that ran. That is the whole reason "unavailable" needed a definition
    // before these rows could be written.
    // see: A feed is unavailable when it does not answer, and wrong when it answers with something else

    [Fact]
    public async Task APayloadForAnotherSessionIsRefusedAndTheStoredBarsAreUnchanged()
    {
        using var store = new TemporaryStore();

        // A clean night first, so there is a stored series for the refusal to
        // leave alone. A test that induced the failure against an empty store
        // would prove that nothing was written where nothing could have been.
        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var before = Count(store);

        Assert.True(before > 700, $"The store held {before} bars before the refusal, expected a year.");

        // The fixture's file is for 2026-09-08. This night asks for the session
        // after it, which is what a night run a day later against a stale file
        // looks like from here.
        var (code, _, error) = await NightAsync(
            store,
            runId: "night-stale",
            clock: FixedClock.At(new DateTimeOffset(2026, 9, 9, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.Equal(1, code);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
        Assert.Contains("2026-09-08", error, StringComparison.Ordinal);
        Assert.Contains("2026-09-09 was asked for", error, StringComparison.Ordinal);

        // The stored series is exactly as it was. Not shortened, not added to.
        Assert.Equal(before, Count(store));
    }

    [Fact]
    public async Task APayloadHoldingNoneOfTheIndexIsRefusedBeforeAnythingIsStored()
    {
        using var store = new TemporaryStore();

        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var before = Count(store);

        // A file with rows in it and none of them ours, which is what a night
        // run before the close produced against the live provider: the payload
        // was full of symbols this index does not hold and carried nothing for
        // any of its five hundred members.
        var none = new ShortBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), "AAPL", "MSFT", "KEYS", "NFLX");

        var (code, _, error) = await NightAsync(store, runId: "night-none", bulk: none);

        Assert.Equal(1, code);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
        Assert.Contains("carries nothing for any of the 4 current member(s)", error, StringComparison.Ordinal);
        Assert.Equal(before, Count(store));
    }

    [Fact]
    public async Task APayloadShortOfSomeMembersIsStoredForTheRestAndSaysHowMany()
    {
        // The counter-test, and it is the one the live night wrote. Two of 503
        // current members are absent from an ordinary day's file, so a rule that
        // refused on any absence would refuse every night. The name simply has
        // no bar tonight, which is its own shorter history.
        //
        // What must not happen is silence. The count leaves the stage so a rise
        // from two to two hundred is visible without refusing anything.
        using var store = new TemporaryStore();

        var short1 = new ShortBulkFeed(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), "MSFT", "NFLX");

        var (code, output, error) = await NightAsync(store, runId: "night-short", bulk: short1);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("2 member(s) the file carried nothing for", output, StringComparison.Ordinal);

        // The two it did carry are stored, and the two it did not are absent
        // rather than invented.
        var stored = Tickers(store, "2026-09-08");

        Assert.Equal(["AAPL", "KEYS"], stored);
    }

    static IReadOnlyList<string> Tickers(TemporaryStore store, string session)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT ticker FROM bar WHERE session_date = '{session}' ORDER BY ticker;";

        var rows = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(reader.GetString(0));
        }

        return rows;
    }

    [Fact]
    public async Task AFeedThatDoesNotAnswerKeepsLastNightsBarsAndSaysSoOnTheRunLog()
    {
        // The behaviour half of the decomposed unavailable row. Its banner and
        // tonight's list are phase 5 surfaces and are owed at 5.4; what is owed
        // here is that the store is left as it was and the night says why.
        using var store = new TemporaryStore();

        var (clean, _, _) = await NightAsync(store, runId: "night-one");

        Assert.Equal(0, clean);

        var before = Count(store);

        var (code, _, error) = await NightAsync(store, runId: "night-down", bulk: new FailingBulkFeed());

        Assert.Equal(1, code);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);
        Assert.Contains("did not answer", error, StringComparison.Ordinal);
        Assert.Equal(before, Count(store));
    }

    [Fact]
    public async Task ANightThatPassesItsDeadlineStopsAndSaysSo()
    {
        // The bound the night has never had. Every feed interface has accepted a
        // cancellation token since 1.1 and nothing supplied one, so a night that
        // hung on a socket hung until somebody looked.
        //
        // Named as a deadline rather than as a step that failed, because a night
        // that ran out of time and a night whose provider refused are different
        // mornings and the operator reads one line to tell them apart.
        using var store = new TemporaryStore();

        // A clean night first, so the deadline below has only the fetch left to
        // spend itself on. Without it the migrate, membership and backfill steps
        // are doing real work and a slow runner reaches the deadline before the
        // fetch.
        //
        // The deadline is measured from that night rather than stated as a
        // figure, and that is the repair this test has now had twice. 2.7 set it
        // at a quarter of a second and a cold runner beat it; the correction
        // raised it to three seconds and called it generous, and a runner that
        // took seven and a half minutes over a suite the other ran in four beat
        // that too. Both were the same shape: a bound written as an absolute
        // number is a bound that assumes a machine speed, and this corpus's own
        // rule is that a test whose answer depends on how fast the machine is
        // has no answer.
        //
        // Measured, it scales. The warm night is every step doing its real work
        // once, so twice that plus a margin is comfortably more than the steps
        // before the fetch will take on the same machine, and it is far less
        // than the five minutes the feed below hangs for. A runner ten times
        // slower produces a warm night ten times longer and a deadline ten
        // times larger, and the arrangement holds.
        var started = Stopwatch.StartNew();
        var (warm, _, _) = await NightAsync(store, runId: "night-one");

        started.Stop();

        Assert.Equal(0, warm);

        var deadline = (started.Elapsed * 2) + TimeSpan.FromSeconds(2);

        var slow = new SlowBulkFeed();

        var (code, _, error) = await NightAsync(
            store,
            runId: "night-slow",
            bulk: slow,
            deadline: deadline);

        // The arrangement, asserted rather than assumed. If the deadline fired
        // before the fetch the feed was never entered, and the assertions below
        // would be about a step this test did not mean to name.
        Assert.True(
            slow.Requests == 1,
            $"the night reached the fetch {slow.Requests} time(s) with a deadline of {deadline}, " +
            $"measured from a warm night of {started.Elapsed}. The deadline fired before the step " +
            "this test is about.");

        Assert.Equal(1, code);
        Assert.Contains("passed the night's deadline", error, StringComparison.Ordinal);
        Assert.Contains("step 'fetch'", error, StringComparison.Ordinal);

        // And the promise section 18 makes about a feed that did not answer.
        Assert.Contains("Last night's bars are kept", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightInsideItsDeadlineIsUnaffectedByHavingOne()
    {
        // The counter-test. A deadline that stopped a night that was doing fine
        // would be a worse fault than no deadline at all.
        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store, deadline: TimeSpan.FromMinutes(5));

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("nightly: green", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightRunsEndToEndOverTheFixture()
    {
        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("nightly: green over a capture, 0 model calls", output, StringComparison.Ordinal);

        // And the request count says what it is. A recorded feed counts the
        // calls a live one would have made, which is how a replay measures
        // the cost shape, so the line has to say which kind of night this
        // was rather than print "network request(s)" over a capture.
        Assert.Contains("none of them to a network", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheStepsRunInTheOrderSectionFourteenStatesThem()
    {
        // The order is read off the run's own output rather than off the source,
        // because the claim is that the night runs them in order and a list
        // written in the right order can still be iterated in another.
        using var store = new TemporaryStore();

        var (_, output, _) = await NightAsync(store);

        var membership = output.IndexOf("  membership:", StringComparison.Ordinal);
        var backfill = output.IndexOf("  backfill:", StringComparison.Ordinal);
        var fetch = output.IndexOf("  fetch:", StringComparison.Ordinal);
        var actions = output.IndexOf("  actions:", StringComparison.Ordinal);

        Assert.True(membership >= 0 && backfill >= 0 && fetch >= 0, $"A step did not run: {output}");
        Assert.True(membership < backfill, "The backfill ran before membership, so it asked an empty index which names are members.");
        Assert.True(backfill < fetch, "The fetch ran before the backfill, so a new name would hold one session rather than a year.");
        Assert.True(fetch < actions, "The action check ran before the fetch, so it would refetch a year the night was about to add a session to.");

        // And the order the document states is the order asserted, read from
        // section 14 rather than repeated here.
        var steps = NightlyRunSteps.In(File.ReadAllText(Repository.Architecture));

        Assert.True(steps.Count >= 8, $"Read {steps.Count} steps from section 14, expected at least 8.");
        Assert.StartsWith("Load index membership", steps[0], StringComparison.Ordinal);
        Assert.StartsWith("Backfill one year", steps[1], StringComparison.Ordinal);
        Assert.StartsWith("Fetch the day", steps[2], StringComparison.Ordinal);
        Assert.StartsWith("Check splits and dividends", steps[3], StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachStepDoesWhatSectionFourteenSaysItDoes()
    {
        // The order alone would pass over three steps that ran and did nothing.
        using var store = new TemporaryStore();

        var (_, first, _) = await NightAsync(store, runId: "night-one");

        // Derived from the captured files and the night's own window rather
        // than written down. The window is the session the night runs on less a
        // year, so a figure written here would be a figure about the day this
        // test was written and would move with the fixture.
        var populations = Fixtures.Populations(FixtureFolder());
        var current = populations.Constituents - populations.Departed.Count;
        var opens = ((IClock)FixedClock.At(Night, SessionZones.UnitedStates)).SessionDateAt(Night).AddYears(-1);

        var backfilled = Directory
            .GetFiles(FixtureFolder(), "bars-*.json")
            .Sum(file => SessionsFrom(file, opens));

        Assert.Contains($"membership: {populations.Constituents} rows written", first, StringComparison.Ordinal);
        Assert.Contains($"backfill: {backfilled} rows written over {current} request(s)", first, StringComparison.Ordinal);
        Assert.Contains($"fetch: {current} rows written for {current} member(s)", first, StringComparison.Ordinal);
        Assert.Contains("1 request(s)", first, StringComparison.Ordinal);

        // Stated, because the two derivations above would agree at zero.
        Assert.Equal(FixtureExpectation.Constituents, populations.Constituents);
        Assert.Equal(FixtureExpectation.CurrentMembers.Length, current);
        Assert.True(backfilled > 700, $"The backfill wrote {backfilled} rows, expected more than 700.");

        // A second night over the same store backfills nothing, which is what
        // the step says of itself: afterwards it is only a new joiner.
        var (code, second, _) = await NightAsync(store, runId: "night-two");

        Assert.Equal(0, code);
        Assert.Contains("backfill: 0 rows written over 0 request(s)", second, StringComparison.Ordinal);
        Assert.Contains("fetch: 0 rows written", second, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFailingStepIsNamedAndTheNightExitsNonZero()
    {
        // The done condition, and the reason it is a done condition: a night
        // that fails silently in the middle is one the operator finds by
        // noticing the page is stale in the morning.
        using var store = new TemporaryStore();

        var (code, _, error) = await NightAsync(store, Path.Combine(Repository.Root, "fixtures", "no-such-fixture"));

        Assert.Equal(1, code);
        Assert.Contains("nightly: no fixture at", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFeedThatFailsLeavesTheStoredBarsAsTheyWere()
    {
        // The half of the failure row that lands here. The banner and tonight's
        // list are phase 5 surfaces, so the row's claim is owed at 5.4; what is
        // owed now is that a failed fetch keeps last night's bars rather than
        // leaving a half-written night.
        using var store = new TemporaryStore();

        await NightAsync(store);

        var before = Count(store);

        var failing = new FailingBulkFeed();
        var fetcher = new BarFetcher(failing, FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile);

        await Assert.ThrowsAsync<HttpRequestFailure>(() => fetcher.RunAsync("GSPC", "run-broken"));

        Assert.Equal(before, Count(store));
        Assert.True(before > 700, $"The store held {before} bars, so this compared two small numbers.");
    }

    // Sessions in one captured file at or after a date, read through the
    // shipped parser rather than by reading the JSON here, so the count is the
    // one the pipeline sees.
    static int SessionsFrom(string file, DateOnly opens) =>
        EquityBrief.Core.Providers.RecordedHistoricalBarFeed
            .Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file))
            .Count(bar => bar.SessionDate >= opens);

    static int Count(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM bar;";

        return Convert.ToInt32(command.ExecuteScalar());
    }
}

// A feed that fails the way the provider does. Named for what it stands in for,
// so the test reads as the failure row rather than as an exception.
public sealed class HttpRequestFailure(string message) : Exception(message);

// A feed that answers with the file minus one name, which is what a truncated
// bulk payload looks like: the name is absent altogether rather than present and
// not traded.
sealed class ShortBulkFeed(EquityBrief.Core.Providers.IBulkPriceFeed inner, params string[] drop)
    : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests => inner.Requests;

    public IReadOnlyList<string> NotSessions => inner.NotSessions;

    public async Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default) =>
        [.. (await inner.RowsAsync(exchange, session, cancellation))
            .Where(row => !drop.Contains(row.Ticker, StringComparer.Ordinal))];
}

// A feed that never answers, which is what a hung socket looks like from here.
sealed class SlowBulkFeed : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests { get; private set; }

    public IReadOnlyList<string> NotSessions => [];

    public async Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default)
    {
        Requests++;

        await Task.Delay(TimeSpan.FromMinutes(5), cancellation);

        return [];
    }
}

sealed class FailingBulkFeed : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests { get; private set; }

    // A feed that did not answer skipped nothing. The two are different states
    // and this is the one where the file never arrived.
    public IReadOnlyList<string> NotSessions => [];

    public Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default)
    {
        Requests++;

        throw new HttpRequestFailure("the bulk price feed did not answer");
    }
}
