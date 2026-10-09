namespace EquityBrief.Core.Ledger;

// One reading the ledger stores of a setup: the column it is stored in and what it is, in one sentence.
public sealed record LedgerReading(string Column, string Definition);

// The ledger's readings, each defined once here and read as it stood on the setup's session: every figure comes from
// the bars to that session, the market series to that session and the quarters filed before it, through the rules'
// own functions, so a row written on the night and a row rebuilt from the history for the same session carry the same
// figures. A reading the inputs do not reach is stored as none and never as nought. The version is the pin of this
// file and of the worker's source that fills the columns, stored on every row, so a row written under another
// definition can be told apart.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public static class LedgerReadings
{
    public const string VersionDeclaration = "public const string Version =";

    public const string Version = "61029e708aa9";

    // The reference high the pullback's readings are taken at, the live design's twenty sessions.
    public const int ReferenceHighSessions = 20;

    // The sessions the dollar volume is the mean over, the index floors' own window.
    public const int LiquiditySessions = 50;

    // The sessions a market series' change and relative return are read over.
    public const int IndexAverageSessions = 200;
    public const int VixChangeSessions = 10;
    public const int RelativeReturnSessions = 63;
    public const int CreditAverageSessions = 50;

    public static IReadOnlyList<LedgerReading> All { get; } =
    [
        new("close_over_twenty", "the close over its 20-session average"),
        new("close_over_fifty", "the close over its 50-session average"),
        new("close_over_long", "the close over its 200-session average"),
        new("fifty_over_long", "the 50-session average over the 200-session average"),
        new("move_share", "the typical move over the close"),
        new("rsi", "the relative strength index on the session"),
        new("rsi_up", "1 where the relative strength index rose on the session, 0 where it did not"),
        new("volume_ratio", "the session's volume over the mean of the 50 sessions before it"),
        new("return_quarter", "the return over 63 sessions"),
        new("return_half_year", "the return over 126 sessions"),
        new("return_twelve_less_one", "the return over the 231 sessions ending 21 sessions before"),
        new("strength", "the mean of the member's places among the members' returns over 63 and 126 sessions, the live measure"),
        new("strength_twelve_less_one", "the member's place among the members' returns over the 231 sessions ending 21 sessions before"),
        new("high_ratio", "the close over the highest high of the 252 sessions ending on the session"),
        new("since_high", "the sessions since the highest high of the 20 sessions before"),
        new("depth", "the pullback from that high in typical moves"),
        new("dry_up", "the volume while it came down over the 50-session average"),
        new("gap_down", "the largest gap down inside that pullback in typical moves"),
        new("rsi_low", "the lowest relative strength index since that high"),
        new("tightness", "the mean daily range of the 20 sessions before over that of the 20 before them"),
        new("liquidity", "the base-10 logarithm of the mean of close times volume over the 50 sessions to the session"),
        new("earnings_sessions", "the weekdays to the next report on file, the report's day counted"),
        new("surprise_sessions", "the sessions since the newest surprise's reaction session"),
        new("surprise_percent", "that surprise in per cent"),
        new("reward_to_risk", "the plan's target distance over its stop distance"),
        new("freshness", "for a pullback the sessions since its trigger first fired, for a drift the sessions since its reaction"),
        new("band_strength", "for a pullback the strength of the band holding the close"),
        new("volume_multiple", "for a breakout the session's volume over its 50-session average, for a drift the reaction session's"),
        new("range_ratio", "for a breakout the mean range of the 20 sessions before over that of the 20 before them"),
        new("reaction_moves", "for a drift the reaction session's rise in typical moves"),
        new("breadth", "the share of the index's members closing above their 200-session average"),
        new("highs_less_lows", "the members at a 252-session high less those at a 252-session low, over the members held"),
        new("index_over_long", "the index's own series' close over its 200-session average, the S&P 500 by its index and the 400 and 600 by their funds"),
        new("vix", "the VIX's close"),
        new("vix_change", "the VIX's close over its close 10 sessions before"),
        new("mid_over_large", "IJH's return over SPY's across 63 sessions"),
        new("small_over_large", "IJR's return over SPY's across 63 sessions"),
        new("credit_over_fifty", "HYG's close over its 50-session average"),
        new("profit", "1 where the four newest quarters filed before the session sum their net income above nothing, 0 otherwise"),
        new("coverage", "1 where those quarters' operating income is at least twice their interest expense or the company is a financial one, 0 otherwise"),
        new("revenue_growth", "the newest quarter's revenue as first filed before the session over the same quarter's a year before, less one"),
        new("growth_change", "that growth less the quarter before's growth on its own year before"),
        new("gross_margin_change", "the newest quarter's gross profit over its revenue less the same quarter's a year before"),
        new("operating_margin_change", "the newest quarter's operating income over its revenue less the same quarter's a year before"),
        new("cash_over_income", "the newest fiscal year's cash from operations over its net income, both as first filed before the session, where that income is above nothing"),
    ];

    public static int Count => All.Count;

    public static int IndexOf(string column)
    {
        for (var at = 0; at < All.Count; at++)
        {
            if (string.Equals(All[at].Column, column, StringComparison.Ordinal))
            {
                return at;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(column), column, "The ledger holds no such reading.");
    }

    // A statistic over another, none where either is not a figure or the divisor is nothing.
    public static double? Over(double value, double against) =>
        double.IsNaN(value) || double.IsNaN(against) || against == 0 ? null : value / against;

    // A statistic as stored, none where it is not a figure.
    public static double? Figure(double value) => double.IsNaN(value) ? null : value;

    public static double? Flag(bool value) => value ? 1 : 0;

    // The base-10 logarithm of the mean of close times volume over the window's sessions, the last the session's own,
    // each close already a statistic; none under the window or where the mean is nothing.
    public static double? Liquidity(IReadOnlyList<(double Close, long Volume)> toTheSession)
    {
        if (toTheSession.Count < LiquiditySessions)
        {
            return null;
        }

        var mean = 0.0;

        for (var at = toTheSession.Count - LiquiditySessions; at < toTheSession.Count; at++)
        {
            mean += toTheSession[at].Close * toTheSession[at].Volume;
        }

        mean /= LiquiditySessions;

        return mean > 0 ? Math.Log10(mean) : null;
    }
}

// The readings of a market series on a session, each over the series' own sessions: a series' close on a day is its
// close on the last of its sessions on or before that day, and a window is counted in the series' own sessions.
public static class SeriesReadings
{
    // The place of the last session on or before the day, or -1 where the series starts after it.
    public static int At(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day)
    {
        var low = 0;
        var high = series.Count - 1;
        var found = -1;

        while (low <= high)
        {
            var middle = (low + high) / 2;

            if (series[middle].Session <= day)
            {
                found = middle;
                low = middle + 1;
            }
            else
            {
                high = middle - 1;
            }
        }

        return found;
    }

    public static double? CloseOn(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day) =>
        At(series, day) is var at && at >= 0 ? series[at].Close : null;

    // The close over the mean of the given count of closes ending on the day's own, none under the count.
    public static double? OverAverage(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day, int sessions)
    {
        var at = At(series, day);

        if (at < sessions - 1)
        {
            return null;
        }

        var sum = 0.0;

        for (var place = at - sessions + 1; place <= at; place++)
        {
            sum += series[place].Close;
        }

        return LedgerReadings.Over(series[at].Close, sum / sessions);
    }

    // The close over the close the given count of sessions before, none where the series does not reach back.
    public static double? OverBefore(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly day, int sessions)
    {
        var at = At(series, day);

        return at < sessions ? null : LedgerReadings.Over(series[at].Close, series[at - sessions].Close);
    }

    // One series' return over another's across the count of sessions, each over its own sessions to the day: the
    // ratio of the two closes' ratios, none where either does not reach back.
    public static double? RelativeReturn(
        IReadOnlyList<(DateOnly Session, double Close)> series,
        IReadOnlyList<(DateOnly Session, double Close)> against,
        DateOnly day,
        int sessions)
    {
        var own = OverBefore(series, day, sessions);
        var other = OverBefore(against, day, sessions);

        return own is { } one && other is { } two && two != 0 ? one / two : null;
    }
}
