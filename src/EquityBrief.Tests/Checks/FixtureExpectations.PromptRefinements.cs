using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;
using Microsoft.Extensions.Configuration;
using ResearchFeeds = EquityBrief.Tests.Providers.ResearchModelFeedTests;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the research prompt's refinements: over every request the fixture's pass asks, the ask said
// again after everything handed in, the reader named, the alternative to a figure no fact holds, each document marked
// by its kind, and a weak and a strong point given for the two cases and the risks with no digit in either; and the
// review, off as shipped, asking the section's model to check its own draft when a profile is named for it, written
// beside the report and drawn beside the trial.
// see: The research prompt repeats each section's ask after the documents, names its reader and marks each document by kind
// see: A review asks a section's model to check its own draft against the section's rules, behind a setting that ships off
public partial class FixtureExpectations
{
    // Every request the fixture's pass asks, the paid lane's and the local model's. A request no recording answers is
    // answered with nothing rather than refused, so a prompt a changed rule no longer matches is still asked and read
    // here, the pass running on as it does over an empty answer.
    static async Task<IReadOnlyList<ModelRequest>> FixturePromptsAsync()
    {
        var local = new LocalEitherWay(new RecordedLocalModelFeed(Folder()));
        var paid = new PaidEitherWay(new RecordedResearchModelFeed(Folder(), ResearchFeeds.Pinned()));

        using var store = await FixtureReplay.ReplayedForResearchAsync(local);

        await FixtureReplay.Researcher(store, FixedClock.At(FixtureReplay.Night, SessionZones.UnitedStates), paid: paid, localModel: local)
            .RunAsync(FixtureReplay.ResearchName, "replay-research");

        return [.. paid.Asked, .. local.Asked];
    }

    sealed class PaidEitherWay(IResearchModelFeed inner) : IResearchModelFeed
    {
        public List<ModelRequest> Asked { get; } = [];

        public int Requests => inner.Requests;

        public int Probes => inner.Probes;

        public string Identity => inner.Identity;

        public Task<string?> UnreachableAsync(CancellationToken cancellation = default) => inner.UnreachableAsync(cancellation);

        public async Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);

            try
            {
                return await inner.CompleteAsync(request, cancellation);
            }
            catch (InvalidOperationException)
            {
                return new ResearchAnswer(request.Model, string.Empty, 0, 0, 0, 0, 0, "stop", FixtureReplay.Night);
            }
        }

        public decimal Price(ResearchAnswer answer) => inner.Price(answer);

        public decimal Ceiling(ModelRequest request) => inner.Ceiling(request);
    }

    sealed class LocalEitherWay(ILocalModelFeed inner) : ILocalModelFeed
    {
        public List<ModelRequest> Asked { get; } = [];

        public int Requests => inner.Requests;

        public async Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);

            try
            {
                return await inner.CompleteAsync(request, cancellation);
            }
            catch (InvalidOperationException)
            {
                return new ModelAnswer(request.Model, string.Empty, 0, 0, "stop");
            }
        }
    }

    [Fact]
    public async Task EveryRequestSaysItsAskAgainAfterEverythingItIsHandedAndBeforeAnyRetrysBrief()
    {
        var asked = await FixturePromptsAsync();

        Assert.True(asked.Count >= 8, $"Read {asked.Count} request(s), expected the pass's.");

        foreach (var request in asked)
        {
            var again = request.Prompt.LastIndexOf("\n" + SectionPrompt.NowWrite + "\n", StringComparison.Ordinal);

            Assert.True(again > 0, $"The {request.Section} request does not say its ask again.");

            // The ask the prompt opens on, said again word for word.
            var opening = request.Prompt.Split('\n')[2];

            Assert.StartsWith(opening, request.Prompt[(again + SectionPrompt.NowWrite.Length + 2)..], StringComparison.Ordinal);

            // After every document and every section already written.
            Assert.True(again > request.Prompt.LastIndexOf("\n[D", StringComparison.Ordinal), $"The {request.Section} request says its ask before a document.");
            Assert.True(again > request.Prompt.LastIndexOf("\nSections already written:", StringComparison.Ordinal), $"The {request.Section} request says its ask before the sections written.");

            // And a retry's brief after it, so what to fix is the last thing read.
            if (request.Prompt.Contains(RetryBrief.Opening, StringComparison.Ordinal))
            {
                Assert.True(request.Prompt.LastIndexOf(RetryBrief.Opening, StringComparison.Ordinal) > again, $"The {request.Section} retry's brief stands before the ask.");
            }
        }

        Assert.Contains(asked, request => request.Prompt.Contains(RetryBrief.Opening, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryRequestNamesItsReaderAndWhatToDoWithAFigureNoFactHolds()
    {
        const string Reader = "The reader is deciding whether to hold a swing trade in this stock over the next weeks to few months, so choose what could move the stock before and at its next report and leave out what would not.";
        const string Alternative = "Where a document gives a figure that is not listed under Facts, say what it shows without the number, such as that orders rose sharply or that most of the growth came from a single segment, and never write it as a figure or spell the number out; describe a change no listed figure sizes the same way, against its base.";

        var asked = await FixturePromptsAsync();

        Assert.All(asked, request => Assert.Contains(Reader, request.System, StringComparison.Ordinal));
        Assert.All(asked, request => Assert.Contains(Alternative, request.System, StringComparison.Ordinal));

        // The risks' own system, which opens on the answer's form, carries both.
        Assert.Contains(asked, request => RiskFields.IsRisks(request.Section) && request.System.StartsWith(SectionPrompt.AsFields, StringComparison.Ordinal));

        // And the reader is told to describe, never to prescribe.
        Assert.All(asked, request => Assert.Contains("never prescribe", request.System, StringComparison.Ordinal));
    }

    [Fact]
    public async Task EveryDocumentIsListedWithItsKindReadFromItsAddress()
    {
        // The rule over addresses worked by hand: the archive and a release wire are the company's own; an opinion site
        // and a mixed site's contributors' part are opinion; a mixed site's news and every other address are news.
        Assert.Equal(DocumentKinds.Filed, DocumentKinds.Of("https://www.sec.gov/Archives/edgar/data/1601046/000160104626000033/ex991.htm"));
        Assert.Equal(DocumentKinds.Filed, DocumentKinds.Of("https://www.businesswire.com/news/home/20260818/en/"));
        Assert.Equal(DocumentKinds.Opinion, DocumentKinds.Of("https://www.fool.com/investing/2026/09/01/should-you-buy/"));
        Assert.Equal(DocumentKinds.Opinion, DocumentKinds.Of("https://seekingalpha.com/article/4800000-keysight-a-buy"));
        Assert.Equal(DocumentKinds.News, DocumentKinds.Of("https://seekingalpha.com/news/4400000-keysight-expects-q4-revenue"));
        Assert.Equal(DocumentKinds.News, DocumentKinds.Of("https://finance.yahoo.com/markets/stocks/articles/keysight.html"));
        Assert.Equal(DocumentKinds.News, DocumentKinds.Of("not an address"));

        // Over the fixture's own requests: every document listed carries a kind, the release its own and the news the
        // news', and each request citing documents is told what the kinds mean.
        var asked = await FixturePromptsAsync();
        var listed = asked.SelectMany(request => Regex.Matches(request.Prompt, @"\n\[D\d+\] ([^\n]*)").Select(match => match.Groups[1].Value)).ToArray();

        Assert.NotEmpty(listed);
        Assert.All(listed, line => Assert.True(
            line.StartsWith(DocumentKinds.Filed + ": ", StringComparison.Ordinal) || line.StartsWith(DocumentKinds.News + ": ", StringComparison.Ordinal) || line.StartsWith(DocumentKinds.Opinion + ": ", StringComparison.Ordinal),
            $"'{line}' carries no kind."));
        Assert.Contains(listed, line => line.StartsWith(DocumentKinds.Filed + ": ", StringComparison.Ordinal));
        Assert.Contains(listed, line => line.StartsWith(DocumentKinds.News + ": ", StringComparison.Ordinal));
        Assert.All(asked.Where(request => request.DocumentIds.Count > 0), request => Assert.Contains(SectionPrompt.Kinds, request.System, StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheTwoCasesAndTheRisksAreGivenAWeakPointAndAStrongOneWithNoDigit()
    {
        foreach (var example in new[] { SectionPrompt.Examples.TwoCases, SectionPrompt.Examples.Risks })
        {
            Assert.DoesNotMatch(@"\d", example);
            Assert.Contains("a weak", example, StringComparison.Ordinal);
            Assert.Contains("a strong one", example, StringComparison.Ordinal);
        }

        Assert.EndsWith(SectionPrompt.Examples.TwoCases, SectionPrompt.Asks[ClaimRules.TwoCasesSection], StringComparison.Ordinal);
        Assert.EndsWith(SectionPrompt.Examples.Risks, SectionPrompt.Asks[RiskFields.Section], StringComparison.Ordinal);

        // Asked in the fixture's own requests for the two sections, and in no other section's.
        var asked = await FixturePromptsAsync();

        Assert.Contains(asked, request => request.Section == ClaimRules.TwoCasesSection && request.Prompt.Contains(SectionPrompt.Examples.TwoCases, StringComparison.Ordinal));
        Assert.Contains(asked, request => request.Section == RiskFields.Section && request.Prompt.Contains(SectionPrompt.Examples.Risks, StringComparison.Ordinal));
        Assert.DoesNotContain(asked, request => request.Section != ClaimRules.TwoCasesSection && request.Section != RiskFields.Section
            && (request.Prompt.Contains(SectionPrompt.Examples.TwoCases, StringComparison.Ordinal) || request.Prompt.Contains(SectionPrompt.Examples.Risks, StringComparison.Ordinal)));
    }

    static IConfiguration ReviewOn() =>
        new ConfigurationBuilder()
            .AddJsonFile(ResearchFeeds.ShippedConfiguration)
            .AddInMemoryCollection(
            [
                new KeyValuePair<string, string?>(ModelProfiles.KeyPath("Research"), ResearchFeeds.NotAKey),
                new KeyValuePair<string, string?>(ModelProfiles.JobField(ModelProfiles.ResearchJob, ResearchLane.ReviewField) + ":" + ModelProfiles.UseField, "deepseek"),
                new KeyValuePair<string, string?>(ModelProfiles.JobField(ModelProfiles.ResearchJob, ResearchLane.ReviewField) + ":From", "2026-09-08"),
            ])
            .Build();

    [Fact]
    public async Task AReviewIsOffAsShippedAndWhenNamedChecksItsOwnDraftBesideTheReportAndIsDrawnBesideTheTrial()
    {
        // As shipped the review names no profile, so no pass asks for one.
        Assert.Null(ResearchLane.Review(new ConfigurationBuilder().AddJsonFile(ResearchFeeds.ShippedConfiguration).Build()));

        var review = ResearchLane.Review(ReviewOn())!;

        Assert.Equal(("deepseek", 3), (review.Profile.Profile, review.Reports));
        Assert.Equal([ClaimRules.TwoCasesSection, RiskFields.Section], review.Sections);

        using var store = await FixtureReplay.ResearchedAsync();

        // The replayed pass under the run id a pass is given, which the run page reads its reports from.
        const string Run = PassRun.Prefix + "20260908T213000Z-KEYS";

        store.Execute($"UPDATE run_log SET run_id = '{Run}' WHERE run_id = 'replay-research';");

        // The same model as the pass, answering the two cases with a revision in the section's shape.
        var settings = ResearchFeeds.Pinned();
        var model = new TrialModel(settings, "The bull case is that orders keep rising [D1].\n\nThe bear case is that supply stays short [D1].");
        var cap = new SpendCap(model, Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);
        var sectionsBefore = Query(store, "SELECT COUNT(*) FROM research_section;").Single();
        var api = new ReadApi(store.DatabaseFile, ResearchClock);
        var pricedBefore = (await api.PaidCallSpendsAsync()).Where(call => call.RunId == Run).Sum(call => call.Spend);

        var outcomes = await new SectionTrial(cap, review with { Profile = settings }, ResearchClock, store.DatabaseFile, review: true).RunAsync("KEYS", Run);

        // The pass's own draft handed back with the checks, asked under the review's round, and nothing written into
        // the report.
        var two = Assert.Single(model.Asked, request => request.Section == ClaimRules.TwoCasesSection);
        var draft = Query(store, $"SELECT prose FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.TwoCasesSection}' ORDER BY version LIMIT 1;").Single();

        Assert.Contains(ReviewBrief.For(ClaimRules.TwoCasesSection, draft), two.Prompt, StringComparison.Ordinal);
        Assert.Contains(outcomes, outcome => outcome.Section == ClaimRules.TwoCasesSection && outcome.Outcome == SectionTrial.FirstTime);
        Assert.Equal(sectionsBefore, Query(store, "SELECT COUNT(*) FROM research_section;").Single());
        var reviewCalls = Query(store, $"SELECT spend FROM run_log WHERE run_id = '{Run}' AND stage LIKE 'research call:%, {TrialCalls.ReviewRound}%';");

        Assert.Equal(model.Requests, reviewCalls.Count);
        Assert.All(Query(store, $"SELECT stage FROM run_log WHERE run_id = '{Run}' AND stage LIKE 'research call:%, {TrialCalls.ReviewRound}%';"), stage => Assert.True(TrialCalls.Is(stage)));
        Assert.NotEmpty(Query(store, $"SELECT stage FROM run_log WHERE run_id = '{Run}' AND stage = '{SectionTrial.ReviewStageFor(ClaimRules.TwoCasesSection)}';"));

        // Its spend stands on the run, which the caps sum, and not in the pass's own price.
        Assert.True(reviewCalls.Sum(spend => decimal.Parse(spend, CultureInfo.InvariantCulture)) > 0m);
        Assert.Equal(pricedBefore, (await api.PaidCallSpendsAsync()).Where(call => call.RunId == Run).Sum(call => call.Spend));

        // Drawn beside the pass's own drafts, named as the model reviewing its draft, with its cost.
        var view = RunScreen.Reports(await api.ReportRowsAsync(), await api.ReportVersionsAsync(), new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 8));
        var row = Assert.Single(view.Trials, one => one.Section == ClaimRules.TwoCasesSection);

        Assert.Equal(TrialSide.OfReview, Assert.Single(row.Asked).Side);
        Assert.Equal(ReadApi.ReviewStage, SectionTrial.ReviewStage);

        var drawn = System.Net.WebUtility.HtmlDecode(new MarkRenderer().TrialsRegion(view.Trials, Web.App.SinglePageApp.NameRoute));

        Assert.Contains("<b>" + settings.Identity + ", reviewing its draft</b>: first time, 1 round", drawn, StringComparison.Ordinal);
        Assert.Contains("data-side=\"review\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-side=\"pass\"", drawn, StringComparison.Ordinal);
    }
}
