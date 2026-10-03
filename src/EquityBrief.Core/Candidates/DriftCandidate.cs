using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The earnings drift family as a registered candidate: the drift's rule at the settings a registration
// states, each of them a parameter, a stop floor of nought holding the stop at the reaction session's low, and
// the market switches it states read after the rule's gates. A member fires where every gate passes and no
// exclusion applies, and its verdict carries the trade the rule places.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, each family registered again whole and its records replayed
public sealed class DriftCandidate : FamilyRuleEvaluator
{
    public const string EvaluatorName = "drift";

    public const string WindowSessionsParameter = "windowSessions";
    public const string ReactionMovesParameter = "reactionMoves";
    public const string VolumeMultipleParameter = "volumeMultiple";
    public const string TargetRiskMultipleParameter = "targetRiskMultiple";
    public const string StopFloorMovesParameter = "stopFloorMoves";

    public override string Name => EvaluatorName;

    public override string Version => "699aa6c0831f";

    public override string Family => DriftRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
        [WindowSessionsParameter, ReactionMovesParameter, VolumeMultipleParameter, TargetRiskMultipleParameter, StopFloorMovesParameter, MarketSwitches.IndexAverageParameter, MarketSwitches.VixLookbackParameter];

    public override IReadOnlyList<string> OwnSources { get; } = SourcesWith("src/EquityBrief.Core/Families/DriftRule.cs");

    // The parameters a registration states for settings and switches, and the settings a registration's parameters state.
    public static IReadOnlyDictionary<string, double> ParametersOf(DriftSettings settings, MarketSwitches switches = default) =>
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [WindowSessionsParameter] = settings.WindowSessions,
            [ReactionMovesParameter] = settings.ReactionMoves,
            [VolumeMultipleParameter] = settings.VolumeMultiple,
            [TargetRiskMultipleParameter] = settings.TargetRiskMultiple,
            [StopFloorMovesParameter] = settings.StopFloorMoves,
            [MarketSwitches.IndexAverageParameter] = switches.IndexAverageSessions,
            [MarketSwitches.VixLookbackParameter] = switches.VixLookbackSessions,
        };

    public static DriftSettings SettingsOf(IReadOnlyDictionary<string, double> parameters) =>
        new(
            (int)parameters[WindowSessionsParameter],
            parameters[ReactionMovesParameter],
            parameters[VolumeMultipleParameter],
            parameters[TargetRiskMultipleParameter],
            parameters[StopFloorMovesParameter]);

    public override FamilyResult EvaluateMember(FamilyMember member, IReadOnlyDictionary<string, double> parameters) =>
        MarketSwitches.Of(parameters).Read(DriftRule.Evaluate(member.Drift, SettingsOf(parameters)), member.Market);

    public override double? TypicalMoveOf(FamilyMember member) => member.Drift.TypicalMove;

    // The widest window a registration's parameters read, so the stage reads every session a reaction it
    // admits can follow.
    public static int WindowOf(IReadOnlyDictionary<string, double> parameters) => (int)parameters[WindowSessionsParameter];
}
