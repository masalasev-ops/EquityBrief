using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
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

    // The last is the wire path every OpenAI-compatible model runtime serves, and it
    // was added at 6.6 with the first model client. The other four were written
    // before any client existed and are names a client might have had; the one the
    // tree actually holds is named for its feed and carries none of them, so a scan
    // over the four would have passed over the one model client it exists to find.
    // A path a client cannot avoid sending is the shape rather than a guess at a
    // name, which is the repair 6.2 made to the provider reader for the same reason.
    static readonly string[] Model =
    [
        "deepseek",
        "ChatCompletion",
        "CompletionRequest",
        "IModelClient",
        "chat/completions",
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
        "src/EquityBrief.Core/Providers/EodhdEarningsCalendarFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdFundamentalsFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdHistoricalBarFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdIndexMembershipFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdNameNewsFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdNewsFeed.cs",
        "src/EquityBrief.Core/Providers/SecEdgarFilingsArchiveFeed.cs",
        "src/EquityBrief.Core/Providers/OpenAiCompatibleModelFeed.cs",
        "src/EquityBrief.Core/Providers/OpenAiCompatibleResearchFeed.cs",
        "src/EquityBrief.Core/Providers/TavilySearchFeed.cs",
    ];

    // The shipped files permitted to reach a model, each by its path, which is the
    // carve by name the decision states rather than the patterns dropped.
    //
    // The first half of that carve, and it lands at 6.6 because 6.6 is where the
    // first model client lands. What the carve names is the file a model may be
    // reached from, and it says nothing about the night: no nightly step holds a
    // model feed, which component-access asserts over each stage's declared feeds,
    // and the queue that calls this lane from the night is 6.10's, where the second
    // half below says which lane the night may call and that its calls come from step
    // 17 alone. A file here that reaches a model and is not a model feed fails, for
    // the reason a client belongs in a feed.
    //
    // Two from 6.7, which adds the research model's live feed, the second file that
    // sends the wire path. Its recorded double reaches no model and carries no pattern,
    // reading the live feed's parser by the feed's own name. And no shipped file names
    // the paid provider: the provider is named in configuration alone, and the first
    // pattern is what holds that, because a file naming it is a file this scan reports.
    // It was three for one commit, while the feed was named for the provider and the
    // double carried the name by calling it.
    // see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name
    // see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in
    internal static readonly string[] MayHoldAModel =
    [
        "src/EquityBrief.Core/Providers/OpenAiCompatibleModelFeed.cs",
        "src/EquityBrief.Core/Providers/OpenAiCompatibleResearchFeed.cs",
    ];

    // What makes a shipped file a provider implementation, which is what the list
    // above has to hold exactly.
    //
    // Read as a shape rather than as a prefix. It was a prefix until 6.2: the seven
    // files were all named for one provider, so the reverse direction asserted that
    // the list equalled the files matching `/Providers/Eodhd`, and the first
    // provider with another name would have failed a check that had nothing wrong
    // with it. A feed file that is neither an interface nor a recorded double is an
    // implementation whatever the provider is called.
    internal static bool IsAProviderImplementation(string file)
    {
        var name = file[(file.LastIndexOf('/') + 1)..];

        return file.Contains("/Providers/", StringComparison.Ordinal)
            && name.EndsWith("Feed.cs", StringComparison.Ordinal)
            && !name.StartsWith("Recorded", StringComparison.Ordinal)
            && !(name.Length > 1 && name[0] == 'I' && char.IsAsciiLetterUpper(name[1]));
    }

    // Which of the scanned files carries something it is not permitted to.
    //
    // Written over its inputs rather than over the checkout, so the exemption
    // can be exercised on constructed sources. A scan that can only run against
    // the tree is one whose carve-out cannot be tested until the carve-out is
    // already load-bearing, which is the wrong order for a guard.
    internal static IReadOnlyList<string> Offences(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyCollection<string> mayHoldAClient,
        IReadOnlyCollection<string>? mayHoldAModel = null)
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

            // The model half takes no exemption from the client list. A feed holds a
            // client by definition, so an exemption covering both would let the first
            // live feed authorise the one figure the limits table puts at zero. It is
            // carved by its own list instead, naming the model feeds alone.
            if (!(mayHoldAModel ?? []).Contains(name, StringComparer.Ordinal))
            {
                offences.AddRange(Model
                    .Where(pattern => text.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    .Select(pattern => $"{name} carries {pattern}"));
            }
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
            MayHoldAClient.Length <= 12,
            $"{MayHoldAClient.Length} shipped files may hold a client, and there are twelve feed " +
            "implementations. A thirteenth is a file that is not one, or a feed nobody declared.");

        // The model list, stated the same way: two files, the local lane's client and
        // the research model's live feed.
        Assert.Equal(2, MayHoldAModel.Length);

        // And the list holds exactly the provider implementations, in both
        // directions, so a file added to it that is not one fails rather than
        // passing quietly. It was six against five before 4.3 added the calendar,
        // seven against six before 6.1 added the fundamentals endpoint, which is
        // the first that no night calls, and eight before 6.2 added the filings
        // archive, which is the first from another provider, nine at 6.6, the
        // local model, which is the first that reaches a model, ten at 6.7, the
        // research model, which is the first that is paid, and eleven at 6.8, one name's
        // own news, which is the night's news endpoint asked for one name and is the
        // reason it holds a client of its own rather than a ticker on the night's.
        var live = Repository.SourceFiles()
            .Select(file => file[Repository.Root.Length..].Replace(Path.DirectorySeparatorChar, '/').TrimStart('/'))
            .Where(IsAProviderImplementation)
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(live, MayHoldAClient.OrderBy(file => file, StringComparer.Ordinal));

        Assert.Empty(Offences(sources, MayHoldAClient, MayHoldAModel));

        // And the model list holds model feeds and nothing else.
        Assert.Empty(NotModelFeeds(sources, MayHoldAModel));
    }

    // A file earns the model carve by implementing a model feed, read against the
    // interfaces the source declares rather than taken on its name.
    internal static IReadOnlyList<string> NotModelFeeds(
        IReadOnlyDictionary<string, string> sources,
        IReadOnlyCollection<string> mayHoldAModel)
    {
        var feeds = FeedInterfaces(sources).Where(feed => feed.EndsWith("ModelFeed", StringComparison.Ordinal)).ToArray();
        var wrong = new List<string>();

        foreach (var name in mayHoldAModel)
        {
            if (!sources.TryGetValue(name, out var source))
            {
                wrong.Add($"{name} is permitted to reach a model and no such shipped file was scanned.");

                continue;
            }

            if (!feeds.Any(feed => Regex.IsMatch(source, @"\b(class|record|struct)\s+\w+[^{;]*:\s*[^{;]*\b" + Regex.Escape(feed) + @"\b")))
            {
                wrong.Add($"{name} is permitted to reach a model and implements no model feed. A model belongs in one.");
            }
        }

        return wrong;
    }

    [Fact]
    public void TheModelCarveCoversTheModelFeedItNamesAndNothingElse()
    {
        // Both directions over constructed sources. The wire path in a file on the
        // model list passes, the same path anywhere else fails, the client list does
        // not carve a model, and a file on the model list that is not a model feed is
        // reported.
        var sources = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["src/EquityBrief.Core/Providers/ILocalModelFeed.cs"] = "public interface ILocalModelFeed { int Requests { get; } }",
            ["src/EquityBrief.Core/Providers/OpenAiCompatibleModelFeed.cs"] =
                "public sealed class OpenAiCompatibleModelFeed(HttpClient client) : ILocalModelFeed { const string P = \"chat/completions\"; }",
            ["src/EquityBrief.Worker/Bars/BarFetcher.cs"] = "public sealed class BarFetcher { const string P = \"chat/completions\"; }",
        };

        string[] client = ["src/EquityBrief.Core/Providers/OpenAiCompatibleModelFeed.cs"];
        string[] model = ["src/EquityBrief.Core/Providers/OpenAiCompatibleModelFeed.cs"];

        var offences = Offences(sources, client, model);

        Assert.Equal("src/EquityBrief.Worker/Bars/BarFetcher.cs carries chat/completions", Assert.Single(offences));

        Assert.Equal(2, Offences(sources, client).Count);

        Assert.Empty(NotModelFeeds(sources, model));
        Assert.Single(NotModelFeeds(sources, ["src/EquityBrief.Worker/Bars/BarFetcher.cs"]));
    }

    [Fact]
    public void AProviderImplementationIsAFeedFileThatIsNeitherAnInterfaceNorADouble()
    {
        // The permanent proof for the reader the reverse direction uses, in both
        // directions and over names rather than over the tree. It replaced a prefix
        // at 6.2, and a prefix over one provider's name is a reader that would have
        // failed on the first provider called something else with nothing wrong in
        // the check.
        Assert.All(
            new[]
            {
                "src/EquityBrief.Core/Providers/EodhdNewsFeed.cs",
                "src/EquityBrief.Core/Providers/SecEdgarFilingsArchiveFeed.cs",
            },
            file => Assert.True(IsAProviderImplementation(file), file));

        Assert.All(
            new[]
            {
                // An interface, which declares a feed and holds nothing.
                "src/EquityBrief.Core/Providers/IFundamentalsFeed.cs",
                // A recorded double, which answers from a capture.
                "src/EquityBrief.Core/Providers/RecordedFilingsArchiveFeed.cs",
                // A file in the folder that is not a feed at all.
                "src/EquityBrief.Core/Providers/ProviderCredentials.cs",
                // A feed-shaped name outside the folder, so the folder is doing
                // work and the suffix is not carrying the whole test.
                "src/EquityBrief.Worker/Bars/BackfillFeed.cs",
            },
            file => Assert.False(IsAProviderImplementation(file), file));

        // And a name beginning with a capital I that is not an interface is an
        // implementation, which is the case the interface test could swallow:
        // `IndexMembership` starts with the same letter.
        Assert.True(IsAProviderImplementation("src/EquityBrief.Core/Providers/IndexArchiveFeed.cs"));
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

    // ---- the carve's second half, which is about the night ----
    //
    // It lands at 6.10 and ahead of the queue it is about, for the reason 2.1 landed the
    // cost carve-out ahead of the live feed: a guard built in the commit that builds what
    // it guards has no run in which it stood alone. The first half, at 6.6, names the
    // files a model may be reached from and says nothing about the night. This half says
    // the two things the decision says of the night: which lane it may call, and that its
    // calls come from step 17 alone.
    // see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name

    // Step 17's own stage on the night's run. Stated here before the queue exists, since the
    // guard lands first, and read against the queue's own constant from the commit that
    // builds it.
    internal const string QueueStage = "overnight queue";

    // What the night may reach: the feeds the night's own record resolves, and the local
    // model. A paid model, a search, a company's financials and the filings archive are an
    // open's, and a night reaching one is a night whose cost is no longer the arithmetic's.
    internal static readonly Feed[] TheNightMayReach =
    [
        Feed.IndexMembership,
        Feed.BulkPrice,
        Feed.HistoricalPrice,
        Feed.SplitsAndDividends,
        Feed.EarningsCalendar,
        Feed.News,
        Feed.LocalModel,
    ];

    static string NightSource() =>
        File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Nightly.cs"));

    // The lanes the night's composition may not call, read off the components its own file
    // constructs and what each of them declares, so a component added to the night that
    // reaches a paid lane is found by its declaration rather than by a name kept here.
    // Comments are stripped first, because a sentence naming a component is not a use of it.
    internal static IReadOnlyList<string> LanesTheNightMayNotCall(
        string nightSource,
        IReadOnlyList<DeclaredComponent> components)
    {
        var constructed = Regex.Matches(SourceStatements.WithoutComments(nightSource), @"\bnew\s+([A-Z]\w*)\s*\(")
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

        return
        [
            .. components
                .Where(component => constructed.Contains(component.Name))
                .SelectMany(component => component.Access.Feeds
                    .Where(feed => !TheNightMayReach.Contains(feed))
                    .Select(feed => $"the night constructs {component.Name}, which reaches {feed}")),
        ];
    }

    // One run log row as the carve reads it.
    internal sealed record CostRow(string RunId, string Stage, int ModelCalls, string Spend, string Detail);

    // What a store's rows carry that the carve does not allow: any spend, whose only source
    // is a paid call, and a model call anywhere but on step 17's own row and the runs that
    // row names as its passes. The passes are read off the queue's row rather than off how a
    // run id reads, because a matcher keyed on the opening of an id answers for every id that
    // happens to open the same way.
    internal static IReadOnlyList<string> CallsTheCarveDoesNotAllow(IReadOnlyList<CostRow> rows, string nightRunId)
    {
        var step = rows.Where(row => row.RunId == nightRunId && row.Stage == QueueStage).ToArray();
        var passes = step.SelectMany(row => PassesNamedIn(row.Detail)).ToHashSet(StringComparer.Ordinal);
        var offences = new List<string>();

        foreach (var row in rows)
        {
            if (row.Spend != "0")
            {
                offences.Add($"{row.RunId} {row.Stage} spent {row.Spend}");
            }

            if (row.ModelCalls > 0 && !step.Contains(row) && !passes.Contains(row.RunId))
            {
                offences.Add($"{row.RunId} {row.Stage} made {row.ModelCalls} model call(s) outside step 17");
            }
        }

        return offences;
    }

    // The runs step 17's row names as the passes it ran.
    static IReadOnlyList<string> PassesNamedIn(string detail)
    {
        if (detail.Length == 0)
        {
            return [];
        }

        using var parsed = System.Text.Json.JsonDocument.Parse(detail);

        var passes = new List<string>();

        if (parsed.RootElement.TryGetProperty("completed", out var completed))
        {
            passes.AddRange(completed.EnumerateArray().Select(pass => pass.GetProperty("runId").GetString()!));
        }

        // The pass the queue stopped at, which made the call that found the local model not
        // answering.
        if (parsed.RootElement.TryGetProperty("stopped", out var stopped) && stopped.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            passes.Add(stopped.GetProperty("runId").GetString()!);
        }

        return passes;
    }

    [Fact]
    public async Task TheArithmeticCallsNoModelAndTheNightsCallsComeFromStepSeventeenAlone()
    {
        // Over a whole recorded night rather than one stage of one, because the claim is
        // about the night and a stage run alone cannot say which steps a night runs.
        using var store = new TemporaryStore();

        var code = await Nightly.RunAsync(
            new StoreLocation(Path.GetDirectoryName(store.DatabaseFile)!),
            FixtureFolder(),
            Index,
            FixedClock.At(Night, SessionZones.UnitedStates),
            new StringWriter(),
            new StringWriter(),
            "run-carve");

        Assert.Equal(0, code);

        var rows = CostRows(store.DatabaseFile);
        var night = rows.Where(row => row.RunId == "run-carve").ToArray();

        // The population, stated: every stage of the arithmetic writes a row under the
        // night's run, the facts step two.
        Assert.True(night.Length >= 17, $"The night wrote {night.Length} row(s), expected at least 17.");

        // No arithmetic stage called a model, read off the rows the night wrote.
        Assert.Equal(0, night.Where(row => row.Stage != QueueStage).Sum(row => row.ModelCalls));

        // And whatever model calls the night made are on step 17's rows, and nothing spent.
        Assert.Empty(CallsTheCarveDoesNotAllow(rows, "run-carve"));

        // Which lane: the night's composition reaches nothing an open reaches.
        var components = ShippedComponents.All();

        Assert.True(components.Count >= 20, $"Read {components.Count} declared components, expected at least 20.");
        Assert.Empty(LanesTheNightMayNotCall(NightSource(), components));
    }

    [Fact]
    public void TheCarveAllowsAModelCallOnStepSeventeensRowsAndNowhereElse()
    {
        // The permanent proof, over constructed rows, since the night this lands in has no
        // step 17 and its rows would prove only the empty case.
        const string NightRun = "night-x";
        const string Named = """{"completed":[{"ticker":"MSFT","runId":"a-pass"}]}""";

        CostRow[] clean =
        [
            new(NightRun, "fetch", 0, "0", ""),
            new(NightRun, QueueStage, 1, "0", Named),
            new("a-pass", "prose", 1, "0", ""),
            new("a-pass", "claims", 0, "0", ""),
        ];

        Assert.Empty(CallsTheCarveDoesNotAllow(clean, NightRun));

        // A model call on an arithmetic stage.
        Assert.Contains("night-x facts made 1 model call(s) outside step 17", CallsTheCarveDoesNotAllow([.. clean, new(NightRun, "facts", 1, "0", "")], NightRun));

        // A model call under a run the queue's row does not name, however the id reads.
        Assert.Single(CallsTheCarveDoesNotAllow([.. clean, new("night-x-queue-NFLX", "prose", 1, "0", "")], NightRun));

        // Any spend at all, on any row.
        Assert.Single(CallsTheCarveDoesNotAllow([.. clean, new("a-pass", "research call: The two cases", 0, "0.0021", "")], NightRun));

        // And step 17's row under another night licenses nothing on this one: both its own
        // call and its pass's are outside this night's step.
        Assert.Equal(
            2,
            CallsTheCarveDoesNotAllow([new(NightRun, "fetch", 0, "0", ""), new("night-y", QueueStage, 1, "0", Named), new("a-pass", "prose", 1, "0", "")], NightRun).Count);
    }

    [Fact]
    public void ANightConstructingAComponentThatReachesAnOpensLaneIsReportedByWhatItDeclares()
    {
        var components = ShippedComponents.All();

        // The spend cap, which holds the research model.
        Assert.Contains(
            LanesTheNightMayNotCall("steps = [ new(\"x\", () => new SpendCap(feed, caps, clock, db).AskAsync(request)) ];", components),
            offence => offence.Contains("SpendCap", StringComparison.Ordinal) && offence.Contains(nameof(Feed.ResearchModel), StringComparison.Ordinal));

        // The research runner, which fetches a name's filings, and the fundamentals fetcher.
        Assert.Contains(
            LanesTheNightMayNotCall("new ResearchRunner(judge, writer, cap, checker, themes, archive, news, lane, clock, db)", components),
            offence => offence.Contains(nameof(Feed.FilingsArchive), StringComparison.Ordinal));
        Assert.Contains(
            LanesTheNightMayNotCall("new FundamentalsFetcher(feed, clock, db, archive)", components),
            offence => offence.Contains(nameof(Feed.CompanyFinancials), StringComparison.Ordinal));

        // The theme runner, which searches.
        Assert.Contains(
            LanesTheNightMayNotCall("new ThemeResearchRunner(cap, checker, search, list, pricing, clock, db)", components),
            offence => offence.Contains(nameof(Feed.SearchTool), StringComparison.Ordinal));

        // The prose writer reaches the local model and nothing else, which the night may.
        Assert.Empty(LanesTheNightMayNotCall("new ProseWriter(model, settings, lane, clock, db)", components));

        // And a comment naming one is not a construction of it.
        Assert.Empty(LanesTheNightMayNotCall("// new SpendCap(feed, caps, clock, db) is not built here", components));
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
        // prices an action moved, and from the 6.0 ruling a suspect name's
        // retries on the nights after, so it is bounded by the actions of the day
        // and of the retry nights before it rather than by the universe. It is
        // carved rather than the rule loosened, because a night that refetched
        // every name would satisfy a loosened rule.
        Assert.Contains("the backfill and the corporate action refetch carved out of it", limits, StringComparison.Ordinal);
        Assert.Contains($"bounded by the actions of the day and of the {CorporateActionChecker.RetryNights} nights before it, and by one request every {CorporateActionChecker.WeeklyRetryDays} days for each name whose retries are spent, rather than by the universe", limits, StringComparison.Ordinal);
    }

    static System.Text.Json.JsonElement Expected(string stage) =>
        System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(FixtureFolder(), "expectations", stage + ".json"))).RootElement;

    [Fact]
    public async Task ASuspectNameCostsOneRequestOnEachNightOfItsRetriesAndOneAWeekOnceTheyAreSpent()
    {
        // The ruling 6.0 owed and 6.11 found unwritten. From the phase 5 sign-off a name
        // whose refetch failed was asked for again on every night after, so a failure that
        // lasted made one per-name request on the nightly path on each night it lasted,
        // and section 17's bound by the day's actions had stopped being true. Measured here
        // night by night over a name whose refetch fails every time it is asked, against a
        // sequence derived from the decision rather than frozen from a run: one request on
        // the action's night and on each retry night, none once the retries are spent, and
        // one again when another action lands.
        // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
        var expected = Expected("suspect-retries");
        var name = expected.GetProperty("failingName").GetString()!;
        var nights = expected.GetProperty("nights").EnumerateArray().ToArray();

        Assert.Equal(CorporateActionChecker.RetryNights, expected.GetProperty("retryNights").GetInt32());
        Assert.Equal(CorporateActionChecker.WeeklyRetryDays, expected.GetProperty("weeklyRetryDays").GetInt32());

        var first = DateOnly.ParseExact(expected.GetProperty("firstActionSession").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var sessions = CorporateActions.SessionsFrom(first, nights.Length);

        // The sessions the expectation writes out so its weeks can be read are the exchange's,
        // read off the closure table here, so a week the table moves moves the test with it.
        Assert.Equal(
            nights.Select(plan => plan.GetProperty("session").GetString()!),
            sessions.Select(session => session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        using var store = await StoredAsync(null);

        var spentOn = new List<int>();

        for (var night = 0; night < nights.Length; night++)
        {
            var plan = nights[night];
            var runId = $"night-{night}";

            // An action on the name on the nights the plan lands one, and none on the rest.
            var actions = new CorporateActions.ActionOnSessions(name, plan.GetProperty("actionLands").GetBoolean() ? [sessions[night]] : []);
            var history = new RecordedHistoricalBarFeed(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

            var outcome = await new CorporateActionChecker(actions, history, FixedClock.At(CorporateActions.NightOn(sessions[night]), SessionZones.UnitedStates), store.DatabaseFile)
                .RunAsync(Index, runId);

            // The per-name figure the carve-out is about, on the outcome and on the row the
            // run page reads, where the action feed's own request is the rest.
            Assert.True(
                plan.GetProperty("requests").GetInt32() == outcome.RefetchRequests,
                $"Night {night}, " + sessions[night].ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) +
                $", asked for {name} {outcome.RefetchRequests} time(s), expected {plan.GetProperty("requests").GetInt32()}.");

            using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
            connection.Open();

            using var row = connection.CreateCommand();
            row.CommandText = "SELECT network_requests FROM run_log WHERE run_id = $r AND stage = $s;";
            row.Parameters.AddWithValue("$r", runId);
            row.Parameters.AddWithValue("$s", CorporateActionChecker.Stage);

            Assert.Equal((long)(actions.Requests + outcome.RefetchRequests), (long)row.ExecuteScalar()!);

            using var state = connection.CreateCommand();
            state.CommandText = "SELECT state, retries FROM series_state WHERE ticker = $t;";
            state.Parameters.AddWithValue("$t", name);

            using var reader = state.ExecuteReader();

            Assert.True(reader.Read());
            Assert.Equal(CorporateActionChecker.Suspect, reader.GetString(0));
            Assert.Equal(plan.GetProperty("retries").GetInt32(), reader.GetInt32(1));

            Assert.Equal(plan.GetProperty("spent").GetBoolean(), (outcome.Spent ?? []).Any(spent => spent.Ticker == name));

            if (plan.GetProperty("spent").GetBoolean())
            {
                spentOn.Add(night);
            }
        }

        // The bound itself. Over one action's first nights, from its night through its last
        // nightly retry, one request each and no more.
        var firstNights = nights.Take(CorporateActionChecker.RetryNights + 1).Sum(plan => plan.GetProperty("requests").GetInt32());

        Assert.Equal(expected.GetProperty("requestsOverTheFirstNights").GetInt32(), firstNights);
        Assert.Equal(CorporateActionChecker.RetryNights + 1, firstNights);
        Assert.NotEmpty(spentOn);

        // And after them, until the next action lands, each request falls on the first session
        // on or after the one before it and the week, worked out here from the closure table
        // rather than read off the expectation, so the two statements of the rule are held to
        // each other.
        var next = Array.FindIndex(nights, 1, plan => plan.GetProperty("actionLands").GetBoolean());
        var asked = Enumerable.Range(0, next)
            .Where(night => nights[night].GetProperty("requests").GetInt32() > 0)
            .Select(night => sessions[night])
            .ToArray();

        Assert.True(asked.Length >= CorporateActionChecker.RetryNights + 3, $"The nights reach {asked.Length - CorporateActionChecker.RetryNights - 1} weekly retries, fewer than two.");

        for (var at = CorporateActionChecker.RetryNights + 1; at < asked.Length; at++)
        {
            var due = asked[at - 1].AddDays(CorporateActionChecker.WeeklyRetryDays);

            while (!ExchangeClosures.IsSession(due))
            {
                due = due.AddDays(1);
            }

            Assert.Equal(due, asked[at]);
        }

        // And the two places the figure is stated for a reader, held to the constant so
        // neither moves alone.
        var carve = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(table => table.Heading == Scope.LimitsTable)
            .Body.Single(cells => cells.Count > 2 && cells[0] == "Per-name network calls in the nightly run");

        Assert.Contains($"asked for again on each of the {CorporateActionChecker.RetryNights} nights after the one that marked it and then every {CorporateActionChecker.WeeklyRetryDays} days", carve[2], StringComparison.Ordinal);
        Assert.Contains($"one action costs {CorporateActionChecker.RetryNights + 1} requests over the {CorporateActionChecker.RetryNights + 1} nights from the one it lands on and 1 every {CorporateActionChecker.WeeklyRetryDays} days after that", carve[2], StringComparison.Ordinal);

        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Contains($"asks for the name's year again on each of the next {CorporateActionChecker.RetryNights} nights", runbook, StringComparison.Ordinal);
        Assert.Contains($"the refetch has failed on {CorporateActionChecker.RetryNights + 1} nights running", runbook, StringComparison.Ordinal);
        Assert.Contains($"asks for it again {CorporateActionChecker.WeeklyRetryDays} days after the session it was last asked for", runbook, StringComparison.Ordinal);
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
            NightQueue.FromFixture(FixtureFolder()),
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

        // And it made no call. The feed was never asked, and no stage ran: the
        // one run log row is the stop itself, recorded against the step it
        // stopped before, which is the surface the operator reads it on. Until
        // the phase 5 sign-off this asserted the run log was empty, which was
        // the absence of the record rather than the presence of the stop.
        Assert.False(spent.Asked);
        Assert.Equal(["migrate"], Stages(store, "run-spent"));
        Assert.Equal(1, OutcomeCount(store, "run-spent", "stopped"));
    }

    static int OutcomeCount(TemporaryStore store, string runId, string outcome)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM run_log WHERE run_id = $run AND outcome = $outcome;";
        command.Parameters.AddWithValue("$run", runId);
        command.Parameters.AddWithValue("$outcome", outcome);

        return Convert.ToInt32(command.ExecuteScalar());
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

    // The model calls are read off the rows the run wrote rather than written here. Until
    // 6.10 this returned a literal zero beside the request count, so the two assertions
    // that the night called no model compared that zero with itself and could not fail,
    // which the carve's second half found when it went to read the figure.
    static async Task<(NightCost Cost, int Members)> NightAsync(string? members)
    {
        using var store = await StoredAsync(members);

        var feed = RecordedBulkPriceFeed.FromFolder(FixtureFolder());
        var outcome = await new BarFetcher(
            feed,
            FixedClock.At(Night, SessionZones.UnitedStates),
            store.DatabaseFile).RunAsync(Index, "run-night");

        var calls = CostRows(store.DatabaseFile).Where(row => row.RunId == "run-night").Sum(row => row.ModelCalls);

        return (new NightCost(feed.Requests, calls), outcome.MembersStored);
    }

    // Every run log row as the carve reads it.
    internal static IReadOnlyList<CostRow> CostRows(string databaseFile)
    {
        using var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT run_id, stage, model_calls, spend, IFNULL(detail, '') FROM run_log ORDER BY rowid;";

        var rows = new List<CostRow>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(new CostRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.GetString(3), reader.GetString(4)));
        }

        return rows;
    }
}
