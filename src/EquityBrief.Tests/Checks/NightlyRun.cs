using EquityBrief.Core.Configuration;
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
        ]);

    const string Fixture = "membership-2026-09-05";

    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static async Task<(int Code, string Output, string Error)> NightAsync(
        TemporaryStore store,
        string? fixture = null,
        string? runId = null)
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            fixture ?? FixtureFolder(),
            "GSPC",
            FixedClock.At(Night, SessionZones.UnitedStates),
            output,
            error,
            runId);

        return (code, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task ANightRunsEndToEndOverTheFixture()
    {
        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store);

        Assert.Equal(0, code);
        Assert.Equal(string.Empty, error);
        Assert.Contains("nightly: green, 0 model calls", output, StringComparison.Ordinal);
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
        Assert.Equal(5, populations.Constituents);
        Assert.Equal(3, current);
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

sealed class FailingBulkFeed : EquityBrief.Core.Providers.IBulkPriceFeed
{
    public int Requests { get; private set; }

    public Task<IReadOnlyList<EquityBrief.Core.Providers.BulkBar>> RowsAsync(
        string exchange,
        CancellationToken cancellation = default)
    {
        Requests++;

        throw new HttpRequestFailure("the bulk price feed did not answer");
    }
}
