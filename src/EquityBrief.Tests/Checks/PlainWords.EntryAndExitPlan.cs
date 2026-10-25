using EquityBrief.Api.Reading;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Checks;

// plain-words over the entry and exit plan a buy point's fold draws on Tonight, from the 22.6 correction of 2026-10-25:
// the plan in every form its stored ladder takes, drawn inside a pick's card beneath the setup's own plan as the fold
// draws it, speaks in the reader's words; and the check fails the full page's own words for the same plan drawn there.
// see: A buy point's fold draws the stock's entry and exit plan beneath its setup's own plan, as the full page draws it
public partial class PlainWords
{
    // A stored plan of every row the ladder writes: a tranche on each of the five conditions, one with no stop beneath
    // it, an exit listed and not traded with the reason the ladder gives, one selling half and the top's half kept on
    // its rising stop, and the invalidation at the lowest stop.
    const string EveryRow = """
        {"tranches":[{"lowEdge":"98.0000","highEdge":"99.0000","condition":"AvailableNow","stop":null},
                     {"lowEdge":"95.0000","highEdge":"96.0000","condition":"FailedBreakdown","stop":"94.0000"},
                     {"lowEdge":"92.0000","highEdge":"93.0000","condition":"FirstCloseBackAbove","stop":"91.0000"},
                     {"lowEdge":"89.0000","highEdge":"90.0000","condition":"SecondDayAfterAShock","stop":"88.0000"},
                     {"lowEdge":"86.0000","highEdge":"87.0000","condition":"ReachesTheZone","stop":"85.0000"}],
         "exits":[{"lowEdge":"101.0000","highEdge":"101.5000","traded":false,"trailing":false,"fraction":"0","reason":"closer than 2 typical days' moves to the blended entry"},
                  {"lowEdge":"105.0000","highEdge":"106.0000","traded":true,"trailing":false,"fraction":"1/2","reason":null},
                  {"lowEdge":"110.0000","highEdge":"111.0000","traded":true,"trailing":true,"fraction":"1/2","reason":null}],
         "invalidation":"85.0000","events":[]}
        """;

    // The whole position kept on its rising stop, and a plan placing nothing to buy beneath its exits.
    const string WholeTrails = """
        {"tranches":[{"lowEdge":"95.0000","highEdge":"97.0000","condition":"ReachesTheZone","stop":"93.0000"}],
         "exits":[{"lowEdge":"110.0000","highEdge":"110.0000","traded":true,"trailing":true,"fraction":"1/1","reason":null}],
         "invalidation":"93.0000","events":[]}
        """;

    const string NothingToBuy = """
        {"tranches":[],"exits":[{"lowEdge":"105.0000","highEdge":"106.0000","traded":true,"trailing":true,"fraction":"1/1","reason":null}],"invalidation":null,"events":[]}
        """;

    // Every reason the ladder's rule and the night's builder store for a plan placing nothing.
    static readonly string[] NoPlanReasons =
    [
        "the trend is down, so no tranche is placed",
        "the trend state could not be classified, so no tranche is placed",
        "no tranche is placed",
        "no bar for this session; the last session stored for the name is 2026-10-21",
        "no support band sits below the price",
        "no support band below the price has an anchor other than a moving average",
        "no 50-day average",
        "no 200-day average",
        "fewer than 2 swing lows",
        "fewer than 2 swing lows and fewer than 2 swing highs",
    ];

    static LadderRow LadderOf(string plan) => new("XYZ", new DateOnly(2026, 10, 23), "uptrend", plan);

    // A pick's card holding the entry and exit plan beneath its setup's own, as tomorrow's buys draws it.
    static string FoldWith(string levels)
    {
        var card = CardsDrawn().First(one => one.Index == "GSPC" && one.Family == EquityBrief.Core.Families.SetupFamilies.Pullback);
        var drawn = new MarkRenderer().DecisionCard(card, linked: true, levels: levels);

        return Cards.Computed("Tomorrow's buys", "<p class=\"card-summary\" data-summary>One stock to buy at tomorrow's open.</p>" + drawn, region: "buy-points");
    }

    [Fact]
    public void TheEntryAndExitPlanSpeaksInTheReadersWordsInEveryFormItsLadderTakes()
    {
        var terms = Terms();
        var marks = new MarkRenderer();

        List<(string Form, string Levels)> forms =
        [
            ("every row", NameScreen.EntryAndExitPlan(marks, "XYZ", LadderOf(EveryRow), 100m)),
            ("the whole position on its rising stop", NameScreen.EntryAndExitPlan(marks, "XYZ", LadderOf(WholeTrails), 100m)),
            ("nothing to buy", NameScreen.EntryAndExitPlan(marks, "XYZ", LadderOf(NothingToBuy), 100m)),
            ("no reason stored", NameScreen.EntryAndExitPlan(marks, "XYZ", LadderOf("""{"tranches":[],"exits":[],"invalidation":null,"events":[]}"""), 100m)),
            ("no plan stored", NameScreen.EntryAndExitPlan(marks, "XYZ", null, 100m)),
            ("no close stored", NameScreen.EntryAndExitPlan(marks, "XYZ", LadderOf(EveryRow), null)),
            .. NoPlanReasons.Select(reason => (reason, NameScreen.EntryAndExitPlan(marks, "XYZ", LadderOf($$"""{"tranches":[],"exits":[],"invalidation":null,"events":[],"reason":"{{reason}}"}"""), 100m))),
        ];

        Assert.All(forms, form => Assert.Empty(Faults(FoldWith(form.Levels), RegionsOf(TonightPage), terms)));
        Assert.Equal(6 + NoPlanReasons.Length, forms.Count);

        // Every row is drawn: the plan column holds the five tranches, their four stops with the lowest the invalidation and
        // the three exits, and the tables say each condition and each exit in the reader's words.
        var every = forms[0].Levels;

        Assert.Contains("data-plan-rows=\"12\"", every, StringComparison.Ordinal);
        Assert.Contains("the second day after a shock, once the price has stayed above the first day's low", every, StringComparison.Ordinal);
        Assert.Contains("closer than 2 typical days' moves to the average buy price", every, StringComparison.Ordinal);
        Assert.Contains(">Rising stop from 110.00 to 111.00</text>", every, StringComparison.Ordinal);
        Assert.Contains("the last 1/2 is kept on a rising stop under the price, not sold at a price", every, StringComparison.Ordinal);
        Assert.Contains("the whole position is kept on a rising stop under the price, not sold at a price", forms[1].Levels, StringComparison.Ordinal);
        Assert.Contains("The plan places no zone to buy in.", forms[2].Levels, StringComparison.Ordinal);

        // A plan placing nothing says why in a line and draws no figure or table.
        Assert.Contains("<p class=\"degraded\" data-plan=\"none\">No entry and exit plan is drawn for XYZ: the trend is down, so no zone to buy in is placed.</p>", forms[6].Levels, StringComparison.Ordinal);
        Assert.Contains("No entry and exit plan is drawn for XYZ: no plan is stored for it on the night.", forms[4].Levels, StringComparison.Ordinal);
        Assert.Contains("No entry and exit plan is drawn for XYZ: no close is stored for it on the night.", forms[5].Levels, StringComparison.Ordinal);
        Assert.All(forms.Skip(3), form => Assert.DoesNotContain("plan-column", form.Levels, StringComparison.Ordinal));
    }

    // The same plan in the full page's own words fails the check where the fold draws it: the stop that follows the price
    // up named as a trail, the whole position held on it, and a plan placing no tranche.
    [Fact]
    public void TheCheckFailsTheFullPagesOwnWordsForThePlanDrawnInTheFold()
    {
        var terms = Terms();
        var marks = new MarkRenderer();
        var rows = NameScreen.PlanRows(LadderOf(WholeTrails));
        var levels = "<section class=\"card-levels\"><h5>Entry and exit plan</h5>" + marks.PlanColumn("XYZ", 100m, rows) + marks.PlanTables("XYZ", NameScreen.PlanRows(LadderOf(NothingToBuy))) + "</section>";
        var faults = Faults(FoldWith(levels), RegionsOf(TonightPage), terms);

        Assert.Contains(faults, fault => fault.StartsWith("'trail' in the card 'buy-points'", StringComparison.Ordinal));
        Assert.Contains(faults, fault => fault.StartsWith("'held' in the card 'buy-points'", StringComparison.Ordinal));
        Assert.Contains(faults, fault => fault.StartsWith("'tranche' in the card 'buy-points'", StringComparison.Ordinal));
        Assert.Empty(Faults(FoldWith(NameScreen.EntryAndExitPlan(marks, "XYZ", LadderOf(WholeTrails), 100m)), RegionsOf(TonightPage), terms));
    }
}
