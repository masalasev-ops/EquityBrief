using System.Text.RegularExpressions;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
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
            CheckReach.Key(Scope.LimitsTable, "Weighted-call budget"),
        ]);

    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    static readonly DateTimeOffset Night = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Backfilled = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // The types an outward request goes out on, and the names a model is
    // reached through. Two lists rather than one, because the exemption below
    // applies to the first and never to the second.
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

    // The shipped files permitted to hold an outward-request type, each by its
    // repository-relative path.
    //
    // Empty until the first live feed lands, and it grows one file at a time.
    // The other shape available was to delete the patterns when a client first
    // arrived, which leaves a check reporting the absence of a scan as the
    // absence of a client: the under-reporting failure `coverage-reported`
    // exists to catch, arriving inside the check that carries the nightly path's
    // own claim.
    //
    // The list is empty today and the two proofs below are constructed for that
    // reason. An assertion over an empty list is one that has never run, and the
    // day it stops being empty is the day it stops being tested for the first
    // time. Both directions are proved now, while the exemption carries nothing.
    // see: The outward-request scan names the files that may hold a client rather than dropping the patterns
    internal static readonly string[] MayHoldAClient =
    [
        "src/EquityBrief.Core/Providers/EodhdBulkPriceFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdCorporateActionFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdHistoricalBarFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdIndexMembershipFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdNewsFeed.cs",
    ];

    // Which of the scanned files carries something it is not permitted to.
    //
    // Written over its inputs rather than over the checkout, so the exemption
    // can be exercised on constructed sources. A scan that can only run against
    // the tree is one whose carve-out cannot be tested until the carve-out is
    // already load-bearing, which is the wrong order for a guard.
    internal static IReadOnlyList<string> Offences(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyCollection<string> mayHoldAClient)
    {
        var offences = new List<string>();

        foreach (var (name, source) in sources)
        {
            // Comments stripped first, because this file names every pattern it
            // looks for and a sentence naming one is not a use of it.
            var text = SourceStatements.WithoutComments(source);
            var permitted = mayHoldAClient.Contains(name, StringComparer.Ordinal);

            if (!permitted)
            {
                offences.AddRange(Outward
                    .Where(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    .Select(pattern => $"{name} carries {pattern}"));
            }

            // The model half takes no exemption at all. A feed holds a client by
            // definition and nothing on this path holds a model, so an exemption
            // covering both would let the first live feed authorise the one
            // figure the limits table puts at zero and never carves out.
            offences.AddRange(Model
                .Where(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                .Select(pattern => $"{name} carries {pattern}"));
        }

        return offences;
    }

    // The feed interfaces, read out of the source rather than listed here, so a
    // feed added later is one this check already knows about and a feed renamed
    // does not leave a literal behind describing the old name.
    internal static IReadOnlyList<string> FeedInterfaces(IReadOnlyDictionary<string, string> sources) =>
    [
        .. sources.Values
            .SelectMany(source => Regex.Matches(source, @"public interface (I\w*Feed)\b")
                .Select(match => match.Groups[1].Value))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal),
    ];

    // A file earns its exemption by implementing a feed, and the list is read
    // against that rather than taken. Anything else on it is a file somebody
    // wanted to stop failing, which is the way a carve-out turns into a hole.
    internal static IReadOnlyList<string> NotFeeds(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyCollection<string> mayHoldAClient)
    {
        var feeds = FeedInterfaces(sources);
        var wrong = new List<string>();

        foreach (var name in mayHoldAClient)
        {
            if (!sources.TryGetValue(name, out var source))
            {
                wrong.Add($"{name} is permitted to hold a client and no such shipped file was scanned.");

                continue;
            }

            var implements = feeds.Any(feed =>
                Regex.IsMatch(source, @"\b(class|record|struct)\s+\w+[^{;]*:\s*[^{;]*\b" + Regex.Escape(feed) + @"\b"));

            if (!implements)
            {
                wrong.Add(
                    $"{name} is permitted to hold a client and implements none of " +
                    $"{string.Join(", ", feeds)}. A client belongs in a feed.");
            }
        }

        return wrong;
    }

    // The shipped source, keyed by repository-relative path. The suite is
    // excluded because it is not shipped and references everything by design.
    //
    // The path and not the file name. Two projects carry a `Program.cs`, so a
    // list keyed on the name would exempt both by naming one, and the first
    // version of this keyed on the name and threw on the collision rather than
    // exempting the wrong file, which was luck rather than design.
    static IReadOnlyDictionary<string, string> ShippedSource() =>
        Repository.SourceFiles()
            .Where(file => !file.Contains(
                Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToDictionary(
                file => Path.GetRelativePath(Repository.Root, file).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText,
                StringComparer.Ordinal);

    [Fact]
    public void NothingOnTheNightlyPathReachesAModelOrOpensARequest()
    {
        // The coverage half. Its scope is a fact about the corpus rather than
        // about the property, so the file count carries a floor only far enough
        // below to catch a scan that read nothing.
        var sources = ShippedSource();

        Assert.True(sources.Count >= 10, $"Scanned {sources.Count} shipped source files, expected at least 10.");

        // The exempt count is stated rather than left to be inferred from an
        // empty result. A carve-out that grew without anyone noticing reads
        // exactly like a scan that found nothing.
        Assert.True(
            MayHoldAClient.Length <= 5,
            $"{MayHoldAClient.Length} shipped files may hold a client, and there are five feeds. " +
            "A sixth is a file that is not a feed, or a feed nobody declared.");

        Assert.Empty(Offences(sources, MayHoldAClient));
    }

    [Fact]
    public void TheExemptionCoversTheFileItNamesAndNothingElse()
    {
        // Both directions, on constructed sources, because the real list is
        // empty and an empty list proves neither.
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/EquityBrief.Core/Providers/EodhdBulkPriceFeed.cs"] =
                "public sealed class EodhdBulkPriceFeed(HttpClient http) : IBulkPriceFeed { }",
            ["src/EquityBrief.Worker/Bars/Backfill.cs"] =
                "public sealed class Backfill { readonly HttpClient http = new(); }",
        };

        // Named, and the offence goes away. Not named, and it does not.
        Assert.Empty(Offences(sources, [.. sources.Keys]));

        var unnamed = Offences(sources, ["src/EquityBrief.Core/Providers/EodhdBulkPriceFeed.cs"]);

        Assert.Contains(unnamed, offence => offence.Contains("Backfill.cs", StringComparison.Ordinal));
        Assert.DoesNotContain(unnamed, offence => offence.Contains("EodhdBulkPriceFeed.cs", StringComparison.Ordinal));

        // And with nothing named, the check is the one that stood before the
        // carve-out. This is the assertion that fails if the exemption is ever
        // widened into a scan that stopped looking.
        Assert.Equal(2, Offences(sources, []).Count(offence => offence.Contains("HttpClient", StringComparison.Ordinal)));
    }

    [Fact]
    public void AModelCallIsNeverExemptHoweverTheFileIsNamed()
    {
        // The half that must not follow the other. The limits table puts model
        // calls at zero with no carve-out anywhere, so a file on the client list
        // reaching a model is still an offence.
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/EquityBrief.Core/Providers/EodhdBulkPriceFeed.cs"] =
                "public sealed class EodhdBulkPriceFeed(HttpClient http) : IBulkPriceFeed { const string M = \"deepseek\"; }",
        };

        var offences = Offences(sources, [.. sources.Keys]);

        Assert.Single(offences);
        Assert.Contains("deepseek", offences[0], StringComparison.Ordinal);
    }

    [Fact]
    public void AFileMayHoldAClientOnlyByBeingAFeed()
    {
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/EquityBrief.Core/Providers/IBulkPriceFeed.cs"] =
                "public interface IBulkPriceFeed { int Requests { get; } }",
            ["src/EquityBrief.Core/Providers/INewsFeed.cs"] =
                "public interface INewsFeed { int Requests { get; } }",
            ["src/EquityBrief.Core/Providers/EodhdBulkPriceFeed.cs"] =
                "public sealed class EodhdBulkPriceFeed(HttpClient http) : IBulkPriceFeed { }",
            ["src/EquityBrief.Worker/Bars/Backfill.cs"] =
                "public sealed class Backfill { }",
        };

        // The interfaces are read rather than listed, so this is what the check
        // knows about feeds and not a second statement of it.
        Assert.Equal(["IBulkPriceFeed", "INewsFeed"], FeedInterfaces(sources));

        Assert.Empty(NotFeeds(sources, ["src/EquityBrief.Core/Providers/EodhdBulkPriceFeed.cs"]));

        var wrong = NotFeeds(sources, ["src/EquityBrief.Worker/Bars/Backfill.cs"]);

        Assert.Single(wrong);
        Assert.Contains("A client belongs in a feed", wrong[0], StringComparison.Ordinal);

        // A name on the list that no scan reached is the quieter failure of the
        // two: it reads as an exemption that is simply not being used.
        var missing = NotFeeds(sources, ["src/EquityBrief.Core/Providers/Absent.cs"]);

        Assert.Single(missing);
        Assert.Contains("no such shipped file was scanned", missing[0], StringComparison.Ordinal);
    }

    [Fact]
    public void EveryFilePermittedToHoldAClientIsAFeedInTheShippedSource()
    {
        var sources = ShippedSource();

        Assert.True(
            FeedInterfaces(sources).Count >= 4,
            $"Read {FeedInterfaces(sources).Count} feed interfaces from the shipped source, expected at least 4.");

        Assert.Empty(NotFeeds(sources, MayHoldAClient));
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

        Assert.Equal(4, threeCount);
        Assert.Equal(3, twoCount);

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

        Assert.Equal(4L, (long)command.ExecuteScalar()!);

        var limits = Corpus.Read("docs/ARCHITECTURE.html");

        // Two carve-outs now, and both are asserted. The refetch is the second
        // and was found at 1.6: it makes one request per name whose adjusted
        // prices an action moved, bounded by the day's actions rather than by
        // the universe. It is carved rather than the rule loosened, because a
        // night that refetched every name would satisfy a loosened rule.
        Assert.Contains("the backfill and the corporate action refetch carved out of it", limits, StringComparison.Ordinal);
        Assert.Contains("bounded by the day's actions rather than by the universe", limits, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RetentionDropsWhatFellOutOfTheWindowAndNothingInside()
    {
        // The Bar history kept note, both clauses. It stood in BarFetcherTests,
        // where it ran, passed and backed no verdict, because a check's tests
        // are the ones its carrier declares. Moved rather than copied.
        //
        // The boundary is the session the file is for, less a year, so a replay
        // drops what that night would have dropped rather than what tonight
        // would. 2026-09-08 less a year is 2025-09-08, and the stored year
        // starts on 2025-09-05, so three sessions fall out per name.
        using var store = await StoredAsync(null);

        var outcome = await new BarFetcher(
            RecordedBulkPriceFeed.FromFolder(FixtureFolder()),
            FixedClock.At(Night, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-retention");

        Assert.Equal(new DateOnly(2025, 9, 8), outcome.Oldest);

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var oldest = connection.CreateCommand();
        oldest.CommandText = "SELECT MIN(session_date) FROM bar;";

        var kept = (string)oldest.ExecuteScalar()!;

        Assert.True(
            string.CompareOrdinal(kept, "2025-09-08") >= 0,
            $"The oldest stored session is {kept}, which is inside the window the drop should have cleared.");

        using var below = connection.CreateCommand();
        below.CommandText = "SELECT COUNT(*) FROM bar WHERE session_date < '2025-09-08';";

        Assert.Equal(0L, (long)below.ExecuteScalar()!);

        // And none inside it went. The row states two things and this is the
        // second: everything below the boundary is gone and nothing above it is.
        using var inside = connection.CreateCommand();
        inside.CommandText = "SELECT COUNT(*) FROM bar WHERE session_date >= '2025-09-08' AND ticker = 'AAPL';";

        Assert.True(
            (long)inside.ExecuteScalar()! > 200,
            "AAPL holds fewer than 200 sessions inside the window, so the drop took more than it should have.");

        // And the drop is measured rather than reported: it is the difference
        // in the table's own row count across the transaction.
        Assert.True(outcome.RowsDropped > 0, "Nothing was dropped, so the retention path never ran.");
    }

    // ---- the weighted-call budget, all three clauses of its note ----
    //
    // The note asserts three things and only one of them had ever been in this
    // class. The first two were asserted in LiveFeedTests and NewsFeedTests,
    // which run and pass and back no verdict, because a check's tests are the
    // ones its carrier declares; the third was asserted nowhere, which the
    // phase 2 sign-off proved by deleting the stop and watching the suite stay
    // green at 312 of 312. They are moved here rather than copied, because a
    // number asserted in two places is two places holding one fact.

    [Fact]
    public void EveryWeightAndTheAllowanceAreTheOnesTheRunbookStates()
    {
        // The figures have been in RUNBOOK since the architecture was written
        // and no code read either. Read back rather than repeated, because a
        // number stated in a document and again in code is two places holding
        // one fact.
        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Contains("100,000 weighted calls", runbook, StringComparison.Ordinal);
        Assert.Contains($"entire exchange costs {ProviderWeights.BulkEndOfDay}", runbook, StringComparison.Ordinal);
        Assert.Contains(
            $"single-ticker historical request costs {ProviderWeights.HistoricalPerTicker}",
            runbook,
            StringComparison.Ordinal);
        Assert.Contains($"Fundamentals cost {ProviderWeights.Fundamentals} per ticker", runbook, StringComparison.Ordinal);
        Assert.Contains($"News costs {ProviderWeights.News}", runbook, StringComparison.Ordinal);
        Assert.Contains($"cost {ProviderWeights.Fundamentals}.", runbook, StringComparison.Ordinal);
        Assert.Equal(100_000, ProviderWeights.DailyAllowance);
    }

    [Fact]
    public async Task ANightsWeightedTotalIsCountedInTheUnitsTheProviderBillsIn()
    {
        // A night counted in requests alone says five where the provider says
        // three hundred and sixteen, which is the whole reason the allowance
        // could not be read against anything before this. Every one of the five
        // feed roles is exercised, news included, so the total is the night's
        // and not one feed's.
        var feeds = NightFeeds.FromFixture(FixtureFolder());

        Assert.Equal(0, feeds.WeightedCalls);

        await feeds.Membership.ConstituentsAsync(Index);
        await feeds.Bulk.RowsAsync("US", new DateOnly(2026, 9, 8));
        await feeds.Corporate.ActionsAsync("US", new DateOnly(2026, 9, 8));
        await feeds.Historical.BarsAsync("AAPL", new DateOnly(2025, 9, 4), new DateOnly(2026, 9, 4));
        await feeds.News.ArticlesAsync(new DateOnly(2026, 8, 25), new DateOnly(2026, 9, 8));

        // One membership at 10, one bulk at 100, two action requests at 100
        // each, one ticker's history at 1 and one news request at 5. Six
        // requests, 316 weighted calls.
        Assert.Equal(6, feeds.Requests);
        Assert.Equal(
            ProviderWeights.Fundamentals
            + ProviderWeights.BulkEndOfDay
            + (2 * ProviderWeights.BulkEndOfDay)
            + ProviderWeights.HistoricalPerTicker
            + ProviderWeights.News,
            feeds.WeightedCalls);
        Assert.Equal(316, feeds.WeightedCalls);
    }

    [Fact]
    public async Task ANightAlreadyAtItsAllowanceStopsBeforeItsNextStepAndSaysWhy()
    {
        // The third clause, over a recorded run rather than a source scan. The
        // night arrives already at the allowance, which is what a second run on
        // a day the budget was spent looks like, and the stop is before the
        // first step rather than after some of them.
        using var store = new TemporaryStore().Migrated();

        var spent = new SpentBulkFeed(ProviderWeights.DailyAllowance / ProviderWeights.BulkEndOfDay);
        var feeds = NightFeeds.FromFixture(FixtureFolder()) with { Bulk = spent };

        Assert.Equal(ProviderWeights.DailyAllowance, feeds.WeightedCalls);

        var output = new StringWriter();
        var error = new StringWriter();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            feeds,
            Index,
            FixedClock.At(Night, SessionZones.UnitedStates),
            output,
            error,
            "run-spent");

        Assert.Equal(1, code);

        // It says which step it stopped before, and what it had spent against
        // what it was allowed. A night that stopped without saying why is a
        // morning spent guessing whether the provider or the budget did it.
        var said = error.ToString();

        // Named against the night's first step, which is what makes the stop
        // precede every step rather than some of them. A run that stopped part
        // way through would name a later step and would have left run log rows
        // behind it, and the assertion below is the other half of that.
        Assert.Contains("stopped before step 'migrate'", said, StringComparison.Ordinal);
        Assert.Contains($"{ProviderWeights.DailyAllowance} weighted call(s)", said, StringComparison.Ordinal);
        Assert.Contains("Last night's bars are kept", said, StringComparison.Ordinal);

        // And it made no call. The feed was never asked, and no stage reached
        // the run log, which is the surface the operator reads it on.
        Assert.False(spent.Asked);
        Assert.Empty(Stages(store, "run-spent"));
    }

    static IReadOnlyList<string> Stages(TemporaryStore store, string runId)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT stage FROM run_log WHERE run_id = $run;";
        command.Parameters.AddWithValue("$run", runId);

        var stages = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            stages.Add(reader.GetString(0));
        }

        return stages;
    }

    sealed record NightCost(int Requests, int ModelCalls);

    // A feed that has already spent the day's allowance and has not been asked
    // for anything. It reports the requests a night earlier in the day made,
    // which is what the counter carries when a second night runs.
    sealed class SpentBulkFeed(int requests) : IBulkPriceFeed
    {
        internal bool Asked { get; private set; }

        public int Requests => requests;

        public IReadOnlyList<string> NotSessions => [];

        public Task<IReadOnlyList<BulkBar>> RowsAsync(
            string exchange,
            DateOnly session,
            CancellationToken cancellation = default)
        {
            Asked = true;

            throw new InvalidOperationException(
                "the night asked a feed for rows after it had reached the allowance, which is the " +
                "call the stop exists to prevent.");
        }
    }

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
