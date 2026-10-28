using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the report page's corrections the operator ruled on 2026-10-27: each a general rule over every name's page,
// asserted here over the fixture's pages and constructed rows.
public partial class ReadSurface
{
    // A section a local model wrote is named local, one the paid model wrote is named paid because the run log prices a
    // call by it, and the news labels and the release's reading are named the same way.
    // see: Every report names the model each of its jobs ran on and whether it is paid or local, the news region the model of its newest label
    [Fact]
    public async Task EveryReportNamesTheModelEachJobRanOnAndWhetherItIsPaidOrLocal()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES " +
            $"('{name}', 'What the company sells', 1, '{night}', 'qwen/qwen3.5-9b', 'accepted', 'A sentence.', '[]', NULL), " +
            $"('{name}', 'The dated calendar items', 1, '{night}', 'deepseek-flash', 'accepted', 'Another sentence.', '[]', NULL);");
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            "('research-probe', 'research call: The dated calendar items', '2026-09-05T04:00:00Z', '2026-09-05T04:01:00Z', 'ok', 0, 1, 1, '0.006', '{\"section\":\"The dated calendar items\",\"model\":\"deepseek-flash\"}');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{name}"));

        Assert.Contains("<tr data-job=\"What the company sells\" data-lane=\"local\"><td>What the company sells</td><td>the local model</td><td>qwen/qwen3.5-9b</td>", page, StringComparison.Ordinal);
        Assert.Contains("<tr data-job=\"The dated calendar items\" data-lane=\"paid\"><td>The dated calendar items</td><td>the paid model</td><td>deepseek-flash</td>", page, StringComparison.Ordinal);

        // The rule over constructed views: a labeller's model the run log prices is paid, the release's reader is local,
        // and stories no label stands for are named as such.
        var paid = new HashSet<string>(StringComparer.Ordinal) { "deepseek-flash" };
        var news = new NewsView(name, new DateOnly(2026, 9, 4), [], 79, 3, 2, 15, 0, 59, "deepseek-flash", "deepseek", null, null);
        var release = new ReleaseReadingView(new DateOnly(2026, 9, 1), new EquityBrief.Core.Releases.ReleaseAnswer("better", "raised", "confident", "not stated"), 5, "google/gemma-4-26b-a4b-qat");
        var jobs = MarkRenderer.ModelJobs([], news, release, paid);

        Assert.Equal(
            [("News labels", true, "labelled 20 of the 79 stories the page draws"), ("The newest results release's reading", false, "read the release accepted 2026-09-01")],
            jobs.Select(job => (job.Job, job.Paid, job.Detail)));
        Assert.Contains("data-lane=\"none\"><td>News labels</td><td>no model</td>", MarkRenderer.ModelJobsTable(MarkRenderer.ModelJobs([], news with { Model = null, Unlabelled = 79 }, null, paid)), StringComparison.Ordinal);
    }

    // A story the feed tagged a member with is the member's only where it names the company by its name or by its ticker
    // beside an American exchange or a ticker's own marks, and a listing on another exchange is never the member's: ROP's
    // Roche stories, tagged with Roche's own listing under the same three letters, are none of Roper's.
    // see: A story is a member's where it names the company by its name or its American listing, and no other story is drawn or counted
    [Fact]
    public void AStoryIsAMembersOnlyWhereItNamesTheCompanyOrItsAmericanListing()
    {
        const string Roper = "Roper Technologies Inc";

        Assert.Equal(["Roper Technologies", "Roper"], EquityBrief.Core.News.StoryMatch.NameKeys(Roper));
        Assert.Equal(["AMERICAN FINANCIAL"], EquityBrief.Core.News.StoryMatch.NameKeys("AMERICAN FINANCIAL GROUP"));
        Assert.Equal(["General Mills"], EquityBrief.Core.News.StoryMatch.NameKeys("General Mills Inc"));
        Assert.Equal(["Amazon"], EquityBrief.Core.News.StoryMatch.NameKeys("Amazon.com Inc"));

        // Roper by its name, its ticker in brackets, after its exchange or with the American mark.
        Assert.True(EquityBrief.Core.News.StoryMatch.IsAbout("Roper Technologies raises its guidance", string.Empty, "ROP", Roper));
        Assert.True(EquityBrief.Core.News.StoryMatch.IsAbout("Earnings before the open", "Results from Acme (ROP) and others.", "ROP", Roper));
        Assert.True(EquityBrief.Core.News.StoryMatch.IsAbout("Software results", "(Nasdaq: ROP) reported", "ROP", Roper));
        Assert.True(EquityBrief.Core.News.StoryMatch.IsAbout("Software results", "Shares of ROP.US rose", "ROP", Roper));

        // Roche, under its Swiss listing of the same letters, and a story naming neither, are not Roper's.
        Assert.False(EquityBrief.Core.News.StoryMatch.IsAbout("What Could Roche Holding (SWX:ROP) Gain From FDA Priority Review In MS?", "Roche said.", "ROP", Roper));
        Assert.False(EquityBrief.Core.News.StoryMatch.IsAbout("Roche keeps 2026 outlook as Q3 sales rise", "Basel. Roche Holding AG said on Thursday.", "ROP", Roper));
        Assert.False(EquityBrief.Core.News.StoryMatch.IsAbout("Biomarkers Market Forecasts Growth", "Profiling Roche, Abbott and Siemens.", "ROP", Roper));
        Assert.False(EquityBrief.Core.News.StoryMatch.IsAbout("Shares on the Swiss exchange", "ROP.SW closed lower.", "ROP", Roper));

        // A listing the feed tags on another exchange is dropped as the payload is read, and the American one kept bare.
        Assert.True(EquityBrief.Core.News.StoryMatch.American("ROP.US"));
        Assert.True(EquityBrief.Core.News.StoryMatch.American("ROP"));
        Assert.False(EquityBrief.Core.News.StoryMatch.American("ROP.SW"));
        var read = EquityBrief.Core.Providers.RecordedNewsFeed.Parse("[{\"date\":\"2026-10-22T12:00:00+00:00\",\"title\":\"t\",\"link\":\"https://example.com/a\",\"content\":\"c\",\"symbols\":[\"ROP.US\",\"ROP.SW\",\"RHHBY.F\"]}]")[0];

        Assert.Equal(["ROP"], read.American!);
        Assert.Equal(["ROP", "RHHBY"], read.Symbols);
        Assert.Equal(["ROP"], EquityBrief.Core.Providers.NewsAttribution.ByName([read]).Keys);
        Assert.Empty(EquityBrief.Core.Providers.NewsAttribution.ByName([read with { American = [] }]));

        // A name no index holds carries no company, and its stories are kept as stored.
        string[] stories = ["Roche keeps 2026 outlook"];

        Assert.Single(EquityBrief.Core.News.StoryMatch.Members(stories, story => (story, string.Empty), "ROP", null));
        Assert.Empty(EquityBrief.Core.News.StoryMatch.Members(stories, story => (story, string.Empty), "ROP", Roper));
    }

    // Why the trend rule read a range, in plain words from its three inputs: ROP's close and averages rose, and its newest
    // swing low stood below the one before it, which alone kept it out of an uptrend.
    // see: How the rules read it opens on the checks of the family that picked the stock, and every input that failed is named in plain words
    [Fact]
    public void TheTrendRulesReadingIsSaidInPlainWordsFromTheInputsItMissed()
    {
        var lows = new[] { new EquityBrief.Core.Report.SwingLow(new DateOnly(2026, 9, 29), 346.578m), new EquityBrief.Core.Report.SwingLow(new DateOnly(2026, 8, 13), 386.8467m) };

        Assert.Equal(
            "The trend rule reads an uptrend where the close is above its 50-day average, the 50-day is above the 200-day and the newest swing low is above the one before it. Here the newest swing low, 346.58 on 2026-09-29, is not above the one before it, 386.85 on 2026-08-13, so it reads a range.",
            EquityBrief.Core.Report.TrendWords.Why("range", 441.65m, 387.90, 362.86, lows));
        Assert.Null(EquityBrief.Core.Report.TrendWords.Why("uptrend", 441.65m, 387.90, 362.86, lows));
        Assert.EndsWith(
            "Here the close of 300.00 is not above its 50-day average of 387.90, the 50-day average of 387.90 is not above the 200-day's 390.00 and the newest swing low, 346.58 on 2026-09-29, is not above the one before it, 386.85 on 2026-08-13, so it reads a downtrend.",
            EquityBrief.Core.Report.TrendWords.Why("downtrend", 300m, 387.90, 390.00, lows),
            StringComparison.Ordinal);
        Assert.Contains("are not all stored", EquityBrief.Core.Report.TrendWords.Why("range", 441.65m, null, 362.86, lows), StringComparison.Ordinal);

        // A stock a setup family picked opens the rules on that family's own sentence and checks.
        var card = new DecisionCardView("GSPC", new DateOnly(2026, 10, 26), "drift", "ROP", "the drift rule", 441.65m, 406.46m, 529.64m,
            [new CardLineView(1, EquityBrief.Core.Cards.CardLines.TrendName, "tick", "drift: beat its estimate, the report of 2026-10-22 beat its estimate by 1.73%")], null);
        var picked = new MarkRenderer().PickedBy(card);

        Assert.StartsWith("<section class=\"picked-by\" data-family=\"drift\"><h4>Why the ", picked, StringComparison.Ordinal);
        Assert.Contains("Picked by the ", picked, StringComparison.Ordinal);
        Assert.Contains("<h5>Why it qualified</h5>", picked, StringComparison.Ordinal);
    }

    // Each concept holds one value worked at the stored close, and a provider's figure read back to the price it was struck
    // at is stale where that price stands more than the stated share from the close: ROP's figures, worked by hand. The
    // shares are the company's value of 45.3 billion over the close of 441.65; the provider's 35.5 billion over them is a
    // price of about 346, its forward 15.0 times the 24.38 a share it divided by is 365.70, and its 1.0% yield on the 3.64
    // rate is 364.00, each more than 3% from the close.
    // see: Each concept a page draws holds one value worked at the stored close beside the provider's own marked stale where struck far from it, and a check flags any concept drawn twice
    [Fact]
    public void EachConceptHoldsOneValueAtTheCloseAndAProvidersFigureStruckFarFromItIsStale()
    {
        const decimal Close = 441.65m;
        const decimal CompanyValue = 45_300_000_000m;
        var shares = CompanyValue / Close;

        var value = EquityBrief.Core.Report.OneValue.StruckValue(35_500_000_000m, shares, Close)!;
        var forward = EquityBrief.Core.Report.OneValue.StruckMultiple(15.0m, 24.38m, Close)!;
        var yield = EquityBrief.Core.Report.OneValue.StruckYield(0.0100m, 3.64m, Close)!;

        Assert.Equal((346.11m, true), (Math.Round(value.StruckAt, 2), value.Stale));
        Assert.Equal((365.70m, true), (forward.StruckAt, forward.Stale));
        Assert.Equal((364.00m, true), (yield.StruckAt, yield.Stale));
        Assert.Equal("18.1", EquityBrief.Core.Report.OneValue.Multiple(Close, 24.38m)!.Value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("0.82%", EquityBrief.Core.Report.ConceptCheck.Yield(EquityBrief.Core.Report.OneValue.Yield(3.64m, Close)!.Value));

        // A provider's figure struck within the share of the close is not stale, and one without its base is not read back.
        Assert.False(EquityBrief.Core.Report.OneValue.StruckMultiple(18.0m, 24.38m, Close)!.Stale);
        Assert.Null(EquityBrief.Core.Report.OneValue.StruckMultiple(15.0m, null, Close));

        // The check reads every marked place back and names a concept drawn with two values, and no other.
        var page = "<dd data-concept=\"market-value\" data-concept-value=\"$45.3B\">x</dd><span data-concept=\"market-value\" data-concept-value=\"$35.5B\">y</span>"
            + "<p data-concept=\"forward-multiple\" data-concept-value=\"18.1\">z</p><td data-concept=\"forward-multiple\" data-concept-value=\"18.1\">w</td>";
        var clashes = EquityBrief.Core.Report.ConceptCheck.Clashes(page);

        Assert.Equal(["market-value"], clashes.Select(clash => clash.Concept));
        Assert.Equal(["$45.3B", "$35.5B"], clashes[0].Values);
        Assert.Equal(
            "<p class=\"notice concept-clash\" role=\"status\" data-clashes=\"1\">This page draws one concept with more than one value, which a fix owes: market value drawn as $45.3B and $35.5B.</p>",
            MarkRenderer.ConceptClashes(clashes));
        Assert.Equal(string.Empty, MarkRenderer.ConceptClashes([]));
    }

    // Where no report date is on file, the next is estimated a year on from the company's report of the year before that
    // falls first after the night: ROP's reports of 2025-10-23, 2026-01-27, 2026-04-23, 2026-07-23 and 2026-10-22 against
    // the night of 2026-10-26 give 2026-01-27's, fifty-two weeks on, 2027-01-26, a Tuesday as the report was. Once the open
    // after the night prints, the fill and the reward against the risk at it are restated: a fill at 440.00 under a target
    // of 529.64 and a stop of 406.46 is (529.64 - 440.00) / (440.00 - 406.46) = 89.64 / 33.54 = 2.67.
    // see: A pick's card estimates the next report from the company's own a year before and places it in the hold, and restates the fill and its reward to risk once the open prints
    [Fact]
    public void APicksCardEstimatesItsNextReportAndRestatesTheFillOnceTheOpenPrints()
    {
        var night = new DateOnly(2026, 10, 26);
        DateOnly[] reported = [new(2025, 10, 23), new(2026, 1, 27), new(2026, 4, 23), new(2026, 7, 23), new(2026, 10, 22)];

        var estimate = EquityBrief.Core.Cards.CardLines.EstimatedReport(reported, night);

        Assert.Equal((new DateOnly(2026, 1, 27), new DateOnly(2027, 1, 26)), estimate);
        Assert.Equal(DayOfWeek.Tuesday, estimate!.Value.Estimated.DayOfWeek);
        Assert.Null(EquityBrief.Core.Cards.CardLines.EstimatedReport([new DateOnly(2025, 1, 2)], night));

        // Inside a hold of 90 sessions it warns; after one of 60 it notes; marked estimated either way.
        var inside = EquityBrief.Core.Cards.CardLines.EstimatedEarnings(estimate.Value, night, 90);
        var after = EquityBrief.Core.Cards.CardLines.EstimatedEarnings(estimate.Value, night, 60);

        Assert.Equal(("warning", true), (inside.Mark, inside.Words.Contains("inside the hold's cap of 90 sessions", StringComparison.Ordinal)));
        Assert.Equal(("note", true), (after.Mark, after.Words.Contains("after the hold's cap of 60 sessions", StringComparison.Ordinal)));
        Assert.StartsWith("No report date is on file for it; estimated from its report of 2026-01-27 a year on, its next is about 2027-01-26", after.Words, StringComparison.Ordinal);

        // The page's card reads the estimate in place of the line saying none is on file.
        var card = new DecisionCardView("GSPC", night, "drift", "ROP", "the drift rule", 441.65m, 406.46m, 529.64m,
            [new CardLineView(4, EquityBrief.Core.Cards.CardLines.EarningsName, "warning", "No report date is on file for it, so a report inside the hold cannot be ruled out.")], null, Cap: 60);
        var estimated = NameScreen.WithEstimatedReport(card, reported)!;

        Assert.Equal(after.Words, estimated.Lines.Single().Words);

        // The fill once the open prints, and none before it.
        Assert.Null(EquityBrief.Core.Cards.CardLines.Filled(night, 441.65m, 406.46m, 529.64m, [(night, 441.65m)]));
        Assert.Equal(
            "Filled at the open of 2026-10-27, 440.00, against the 441.65 the plan was worked at; at the fill the reward against the risk is 2.67, the target of 529.64 over the stop of 406.46.",
            EquityBrief.Core.Cards.CardLines.Filled(night, 441.65m, 406.46m, 529.64m, [(night, 441.65m), (new DateOnly(2026, 10, 27), 440.00m)]));
        Assert.EndsWith(
            "the open was at or below the stop of 406.46, so the plan's reward against its risk does not stand at the fill.",
            EquityBrief.Core.Cards.CardLines.Filled(night, 441.65m, 406.46m, 529.64m, [(new DateOnly(2026, 10, 27), 400.00m)]),
            StringComparison.Ordinal);

        // The plan region opens on the pick's own exit, and the chart draws its stop and target.
        var marks = new MarkRenderer();

        Assert.Contains("its trade is sold at 529.64, at a close below 406.46 or after 60 trading days, whichever comes first.", marks.PicksOwnExit(card), StringComparison.Ordinal);

        var bars = Enumerable.Range(0, 40).Select(day => new ChartBar(night.AddDays(day - 40), 420m, 450m, 400m, 430m + (day % 5), 1000)).ToArray();
        var chart = marks.LevelChart("ROP", bars, frame: new ChartFrame(Pick: new ChartPick("drift", 406.46m, 529.64m)));

        Assert.Contains("<line class=\"m-pick m-pick-stop\" data-stop=\"406.46\"", chart, StringComparison.Ordinal);
        Assert.Contains("the pick's target, 529.64, above the chart", chart, StringComparison.Ordinal);
    }

    // ROP's two contradicted sentences, held to its stored latest quarter: an operating margin of 28.7% against 28.4% a
    // year earlier is wider, so "slightly narrower" is withheld with what the figures show; revenue 0.2% over its estimate is
    // a beat, so "fell just short of the consensus" keeps its sentence and is given the stored side beside it. A sentence
    // about an adjusted figure is not read, the markers are taken out and DEPS is spelled out where first used.
    // see: A written sentence stating a direction the stored latest quarter contradicts is withheld where it is drawn, and a beat or a miss another source states against it is named beside it
    // see: A written section is drawn without its bracketed markers, its sources listed by title beneath it and every abbreviation spelled out where first used
    [Fact]
    public void AWrittenSentenceTheStoredQuarterContradictsIsWithheldOrGivenTheStoredSide()
    {
        static EquityBrief.Core.Report.QuarterLine Line(string name, double? against, double? year, double? margin = null, double? before = null) =>
            new(name, 1m, null, null, against, null, year, margin, before, false, null, null, null);

        var quarter = new EquityBrief.Core.Report.LatestQuarter(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 22), new DateOnly(2025, 9, 30),
        [
            Line("Revenue", 0.2, 7.6),
            Line("Gross profit", null, 8.5, 70.1, 69.5),
            Line("Operating income", null, 8.8, 28.7, 28.4),
            Line("Net income", null, 21.6, 22.3, 19.8),
            Line("Earnings a share", 1.7, 14.6),
        ]);
        var facts = EquityBrief.Core.Research.Directions.Of(quarter);

        Assert.Contains(facts, fact => fact is { Concept: "operating margin", Kind: "margin", Sign: 1 });
        Assert.Contains(facts, fact => fact is { Concept: "revenue", Kind: "estimate", Sign: 1 });

        const string Margin = "Selling, general and administrative costs grew faster than revenue and unallocated corporate costs also rose, leaving the operating margin slightly narrower than a year earlier [N].";
        const string Bear = "The bear case begins with revenue, which fell just short of the consensus estimate, so the quarter was an earnings beat without a demand beat [D2].";

        Assert.Equal(
            "A sentence is withheld here: it said the operating margin was narrower, where the stored figures show 28.7% against 28.4% a year earlier, wider.",
            MarkRenderer.Readable(Margin, facts));
        Assert.Equal(
            "The bear case begins with revenue, which fell just short of the consensus estimate, so the quarter was an earnings beat without a demand beat; the estimate the page draws puts the revenue at +0.2%, a beat.",
            MarkRenderer.Readable(Bear, facts));

        // The withheld sentence keeps the words its part opens on, a sentence agreeing with the figures stands as written,
        // and one about an adjusted figure is not read.
        Assert.StartsWith("The bear case: A sentence is withheld here", MarkRenderer.Readable("The bear case is that the operating margin narrowed [N].", facts), StringComparison.Ordinal);
        Assert.Equal("Revenue beat the consensus estimate.", MarkRenderer.Readable("Revenue beat the consensus estimate [D1].", facts));
        Assert.Equal("The adjusted earnings before interest, taxes, depreciation and amortization (EBITDA) margin narrowed.", MarkRenderer.Readable("The adjusted EBITDA margin narrowed [D1].", facts));

        // The markers come out and DEPS is spelled out where first used, and only there.
        Assert.Equal(
            "Roper lifted its full year adjusted diluted earnings a share (DEPS) outlook. The DEPS range moved up.",
            MarkRenderer.Readable("Roper lifted its full year adjusted DEPS outlook [D1][D2]. The DEPS range moved up [D1].", facts));

        // The headline is held to the same directions, and none stand where the page holds no latest quarter.
        Assert.NotEmpty(EquityBrief.Core.Research.Directions.Clashes("Margins narrowed as the operating margin slipped.", facts));
        Assert.Empty(EquityBrief.Core.Research.Directions.Of(null));
    }

    // The proof reads each rule back off a drawn page: a marker left in the prose fails, two values of one concept fail, a
    // masthead's pill passes, a rule whose region is not drawn or that is not built is unexamined, and the counts over
    // several pages name the pages each rule failed on, unexamined counted apart from a pass.
    // see: Every report page rule is held over every name's drawn page and reported passed, failed or unexamined with the pages each failed on
    [Fact]
    public void TheProofReadsEachRuleBackOffADrawnPageAndCountsUnexaminedApart()
    {
        const string Held = "<span class=\"m-price\" data-live=\"no\"><span class=\"pill closed\">CLOSED</span></span><p class=\"prose\">Revenue rose.</p><dd data-concept=\"market-value\" data-concept-value=\"$45.3B\"></dd>";
        const string Broken = "<p class=\"prose\">Revenue rose [D1]. Adjusted DEPS rose.</p><dd data-concept=\"market-value\" data-concept-value=\"$45.3B\"></dd><span data-concept=\"market-value\" data-concept-value=\"$35.5B\"></span>";

        var held = ReportProof.Evaluate("AAA", Held);
        var broken = ReportProof.Evaluate("BBB", Broken);

        string Verdict(ProofPage page, string rule) => page.Verdicts.Single(verdict => verdict.Rule == rule).Verdict;

        Assert.Equal((ReportProof.Passed, ReportProof.Failed), (Verdict(held, "markers"), Verdict(broken, "markers")));
        Assert.Equal((ReportProof.Passed, ReportProof.Failed), (Verdict(held, "one-value"), Verdict(broken, "one-value")));
        Assert.Equal((ReportProof.Passed, ReportProof.Failed), (Verdict(held, "abbreviations"), Verdict(broken, "abbreviations")));
        Assert.Equal((ReportProof.Passed, ReportProof.Failed), (Verdict(held, "header"), Verdict(broken, "header")));
        Assert.Equal(ReportProof.Unexamined, Verdict(held, "rules-family"));
        Assert.Equal(ReportProof.Unexamined, Verdict(held, "levels-older"));
        Assert.Equal(ReportProof.Unexamined, Verdict(held, "newest-quarter"));
        Assert.Equal(ReportProof.Rules.Count, held.Verdicts.Count);

        // A quarter dated by an older filing beside a release accepted after the following quarter ended fails, one beside
        // its own quarter's release filed the day after the filing passes, and one labelled as the release's passes.
        const string Release = "<blockquote class=\"guidance\" data-guidance=\"located\" data-exhibit=\"a.htm\" data-filed=\"2026-10-22\">";
        const string Filed = "<section class=\"card spined fund\" id=\"quarter\"><div class=\"spine\"><div class=\"lbl\">The latest quarter</div><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>2026-07-24</b><span>from the filing</span></div></div><div class=\"main\"><h2>The quarter to 2026-06-30, line by line</h2></div></section>";

        Assert.Equal(ReportProof.Failed, Verdict(ReportProof.Evaluate("CCC", Filed + Release), "newest-quarter"));
        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", Filed + Release.Replace("2026-10-22", "2026-07-25")), "newest-quarter"));
        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", Filed), "newest-quarter"));

        // A section left out with no control writing it again alone fails, one with it passes, and the industry cycle,
        // its theme's, asks for none.
        const string LeftOut = "<p class=\"left-out\" data-section=\"The risks\">The risks are left out: refused.</p>";
        const string Cycle = "<p class=\"not-written\" data-section=\"The industry cycle\">The industry cycle is not written: no source.</p>";
        var again = new MarkRenderer().LeftOut("CCC", [new LeftOutSection("CCC", "The risks", "refused.")], cost: new ResearchCost(1, 0.05m, 0.05m, "within the caps"));

        Assert.Equal(ReportProof.Failed, Verdict(ReportProof.Evaluate("CCC", LeftOut + Cycle), "rerun-alone"));
        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", again + Cycle), "rerun-alone"));
        Assert.Equal(ReportProof.Unexamined, Verdict(ReportProof.Evaluate("CCC", Cycle), "rerun-alone"));
        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", Filed.Replace(">Filed<", ">Reported<").Replace("2026-07-24", "2026-10-22") + Release), "newest-quarter"));

        // Each drawn sentence of a section held to the stored quarter is read against its directions again: ROP's bear
        // case calling a margin of 28.7% against 28.4% narrower fails where drawn as written, passes as the page draws it,
        // its place withheld, and a page holding no section held to a stored quarter is unexamined.
        var widened = new EquityBrief.Core.Research.DirectionFact("operating margin", EquityBrief.Core.Research.Directions.Margin, 1, "28.7% against 28.4% a year earlier");
        var bear = new WrittenCell("The two cases", "The bear case: the operating margin was slightly narrower than a year earlier.", new DateOnly(2026, 10, 26), "deepseek-flash", []);
        var drawnBear = new MarkRenderer().WrittenSection("CCC", bear, [], facts: [widened]);
        const string AsWritten = "<section class=\"written-section\" data-ticker=\"CCC\" data-section=\"The two cases\" data-directions=\"operating margin|margin|1\"><p class=\"prose\">The bear case: the operating margin was slightly narrower than a year earlier.</p></section>";

        Assert.Contains("data-directions=\"operating margin|margin|1\"", drawnBear, StringComparison.Ordinal);
        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", drawnBear), "directions"));
        Assert.Equal(ReportProof.Failed, Verdict(ReportProof.Evaluate("CCC", AsWritten), "directions"));
        Assert.Equal(ReportProof.Unexamined, Verdict(held, "directions"));

        // The news region states the stories it left out and its counts add up over the stories it kept: seven, six in the
        // whole list and one opinion, the six one positive, one negative, one neutral and three not labelled.
        const string News = "<div class=\"news-region\" data-ticker=\"CCC\" data-articles=\"7\" data-all=\"6\" data-positive=\"1\" data-negative=\"1\" data-neutral=\"1\" data-opinion=\"1\" data-unlabelled=\"3\" data-left-out=\"35\">";

        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", News), "news-matched"));
        Assert.Equal(ReportProof.Failed, Verdict(ReportProof.Evaluate("CCC", News.Replace("data-unlabelled=\"3\"", "data-unlabelled=\"2\"")), "news-matched"));
        Assert.Equal(ReportProof.Failed, Verdict(ReportProof.Evaluate("CCC", News.Replace(" data-left-out=\"35\"", string.Empty)), "news-matched"));
        Assert.Equal(ReportProof.Unexamined, Verdict(held, "news-matched"));

        // The peer group is read against the page's own account of what the company sells: ROP's application software in a
        // group of electronic equipment makers is flagged, and a page drawing the flag where the group names the business
        // fails as one drawing none where it does not.
        const string Peers = "<div class=\"sub\">Against Electronic Equipment, Instruments &amp; Components's S&amp;P 500 members on the night, the provider's classification</div>";
        var sells = new MarkRenderer().WrittenSection("CCC", new WrittenCell(MarkRenderer.WhatItSells, "Roper sells vertical application software and network software to niche markets.", new DateOnly(2026, 10, 26), "deepseek-flash", []), []);
        var flag = MarkRenderer.IndustryMismatch("Electronic Equipment, Instruments & Components", "Roper sells vertical application software and network software to niche markets.");

        Assert.NotEqual(string.Empty, flag);
        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", Peers + flag + sells), "industry-mismatch"));
        Assert.Equal(ReportProof.Failed, Verdict(ReportProof.Evaluate("CCC", Peers + sells), "industry-mismatch"));
        Assert.Equal(ReportProof.Passed, Verdict(ReportProof.Evaluate("CCC", Peers + sells), "industry-label"));
        Assert.Equal(ReportProof.Failed, Verdict(ReportProof.Evaluate("CCC", Peers.Replace("the provider's classification", "a group")), "industry-label"));
        Assert.Equal(ReportProof.Unexamined, Verdict(ReportProof.Evaluate("CCC", Peers), "industry-mismatch"));

        var counted = ReportProof.Counted([held, broken]).Single(rule => rule.Rule == "one-value");

        Assert.Equal((1, 1, 0), (counted.Passed, counted.Failed, counted.Unexamined));
        Assert.Equal(["BBB"], counted.Failing);

        // A rule neither page can be read for counts both as unexamined and neither as passed.
        var unread = ReportProof.Counted([held, broken]).Single(rule => rule.Rule == "rules-family");

        Assert.Equal((0, 0, 2), (unread.Passed, unread.Failed, unread.Unexamined));
        Assert.Contains("unexamined is never counted as a pass", ReportProof.Html(new ProofReport(DateTimeOffset.UnixEpoch, "GSPC", null, ReportProof.Counted([held, broken]), [held, broken])), StringComparison.Ordinal);
    }

    // Sentences joined by a marker after the full stop and a line break are each read alone, as GM's short version holds
    // them, so a sentence about an adjusted figure leaves its neighbour read; and a section written before the latest
    // quarter was reported speaks of an earlier quarter and is held to nothing.
    // see: A written sentence stating a direction the stored latest quarter contradicts is withheld where it is drawn, and a beat or a miss another source states against it is named beside it
    [Fact]
    public void SentencesJoinedByAMarkerAreReadAloneAndASectionWrittenBeforeTheQuarterIsHeldToNothing()
    {
        var quarter = new EquityBrief.Core.Report.LatestQuarter(
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 10, 21),
            new DateOnly(2025, 9, 30),
            [new EquityBrief.Core.Report.QuarterLine(EquityBrief.Core.Report.QuarterLines.NetIncome, 1_300_000_000m, null, null, null, 1_100_000_000m, 18.2, 6.9, 5.8, false, null, null, null)]);
        const string Short = "\"Adjusted operating income\" rose and its adjusted margin widened from a year earlier. [C7]\nNet income attributable to stockholders fell from a year earlier. [C9]";

        var held = MarkRenderer.Readable(Short, EquityBrief.Core.Research.Directions.For(quarter, new DateOnly(2026, 10, 22)));

        Assert.Equal("\"Adjusted operating income\" rose and its adjusted margin widened from a year earlier. A sentence is withheld here: it said the net income fell, where the stored figures show +18.2% on a year earlier, rose.", held);
        Assert.Empty(EquityBrief.Core.Research.Directions.For(quarter, new DateOnly(2026, 10, 11)));
        Assert.NotEmpty(EquityBrief.Core.Research.Directions.For(quarter, new DateOnly(2026, 10, 21)));
        Assert.Equal("\"Adjusted operating income\" rose and its adjusted margin widened from a year earlier. Net income attributable to stockholders fell from a year earlier.", MarkRenderer.Readable(Short, EquityBrief.Core.Research.Directions.For(quarter, new DateOnly(2026, 10, 11))));
    }

    // A quarter reported after the newest filing the store holds is dated by its report and labelled as the results
    // release's, and its segments say they are the older filing's: MSFT's newest filing taken out of a replayed store, as
    // a release stands before its filing is stored.
    // see: A quarter reported before its filing is stored is labelled as the release's, its awaited and next report lines moving on and its release quoted where it says what it reported
    [Fact]
    public async Task AQuarterReportedAfterTheNewestStoredFilingIsDatedByItsReportAndLabelledAsTheReleases()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var filed = Rows(store, "SELECT filing_date FROM fundamentals WHERE ticker = 'MSFT' ORDER BY filing_date DESC;").Select(row => row[0]).ToArray();
        var reported = Rows(store, "SELECT report_date FROM reported_quarter WHERE ticker = 'MSFT' AND fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = 'MSFT') ORDER BY period_end DESC LIMIT 1;").Single()[0];

        // As stored, the quarter is the newest filing's.
        Assert.Contains($"<div class=\"lbl\">The latest quarter</div><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>{filed[0]}</b><span>from the filing</span>", await NameRoute(store, "MSFT"), StringComparison.Ordinal);

        store.Execute($"DELETE FROM fundamentals WHERE ticker = 'MSFT' AND filing_date = '{filed[0]}';");

        var page = await NameRoute(store, "MSFT");

        Assert.Contains($"<div class=\"lbl\">The latest quarter</div><div class=\"dl\"><span class=\"dl-k\">Reported</span><b>{reported}</b><span>from the results release, filing not stored yet</span>", page, StringComparison.Ordinal);
        Assert.Equal(filed.Length > 1, page.Contains("<span>from the filing before the newest quarter, whose filing is not stored yet</span>", StringComparison.Ordinal));
        Assert.Equal(ReportProof.Passed, ReportProof.Evaluate("MSFT", page).Verdicts.Single(verdict => verdict.Rule == "newest-quarter").Verdict);
    }

    // A next report the night has passed is said as reported, with the one after it not announced, in the snapshot and in
    // the estimate's line beneath the numbers; and a results release newer than the one the guidance was read from opens
    // the box management speaks in on its sentence saying what the company reported.
    // see: A quarter reported before its filing is stored is labelled as the release's, its awaited and next report lines moving on and its release quoted where it says what it reported
    [Fact]
    public async Task AReportTheNightHasPassedIsSaidAsReportedAndANewerReleaseOpensTheBoxManagementSpeaksIn()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        var filings = await Api(store).FundamentalsAsync(Name);

        using var payload = System.Text.Json.JsonDocument.Parse(StoredPayload(store, Name));

        var estimated = payload.RootElement.GetProperty("estimated");
        var reportDate = DateOnly.ParseExact(estimated.GetProperty("reportDate").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var periodEnd = estimated.GetProperty("periodEnd").GetString();

        // The night before the report the next report stands as stored, and on its day it is reported.
        var before = NameScreen.Numbers(filings, new ConceptFigures(null, reportDate.AddDays(-1), null, null));
        var after = NameScreen.Numbers(filings, new ConceptFigures(null, reportDate, null, null));

        Assert.DoesNotContain("data-reportDate=\"reported\"", before, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"<div><dt>Next report</dt><dd data-reportDate=\"reported\"><b>not announced</b><small>the quarter to {periodEnd} was reported on {reportDate:yyyy-MM-dd}</small></dd></div>"), after, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"The quarter to {periodEnd} was reported on {reportDate:yyyy-MM-dd}, and the next report is not announced."), after, StringComparison.Ordinal);

        // A release a year after the guidance's opens the box on its sentence saying what the company reported; one before
        // it does not.
        var newer = EquityBrief.Core.Report.ReleaseOpening.Of("ex99.htm", reportDate.AddDays(400), "https://www.sec.gov/ex99.htm", "Cupertino, California, October 30, 2027 ... Apple today announced financial results for its fiscal 2027 fourth quarter. Revenue rose.", "Apple Inc.", "AAPL");
        var released = NameScreen.Numbers(filings, new ConceptFigures(null, reportDate, null, null), newer);

        Assert.Contains("<blockquote class=\"guidance release-opening\"", released, StringComparison.Ordinal);
        Assert.Contains("opens</p><p>Apple today announced financial results for its fiscal 2027 fourth quarter.</p>", WebUtility.HtmlDecode(released), StringComparison.Ordinal);

        if (payload.RootElement.TryGetProperty("guidance", out var guidance) && guidance.TryGetProperty("filedOn", out var guidedOn) && guidedOn.GetString() is { Length: 10 } guided)
        {
            var older = EquityBrief.Core.Report.ReleaseOpening.Of("ex99.htm", DateOnly.ParseExact(guided, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture), "https://www.sec.gov/ex99.htm", "Cupertino, California, July 30, 2026 ... Apple today announced financial results.", "Apple Inc.", "AAPL");

            Assert.DoesNotContain("release-opening", NameScreen.Numbers(filings, new ConceptFigures(null, reportDate, null, null), older), StringComparison.Ordinal);
        }
    }

    // The box management speaks in quotes the newest release's sentence saying what the company reported, read by its
    // name, its ticker or its listing past a headline, its bullets and its dateline: each line below read by hand off the
    // stored release it is cut from, as the store held each on 2026-10-28, where the dateline the box read before found no
    // line in a release whose city is written in capitals and quoted a conference call's day in others.
    // see: A quarter reported before its filing is stored is labelled as the release's, its awaited and next report lines moving on and its release quoted where it says what it reported
    [Fact]
    public void TheBoxManagementSpeaksInQuotesTheSentenceInWhichTheReleaseSaysWhatTheCompanyReported()
    {
        static string? Line(string text, string company, string ticker) =>
            EquityBrief.Core.Report.ReleaseOpening.Of("ex99.htm", new DateOnly(2026, 10, 28), "u", text, company, ticker)?.Line;

        // A city in capitals, a dash either side of its day, and a conference call's day later in the text.
        Assert.Equal(
            "Everpure (NYSE: P), the company revolutionizing storage and data management, today announced financial results for its second quarter fiscal year 2027 ended August 2, 2026.",
            Line("EX-99.1 Document Exhibit 99.1 Everpure Announces Second Quarter Fiscal 2027 Financial Results Total revenue growth of 38% year-over-year SANTA CLARA, Calif. – August 26, 2026 – Everpure (NYSE: P), the company revolutionizing storage and data management, today announced financial results for its second quarter fiscal year 2027 ended August 2, 2026. “Q2 marks eight straight quarters of accelerating revenue growth.” The company will host an investor day on Wednesday, September 23, 2026.", "Everpure, Inc.", "P"));

        // Highlights in bullets, one of them "on a reported basis", before a city whose day stands in brackets.
        Assert.Equal(
            "Allegion plc (NYSE: ALLE), a leading global security products and solutions provider, today reported financial results for its third quarter (ended Sept. 30, 2026).",
            Line("EX-99.1 PRESS RELEASE DATED OCTOBER 22, 2026 Document Allegion (NYSE: ALLE) Reports Q3-2026 Financial Results Quarterly Financial Highlights • Revenues of $1,172.6 million, up 9.6% on a reported basis and up 7.2% on an organic basis • Operating margin of 24.3%, compared with 21.8% DUBLIN (Oct. 22, 2026) – Allegion plc (NYSE: ALLE), a leading global security products and solutions provider, today reported financial results for its third quarter (ended Sept. 30, 2026). Revenues rose.", "Allegion PLC", "ALLE"));

        // The name ending in "Inc." before its listing, which no full stop there cuts.
        Assert.Equal(
            "Allison Transmission Holdings Inc. (NYSE: ALSN), today reported second quarter net sales of $1,566 million with an adjusted EBITDA margin of 26 percent and net cash provided by operating activities of $312 million.",
            Line("INDIANAPOLIS, August 3, 2026 – Allison Transmission Holdings Inc. (NYSE: ALSN), today reported second quarter net sales of $1,566 million with an adjusted EBITDA margin of 26 percent and net cash provided by operating activities of $312 million. Net income rose.", "ALLISON TRANSMISSION HOLDINGS", "ALSN"));

        // A headline in the present tense after a day the exhibit is dated by: the past tense the release opens on.
        Assert.Equal(
            "Southern Company today reported second-quarter earnings of $1.2 billion, or $1.03 per share, in 2026 compared with earnings of $0.9 billion.",
            Line("EX-99.1 Document July 30, 2026 Southern Company reports second-quarter 2026 earnings ATLANTA – Southern Company today reported second-quarter earnings of $1.2 billion, or $1.03 per share, in 2026 compared with earnings of $0.9 billion. Adjusted earnings rose.", "Southern Company", "SO"));

        // A headline in capitals running into its city and its state with no day between.
        Assert.Equal(
            "National Fuel Gas Company (“National Fuel” or the “Company”) (NYSE:NFG) today announced consolidated results for the third quarter of its 2026 fiscal year.",
            Line("Silverstein Chief Financial Officer 716-857-6987 NATIONAL FUEL REPORTS THIRD QUARTER FISCAL 2026 EARNINGS WILLIAMSVILLE, N.Y. National Fuel Gas Company (“National Fuel” or the “Company”) (NYSE:NFG) today announced consolidated results for the third quarter of its 2026 fiscal year. Net income rose.", "NATIONAL FUEL GAS", "NFG"));

        // A present tense where no past tense says it, straight after its dateline's day.
        Assert.Equal(
            "L3Harris Technologies (NYSE: LHX) reports second quarter 2026 results.",
            Line("L3Harris Technologies Reports Robust Second Quarter 2026 Results MELBOURNE, Fla., July 29, 2026 \x2014 L3Harris Technologies (NYSE: LHX) reports second quarter 2026 results. Revenue rose.", "L3Harris Technologies Inc", "LHX"));

        // A name the release uses that the store no longer does, read by its listing and quoted from the name before it.
        Assert.Equal(
            "Pure Storage (NYSE: P), the most advanced data storage platform, today announced financial results for its second quarter fiscal year 2026 ended August 3, 2025.",
            Line("Pure Storage Announces Second Quarter Fiscal 2026 Financial Results SANTA CLARA, Calif., August 27, 2025 /PRNewswire/ -- Pure Storage (NYSE: P), the most advanced data storage platform, today announced financial results for its second quarter fiscal year 2026 ended August 3, 2025. Revenue rose.", "Everpure, Inc.", "P"));

        // A bullet straight after the sentence ends it.
        Assert.Equal(
            "Abbott today announced financial results for the third quarter ended Sept. 30, 2026.",
            Line("Abbott Reports Third-Quarter 2026 Results ABBOTT PARK, Ill., Oct. 21, 2026 \x2014 Abbott today announced financial results for the third quarter ended Sept. 30, 2026. • Third-quarter sales increased 14.5 percent on a reported basis.", "Abbott Laboratories", "ABT"));

        // None where the release says it nowhere: highlights alone after the dateline and a conference call's day; and a
        // sentence running on from a headline is none of the release's own.
        Assert.Null(Line("Visa Reports Fiscal Fourth Quarter and Full-Year 2026 Results San Francisco, October 27, 2026 – Visa (NYSE: V) Fiscal Fourth Quarter Results: • GAAP net income of $6.0B or $3.19 per share • Net revenue of $12.1B, an increase of 13%. Visa will host a conference call on Tuesday, October 27, 2026 at 5:00 p.m.", "Visa Inc. Class A", "V"));
        Assert.Null(Line("Mercury General Corporation Announces Second Quarter Results and Declares Quarterly Dividend Los Angeles, California Mercury General Corporation (NYSE: MCY) reported today for the second quarter of 2026: Consolidated Highlights", "Mercury General Corporation", "MCY"));
    }

    // The short version opens on the pick as code writes it from the card, then on the quarter, each a labelled paragraph,
    // and a stock no family picked opens on the quarter; ROP's quarter to September, its operating margin 28.7% against
    // 28.4% a year earlier.
    // see: The short version opens on the pick and then the quarter as code writes them, before what worked and what did not
    [Fact]
    public void TheShortVersionOpensOnThePickThenTheQuarterAndOnTheQuarterAloneForAStockNotPicked()
    {
        var quarter = new EquityBrief.Core.Report.LatestQuarter(
            new DateOnly(2026, 9, 30),
            new DateOnly(2026, 10, 22),
            new DateOnly(2025, 9, 30),
            [
                new EquityBrief.Core.Report.QuarterLine(EquityBrief.Core.Report.QuarterLines.Revenue, 2_171_800_000m, 2_167_000_000m, 4_800_000m, 0.2, 2_017_500_000m, 7.6, null, null, false, null, null, null),
                new EquityBrief.Core.Report.QuarterLine(EquityBrief.Core.Report.QuarterLines.OperatingIncome, 623_300_000m, null, null, null, 573_000_000m, 8.8, 28.7, 28.4, false, null, null, null),
            ]);
        var card = new DecisionCardView("GSPC", new DateOnly(2026, 10, 26), EquityBrief.Core.Families.SetupFamilies.Pullback, "ROP", "The pullback on the S&P 500", 441.65m, 420.10m, 478.74m, [], null, Cap: 63);

        static string[] Parts(string lead) => [.. System.Text.RegularExpressions.Regex.Matches(lead, "data-part=\"([a-z]+)\"").Select(match => match.Groups[1].Value)];

        var picked = MarkRenderer.ShortVersionLead(card, quarter);

        Assert.Equal(["pick", "quarter"], Parts(picked));
        Assert.Contains("buy at the next open, at about 441.65; sell at a close below 420.10; sell at 478.74", WebUtility.HtmlDecode(picked), StringComparison.Ordinal);
        Assert.Contains("an operating margin of 28.7% against 28.4% a year earlier", picked, StringComparison.Ordinal);
        Assert.Equal(["quarter"], Parts(MarkRenderer.ShortVersionLead(null, quarter)));
        Assert.Equal(string.Empty, MarkRenderer.ShortVersionLead(null, null));
    }

    // Where the stored year holds no band above the close, the pulled sessions' turns above it are read at the store's
    // scale: a pulled high of 240 that is the highest of the ten sessions either side of it, at a scale of two, is a turn at
    // 480, and with a typical day's move of 10 a band from 475 to 480, marked as resistance and older; a turn below the close
    // is read only where the year holds no band below it either.
    // see: Where the stored year holds no band on a side the name page draws bands read from the pulled history, marked older and read by nothing else
    [Fact]
    public void ASideTheStoredYearLeavesEmptyReadsTheTurnsOfThePulledHistoryMarkedOlder()
    {
        var first = new DateOnly(2024, 1, 2);
        var pulled = Enumerable.Range(0, 41)
            .Select(day => (first.AddDays(day), High: day == 20 ? 240m : 200m + (day % 3), Low: day == 30 ? 150m : 190m))
            .ToArray();

        var above = EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: true, below: false);

        Assert.Equal([new EquityBrief.Core.Report.OlderBand("resistance", 475m, 480m, first.AddDays(20))], above);
        Assert.Empty(EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: false, below: false));

        var both = EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: true, below: true);

        Assert.Contains(both, band => band is { Role: "support", Low: 300m, High: 305m });
        Assert.Contains("<table class=\"older-bands\">", MarkRenderer.OlderBandsTable(both, 441.65m), StringComparison.Ordinal);
        Assert.Contains("no rule, plan or listing reads them", MarkRenderer.OlderBandsTable(both, 441.65m), StringComparison.Ordinal);
        Assert.Equal(string.Empty, MarkRenderer.OlderBandsTable([], 441.65m));
    }

    // Every fixture name's page draws each concept it marks with one value, and its masthead, its numbers and its valuation
    // carry the marks the check reads.
    [Fact]
    public async Task EveryFixtureNamesPageDrawsEachConceptWithOneValue()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var marked = 0;

        foreach (var name in FiredNamesOn(store, NightIn(store)))
        {
            var page = await client.GetStringAsync($"/screens/name/{name}");

            Assert.Empty(EquityBrief.Core.Report.ConceptCheck.Clashes(page));
            Assert.DoesNotContain("concept-clash", page, StringComparison.Ordinal);
            marked += EquityBrief.Core.Report.ConceptCheck.Drawn(page).Count;
        }

        Assert.True(marked > 0, "No fixture page marks any concept, so the check read nothing.");
    }
}
