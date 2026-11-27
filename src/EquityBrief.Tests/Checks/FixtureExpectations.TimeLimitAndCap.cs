using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the January pin move: each price family's time limit and cap a night are two settings a rule's
// registration states, read off its parameters and refused outside their bounds, each its family's own where none is stated
// so a rule stating neither is held and listed as before; the S&P 500's families, the S&P 400's and 600's, the pullback's
// variants and the dip list and hold at them, worked by hand over constructed verdicts; a rule's walk the pullback's or the
// dip's own exit cannot hold at its time limit says so rather than walk it at its family's; and a survivor whose line names
// either registers them as its settings in place of queueing.
// see: Each price family's time limit and cap a night are registered settings, today's where a rule states none
public partial class FixtureExpectations
{
    // The January pin move's rows of section 17: the time limit and the cap a night, the reward check on the traded plan, the
    // buy range on a rule's plan and the scored years.
    internal static readonly string[] PinMoveClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The time limit and the cap a night"),
        CheckReach.Key(Scope.LimitsTable, "The reward check on the traded plan"),
        CheckReach.Key(Scope.LimitsTable, "The buy range on a rule's plan"),
        CheckReach.Key(Scope.LimitsTable, "The scored years"),
    ];

    static Dictionary<string, double> Stated(params (string Name, double Value)[] settings) =>
        settings.ToDictionary(setting => setting.Name, setting => setting.Value, StringComparer.Ordinal);

    [Fact]
    public void ARulesTimeLimitAndCapANightAreReadOffItsSettingsAndAreItsFamilysOwnWhereItStatesNone()
    {
        // None stated: the family's own, and the hooks are off.
        Assert.True(RuleHooks.Of(Stated()).IsOff);
        Assert.Equal(63, RuleHooks.Of(Stated()).HeldAtMost(63));
        Assert.Equal(5, RuleHooks.Of(Stated()).ListedAtMost(5));

        // Stated: 20 sessions and 3 a night, in the rule's words, and no book's.
        var stated = RuleHooks.Of(Stated((RuleHooks.TimeLimitParameter, 20), (RuleHooks.PerNightParameter, 3)));

        Assert.False(stated.IsOff);
        Assert.False(stated.AreABooks);
        Assert.Equal((20, 3), (stated.HeldAtMost(63), stated.ListedAtMost(5)));
        Assert.Equal("held at most 20 sessions; at most 3 a night", stated.Words());
        Assert.True(RuleHooks.IsHook(RuleHooks.TimeLimitParameter));
        Assert.True(RuleHooks.IsHook(RuleHooks.PerNightParameter));
        Assert.Empty(RuleHooks.Without(Stated((RuleHooks.TimeLimitParameter, 20), (RuleHooks.PerNightParameter, 3))));

        // Refused outside their bounds: a part of a session, none, past a market's year, or no pick a night.
        foreach (var refused in new[] { Stated((RuleHooks.TimeLimitParameter, 20.5)), Stated((RuleHooks.TimeLimitParameter, 0)), Stated((RuleHooks.TimeLimitParameter, 253)), Stated((RuleHooks.PerNightParameter, 0)), Stated((RuleHooks.PerNightParameter, 2.5)) })
        {
            Assert.Throws<ArgumentException>(() => RuleHooks.Of(refused));
        }

        Assert.Equal(252, RuleHooks.Of(Stated((RuleHooks.TimeLimitParameter, 252))).TimeLimit);
    }

    [Fact]
    public void TheSP500FamiliesTheIndexRulesThePullbacksVariantsAndTheDipListTheCapANightTheirRuleStates()
    {
        static (string, ShadowOutcome) Passing(string ticker, double order) =>
            (ticker, new ShadowOutcome("a rule", true, new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [FamilyRuleEvaluator.EntryValue] = "100",
                [FamilyRuleEvaluator.StopValue] = "95",
                [FamilyRuleEvaluator.TargetValue] = "110",
                [FamilyRuleEvaluator.OrderValue] = order.ToString(System.Globalization.CultureInfo.InvariantCulture),
                [FamilyRuleEvaluator.MoveValue] = "2",
            }));

        // Seven fire, A highest to G lowest. By hand: five a night keeps A to E; three keeps A to C; seven keeps all seven; a
        // stock held is passed over and the next taken in its place.
        (string, ShadowOutcome)[] night = [Passing("A", 7), Passing("B", 6), Passing("C", 5), Passing("D", 4), Passing("E", 3), Passing("F", 2), Passing("G", 1)];
        var nothingHeld = new HashSet<string>(StringComparer.Ordinal);

        Assert.Equal(["A", "B", "C", "D", "E"], FamilyRecorder.Keep(night, nothingHeld, RuleHooks.Of(Stated())).Select(one => one.Ticker));
        Assert.Equal(["A", "B", "C"], FamilyRecorder.Keep(night, nothingHeld, RuleHooks.Of(Stated((RuleHooks.PerNightParameter, 3)))).Select(one => one.Ticker));
        Assert.Equal(["A", "B", "C", "D", "E", "F", "G"], FamilyRecorder.Keep(night, nothingHeld, RuleHooks.Of(Stated((RuleHooks.PerNightParameter, 10)))).Select(one => one.Ticker));
        Assert.Equal(["A", "C", "D"], FamilyRecorder.Keep(night, new HashSet<string>(["B"], StringComparer.Ordinal), RuleHooks.Of(Stated((RuleHooks.PerNightParameter, 3)))).Select(one => one.Ticker));

        // The S&P 500 pullback's variant: three of the four with a stop below the buy at three a night, each held the time
        // limit it states, 20 sessions, where the family holds its own.
        (RuleCards.FilterRow, ShadowOutcome) Row(string ticker, double reward) => (FilterRowOf(ticker, 0.5, 1, 100m, 95m, 100m + (5m * (decimal)reward), reward), Fired());

        var pullbacks = RuleCards.KeepPullbackPicks([Row("W", 4), Row("X", 3), Row("Y", 2), Row("Z", 1)], nothingHeld, RuleHooks.Of(Stated((RuleHooks.PerNightParameter, 3), (RuleHooks.TimeLimitParameter, 20))));

        Assert.Equal(["W", "X", "Y"], pullbacks.Select(pick => pick.Ticker));
        Assert.All(pullbacks, pick => Assert.Equal(20, pick.Cap));
        Assert.All(RuleCards.KeepPullbackPicks([Row("W", 4)], nothingHeld), pick => Assert.Equal(SetupFamilies.Pullbacks.CapSessions, pick.Cap));

        // The dip: seven passing, two a night lists two and holds the rest back as past its cap, five as before.
        DipAnswer[] dips = [.. "ABCDEFG".Select(letter => new DipAnswer(letter.ToString(), [], 100m, 95m, 5, 10, null))];
        var noneOpen = new Dictionary<string, (HeldTrade Trade, string Index)>(StringComparer.Ordinal);
        var noneListed = new Dictionary<string, string>(StringComparer.Ordinal);

        Assert.Equal(2, DipFamily.Draw(dips, noneOpen, noneListed, 2).Count(pick => pick.State == FamilyList.Listed));
        Assert.Equal(5, DipFamily.Draw(dips, noneOpen, noneListed, 2).Count(pick => pick.State == FamilyList.PastFive));
        Assert.Equal(5, DipFamily.Draw(dips, noneOpen, noneListed).Count(pick => pick.State == FamilyList.Listed));

        // The S&P 400's and 600's candidates in their own places, the cap a night read where the night takes them.
        IndexAnswer Answer(string ticker, int place) => new(ticker, BreakoutRule.Name, true, place, 100m, 95m, null, 5m, 63, null, null);

        var candidates = IndexRuleTrades.Candidates(RuleHooks.Of(Stated()), BreakoutRule.Name, [Answer("Q", 2), Answer("P", 1), Answer("R", 3)], nothingHeld, null, null, null);

        Assert.Equal(["P", "Q"], candidates.Take(RuleHooks.Of(Stated((RuleHooks.PerNightParameter, 2))).ListedAtMost(IndexRules.PerNight)).Select(one => one.Ticker));
    }

    [Fact]
    public void AnIndexRuleStatesItsTimeLimitOnceAndAWalkThatCannotHoldItSaysSo()
    {
        // An S&P 400 breakout states its time limit as its hold or as the time limit, never both.
        var breakout = IndexEvaluator("breakout-400");
        var provisional = IndexRules.Provisional(breakout);

        Assert.Null(IndexRules.Refusal(breakout, new Dictionary<string, double>(provisional, StringComparer.Ordinal) { [RuleHooks.TimeLimitParameter] = 20 }));
        Assert.Null(IndexRules.Refusal(breakout, new Dictionary<string, double>(provisional, StringComparer.Ordinal) { [IndexRuleCandidate.HoldParameter] = 21 }));
        Assert.Contains("a rule states its time limit once", IndexRules.Refusal(breakout, new Dictionary<string, double>(provisional, StringComparer.Ordinal) { [IndexRuleCandidate.HoldParameter] = 21, [RuleHooks.TimeLimitParameter] = 20 }), StringComparison.Ordinal);

        // The pullback's and the dip's own exits read outcomes worked at the family's own limit: a rule stating another time
        // limit and no plan is not walked, one stating a plan is, and the breakout's and the drift's walks hold any.
        var plan = PlanHook.ParametersOf(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.TypicalStop(2), PlanPoints.TimeLimit));
        var limited = Stated((RuleHooks.TimeLimitParameter, 20));
        var limitedUnderAPlan = new Dictionary<string, double>(plan, StringComparer.Ordinal) { [RuleHooks.TimeLimitParameter] = 20 };

        Assert.Contains("a time limit of 20 sessions and no plan", RuleWalk.CannotHold(SetupFamilies.Pullback, limited), StringComparison.Ordinal);
        Assert.NotNull(RuleWalk.CannotHold(DipRule.Name, limited));
        Assert.Null(RuleWalk.CannotHold(SetupFamilies.Pullback, limitedUnderAPlan));
        Assert.Null(RuleWalk.CannotHold(BreakoutRule.Name, limited));
        Assert.Null(RuleWalk.CannotHold(DriftRule.Name, limited));
        Assert.Null(RuleWalk.CannotHold(SetupFamilies.Pullback, Stated((RuleHooks.PerNightParameter, 3))));
    }

    [Fact]
    public void ASurvivorsVariantRegistersTheTimeLimitAndTheCapANightItsLineNames()
    {
        var cuts = new Dictionary<string, double[]> { ["volume_ratio"] = [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.2, 2.0, 3.0] };
        var named = Survivor(Runs.Replace("sell after 63", "sell after 20", StringComparison.Ordinal).Replace("cap 5", "cap 3", StringComparison.Ordinal));

        // The running revision holds 63 and lists five: the line's 20 and 3 are written.
        var settings = Survivors.Parameters(named, Stated(), cuts);

        Assert.Equal((20.0, 3.0), (settings[RuleHooks.TimeLimitParameter], settings[RuleHooks.PerNightParameter]));

        // A line naming the family's own writes neither.
        var own = Survivors.Parameters(Survivor(Runs), Stated(), cuts);

        Assert.False(own.ContainsKey(RuleHooks.TimeLimitParameter));
        Assert.False(own.ContainsKey(RuleHooks.PerNightParameter));

        // An S&P 400 or 600 revision holding 21 sessions: the line's 63, the family's own, differs from what its variant
        // would hold, so it is written and the hold set to nought, the rule stating its time limit once.
        var fromHold = Survivors.Parameters(Survivor(Runs), Stated((IndexRuleCandidate.HoldParameter, 21)), cuts);

        Assert.Equal(21, Survivors.RunningTimeLimit(Survivor(Runs), Stated((IndexRuleCandidate.HoldParameter, 21))));
        Assert.Equal((63.0, 0.0), (fromHold[RuleHooks.TimeLimitParameter], fromHold[IndexRuleCandidate.HoldParameter]));

        // The dip's limit dial is the time limit its variant holds where its revision states no other.
        var dip = Survivor("event dip(500) · buy next open · stop 2 typical days · take profit whole at 2 risks · sell after 10 sessions · cap 5 a night by rsi lowest");

        Assert.Equal(10, Survivors.RunningTimeLimit(dip, Stated((DipCandidate.LimitParameter, 10))));
        Assert.False(Survivors.Parameters(dip, Stated((DipCandidate.LimitParameter, 10)), cuts).ContainsKey(RuleHooks.TimeLimitParameter));
    }

    // see: The reward check on the traded plan rides the first pin move it is ready for and registers in the background once it lands
    [Fact]
    public void TheRewardCheckOnTheTradedPlanReadsTheOrdersOwnStopAndTheOnePriceItSellsTheWholeAt()
    {
        // An order at the next open on a close of 100, its typical day 1, the rule's own stop 95 and target 110, a band above
        // at 103. By hand, from the close: the stop 2 typical days under, 98, a risk of 2.
        var close = 100.0;
        var levels = new PlanLevels([], [], [103.0]);
        var ruleSale = new PlanSale(110, null, []);
        var twoDays = new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.TypicalStop(2), PlanPoints.TimeLimit);
        PlanOrder Order(PlanCombination combination) => PlanHook.OrderOf(combination, close, levels, null, 1, 95, ruleSale, 63)!;

        // Taking profit on the whole at 3 times the risk: 106, (106 - 100) / 2 = 3. Selling the whole at the first band: 103,
        // (103 - 100) / 2 = 1.5. Its rule's own sale at 110 from the rule's own stop of 95: (110 - 100) / 5 = 2. The time limit
        // alone names no price and reads none.
        Assert.Equal(3, Order(twoDays.TakingAtRisk(3)).TradedReward(close));
        Assert.Equal(1.5, Order(twoDays with { Exit = PlanPoints.FirstBand }).TradedReward(close));
        Assert.Equal(2, Order(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale)).TradedReward(close));
        Assert.Null(Order(twoDays).TradedReward(close));

        // Selling half first reads its first take-profit, the lowest price a close sells at above the buy. Half at 2 times the
        // risk on the time limit alone: 104, (104 - 100) / 2 = 2. Half at 1.5 times, 103, with the first band at 103 too: 1.5.
        // Half at 2 times beside the rule's own target of 110, from the rule's stop of 95: the half at 100 + 2 x 5 = 110 and the
        // target at 110, (110 - 100) / 5 = 2. Half at 3 times, 106, with the first band at 103 under it: the band sells the
        // whole first, (103 - 100) / 2 = 1.5. Half at 3 times from the rule's own stop, 115, the rule's target of 110 under it:
        // (110 - 100) / 5 = 2.
        Assert.Equal(2, Order(twoDays.Halving(2)).TradedReward(close));
        Assert.Equal(1.5, Order((twoDays with { Exit = PlanPoints.FirstBand }).Halving(1.5)).TradedReward(close));
        Assert.Equal(2, Order(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale).Halving(2)).TradedReward(close));
        Assert.Equal(1.5, Order((twoDays with { Exit = PlanPoints.FirstBand }).Halving(3)).TradedReward(close));
        Assert.Equal(2, Order(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale).Halving(3)).TradedReward(close));

        // At 2 times its risk the check passes the 3 and the 2 and fails the 1.5 and the plan naming no price; stated off, every
        // order passes, as the family's own check on the setup's stop and target reads.
        var plan = PlanHook.ParametersOf(twoDays.TakingAtRisk(3));
        var checking = RuleHooks.Of(new Dictionary<string, double>(plan, StringComparer.Ordinal) { [RuleHooks.RewardTradedParameter] = 2 });

        Assert.True(checking.TradedPasses(Order(twoDays.TakingAtRisk(3)), close));
        Assert.True(checking.TradedPasses(Order(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale)), close));
        Assert.False(checking.TradedPasses(Order(twoDays with { Exit = PlanPoints.FirstBand }), close));
        Assert.False(checking.TradedPasses(Order(twoDays), close));
        Assert.False(checking.TradedPasses(null, close));
        Assert.True(RuleHooks.Of(plan).TradedPasses(Order(twoDays), close));
        Assert.Equal("the reward check on the traded plan's own stop and take-profit, at 2 times its risk", checking.Words()!.Split("; ")[^1]);

        // Stated only beside a plan, one selling half first among them, and over nought. Every order of a plan selling half at
        // 1.5 times its risk reads 1.5 at most, so at 2 times the check refuses every one of them.
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(Stated((RuleHooks.RewardTradedParameter, 2))));
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(new Dictionary<string, double>(plan, StringComparer.Ordinal) { [RuleHooks.RewardTradedParameter] = 0 }));
        var halving = RuleHooks.Of(new Dictionary<string, double>(PlanHook.ParametersOf(twoDays.Halving(1.5)), StringComparer.Ordinal) { [RuleHooks.RewardTradedParameter] = 2 });

        Assert.False(halving.TradedPasses(Order(twoDays.Halving(1.5)), close));
        Assert.True(halving.TradedPasses(Order(twoDays.Halving(2)), close));

        // The S&P 500 pullback's variants may state it with their time limit and cap; the dip's night reads none of it.
        Assert.Null(EquityBrief.Worker.Candidates.CandidateRegistrar.SwingHooksUnread(RuleHooks.Of(new Dictionary<string, double>(plan, StringComparer.Ordinal) { [RuleHooks.RewardTradedParameter] = 2, [RuleHooks.TimeLimitParameter] = 20, [RuleHooks.PerNightParameter] = 3 })));
        Assert.NotNull(EquityBrief.Worker.Candidates.CandidateRegistrar.SwingHooksUnread(RuleHooks.Of(Stated((RuleHooks.ExitParameter, 1)))));
        Assert.NotNull(EquityBrief.Worker.Candidates.CandidateRegistrar.DipHooksUnread(checking));
        Assert.Null(EquityBrief.Worker.Candidates.CandidateRegistrar.DipHooksUnread(RuleHooks.Of(plan)));
    }

    // see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
    [Fact]
    public void APlanBuyingAtTheNextOpenInARangeIsStatedPlacedAndFilledAsTheRangeFillsIt()
    {
        // The provisional widths, half a typical day above and two under, stated alone on the rule's own plan: a plan of its own
        // and not the rule's, its words naming the range and read back, its parameters written back.
        var stated = Stated((PlanHook.RangeCeilingParameter, 0.5), (PlanHook.RangeFloorParameter, 2));
        var ranged = PlanHook.Of(stated)!;

        Assert.Equal(new RangeWidths(0.5, 2), ranged.Range);
        Assert.Equal((PlanPoints.NextOpen, PlanPoints.RuleStop, PlanPoints.RuleSale), (ranged.Entry, ranged.Stop, ranged.Exit));
        Assert.EndsWith("; bought in its range: a ceiling 0.5 typical days above the last close and a floor 2 typical days under it", ranged.Words, StringComparison.Ordinal);
        Assert.Equal(ranged, PlanPrices.Named(ranged.Words));
        Assert.Equal(ranged, PlanHook.Of(PlanHook.ParametersOf(ranged)));
        Assert.Null(PlanHook.Of(Stated((PlanHook.RangeCeilingParameter, 0))));
        Assert.Null(PlanHook.Of(Stated((PlanHook.RangeCeilingParameter, 1)))!.Range!.Value.Floor);

        // Refused off the widths the test walks, and on a buy at a level.
        Assert.Throws<ArgumentException>(() => PlanHook.Of(Stated((PlanHook.RangeCeilingParameter, 0.7))));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(Stated((PlanHook.RangeCeilingParameter, 0.5), (PlanHook.RangeFloorParameter, 3))));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(Stated((PlanHook.RangeFloorParameter, 2))));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(Stated((PlanHook.EntryParameter, 1), (PlanHook.WindowParameter, 3), (PlanHook.RangeCeilingParameter, 0.5))));

        // Placed on a close of 100 with a typical day of 2: by hand a ceiling of 101 and a floor of 96, waiting the two sessions
        // its order at the ceiling stands; the same plan with no range buys at whatever price it opens and waits none.
        var order = PlanHook.OrderOf(ranged with { Stop = PlanPoints.TypicalStop(2) }, 100, new PlanLevels([], [], []), null, 2, 95, new PlanSale(110, null, []), 63)!;

        Assert.Equal(new BuyRange(101, 96), order.Range);
        Assert.Equal(BuyRanges.OrderSessions, order.Window);
        Assert.Null(PlanHook.OrderOf(ranged with { Range = null }, 100, new PlanLevels([], [], []), null, 2, 95, new PlanSale(110, null, []), 63)!.Range);

        // Walked from the night's bar 0. An open of 100.5 inside the range buys there, its stop 2 typical days under at 96.5. An
        // open of 102 over the ceiling and a low of 100.8 that session fills at the ceiling, 101. An open of 95 under the floor
        // buys nothing and waits for nothing. An open of 103 and 104 with lows of 102 and 103 never comes back: not bought. A
        // night with no session after it waits.
        PlanTradeState Walked(double[] opens, double[] lows) => PlanHook.Walk(order, opens, lows, opens, 0);

        Assert.Equal((false, (int?)1, (double?)100.5, (double?)96.5), Of(Walked([100, 100.5, 100.5], [100, 100, 100])));
        Assert.Equal(((int?)1, (double?)101), (Walked([100, 102, 102], [100, 100.8, 101]).FillBar, Walked([100, 102, 102], [100, 100.8, 101]).Fill));
        Assert.True(Walked([100, 95, 95], [100, 94, 94]).Unfilled);
        Assert.True(Walked([100, 103, 104, 105], [100, 102, 103, 104]).Unfilled);
        Assert.True(Walked([100], [100]).Waiting);

        static (bool, int?, double?, double?) Of(PlanTradeState state) => (state.Waiting, state.FillBar, state.Fill, state.Stop);
    }

    // see: An order at a level buys nothing on an open two typical days under its price in a variant of its own, and a plan selling half first is checked at its first take-profit
    [Fact]
    public void AnOrderAtALevelWithAFloorBuysNothingOnAnOpenMoreThanItsTypicalDaysUnderItsPrice()
    {
        // The provisional floor, two typical days, stated alone on a buy 1 typical day under the close within 5 sessions: a plan of
        // its own, its words naming the floor and read back, its parameters written back.
        var level = Stated((PlanHook.EntryParameter, PlanHook.Distance), (PlanHook.DistanceParameter, 1), (PlanHook.WindowParameter, 5), (PlanHook.StopParameter, 2), (PlanHook.RangeFloorParameter, 2));
        var floored = PlanHook.Of(level)!;

        Assert.Equal(2, floored.LevelFloor);
        Assert.Null(floored.Range);
        Assert.EndsWith("; bought above its floor: not bought on an open more than 2 typical days under its price", floored.Words, StringComparison.Ordinal);
        Assert.Equal(floored, PlanPrices.Named(floored.Words));
        Assert.Equal(floored, PlanHook.Of(PlanHook.ParametersOf(floored)));
        Assert.Equal(PlanPoints.Changes(floored with { LevelFloor = null }) + 1, PlanPoints.Changes(floored));

        // Refused off the floors the range's test walks, and on the first pullback, which buys at an open with no price.
        Assert.Throws<ArgumentException>(() => PlanHook.Of(new Dictionary<string, double>(level, StringComparer.Ordinal) { [PlanHook.RangeFloorParameter] = 3 }));
        Assert.Throws<ArgumentException>(() => PlanHook.Of(Stated((PlanHook.EntryParameter, PlanHook.FirstPullback), (PlanHook.WindowParameter, PlanPoints.FirstPullbackSessions), (PlanHook.RangeFloorParameter, 2))));

        // Placed on a close of 100 with a typical day of 2: by hand the level 98 and the floor 98 - 2 x 2 = 94.
        var order = PlanHook.OrderOf(floored, 100, new PlanLevels([], [], []), null, 2, 90, new PlanSale(110, null, []), 63)!;

        Assert.Equal(98, order.Level);
        Assert.Equal(94, order.LevelFloor);
        Assert.Null(PlanHook.OrderOf(floored with { LevelFloor = null }, 100, new PlanLevels([], [], []), null, 2, 90, new PlanSale(110, null, []), 63)!.LevelFloor);

        // Walked from the night's bar 0. An open of 95 under the level and above the floor buys there. An open of 93 under the
        // floor buys nothing, where the same order with no floor buys at 93. An open of 99 with a low of 97 fills at the level,
        // 98. Opens over the level that never trade down to it within the window: not bought, as before.
        PlanTradeState Walked(PlanOrder walked, double[] opens, double[] lows) => PlanHook.Walk(walked, opens, lows, opens, 0);
        var plain = order with { LevelFloor = null };

        Assert.Equal(((int?)1, (double?)95), (Walked(order, [100, 95, 95], [100, 95, 95]).FillBar, Walked(order, [100, 95, 95], [100, 95, 95]).Fill));
        Assert.True(Walked(order, [100, 93, 93], [100, 92, 92]).Unfilled);
        Assert.False(Walked(order, [100, 93], [100, 92]).Waiting);
        Assert.Equal((double?)93, Walked(plain, [100, 93, 93], [100, 92, 92]).Fill);
        Assert.Equal((double?)98, Walked(order, [100, 99, 99], [100, 97, 97]).Fill);
        Assert.True(Walked(order, [100, 101, 101, 101, 101, 101, 101], [100, 99, 99, 99, 99, 99, 99]).Unfilled);
    }
}
