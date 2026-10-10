using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The earnings drift on the S&P 400 or the S&P 600 as a registered candidate: its sweep's grid of four dials, the stop
// floor in typical moves, nought holding the stop at the reaction's low, and the levels its index's sweep read beside
// them, the drift's own two among them: the wider window and the peers' surprise before the reaction above nothing.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
public sealed class IndexDriftCandidate(string index) : IndexRuleCandidate(index)
{
    public const string WindowSessionsParameter = "windowSessions";
    public const string ReactionMovesParameter = "reactionMoves";
    public const string VolumeMultipleParameter = "volumeMultiple";
    public const string TargetMultipleParameter = "targetMultiple";
    public const string StopFloorParameter = "stopFloor";
    public const string WideWindowParameter = "wideWindow";
    public const string PeersParameter = "peers";

    public override string Version => "285c79d96f7a";

    public override string SetupFamily => DriftRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
        [WindowSessionsParameter, ReactionMovesParameter, VolumeMultipleParameter, TargetMultipleParameter, StopFloorParameter, .. Levels, WideWindowParameter, PeersParameter];

    public override IReadOnlyList<string> OwnSources { get; } = IndexSourcesWith("src/EquityBrief.Worker/Sweep/DriftSweep.cs");
}
