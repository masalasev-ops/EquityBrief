using System.Net;
using EquityBrief.Api.Reading;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// The report page rules the corrections after ROP's page added: a stock held on an earlier pick's open trade, what worked
// and what did not held to four sentences a paragraph, older bands read out to a pick's levels, a next report estimated
// where none is on file, the sources list held to the documents about the company, and the proof reading each back.
public partial class ReadSurface
{
    static string VerdictOf(ProofPage page, string rule) => page.Verdicts.Single(verdict => verdict.Rule == rule).Verdict;

    // ROP picked by the earnings drift on 2026-10-26, filled at the open of 2026-10-27 at 434.12, its stop a close below
    // 406.46 and its target 529.64, its limit 60 sessions to about 22 Jan 2027: its reward against its risk at the fill is
    // (529.64 - 434.12) / (434.12 - 406.46), 95.52 over 27.66, 3.45.
    static PickCell RopHeldPick(string status = PickStatus.Open) =>
        new("ROP", "Roper Technologies Inc", new DateOnly(2026, 10, 26), "drift", 434.12m, 406.46m, 529.64m, status, 1, null, null, null, null, null, null, null,
            Family: EquityBrief.Core.Families.DriftRule.Name, Setup: "Earnings drift", FilledOn: new DateOnly(2026, 10, 27), Cap: 60);

    static DecisionCardRow RopHeldCardRow() =>
        new("GSPC", new DateOnly(2026, 10, 26), EquityBrief.Core.Families.DriftRule.Name, "ROP", 1, 441.65m, 406.46m, 529.64m, "the drift rule", "{}", "[]", null, Cap: 60,
            Hits: "{\"through\":\"2027-01-22\",\"reactions\":{\"count\":0,\"pastTheStop\":0},\"events\":[],\"pastTheTable\":[]}");

    // A stock a family holds an open trade in, picked on an earlier night and listed under none tonight, is drawn with that
    // trade first: the short version opens on it, a short card draws each of its lines, the plan opens on its own exits and
    // the proof reads all of it back with the chart's stop and target. A stock tonight's list names, a trade that has ended
    // and a night that stored no card for the family hold none.
    // see: A stock held on a family's open trade is drawn with that trade first, its short card and its own exits on the plan and the chart
    [Fact]
    public void AStockHeldOnAFamilysOpenTradeOpensOnThatTradeWithItsShortCardAndItsOwnExits()
    {
        var held = NameScreen.HeldOf(null, [RopHeldPick()], [RopHeldCardRow()], [])!;

        Assert.Equal(3.45m, decimal.Round(held.RewardToRisk!.Value, 2));
        Assert.Null(NameScreen.HeldOf(CardScreen.View(RopHeldCardRow()), [RopHeldPick()], [RopHeldCardRow()], []));
        Assert.Null(NameScreen.HeldOf(null, [RopHeldPick(PickStatus.Target)], [RopHeldCardRow()], []));
        Assert.Null(NameScreen.HeldOf(null, [RopHeldPick()], [RopHeldCardRow() with { Night = new DateOnly(2026, 10, 23) }], []));

        // The short version opens on the held trade, then on the quarter.
        var quarter = new EquityBrief.Core.Report.LatestQuarter(new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 22), new DateOnly(2025, 9, 30),
            [new EquityBrief.Core.Report.QuarterLine(EquityBrief.Core.Report.QuarterLines.Revenue, 2_171_800_000m, 2_167_000_000m, 4_800_000m, 0.2, 2_017_500_000m, 7.6, null, null, false, null, null, null)]);
        var lead = WebUtility.HtmlDecode(MarkRenderer.ShortVersionLead(null, quarter, held));

        Assert.Equal(["held", "quarter"], System.Text.RegularExpressions.Regex.Matches(lead, "data-part=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains(
            "The earnings drift picked it on 26 Oct 2026 and holds it: bought at 434.12 at the open of 27 Oct 2026; sold at 529.64, at a close below 406.46 or after 60 trading days, about 22 Jan 2027, whichever comes first; open after 1 session; its reward to risk at the fill 3.45.",
            lead,
            StringComparison.Ordinal);

        // Its short card draws each line, and the plan opens on the trade's own exits.
        var marks = new MarkRenderer();
        var card = WebUtility.HtmlDecode(System.Text.RegularExpressions.Regex.Replace(marks.HeldCard(held), "<[^>]+>", "|"));

        foreach (var line in new[] { "Earnings drift, picked on 26 Oct 2026", "434.12, the open of 27 Oct 2026", "a close below 406.46", "|529.64|", "after 60 trading days, about 22 Jan 2027", "Open, 1 session held", "|3.45|" })
        {
            Assert.Contains(line, card, StringComparison.Ordinal);
        }

        var exits = marks.PicksOwnExit(held.Card, held.Pick);

        Assert.Contains("data-held=\"2026-10-26\"", exits, StringComparison.Ordinal);
        Assert.Contains("held on the trade the earnings drift picked it for on 26 Oct 2026, sold at 529.64, at a close below 406.46 or after 60 trading days, whichever comes first.", WebUtility.HtmlDecode(exits), StringComparison.Ordinal);

        // Where the levels' plan places no exit, its line names the held trade's in place of saying it places none.
        var noExit = WebUtility.HtmlDecode(marks.PlanTables("ROP", [], trade: held.Card, held: true));

        Assert.Contains("The levels' plan places no exit of its own; the held trade is sold at 529.64, at a close below 406.46 or after 60 trading days, whichever comes first.", noExit, StringComparison.Ordinal);
        Assert.DoesNotContain("data-exits=\"none\"", noExit, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-exits=\"none\">The plan places no exit.</p>", marks.PlanTables("ROP", []), StringComparison.Ordinal);

        // The proof passes the page drawing all four and fails one missing the plan's exits or the chart's levels.
        var chart = "<g class=\"pick-levels\" data-family=\"drift\" data-pick-stop=\"406.46\" data-pick-target=\"529.64\">";
        var page = MarkRenderer.ShortVersionLead(null, quarter, held) + marks.HeldCard(held) + exits + chart;

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", page), "held-trade"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", page), "short-version"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", page.Replace(exits, string.Empty, StringComparison.Ordinal)), "held-trade"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", page.Replace(chart, string.Empty, StringComparison.Ordinal)), "held-trade"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", page + marks.PlanTables("ROP", [])), "held-trade"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", page + marks.PlanTables("ROP", [], trade: held.Card, held: true)), "held-trade"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", MarkRenderer.ShortVersionLead(null, quarter) + marks.HeldCard(held) + exits + chart), "short-version"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", MarkRenderer.ShortVersionLead(null, quarter)), "held-trade"));
    }

    // What worked and what did not is drawn its first four sentences a paragraph, each paragraph cut alone, and the proof
    // fails a paragraph drawn with five.
    // see: What worked and what did not is drawn four sentences a paragraph at most
    [Fact]
    public void WhatWorkedAndWhatDidNotIsDrawnFourSentencesAParagraphAtMost()
    {
        const string Six = "One worked. Two worked. Three did not. Four did not. Five worked. Six did not.";
        var section = new WrittenCell(MarkRenderer.TheShortVersion, Six + "\n\nSeven worked. Eight did not.", new DateOnly(2026, 10, 26), "deepseek-flash", []);
        var drawn = new MarkRenderer().WrittenSection("ROP", section, [], proseLabel: "What worked and what did not.", sentencesAParagraph: MarkRenderer.WhatWorkedSentences);

        Assert.Equal(4, MarkRenderer.WhatWorkedSentences);
        Assert.Contains("One worked. Two worked. Three did not. Four did not.</p>", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("Five worked", drawn, StringComparison.Ordinal);
        Assert.Contains("Seven worked. Eight did not.", drawn, StringComparison.Ordinal);
        Assert.Contains("Six did not.", new MarkRenderer().WrittenSection("ROP", section, [], proseLabel: "What worked and what did not."), StringComparison.Ordinal);

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", drawn), "what-worked"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", new MarkRenderer().WrittenSection("ROP", section, [], proseLabel: "What worked and what did not.")), "what-worked"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", "<p class=\"prose\">Revenue rose.</p>"), "what-worked"));
    }

    // Older bands read out from the history pulled before the store's year as far as a pick's target and stop: turns at
    // pulled highs of 230, 250, 270 and 290 read at a scale of two are bands of 455 to 460, 495 to 500, 535 to 540 and 575
    // to 580 with a typical move of 10; the two nearest stand alone, a target of 529.64 takes the third, which reaches it,
    // and no more, and read from above a stored band at 470 the two nearest past it already reach it. Below, pulled lows
    // of 170, 150 and 130 are bands of 340 to 345, 300 to 305 and 260 to 265, and a stop of 280 takes the third.
    // see: Older bands are read out as far as a pick's stop and target, marked older and read by nothing else
    [Fact]
    public void OlderBandsReadOutAsFarAsAPicksTargetAndStop()
    {
        var first = new DateOnly(2021, 1, 4);
        var turnsUp = new Dictionary<int, decimal> { [15] = 230m, [40] = 250m, [65] = 270m, [90] = 290m };
        var turnsDown = new Dictionary<int, decimal> { [30] = 170m, [70] = 150m, [100] = 130m };
        var pulled = Enumerable.Range(0, 121)
            .Select(day => (first.AddDays(day), turnsUp.GetValueOrDefault(day, 200m + (day % 3)), turnsDown.GetValueOrDefault(day, 190m)))
            .ToArray();

        static decimal[] Highs(IEnumerable<EquityBrief.Core.Report.OlderBand> bands) => [.. bands.Select(band => band.High)];

        Assert.Equal([460m, 500m], Highs(EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: true, below: false)));
        Assert.Equal([460m, 500m, 540m], Highs(EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: true, below: false, reachUp: 529.64m)));
        Assert.Equal([500m, 540m], Highs(EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: true, below: false, reachUp: 529.64m, aboveFrom: 470m)));
        Assert.Equal([460m, 500m, 540m, 580m], Highs(EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: true, below: false, reachUp: 900m)));

        var below = EquityBrief.Core.Report.OlderBands.Of(pulled, 2m, 441.65m, 10m, above: false, below: true, reachDown: 280m);

        Assert.Equal([340m, 300m, 260m], below.Select(band => band.Low));
        Assert.All(below, band => Assert.Equal("support", band.Role));

        // The proof reads the bands the page draws, stored and older, against the stop and target its chart draws.
        const string Chart = "<g class=\"pick-levels\" data-family=\"drift\" data-pick-stop=\"280\" data-pick-target=\"529.64\">";
        const string Stored = "<tr class=\"band\" data-low-edge=\"300\" data-high-edge=\"305\" data-role=\"support\">";
        const string Reaching = "<tr class=\"band older\" data-older-role=\"resistance\" data-low-edge=\"535\" data-high-edge=\"540\" data-made-on=\"2021-03-01\">";
        const string Short = "<tr class=\"band older\" data-older-role=\"support\" data-low-edge=\"260\" data-high-edge=\"265\" data-made-on=\"2021-04-14\">";

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", Chart + Stored + Reaching + Short), "levels-cover"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", Chart + Stored + Short), "levels-cover"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", Chart + Stored + Reaching), "levels-cover"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", Stored + Reaching), "levels-cover"));
    }

    // A next report the night has passed reads as not announced with the day it is estimated at, from the company's own
    // report of the same quarter a year before, marked estimated in the snapshot and in the line beneath the numbers; the
    // proof fails a page drawing it as not announced with no estimate.
    // see: The page estimates a next report not on file from the company's own report of the same quarter a year before, marked estimated
    [Fact]
    public async Task ANextReportNotOnFileIsDrawnWithTheDayItIsEstimatedAtMarkedEstimated()
    {
        using var store = await WithFundamentals(withTheArchive: true);

        var filings = await Api(store).FundamentalsAsync(Name);

        using var payload = System.Text.Json.JsonDocument.Parse(StoredPayload(store, Name));

        var reportDate = DateOnly.ParseExact(payload.RootElement.GetProperty("estimated").GetProperty("reportDate").GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var from = reportDate.AddDays(-273);
        var estimated = from.AddDays(364);
        var drawn = NameScreen.Numbers(filings, new ConceptFigures(null, reportDate, null, null), null, (from, estimated));
        var bare = NameScreen.Numbers(filings, new ConceptFigures(null, reportDate, null, null));

        Assert.Contains(FormattableString.Invariant($"<dd data-reportDate=\"reported\" data-estimated=\"{estimated:yyyy-MM-dd}\"><b>not announced</b><small class=\"estimated\">estimated about {estimated:yyyy-MM-dd}, from its report of {from:yyyy-MM-dd} a year on</small>"), drawn, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"and the next report is not announced; estimated about {estimated:yyyy-MM-dd} from its report of {from:yyyy-MM-dd} a year on."), drawn, StringComparison.Ordinal);

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate(Name, drawn), "next-report-estimated"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate(Name, bare), "next-report-estimated"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate(Name, NameScreen.Numbers(filings, new ConceptFigures(null, reportDate.AddDays(-1), null, null))), "next-report-estimated"));
    }

    // The sources list keeps the documents naming the company or its industry in their title, address or opening and folds
    // the rest beneath it, saying where fewer than two remain: ROP's Roche releases name neither, and a market roundup
    // naming Roper only past its opening does not either. The proof fails a list that keeps too few without saying so.
    // see: A stock's industry group is the provider's until an override is set and is checked against its own account, and the sources list and the cycle read only documents naming the company or its industry in their opening
    [Fact]
    public void TheSourcesListKeepsTheDocumentsAboutTheCompanyOrItsIndustryAndFoldsTheRestBeneath()
    {
        const string Company = "Roper Technologies Inc";
        const string Industry = "Electronic Equipment, Instruments & Components";
        var release = new SourceCell("r1", "Roper Technologies announces third quarter financial results", "https://www.sec.gov/Archives/edgar/data/882835/a2026q3earningsrelease.htm", new DateOnly(2026, 10, 22), "Roper Technologies, Inc. (Nasdaq: ROP) reported financial results for the third quarter.");
        var roche = new SourceCell("n1", "Roche keeps 2026 outlook as Q3 sales rise", "https://www.example.com/roche", new DateOnly(2026, 10, 23), "Basel. Roche said its pharmaceuticals sales rose.");
        var roundup = new SourceCell("n2", "Stocks to watch this week", "https://www.example.com/roundup", new DateOnly(2026, 10, 24), new string('x', EquityBrief.Core.News.StoryMatch.Opening) + " Roper Technologies rose two per cent.");

        // Read as the page reads each document: its title, then its address and the opening of its text.
        static bool Relevant(SourceCell document) => EquityBrief.Core.Report.IndustryCheck.Relevant(document.Title, document.Url + " " + document.Opening, Company, Industry);

        Assert.Equal([true, false, false], new[] { release, roche, roundup }.Select(Relevant));

        var marks = new MarkRenderer();
        var few = marks.Sources("ROP", [release, roche, roundup], [], false, Relevant);

        Assert.Contains("data-documents=\"1\" data-left-out=\"2\" data-relevance=\"tested\"", few, StringComparison.Ordinal);
        Assert.Contains("Not enough relevant sources: 1 of the 3 documents the written regions cite name the company or its industry.", few, StringComparison.Ordinal);
        Assert.Contains("<details class=\"sources-left-out\" data-left-out=\"2\"><summary>2 documents name neither the company nor its industry, left out of the list</summary>", few, StringComparison.Ordinal);
        Assert.Contains("data-document=\"n1\"", few, StringComparison.Ordinal);

        var second = release with { Id = "r2", Title = "Roper Technologies quarterly report", Opening = "Roper Technologies files its quarterly report." };
        var enough = marks.Sources("ROP", [release, second, roche], [], false, Relevant);

        Assert.DoesNotContain("Not enough relevant sources", enough, StringComparison.Ordinal);
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", few), "sources-relevant"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", enough), "sources-relevant"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", few.Replace("data-documents=\"few\">Not enough relevant sources: ", "data-documents=\"few\">", StringComparison.Ordinal)), "sources-relevant"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", marks.Sources("ROP", [release, roche], [], false)), "sources-relevant"));
    }

    // The proof reads the news region's rows for a story it keeps with no label, the key beneath the nights the list picked
    // the stock before for each setup's own limit and the segments a quarter reported before its filing draws, and lists
    // every page whose peer group names none of its business with the group proposed for the operator to approve, setting
    // none.
    // see: Every report page rule is held over every name's drawn page and reported passed, failed or unexamined with the pages each failed on
    [Fact]
    public void TheProofReadsTheNewsLabelsTheOwnLimitAndListsThePeerGroupsToApprove()
    {
        const string News = "<div class=\"news-region\" data-ticker=\"ROP\" data-articles=\"2\" data-all=\"2\" data-positive=\"1\" data-negative=\"0\" data-neutral=\"0\" data-opinion=\"0\" data-unlabelled=\"1\" data-left-out=\"3\">";
        const string Labelled = "<li class=\"news-row\" data-article=\"abb42726f9194b1f\" data-state=\"labelled\" data-tabs=\"all positive\">";
        const string Waiting = "<li class=\"news-row\" data-article=\"675afc98361468a3\" data-state=\"unlabelled\" data-tabs=\"all\">";
        const string Refused = "<li class=\"news-row\" data-article=\"eda56412a9bf8d11\" data-state=\"refused\" data-tabs=\"all\">";

        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", News + Labelled + Waiting), "news-labelled"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", News + Labelled + Refused), "news-labelled"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", Labelled), "news-labelled"));

        const string Before = "<section class=\"card\" data-card=\"on-the-list-before\">";

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", Before + "followed to its stop, its target or the end of its own setup's limit, 60 sessions for the earnings drift"), "own-limit"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", Before + "followed for 63 sessions"), "own-limit"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", News), "own-limit"));

        const string Peers = "<div class=\"sub\">Against Electronic Equipment, Instruments &amp; Components's S&amp;P 500 members on the night, the provider's classification</div>";
        var sells = new MarkRenderer().WrittenSection("ROP", new WrittenCell(MarkRenderer.WhatItSells, RopSells, new DateOnly(2026, 10, 26), "deepseek-flash", []), []);
        var page = ReportProof.Evaluate("ROP", Peers + sells);

        Assert.Equal(new PeerProposal("ROP", "Electronic Equipment, Instruments & Components", "software", "Software"), page.Peer);
        Assert.Null(ReportProof.Evaluate("ROP", Peers.Replace("Electronic Equipment, Instruments &amp; Components", "Software", StringComparison.Ordinal) + sells).Peer);

        var html = ReportProof.Html(new ProofReport(DateTimeOffset.UnixEpoch, "GSPC", null, ReportProof.Counted([page]), [page]));

        Assert.Contains("<p data-proposals=\"1\">", html, StringComparison.Ordinal);
        Assert.Contains("<tr data-proposal=\"ROP\"><td>ROP</td><td>Electronic Equipment, Instruments &amp; Components</td><td>software</td><td>Software</td></tr>", html, StringComparison.Ordinal);

        // A quarter reported before its filing is stored draws the release's own segment table, and a segment commentary
        // written from the filing's segments says so beneath it; a page whose release states no such table is unexamined.
        const string Reported = "<section class=\"card spined fund\" id=\"quarter\"><div class=\"spine\"><div class=\"lbl\">The latest quarter</div><div class=\"dl\"><span class=\"dl-k\">Reported</span><b>2026-10-22</b><span>from the results release, filing not stored yet</span></div></div></section>";
        const string ReleaseTable = "<div class=\"sub segments-release\" data-quarter=\"2026-09-30\">The quarter to 2026-09-30, from the results release, filing not stored yet</div>";
        const string FromFiling = "<div class=\"written-part\" id=\"the-segment-commentary\" data-section=\"The segment commentary\" data-written-from=\"filing\">";
        const string Said = "<p class=\"degraded written-from\">Written from the segments the filing before it reported, before the results release's own table above was read; a report written now reads the release's figures.</p>";

        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", Reported + ReleaseTable + FromFiling + Said), "segments-release"));
        Assert.Equal(ReportProof.Passed, VerdictOf(ReportProof.Evaluate("ROP", Reported + ReleaseTable + FromFiling.Replace("filing", "release", StringComparison.Ordinal)), "segments-release"));
        Assert.Equal(ReportProof.Failed, VerdictOf(ReportProof.Evaluate("ROP", Reported + ReleaseTable + FromFiling), "segments-release"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", Reported), "segments-release"));
        Assert.Equal(ReportProof.Unexamined, VerdictOf(ReportProof.Evaluate("ROP", Reported.Replace(">Reported<", ">Filed<", StringComparison.Ordinal) + ReleaseTable), "segments-release"));
    }
}
