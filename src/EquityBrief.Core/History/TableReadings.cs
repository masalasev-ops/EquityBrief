using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// The table of every member's readings on every session reads seven readings its own way. Four read a price level, and
// read it at the price the stock traded at on the day, its stored price times its raw close over its close, never at a
// price a later dividend or split adjusted: the third and fourth published alphas, which rank the open and the low across
// the index, and the two liquidity readings, which read the dollars traded. A return is read as stored, since an adjustment
// moves no day's return. Three read how far the history reaches, and read a fixed length of it on every session, the length
// the night reads, so a rule tested on the table reads what the night reads live: how many years since the price stood this
// high, the days since the older low or high, and the stretch's share of the history. A reading whose day holds no raw close
// is none.
// see: The every-session table reads four readings at the traded price and three over a fixed length of history, the night's own
public static class TableReadings
{
    // The sessions of history the three reach readings read on every session: the night's own reach.
    public const int HistorySessions = 2792;

    public static IReadOnlyList<string> TradedColumns { get; } = ["alpha_three", "alpha_four", "liquidity", "illiquidity"];

    public static IReadOnlyList<string> ReachColumns { get; } = ["highest_since_years", "older_days", "stretch_share"];

    // The readings that read a bar's volume, none on a session whose close moved with no volume, its volume missing.
    // see: A bar whose close moved with no volume traded with its volume missing
    public static IReadOnlyList<string> VolumeColumns { get; } =
    [
        "volume_ratio", "dry_up", "volume_multiple", "liquidity", "up_volume_share", "price_volume_correlation", "illiquidity",
        "volume_slope", "alpha_two", "alpha_three", "alpha_six", "drop_volume", "night_volume_ratio",
    ];

    // A price at the price the stock traded at on its bar's day; none where the bar holds no raw close.
    public static decimal? Traded(SweepBar bar, decimal price) =>
        bar.RawClose > 0 && bar.Close > 0 ? price * bar.RawClose / bar.Close : null;

    // The base-10 logarithm of the mean of the dollars traded over the liquidity's window, the raw close times the volume, a
    // day whose volume is missing left out of the mean; none under the window, where a day holds no raw close, or where the
    // mean is nothing.
    public static double? Liquidity(IReadOnlyList<SweepBar> bars, int bar)
    {
        if (bar + 1 < LedgerReadings.LiquiditySessions)
        {
            return null;
        }

        var sum = 0.0;
        var held = 0;

        for (var at = bar - LedgerReadings.LiquiditySessions + 1; at <= bar; at++)
        {
            if (!(bars[at].RawClose > 0))
            {
                return null;
            }

            if (LongTraded.VolumeMissing(bars, at))
            {
                continue;
            }

            sum += Statistic.FromPrice(bars[at].RawClose) * Statistic.FromVolume(bars[at].Volume);
            held++;
        }

        return held > 0 && sum > 0 ? Math.Log10(sum / held) : null;
    }

    // Amihud's illiquidity at the traded price: the base-10 logarithm of the mean of the absolute daily return as stored over
    // the raw close times the volume, a session trading nothing left out; none where a day holds no raw close or the mean is
    // nothing.
    public static double? Illiquidity(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (bar - sessions < 0)
        {
            return null;
        }

        var sum = 0.0;
        var held = 0;

        for (var at = bar - sessions + 1; at <= bar; at++)
        {
            if (!(bars[at].RawClose > 0))
            {
                return null;
            }

            var dollars = Statistic.FromPrice(bars[at].RawClose) * Statistic.FromVolume(bars[at].Volume);

            if (dollars > 0 && FactorReadings.Return(bars, at) is { } move)
            {
                sum += Math.Abs(move) / dollars;
                held++;
            }
        }

        return held > 0 && sum > 0 ? Math.Log10(sum / held) : null;
    }

    // The first bar of the older history the reach readings read for a bar whose window starts at the start given: the
    // history held to the fixed length, the window first.
    public static int OlderFrom(int start, int bar) => Math.Min(start, Math.Max(0, bar + 1 - HistorySessions));

    // The days a code reported its quarters, which the table's weekdays to the next report count to. Where its filings reach,
    // each quarter's is the last 8-K with item 2.02 filed on or before the 10-Q or 10-K filed on or after it, so an earlier one
    // in the same span, a preliminary, is left out and so is a calendar's day inside the filings' span; the calendars' days
    // before the first such 8-K and after the last and the report that closes it stand, and every one of them where the
    // filings hold none.
    // see: The every-session table counts the weekdays to the next report from the results 8-K each quarter's 10-Q or 10-K closes
    public static DateOnly[] ReportDays(IEnumerable<DateOnly> results, IEnumerable<DateOnly> periodic, IEnumerable<DateOnly> prints)
    {
        var filed = results.Distinct().Order().ToArray();
        var reports = periodic.Distinct().Order().ToArray();
        var calendars = prints.Distinct().Order().ToArray();

        if (filed.Length == 0)
        {
            return calendars;
        }

        var kept = new List<DateOnly>();

        for (var at = 0; at < filed.Length; at++)
        {
            var closing = Array.FindIndex(reports, report => report >= filed[at]);

            if (at + 1 < filed.Length && closing >= 0 && filed[at + 1] <= reports[closing])
            {
                continue;
            }

            kept.Add(filed[at]);
        }

        var lastClosing = Array.FindIndex(reports, report => report >= filed[^1]);
        var after = lastClosing >= 0 ? reports[lastClosing] : filed[^1];

        kept.AddRange(calendars.Where(day => day < filed[0] || day > after));

        return [.. kept.Distinct().Order()];
    }
}
