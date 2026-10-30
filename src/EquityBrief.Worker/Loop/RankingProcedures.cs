using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;

namespace EquityBrief.Worker.Loop;

// From 21.8 a family's ranking variant as a proposal the tester judges as the learned score's: in each fold's year the
// family's setups at the ledger's loose gates ranked by the order the audit's fifths choose, the score fitted inside the
// fold or the expected value of the setups like each matched among what ended before the fold's year, the best five taken
// a night, against the rule's own walk; counted in the family's correction like every proposal. An approval registers
// the change only where the night's step lists by that order: the score on every index's breakout, drift and dip and the
// S&P 400's and 600's pullback, and the expected value on the S&P 500's breakout and drift alone; elsewhere the proposal
// is read and put to no one.
// see: A family's ranking variant orders by the learned score where its audit's top fifth stands above the bottom and otherwise by the expected value, and is built nowhere neither orders
public static class RankingProcedures
{
    public static string ProposalName(RankingOrder order) => RankingChoice.Proposal + " by " + RankingChoice.Words(order);

    // Whether the night's step on the index lists a family's rule ranked by the order.
    public static bool Approvable(string index, string family, RankingOrder order) => order switch
    {
        RankingOrder.Score => !(index == WalkForwardTester.LargeIndex && family == SetupFamilies.Pullback),
        RankingOrder.Expected => index == WalkForwardTester.LargeIndex && family is BreakoutRule.Name or DriftRule.Name,
        _ => false,
    };

    // The words a change of the order states, as the hooks say it.
    public static string ChangeOf(RankingOrder order) => RankingWalk.Ranking(order).Words() ?? string.Empty;

    public static IReadOnlyList<LoopProposalRead> Run(LoopRead read, EngineRule rule, RankingWalk walk, IReadOnlyList<FittedScore> fitted, LikePool like, Action<string> progress)
    {
        var folds = fitted
            .Where(one => one.Family == rule.Family && one.Model is RidgeModel && read.Folds.Any(fold => fold.Year == one.Year))
            .ToDictionary(one => one.Year, one => (RidgeModel)one.Model);
        IScoreModel? ScoreOf(int year) => folds.GetValueOrDefault(year);
        var pool = like.With(folds.ToDictionary(pair => (rule.Family, pair.Key), pair => pair.Value));
        var (order, score, expected) = walk.Choose(ScoreOf, walk.ExpectedOf(pool));

        progress(ScoreAudit.RankingLine(rule.Family, order, score, expected));

        if (order == RankingOrder.None)
        {
            return [];
        }

        var places = read.Calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var walked = walk.Folds(order, read.Folds, places, ScoreOf, walk.ExpectedOf(pool));
        var change = ChangeOf(order);
        var approvable = Approvable(read.Index, rule.Family, order);
        var finding = ScoreAudit.RankingLine(rule.Family, order, score, expected)
            + (approvable ? string.Empty : "; an approval cannot register it here, since the night's step on this index does not list a rule by this order");

        return
        [
            new LoopProposalRead(
                rule.Family,
                ProposalName(order),
                change,
                rule.Current,
                rule.Unit,
                rule.Cap,
                [.. read.Folds.Select((fold, at) => (fold, walked[at] is null ? null : change))],
                walked.Count(one => one is not null),
                rule.Evidence(read.Folds, read.Calendar, walked, rule.Own))
            {
                Finding = finding,
                Change = approvable ? LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.RankingParameter] = RankingChoice.HookOf(order)!.Value }) : null,
                Reference = rule.Reference,
            },
        ];
    }
}
