using EquityBrief.Api.Reading;
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
            "<details class=\"another-way\" data-way=\"wait\" data-traded=\"false\"><summary>Not traded: another way in, for comparison</summary><p class=\"entry-line\" data-entry=\"wait\">Provisional, and it sets nothing. Wait to buy between $57.91 and $58.04, 2.4 typical days below tonight's close. Stop $54.70. First exit $67.72, about 3.0 to 1.</p><p class=\"another-stops\">Its stop, 54.70, is its own: the pullback's trade is sold on a close below 61.55.</p></details>",
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
}
