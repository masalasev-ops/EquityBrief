using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Sweep;

// The readings the sweep takes over the window the night reads: each bar's Wilder average seeded where the
// night's year of bars begins, the highest high of a year, the range's tightness, the volume against its
// average, and the shape of a pullback. Pure over one name's bars, so a test holds each to the night's own
// function over the same window.
// see: Code owns every number
public static class SweepWindows
{
    // The first bar of the window the night computes a bar's indicators over: the bars from one year before
    // the bar's session, which is what the bar store holds on that night.
    public static int[] WindowStarts(IReadOnlyList<SweepBar> bars, int years = 1)
    {
        var starts = new int[bars.Count];

        for (int bar = 0, first = 0; bar < bars.Count; bar++)
        {
            var oldest = bars[bar].Session.AddYears(-years);

            while (first < bar && bars[first].Session < oldest)
            {
                first++;
            }

            starts[bar] = first;
        }

        return starts;
    }

    // Wilder's ATR and RSI at each bar, each seeded on the first fourteen changes of the bar's own window, as the
    // night's indicator engine seeds them on the year it holds. The seed moves with the window, so a value is
    // recomputed from its window's start for every bar, which over a year of bars is the cost of a second pass.
    public static (double[] Atr, double[] Rsi) Wilder(IReadOnlyList<SweepBar> bars, int[] windowStarts)
    {
        var atr = new double[bars.Count];
        var rsi = new double[bars.Count];
        var highs = bars.Select(bar => Statistic.FromPrice(bar.High)).ToArray();
        var lows = bars.Select(bar => Statistic.FromPrice(bar.Low)).ToArray();
        var closes = bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray();

        Array.Fill(atr, double.NaN);
        Array.Fill(rsi, double.NaN);

        var ranges = new double[bars.Count];
        var gains = new double[bars.Count];
        var losses = new double[bars.Count];

        for (var index = 1; index < bars.Count; index++)
        {
            var previous = closes[index - 1];
            var change = closes[index] - previous;

            ranges[index] = Math.Max(highs[index] - lows[index], Math.Max(Math.Abs(highs[index] - previous), Math.Abs(lows[index] - previous)));
            gains[index] = change > 0 ? change : 0;
            losses[index] = change < 0 ? -change : 0;
        }

        var wilder = IndicatorSeries.Wilder;

        // Every bar sharing a window start shares one Wilder series, so the series is run once per start and
        // read off for each bar the start serves.
        for (var bar = 0; bar < bars.Count;)
        {
            var start = windowStarts[bar];
            var end = bar;

            while (end + 1 < bars.Count && windowStarts[end + 1] == start)
            {
                end++;
            }

            if (end - start + 1 >= IndicatorSeries.WilderWarmup)
            {
                double range = 0, gain = 0, loss = 0;

                for (var index = start + 1; index <= start + wilder; index++)
                {
                    range += ranges[index];
                    gain += gains[index];
                    loss += losses[index];
                }

                var averageRange = range / wilder;
                var averageGain = gain / wilder;
                var averageLoss = loss / wilder;

                Read(start + wilder);

                for (var index = start + wilder + 1; index <= end; index++)
                {
                    averageRange = ((averageRange * (wilder - 1)) + ranges[index]) / wilder;
                    averageGain = ((averageGain * (wilder - 1)) + gains[index]) / wilder;
                    averageLoss = ((averageLoss * (wilder - 1)) + losses[index]) / wilder;

                    Read(index);
                }

                void Read(int index)
                {
                    if (index >= bar && index <= end)
                    {
                        atr[index] = averageRange;
                        rsi[index] = averageLoss == 0 ? (averageGain == 0 ? 50 : 100) : 100 - (100 / (1 + (averageGain / averageLoss)));
                    }
                }
            }

            bar = end + 1;
        }

        return (atr, rsi);
    }

    // The highest high of the sessions to the bar over a window, and none where the series is short of it.
    public static double HighestHigh(IReadOnlyList<SweepBar> bars, int bar, int window)
    {
        if (bar + 1 < window)
        {
            return double.NaN;
        }

        var top = bars[bar - window + 1].High;

        for (var index = bar - window + 2; index <= bar; index++)
        {
            if (bars[index].High > top)
            {
                top = bars[index].High;
            }
        }

        return Statistic.FromPrice(top);
    }

    // The tightness of the range at a bar, the swing reading's own function over the bars to it.
    public static double Tightness(IReadOnlyList<SweepBar> bars, int bar)
    {
        var count = Math.Min(bar + 1, SwingReadings.TightLongSessions + 1);
        var window = new ReadingBar[count];

        for (var at = 0; at < count; at++)
        {
            var one = bars[bar - count + 1 + at];

            window[at] = new ReadingBar(one.Session, one.High, one.Low, one.Close, one.Volume);
        }

        return SwingReadings.Tightness(window) ?? double.NaN;
    }

    // The newest session making the highest high of the window ending at the bar, as the pullback reads it, and
    // how many sessions have traded since it.
    public static int SessionsSinceHigh(IReadOnlyList<SweepBar> bars, int bar, int window)
    {
        if (bar + 1 < window)
        {
            return -1;
        }

        var start = bar - window + 1;
        var top = bars[start].High;
        var at = start;

        for (var index = start; index <= bar; index++)
        {
            if (bars[index].High >= top)
            {
                top = bars[index].High;
                at = index;
            }
        }

        return bar - at;
    }

    // The largest gap down inside the pullback, from the session after the high to the bar: each session's open
    // below the close before it, in the bar's typical moves; nought where none opened lower, and none where a
    // bar carries no open or the typical move is missing.
    public static double LargestGapDown(IReadOnlyList<SweepBar> bars, int bar, int sinceHigh, double typicalMove)
    {
        if (sinceHigh < 0 || double.IsNaN(typicalMove) || typicalMove <= 0)
        {
            return double.NaN;
        }

        var largest = 0d;

        for (var index = bar - sinceHigh + 1; index <= bar; index++)
        {
            if (bars[index].Open <= 0)
            {
                return double.NaN;
            }

            var gap = Statistic.FromPrice(bars[index - 1].Close - bars[index].Open) / typicalMove;

            if (gap > largest)
            {
                largest = gap;
            }
        }

        return largest;
    }

    // A session's volume over the average of the fifty sessions before it, and none where the average is missing.
    public static double VolumeRatio(IReadOnlyList<SweepBar> bars, int bar, IReadOnlyList<double> volumeAverage)
    {
        if (bar < 1 || double.IsNaN(volumeAverage[bar - 1]) || volumeAverage[bar - 1] <= 0)
        {
            return double.NaN;
        }

        return Statistic.FromVolume(bars[bar].Volume) / volumeAverage[bar - 1];
    }
}
