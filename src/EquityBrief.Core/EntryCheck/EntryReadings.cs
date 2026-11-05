using EquityBrief.Core.Prices;
using EquityBrief.Core.Report;

namespace EquityBrief.Core.EntryCheck;

// One session of a stock as the entry check reads it, at the store's scale.
public sealed record EntryBar(DateOnly Session, decimal High, decimal Low, decimal Close, long Volume);

// What the entry check reads of a member on a night, none where its inputs do not reach. No rule, plan, list or gate reads it.
public sealed record EntryReading(
    string Ticker,
    DateOnly Session,
    decimal Close,
    double? TypicalDay,
    double? StretchPercent,
    double? StretchDays,
    double? StretchShare,
    double? GainTwenty,
    double? Rsi,
    bool? LargestGain,
    double? VolumeRatio,
    decimal? GapHigh,
    decimal? GapLow,
    DateOnly? HighestSince,
    DateOnly? HistoryStart,
    decimal? OlderLow,
    decimal? OlderHigh,
    DateOnly? OlderOn,
    double? OlderDays);

// The entry check's readings of the bars, arithmetic over stored data alone.
// see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
public static class EntryReadings
{
    public const int AverageSessions = 20;
    public const int GainSessions = 20;
    public const int LargestGainSessions = 250;

    // How rare a stretch this far above the average must be among the stock's own stored days for the card to name it, and how
    // long ago a highest close must be for the card to name it. Provisional, and display alone: the state a stock is in is not
    // set by them, and the test over stored history is what would.
    public const double StretchedShare = 10;
    public const int HighestCloseDays = 365;

    // `older` is the sessions pulled before the stored year at the store's scale, oldest first, and `stored` the stored year,
    // the night last.
    public static EntryReading? Of(
        string ticker,
        IReadOnlyList<EntryBar> stored,
        IReadOnlyList<EntryBar> older,
        double? typicalDay,
        double? rsi,
        double? volumeAverage)
    {
        if (stored.Count == 0)
        {
            return null;
        }

        var night = stored[^1];
        var close = Statistic.FromPrice(night.Close);
        var (percent, share) = Stretch(stored);
        var average = percent is { } above ? close / (1 + (above / 100)) : default(double?);
        var all = older.Concat(stored).ToArray();
        var band = Older(older, night.Close, typicalDay);
        var gapped = stored.Count > 1 && night.Low > stored[^2].High;

        return new EntryReading(
            ticker,
            night.Session,
            night.Close,
            typicalDay,
            percent,
            average is { } mean && typicalDay is { } day && day > 0 ? (close - mean) / day : null,
            share,
            stored.Count > GainSessions ? ((close / Statistic.FromPrice(stored[^(GainSessions + 1)].Close)) - 1) * 100 : null,
            rsi,
            LargestGain(all),
            volumeAverage is { } volume && volume > 0 ? night.Volume / volume : null,
            gapped ? stored[^2].High : null,
            gapped ? night.Low : null,
            all.Take(all.Length - 1).Where(bar => bar.Close >= night.Close).Select(bar => (DateOnly?)bar.Session).LastOrDefault(),
            all[0].Session,
            band?.Low,
            band?.High,
            band?.MadeOn,
            band is { } read && typicalDay is { } typical && typical > 0 ? (Statistic.FromPrice(read.Low) - close) / typical : null);
    }

    // The close over its 20-session average in per cent on the night, and the share of the stored days holding that average that
    // stood this far above it or more.
    static (double? Percent, double? Share) Stretch(IReadOnlyList<EntryBar> stored)
    {
        if (stored.Count < AverageSessions)
        {
            return (null, null);
        }

        var over = new List<double>();
        var sum = 0d;

        for (var at = 0; at < stored.Count; at++)
        {
            sum += Statistic.FromPrice(stored[at].Close);

            if (at >= AverageSessions)
            {
                sum -= Statistic.FromPrice(stored[at - AverageSessions].Close);
            }

            if (at >= AverageSessions - 1)
            {
                var mean = sum / AverageSessions;

                over.Add(((Statistic.FromPrice(stored[at].Close) / mean) - 1) * 100);
            }
        }

        var tonight = over[^1];

        return (tonight, 100d * over.Count(one => one >= tonight) / over.Count);
    }

    // Whether the night's one-day gain is the largest of the last 250, none where fewer are held.
    static bool? LargestGain(IReadOnlyList<EntryBar> all)
    {
        if (all.Count <= LargestGainSessions)
        {
            return null;
        }

        var gains = new List<double>();

        for (var at = all.Count - LargestGainSessions; at < all.Count; at++)
        {
            gains.Add(Statistic.FromPrice(all[at].Close) / Statistic.FromPrice(all[at - 1].Close));
        }

        return gains[^1] > 1 && gains.Take(gains.Count - 1).All(gain => gains[^1] > gain);
    }

    // The nearest band above the close from the sessions before the stored year.
    static OlderBand? Older(IReadOnlyList<EntryBar> older, decimal close, double? typicalDay) =>
        typicalDay is { } day && day > 0
            ? OlderBands.Of([.. older.Select(bar => (bar.Session, bar.High, bar.Low))], 1m, close, Statistic.ToPrice(day), above: true, below: false)
                .Where(band => band.Low > close)
                .OrderBy(band => band.Low)
                .FirstOrDefault()
            : null;

    // Whether the card names a stretch, a gap, a largest gain and a highest close.
    public static bool Stretched(EntryReading reading) =>
        reading is { StretchShare: { } share, StretchDays: > 0 } && share <= StretchedShare;

    public static bool HighestLongSince(EntryReading reading) =>
        reading.HighestSince is { } since ? reading.Session.DayNumber - since.DayNumber > HighestCloseDays : reading.HistoryStart is not null;
}
