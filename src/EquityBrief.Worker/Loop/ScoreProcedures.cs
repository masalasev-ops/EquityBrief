using EquityBrief.Core.Loop;
using EquityBrief.Worker.Cards;

namespace EquityBrief.Worker.Loop;

// One score a run fitted: the family, the year its fold tests or, for the score fitted on all finished data, the year
// after the history's, the first session of the setups it learned on and the session it was cut at, and the model, the
// ridge score or from 17.15 its challenger's trees.
public sealed record FittedScore(string Family, int Year, DateOnly LearnedFrom, DateOnly LearnedBefore, IScoreModel Model)
{
    public string Kind => Model is TreeModel ? ScoreProcedures.Trees : ScoreProcedures.Ridge;

    public string Pin => Model is TreeModel ? TreeFit.Version : RidgeScore.Version;

    public string Words => Model switch
    {
        TreeModel trees => TreeModel.Words(trees),
        RidgeModel ridge => RidgeScore.Words(ridge),
        _ => string.Empty,
    };
}

// The learned score as the tester runs it inside each fold, for each family a rule's hooks reach on an index: the
// ledger's finished setups of the family on all three indices, each fold fitting the score on those whose path ended
// before its year began, and three proposals, the rule's list ordered by the score, and ordered with the lowest fifth or
// the lowest two fifths of the scores the fold learned on on the index left off, each walked with the rule over the
// fold's year through the hooks a registration would set, against the rule as it stands. From 17.15 the challenger's
// trees are fitted on the same rows of each fold and bring the same three, each put only where it passes and its total
// edge over the test years is above the ridge score's proposal of its kind.
// see: A fitted statistical model is a rule
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
// see: The learned score's challenger is a tree ensemble fitted by FastTree and scored by code from its stored trees, put only where it passes and earns more than the ridge score
public static class ScoreProcedures
{
    // Each proposal's share of the scores left off, none for the list ordered alone.
    public static IReadOnlyList<double?> LeftOff { get; } = [null, 0.2, 0.4];

    public const string ProposalPrefix = RidgeScore.Proposal;

    // The two kinds of score a run fits, as its model rows name them.
    public const string Ridge = "ridge";

    public const string Trees = "trees";

    // Whether a pick's card on a family draws the score's part: the breakout, the drift and from 19.9 the fundamentals-first
    // family on every index, and the pullback on the S&P 400 and 600, as the card's decision names them; the tester fits
    // the score for every family a rule's hooks reach, the S&P 500's pullback among them from 17.14.
    // see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
    public static bool Reaches(string index, string family) =>
        family is EquityBrief.Core.Families.BreakoutRule.Name or EquityBrief.Core.Families.DriftRule.Name or EquityBrief.Core.Families.FundamentalsRule.Name
        || (family == EquityBrief.Core.Families.SetupFamilies.Pullback && index != WalkForwardTester.LargeIndex);

    public static string ProposalName(int rank) => KindName(ProposalPrefix, rank);

    public static string TreesName(int rank) => KindName(TreeScore.Proposal, rank);

    static string KindName(string prefix, int rank) => rank switch
    {
        1 => prefix + ", ordering the list",
        2 => prefix + ", its lowest fifth left off",
        _ => prefix + ", its lowest two fifths left off",
    };

    // A fitted score's change in words, naming it by its hash, which the run's model row holds whole.
    public static string ChangeOf(FittedScore score, string index, double? share) =>
        FormattableString.Invariant($"orders the list by the {(score.Model is TreeModel ? "learned trees" : "learned score")} {score.Model.Hash}, fitted on the setups ended before {score.LearnedBefore:yyyy-MM-dd}")
        + (share is { } left && score.Model.FloorAt(index, left) is { } floor
            ? FormattableString.Invariant($", a score under {floor:0.####}, the {left * 100:0}th hundredth of those it learned on on the {DecisionCards.NameOf(index)}, left off")
            : string.Empty);

    public static (IReadOnlyList<LoopProposalRead> Proposals, IReadOnlyList<FittedScore> Scores) Run(LoopRead read, RuleWalk rule, LoopReadings readings, ScoreRows rows, Action<string> progress) =>
        Run(read, EngineRule.Of(read, rule, readings), rows, progress);

    // From 21.5 the same over any family's rule as the engines read it, a book family's among them, its list the members
    // its book chooses.
    public static (IReadOnlyList<LoopProposalRead> Proposals, IReadOnlyList<FittedScore> Scores) Run(LoopRead read, EngineRule rule, ScoreRows rows, Action<string> progress)
    {
        progress(FormattableString.Invariant($"fitting the learned score and its challenger's trees of the {rule.Family} on {rows.Count} finished setup(s) across the three indices"));

        var (ridge, ridgeScores) = Kind(read, rule, rows, (learnt, learning) => RidgeScore.Fit(learnt, learning), ProposalName);
        var (trees, treeScores) = Kind(read, rule, rows, (learnt, learning) => TreeFit.Fit(learnt, learning), TreesName);

        return ([.. ridge, .. trees], [.. ridgeScores, .. treeScores]);
    }

    static (IReadOnlyList<LoopProposalRead> Proposals, IReadOnlyList<FittedScore> Scores) Kind(
        LoopRead read,
        EngineRule rule,
        ScoreRows rows,
        Func<ScoreRows, IReadOnlyList<int>, IScoreModel?> fit,
        Func<int, string> name)
    {
        FittedScore? FitIn(LoopFold fold)
        {
            var learning = rows.Learning(fold);

            return fit(rows, learning) is { } model
                ? new FittedScore(rule.Family, fold.Year, learning.Min(rows.Session), fold.TestFrom, model)
                : null;
        }

        var fitted = read.Folds.Select(FitIn).ToArray();
        var named = FitIn(read.AllFinished);
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

                return rule.Walk(score.Model.HookParameters(share is { } least ? score.Model.FloorAt(read.Index, least) : null), score.Model as TreeModel);
            }

            proposals.Add(new LoopProposalRead(
                rule.Family,
                name(rank),
                named is null ? null : ChangeOf(named, read.Index, share),
                rule.Current,
                rule.Unit,
                rule.Cap,
                [.. read.Folds.Select((fold, at) => (fold, fitted[at] is { } one ? ChangeOf(one, read.Index, share) : null))],
                fitted.Count(one => one is not null),
                rule.Evidence(read.Folds, read.Calendar, [.. fitted.Select(Walked)], rule.Own))
            {
                Finding = named?.Words,
                Change = named is null || (share is { } least && named.Model.FloorAt(read.Index, least) is null)
                    ? null
                    : LoopChange.OfHooks(named.Model.HookParameters(share is { } left ? named.Model.FloorAt(read.Index, left) : null)),
                Reference = rule.Reference,
            });
        }

        var scores = fitted.OfType<FittedScore>().ToList();

        if (named is not null)
        {
            scores.Add(named);
        }

        return (proposals, scores);
    }

    // Each of the trees' proposals read against the ridge score's proposal of its kind in the same run: its total edge
    // after costs over the test years and the ridge's, the trees put only where theirs is above it; every other proposal
    // as it was judged.
    public static IReadOnlyList<WalkForwardTester.Tested> AgainstRidge(IReadOnlyList<WalkForwardTester.Tested> tested) =>
    [
        .. tested.Select(one =>
        {
            if (!one.Proposal.Proposal.StartsWith(TreeScore.Proposal, StringComparison.Ordinal))
            {
                return one;
            }

            var kind = one.Proposal.Proposal[TreeScore.Proposal.Length..];
            var ridge = tested.FirstOrDefault(other => other.Proposal.Family == one.Proposal.Family && other.Proposal.Proposal == ProposalPrefix + kind);
            var total = one.Proposal.Evidence.ProposedResults.Sum();
            var against = ridge?.Proposal.Evidence.ProposedResults.Sum();
            var beats = against is { } bar && total > bar;
            var said = against is { } stood
                ? FormattableString.Invariant($"its total edge after costs over the test years, {total:0.###}, is {(beats ? "above" : "not above")} the learned score's of its kind, {stood:0.###}")
                : "the learned score brought no proposal of its kind to be read against";

            return one with
            {
                BeatsRidge = beats,
                Proposal = one.Proposal with { Finding = one.Proposal.Finding is { } finding ? finding + "; " + said : said },
            };
        }),
    ];

    // The words a run prints of a fitted score.
    public static string Line(FittedScore score) =>
        FormattableString.Invariant($"the {(score.Model is TreeModel ? "learned trees" : "learned score")} of the {score.Family} for {score.Year}, {score.Model.Hash}: ") + score.Words;
}
