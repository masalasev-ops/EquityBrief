namespace EquityBrief.Core.Candidates;

// The evaluators the code carries, which is the set a registration may name.
//
// A register naming an evaluator nothing implements is a registration that
// cannot be evaluated, and the night that discovered it would be the night after
// the window opened. So the registrar resolves the name here and refuses at the
// write instead.
//
// The list rather than reflection over the assembly, because a set discovered by
// scanning is a set that silently grows: an evaluator written and not added here
// is one nobody chose to offer, and it should be unregistrable until somebody
// does. The direction that keeps the list from rotting is asserted the other way
// round by the check, which fails an evaluator type this does not carry.
public static class CandidateEvaluators
{
    public static IReadOnlyList<CandidateEvaluator> All { get; } =
    [
        new MomentumIndexReading(),
        new MomentumHistogramTurn(),
        new ArrivedAndNarrow(),
        new VolumeAgainstTheNight(),
        new CrossedByAMargin(),
        new SwingFilterRule(),
    ];

    public static CandidateEvaluator? Find(string name) =>
        All.FirstOrDefault(evaluator => string.Equals(evaluator.Name, name, StringComparison.Ordinal));

    public static IReadOnlyList<string> Names =>
        [.. All.Select(evaluator => evaluator.Name).OrderBy(name => name, StringComparer.Ordinal)];
}
