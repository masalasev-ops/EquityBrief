using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// The report page's gaps the operator named after ROP's and KEY's pages: each job's profile, the cost figures with their
// scope, one scale a segment table, the note above a commentary written from the filing, the pages waiting on a quarter,
// a held trade's plan against its fill, the next report against the hold, the short version's sentences by priority,
// every section's sources held to the company, and the peer check read off the company's own segments.
public partial class ReadSurface
{
    static readonly JobProfile[] SettingsProfiles =
    [
        new("deepseek", "deepseek-flash", false),
        new("claude-sonnet", "claude-sonnet-5-5", false),
        new("gemma-4", "google/gemma-4-26b-a4b-qat", true),
        new("qwen-3.5", "qwen/qwen3.5-9b", true),
    ];

    // ROP's quarter to 2026-09-30 as its results release states its segments.
    static SegmentReading RopReleaseSegments() =>
        new(
            "release",
            new DateOnly(2026, 9, 30),
            3,
            2_171_800_000m,
            2_171_800_000m,
            [
                new("Application Software", 1_239_700_000m, null, 6.8, 357_600_000m, null, 10.7, 57.1),
                new("Network Software", 446_100_000m, null, 7.9, 189_600_000m, null, 6.5, 20.5),
                new("Technology Enabled Products", 486_000_000m, null, 9.7, 160_700_000m, null, 7.0, 22.4),
            ],
            null);

    // A label's own profile names its lane: a local model's labels are the local model's whatever the run log once priced
    // under the same stage, the release reading is the local run's, and a section is labelled by the profile the settings
    // name for its model. The proof holds each row's lane to its profile's.
    // see: Every report names the profile each of its jobs ran under and whether it is paid or local, read off the job's own row
    [Fact]
    public void EachJobIsLabelledByTheProfileItRanUnderAndTheProofHoldsItsLaneToThatProfile()
    {
        var everyModelPriced = new HashSet<string>(StringComparer.Ordinal) { "deepseek-flash", "google/gemma-4-26b-a4b-qat" };
        var news = new NewsView("ROP", new DateOnly(2026, 10, 27), [], 16, 6, 2, 8, 0, 0, "google/gemma-4-26b-a4b-qat", "gemma-4", null, null, Local: true);
        var release = new ReleaseReadingView(new DateOnly(2026, 10, 22), new EquityBrief.Core.Releases.ReleaseAnswer("better", "raised", "confident", "not stated"), 5, "google/gemma-4-26b-a4b-qat");
        var written = new WrittenCell("The two cases", "A sentence.", new DateOnly(2026, 10, 27), "deepseek-flash", []);
        var jobs = MarkRenderer.ModelJobs([written], news, release, SettingsProfiles, everyModelPriced);

        Assert.Equal(
            [("News labels", false, "gemma-4"), ("The newest results release's reading", false, "gemma-4"), ("The two cases", true, "deepseek")],
            jobs.Select(job => (job.Job, job.Paid, job.Profile)));

        var table = MarkRenderer.ModelJobsTable(jobs);

        Assert.Contains("<td>the local model, profile gemma-4</td><td>google/gemma-4-26b-a4b-qat</td>", table, StringComparison.Ordinal);
        Assert.Contains("<td>the paid model, profile deepseek</td><td>deepseek-flash</td>", table, StringComparison.Ordinal);

        var context = new ProofContext(SettingsProfiles, null);

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", table, context), "model-table"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", table.Replace("data-job=\"News labels\" data-lane=\"local\"", "data-job=\"News labels\" data-lane=\"paid\"", StringComparison.Ordinal), context), "model-table"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", table.Replace("data-profile=\"deepseek\"", "data-profile=\"none\"", StringComparison.Ordinal), context), "model-table"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", table), "model-table"));
    }

    // The page states the full report passes of the seven nights the run page's table counts apart from the passes writing
    // part of a report, beside the most any research pass has cost with its stock, day and profile; the labeller's runs are
    // no research pass. The proof holds the seven nights to the run page's own table.
    // see: The report page states its research passes' cost over the seven nights the run page counts and the most any pass cost, each with its scope
    [Fact]
    public void TheCostLineStatesTheSevenNightsFullPassesApartAndTheMostAnyPassCostEachWithItsScope()
    {
        static RunStageRow Row(string run, string stage, string spend, string detail) =>
            new(run, stage, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "ok", 0, 1, 1, spend, detail);

        const string Full = "[\"What the company sells\",\"The two cases\",\"The short version\"]";
        RunStageRow[] rows =
        [
            Row("research-mdt", "research", "0", "{\"ticker\":\"MDT\",\"asOf\":\"2026-09-29\",\"warranted\":" + Full + "}"),
            Row("research-mdt", "research call: The two cases", "0.5", "{\"model\":\"claude-sonnet-5-5\"}"),
            Row("research-mdt", "research call: The short version", "0.256394", "{\"model\":\"claude-sonnet-5-5\"}"),
            Row("research-bx", "research", "0", "{\"ticker\":\"BX\",\"asOf\":\"2026-10-24\",\"warranted\":" + Full + "}"),
            Row("research-bx", "research call: The two cases", "0.0861", "{\"model\":\"deepseek-flash\"}"),
            Row("research-bx", "research call: The two cases, trial", "0.9", "{\"model\":\"claude-sonnet-5-5\"}"),
            Row("research-ozk", "research", "0", "{\"ticker\":\"OZK\",\"asOf\":\"2026-10-27\",\"warranted\":" + Full + "}"),
            Row("research-ozk", "research call: The short version", "0.0268", "{\"model\":\"deepseek-flash\"}"),
            Row("research-hpq", "research", "0", "{\"ticker\":\"HPQ\",\"asOf\":\"2026-10-22\",\"warranted\":[\"The industry cycle\"]}"),
            Row("research-hpq", "research call: The industry cycle", "0.0026", "{\"model\":\"deepseek-flash\"}"),
            Row("label-new-1", "research call: news label, UPS 1", "0.0004", "{\"model\":\"deepseek-flash\"}"),
        ];

        var passes = RunScreen.PassCosts(rows, SettingsProfiles);

        Assert.Equal(["research-bx", "research-hpq", "research-mdt", "research-ozk"], passes.Select(pass => pass.RunId).Order(StringComparer.Ordinal));
        Assert.Equal(0.0861m, passes.Single(pass => pass.Ticker == "BX").Cost);
        Assert.False(passes.Single(pass => pass.Ticker == "HPQ").Full);

        var cost = NameScreen.Cost(passes, new EquityBrief.Core.Spending.SpendVerdict(false, null, 0.38m, 6.13m, new EquityBrief.Core.Spending.SpendCaps(10m, 50m), null), new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 27));
        var line = MarkRenderer.ShortCostLine(cost);

        Assert.Contains("Over the seven nights 19 Oct to 27 Oct 2026, the run page's table, 2 full report passes, each writing the whole report through its short version, cost $0.0268 to $0.0861 each on the deepseek profile; one pass writing part of a report, one section or the stale ones, cost $0.0026.", line, StringComparison.Ordinal);
        Assert.Contains("The most any research pass has cost is $0.7564, MDT's full report of 29 Sep 2026 on the claude-sonnet profile, the highest of the 4 passes since the first.", line, StringComparison.Ordinal);

        var standing = new MarkRenderer().ReportStanding("ROP", new ReportStandingView("stands", new DateOnly(2026, 10, 27), [], [new ResearchControl(NameScreen.RefreshKind, "Regenerate Report", Refresh: true, PaidForLocal: false)], cost), namesTheDay: true);
        var agreeing = new ProofContext(SettingsProfiles, (new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 27), 3, 0.0026m, 0.0861m));

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", standing, agreeing), "cost-figures"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", standing, agreeing with { RunTable = (new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 27), 4, 0.0026m, 0.0861m) }), "cost-figures"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", "<p>no cost</p>", agreeing), "cost-figures"));
    }

    // A segment table draws every amount in the one scale its smallest reads in, so ROP's release reads $1,239.7M beside
    // $446.1M and not $1.2B; and a commentary written from the filing's segments says so above its text.
    // see: A segment table draws every amount in one scale, the one its smallest amount reads in
    [Fact]
    public void ASegmentTableDrawsEveryAmountInOneScaleAndTheProofReadsItBack()
    {
        var table = new MarkRenderer().SegmentsTable(RopReleaseSegments(), "USD");

        foreach (var amount in new[] { "$1,239.7M", "$446.1M", "$486.0M", "$357.6M", "$2,171.8M" })
        {
            Assert.Contains(">" + amount + "</td>", table, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("$1.2B", table, StringComparison.Ordinal);
        Assert.Equal(("B"), Figures.ScaleOf([2_000_000_000m, 3_500_000_000m])?.Suffix);
        Assert.Null(Figures.ScaleOf([400_000m, 2_000_000m]));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", table), "segments-scale"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", table.Replace(">$1,239.7M</td>", ">$1.2B</td>", StringComparison.Ordinal)), "segments-scale"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", "<p>none</p>"), "segments-scale"));
    }

    // A page drawing an older quarter than its newest release reports names the quarter it awaits and the first of the night
    // the quarters fetch next asks the provider for it and the day its filing is expected, the company's own lag a year
    // before and never past the day it is due.
    // see: A page drawing an older quarter than its newest results release reports names the quarter it awaits and the first day it is expected to read it
    [Fact]
    public void APageWaitingOnItsNewestQuarterNamesItAndTheFirstDayItIsExpectedToDrawIt()
    {
        FirstFiled[] lastYear = [new(new DateOnly(2025, 9, 30), new DateOnly(2025, 10, 30), "10-Q"), new(new DateOnly(2025, 12, 31), new DateOnly(2026, 2, 12), "10-K")];

        // Reported on the night itself and not asked yet: the fetch asks the next night, before the filing is expected.
        var reported = QuarterWaits.Of(new DateOnly(2026, 10, 27), new DateOnly(2026, 6, 30), new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 27), [], lastYear);

        Assert.Equal((new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 28), new DateOnly(2026, 10, 30), new DateOnly(2026, 11, 9), new DateOnly(2026, 10, 28)), (reported.Quarter, reported.NextAsk!.Value, reported.FilingExpected!.Value, reported.FilingDue, reported.Clears));

        // A release filed ahead of the calendar's report date: the fetch first asks the night after that date, and the filing
        // is expected no earlier than the report.
        var ahead = QuarterWaits.Of(new DateOnly(2026, 10, 15), new DateOnly(2026, 6, 30), new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 3), [], lastYear);

        Assert.Equal((new DateOnly(2026, 11, 4), new DateOnly(2026, 11, 3), new DateOnly(2026, 11, 3)), (ahead.NextAsk!.Value, ahead.FilingExpected!.Value, ahead.Clears));
        Assert.Contains("after this release", ahead.Ask, StringComparison.Ordinal);

        // Asked on fewer nights than the retries: asked again the next weekday; past them, weekly on the night the row names.
        var retrying = QuarterWaits.Of(new DateOnly(2026, 10, 21), new DateOnly(2026, 6, 30), new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 21), [new(new DateOnly(2026, 10, 22), "not yet posted", 1, null), new(new DateOnly(2026, 10, 23), "not yet posted", 2, null)], lastYear);
        var weekly = QuarterWaits.Of(new DateOnly(2026, 10, 1), new DateOnly(2026, 6, 30), new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 1), [new(new DateOnly(2026, 10, 9), "not yet posted", 6, new DateOnly(2026, 10, 16)), new(new DateOnly(2026, 10, 16), "not yet posted", 7, new DateOnly(2026, 10, 23)), new(new DateOnly(2026, 10, 23), "not yet posted", 8, new DateOnly(2026, 10, 30))], []);

        Assert.Equal(new DateOnly(2026, 10, 26), retrying.NextAsk);
        Assert.Equal(new DateOnly(2026, 10, 30), weekly.NextAsk);
        Assert.Null(weekly.FilingExpected);
        Assert.Equal(new DateOnly(2026, 10, 30), weekly.Clears);

        // The proof reads the line off a page whose card draws the filing's older quarter beside a newer release.
        var line = MarkRenderer.QuarterWaitLine(reported);
        const string Card = "<section class=\"card spined fund\" id=\"quarter\"><div class=\"spine\"><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>2026-08-05</b></div></div><h2>The quarter to 2026-06-30, line by line</h2>";
        const string Release = "<p class=\"guidance release-opening\" data-accepted=\"2026-10-27\">A line.</p>";
        var page = ReportProof.Evaluate("BA", Card + line + Release);

        Assert.Equal(ReportProof.Passed, VerdictOf(page, "quarter-wait"));
        Assert.Equal(ReportProof.Failed, VerdictOf(page, "newest-quarter"));
        Assert.EndsWith("; expected to clear by 2026-10-28", page.Verdicts.Single(verdict => verdict.Rule == "newest-quarter").Why, StringComparison.Ordinal);
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("BA", Card + Release), "quarter-wait"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("BA", Card), "quarter-wait"));

        var html = ReportProof.Html(new ProofReport(DateTimeOffset.UnixEpoch, "GSPC", null, ReportProof.Counted([page]), [page]));

        Assert.Contains("<p data-waiting=\"1\">", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-wait=\"BA\"><td>BA</td><td>2026-10-27</td><td>2026-09-30</td><td>2026-10-28</td><td>2026-10-30</td><td>2026-11-09</td><td>2026-10-28</td></tr>", html, StringComparison.Ordinal);
    }

    // A held trade's card states the reward to risk its plan was struck at the close beside the one at its fill: KEY's drift
    // trade, struck at 1.44 on the close of 20.81 and filled at 21.00, 0.96 at its fill, under 1; the report counts it.
    // see: A held trade's card states the reward to risk its plan was struck at beside the one at its fill
    [Fact]
    public void AHeldTradesCardStatesThePlannedRewardToRiskBesideTheFillsAndTheReportCountsFillsUnderOne()
    {
        var pick = new PickCell("KEY", "KeyCorp", new DateOnly(2026, 10, 20), "drift", 21.00m, 20.04m, 21.92m, PickStatus.Open, 5, null, null, null, null, null, null, null,
            Family: EquityBrief.Core.Families.DriftRule.Name, Setup: "Earnings drift", FilledOn: new DateOnly(2026, 10, 21), Cap: 60);
        var row = new DecisionCardRow("GSPC", new DateOnly(2026, 10, 20), EquityBrief.Core.Families.DriftRule.Name, "KEY", 1, 20.81m, 20.04m, 21.92m, "the drift rule", "{}", "[]", null, Cap: 60,
            Hits: "{\"through\":\"2027-01-15\",\"reactions\":{\"count\":0,\"pastTheStop\":0},\"events\":[],\"pastTheTable\":[]}");
        var held = NameScreen.HeldOf(null, [pick], [row], [])!;

        Assert.Equal("1.44", decimal.Round(held.PlannedRewardToRisk!.Value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));
        Assert.Equal("0.96", decimal.Round(held.RewardToRisk!.Value, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));

        var card = new MarkRenderer().HeldCard(held);

        Assert.Contains("<dt>Reward to risk planned at the close</dt><dd>1.44, struck at the close of 20.81</dd>", card, StringComparison.Ordinal);
        Assert.Contains("<dt>Reward to risk at the fill</dt><dd>0.96, under 1: the open left less reward than risk</dd>", card, StringComparison.Ordinal);

        var keyPage = ReportProof.Evaluate("KEY", card);
        var ropPage = ReportProof.Evaluate("ROP", new MarkRenderer().HeldCard(NameScreen.HeldOf(null, [RopHeldPick()], [RopHeldCardRow()], [])!));

        Assert.Equal(ReportProof.Passed, VerdictOf(keyPage, "fill-against-plan"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("KEY", card.Replace("<dt>Reward to risk planned at the close</dt>", "<dt>Planned</dt>", StringComparison.Ordinal)), "fill-against-plan"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("KEY", "<p>none</p>"), "fill-against-plan"));

        var html = ReportProof.Html(new ProofReport(DateTimeOffset.UnixEpoch, "GSPC", null, ReportProof.Counted([keyPage, ropPage]), [keyPage, ropPage]));

        Assert.Contains("<p data-held=\"2\" data-under-one=\"1\">2 held trades, 1 of them filled at a reward to risk under 1.</p>", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-fill=\"KEY\" data-under-one=\"yes\"><td>KEY</td><td>drift</td><td>2026-10-20</td><td class=\"n\">1.44</td><td class=\"n fail\">0.96</td></tr>", html, StringComparison.Ordinal);
    }

    // A next report the calendar does not date is listed in it estimated from the company's own a year on, even past the
    // ninety days the timeline draws, and both the calendar and the plan's block around the next report place it against the
    // hold: ROP's estimated 26 Jan 2027 falls after its drift trade's last day of 22 Jan; KEY's calendar dates 20 Jan, its own
    // estimate 19 Jan beside it, after its last day of 15 Jan.
    // see: The calendar and the plan's block around the next report list it declared or estimated and place it against the hold
    [Fact]
    public void TheNextReportDeclaredOrEstimatedIsListedInTheCalendarAndPlacedAgainstTheHold()
    {
        var night = new DateOnly(2026, 10, 27);
        var (items, past) = EquityBrief.Core.Cards.PageCalendar.Of(night, [], null, [], (new DateOnly(2026, 1, 27), new DateOnly(2027, 1, 26)));
        var rop = new CalendarView(night, night.AddDays(EquityBrief.Core.Cards.PageCalendar.Days), items, past) { HoldThrough = new DateOnly(2027, 1, 22), Estimate = (new DateOnly(2026, 1, 27), new DateOnly(2027, 1, 26)) };
        var calendar = WebUtility.HtmlDecode(new MarkRenderer().TheCalendar("ROP", rop, null, []));
        var around = WebUtility.HtmlDecode(MarkRenderer.NextReportAroundThePlan(rop));

        Assert.Contains("<tr data-date=\"2027-01-26\" data-kind=\"results\" data-declared=\"no\" data-inside-hold=\"no\" data-hold-through=\"2027-01-22\"><td>2027-01-26 <small class=\"estimated\">estimated</small></td><td>Results, estimated from its report of 27 Jan 2026 a year on, after the hold's last day of about 22 Jan 2027, after the 90 days the timeline draws</td><td class=\"num\">91</td></tr>", calendar, StringComparison.Ordinal);
        Assert.Contains("The next report is not announced: it is estimated about 26 Jan 2027, from its report of 27 Jan 2026 a year on, after the hold's last day of about 22 Jan 2027, so it falls outside the hold.", around, StringComparison.Ordinal);
        Assert.DoesNotContain("data-date=\"2027-01-26\" data-kind=\"results\" data-declared=\"no\"><title>", new MarkRenderer().CalendarTimeline("ROP", rop), StringComparison.Ordinal);

        var keyNight = new DateOnly(2026, 10, 27);
        var (keyItems, keyPast) = EquityBrief.Core.Cards.PageCalendar.Of(keyNight, [(new DateOnly(2027, 1, 20), "earnings", "before")], null, [], (new DateOnly(2026, 1, 20), new DateOnly(2027, 1, 19)));
        var key = new CalendarView(keyNight, keyNight.AddDays(EquityBrief.Core.Cards.PageCalendar.Days), keyItems, keyPast) { HoldThrough = new DateOnly(2027, 1, 15), Estimate = (new DateOnly(2026, 1, 20), new DateOnly(2027, 1, 19)) };

        Assert.Contains(
            "The next report is on 20 Jan 2027, as the calendar dates it; the company's own report of 20 Jan 2026 a year on estimates about 19 Jan 2027, after the hold's last day of about 15 Jan 2027, so it falls outside the hold.",
            WebUtility.HtmlDecode(MarkRenderer.NextReportAroundThePlan(key)),
            StringComparison.Ordinal);

        // Inside the hold where it falls on or before the last day.
        Assert.Contains("inside the hold, which ends about 30 Jan 2027", WebUtility.HtmlDecode(MarkRenderer.NextReportAroundThePlan(rop with { HoldThrough = new DateOnly(2027, 1, 30) })), StringComparison.Ordinal);

        const string Held = "<section class=\"held-card\" data-family=\"drift\">";
        const string Plan = "<div class=\"sub\">Around the next report</div>";

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", Held + calendar + Plan + around), "next-report-hold"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", Held + calendar + Plan), "next-report-hold"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", Held + WebUtility.HtmlDecode(new MarkRenderer().TheCalendar("ROP", rop with { HoldThrough = null }, null, [])) + Plan + around), "next-report-hold"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", calendar), "next-report-hold"));
    }

    // Each paragraph of what worked and what did not keeps its first sentence on demand, on cost, naming a segment and on
    // the main risk, drawn in the paragraph's own order, where its first four sentences would have kept none of them.
    // see: What worked and what did not keeps four sentences a paragraph chosen by demand then cost then segments then the main risk
    [Fact]
    public void WhatWorkedKeepsASentenceOnEachPriorityRatherThanItsFirstFour()
    {
        string[] sentences =
        [
            "The quarter closed a busy year.",
            "Management thanked its staff.",
            "The board met twice.",
            "Margins widened as costs fell.",
            "Revenue rose on stronger orders.",
            "Application Software led the growth.",
            "The main risk is a slower software market.",
        ];

        var chosen = SentencePriority.Choose(sentences, MarkRenderer.WhatWorkedSentences, ["Application Software", "Network Software"]);

        Assert.Equal([sentences[3], sentences[4], sentences[5], sentences[6]], chosen);
        Assert.Equal(sentences[..2], SentencePriority.Choose(sentences[..2], 4));

        var section = new MarkRenderer().WrittenSection(
            "ROP",
            new WrittenCell(MarkRenderer.TheShortVersion, string.Join(" ", sentences), new DateOnly(2026, 10, 27), "deepseek-flash", []),
            [],
            proseLabel: "What worked and what did not.",
            sentencesAParagraph: MarkRenderer.WhatWorkedSentences,
            segmentNames: ["Application Software", "Network Software"]);
        var page = new MarkRenderer().SegmentsTable(RopReleaseSegments(), "USD") + section;

        Assert.Contains("data-priorities=\"demand cost segments risk\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Management thanked its staff.", section, StringComparison.Ordinal);
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", page), "short-priority"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", page.Replace("Margins widened as costs fell.", "The year closed.", StringComparison.Ordinal)), "short-priority"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", "<p>none</p>"), "short-priority"));
    }

    // Every written section's own list holds its documents to the company and its industry over their whole text, as the
    // page's list does: ROP's risks and two cases cite Roche's (SWX:ROP) story, folded apart in each, where two market
    // roundups naming Roper only past their opening stand. The proof fails a page keeping a document in one list it folds
    // apart in another.
    // see: Every written section's sources and the page's are held to documents naming the company or its industry in their whole text
    [Fact]
    public void EachWrittenSectionsOwnSourcesListIsHeldToTheCompanyAndItsIndustryOverTheWholeText()
    {
        var release = new SourceCell("r1", "Results release, a2026q3earningsrelease.htm", "https://www.sec.gov/Archives/edgar/data/882835/a2026q3earningsrelease.htm", new DateOnly(2026, 10, 22), "Roper Technologies, Inc. (Nasdaq: ROP) reported results.");
        var roundup = new SourceCell("n1", "Top Analyst Reports for Intel, Novartis & Itau Unibanco", "https://finance.yahoo.com/a", new DateOnly(2026, 10, 24), new string('x', 2000) + " Roper Technologies shares have outperformed.");
        var roche = new SourceCell("n2", "Can Roche Holding (SWX:ROP) Still Be Cheap After Fresh Trial Wins?", "https://finance.yahoo.com/b", new DateOnly(2026, 10, 25), "Roche Holding's trials met their endpoints.");
        static bool Relevant(SourceCell document) => IndustryCheck.Relevant(document.Title, document.Url + " " + document.Text, "Roper Technologies Inc", "Software");

        var risks = new MarkRenderer().WrittenSection("ROP", new WrittenCell(MarkRenderer.TheRisks, "A risk.", new DateOnly(2026, 10, 27), "deepseek-flash", ["r1", "n1", "n2"]), [release, roundup, roche], relevant: Relevant);

        Assert.Contains("<ul class=\"section-sources\" data-cites=\"3\" data-relevance=\"tested\" data-left-out=\"1\">", risks, StringComparison.Ordinal);
        Assert.Contains("<li data-marker=\"D2\" data-document=\"n1\">", risks, StringComparison.Ordinal);
        Assert.Contains("<li data-marker=\"D3\" data-document=\"n2\" data-relevant=\"no\">", risks, StringComparison.Ordinal);

        var sources = new MarkRenderer().Sources("ROP", [release, roundup, roche], [], false, Relevant);

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", risks + sources), "sources-test"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", risks + sources.Replace("data-document=\"n2\" data-published-on=\"2026-10-25\" data-url=\"https://finance.yahoo.com/b\" data-relevant=\"no\"", "data-document=\"n2\" data-published-on=\"2026-10-25\" data-url=\"https://finance.yahoo.com/b\"", StringComparison.Ordinal)), "sources-test"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", new MarkRenderer().WrittenSection("ROP", new WrittenCell(MarkRenderer.TheRisks, "A risk.", new DateOnly(2026, 10, 27), "deepseek-flash", ["r1"]), [release]) + sources), "sources-test"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", sources), "sources-test"));
    }

    // The peer check reads the company's own segments and revenue lines: ROP's software segments, 78 per cent of its quarter,
    // are flagged against Electronic Equipment and stand under the override to Software; the 18 pages the description's
    // words flagged are flagged no longer, their segments and revenue lines putting no business the list names past half
    // their revenue in a group naming none of it, a health insurer's premiums standing among the health care providers.
    // see: A stock's industry group is checked against the company's own segments and revenue lines and not its description
    [Fact]
    public void ThePeerCheckReadsTheCompanysOwnSegmentsAndFlagsRopAndNoneOfTheEighteen()
    {
        BusinessLine[] rop = [new("Application Software", 1_239_700_000m), new("Network Software", 446_100_000m), new("Technology Enabled Products", 486_000_000m)];

        Assert.Equal(("software", 0.776), IndustryCheck.Mismatch("Electronic Equipment, Instruments & Components", rop) is { } flagged ? (flagged.Business, Math.Round(flagged.Share, 3)) : default);
        Assert.Null(IndustryCheck.Mismatch("Software", rop));

        var mustNotFlag = new (string Ticker, string Group, BusinessLine[] Lines)[]
        {
            ("ADM", "Food Products", [new("Ag Services and Oilseeds", 17_916_000_000m), new("Carbohydrate Solutions", 2_757_000_000m), new("Nutrition", 1_902_000_000m), new("Other Business", 106_000_000m)]),
            ("BDX", "Health Care Equipment & Supplies", [new("Operating Segments | Medical Essentials Revenues", 1_675_000_000m), new("Operating Segments | Connected Care Revenues", 1_224_000_000m), new("Operating Segments | BioPharma Systems Revenues", 670_000_000m), new("Operating Segments | Interventional Revenues", 1_414_000_000m), new("United States Revenues", 3_081_000_000m), new("International Revenues", 1_902_000_000m), new("Medication Delivery Solutions | Operating Segments | Medical Essentials Revenues", 1_164_000_000m), new("Specimen Management | Operating Segments | Medical Essentials Revenues", 511_000_000m)]),
            ("BX", "Capital Markets", []),
            ("DGX", "Health Care Providers & Services", [new("DIS business", 2_978_000_000m), new("All other operating segments", 65_000_000m)]),
            ("DHR", "Health Care Equipment & Supplies", []),
            ("ECL", "Chemicals", []),
            ("ETN", "Electrical Equipment", [new("Electrical Americas", 3_951_000_000m), new("Electrical Global", 2_517_000_000m), new("Aerospace", 1_222_000_000m), new("Mobility", 841_000_000m)]),
            ("HPQ", "Technology Hardware, Storage & Peripherals", [new("Total segment net revenue Products net revenue", 15_679_000_000m), new("Personal Systems | Total segment net revenue Products net revenue", 11_767_000_000m), new("Printing | Total segment net revenue Products net revenue", 3_912_000_000m), new("Commercial PS | Personal Systems | Total segment net revenue Products net revenue", 8_579_000_000m), new("Supplies | Printing | Total segment net revenue Products net revenue", 2_536_000_000m)]),
            ("HUM", "Health Care Providers & Services", [new("External Revenues Total revenues", 40_614_000_000m), new("Product Premiums revenue", 38_834_000_000m), new("Service Services revenue", 1_780_000_000m), new("Operating Segments | Insurance Total revenues", 39_140_000_000m), new("Operating Segments | CenterWell Total revenues", 6_790_000_000m), new("Operating Segments | Product | Insurance Premiums revenue", 38_834_000_000m), new("Intersegment revenues Total revenues", -5_211_000_000m)]),
            ("JBL", "Electronic Equipment, Instruments & Components", [new("Operating Segments Net revenue", 8_751_000_000m), new("Regulated Industries | Operating Segments Net revenue", 3_181_000_000m), new("Intelligent Infrastructure | Operating Segments Net revenue", 4_169_000_000m), new("Connected Living and Digital Commerce | Operating Segments Net revenue", 1_401_000_000m)]),
            ("JNJ", "Pharmaceuticals", []),
            ("LUV", "Passenger Airlines", []),
            ("MAR", "Hotels, Restaurants & Leisure", [new("Net fee revenues", 1_547_000_000m), new("Owned, leased, and other", 466_000_000m), new("Reimbursements", 5_058_000_000m)]),
            ("MDT", "Health Care Equipment & Supplies", [new("Operating Segments Total net sales", 8_884_000_000m), new("Cardiovascular | Operating Segments Total net sales", 3_927_000_000m), new("Cardiovascular | Electrophysiology Therapies | Operating Segments Total net sales", 2_218_000_000m), new("Neuroscience | Operating Segments Total net sales", 2_678_000_000m), new("Medical Surgical | Surgical & Endoscopy | Operating Segments Total net sales", 1_740_000_000m), new("Diabetes | Operating Segments Total net sales", 843_000_000m)]),
            ("MMM", "Industrial Conglomerates", []),
            ("NTAP", "Technology Hardware, Storage & Peripherals", [new("U.S. Net revenues", 930_000_000m)]),
            ("PEP", "Beverages", [new("PepsiCo Foods North America", 6_504_000_000m), new("PepsiCo Beverages North America", 7_706_000_000m), new("International Beverage Franchise", 1_401_000_000m), new("Europe, Middle East & Africa (Segment)", 5_413_000_000m), new("Latin America Foods (Segment)", 3_021_000_000m), new("Asia Pacific Foods (Segment)", 1_229_000_000m)]),
            ("URI", "Trading Companies & Distributors", [new("Equipment rentals", 4_316_000_000m), new("Sales of rental equipment", 348_000_000m), new("Sales of new equipment", 84_000_000m), new("Contractor supplies sales", 47_000_000m), new("Service and other revenues", 98_000_000m)]),
        };

        Assert.Equal(18, mustNotFlag.Length);
        Assert.All(mustNotFlag, page => Assert.True(IndustryCheck.Mismatch(page.Group, page.Lines) is null, page.Ticker + " is flagged"));

        // HUM's premiums carry most of its revenue, which reads as insurance: named among the health care providers.
        Assert.Equal("insurance", IndustryCheck.Business(mustNotFlag.Single(page => page.Ticker == "HUM").Lines)?.Business);

        // The proof reads the lines off the drawn table, the release's own clean one for ROP.
        const string Peers = "<div class=\"sub\">Against Electronic Equipment, Instruments &amp; Components's S&amp;P 500 members on the night, the provider's classification</div>";
        var table = new MarkRenderer().SegmentsTable(RopReleaseSegments(), "USD");
        var flag = MarkRenderer.IndustryMismatch("Electronic Equipment, Instruments & Components", rop);

        Assert.Contains("put 78% of the quarter's revenue in software", flag, StringComparison.Ordinal);
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", Peers + flag + table), "industry-mismatch"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", Peers + table), "industry-mismatch"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", Peers), "industry-mismatch"));
    }
}
