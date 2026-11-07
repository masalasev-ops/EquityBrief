using EquityBrief.Api.Reading;
using EquityBrief.Core.EntryCheck;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// Plans on a pick's page over the faults the operator's addendum of 2026-11-07 names, word for word in
// prompts/2026-11-07-pick-page-plans-addendum.md, each held to CTSH's page as it should read against the page as it read
// on 2026-11-06: picked by the pullback, buy 64.72, stop 61.55, target 71.26.
public partial class ReadSurface
{
    // CTSH's stored plan of 2026-11-06: three tranches with their stops, four exits a quarter each, the top trailing, and the
    // invalidation at the lowest stop.
    const string CtshPlan = """
        {"tranches":[{"lowEdge":"61.5500","highEdge":"66.2600","condition":"AvailableNow","stop":"57.9100"},
                     {"lowEdge":"57.9100","highEdge":"58.0400","condition":"ReachesTheZone","stop":"54.7000"},
                     {"lowEdge":"54.7000","highEdge":"56.2200","condition":"ReachesTheZone","stop":"52.4900"}],
         "exits":[{"lowEdge":"67.7200","highEdge":"67.7200","traded":true,"trailing":false,"fraction":"1/4","reason":null},
                  {"lowEdge":"69.1800","highEdge":"69.1800","traded":true,"trailing":false,"fraction":"1/4","reason":null},
                  {"lowEdge":"71.2600","highEdge":"71.2600","traded":true,"trailing":false,"fraction":"1/4","reason":null},
                  {"lowEdge":"73.9000","highEdge":"73.9000","traded":true,"trailing":true,"fraction":"1/4","reason":null}],
         "invalidation":"52.4900","events":[]}
        """;

    static LadderRow CtshLadder() => new("CTSH", new DateOnly(2026, 11, 6), "uptrend", CtshPlan);

    // On 2026-11-06 Tonight drew beneath CTSH's card its entry and exit plan open, its first tranche bought at the price now
    // with a stop of 57.91, and the name page's entry check a wait plan with a stop of 54.70, beside the pullback's 61.55:
    // two stops for one buy, neither folded nor named as not traded. Each other way in is folded beneath the rule's plan
    // under the words that it is not traded, its stops named as its own; a stock no family picked draws its plan as before.
    // see: A pick's page draws its rule's plan as the plan and folds every other way in beneath it as not traded, each stop named as its own
    [Fact]
    public void APicksPageDrawsItsRulesPlanAndFoldsEveryOtherWayInWithItsOwnStops()
    {
        var fold = NameScreen.EntryAndExitPlan(new MarkRenderer(), "CTSH", CtshLadder(), 64.72m, "pullback", 61.55m);

        Assert.Contains("<details class=\"another-way\" data-way=\"levels\" data-traded=\"false\"><summary>Not traded: another way in, for comparison</summary><h5>Entry and exit plan</h5>", fold, StringComparison.Ordinal);
        Assert.Contains("<p class=\"another-stops\">Its stops, 57.91, 54.70 and 52.49, are its own: the pullback's trade is sold on a close below 61.55.</p></details>", fold, StringComparison.Ordinal);

        var reading = EntryWorked() with { WaitLow = 57.91m, WaitHigh = 58.04m, WaitStop = 54.70m, WaitExit = 67.72m, WaitReward = 3.0, ZoneDays = 2.4 };

        Assert.Contains(
            "<details class=\"another-way\" data-way=\"wait\" data-traded=\"false\"><summary>Not traded: another way in, for comparison</summary><p class=\"entry-line\" data-entry=\"wait\">Provisional, and it sets nothing. Wait to buy between $57.91 and $58.04, 2.4 typical days below tonight's close. Stop $54.70. First exit $67.72, about 3.0 to 1. It is worth taking if it reaches its first exit before its stop more than 25.0% of the time.</p><p class=\"another-stops\">Its stop, 54.70, is its own: the pullback's trade is sold on a close below 61.55.</p></details>",
            new MarkRenderer().EntryCheck(reading, 64.72m, 61.55m, "pullback"),
            StringComparison.Ordinal);
        Assert.Equal(
            "This is the pullback's own plan, and its trade is sold by these exits alone. Another way in, worked from the stock's support and resistance levels, is folded below it: not traded, with stops of its own.",
            MarkRenderer.OnePlanSays("pullback", below: true));

        // A stock no family picked draws the levels' plan as its plan, unfolded; a wait plan with no stop names none.
        Assert.DoesNotContain("another-way", NameScreen.EntryAndExitPlan(new MarkRenderer(), "CTSH", CtshLadder(), 64.72m), StringComparison.Ordinal);
        Assert.DoesNotContain("another-way", new MarkRenderer().EntryCheck(reading, 64.72m, 61.55m, null), StringComparison.Ordinal);
        Assert.Equal("It places no stop of its own: the dip's trade is sold by its own exits.", EquityBrief.Core.Cards.PlanWords.OwnStops([], null, "dip"));
    }

    // CTSH's bands above its close on 2026-11-06: the swing high of 2026-10-22 at 73.90, turned once, and the 78.6, 61.8 and
    // 50 per cent retracements of the move that ended on 2026-10-29 at 71.26, 69.18 and 67.72, none of which turned the price.
    static IReadOnlyList<EquityBrief.Core.Cards.FlagBand> CtshBands() =>
    [
        EquityBrief.Core.Cards.FlagBand.Of(73.90m, 73.90m, [("swing high", new DateOnly(2026, 10, 22))]),
        EquityBrief.Core.Cards.FlagBand.Of(71.26m, 71.26m, [("retracement 78.6", new DateOnly(2026, 10, 29))]),
        EquityBrief.Core.Cards.FlagBand.Of(69.18m, 69.18m, [("retracement 61.8", new DateOnly(2026, 10, 29))]),
        EquityBrief.Core.Cards.FlagBand.Of(67.72m, 67.72m, [("retracement 50", new DateOnly(2026, 10, 29))]),
    ];

    // On 2026-11-06 CTSH's plans raised four things no part of the page said: its first tranche made 0.64 to 1 to its first
    // exit; that exit, 67.72, and the pullback's own target, 71.26, were retracements the price had never turned at, the
    // first band above that had being 73.90; and the pullback's 2 to 1 gate was passed at 2.06. Each plan says so beside it as
    // information, worked by hand: 9.18 over 3.17 to 73.90, 2.90; 3.815 over 5.995 to 67.72, 0.64, and 9.995 over it to
    // 73.90, 1.67. The fourth flag does not fire on CTSH, its 1.14 typical days' stop wider than its median report-day move
    // of 1.02, and fires on a stop of 0.90 against a median of 1.30.
    // see: A plan's arithmetic is flagged beside it as information and changes nothing the plan does
    [Fact]
    public void APlansArithmeticIsFlaggedBesideItAsInformation()
    {
        var steps = "Passed every gate of the pullback on the S&P 500: trade, reward to risk 2.0586 at or above 2; the stop 1.14 typical moves below the entry, inside 0.5 to 4.";
        var hits = new CardHitsView(new DateOnly(2027, 2, 9), 4, 1.02, 0.89, 2, null, false, null, null, null, [], []);
        var card = new DecisionCardView("GSPC", new DateOnly(2026, 11, 6), "pullback", "CTSH", "the pullback", 64.72m, 61.55m, 71.26m,
            [new CardLineView(1, EquityBrief.Core.Cards.CardLines.TrendName, "tick", steps), new CardLineView(4, EquityBrief.Core.Cards.CardLines.EarningsName, "note", "Reported on 2026-10-29. The next date isn't announced yet; it is estimated about 2027-02-03, from its report of 2026-02-04 a year on, inside this trade's hold.")],
            null, Hits: hits, Cap: 63);
        var flagged = NameScreen.WithPlanFlags(card, CtshBands())!;

        Assert.Equal(
            [
                "Its target, 71.26, is a retracement the price has never turned at; to the first band above it that has, 73.90, its reward to risk is 2.90 to 1.",
                "It passed its 2 to 1 gate by 0.06, under a tenth.",
            ],
            flagged.PlanFlags);
        Assert.Contains("<div class=\"plan-flags\" data-flags=\"2\"><p>Worth knowing from the plan's own numbers, which changes nothing it does:</p><ul><li>Its target, 71.26,", new MarkRenderer().DecisionCard(flagged), StringComparison.Ordinal);

        Assert.Equal(
            [
                "Its reward to risk to the first exit is 0.64 to 1, under one to one: it risks more than it makes there.",
                "The first exit, 67.72, is a retracement the price has never turned at; to the first band above it that has, 73.90, its reward to risk is 1.67 to 1.",
            ],
            MarkRenderer.StagedFlags(NameScreen.PlanRows(CtshLadder(), plain: true), CtshBands()));

        Assert.Equal(
            ["Its stop is 0.90 typical days below the buy while a report falls inside the hold, and its median report-day move of 1.30 typical days is wider than that."],
            EquityBrief.Core.Cards.PlanFlags.Of(100m, 99m, 103m, [], "its target", stopTypical: 0.9, reportInside: true, reportMedianTypical: 1.3));
        Assert.Empty(EquityBrief.Core.Cards.PlanFlags.Of(100m, 99m, 103m, [], "its target", gate: 2m, stopTypical: 1.14, reportInside: true, reportMedianTypical: 1.02));
    }

    // On 2026-11-06 CTSH's card said "You risk $1 to make $2.06." and sized 309 shares on the example account, stating no
    // share of the time the plan must be right to break even and not whether the 309 were the whole position; the wait plan
    // gave "about 3.0 to 1" alone. Each plan states its break-even, worked by hand: 3.17 at risk against 6.54 to the target,
    // 3.17 over 9.71, 32.6%; a wait plan at 3 to 1, one in four.
    // see: Each plan states the share of the time it must reach its exit before its stop to break even, and whether its shares are the whole position
    [Fact]
    public void EachPlanStatesItsBreakEvenAndWhetherItsSharesAreTheWholePosition()
    {
        var card = new DecisionCardView("GSPC", new DateOnly(2026, 11, 6), "pullback", "CTSH", "the pullback", 64.72m, 61.55m, 71.26m, [], null,
            Money: new CardMoneyView(309, 981.08m, 19_998.48m, 0.2m, 14.40m, true, false, 1m, 2_019.62m, Example: true), Cap: 63);
        var reward = (71.26m - 64.72m) / (64.72m - 61.55m);
        var pick = new FamilyPickCell(new ListingCell("CTSH", new DateOnly(2026, 11, 6), 2, 0, 64.72m, []), 2, 64.72m, 61.55m, 71.26m, false, reward, "why", []);
        var drawn = new MarkRenderer().DecisionCard(card, pick);

        Assert.Equal(0.3265m, Math.Round(EquityBrief.Core.Cards.PlanWords.BreakEven(reward), 4));
        Assert.Contains("<p class=\"card-break-even\" data-break-even=\"0.326\">It is worth taking if it reaches its target before its stop more than 32.6% of the time.</p>", drawn, StringComparison.Ordinal);

        // The stock's full page draws the card with no pick beside it, and works the same share from the card's own prices.
        Assert.Contains("<p class=\"card-break-even\" data-break-even=\"0.326\">", new MarkRenderer().DecisionCard(card), StringComparison.Ordinal);
        Assert.Contains("Fewer shares than the risk allows: the position cap limits it. Buying and selling costs about $14.40 at the published rates. The 309 shares are the whole position, bought at once.</p>", drawn, StringComparison.Ordinal);

        var reading = EntryWorked() with { WaitLow = 57.91m, WaitHigh = 58.04m, WaitStop = 54.70m, WaitExit = 67.72m, WaitReward = 3.0, ZoneDays = 2.4 };

        Assert.EndsWith("First exit $67.72, about 3.0 to 1. It is worth taking if it reaches its first exit before its stop more than 25.0% of the time.", EntryWords.Wait(reading), StringComparison.Ordinal);
    }

    // On 2026-11-06 CTSH's card said its next report was "about 2027-01-28", three months after its report of 2026-10-29,
    // while the same page's numbers, calendar and plan said "about 2027-02-03", from its report of 2026-02-04 a year on: two
    // dates from two rules. The card reads the page's estimate, named with its source, wherever its reports a year back give
    // one, on the stock's page and on Tonight; about three months on, so said, only where they give none.
    // see: A page draws one next report date, the calendar's where it stands and otherwise one estimate stated with its source
    [Fact]
    public void APageDrawsOneNextReportDateAndItsCardReadsTheSame()
    {
        var night = new DateOnly(2026, 11, 6);
        DateOnly[] reported = [new(2025, 10, 29), new(2026, 2, 4), new(2026, 4, 29), new(2026, 7, 29), new(2026, 10, 29)];
        var estimate = EquityBrief.Core.Cards.CardLines.EstimatedReport(reported, night, new DateOnly(2026, 10, 29));

        Assert.Equal((new DateOnly(2026, 2, 4), new DateOnly(2027, 2, 3)), estimate);

        var card = new DecisionCardView("GSPC", night, "pullback", "CTSH", "the pullback", 64.72m, 61.55m, 71.26m,
            [new CardLineView(4, EquityBrief.Core.Cards.CardLines.EarningsName, "warn", "No report date is on file for it, so it cannot be placed in the hold.")], null, Cap: 63);
        var line = NameScreen.WithEstimatedReport(card, reported)!.Lines.Single(one => one.Name == EquityBrief.Core.Cards.CardLines.EarningsName);

        Assert.Equal("Reported on 2026-10-29. The next date isn't announced yet; it is estimated about 2027-02-03, from its report of 2026-02-04 a year on, inside this trade's hold.", line.Words);

        // Tonight's card reads the same from the night's report days.
        var row = new DecisionCardRow("GSPC", night, "pullback", "CTSH", 2, 64.72m, 61.55m, 71.26m, "the pullback", "{}", """[{"line":4,"name":"Earnings in the hold","verdict":"warn","words":"No report date is on file for it, so it cannot be placed in the hold."}]""", null, Cap: 63);
        var tonight = CardScreen.View(row, CardContext.None with { NewestReports = new Dictionary<string, DateOnly> { ["CTSH"] = new(2026, 10, 29) }, ReportDays = new Dictionary<string, IReadOnlyList<DateOnly>> { ["CTSH"] = reported } });

        Assert.Equal(line.Words, tonight.Lines.Single(one => one.Name == EquityBrief.Core.Cards.CardLines.EarningsName).Words);

        // A stock holding no report a year back reads about three months on.
        Assert.Equal(
            "Reported on 2026-10-29. The next date isn't announced yet; it's usually about three months after the last one, about 2027-01-28, inside this trade's hold.",
            EquityBrief.Core.Cards.CardLines.ReportedRecently(new DateOnly(2026, 10, 29), night, 63)!.Words);
    }

    // On 2026-11-06 CTSH's analysts were dated by the UTC day each fetch was stored on: its fetch of 01:23 UTC on 2026-10-10
    // was asked on 9 Oct in New York, and Yahoo's targets were stamped the same way. Its revisions stood undated beneath the
    // consensus, from the quarters fetch of 2026-10-30, though the night asks the year's revisions every night. Each analyst
    // figure is dated by its New York day; the revisions say their fetch day against the newest report, and the year's row
    // reads the night's own newer ask, dated.
    // see: An analyst figure is dated by the New York day it was fetched on, and its revisions by that day against the newest report
    [Fact]
    public void AnAnalystFigureIsDatedByItsNewYorkDayAndTheRevisionsAgainstTheNewestReport()
    {
        Assert.Equal(new DateOnly(2026, 10, 9), EquityBrief.Core.Time.SessionZones.NewYorkDay(new DateTimeOffset(2026, 10, 10, 1, 23, 16, TimeSpan.Zero)));
        Assert.Equal(new DateOnly(2026, 10, 30), EquityBrief.Core.Time.SessionZones.NewYorkDay(new DateTimeOffset(2026, 10, 30, 23, 48, 6, TimeSpan.Zero)));

        var yahoo = new EquityBrief.Core.Analysts.YahooAnalystView(new DateTimeOffset(2026, 11, 7, 3, 0, 0, TimeSpan.Zero), new EquityBrief.Core.Providers.YahooAnalystAnswer("USD", 60m, 68.86m, 69m, 80m, 28, "buy", [], []));

        Assert.Contains("Yahoo's targets as asked on 2026-11-06", new MarkRenderer().TargetRange("CTSH", yahoo, 64.72m), StringComparison.Ordinal);

        var night = new DateOnly(2026, 11, 6);
        var year = new EquityBrief.Core.Quarters.EstimatePeriod("0y", new DateOnly(2026, 12, 31), 5.76m, 5.68m, 5.82m, 5.28m, 28, 22.2e9m, 22.1e9m, 22.3e9m, null, 30, 5.76m, 5.76m, 5.75m, 5.74m, 5.71m, 1, 2, null, 1);
        var reading = EquityBrief.Core.Report.AnalystView.WithNightRevision(
            EquityBrief.Core.Report.AnalystView.Of(new DateOnly(2026, 10, 30), [year], [], night, 64.72m),
            new EquityBrief.Core.Report.NightRevision(night, 0, 3, 1, 4, 5.70m, 5.76m))!;
        var drawn = new MarkRenderer().ConsensusTable(reading, "USD", new DateOnly(2026, 10, 29));

        Assert.Contains("<p class=\"revisions-fetched\" data-fetched=\"2026-10-30\" data-year-asked=\"2026-11-06\">Revisions as fetched on 2026-10-30, after the 2026-10-29 report; this fiscal year's as the night asked them on 2026-11-06.</p>", drawn, StringComparison.Ordinal);
        Assert.Contains("<tr data-period=\"0y\"><td>This fiscal year</td><td class=\"num\" data-up-7=\"0\">0</td><td class=\"num\" data-down-7=\"3\">3</td><td class=\"num\" data-up-30=\"1\">1</td><td class=\"num\" data-down-30=\"4\">4</td>", drawn, StringComparison.Ordinal);

        // A night's ask no newer than the fetch leaves the fetch's row, and a fetch before the newest report says so.
        var older = EquityBrief.Core.Report.AnalystView.WithNightRevision(EquityBrief.Core.Report.AnalystView.Of(new DateOnly(2026, 10, 30), [year], [], night, 64.72m), new EquityBrief.Core.Report.NightRevision(new DateOnly(2026, 10, 29), 0, 3, 1, 4, 5.70m, 5.76m))!;

        Assert.Null(older.YearAskedOn);
        Assert.Contains("Revisions as fetched on 2026-10-30, before the 2026-11-05 report.</p>", new MarkRenderer().ConsensusTable(older, "USD", new DateOnly(2026, 11, 5)), StringComparison.Ordinal);
    }

    // On 2026-11-06 CTSH's news drew "Cognizant and Google Cloud Expand Alliance to Accelerate Agentic Enterprise
    // Transformation" three times, each from finance.yahoo.com on 2026-10-19, each labelled positive and each counted. A
    // story the feed carried more than once is drawn and counted once, its labelled copy kept, and the region says how many
    // copies it merged; Tonight's counts read it once too.
    // see: A story the feed carried more than once is drawn and counted once, and the page says how many copies it merged
    [Fact]
    public void AStoryTheFeedCarriedMoreThanOnceIsDrawnAndCountedOnce()
    {
        const string Title = "Cognizant and Google Cloud Expand Alliance to Accelerate Agentic Enterprise Transformation";

        NewsArticleRow Story(string id, string title, string published, string? outcome) =>
            new(id, title, "finance.yahoo.com", published, "https://finance.yahoo.com/" + id, EquityBrief.Core.Research.Admissibility.Accepted, outcome, null, outcome is null ? null : "product", outcome is null ? null : "positive", outcome is null ? null : "why", "model", "profile");

        var view = NewsScreen.Build("CTSH", new DateOnly(2026, 11, 6),
        [
            Story("a1", "Cognizant Completes Acquisition of Wizeline", "2026-11-06T13:00:00Z", EquityBrief.Core.News.NewsLabelling.Labelled),
            Story("a2", Title, "2026-10-19T12:00:00Z", null),
            Story("a3", Title, "2026-10-19T12:05:00Z", EquityBrief.Core.News.NewsLabelling.Labelled),
            Story("a4", Title.ToUpperInvariant() + ".", "2026-10-19T14:00:00Z", EquityBrief.Core.News.NewsLabelling.Labelled),
        ], listed: true, run: null);

        Assert.Equal((2, 2, 0, 2), (view.Rows.Count, view.Positive, view.Unlabelled, view.Merged));
        Assert.Equal(["a1", "a3"], view.Rows.Select(row => row.ArticleId));
        Assert.Contains("<p class=\"news-merged\" data-merged=\"2\">2 copies of a story the feed carried more than once were merged, so each story is drawn and counted once.</p>", new MarkRenderer().NewsRegion(view), StringComparison.Ordinal);

        // The same title on another day is another story.
        Assert.NotEqual(EquityBrief.Core.News.NewsDuplicates.Key(Title, "2026-10-19"), EquityBrief.Core.News.NewsDuplicates.Key(Title, "2026-10-20"));
    }
}
