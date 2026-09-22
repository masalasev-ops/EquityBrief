using EquityBrief.Core.Candidates;

namespace EquityBrief.Worker.Candidates;

// One candidate as a registration states it: the name it will be known by, the rule in words, the
// test it will be judged by, the evaluator that runs it and the numbers it runs at.
public sealed record Registration(
    string Candidate,
    string Rule,
    string Test,
    string Evaluator,
    IReadOnlyDictionary<string, double> Parameters);

// The three candidates phase 10 registers, written down rather than typed at the command line.
//
// In code because a registration is a statement about what was tried, and a rule retyped by hand
// at the prompt is a rule nobody can check afterwards against the condition that ran. The command
// names this set and writes the three rows at one instant, so each opens at a third of the level.
// see: The three candidates are registered at one instant
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public static class TheThreeCandidates
{
    public const string ArrivedAndNarrowName = "arrived and narrow";

    public const string VolumeAgainstTheNightName = "volume against the night";

    public const string CrossedByAMarginName = "crossed by a margin";

    public static IReadOnlyList<Registration> All { get; } =
    [
        new(
            ArrivedAndNarrowName,
            "the previous close was above the nearest buying zone's high edge, tonight's close is inside that zone " +
            "at either edge, and the zone is no wider than the registered multiple of the name's typical daily move",
            "a sign-flip test over blocks of 63 exchange sessions, read at 8, 12 and 16 non-empty whole blocks, " +
            "against each setup's own calibrated bar, at a third of 0.05 spent across the looks",
            ArrivedAndNarrow.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [ArrivedAndNarrow.Width] = ArrivedAndNarrow.ProposedWidth }),
        new(
            VolumeAgainstTheNightName,
            "tonight's volume over the name's fifty-day average is above the registered multiple of the night's " +
            "median ratio and above that multiple on its own, the median taken before any name is evaluated",
            "a sign-flip test over blocks of 63 exchange sessions, read at 8, 12 and 16 non-empty whole blocks, " +
            "against each setup's own calibrated bar, at a third of 0.05 spent across the looks",
            VolumeAgainstTheNight.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [VolumeAgainstTheNight.Multiple] = VolumeAgainstTheNight.ProposedMultiple }),
        new(
            CrossedByAMarginName,
            "the close went through a whole band since the previous session and finished at least the registered " +
            "margin of a typical daily move past the edge it crossed, the high edge on a rise and the low edge on a fall",
            "a sign-flip test over blocks of 63 exchange sessions, read at 8, 12 and 16 non-empty whole blocks, " +
            "against each setup's own calibrated bar, at a third of 0.05 spent across the looks",
            CrossedByAMargin.EvaluatorName,
            new Dictionary<string, double>(StringComparer.Ordinal) { [CrossedByAMargin.Margin] = CrossedByAMargin.SettledMargin }),
    ];
}
