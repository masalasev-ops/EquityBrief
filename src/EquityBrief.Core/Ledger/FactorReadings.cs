using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Core.Ledger;

// The published price and volume factors the ledger reads beside its own, each over a member's bars to the session and
// none where they do not reach back, a statistic of its prices and volumes read through the crossing helpers: a daily
// return is the close over the close of the bar before, less one. The five formulas of Kakushadze's 101 Formulaic Alphas
// (2016) read the members' places on each session, which the worker works over the index's members and hands in: a
// place is the share of the members under the value and half the share level with it, its own among them, as the chart
// of what is behind a stock reads a place. A window counts the member's own bars, as every reading of the catalogue does.
// see: The published price and volume factors are ledger readings for the engines to search, and no factor chooses a stock
public static class FactorReadings
{
    public const int WeekSessions = 5;

    public const int MonthSessions = 21;

    public const int QuarterSessions = 63;

    // The bars a beta and the lowest low read, the 252 of the year the night holds, so a row written on the night and one
    // rebuilt from the history read the same bars; a beta reads the 251 returns between them.
    public const int YearBars = 252;

    public const int PlaceSessions = 10;

    public const int VolumeSessions = 20;

    // How far back a streak is counted: the returns the year the night holds reaches.
    public const int StreakReach = YearBars - 1;

    // Alpha#1's windows: the returns its deviation is read over and the sessions its largest value is sought in.
    public const int ArgMaxDeviation = 20;

    public const int ArgMaxSessions = 5;

    // Alpha#2's change in the logarithm of volume, over 2 sessions, read over 6; Alpha#3's and Alpha#6's correlations over
    // 10; Alpha#4's time-series rank over 9.
    public const int VolumeChangeSessions = 2;

    public const int Alpha2Sessions = 6;

    public const int Alpha3Sessions = 10;

    public const int Alpha4Sessions = 9;

    public const int Alpha6Sessions = 10;

    static double Close(SweepBar bar) => Statistic.FromPrice(bar.Close);

    // The daily return of a bar on the bar before, none for the first bar or a close of nothing before it.
    public static double? Return(IReadOnlyList<SweepBar> bars, int bar) =>
        bar >= 1 && bars[bar - 1].Close > 0 ? (Close(bars[bar]) / Close(bars[bar - 1])) - 1 : null;

    // The daily returns of the given count of bars ending on the bar, none where a bar is short of one.
    public static double[]? Returns(IReadOnlyList<SweepBar> bars, int bar, int count)
    {
        if (bar - count < 0)
        {
            return null;
        }

        var returns = new double[count];

        for (var at = 0; at < count; at++)
        {
            if (Return(bars, bar - count + 1 + at) is not { } one)
            {
                return null;
            }

            returns[at] = one;
        }

        return returns;
    }

    // The return over the given count of sessions: the close over the close that many bars before, less one.
    public static double? ReturnOver(IReadOnlyList<SweepBar> bars, int bar, int sessions) =>
        bar - sessions >= 0 && bars[bar - sessions].Close > 0 ? (Close(bars[bar]) / Close(bars[bar - sessions])) - 1 : null;

    public static double? LargestReturn(IReadOnlyList<SweepBar> bars, int bar, int sessions) =>
        Returns(bars, bar, sessions) is { } returns ? returns.Max() : null;

    // The sample standard deviation, over one fewer than the count, none under two values.
    public static double? Deviation(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
        {
            return null;
        }

        var mean = values.Average();
        var squares = 0.0;

        foreach (var value in values)
        {
            squares += (value - mean) * (value - mean);
        }

        return Math.Sqrt(squares / (values.Count - 1));
    }

    public static double? DeviationOver(IReadOnlyList<SweepBar> bars, int bar, int sessions) =>
        Returns(bars, bar, sessions) is { } returns ? Deviation(returns) : null;

    // The skewness of the returns: their mean cubed deviation over their population standard deviation cubed, none where
    // every return is level.
    public static double? Skewness(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (Returns(bars, bar, sessions) is not { } returns)
        {
            return null;
        }

        var mean = returns.Average();
        var second = returns.Average(value => (value - mean) * (value - mean));
        var third = returns.Average(value => (value - mean) * (value - mean) * (value - mean));

        return second > 0 ? third / Math.Pow(second, 1.5) : null;
    }

    // The mean over the bars of the close's place in the session's range, close less low over high less low, a session
    // whose high is its low left out, none where every one is.
    public static double? ClosePlace(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (bar - sessions + 1 < 0)
        {
            return null;
        }

        var sum = 0.0;
        var held = 0;

        for (var at = bar - sessions + 1; at <= bar; at++)
        {
            var range = Statistic.FromPrice(bars[at].High) - Statistic.FromPrice(bars[at].Low);

            if (range > 0)
            {
                sum += (Close(bars[at]) - Statistic.FromPrice(bars[at].Low)) / range;
                held++;
            }
        }

        return held > 0 ? sum / held : null;
    }

    // The share of the bars' volume traded on bars closing above the close before, none where they traded nothing.
    public static double? UpVolumeShare(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (bar - sessions < 0)
        {
            return null;
        }

        var up = 0.0;
        var all = 0.0;

        for (var at = bar - sessions + 1; at <= bar; at++)
        {
            var volume = Statistic.FromVolume(bars[at].Volume);

            all += volume;
            up += bars[at].Close > bars[at - 1].Close ? volume : 0;
        }

        return all > 0 ? up / all : null;
    }

    // The correlation of two equal series, none where either holds one value throughout.
    public static double? Correlation(IReadOnlyList<double> one, IReadOnlyList<double> other)
    {
        if (one.Count != other.Count || one.Count < 2)
        {
            return null;
        }

        var meanOne = one.Average();
        var meanOther = other.Average();
        var products = 0.0;
        var squaresOne = 0.0;
        var squaresOther = 0.0;

        for (var at = 0; at < one.Count; at++)
        {
            products += (one[at] - meanOne) * (other[at] - meanOther);
            squaresOne += (one[at] - meanOne) * (one[at] - meanOne);
            squaresOther += (other[at] - meanOther) * (other[at] - meanOther);
        }

        return squaresOne > 0 && squaresOther > 0 ? products / Math.Sqrt(squaresOne * squaresOther) : null;
    }

    // The correlation of the daily close change with the daily volume over the bars.
    public static double? PriceVolumeCorrelation(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (bar - sessions < 0)
        {
            return null;
        }

        var changes = new double[sessions];
        var volumes = new double[sessions];

        for (var at = 0; at < sessions; at++)
        {
            var held = bar - sessions + 1 + at;

            changes[at] = Close(bars[held]) - Close(bars[held - 1]);
            volumes[at] = Statistic.FromVolume(bars[held].Volume);
        }

        return Correlation(changes, volumes);
    }

    // Amihud's illiquidity: the base-10 logarithm of the mean of the absolute daily return over the session's close times
    // its volume, a session trading nothing left out, none where the mean is nothing.
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
            var dollars = Close(bars[at]) * Statistic.FromVolume(bars[at].Volume);

            if (dollars > 0 && Return(bars, at) is { } move)
            {
                sum += Math.Abs(move) / dollars;
                held++;
            }
        }

        return held > 0 && sum > 0 ? Math.Log10(sum / held) : null;
    }

    // The overnight returns, each the open over the close before less one, summed over the bars, less the intraday
    // returns, each the close over the open less one, summed over the same; none where a bar holds no open.
    public static double? OvernightLessIntraday(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (bar - sessions < 0)
        {
            return null;
        }

        var overnight = 0.0;
        var intraday = 0.0;

        for (var at = bar - sessions + 1; at <= bar; at++)
        {
            if (bars[at].Open <= 0 || bars[at - 1].Close <= 0)
            {
                return null;
            }

            var open = Statistic.FromPrice(bars[at].Open);

            overnight += (open / Close(bars[at - 1])) - 1;
            intraday += (Close(bars[at]) / open) - 1;
        }

        return overnight - intraday;
    }

    // The close over the lowest low of the bars ending on the session.
    public static double? LowRatio(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (bar - sessions + 1 < 0)
        {
            return null;
        }

        var low = double.MaxValue;

        for (var at = bar - sessions + 1; at <= bar; at++)
        {
            low = Math.Min(low, Statistic.FromPrice(bars[at].Low));
        }

        return low > 0 ? Close(bars[bar]) / low : null;
    }

    // The signed count of the consecutive bars to the session closing the same way on the close before, rises counted up
    // and falls down, stopped at the reach; nought where the session closed level; none where the count runs back to the
    // first bar held, which closes on no close before it.
    public static double? Streak(IReadOnlyList<SweepBar> bars, int bar)
    {
        if (bar < 1)
        {
            return null;
        }

        var way = Math.Sign(bars[bar].Close - bars[bar - 1].Close);

        if (way == 0)
        {
            return 0;
        }

        var count = 0;

        for (var at = bar; count < StreakReach; at--)
        {
            if (at < 1)
            {
                return null;
            }

            if (Math.Sign(bars[at].Close - bars[at - 1].Close) != way)
            {
                break;
            }

            count++;
        }

        return way * count;
    }

    // On-balance volume over the bars, each bar's volume added on a close above the close before and taken off on one
    // below: the slope of its least-squares line on the bars' order, over the bars' mean volume.
    public static double? VolumeSlope(IReadOnlyList<SweepBar> bars, int bar, int sessions)
    {
        if (bar - sessions < 0)
        {
            return null;
        }

        var balance = new double[sessions];
        var order = new double[sessions];
        var running = 0.0;
        var volume = 0.0;

        for (var at = 0; at < sessions; at++)
        {
            var held = bar - sessions + 1 + at;
            var shares = Statistic.FromVolume(bars[held].Volume);

            running += Math.Sign(bars[held].Close - bars[held - 1].Close) * shares;
            balance[at] = running;
            order[at] = at;
            volume += shares;
        }

        var mean = volume / sessions;

        return mean > 0 && Slope(balance, order) is { } slope ? slope / mean : null;
    }

    // The least-squares slope of one series on another, none where the other holds one value throughout.
    public static double? Slope(IReadOnlyList<double> on, IReadOnlyList<double> against)
    {
        if (on.Count != against.Count || on.Count < 2)
        {
            return null;
        }

        var meanOn = on.Average();
        var meanAgainst = against.Average();
        var products = 0.0;
        var squares = 0.0;

        for (var at = 0; at < on.Count; at++)
        {
            products += (on[at] - meanOn) * (against[at] - meanAgainst);
            squares += (against[at] - meanAgainst) * (against[at] - meanAgainst);
        }

        return squares > 0 ? products / squares : null;
    }

    // The member's daily returns and a series' over the same days, the series' close on the last of its sessions on or
    // before a bar's day over its close on or before the bar before's, less one; none where the series does not reach.
    static (double[] Own, double[] Market)? Paired(IReadOnlyList<SweepBar> bars, int bar, int count, IReadOnlyList<(DateOnly Session, double Close)> series)
    {
        if (Returns(bars, bar, count) is not { } own)
        {
            return null;
        }

        var market = new double[count];

        for (var at = 0; at < count; at++)
        {
            var held = bar - count + 1 + at;

            if (SeriesReadings.CloseOn(series, bars[held - 1].Session) is not { } before || before <= 0
                || SeriesReadings.CloseOn(series, bars[held].Session) is not { } after)
            {
                return null;
            }

            market[at] = (after / before) - 1;
        }

        return (own, market);
    }

    // The slope of the member's daily returns on the series' over the returns ending on the bar.
    public static double? Beta(IReadOnlyList<SweepBar> bars, int bar, IReadOnlyList<(DateOnly Session, double Close)> series, int returns) =>
        Paired(bars, bar, returns, series) is { } paired ? Slope(paired.Own, paired.Market) : null;

    // The sample standard deviation of the residuals of the least-squares line of the member's daily returns on the
    // series' over the returns ending on the bar.
    public static double? ResidualDeviation(IReadOnlyList<SweepBar> bars, int bar, IReadOnlyList<(DateOnly Session, double Close)> series, int returns)
    {
        if (Paired(bars, bar, returns, series) is not { } paired || Slope(paired.Own, paired.Market) is not { } slope)
        {
            return null;
        }

        var intercept = paired.Own.Average() - (slope * paired.Market.Average());
        var residuals = new double[returns];

        for (var at = 0; at < returns; at++)
        {
            residuals[at] = paired.Own[at] - intercept - (slope * paired.Market[at]);
        }

        return Deviation(residuals);
    }

    // The member's return over the sessions less the series' between the same two days, the series' close on the last of
    // its sessions on or before each; none where the series starts after the first.
    public static double? RelativeReturn(IReadOnlyList<SweepBar> bars, int bar, IReadOnlyList<(DateOnly Session, double Close)> series, int sessions)
    {
        if (ReturnOver(bars, bar, sessions) is not { } own
            || SeriesReadings.CloseOn(series, bars[bar - sessions].Session) is not { } from || from <= 0
            || SeriesReadings.CloseOn(series, bars[bar].Session) is not { } to)
        {
            return null;
        }

        return own - ((to / from) - 1);
    }

    // A value's place among the values the members hold, the share of them under it and half the share level with it,
    // its own among them.
    public static double Place(double value, IReadOnlyList<double> among)
    {
        var under = 0.0;

        foreach (var other in among)
        {
            under += other < value ? 1 : other == value ? 0.5 : 0;
        }

        return under / among.Count;
    }

    // Alpha#1's Ts_ArgMax(SignedPower(((returns < 0) ? stddev(returns, 20) : close), 2.), 5) of a member on a bar: of its 5
    // bars to the session, the place of the one holding the largest square of the deviation of the 20 returns to it where
    // it fell and of its close where it did not, 1 for the oldest and 5 for the session's own, the older of two equal.
    public static double? ArgMaxValue(IReadOnlyList<SweepBar> bars, int bar)
    {
        if (bar - ArgMaxSessions + 1 - ArgMaxDeviation < 0)
        {
            return null;
        }

        var largest = double.MinValue;
        var place = 0;

        for (var at = 0; at < ArgMaxSessions; at++)
        {
            var held = bar - ArgMaxSessions + 1 + at;

            if (Return(bars, held) is not { } move)
            {
                return null;
            }

            double value;

            if (move < 0)
            {
                if (DeviationOver(bars, held, ArgMaxDeviation) is not { } deviation)
                {
                    return null;
                }

                value = deviation;
            }
            else
            {
                value = Close(bars[held]);
            }

            var square = Math.Sign(value) * value * value;

            if (square > largest)
            {
                largest = square;
                place = at + 1;
            }
        }

        return place;
    }

    // Alpha#2's delta(log(volume), 2): the natural logarithm of the bar's volume less that of the volume 2 bars before,
    // none where either traded nothing.
    public static double? VolumeChange(IReadOnlyList<SweepBar> bars, int bar) =>
        bar - VolumeChangeSessions >= 0 && bars[bar].Volume > 0 && bars[bar - VolumeChangeSessions].Volume > 0
            ? Math.Log(Statistic.FromVolume(bars[bar].Volume)) - Math.Log(Statistic.FromVolume(bars[bar - VolumeChangeSessions].Volume))
            : null;

    // Alpha#2's ((close - open) / open), none where the bar holds no open.
    public static double? IntradayReturn(IReadOnlyList<SweepBar> bars, int bar) =>
        bars[bar].Open > 0 ? (Close(bars[bar]) - Statistic.FromPrice(bars[bar].Open)) / Statistic.FromPrice(bars[bar].Open) : null;

    // Ts_Rank of the newest of a series: its place among the series' values, 1 for the lowest and the count for the
    // highest, a tie taking the mean of the places it shares.
    public static double TimeRank(IReadOnlyList<double> values)
    {
        var newest = values[^1];
        var under = values.Count(value => value < newest);
        var level = values.Count(value => value == newest);

        return under + ((level + 1) / 2.0);
    }

    // Alpha#6, (-1 * correlation(open, volume, 10)), over the member's own bars.
    public static double? Alpha6(IReadOnlyList<SweepBar> bars, int bar)
    {
        if (bar - Alpha6Sessions + 1 < 0)
        {
            return null;
        }

        var opens = new double[Alpha6Sessions];
        var volumes = new double[Alpha6Sessions];

        for (var at = 0; at < Alpha6Sessions; at++)
        {
            var held = bar - Alpha6Sessions + 1 + at;

            if (bars[held].Open <= 0)
            {
                return null;
            }

            opens[at] = Statistic.FromPrice(bars[held].Open);
            volumes[at] = Statistic.FromVolume(bars[held].Volume);
        }

        return Correlation(opens, volumes) is { } correlation ? -correlation : null;
    }
}
