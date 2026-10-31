using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// From 21.9 a breakout's or a drift's graded exposure as a variant: the live rule with the exposure hook in its market
// check's place, five picks a night where the index's breadth stands at or above the floor the rule's check reads and the
// VIX at or under its median, two where one holds and none where neither. The floor is the rule's own: the S&P 500's
// families read the breadth at the market floor their sweep reads, and an S&P 400 or 600 rule at the floor it states on
// its own index's breadth, the S&P 500's own floor where it reads the S&P 500's breadth, and none where it reads no
// market check, which leaves nothing to grade. The tester proposes it against the rule's own walk, its listings read
// with the market check open; it fits nothing, so every fold walks it as it stands.
// see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
public static class ExposureProcedures
{
    public const string Proposal = ExposureVariant.Proposal;

    // The families whose walk reads the exposure, from 21.16 the pullback and the dip beside the breakout and the drift.
    // see: The pullback's and the dip's graded exposure stand in their market check's place as a variant, walked as the breakout's and the drift's are
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, DipRule.Name];

    // The breadth floor a rule's market check reads, which its exposure grades at; none where it reads no check, and for a
    // dip none where its market setting reads no breadth, its fund's trend or the VIX.
    public static double? FloorOf(CandidateEvaluator? evaluator, IReadOnlyDictionary<string, double> p) => evaluator switch
    {
        DipCandidate dip => DipRule.MarketLevels(dip.Index).ElementAtOrDefault((int)p.GetValueOrDefault(DipCandidate.MarketParameter)) is { Reads: DipRule.ReadsOwnBreadth or DipRule.ReadsLargeBreadth } level
            ? level.Floor
            : null,
        IndexRuleCandidate when p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter) == IndexRuleCandidate.NoMarketCheck => null,
        IndexRuleCandidate when p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter) == 1 => IndexRuleCandidate.LargeBreadthFloor,
        IndexRuleCandidate => p.TryGetValue(IndexRuleCandidate.BreadthFloorParameter, out var floor) && floor > 0 ? floor : FamilySweep.MarketFloor,
        FamilyRuleEvaluator => FamilySweep.MarketFloor,
        _ => null,
    };

    // A rule's parameters with the exposure at a floor.
    public static IReadOnlyDictionary<string, double> Exposed(IReadOnlyDictionary<string, double> p, double floor) =>
        new Dictionary<string, double>(p, StringComparer.Ordinal) { [RuleHooks.ExposureParameter] = floor };

    // The exposure's words at a floor, as the hooks say them.
    public static string WordsOf(double floor) =>
        RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExposureParameter] = floor }).Words() ?? string.Empty;

    // The proposal: the exposed walk in every fold's year against the rule's own.
    public static LoopProposalRead Run(LoopRead read, EngineRule rule, IReadOnlyList<(int Entry, double? Edge)> exposed, double floor)
    {
        var words = WordsOf(floor);

        return new LoopProposalRead(
            rule.Family,
            Proposal,
            words,
            rule.Current,
            rule.Unit,
            rule.Cap,
            [.. read.Folds.Select(fold => (fold, (string?)words))],
            read.Folds.Count,
            rule.Evidence(read.Folds, read.Calendar, [.. read.Folds.Select(_ => exposed)], rule.Own))
        {
            Finding = FormattableString.Invariant($"the {rule.Family}'s exposure graded at the breadth floor its market check reads, {floor:0.00}"),
            Change = LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExposureParameter] = floor }),
            Reference = rule.Reference,
        };
    }
}
