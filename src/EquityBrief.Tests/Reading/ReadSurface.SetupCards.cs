using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.3: every setup card built one way. Each kind of rule's chips are its settings in the short words of its
// own table, worked by hand; every registered variant of the S&P 500's families differs from its live rule on a chip, so
// its tab names what it moves; the funnel's largest drop is the largest fall from one gate to the next, the earlier gate
// taking a tie; a card listing nothing says so in one line on both sides of its floors; each empty sector says why; and
// a heavyweight's standing tonight and the card's tiles are worked by hand at their edges.
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public partial class ReadSurface
{
    // The rows 19.3 adds: a family's card's header, guide, picks as rows of its own, one line and stocks one gate short; its
    // rule's tabs, chips, marked chips, share line, a variant's picks with their numbers, the funnel's bar and the forming
    // list's expander; and the heavyweights card's tiles, sectors, empty sectors' reasons, return since bought and standing.
    internal static readonly string[] SetupCardRows =
    [
        .. new[]
        {
            "a header naming the setup in its tint with a pill for its standing",
            "a reading guide for every setup card behind a press, where the key it repeated stood",
            "its live rule's picks drawn as rows of its own in the setup's order, each ticker opening the stock's chart page with the company beneath it",
            "one line naming the gate that stopped the most of the members reaching it and its empty stretch against its usual longest or the completed stretches it holds of the thirty a usual stretch waits for",
            "behind an expander naming their count the stocks one gate short of its buy point, each with the gate it missed and its words beneath the pointer and the family's three numbers",
        }.Select(part => Harness.CheckReach.Key("15.7 Tonight", "A family's card, " + part)),
        .. new[]
        {
            "a tab a rule beneath the card's header naming the live rule or the provisional rule first and then each registered variant by the number of its first registration and the settings it differs on in short words written by code from the register's parameters through one table of short words an evaluator",
            "the chosen rule's settings as chips with its sentence a click away",
            "a chosen variant's differing chips marked",
            "one line naming the picks it shares with the live rule's passes tonight and those it adds and drops",
            "its own picks with their plans and the family's three numbers in place of the live rule's rows",
            "the funnel drawn as one bar a gate as long as the share of the members reaching it that passed it with how many passed of how many reached it beside it and the gate that stopped the most of those reaching it marked and named, or a first gate passing no member marked so",
            "behind an expander naming the count forming",
        }.Select(part => Harness.CheckReach.Key("15.7 Tonight", "A family's card's rule, " + part)),
        .. new[]
        {
            "four tiles counting its holdings and the sectors of the eleven holding one and stating its return since the last rebalance beside the equal-weighted return of the funds of the sectors it holds over the same sessions and the sessions to the next rebalance",
            "the holdings grouped by sector under each sector's heading with its fund and the fund's return over the look-back to the night",
            "each empty sector with why its book bought nothing there",
            "its return since bought",
            "a pill saying whether it is still leading or lost its lead or is below its 200-day average read off tonight's closes by the rule's own functions and marked as information only",
        }.Select(part => Harness.CheckReach.Key("15.7 Tonight", "The sector heavyweights' card, " + part)),
    ];

    // The rows 19.3 retires: a family's card's rows' place, the buy's place between its stop and target, the why in words
    // and the key; the card's rule's selector and a variant's picks in place of the live rule's rows; the list of stocks
    // close to a buy point across the setups, which each card draws behind its expander; and the heavyweights card's
    // sector cell and held while leading.
    internal static readonly string[] RetiredCardRows =
    [
        Harness.CheckReach.Key("15.7 Tonight", "A family's card, its place down the page"),
        Harness.CheckReach.Key("15.7 Tonight", "A family's card, where the buy sits between the stop and the target"),
        Harness.CheckReach.Key("15.7 Tonight", "A family's card, why it is listed tonight in the figures its family stored"),
        Harness.CheckReach.Key("15.7 Tonight", "A family's card, a key saying how to read it"),
        Harness.CheckReach.Key("15.7 Tonight", "A family's card's rule, a selector beside the card's state line listing the live rule or the provisional rule first and then each registered variant by the number of its first registration with the register's words"),
        Harness.CheckReach.Key("15.7 Tonight", "A family's card's rule, its own picks with their plans in place of the live rule's rows"),
        Harness.CheckReach.Key("15.7 Tonight", "Close to a buy point across the setups, one row a stock and setup with the setup's label"),
        Harness.CheckReach.Key("15.7 Tonight", "Close to a buy point across the setups, the one gate it missed in the gate's words"),
        Harness.CheckReach.Key("15.7 Tonight", "Close to a buy point across the setups, the count shown of the count there are"),
        Harness.CheckReach.Key("15.7 Tonight", "The sector heavyweights' card, each with its sector"),
        Harness.CheckReach.Key("15.7 Tonight", "The sector heavyweights' card, held while leading in place of a stop and a target"),
    ];

    static string[] Words(RuleKind kind, IReadOnlyDictionary<string, double> parameters) =>
        [.. RuleChips.Of(kind, parameters).Select(chip => chip.Words)];

    [Fact]
    public void EachKindOfRulesChipsAreItsSettingsInTheShortWordsOfItsOwnTable()
    {
        // The S&P 500's pullback at a filter version's settings: its strength, depth, dry-up, arrival, report window,
        // reward to risk, stop, breadth and plan, the dials a variant may add drawn only where stated.
        Assert.Equal(
            ["stronger than 50%", "1 to 5 moves down", "volume under 1.5 times", "turns up within 3", "no report in 15 sessions", "1.5 times the risk", "stop 0.5 to 4 moves", "breadth at least 45%", "the swing plan"],
            Words(RuleKind.Pullback, SwingFilterRule.ParametersOf(new FilterSettings(0.45, 0.5, 1, 5, 1.5, 1.5, 0.5, 4, 15, 3, TradeInput.Swing))));
        Assert.Equal(
            ["stronger than 50%", "1 to 5 moves down", "no dry-up gate", "turns up within 3", "no report in 15 sessions", "1.5 times the risk", "stop 0.5 to 4 moves", "market check off", "the ladder's plan", "no deteriorating business", "best 3 a night", "estimates raised"],
            Words(RuleKind.Pullback, SwingFilterRule.ParametersOf(new FilterSettings(0.45, 0.5, 1, 5, double.PositiveInfinity, 1.5, 0.5, 4, 15, 3, TradeInput.Ladder), marketGate: false, skipDeteriorating: true, bestOf: 3, raisedEstimates: true)));

        // The breakout and the drift, each switch drawn only where it is read.
        Assert.Equal(
            ["126-session high", "volume 1.5 times", "ranges at 0.85", "stop 1.5 moves", "index above its 200-day"],
            Words(RuleKind.Breakout, new Dictionary<string, double> { ["highSessions"] = 126, ["volumeMultiple"] = 1.5, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5, ["indexAverageSessions"] = 200, ["vixLookbackSessions"] = 0 }));
        Assert.Equal(
            ["within 3 sessions", "up 0.5 moves", "volume 2 times", "target 2.5 times the risk", "stop at least 1 move", "VIX falling over 10"],
            Words(RuleKind.Drift, new Dictionary<string, double> { ["windowSessions"] = 3, ["reactionMoves"] = 0.5, ["volumeMultiple"] = 2, ["targetRiskMultiple"] = 2.5, ["stopFloorMoves"] = 1, ["indexAverageSessions"] = 0, ["vixLookbackSessions"] = 10 }));

        // The S&P 500's sector heavyweights at the live setting.
        Assert.Equal(
            ["10 largest", "251-session look-back", "2 leaders", "against the fund", "beta at least 1", "sold on losing the lead"],
            Words(RuleKind.Heavyweights, SectorHeavyweightCandidate.ParametersOf(HeavyweightRule.Live)));

        // An S&P 400 or 600 pullback, its levels after its dials and its own market dial beside the breadth it reads; with
        // the market check off the dial is no part of the chip.
        var pullback = new Dictionary<string, double>
        {
            ["band"] = 0, ["creditAverage"] = 0, ["creditChange"] = 0, ["depthHigh"] = 5, ["depthLow"] = 1, ["dryUp"] = 1.5, ["floors"] = 1, ["freshness"] = 3, ["fundOverSpy"] = 0,
            ["hold"] = 0, ["industryFall"] = 0, ["largeBreadth"] = 0, ["lowestClose"] = 5, ["market"] = 0.45, ["quality"] = 1, ["rewardToRisk"] = 2, ["stopHigh"] = 4, ["stopLow"] = 0.5, ["strength"] = 0.5,
        };

        Assert.Equal(
            ["strength 0.5", "1 to 5 moves down", "volume under 1.5 times", "fresh within 3", "2 times the risk", "stop 0.5 to 4 moves", "band strength 0", "dollar floor 1 times", "close at least $5", "the profit gate", "market check on its own breadth at 45%"],
            Words(RuleKind.IndexPullback, pullback));
        Assert.Equal(
            IndexRuleCandidate.MarketCheckOff,
            Words(RuleKind.IndexPullback, new Dictionary<string, double>(pullback) { ["largeBreadth"] = IndexRuleCandidate.NoMarketCheck, ["market"] = 0 })[^1]);
        Assert.Equal(
            "market check on the S&P 500's breadth, its own dial off",
            Words(RuleKind.IndexPullback, new Dictionary<string, double>(pullback) { ["largeBreadth"] = 1, ["market"] = 0 })[^1]);

        // An index drift with its window and stop floor, and the index heavyweights in each design.
        Assert.Equal(
            ["within 3 sessions", "up 0.5 moves", "volume 2 times", "target 2.5 times the risk", "dollar floor 1 times", "close at least $5", "the profit gate", "market check off"],
            Words(RuleKind.IndexDrift, new Dictionary<string, double> { ["windowSessions"] = 3, ["reactionMoves"] = 0.5, ["volumeMultiple"] = 2, ["targetMultiple"] = 2.5, ["stopFloor"] = 0, ["wideWindow"] = 0, ["peers"] = 0, ["floors"] = 1, ["lowestClose"] = 5, ["quality"] = 1, ["largeBreadth"] = 2 }));
        Assert.Equal(
            ["10 largest", "251-session look-back", "2 leaders", "beta at least 1", "sold on losing the lead", "dollar floor 1 times", "profit and cover"],
            Words(RuleKind.IndexHeavyweights, new Dictionary<string, double> { ["design"] = 0, ["floors"] = 1, ["highBeta"] = 1, ["industries"] = 0, ["largest"] = 10, ["leaders"] = 2, ["lookBack"] = 251, ["members"] = 0, ["quality"] = 2, ["soldOnLeading"] = 1, ["soldUnderAverage"] = 0, ["twelveOne"] = 0, ["window"] = 0 }));
        Assert.Equal(
            ["design (b)", "5 strongest industries", "over 21 sessions", "1 member an industry", "dollar floor 1 times", "the profit gate"],
            Words(RuleKind.IndexHeavyweights, new Dictionary<string, double> { ["design"] = 1, ["window"] = 21, ["industries"] = 5, ["members"] = 1, ["quality"] = 1, ["floors"] = 1 }));

        // A tab names each chip the variant differs on, and a setting the live rule states that the variant does not.
        var live = RuleChips.Of(RuleKind.Breakout, new Dictionary<string, double> { ["highSessions"] = 126, ["volumeMultiple"] = 1.5, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5, ["indexAverageSessions"] = 200 });
        var variant = RuleChips.Of(RuleKind.Breakout, new Dictionary<string, double> { ["highSessions"] = 251, ["volumeMultiple"] = 1.5, ["rangeCeiling"] = 0.85, ["stopMoves"] = 1.5 });

        Assert.Equal("V2 · 251-session high · without index above its 200-day", RuleChips.TabWords(2, RuleChips.Differing(live, variant)));
        Assert.Equal("V1 · the same settings", RuleChips.TabWords(1, RuleChips.Differing(live, live)));
    }

    [Fact]
    public void EveryRegisteredVariantOfTheSAndP500sFamiliesDiffersFromItsLiveRuleOnAChip()
    {
        // The pullback's family as registered under a version whose strength floor stands under the top third, as version
        // 5's does at 0.5: under the proposed settings, already at the top third, that variant would be the live rule.
        IReadOnlyList<IReadOnlyList<Registration>> families =
        [
            TheSwingFamily.For("5", FilterSettings.Proposed with { StrengthFloor = 0.5 }),
            TheSetupFamilies.Breakouts,
            TheSetupFamilies.Drifts,
            TheSetupFamilies.Heavyweights,
        ];

        foreach (var family in families)
        {
            var kind = RuleScreen.KindOf(CandidateEvaluators.Find(family[0].Evaluator)!)!.Value;
            var live = RuleChips.Of(kind, family[0].Parameters);

            foreach (var registration in family.Skip(1))
            {
                var differing = RuleChips.Differing(live, RuleChips.Of(kind, registration.Parameters));

                Assert.True(differing.Count > 0, $"{registration.Candidate} draws the live rule's chips: its tab could not name what it moves.");
                Assert.DoesNotContain("the same settings", RuleChips.TabWords(1, differing), StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void TheFunnelsLargestDropIsTheLargestFallFromOneGateToTheNextTheEarlierTakingATie()
    {
        Assert.Equal(new FunnelDrop("new high", 6, 10), RuleFunnelDrop.Largest([("market", 10), ("new high", 4), ("volume", 3), ("tightened", 2), ("trade", 2)]));
        Assert.Equal(new FunnelDrop("volume", 8, 9), RuleFunnelDrop.Largest([("market", 9), ("new high", 9), ("volume", 1), ("trade", 1)]));
        Assert.Equal(new FunnelDrop("b", 4, 10), RuleFunnelDrop.Largest([("a", 10), ("b", 6), ("c", 2)]));
        Assert.Null(RuleFunnelDrop.Largest([("a", 3), ("b", 3)]));
        Assert.Null(RuleFunnelDrop.Largest([("market", 0), ("setup", 0)]));
        Assert.Null(RuleFunnelDrop.Largest([("market", 5)]));
    }

    // A card listing nothing tonight, its funnel the volume gate stopping 8 of the 9 reaching it unless another is given,
    // at a stretch given.
    static string EmptyCard(StretchLineView stretch, string? empty = "No stock passed this setup tonight.", IReadOnlyList<(string Gate, int Passed)>? funnel = null, int? reached = null)
    {
        var live = new RuleChoiceView("the live breakout rule", RuleScreenWords.LiveSlug, null, true, "Live");
        var rule = new RuleView(live, [live], [new RulePartView("A stock closes above its high", false)], null, funnel ?? [("market", 9), ("new high", 9), ("volume", 1), ("trade", 1)], stretch, null, true, null, Reached: reached);

        return new MarkRenderer().FamilyCard(new FamilyCardView("breakout", "Breakout", "Breakouts to a new high", "Breakout from a base", "A stock closes above its high.", 2, 3, new DateOnly(2026, 1, 2), 0, [], [], empty, RuleChoice: rule));
    }

    [Fact]
    public void ACardListingNothingSaysSoInOneLineOnBothSidesOfItsFloors()
    {
        // From 22.6 the card opens on its sentence, worked by hand over the funnel: no picks tonight, the step most stocks
        // missed in the setup's own words for it, the volume stopping 8 of the 9 reaching it, and the furthest any got, its
        // trade's room for its stop, which 1 passed.
        const string Said = "No picks tonight. The step most stocks missed: volume too light, 8 of the 9 that got that far. The furthest any got: room for its stop, 1 of them.";

        // Under the floors: the quiet-nights line says it is too new to know a normal quiet run, the completed stretches
        // against the thirty the mark waits for.
        var under = EmptyCard(new StretchLineView(14, null, false, 29, 600));

        Assert.Contains($"<p class=\"card-summary\" data-summary><span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">{Said}</span></p>", under, StringComparison.Ordinal);
        Assert.Contains(">Quiet nights: this is its 14th quiet night in a row; it is too new to know a normal quiet run, 29 of the 30 runs it needs having ended.</p>", under, StringComparison.Ordinal);

        // At the floors: the quiet run against its usual longest, and past it flagged on its sentence and its line.
        var flagged = EmptyCard(new StretchLineView(49, 48, true, 30, 600));

        Assert.Contains(">Quiet nights: this is its 14th quiet night in a row; runs of up to 48 have happened before.</p>", EmptyCard(new StretchLineView(14, 48, false, 30, 600)), StringComparison.Ordinal);
        Assert.Contains($"<span class=\"family-empty-line flagged\" data-picks=\"0\" data-flagged=\"true\">{Said}</span>", flagged, StringComparison.Ordinal);
        Assert.Contains(">Quiet nights: this is its 49th quiet night in a row; runs of up to 48 have happened before, so this one is longer than usual.</p>", flagged, StringComparison.Ordinal);
        Assert.Contains(">Quiet nights: this is its 1st quiet night in a row; runs of up to 48 have happened before.</p>", EmptyCard(new StretchLineView(1, 48, false, 30, 600)), StringComparison.Ordinal);

        // A card listing nothing names no pick, says why in its sentence, and keeps the rest in its folds, each closed: the
        // full rule, how the stocks it read were narrowed and how the setup has done.
        var card = EmptyCard(new StretchLineView(14, 48, false, 30, 600));

        Assert.DoesNotContain("card-picks", card, StringComparison.Ordinal);
        Assert.Contains("<details class=\"fold fam-rule\"><summary>The full rule, and how its settings were chosen</summary>", card, StringComparison.Ordinal);
        Assert.Contains("<details class=\"fold fam-funnel\"><summary>How the 9 stocks it read were narrowed to 1</summary>", card, StringComparison.Ordinal);
        Assert.Contains("<details class=\"fold fam-record\"><summary>How this setup has done</summary>", card, StringComparison.Ordinal);
        Assert.DoesNotMatch("<details class=\"fold[^\"]*\" open", card);

        // The funnel leaves out the market check's row while the check is open, since it then passes every member read.
        Assert.Contains("data-market-row=\"false\"><ol class=\"rule-funnel\"><li data-gate=\"new high\"", card, StringComparison.Ordinal);
        Assert.DoesNotContain("<li data-gate=\"market\"", card, StringComparison.Ordinal);

        // A night the market check closed over the 397 members its night read: the line says the check closed, its breadth
        // and floor standing in the market line above, and names no gate from the funnel, which holds no fall before its
        // first gate; the funnel draws the market's row as passing none of the 397 reaching it, an empty bar, and each part
        // after it as passing none of the none reaching it.
        IReadOnlyList<(string Gate, int Passed)> closed = [("market", 0), ("setup", 0), ("floors", 0)];
        var shut = EmptyCard(
            new StretchLineView(18, 58, false, 30, 600),
            "The market check closed every list of the S&P 400 tonight: the S&P 400's breadth was 43.8% of its members closing above their 200-day average, below its floor of 45%.",
            closed,
            397);

        Assert.Contains(
            "<span class=\"family-empty-line\" data-picks=\"0\" data-flagged=\"false\">No picks tonight: the market check closed its list.</span>",
            shut,
            StringComparison.Ordinal);
        Assert.Contains("<p class=\"funnel-drop\" data-drop=\"market\">No stock got past the first step tonight: the market check closed.</p>", shut, StringComparison.Ordinal);
        Assert.Contains("data-largest-drop=\"market\" data-market-row=\"true\"", shut, StringComparison.Ordinal);
        Assert.Equal(
            [("market", "0", "397", true, "0", "0 of 397"), ("setup", "0", "0", false, "0", "0 of 0"), ("floors", "0", "0", false, "0", "0 of 0")],
            FunnelRows(shut));
        Assert.DoesNotContain("No gate stopped", shut, StringComparison.Ordinal);

        // A funnel stored without the count of the members its night read says how many passed its first gate alone.
        Assert.Equal(
            [("market", "0", "none", true, "0", "0"), ("setup", "0", "0", false, "0", "0 of 0"), ("floors", "0", "0", false, "0", "0 of 0")],
            FunnelRows(EmptyCard(new StretchLineView(18, 58, false, 30, 600), "The market check closed every list tonight.", closed)));
    }

    // A rendered card's funnel rows in order: each gate, how many passed it, how many reached it, whether it is marked as
    // stopping the most, its bar's width in per cent and the words beside it.
    static IReadOnlyList<(string Gate, string Passed, string Reached, bool Marked, string Width, string Words)> FunnelRows(string card) =>
    [
        .. Regex.Matches(card, "<li data-gate=\"([^\"]+)\" data-passed=\"([^\"]+)\" data-reached=\"([^\"]+)\"( class=\"largest-drop\")?><span class=\"fb-name\">[^<]*</span><span class=\"fb-bar\"><span class=\"fb-fill\" style=\"width:([^%]+)%\"></span></span><b class=\"fb-count\">([^<]+)</b></li>")
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, match.Groups[4].Success, match.Groups[5].Value, match.Groups[6].Value)),
    ];

    // A card with a pick, or with a variant chosen, is drawn open under its header and its full card, never folded; its
    // funnel says beneath the bars which gate stopped the most, the one place the card says it.
    [Fact]
    public void ACardWithAPickOrAVariantChosenOpensWithItsFunnelsLineAndIsNeverFolded()
    {
        var live = new RuleChoiceView("the live breakout rule", RuleScreenWords.LiveSlug, null, true, "Live");
        var variant = new RuleChoiceView("the breakout rule at a volume of 1.25 times", "v1", 1, false, "V1 · volume 1.25 times");
        IReadOnlyList<(string Gate, int Passed)> funnel = [("market", 9), ("new high", 9), ("volume", 1), ("trade", 1)];
        var chosen = new RuleView(variant, [live, variant], [new RulePartView("A stock closes above its high", false)], [], funnel, new StretchLineView(1, 48, false, 30, 600), null, true, null);
        var card = new MarkRenderer().FamilyCard(new FamilyCardView("breakout", "Breakout", "Breakouts to a new high", "Breakout from a base", "A stock closes above its high.", 2, 3, new DateOnly(2026, 1, 2), 1, [], [], "No stock passed this setup tonight.", RuleChoice: chosen));

        Assert.False(MarkRenderer.Folded(new FamilyCardView("breakout", "Breakout", "Breakouts to a new high", "Breakout from a base", "A stock closes above its high.", 2, 3, new DateOnly(2026, 1, 2), 1, [], [], null, RuleChoice: chosen)));

        // From 22.6 the card opens on what the version chosen would buy, beneath it the banner saying it is a version being
        // tested and not tonight's picks, and its funnel's fold names the step most stocks missed in the setup's words.
        Assert.Contains("data-folded=\"false\"><p class=\"card-summary\" data-summary>This version finds no pick tonight.</p><div class=\"variant-band\" role=\"status\" data-variant=\"1\"><p class=\"band-head\">A version being tested, not tonight's picks</p><p>Version: volume 1.25 times</p>", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"funnel-drop\" data-drop=\"volume\">The step most stocks missed: volume too light, 8 of the 9 that got that far.</p>", card, StringComparison.Ordinal);
    }

    // A constructed S&P 400 night whose first gate drawn stops all but one: its three members read with the market check
    // open, M1 alone passing the breakout's setup and every part after it. The rule cards store the funnel with the three
    // the night read, and the card draws each bar as the share of the members reaching its gate that passed it, with how
    // many passed of how many reached it beside it: the setup's 1 of 3 a third of its bar and marked, each part after it 1
    // of 1 and whole, and the line naming the gate that stopped the most standing beneath them.
    [Fact]
    public async Task EachFunnelBarIsTheShareOfTheMembersReachingItsGateThatPassedIt()
    {
        using var store = UniversesStore();

        var cards = await new RuleCards(FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("night-funnel", [], new DateTimeOffset(2026, 10, 2, 23, 0, 0, TimeSpan.Zero));

        Assert.Null(cards.Indices.Single(index => index.Index == "MID").Fault);
        Assert.Equal(
            RuleRows.GatesJson([(RuleRows.Market, 3), (RuleRows.Setup, 1), (RuleRows.Floors, 1), (RuleRows.Profit, 1), (RuleRows.Cover, 1)], 3),
            Text(store, $"SELECT gates FROM rule_night WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND family = 'breakout';"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")), "breakout");

        Assert.Equal(
            [("setup", "1", "3", true, "33.3", "1 of 3"), ("floors", "1", "1", false, "100", "1 of 1"), ("profit", "1", "1", false, "100", "1 of 1"), ("interest cover", "1", "1", false, "100", "1 of 1")],
            FunnelRows(card));
        Assert.Contains("<p class=\"funnel-drop\" data-drop=\"setup\">The step most stocks missed: not at a new high on heavy volume, 2 of the 3 that got that far.</p>", card, StringComparison.Ordinal);
    }

    // A constructed S&P 400 night whose market check closed, the index's own breadth of seven in seventeen under the
    // provisional rule's floor: the index families and the rule cards store the breakout's funnel with the seventeen
    // members the night read while its market check passes none, and the card draws the market's row as passing none of
    // the seventeen reaching it, marked, and each part after it none of none.
    [Fact]
    public async Task AClosedMarketChecksRowIsDrawnAgainstTheMembersTheNightReadAsTheRuleCardsStoredThem()
    {
        using var store = Checks.FixtureExpectations.ClosedIndexStore();
        var night = new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero);
        var clock = FixedClock.At(night, SessionZones.UnitedStates);

        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-closed", default, [], night);

        var cards = await new RuleCards(clock, store.DatabaseFile).RunAsync("night-closed-cards", [], night);

        Assert.Null(cards.Indices.Single(index => index.Index == "MID").Fault);
        Assert.Equal(
            RuleRows.GatesJson([(RuleRows.Market, 0), (RuleRows.Setup, 0), (RuleRows.Floors, 0), (RuleRows.Profit, 0), (RuleRows.Cover, 0)], 17),
            Text(store, $"SELECT gates FROM rule_night WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND family = 'breakout';"));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = Section(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400")), "breakout");

        Assert.Equal(
            [("market", "0", "17", true, "0", "0 of 17"), ("setup", "0", "0", false, "0", "0 of 0"), ("floors", "0", "0", false, "0", "0 of 0"), ("profit", "0", "0", false, "0", "0 of 0"), ("interest cover", "0", "0", false, "0", "0 of 0")],
            FunnelRows(card));
    }

    static CardCell NumberCell(double value) => new(value, null);

    static CardCell NoNumber(string why) => new(null, why);

    // A member's catalogue readings with the columns given held and every other none.
    static double?[] ReadingsOf(params (string Column, double Value)[] held)
    {
        var readings = new double?[LedgerReadings.Count];

        foreach (var (column, value) in held)
        {
            readings[LedgerReadings.IndexOf(column)] = value;
        }

        return readings;
    }

    // From the 19.3 correction of 2026-10-24, worked by hand: a family's numbers read off the answer the night stored for the
    // stock where one is handed in, as the S&P 500's nights store every gate with its values. ALLE's drift answer of
    // 2026-10-23, short of its volume, reads its reaction of 5.2065 typical moves, its volume of 1.7359 times and 1 session
    // since the report as the operator's store held them; GL's breakout answer its volume of 3.4117 times and ranges of
    // 0.8375, with its sessions since its high, 4, off its readings, which no gate holds; and a pullback's swing answer its
    // depth, dry-up and strength, whatever the member's readings hold. A gate holding no value, or one stored as none or
    // as no number, says it stored nothing; a gate the answer never reached says so; and the business-first family, whose
    // S&P 500 answer holds no values, reads its member's readings, saying where they hold none.
    [Fact]
    public void EachFamilysNumbersAreReadOffTheAnswerTheNightStoredAndSayWhichWhereItHoldsNone()
    {
        static Gate Part(string name, bool passed, params (string Key, string Value)[] values) => new(name, passed, "its words", FamilyRule.Values(values));

        var member = ReadingsOf(("since_high", 4), ("depth", 9.5));

        IReadOnlyList<Gate> alle =
        [
            Part(FamilyRule.Market, true, ("breadth", "0.6"), ("floor", "0.5")),
            Part(DriftRule.Print, true, ("report", "2026-10-22"), ("session", "2026-10-22"), ("back", "1"), ("window", "3")),
            Part(DriftRule.Beat, true),
            Part(DriftRule.Reaction, true, ("moves", "5.2065185076060505"), ("floor", "0.5"), ("typical move", "3.365012526970862")),
            Part(DriftRule.Volume, false, ("multiple", "1.7359287717276313"), ("floor", "2")),
            Part(DriftRule.Held, true),
            Part(FamilyRule.Trade, true),
        ];
        IReadOnlyList<Gate> gl =
        [
            Part(FamilyRule.Market, true),
            Part(BreakoutRule.NewHigh, true, ("sessions", "252"), (BreakoutRule.WindowValue, "126")),
            Part(BreakoutRule.Volume, true, ("multiple", "3.411733765277914"), ("floor", "1.5")),
            Part(BreakoutRule.Tightened, true, ("ratio", "0.8375345830200104"), ("ceiling", "0.85")),
            Part(FamilyRule.Trade, false),
        ];
        IReadOnlyList<Gate> pullback =
        [
            Part(SwingGates.Market, true),
            Part(SwingGates.Trend, true, ("trend state", "uptrend"), ("strength", "0.734"), ("floor", "0.5")),
            Part(SwingGates.Setup, true, ("family", "pullback"), ("depth", "2.3456"), ("dry-up", "0.8")),
            Part(SwingGates.Trigger, true),
            Part(SwingGates.Trade, false),
        ];

        Assert.Equal([NumberCell(5.2065185076060505), NumberCell(1.7359287717276313), NumberCell(1)], CardNumbers.Cells(DriftRule.Name, alle, null, member));
        Assert.Equal([NumberCell(4), NumberCell(3.411733765277914), NumberCell(0.8375345830200104)], CardNumbers.Cells(BreakoutRule.Name, gl, null, member));
        Assert.Equal([NumberCell(2.3456), NumberCell(0.8), NumberCell(0.734)], CardNumbers.Cells(SetupFamilies.Pullback, pullback, null, member));

        // A print outside the window: the gates after it store nothing and its sessions back are read.
        IReadOnlyList<Gate> outside =
        [
            Part(FamilyRule.Market, true),
            Part(DriftRule.Print, false, ("report", "2026-08-20"), ("session", "2026-08-20"), ("back", "35"), ("window", "3")),
            Part(DriftRule.Beat, false),
            Part(DriftRule.Reaction, false),
            Part(DriftRule.Volume, false),
        ];

        Assert.Equal([NoNumber(CardNumbers.NotStored), NoNumber(CardNumbers.NotStored), NumberCell(35)], CardNumbers.Cells(DriftRule.Name, outside, null, member));

        // A volume stored as none and ranges stored as no number store nothing; an answer stored before its tightened gate
        // never reached it.
        IReadOnlyList<Gate> unread = [Part(FamilyRule.Market, true), Part(BreakoutRule.Volume, false, ("multiple", "none")), Part(BreakoutRule.Tightened, true, ("ratio", "NaN"))];
        IReadOnlyList<Gate> older = [Part(FamilyRule.Market, true), Part(BreakoutRule.NewHigh, true), Part(BreakoutRule.Volume, true, ("multiple", "1.6"))];

        Assert.Equal([NumberCell(4), NoNumber(CardNumbers.NotStored), NoNumber(CardNumbers.NotStored)], CardNumbers.Cells(BreakoutRule.Name, unread, null, member));
        Assert.Equal([NumberCell(4), NumberCell(1.6), NoNumber(CardNumbers.NotReached)], CardNumbers.Cells(BreakoutRule.Name, older, null, member));
        Assert.Equal([NumberCell(9.5), NoNumber(CardNumbers.NotRead), NoNumber(CardNumbers.NotRead)], CardNumbers.Cells(FundamentalsRule.Name, null, null, member));
        Assert.Empty(CardNumbers.Cells(HeavyweightRule.Name, alle, null, member));

        // Drawn: a value in its form, and a number held nowhere as none with why.
        var drawn = new MarkRenderer().FamilyCard(new FamilyCardView(
            BreakoutRule.Name, "Breakout", "Breakouts to a new high", "Breakout from a base", "A stock closes above its high.", 2, 3, new DateOnly(2026, 1, 2), 0, [], [], null,
            Close: [new CloseToBuyCell("B1", "B One", BreakoutRule.Name, "Breakout", "volume", "volume 1.60 times its 50-session average, below 1.75"), new CloseToBuyCell("B2", null, BreakoutRule.Name, "Breakout", "new high", "no new high")],
            Numbers: new Dictionary<string, IReadOnlyList<CardCell>>
            {
                ["B1"] = CardNumbers.Cells(BreakoutRule.Name, older, null, member),
                ["B2"] = [NoNumber(CardNumbers.NotRead), NoNumber(CardNumbers.NoSetup), NoNumber(CardNumbers.NotStored)],
            }));

        Assert.Contains("<td class=\"r num\" data-number=\"since_high\" data-value=\"4\">4</td><td class=\"r num\" data-number=\"volume_multiple\" data-value=\"1.6\">1.60 times</td><td class=\"r num\" data-number=\"range_ratio\" data-value=\"none\" data-none=\"not reached\"><span class=\"degraded\">none, not reached</span></td>", drawn, StringComparison.Ordinal);
        Assert.Contains("<td class=\"missed-gate\" title=\"volume 1.60 times its 50-day average, below 1.75\"><b>Volume too light</b></td>", drawn, StringComparison.Ordinal);
        Assert.Equal(["not read", "no setup stored", "not stored"], Regex.Matches(Assert.Single(Blocks(drawn, "<tr data-ticker=\"B2\".*?</tr>")), "data-none=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<summary>Stocks one step short (2)</summary>", drawn, StringComparison.Ordinal);
    }

    // From the 19.3 correction of 2026-10-24, worked by hand: on an index whose answers hold no values, a family's own
    // numbers read off the stock's setup the ledger stored on the night and every other off the member's readings, each
    // saying where none is held; and a drift's, which the ledger stores on its report day alone, off the setup of that day
    // within the window its rule states, its last session included, its sessions since the report counted on the
    // exchange's calendar: from Thursday 2026-10-22 to Friday 2026-10-23 one; from Tuesday 2026-10-20 three, at the
    // provisional rule's window of 3 and read; from Monday 2026-10-19 four, past it and read only at the S&P 400's live
    // window of 10; and across Thanksgiving from Wednesday 2026-11-25 to Friday 2026-11-27 one. The window is the live
    // rule's, its wider window where it states one, and the provisional rule's off the night's settings where none stands.
    [Fact]
    public void AnIndexsNumbersAreReadOffTheLedgersSetupsAndADriftsOffItsReportDayWithinItsRulesWindow()
    {
        var member = ReadingsOf(("since_high", 4));
        var breakout = ReadingsOf(("volume_multiple", 2.0), ("since_high", 7));

        Assert.Equal([NumberCell(4), NumberCell(2.0), NoNumber(CardNumbers.NotRead)], CardNumbers.Cells(BreakoutRule.Name, null, breakout, member));
        Assert.Equal([NumberCell(4), NoNumber(CardNumbers.NoSetup), NoNumber(CardNumbers.NoSetup)], CardNumbers.Cells(BreakoutRule.Name, null, null, member));
        Assert.Equal([NoNumber(CardNumbers.NotRead), NumberCell(2.0), NoNumber(CardNumbers.NotRead)], CardNumbers.Cells(BreakoutRule.Name, null, breakout, null));

        var reaction = ReadingsOf(("reaction_moves", 2.25), ("volume_multiple", 3.0), ("freshness", 0));
        var night = new DateOnly(2026, 10, 23);
        var since = LedgerReadings.IndexOf("freshness");

        Assert.Equal([NumberCell(2.25), NumberCell(3.0), NumberCell(0)], CardNumbers.Cells(DriftRule.Name, null, CardNumbers.AtReportDay(night, reaction, night, 3), null));
        Assert.Equal([NumberCell(2.25), NumberCell(3.0), NumberCell(1)], CardNumbers.Cells(DriftRule.Name, null, CardNumbers.AtReportDay(new DateOnly(2026, 10, 22), reaction, night, 3), null));
        Assert.Equal(3.0, CardNumbers.AtReportDay(new DateOnly(2026, 10, 20), reaction, night, 3)![since]);
        Assert.Null(CardNumbers.AtReportDay(new DateOnly(2026, 10, 19), reaction, night, 3));
        Assert.Equal(4.0, CardNumbers.AtReportDay(new DateOnly(2026, 10, 19), reaction, night, 10)![since]);
        Assert.Equal(1.0, CardNumbers.AtReportDay(new DateOnly(2026, 11, 25), reaction, new DateOnly(2026, 11, 27), 3)![since]);
        Assert.Null(CardNumbers.AtReportDay(new DateOnly(2026, 10, 26), reaction, night, 3));
        Assert.Equal(0.0, reaction[since]);
        Assert.Equal([NoNumber(CardNumbers.NoSetup), NoNumber(CardNumbers.NoSetup), NoNumber(CardNumbers.NoSetup)], CardNumbers.Cells(DriftRule.Name, null, null, member));

        var settings = IndexRuleSettings.Read(IndexFamilies.Settings("MID"));
        CandidateRow Live(string parameters) => new(1, "the live S&P 400 drift rule", "drift-400", "registered", null, DateTimeOffset.UnixEpoch, parameters);

        Assert.Equal(3, TonightScreen.DriftWindow(null, settings));
        Assert.Equal(10, TonightScreen.DriftWindow(Live("{\"entry\": 1, \"wideWindow\": 0, \"windowSessions\": 10}"), settings));
        Assert.Equal(21, TonightScreen.DriftWindow(Live("{\"wideWindow\": 21, \"windowSessions\": 3}"), settings));
        Assert.Null(TonightScreen.DriftWindow(Live("no parameters"), settings));
    }

    // From the 19.3 correction of 2026-10-24: on the S&P 500 each card's stocks one gate short draw the values their stored
    // answers hold, read back off the page and worked by hand against the words the shared list draws for the same rows.
    // Over the night the family pages are read on: N1, one gate short of the pullback at its trade, its swing answer
    // storing a depth of 2.3456, a dry-up of 0.8 and a strength of 0.734 while its ledger readings hold others; K3, short
    // of the breakout at its volume of 1.2 times its average under 1.5, its ranges at 0.7 and its sessions since its high,
    // 3, off its readings; K5, short at its volume where no average was stored, whose gate stored nothing; and D1, short
    // of the drift at its volume as ALLE's answer stood on 2026-10-23, its reaction 5.2065 typical moves a session back on
    // 1.7359 times its average under 2.
    [Fact]
    public async Task OnTheSAndP500EachCardsStocksOneGateShortDrawTheValuesTheirStoredAnswersHold()
    {
        using var store = await FamilyPagesStore();

        object Part(string gate, bool passed, string reason, Dictionary<string, string> values) => new { gate, passed, reason, values };

        var n1 = JsonSerializer.Serialize(new
        {
            gates = new[]
            {
                Part(SwingGates.Market, true, "breadth at or above its floor", new() { ["breadth"] = "0.6", ["floor"] = "0.5" }),
                Part(SwingGates.Trend, true, "an uptrend at or above its floor", new() { ["trend state"] = "uptrend", ["strength"] = "0.734", ["floor"] = "0.5" }),
                Part(SwingGates.Setup, true, "a pullback into an anchored support band", new() { ["family"] = "pullback", ["depth"] = "2.3456", ["dry-up"] = "0.8" }),
                Part(SwingGates.Trigger, true, "the close above the previous session's high", new() { ["arrived"] = "tonight" }),
                Part(SwingGates.Trade, false, "reward to risk below its floor", new() { ["reward to risk"] = "1.2" }),
            },
            notes = Array.Empty<string>(),
        });

        store.Execute($"UPDATE gate_result SET gates = '{n1}' WHERE ticker = 'N1' AND session_date = '{TheSwitch}';");

        static string Breakout(string volumeWords, IReadOnlyDictionary<string, string> volume, string ratio) => FamilyRule.GatesJson(
        [
            new Gate(FamilyRule.Market, true, "breadth at or above its floor", FamilyRule.Values(("breadth", "0.6"), ("floor", "0.5"))),
            new Gate(BreakoutRule.NewHigh, true, "the close of 30 is above the highest high of the 126 sessions before it, 29", FamilyRule.Values(("close", "30"), ("high", "29"), ("sessions", "252"), (BreakoutRule.WindowValue, "126"))),
            new Gate(BreakoutRule.Volume, false, volumeWords, volume),
            new Gate(BreakoutRule.Tightened, true, "ranges narrowed", FamilyRule.Values(("ratio", ratio), ("ceiling", "0.85"))),
            new Gate(FamilyRule.Trade, true, "the stop 1.5 typical moves beneath", FamilyRule.Values(("close", "30"))),
        ]);

        store.Execute($"UPDATE family_result SET gates = '{Breakout("volume 1.20 times its 50-session average, below 1.5", FamilyRule.Values(("multiple", "1.2"), ("floor", "1.5")), "0.7")}' WHERE ticker = 'K3' AND session_date = '{TheSwitch}';");
        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{TheSwitch}', 'K5', 'breakout', 0, 1, NULL, '30', '28', NULL, NULL, '[]', '{Breakout("not available: no 50-session average volume is stored for the night", FamilyRule.Values(), "0.8")}');");

        var drift = FamilyRule.GatesJson(
        [
            new Gate(FamilyRule.Market, true, "breadth at or above its floor", FamilyRule.Values(("breadth", "0.6"), ("floor", "0.5"))),
            new Gate(DriftRule.Print, true, "the print of 2026-10-01 reacted on 2026-10-01, 1 session(s) before tonight, inside the 3-session window", FamilyRule.Values(("report", "2026-10-01"), ("session", "2026-10-01"), ("back", "1"), ("window", "3"))),
            new Gate(DriftRule.Beat, true, "the print beat its estimate", FamilyRule.Values(("surprise", "4.5"))),
            new Gate(DriftRule.Reaction, true, "the reaction session closed up 5.21 typical moves, at or above 0.5 up", FamilyRule.Values(("moves", "5.2065185076060505"), ("floor", "0.5"), ("typical move", "3.365012526970862"))),
            new Gate(DriftRule.Volume, false, "the reaction session's volume was 1.74 times its 50-session average, below 2.0", FamilyRule.Values(("multiple", "1.7359287717276313"), ("floor", "2"))),
            new Gate(DriftRule.Held, true, "the close of 190 is above the reaction session's low of 180", FamilyRule.Values(("close", "190"), ("low", "180"))),
            new Gate(FamilyRule.Trade, true, "the stop at the reaction's low", FamilyRule.Values(("close", "190"), ("stop", "180"), ("target", "215"), (FamilyRule.RewardToRiskValue, "2.5"))),
        ]);

        store.Execute(
            "INSERT INTO family_result (session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates) " +
            $"VALUES ('{TheSwitch}', 'D1', 'drift', 0, 1, NULL, '190', '180', '215', 4.5, '[]', '{drift}');");

        // The members' readings as a night stores them, holding none of a family's own: N1's others than its answer's, and
        // K3's sessions since its high.
        foreach (var (ticker, readings) in new[] { ("N1", ReadingsOf(("depth", 9.5), ("dry_up", 1.9), ("strength", 0.1))), ("K3", ReadingsOf(("since_high", 3))) })
        {
            store.Execute($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('GSPC', '{TheSwitch}', '{ticker}', '{EquityBrief.Worker.Ledger.SetupLedger.ReadingsText(readings)}', '{LedgerReadings.Version}');");
        }

        await RedrawTheFamilies(store);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        string Row(string family, string ticker) =>
            Assert.Single(Blocks(Assert.Single(Blocks(FamilyCardOf(page, family), "<details class=\"expander close-rows\".*?</details>")), $"<tr data-ticker=\"{ticker}\" data-gate=.*?</tr>"));

        string Words(string family, string ticker) =>
            Regex.Match(Assert.Single(Blocks(CloseCard(page), $"<tr data-ticker=\"{ticker}\" data-family=\"{family}\".*?</tr>")), "<td class=\"close-had\">([^<]*)</td>").Groups[1].Value;

        // Every row of each card's table, in its order.
        Assert.Equal(["N1|trade"], Regex.Matches(Assert.Single(Blocks(FamilyCardOf(page, SetupFamilies.Pullback), "<details class=\"expander close-rows\".*?</details>")), "<tr data-ticker=\"([^\"]+)\" data-gate=\"([^\"]+)\">").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}"));
        Assert.Equal(["K3|volume", "K5|volume"], Regex.Matches(Assert.Single(Blocks(FamilyCardOf(page, BreakoutRule.Name), "<details class=\"expander close-rows\".*?</details>")), "<tr data-ticker=\"([^\"]+)\" data-gate=\"([^\"]+)\">").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}"));
        Assert.Equal(["D1|volume"], Regex.Matches(Assert.Single(Blocks(FamilyCardOf(page, DriftRule.Name), "<details class=\"expander close-rows\".*?</details>")), "<tr data-ticker=\"([^\"]+)\" data-gate=\"([^\"]+)\">").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}"));

        // N1: its answer's three, not its readings', in their forms; the shared list's row its trade's reward of 1.2
        // against the version's floor of 1.5.
        Assert.Contains("<td class=\"r num\" data-number=\"depth\" data-value=\"2.3456\">2.35 moves</td><td class=\"r num\" data-number=\"dry_up\" data-value=\"0.8\">0.80 times</td><td class=\"r num\" data-number=\"strength\" data-value=\"0.734\">73%</td>", Row(SetupFamilies.Pullback, "N1"), StringComparison.Ordinal);
        Assert.Equal("the reward is 1.20 times the risk, needs 1.5", Words(SetupFamilies.Pullback, "N1"));

        // K3: its volume as the shared list words it, 1.20 times, its ranges at 0.70 and 3 sessions since its high.
        Assert.Contains("<td class=\"r num\" data-number=\"since_high\" data-value=\"3\">3</td><td class=\"r num\" data-number=\"volume_multiple\" data-value=\"1.2\">1.20 times</td><td class=\"r num\" data-number=\"range_ratio\" data-value=\"0.7\">0.70</td>", Row(BreakoutRule.Name, "K3"), StringComparison.Ordinal);
        Assert.Equal("volume 1.20 times its 50-day average, below 1.5", Words(BreakoutRule.Name, "K3"));

        // K5: no average, so its volume stored nothing, as the shared list says; no readings, so its high is not read.
        Assert.Contains("<td class=\"r num\" data-number=\"since_high\" data-value=\"none\" data-none=\"not read\"><span class=\"degraded\">none, not read</span></td><td class=\"r num\" data-number=\"volume_multiple\" data-value=\"none\" data-none=\"not stored\"><span class=\"degraded\">none, not stored</span></td><td class=\"r num\" data-number=\"range_ratio\" data-value=\"0.8\">0.80</td>", Row(BreakoutRule.Name, "K5"), StringComparison.Ordinal);
        Assert.Equal("not available: no 50-day average volume is stored for the night", Words(BreakoutRule.Name, "K5"));

        // D1: its reaction of 5.21 moves, its volume of 1.74 times as the shared list words it, and 1 session since the
        // report.
        Assert.Contains("<td class=\"r num\" data-number=\"reaction_moves\" data-value=\"5.2065185076060505\">5.21 moves</td><td class=\"r num\" data-number=\"volume_multiple\" data-value=\"1.7359287717276313\">1.74 times</td><td class=\"r num\" data-number=\"freshness\" data-value=\"1\">1</td>", Row(DriftRule.Name, "D1"), StringComparison.Ordinal);
        Assert.Equal("the report day's volume was 1.74 times its 50-day average, below 2.0", Words(DriftRule.Name, "D1"));

        // And no cell on any card's table draws none without why.
        Assert.DoesNotMatch("data-value=\"none\"(?! data-none=)", page);
    }

    // From the 19.3 correction of 2026-10-24: on the S&P 400, whose answers store their setup with no values, each card's
    // stocks one gate short draw a family's own numbers off the setup the ledger stored and every other off the member's
    // readings, read back off the page. Over the closed night of 2026-10-02 IA, IB, IC and ID break out and the market check
    // alone keeps them off: IA's setup stores its volume at 2.0 times and its ranges at 0.62 and its readings 4 sessions
    // since its high; IB's setup, which a history build wrote, its volume at 1.6 times and no ranges, its readings 2; IC
    // holds no setup, its readings 6; and ID neither. F1, F2 and F3's drift answers are short at the market check alone,
    // and their report days' setups are read at the provisional rule's window of 3: F1's on the night, none since; F2's on
    // Tuesday 2026-09-29, three sessions back and read; F3's on Monday 2026-09-28, four back and not.
    [Fact]
    public async Task OnAnIndexEachCardsStocksOneGateShortDrawTheLedgersSetupAndADriftItsReportDaysWithinItsWindow()
    {
        using var store = Checks.FixtureExpectations.ClosedIndexStore();
        var night = new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero);

        await new IndexFamilies(FixedClock.At(night, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("night-numbers", default, [], night);

        var market = Text(store, $"SELECT gates FROM index_family_result WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND family = 'breakout' AND ticker = 'IA';");
        var closed = FamilyRule.GatesOf(market).First(gate => gate.Name == EquityBrief.Core.Cards.RuleRows.Market);

        Assert.False(closed.Passed);

        var shortOfTheCheck = FamilyRule.GatesJson(
        [
            closed,
            new Gate(EquityBrief.Core.Cards.RuleRows.Setup, true, string.Empty, FamilyRule.Values()),
            new Gate(EquityBrief.Core.Cards.RuleRows.Floors, true, string.Empty, FamilyRule.Values()),
            new Gate(EquityBrief.Core.Cards.RuleRows.Profit, true, string.Empty, FamilyRule.Values()),
        ]);

        store.Execute($"UPDATE index_family_result SET gates = '{shortOfTheCheck}' WHERE index_code = 'MID' AND session_date = '{IndexNight}' AND family = 'drift' AND ticker IN ('F1', 'F2', 'F3');");

        void Setup(string family, string ticker, string session, string source, params (string Column, double Value)[] readings) =>
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin"
                + string.Concat(readings.Select(reading => ", " + reading.Column)) + ") "
                + $"VALUES ('MID', '{family}', '{ticker}', '{session}', 'the provisional rule', 0, '10', '9', 21, 'open', 0, '{source}', '{LedgerReadings.Version}'"
                + string.Concat(readings.Select(reading => ", " + reading.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture))) + ");");

        // IB's and F2's written by a history build run over their sessions, which the ledger keeps one a session as the
        // night's.
        Setup(BreakoutRule.Name, "IA", IndexNight, "night", ("volume_multiple", 2.0), ("range_ratio", 0.62));
        Setup(BreakoutRule.Name, "IB", IndexNight, "history", ("volume_multiple", 1.6));
        Setup(DriftRule.Name, "F1", IndexNight, "night", ("reaction_moves", 1.5), ("volume_multiple", 2.5), ("freshness", 0));
        Setup(DriftRule.Name, "F2", "2026-09-29", "history", ("reaction_moves", 2.25), ("volume_multiple", 3.0), ("freshness", 0));
        Setup(DriftRule.Name, "F3", "2026-09-28", "night", ("reaction_moves", 0.75), ("volume_multiple", 2.2), ("freshness", 0));

        foreach (var (ticker, high) in new[] { ("IA", 4.0), ("IB", 2.0), ("IC", 6.0) })
        {
            store.Execute($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('MID', '{IndexNight}', '{ticker}', '{EquityBrief.Worker.Ledger.SetupLedger.ReadingsText(ReadingsOf(("since_high", high)))}', '{LedgerReadings.Version}');");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{IndexNight}?universe=400"));

        // Each row of a card's table as its ticker and its three cells as drawn.
        IReadOnlyList<string> Rows(string family) =>
        [
            .. Regex.Matches(Assert.Single(Blocks(Section(page, family), "<details class=\"expander close-rows\".*?</details>")), "<tr data-ticker=\"([^\"]+)\" data-gate=\"[^\"]+\">(.*?)</tr>", RegexOptions.Singleline)
                .Select(match => match.Groups[1].Value + "|" + string.Join("|", Regex.Matches(match.Groups[2].Value, "<td class=\"r num\" data-number=\"[^\"]+\" data-value=\"[^\"]+\"(?: data-none=\"[^\"]+\")?>(.*?)</td>").Select(cell => Regex.Replace(cell.Groups[1].Value, "<[^>]+>", string.Empty)))),
        ];

        Assert.Equal(
            [
                "IA|4|2.00 times|0.62",
                "IB|2|1.60 times|none, not read",
                "IC|6|none, no setup stored|none, no setup stored",
                "ID|none, not read|none, no setup stored|none, no setup stored",
            ],
            Rows(BreakoutRule.Name));
        Assert.Equal(
            [
                "F1|1.50 moves|2.50 times|0",
                "F2|2.25 moves|3.00 times|3",
                "F3|none, no setup stored|none, no setup stored|none, no setup stored",
            ],
            Rows(DriftRule.Name));
    }

    // The sources the 19.3 correction of 2026-10-24 writes.
    static readonly string[] OneGateShortSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.CardNumbers.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Indices.cs",
        "src/EquityBrief.Core/Cards/CardNumbers.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Families.cs",
    ];

    // From the 19.3 correction of 2026-10-24: no source a rule's version pins is one the correction writes, so no rule's
    // version moves, the reader shown to find a source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheOneGateShortCorrectionWrites()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(OneGateShortSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(OneGateShortSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    [Fact]
    public void EachEmptySectorSaysWhyItsBookBoughtNothingThere()
    {
        var on = new DateOnly(2026, 10, 1);

        HeavyweightReadRow Row(string sector, double? lead, bool trend, double? beta, string? ticker = null) =>
            new(on, sector, 1, ticker ?? sector[..2].ToUpperInvariant() + "1", "CIK 1", 1000m, 0.2, 0.1, lead, trend, false, beta);

        IReadOnlyList<HeavyweightReadRow> read =
        [
            Row("Energy", 0, true, 1.2),
            Row("Energy", -0.01, true, 1.2, "EN2"),
            Row("Utilities", 0.02, false, 1.2),
            Row("Materials", 0.02, true, 0.99),
            Row("Industrials", 0.02, true, 1.0),
            Row("Real Estate", 0.02, true, null),
            Row("Financials", 0.33, false, 1.3, "FI1"),
            Row("Financials", 0.25, false, 1.4, "FI2"),
            Row("Financials", -0.02, true, 1.1, "FI3"),
            Row("Communication Services", 0.649, true, 0.49, "WB1"),
            Row("Communication Services", 0.4, true, 0.8, "WB2"),
            Row("Communication Services", 0.44, false, 1.34, "GO1"),
            Row("Consumer Staples", -0.017, true, 1.0, "CS1"),
            Row("Consumer Staples", -0.05, true, 1.0, "CS2"),
        ];

        // In words a reader needs no rule to follow, each naming the stock closest to being bought and the figure it missed
        // by, worked by hand: none beating the fund, the closest matching it or trailing it; one beating it out of an uptrend
        // and several; one in an uptrend moving less than the market and several, the strongest named; a beta not read;
        // and what the rebalance bought sold since, named where the book's holdings are handed in.
        Assert.Equal("Nothing held: none of its 2 largest companies beat XLE over the 251 sessions to the 2026-10-01 rebalance; the closest, EN1, matched it.", TonightScreen.EmptySectorReason("Energy", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: none of its 2 largest companies beat its members' average over the 251 sessions to the 2026-10-01 rebalance; the closest, EN1, matched it.", TonightScreen.EmptySectorReason("Energy", read, fundReturn: false, highBeta: true));
        Assert.Equal("Nothing held: none of its 2 largest companies beat XLP over the 251 sessions to the 2026-10-01 rebalance; the closest, CS1, trailed it by 1.7 points.", TonightScreen.EmptySectorReason("Consumer Staples", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: only UT1 beat XLU over the 251 sessions to the 2026-10-01 rebalance, by 2.0 points, and it was not in an uptrend, the close above the 50-day average and that above the 200-day, which the rule requires.", TonightScreen.EmptySectorReason("Utilities", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: 2 of its 3 largest beat XLF over the 251 sessions to the 2026-10-01 rebalance, led by FI1 by 33.0 points, but none was in an uptrend, the close above the 50-day average and that above the 200-day, which the rule requires.", TonightScreen.EmptySectorReason("Financials", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: MA1 beat XLB over the 251 sessions to the 2026-10-01 rebalance, by 2.0 points, and was in an uptrend, the close above the 50-day average and that above the 200-day, but its beta was 0.99, so it moves about 99% as much as the market, and the rule buys only stocks that move at least as much as the market, a beta of 1 or more.", TonightScreen.EmptySectorReason("Materials", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: 2 beat XLC over the 251 sessions to the 2026-10-01 rebalance and were in an uptrend, the close above the 50-day average and that above the 200-day, the strongest WB1 by 64.9 points, but its beta was 0.49, so it moves about 49% as much as the market, the others less, and the rule buys only stocks that move at least as much as the market, a beta of 1 or more.", TonightScreen.EmptySectorReason("Communication Services", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: RE1 beat XLRE over the 251 sessions to the 2026-10-01 rebalance, by 2.0 points, and was in an uptrend, the close above the 50-day average and that above the 200-day, but its beta could not be read, and the rule buys only stocks that move at least as much as the market, a beta of 1 or more.", TonightScreen.EmptySectorReason("Real Estate", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: what the 2026-10-01 rebalance bought here has been sold since.", TonightScreen.EmptySectorReason("Industrials", read, fundReturn: true, highBeta: true));
        Assert.Equal("Nothing held: what the 2026-10-01 rebalance bought here has been sold since.", TonightScreen.EmptySectorReason("Materials", read, fundReturn: true, highBeta: false));
        Assert.Equal(
            "Nothing held: the 2026-10-01 rebalance bought IN1, sold since on 2026-10-05. Next rebalance 2026-11-02.",
            TonightScreen.EmptySectorReason("Industrials", read, fundReturn: true, highBeta: true, holdings: [new HeavyweightHoldingRow("IN1", on, "Industrials", "IN One", 10m, new DateOnly(2026, 10, 5), 9m, "no longer leading", -0.1, 0.0)], next: new DateOnly(2026, 11, 2)));
        Assert.Equal("Nothing held: only UT1 beat XLU over the 126 sessions to the 2026-10-01 rebalance, by 2.0 points, and it was not in an uptrend, the close above the 50-day average and that above the 200-day, which the rule requires.", TonightScreen.EmptySectorReason("Utilities", read, fundReturn: true, highBeta: true, lookBack: 126));
        Assert.Equal("Its last rebalance read none of its companies.", TonightScreen.EmptySectorReason("Health Care", read, fundReturn: true, highBeta: true));
        Assert.Equal("No rebalance has been read yet.", TonightScreen.EmptySectorReason("Energy", [], fundReturn: true, highBeta: true));
        Assert.Equal("This book stores no reading for a sector it bought nothing in.", TonightScreen.StoresNoSectorReading);
    }

    [Fact]
    public void AHeavyweightsStandingTonightIsReadAtTheEdgesOfItsLeadAndItsAverage()
    {
        // A return of 25 per cent over the look-back: level with its sector's has lost its lead, a hair over it is still
        // leading.
        Assert.Equal(HeavyweightStandings.LostItsLead, HeavyweightStandings.Of(100, 80, 90, 0.25).Word);
        Assert.Equal(HeavyweightStandings.StillLeading, HeavyweightStandings.Of(100, 80, 90, 0.2499).Word);
        Assert.Equal(0.25, HeavyweightStandings.Of(100, 80, 90, 0.2499).Return!.Value, 12);

        // A close at its average is not under it; a close a cent under is, whatever its lead.
        Assert.Equal(HeavyweightStandings.StillLeading, HeavyweightStandings.Of(100, 80, 100, 0.1).Word);
        Assert.Equal(HeavyweightStandings.BelowItsAverage, HeavyweightStandings.Of(100, 80, 100.01, 0.1).Word);
        Assert.Equal(0.1 / 0.9, HeavyweightStandings.Of(100, 80, 90, 0.1).OverAverage!.Value, 12);

        // A close, the close a look-back before or the sector's return not held: not read.
        Assert.Equal(HeavyweightStandings.NotRead, HeavyweightStandings.Of(null, 80, 90, 0.1).Word);
        Assert.Equal(HeavyweightStandings.NotRead, HeavyweightStandings.Of(100, null, 90, 0.1).Word);
        Assert.Equal(HeavyweightStandings.NotRead, HeavyweightStandings.Of(100, 80, 90, null).Word);

        // A sector's members' mean leaves a member holding none out, and an equal share reads each return alike.
        Assert.Equal(0.2, HeavyweightStandings.MembersMean([0.1, null, 0.3])!.Value, 12);
        Assert.Null(HeavyweightStandings.MembersMean([null]));
        Assert.Equal(0.0, HeavyweightStandings.EqualWeighted([(110, 100), (45, 50), (null, 10)])!.Value, 12);
    }

    [Fact]
    public async Task AHoldingsCloseALookBackBeforeTheNightIsReadFromTheKeptBarsOnlyWhereNoRefetchHasWrittenItsYearSince()
    {
        // The store's year holds two sessions, 10-01 and 10-02, and the fetcher kept 09-30 on the night of 10-01; a look-back
        // of two sessions reaches 09-30. Written nightly, the kept close is read; with the year written again by a refetch
        // after the drop, it is not, the two standing on different bases.
        static async Task<StandingCloses> Read(string observed)
        {
            using var store = new TemporaryStore().Migrated();

            foreach (var (day, close, at) in new[] { ("2026-10-01", "102", observed), ("2026-10-02", "104", "2026-10-02T23:10:00Z") })
            {
                store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{day}', '{close}', '{close}', '{close}', '{close}', 1000, 'bulk', '{at}', '{close}');");
            }

            store.Execute("INSERT INTO kept_bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close, kept_on) VALUES ('AAA', '2026-09-30', '80', '80', '80', '80', 1000, 'bulk', '2026-09-30T23:10:00Z', '80', '2026-10-01');");

            var read = new ReadApi(store.DatabaseFile, EquityBrief.Core.Time.FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 50, 0, TimeSpan.Zero), EquityBrief.Core.Time.SessionZones.UnitedStates));

            return (await read.StockStandingClosesAsync(["AAA"], new DateOnly(2026, 10, 2), 2, new DateOnly(2026, 10, 1)))["AAA"];
        }

        Assert.Equal(new StandingCloses(104m, 80m, 102m), await Read("2026-10-01T23:10:00Z"));
        Assert.Equal(new StandingCloses(104m, 80m, 102m), await Read("2026-10-02T23:59:59Z"));
        Assert.Equal(new StandingCloses(104m, null, 102m), await Read("2026-10-03T00:00:01Z"));
    }

    [Fact]
    public void AHeavyweightsCardsTilesAndSectorsAreWorkedByHand()
    {
        // Two holdings: A in Energy at 110 tonight, 100 at the rebalance and 88 a look-back before; B in Utilities at 45,
        // 50 and 50. XLE at 52.5, 50 and 42; XLU at 20, 20 and 25.
        var night = new DateOnly(2026, 10, 2);
        var next = new DateOnly(2026, 11, 3);
        var card = new HeavyweightCardView(
            "Sector heavyweights", "Largest companies leading their sectors", "the rule", 251, new DateOnly(2026, 10, 1), next,
            [
                new HeavyweightHoldingCell("A", null, "Energy", new DateOnly(2026, 10, 1), 0.05, new DateOnly(2026, 10, 1), 110m, 100, false),
                new HeavyweightHoldingCell("B", null, "Utilities", new DateOnly(2026, 9, 1), 0.02, new DateOnly(2026, 9, 1), 45m, 46, true),
            ],
            ["A"], [], null);
        var stocks = new Dictionary<string, StandingCloses> { ["A"] = new(110m, 88m, 100m), ["B"] = new(45m, 50m, 50m) };
        var funds = new Dictionary<string, StandingCloses> { ["XLE"] = new(52.5m, 42m, 50m), ["XLU"] = new(20m, 25m, 20m) };
        double? Fund(string sector) => sector == "Energy" ? 0.25 : sector == "Utilities" ? -0.2 : null;

        var drawn = TonightScreen.WithStanding(card, night, stocks, funds, new Dictionary<string, double?> { ["A"] = 1.1, ["B"] = null }, Fund, _ => "none bought", largeFunds: false);
        var tiles = drawn.Tiles!;

        // Two holdings in two of the eleven sectors; the book up 10 and down 10 per cent since the rebalance, nothing in
        // all, and its funds up 5 and level, 2.5 per cent; the sessions to November's first rebalance on the calendar.
        Assert.Equal((2, 2, 11), (tiles.Holdings, tiles.Sectors, tiles.Of));
        Assert.Equal(0.0, tiles.BookSince!.Value, 12);
        Assert.Equal(0.025, tiles.FundsSince!.Value, 12);
        Assert.Equal(["XLE", "XLU"], tiles.Funds);
        Assert.Equal(EquityBrief.Core.Bars.ExchangeClosures.SessionsUntil(night, next), tiles.SessionsToNext);

        // A's return over the look-back 25 per cent against its fund's 25: lost its lead; its growth since bought 10 per
        // cent. B under its average of 46: below its 200-day, its growth not carried to the night.
        var a = drawn.Holdings.Single(holding => holding.Ticker == "A");
        var b = drawn.Holdings.Single(holding => holding.Ticker == "B");

        Assert.Equal(HeavyweightStandings.LostItsLead, a.Standing!.Word);
        Assert.Equal(0.1, a.SinceBought!.Value, 12);
        Assert.Equal(HeavyweightStandings.BelowItsAverage, b.Standing!.Word);
        Assert.Null(b.SinceBought);

        // The eleven sectors in order, each fund's return over the look-back, and a sector holding none saying why.
        Assert.Equal(EquityBrief.Core.Families.GicsSectors.Eleven, drawn.Sectors!.Select(sector => sector.Sector));
        Assert.Equal(0.25, drawn.Sectors!.Single(sector => sector.Sector == "Energy").FundReturn!.Value, 12);
        Assert.Equal(-0.2, drawn.Sectors!.Single(sector => sector.Sector == "Utilities").FundReturn!.Value, 12);
        Assert.Null(drawn.Sectors!.Single(sector => sector.Sector == "Energy").Reason);
        Assert.Equal("none bought", drawn.Sectors!.Single(sector => sector.Sector == "Materials").Reason);

        // On an index card the funds are named as the S&P 500's.
        Assert.Equal("XLE, the S&P 500's fund,", TonightScreen.WithStanding(card, night, stocks, funds, new Dictionary<string, double?>(), Fund, _ => null, largeFunds: true).Sectors!.Single(sector => sector.Sector == "Energy").Fund);

        // A design (b) book reads no standing of its own.
        Assert.All(TonightScreen.WithStanding(card, night, stocks, funds, new Dictionary<string, double?>(), Fund, _ => null, designB: true).Holdings, holding => Assert.Equal(HeavyweightStandings.NotForThisDesign, holding.Standing!.Word));
    }
}
