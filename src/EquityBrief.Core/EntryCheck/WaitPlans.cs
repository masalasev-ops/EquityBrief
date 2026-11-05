using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.EntryCheck;

// A provisional plan for waiting rather than buying at the open: the zone to buy in, the stop, the first exit, the reward to risk and
// how far the zone stands below the close in typical days. Or why the ladder draws none.
public sealed record WaitPlan(
    decimal? ZoneLow,
    decimal? ZoneHigh,
    decimal? Stop,
    decimal? FirstExit,
    double? RewardToRisk,
    double? ZoneBelowDays,
    string? Why);

// The wait plan from the existing ladder alone, with no new rule: its bands, its stop rule and its first resistance at twice the risk.
// see: The wait plan is drawn from the ladder's own bands and stop, is provisional and sets nothing
public static class WaitPlans
{
    // The risk a first exit must stand from the zone's middle, as the order states it.
    public const decimal ExitAtRisks = 2m;

    public const string TrendDown = "the trend is down, so the ladder places nothing";
    public const string NotClassified = "the trend could not be classified, so the ladder places nothing";
    public const string NoBand = "no support band with an anchor other than a moving average stands under the close";

    // On a gap day the zone is the gap, widened down to the top of the nearest support band if that top is within one typical day under
    // the gap; on any other day, or where no band is that near, it is the nearest support band under the close. A band anchored only by
    // an average carries no tranche on the ladder, so it carries no zone here.
    public static WaitPlan Of(
        decimal close,
        decimal? gapHigh,
        decimal? gapLow,
        double? typicalDay,
        IReadOnlyList<Level> bands,
        string trendState,
        IReadOnlyList<decimal> swingLows)
    {
        if (trendState == TrendState.Downtrend)
        {
            return new WaitPlan(null, null, null, null, null, null, TrendDown);
        }

        if (trendState == TrendState.NotClassified)
        {
            return new WaitPlan(null, null, null, null, null, null, NotClassified);
        }

        var under = bands
            .Where(band => band.Role == LevelSeries.Support && band.HasNonAverageAnchor && band.HighEdge < close)
            .OrderByDescending(band => band.HighEdge)
            .ToArray();

        var typical = typicalDay is { } day && day > 0 ? Statistic.ToPrice(day) : default(decimal?);
        Level? chosen = null;
        decimal low = 0;
        decimal high = 0;

        if (gapHigh is { } top && gapLow is { } bottom && typical is { } oneDay
            && under.FirstOrDefault(one => one.HighEdge <= top) is { } beneathTheGap && top - beneathTheGap.HighEdge <= oneDay)
        {
            (chosen, low, high) = (beneathTheGap, beneathTheGap.HighEdge, bottom);
        }
        else if (under.Length > 0)
        {
            (chosen, low, high) = (under[0], under[0].LowEdge, under[0].HighEdge);
        }

        if (chosen is null)
        {
            return new WaitPlan(null, null, null, null, null, null, NoBand);
        }

        var next = bands
            .Where(other => other.Role == LevelSeries.Support && other.LowEdge < chosen.LowEdge)
            .OrderByDescending(other => other.LowEdge)
            .FirstOrDefault();
        var stop = LadderSeries.OutsideEverySupportBand(LadderSeries.StopFor(chosen, next?.LowEdge, trendState, swingLows), bands);
        var entry = (low + high) / 2;
        var below = typicalDay is { } read && read > 0 ? (Statistic.FromPrice(close) - Statistic.FromPrice(high)) / read : default(double?);

        if (stop is not { } floor)
        {
            return new WaitPlan(low, high, null, null, null, below, null);
        }

        var risk = entry - floor;
        var exit = bands
            .Where(other => other.Role == LevelSeries.Resistance && other.LowEdge - entry >= risk * ExitAtRisks)
            .OrderBy(other => other.LowEdge)
            .FirstOrDefault();

        return new WaitPlan(low, high, floor, exit?.LowEdge, exit is null ? null : Statistic.FromPrice((exit.LowEdge - entry) / risk), below, null);
    }
}
