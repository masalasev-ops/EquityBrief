using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.History;

// How a code's long prices stand against the newest pull's over the sessions both hold: agreeing; one instrument, its
// volumes at one ratio, whose closes step apart on a corporate action one side adjusts and the other does not; another
// instrument whose long side trades thin while the pull's does not; or another instrument whose long side is kept.
public enum LongPulledKind
{
    Agrees,
    OneInstrument,
    AnotherThin,
    AnotherKept,
}

// What the comparison read: the kind, the sessions both hold, those whose closes stand more than 1% apart once the two are
// put at one scale on the newest of them, each side's median volume over them and the lasting steps between the two.
public sealed record LongPulledVerdict(LongPulledKind Kind, int Both, int Apart, long LongVolume, long PulledVolume, int Steps)
{
    public bool Listed => Kind != LongPulledKind.Agrees;
}

// From the 24.19 correction of 2026-11-25 a code's long prices held against the newest pull's over the sessions both hold, and
// read as the class the two fall in: a code whose closes stand more than 1% apart at one scale on most of those sessions, whose
// long side trades under 10,000 shares on the median session while the pull's does not, or whose two series step apart by more
// than 1% and stay apart, is listed. One instrument, its volumes within 2% of one ratio on nine sessions in ten, is put at the
// pull's adjustment before the store's own bars, each session at the median ratio of the 21 around it so a print one side
// alone carries moves nothing; another instrument whose long side is the thin one reads the pull's prices on every session the
// pull holds and none of the long side's before the pull's last; another whose long side is not the thin one keeps the long
// side, listed alone.
// see: A code whose long prices part from the newest pull's is read at the pull's adjustment where both are one instrument, and from the pull where the long side is another and thin
public static class LongPulled
{
    public const decimal ApartBy = 0.01m;

    public const double MostSessions = 0.5;

    public const long ThinVolume = 10_000;

    public const decimal OneRatioWithin = 0.02m;

    public const double OneRatioShare = 0.9;

    public const int StepWindow = 21;

    public static LongPulledVerdict Compare(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> pulled)
    {
        var (both, ratios) = Shared(own, pulled);

        if (both.Length < 2)
        {
            return new LongPulledVerdict(LongPulledKind.Agrees, both.Length, 0, 0, 0, 0);
        }

        var scale = ratios[^1];
        var apart = ratios.Count(ratio => Math.Abs((ratio / scale) - 1m) > ApartBy);
        var ownVolume = Median(both.Select(pair => pair.Own.Volume));
        var pulledVolume = Median(both.Select(pair => pair.Pulled.Volume));
        var steps = Steps(Smoothed(ratios));
        var most = apart > MostSessions * both.Length;
        var kind = OneInstrument(both)
            ? most || steps > 0 ? LongPulledKind.OneInstrument : LongPulledKind.Agrees
            : ownVolume < ThinVolume && pulledVolume >= ThinVolume ? LongPulledKind.AnotherThin
            : most ? LongPulledKind.AnotherKept : LongPulledKind.Agrees;

        return new LongPulledVerdict(kind, both.Length, apart, ownVolume, pulledVolume, steps);
    }

    // A code's series as the class reads it: one instrument's sessions before the store's first bar at the pull's adjustment,
    // another thin one's from the pull, and any other as it stands.
    public static SweepBar[] Read(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> pulled, DateOnly? firstStored)
    {
        var verdict = Compare(own, pulled);

        switch (verdict.Kind)
        {
            case LongPulledKind.AnotherThin:
            {
                var last = pulled[^1].Session;
                var held = pulled.Select(bar => bar.Session).ToHashSet();

                return [.. pulled.Where(bar => bar.Close > 0).Concat(own.Where(bar => bar.Session > last && !held.Contains(bar.Session))).OrderBy(bar => bar.Session)];
            }

            case LongPulledKind.OneInstrument:
            {
                var (both, ratios) = Shared(own, pulled);
                var smoothed = Smoothed(ratios);
                var reference = Array.FindLastIndex(both, pair => firstStored is not { } first || pair.Own.Session < first);

                if (reference < 0)
                {
                    return [.. own];
                }

                var at = 0;
                var read = new SweepBar[own.Count];

                for (var place = 0; place < own.Count; place++)
                {
                    var bar = own[place];

                    while (at + 1 < both.Length && both[at + 1].Own.Session <= bar.Session)
                    {
                        at++;
                    }

                    if (firstStored is { } stored && bar.Session >= stored || bar.Session > both[reference].Own.Session)
                    {
                        read[place] = bar;

                        continue;
                    }

                    var factor = smoothed[reference] / smoothed[at];

                    read[place] = factor == 1m ? bar : bar with
                    {
                        High = PriceForm.Round(bar.High * factor, Statistic.Places),
                        Low = PriceForm.Round(bar.Low * factor, Statistic.Places),
                        Close = PriceForm.Round(bar.Close * factor, Statistic.Places),
                        Open = PriceForm.Round(bar.Open * factor, Statistic.Places),
                    };
                }

                return read;
            }

            default:
                return [.. own];
        }
    }

    static ((SweepBar Own, SweepBar Pulled)[] Both, decimal[] Ratios) Shared(IReadOnlyList<SweepBar> own, IReadOnlyList<SweepBar> pulled)
    {
        var theirs = pulled.Where(bar => bar.Close > 0).GroupBy(bar => bar.Session).ToDictionary(group => group.Key, group => group.First());
        (SweepBar Own, SweepBar Pulled)[] both = [.. own.Where(bar => bar.Close > 0 && theirs.ContainsKey(bar.Session)).Select(bar => (bar, theirs[bar.Session]))];

        return (both, [.. both.Select(pair => pair.Own.Close / pair.Pulled.Close)]);
    }

    // Both sides one instrument: volumes within 2% of their median ratio on nine sessions in ten where both traded.
    static bool OneInstrument((SweepBar Own, SweepBar Pulled)[] both)
    {
        decimal[] ratios = [.. both.Where(pair => pair.Own.Volume > 0 && pair.Pulled.Volume > 0).Select(pair => decimal.Divide(pair.Own.Volume, pair.Pulled.Volume))];

        if (ratios.Length == 0)
        {
            return false;
        }

        var median = Median(ratios);

        return ratios.Count(ratio => Math.Abs((ratio / median) - 1m) <= OneRatioWithin) >= OneRatioShare * ratios.Length;
    }

    // Each session's ratio as the median of the 21 sessions around it, so a step stands from its own session and a print one
    // side alone carries for a few sessions moves nothing.
    static decimal[] Smoothed(decimal[] ratios)
    {
        var half = StepWindow / 2;
        var smoothed = new decimal[ratios.Length];

        for (var at = 0; at < ratios.Length; at++)
        {
            smoothed[at] = Median(ratios[Math.Max(0, at - half)..Math.Min(ratios.Length, at + half + 1)]);
        }

        return smoothed;
    }

    static int Steps(decimal[] smoothed) =>
        Enumerable.Range(1, Math.Max(0, smoothed.Length - 1)).Count(at => Math.Abs((smoothed[at] / smoothed[at - 1]) - 1m) > ApartBy);

    static decimal Median(IEnumerable<decimal> values)
    {
        decimal[] sorted = [.. values.Order()];

        return sorted.Length == 0 ? 0m : sorted.Length % 2 == 1 ? sorted[sorted.Length / 2] : (sorted[(sorted.Length / 2) - 1] + sorted[sorted.Length / 2]) / 2;
    }

    static long Median(IEnumerable<long> values)
    {
        long[] sorted = [.. values.Order()];

        return sorted.Length == 0 ? 0 : sorted[sorted.Length / 2];
    }
}
