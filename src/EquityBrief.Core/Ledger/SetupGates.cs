namespace EquityBrief.Core.Ledger;

// The loose gates: what makes a member-session a setup of a family, each a grid step beyond the widest level any
// sweep or search tried, so an engine can later place a boundary outside the searched range and still find the
// setups beyond it in the ledger. A setup is not a pick: the live rule's own pass is stored beside it.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public static class SetupGates
{
    // The pullback: a strength place of at least 0.20, a pullback of half a move to 12 typical moves from the
    // reference high, a trigger first fired within 14 sessions, a plan paying at least half its risk, whatever the
    // band's strength, the dry-up or the market read.
    public const double PullbackStrength = 0.20;
    public const double PullbackDepthLow = 0.5;
    public const double PullbackDepthHigh = 12;
    public const int PullbackFreshness = 14;
    public const double PullbackRewardToRisk = 0.5;

    // The breakout: a close above the highest high of the 63 sessions before on at least the 50-session average
    // volume, with the ranges of the 20 sessions before no wider than 1.25 of the 20 before them, whatever the market.
    public const int BreakoutHighSessions = 63;
    public const double BreakoutVolume = 1.0;
    public const double BreakoutRangeCeiling = 1.25;

    // The drift: one setup a report, on the reaction session, with a surprise above nothing and a rise of at least a
    // quarter of a typical move.
    public const double DriftReactionMoves = 0.25;

    // Every family's floors on every index: a close of at least $5 and a dollar volume of at least half the index's
    // own floor, an index with no floor clearing it.
    public const decimal LowestPrice = Readings.MemberReadings.LowestPrice;
    public const decimal FloorShare = 0.5m;

    // The history build's budget, in gigabytes; over it the pullback's strength floor rises a grid step until it fits.
    public const double BudgetGigabytes = 0.5;

    public static bool ClearsTheFloors(string indexCode, decimal close, decimal? dollarVolume) =>
        close >= LowestPrice
        && (Readings.MemberReadings.DollarVolumeFloor(indexCode) is not { } floor
            || (dollarVolume is { } held && held >= floor * FloorShare));

    // A reading not available fails the gate that reads it.
    public static bool Pullback(double strength, double depth, int age, double rewardToRisk) =>
        strength >= PullbackStrength
        && depth >= PullbackDepthLow && depth <= PullbackDepthHigh
        && age >= 0 && age <= PullbackFreshness
        && rewardToRisk >= PullbackRewardToRisk;

    public static bool Breakout(double close, double highBefore, double volumeMultiple, double rangeRatio) =>
        close > highBefore && volumeMultiple >= BreakoutVolume && rangeRatio <= BreakoutRangeCeiling;

    public static bool Drift(double surprisePercent, double reactionMoves) =>
        surprisePercent > 0 && reactionMoves >= DriftReactionMoves;
}
