using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Loop;
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
        // (103 - 100) / 2 = 1.5. Its rule's own sale at 110 from the rule's own stop of 95: (110 - 100) / 5 = 2. Selling half at
        // a multiple, or on the time limit alone, names no one price for the whole and reads none.
        Assert.Equal(3, Order(twoDays.TakingAtRisk(3)).TradedReward(close));
        Assert.Equal(1.5, Order(twoDays with { Exit = PlanPoints.FirstBand }).TradedReward(close));
        Assert.Equal(2, Order(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.RuleStop, PlanPoints.RuleSale)).TradedReward(close));
        Assert.Null(Order(twoDays.Halving(2)).TradedReward(close));
        Assert.Null(Order(twoDays).TradedReward(close));

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

        // Stated only beside a plan selling the whole at one price, and over nought.
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(Stated((RuleHooks.RewardTradedParameter, 2))));
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(new Dictionary<string, double>(PlanHook.ParametersOf(twoDays.Halving(2)), StringComparer.Ordinal) { [RuleHooks.RewardTradedParameter] = 2 }));
        Assert.Throws<ArgumentException>(() => RuleHooks.Of(new Dictionary<string, double>(plan, StringComparer.Ordinal) { [RuleHooks.RewardTradedParameter] = 0 }));

        // The S&P 500 pullback's variants may state it with their time limit and cap; the dip's night reads none of it.
        Assert.Null(EquityBrief.Worker.Candidates.CandidateRegistrar.SwingHooksUnread(RuleHooks.Of(new Dictionary<string, double>(plan, StringComparer.Ordinal) { [RuleHooks.RewardTradedParameter] = 2, [RuleHooks.TimeLimitParameter] = 20, [RuleHooks.PerNightParameter] = 3 })));
        Assert.NotNull(EquityBrief.Worker.Candidates.CandidateRegistrar.SwingHooksUnread(RuleHooks.Of(Stated((RuleHooks.ExitParameter, 1)))));
        Assert.NotNull(EquityBrief.Worker.Candidates.CandidateRegistrar.DipHooksUnread(checking));
        Assert.Null(EquityBrief.Worker.Candidates.CandidateRegistrar.DipHooksUnread(RuleHooks.Of(plan)));
    }
}
