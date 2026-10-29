using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.4: the dip's rules read the hooks as the swing rules do, each rule's hooks read off its
// registration beside its dials with the sell dial named apart from the hooks' exit, a registration from before 21.4
// naming the dial exit read as the same setting with no hook, and a rule's ranked answers kept and ordered by its hooks
// over a constructed night while a rule stating none lists exactly as before.
// see: The engines' hooks are eight forms a registration states and all default off, and every source a rule pins moves only in a checkpoint that registers every rule reading it again
public partial class FixtureExpectations
{
    [Fact]
    public void ADipRuleReadsItsHooksBesideItsDialsAndARegistrationFromBeforeNamesItsSellDialExitAndNoHook()
    {
        var at = new DateTimeOffset(2026, 10, 29, 12, 0, 0, TimeSpan.Zero);
        var evaluator = new DipCandidate(MarketCloses.Index);

        RegisterRow Row(long id, string candidate, IReadOnlyDictionary<string, double> parameters) =>
            new(id, candidate, "rule", "test", evaluator.Name, CandidateEvaluator.Write(parameters), evaluator.Version, "registered", null, at, null);

        var dials = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [DipCandidate.DropParameter] = -3,
            [DipCandidate.TrendParameter] = 50,
            [DipCandidate.SellParameter] = DipRule.RsiExit,
            [DipCandidate.LimitParameter] = 10,
            [DipCandidate.StopParameter] = 3,
            [DipCandidate.MarketParameter] = 6,
            [DipCandidate.VolumeParameter] = 0,
        };
        var before = new Dictionary<string, double>(dials, StringComparer.Ordinal);

        before.Remove(DipCandidate.SellParameter);
        before[DipCandidate.SellParameterBefore] = DipRule.RsiExit;

        var hooked = new Dictionary<string, double>(dials, StringComparer.Ordinal) { ["also_close_over_long_above"] = 1, ["exposure"] = 0.45, ["exit"] = 2 };
        var rules = DipFamily.RulesOn(MarketCloses.Index, [Row(1, FamilyRecords.LivePrefix + "the S&P 500 dip rule", dials), Row(2, "the S&P 500 dip rule from before", before), Row(3, "the S&P 500 dip rule over its long average", hooked)], at.AddHours(1));

        // Three rules at one setting, the live rule first: the two stating no hook read as off, the third's conditions, its
        // exposure and the menu's second exit read beside the dials, and the registration from before reads the same sell.
        Assert.Equal(3, rules.Count);
        Assert.All(rules, rule => Assert.Equal(rules[0].Setting, rule.Setting));
        Assert.Equal((true, true, false), (rules[0].Hooks.IsOff, rules[1].Hooks.IsOff, rules[2].Hooks.IsOff));
        Assert.Equal(DipRule.RsiExit, rules[1].Setting.ExitLevel);
        Assert.Equal((0.45, 2, "close_over_long"), (rules[2].Hooks.Exposure, rules[2].Hooks.Exit, rules[2].Hooks.Also.Single().Column));
        Assert.Equal(ExitKind.BreakEven, rules[2].Hooks.ExitChoice!.Kind);

        // Over a constructed night of six passing answers in the dip's own order, the hooked rule keeps those at or above
        // their long average, the exposure's two picks where one switch holds, and the rule stating none keeps all six.
        static DipAnswer Answer(string ticker, double rsi) => new(ticker, [], 100m, 95m, 10, rsi, null);

        DipAnswer[] answers = [Answer("A", 5), Answer("B", 8), Answer("C", 9), Answer("D", 12), Answer("E", 15), Answer("F", 18)];
        var column = LedgerReadings.IndexOf("close_over_long");

        double?[] Readings(string ticker)
        {
            var readings = NightReadings(0.5, 1.2);

            readings[column] = ticker is "B" or "E" ? 0.9 : 1.1;

            return readings;
        }

        Assert.Equal(["A", "B", "C", "D", "E", "F"], rules[0].Hooks.Order(DipRule.Ranked(answers), answer => Readings(answer.Ticker)).Select(answer => answer.Ticker));
        Assert.Equal(["A", "C"], rules[2].Hooks.Order(DipRule.Ranked(answers), answer => Readings(answer.Ticker)).Select(answer => answer.Ticker));
        Assert.Equal(["A", "C", "D", "F"], RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_close_over_long_above"] = 1 }).Order(DipRule.Ranked(answers), answer => Readings(answer.Ticker)).Select(answer => answer.Ticker));

        // The exposure's answer in the market setting's place: two picks where the breadth holds and the VIX stands over
        // its median, and a closed gate where neither holds.
        Assert.True(IndexNightRead.ExposureGate(rules[2].Hooks, 0.45, NightReadings(0.5, 1.2)).Passed);
        Assert.Contains("allows 2 pick(s)", IndexNightRead.ExposureGate(rules[2].Hooks, 0.45, NightReadings(0.5, 1.2)).Reason, StringComparison.Ordinal);
        Assert.False(IndexNightRead.ExposureGate(rules[2].Hooks, 0.45, NightReadings(0.3, 1.2)).Passed);
    }
}
