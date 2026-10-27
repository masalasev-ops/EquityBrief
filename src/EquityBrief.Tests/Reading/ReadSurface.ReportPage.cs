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
