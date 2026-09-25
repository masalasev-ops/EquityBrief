using EquityBrief.Core.Candidates;
using EquityBrief.Core.Filter;

namespace EquityBrief.Worker.Candidates;

// The swing family, written down rather than typed at the command line: the live filter at the open
// version's settings and five variants, each the same whole rule with one setting moved to its other
// side, all run by the one evaluator. And the words each retirement of phase 10's three carries.
// see: The swing filter opens loose on the swing trade's own plan, and each of its five variants moves one setting to its other side
// see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
// see: The three phase 10 candidates are retired when the swing family registers, and each retirement says no result of theirs was read
public static class TheSwingFamily
{
    // The words every retirement at the family's registration carries.
    public const string NothingRead = "no result of this candidate was read before it was retired";

    public const string Evidence =
        "retired when the swing family registered, since from phase 12 no reason chooses tonight's list and the question " +
        "this candidate was registered to answer no longer exists; " + NothingRead;

    public const string Rule =
        "the swing filter's gates, its trigger's arrival inside its window and its exclusions at every setting stated, " +
        "a member firing where every gate read passes and no exclusion applies";

    public const string Test =
        "a sign-flip test over blocks of 63 exchange sessions, read at 8, 12 and 16 non-empty whole blocks, against each " +
        "setup's own calibrated bar, at a sixth of 0.05 spent across the looks";

    // The other side of each live setting a variant moves to.
    public const double VariantRewardToRisk = 2;

    public const double VariantDepthLow = 1;

    public const double VariantDepthHigh = 3;

    public const double VariantStrength = 2.0 / 3.0;

    public const int VariantArrival = 1;

    public const string RewardToRiskName = "the swing filter at a reward to risk of 2";

    public const string DepthName = "the swing filter at a pullback of 1 to 3 typical moves";

    public const string MarketOffName = "the swing filter with the market gate off";

    public const string StrengthName = "the swing filter with strength in the top third";

    public const string ArrivalName = "the swing filter with one-session arrival";

    // The six, the live filter first at the open version's settings.
    public static IReadOnlyList<Registration> For(string version, FilterSettings live) =>
    [
        new(SwingFamily.LiveCandidate(version), Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live)),
        new(RewardToRiskName, Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { RewardToRiskFloor = VariantRewardToRisk })),
        new(DepthName, Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { DepthLow = VariantDepthLow, DepthHigh = VariantDepthHigh })),
        new(MarketOffName, Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live, marketGate: false)),
        new(StrengthName, Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { StrengthFloor = VariantStrength })),
        new(ArrivalName, Rule, Test, SwingFilterRule.EvaluatorName, SwingFilterRule.ParametersOf(live with { ArrivalSessions = VariantArrival })),
    ];

    // The candidates the family's registration retires: phase 10's three.
    public static IReadOnlyList<string> Retires => [.. TheThreeCandidates.All.Select(one => one.Candidate)];
}
