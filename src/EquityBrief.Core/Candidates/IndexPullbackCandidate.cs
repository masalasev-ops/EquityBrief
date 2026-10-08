using EquityBrief.Core.Families;

namespace EquityBrief.Core.Candidates;

// The pullback on the S&P 400 or the S&P 600 as a registered candidate: the nine dials of its sweep's extended grid,
// the stop's two bounds two of the parameters and a dry-up or market floor of nought standing for none, and the levels
// its index's sweep read beside them.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
public sealed class IndexPullbackCandidate(string index) : IndexRuleCandidate(index)
{
    public const string StrengthParameter = "strength";
    public const string DepthLowParameter = "depthLow";
    public const string DepthHighParameter = "depthHigh";
    public const string DryUpParameter = "dryUp";
    public const string FreshnessParameter = "freshness";
    public const string RewardToRiskParameter = "rewardToRisk";
    public const string StopLowParameter = "stopLow";
    public const string StopHighParameter = "stopHigh";
    public const string MarketParameter = "market";
    public const string BandParameter = "band";

    public override string Version => "10a73e50cb06";

    public override string SetupFamily => SetupFamilies.Pullback;

    public override IReadOnlyList<string> Parameters { get; } =
    [
        StrengthParameter, DepthLowParameter, DepthHighParameter, DryUpParameter, FreshnessParameter, RewardToRiskParameter,
        StopLowParameter, StopHighParameter, MarketParameter, BandParameter, .. Levels,
    ];

    public override IReadOnlyList<string> OwnSources { get; } =
        IndexSourcesWith("src/EquityBrief.Worker/Sweep/SweepCandidates.cs", "src/EquityBrief.Worker/Sweep/SweepIdeas.cs", "src/EquityBrief.Core/Sweep/SweepAxes.cs");
}
