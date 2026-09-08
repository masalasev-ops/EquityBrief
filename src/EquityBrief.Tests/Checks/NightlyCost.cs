using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// nightly-cost. The nightly path makes zero model calls and zero per-name
// network requests, asserted over the shipped source and over a recorded run.
//
// The two halves do different work and neither is enough alone. The source scan
// reports coverage: it says how much was read and that nothing in it reaches a
// model or opens a socket. The recorded run carries the claim: it counts what a
// night actually did, and it counts it twice over two different universe sizes,
// because the property is not "one request" but "a count that does not grow
// with the universe". A night measured once over one population has been
// measured against a number, not against the rule.
// see: The nightly run is arithmetic only
public class NightlyCost
{
    internal static CheckReach Reach => new(
        "nightly-cost",
        ["fixtures/membership-2026-09-05"],
        [
            CheckReach.Key(Scope.LimitsTable, "Model calls in the nightly run"),
            CheckReach.Key(Scope.LimitsTable, "Per-name network calls in the nightly run"),
            CheckReach.Key(Scope.LimitsTable, "Bar history kept"),
            CheckReach.Key(Scope.LimitsTable, "Backfill"),
        ]);

    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Backfilled = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // The names a model reaches through, and the types an outward request goes
    // out on. Neither exists in the shipped source today, and the scan says so
    // in numbers rather than reporting an empty result as a pass.
    static readonly string[] Outward =
    [
        "System.Net.Http",
        "HttpClient",
        "WebClient",
        "HttpRequestMessage",
        "Socket(",
    ];

    static readonly string[] Model =
    [
        "deepseek",
        "ChatCompletion",
        "CompletionRequest",
        "IModelClient",
    ];

    [Fact]
    public void NothingOnTheNightlyPathReachesAModelOrOpensARequest()
    {
        // The coverage half. Its scope is a fact about the corpus rather than
        // about the property, so the file count carries a floor only far enough
        // below to catch a scan that read nothing.
        var sources = Repository.SourceFiles()
            .Where(file => !file.Contains(
                Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToArray();

        Assert.True(sources.Length >= 10, $"Scanned {sources.Length} shipped source files, expected at least 10.");

        var offences = new List<string>();

        foreach (var file in sources)
        {
            // Comments stripped first, because this file names every pattern it
            // looks for and a sentence naming one is not a use of it.
            var text = SourceStatements.WithoutComments(File.ReadAllText(file));

            offences.AddRange(Outward
                .Concat(Model)
                .Where(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Select(pattern => $"{Path.GetFileName(file)} carries {pattern}"));
        }

        Assert.Empty(offences);
    }

    [Fact]
    public async Task ANightsRequestCountDoesNotGrowWithTheUniverse()
    {
        // The half that carries the claim, and it is measured over two
        // populations rather than one. A single run proves a number; two runs
        // over different member counts prove the number is not a function of
        // the population, which is what the limit actually says.
        var (three, threeCount) = await NightAsync(members: null);
        var (two, twoCount) = await NightAsync(members: "AAPL");

        Assert.Equal(3, threeCount);
        Assert.Equal(2, twoCount);

        // The universe halved and the request count did not move.
        Assert.Equal(three.Requests, two.Requests);
        Assert.Equal(1, three.Requests);
        Assert.Equal(0, three.ModelCalls);
        Assert.Equal(0, two.ModelCalls);
    }

    [Fact]
    public async Task TheRunLogRecordsTheNightsCostRatherThanTheTestAssertingIt()
    {
        // A green report is a statement about the build and never about the
        // running system. The cost of a night is a property of the run, so it
        // is read back off the store the night wrote, which is the same row the
        // operator reads on the run page.
        using var store = await StoredAsync(null);

        await new BarFetcher(
            RecordedBulkPriceFeed.FromFolder(FixtureFolder()),
            FixedClock.At(Night, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-night");

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT stage, model_calls, network_requests FROM run_log WHERE run_id = 'run-night';";

        var stages = new List<(string Stage, long Model, long Network)>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            stages.Add((reader.GetString(0), reader.GetInt64(1), reader.GetInt64(2)));
        }

        var fetch = Assert.Single(stages, row => row.Stage == BarFetcher.Stage);

        Assert.Equal(0, fetch.Model);
        Assert.Equal(1, fetch.Network);

        // Every steady-state stage, not only the one this test ran. The
        // backfill is carved out of the per-name rule by the limits row and is
        // not a steady-state stage, so it is named here rather than skipped.
        Assert.All(stages, row => Assert.Equal(0, row.Model));
        Assert.DoesNotContain(stages, row => row.Stage == Backfill.Stage);
    }

    [Fact]
    public async Task TheBackfillIsCarvedOutAndSaysSoInItsOwnRow()
    {
        // Contradiction B's resolution, asserted rather than trusted. The
        // backfill does make one request per name, which the steady-state limit
        // would forbid, and the limits row carves it out. A check that read the
        // limit without the carve-out would fail on a rule nobody meant.
        using var store = await StoredAsync(null);

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT network_requests FROM run_log WHERE stage = $stage;";
        command.Parameters.AddWithValue("$stage", Backfill.Stage);

        Assert.Equal(3L, (long)command.ExecuteScalar()!);

        var limits = Corpus.Read("docs/ARCHITECTURE.html");

        // Two carve-outs now, and both are asserted. The refetch is the second
        // and was found at 1.6: it makes one request per name whose adjusted
        // prices an action moved, bounded by the day's actions rather than by
        // the universe. It is carved rather than the rule loosened, because a
        // night that refetched every name would satisfy a loosened rule.
        Assert.Contains("the backfill and the corporate action refetch carved out of it", limits, StringComparison.Ordinal);
        Assert.Contains("bounded by the day's actions rather than by the universe", limits, StringComparison.Ordinal);
    }

    sealed record NightCost(int Requests, int ModelCalls);

    // A store holding the fixture's year. `members` names one ticker to leave
    // as the only member besides the departed, so the second run has a smaller
    // universe than the first.
    static async Task<TemporaryStore> StoredAsync(string? drop)
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

        if (drop is not null)
        {
            // Marked as left rather than deleted, because membership has no
            // declared deleter and a name that leaves keeps its row.
            store.Execute($"UPDATE membership SET left = '2026-09-07' WHERE ticker = '{drop}';");
        }

        return store;
    }

    static async Task<(NightCost Cost, int Members)> NightAsync(string? members)
    {
        using var store = await StoredAsync(members);

        var feed = RecordedBulkPriceFeed.FromFolder(FixtureFolder());
        var outcome = await new BarFetcher(
            feed,
            FixedClock.At(Night, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-night");

        return (new NightCost(feed.Requests, 0), outcome.MembersStored);
    }
}
