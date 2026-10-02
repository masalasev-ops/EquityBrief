using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The breakout family as a registered candidate: the breakout's rule at the settings a registration states,
// each of them a parameter. A member fires where every gate passes and no exclusion applies, and its verdict
// carries the trade the rule places.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
public sealed class BreakoutCandidate : FamilyRuleEvaluator
{
    public const string EvaluatorName = "breakout";

    public const string HighSessionsParameter = "highSessions";
    public const string VolumeMultipleParameter = "volumeMultiple";
    public const string RangeCeilingParameter = "rangeCeiling";
    public const string StopMovesParameter = "stopMoves";

    public override string Name => EvaluatorName;

    public override string Version => "65ea06f28fff";

    public override string Family => BreakoutRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
        [HighSessionsParameter, VolumeMultipleParameter, RangeCeilingParameter, StopMovesParameter];

    public override IReadOnlyList<string> OwnSources { get; } = SourcesWith("src/EquityBrief.Core/Families/BreakoutRule.cs");

    // The parameters a registration states for settings, and the settings a registration's parameters state.
    public static IReadOnlyDictionary<string, double> ParametersOf(BreakoutSettings settings) =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [HighSessionsParameter] = settings.HighSessions,
            [VolumeMultipleParameter] = settings.VolumeMultiple,
            [RangeCeilingParameter] = settings.RangeCeiling,
            [StopMovesParameter] = settings.StopMoves,
        };

    public static BreakoutSettings SettingsOf(IReadOnlyDictionary<string, double> parameters) =>
        new(
            (int)parameters[HighSessionsParameter],
            parameters[VolumeMultipleParameter],
            parameters[RangeCeilingParameter],
            parameters[StopMovesParameter]);

    public override FamilyResult EvaluateMember(FamilyMember member, IReadOnlyDictionary<string, double> parameters) =>
        BreakoutRule.Evaluate(member.Breakout, SettingsOf(parameters));

    public override double? TypicalMoveOf(FamilyMember member) => member.Breakout.TypicalMove;
}
