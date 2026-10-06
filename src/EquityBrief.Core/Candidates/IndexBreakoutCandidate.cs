using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The breakout on the S&P 400 or the S&P 600 as a registered candidate: its sweep's grid of four dials, a range ceiling
// of nought standing for none, and the levels its index's sweep read beside them, the breakout's own three among them:
// the close's nearness to its year's high, the sessions since that high, and a volume at least the stated multiple.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
public sealed class IndexBreakoutCandidate(string index) : IndexRuleCandidate(index)
{
    public const string HighSessionsParameter = "highSessions";
    public const string VolumeMultipleParameter = "volumeMultiple";
    public const string RangeCeilingParameter = "rangeCeiling";
    public const string StopMovesParameter = "stopMoves";
    public const string NearnessParameter = "nearness";
    public const string RecencyParameter = "recency";
    public const string HighVolumeParameter = "highVolume";

    public override string Version => "000000000000";

    public override string SetupFamily => BreakoutRule.Name;

    public override IReadOnlyList<string> Parameters { get; } =
        [HighSessionsParameter, VolumeMultipleParameter, RangeCeilingParameter, StopMovesParameter, .. Levels, NearnessParameter, RecencyParameter, HighVolumeParameter];

    public override IReadOnlyList<string> OwnSources { get; } = IndexSourcesWith("src/EquityBrief.Worker/Sweep/BreakoutSweep.cs");
}
