using EquityBrief.Core.Loop;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Worker.Loop;

// One score a run fitted: the family, the year its fold tests or, for the score fitted on all finished data, the year
// after the history's, the first session of the setups it learned on and the session it was cut at, and the model.
public sealed record FittedScore(string Family, int Year, DateOnly LearnedFrom, DateOnly LearnedBefore, RidgeModel Model);

// The learned score as the tester runs it inside each fold, for each family a rule's hooks reach on an index: the
// ledger's finished setups of the family on all three indices, each fold fitting the score on those whose path ended
// before its year began, and three proposals, the rule's list ordered by the score, and ordered with the lowest fifth or
// the lowest two fifths of the scores the fold learned on on the index left off, each walked with the rule over the
// fold's year through the hooks a registration would set, against the rule as it stands.
// see: A fitted statistical model is a rule
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public static class ScoreProcedures
{
    // Each proposal's share of the scores left off, none for the list ordered alone.
    public static IReadOnlyList<double?> LeftOff { get; } = [null, 0.2, 0.4];

    public const string ProposalPrefix = RidgeScore.Proposal;

    // Whether a score reaches a family on an index: the breakout and the drift on every index, and the pullback on the
    // S&P 400 and 600, whose provisional rule takes the hooks where the S&P 500's swing filter takes none.
    public static bool Reaches(string index, string family) =>
        family is EquityBrief.Core.Families.BreakoutRule.Name or EquityBrief.Core.Families.DriftRule.Name
        || (family == EquityBrief.Core.Families.SetupFamilies.Pullback && index != WalkForwardTester.LargeIndex);

    public static string ProposalName(int rank) => rank switch
    {
        1 => ProposalPrefix + ", ordering the list",
        2 => ProposalPrefix + ", its lowest fifth left off",
        _ => ProposalPrefix + ", its lowest two fifths left off",
    };

    // A fitted score's change in words, naming it by its hash, which the run's model row holds whole.
    public static string ChangeOf(FittedScore score, string index, double? share) =>
        FormattableString.Invariant($"orders the list by the learned score {score.Model.Hash}, fitted on the setups ended before {score.LearnedBefore:yyyy-MM-dd}")
        + (share is { } left && score.Model.FloorAt(index, left) is { } floor
            ? FormattableString.Invariant($", a score under {floor:0.####}, the {left * 100:0}th hundredth of those it learned on on the {DecisionCards.NameOf(index)}, left off")
            : string.Empty);

    public static (IReadOnlyList<LoopProposalRead> Proposals, IReadOnlyList<FittedScore> Scores) Run(LoopRead read, RuleWalk rule, LoopReadings readings, ScoreRows rows, Action<string> progress)
    {
        progress(FormattableString.Invariant($"fitting the learned score of the {rule.Family} on {rows.Count} finished setup(s) across the three indices"));

        IReadOnlyList<double?> ReadingsOf(int at) => readings.Of(rule.Listings[at].Name, rule.Listings[at].Bar, rule.Listings[at].Session);

        FittedScore? FitIn(LoopFold fold)
        {
            var learning = rows.Learning(fold);

            return RidgeScore.Fit(rows, learning) is { } model
                ? new FittedScore(rule.Family, fold.Year, learning.Min(rows.Session), fold.TestFrom, model)
                : null;
        }

        var fitted = read.Folds.Select(FitIn).ToArray();
        var named = FitIn(read.AllFinished);
        IReadOnlyList<(int Entry, double? Edge)> own = [.. rule.Own.Select(one => (one.Entry, one.Edge))];
        var proposals = new List<LoopProposalRead>();

        for (var rank = 1; rank <= LeftOff.Count; rank++)
        {
            var share = LeftOff[rank - 1];

            IReadOnlyList<(int Entry, double? Edge)>? Walked(FittedScore? score)
            {
                if (score is null || (share is { } left && score.Model.FloorAt(read.Index, left) is null))
                {
                    return null;
                }

                var hooks = RuleHooks.Of(score.Model.HookParameters(share is { } least ? score.Model.FloorAt(read.Index, least) : null));

                return [.. rule.Hooked(hooks, ReadingsOf).Select(one => (one.Entry, one.Edge))];
            }

            proposals.Add(new LoopProposalRead(
                rule.Family,
                ProposalName(rank),
                named is null ? null : ChangeOf(named, read.Index, share),
                rule.Current,
                LoopProcedures.Risks,
                rule.Cap,
                [.. read.Folds.Select((fold, at) => (fold, fitted[at] is { } one ? ChangeOf(one, read.Index, share) : null))],
                fitted.Count(one => one is not null),
                LoopProcedures.Evidence(read.Folds, read.Calendar, [.. fitted.Select(Walked)], own))
            {
                Finding = named is null ? null : RidgeScore.Words(named.Model),
                Change = named is null || (share is { } least && named.Model.FloorAt(read.Index, least) is null)
                    ? null
                    : LoopChange.OfHooks(named.Model.HookParameters(share is { } left ? named.Model.FloorAt(read.Index, left) : null)),
                Reference = LoopProcedures.Reference(read, own),
            });
        }

        var scores = fitted.OfType<FittedScore>().ToList();

        if (named is not null)
        {
            scores.Add(named);
        }

        return (proposals, scores);
    }

    // The words a run prints of a fitted score.
    public static string Line(FittedScore score) =>
        FormattableString.Invariant($"the learned score of the {score.Family} for {score.Year}, {score.Model.Hash}: ") + RidgeScore.Words(score.Model);
}
