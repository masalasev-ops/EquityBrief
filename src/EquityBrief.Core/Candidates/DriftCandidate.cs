using EquityBrief.Core.Families;
using EquityBrief.Core.Releases;

namespace EquityBrief.Core.Candidates;

// The earnings drift family as a registered candidate: the drift's rule at the settings a registration
// states, each of them a parameter, a stop floor of nought holding the stop at the reaction session's low, and
// the market switches it states read after the rule's gates, and from 20.4 the tone condition after them where the
// registration names a reading version. A member fires where every gate passes and no exclusion applies, and its
// verdict carries the trade the rule places.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, each family registered again whole and its records replayed
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
public sealed class DriftCandidate : FamilyRuleEvaluator
{
    public const string EvaluatorName = "drift";

    public const string WindowSessionsParameter = "windowSessions";
    public const string ReactionMovesParameter = "reactionMoves";
    public const string VolumeMultipleParameter = "volumeMultiple";
    public const string TargetRiskMultipleParameter = "targetRiskMultiple";
    public const string StopFloorMovesParameter = "stopFloorMoves";

    public override string Name => EvaluatorName;

    public override string Version => "f6b52da6e0c6";

    public override string Family => DriftRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
        [WindowSessionsParameter, ReactionMovesParameter, VolumeMultipleParameter, TargetRiskMultipleParameter, StopFloorMovesParameter, MarketSwitches.IndexAverageParameter, MarketSwitches.VixLookbackParameter];

    // Its rule's sources, the tone condition's and the engines' hooks a registration may state beside its parameters,
    // which its list and its trades are read through.
    // see: Every engine's settings hooks land together and all default off, so the families' pins move once
    public override IReadOnlyList<string> OwnSources { get; } =
        [.. SourcesWith("src/EquityBrief.Core/Families/DriftRule.cs"), "src/EquityBrief.Core/Releases/ReleaseTone.cs", .. EquityBrief.Core.Loop.RuleHooks.Sources];

    // The parameters a registration states for settings, switches and a reading version where it reads one, and the
    // settings a registration's parameters state.
    public static IReadOnlyDictionary<string, double> ParametersOf(DriftSettings settings, MarketSwitches switches = default, long readingVersion = 0)
    {
        var parameters = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            [WindowSessionsParameter] = settings.WindowSessions,
            [ReactionMovesParameter] = settings.ReactionMoves,
            [VolumeMultipleParameter] = settings.VolumeMultiple,
            [TargetRiskMultipleParameter] = settings.TargetRiskMultiple,
            [StopFloorMovesParameter] = settings.StopFloorMoves,
            [MarketSwitches.IndexAverageParameter] = switches.IndexAverageSessions,
            [MarketSwitches.VixLookbackParameter] = switches.VixLookbackSessions,
        };

        if (readingVersion > 0)
        {
            parameters[ReleaseTone.VersionParameter] = readingVersion;
        }

        return parameters;
    }

    public static DriftSettings SettingsOf(IReadOnlyDictionary<string, double> parameters) =>
        new(
            (int)parameters[WindowSessionsParameter],
            parameters[ReactionMovesParameter],
            parameters[VolumeMultipleParameter],
            parameters[TargetRiskMultipleParameter],
            parameters[StopFloorMovesParameter]);

    public override FamilyResult EvaluateMember(FamilyMember member, IReadOnlyDictionary<string, double> parameters) =>
        ReleaseTone.Read(
            MarketSwitches.Of(parameters).Read(DriftRule.Evaluate(member.Drift, SettingsOf(parameters)), member.Market),
            ReleaseTone.VersionOf(parameters),
            member.Releases);

    public override double? TypicalMoveOf(FamilyMember member) => member.Drift.TypicalMove;

    // The widest window a registration's parameters read, so the stage reads every session a reaction it
    // admits can follow.
    public static int WindowOf(IReadOnlyDictionary<string, double> parameters) => (int)parameters[WindowSessionsParameter];
}
