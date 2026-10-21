using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The earnings drift on the S&P 400 or the S&P 600 as a registered candidate: its sweep's grid of four dials, the stop
// floor in typical moves, nought holding the stop at the reaction's low, and the levels its index's sweep read beside
// them, the drift's own among them: the wider window, the peers' surprise before the reaction above nothing, and from
// 20.11 the entry on the first pullback after the reaction and the print's quarter's revenue grown on the year as
// first filed; and from 20.4 the tone condition where a registration names a reading version.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
// see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
// see: A drift rule on the S&P 400 or 600 stating the first pullback is bought on the night through the one function its search's entry level reads
public sealed class IndexDriftCandidate(string index) : IndexRuleCandidate(index)
{
    public const string WindowSessionsParameter = "windowSessions";
    public const string ReactionMovesParameter = "reactionMoves";
    public const string VolumeMultipleParameter = "volumeMultiple";
    public const string TargetMultipleParameter = "targetMultiple";
    public const string StopFloorParameter = "stopFloor";
    public const string WideWindowParameter = "wideWindow";
    public const string PeersParameter = "peers";
    public const string EntryParameter = "entry";
    public const string RevenueGrewParameter = "revenueGrew";

    public override string Version => "c7b3c0232b0e";

    public override string SetupFamily => DriftRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
        [WindowSessionsParameter, ReactionMovesParameter, VolumeMultipleParameter, TargetMultipleParameter, StopFloorParameter, .. Levels, WideWindowParameter, PeersParameter, EntryParameter, RevenueGrewParameter];

    public override IReadOnlyList<string> OwnSources { get; } =
        IndexSourcesWith(
            "src/EquityBrief.Worker/Sweep/DriftSweep.cs",
            "src/EquityBrief.Core/Releases/ReleaseTone.cs",
            "src/EquityBrief.Core/Families/RevenueGrowth.cs",
            "src/EquityBrief.Core/Families/FirstFiledRevenue.cs");
}
