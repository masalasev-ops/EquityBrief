using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Sweep;

// One session of one name as the sweep reads it. The open is read for the gap inside a pullback alone, and a bar
// read before it was carried holds nought there. The unadjusted close is read for a company's value alone, and a bar
// read without it holds nought there.
public readonly record struct SweepBar(DateOnly Session, decimal High, decimal Low, decimal Close, long Volume, decimal Open = 0, decimal RawClose = 0);

// A plan read off one support band or one average: its entry, stop and target, their reward to risk rounded as
// the trade gate rounds it, and the stop's distance below the entry in typical moves.
public readonly record struct SweepPlan(decimal Entry, decimal Stop, decimal Target, decimal RewardToRisk, double StopMoves, decimal? EntryHigh = null);

// The readings a design's axes choose between, each the live filter's own rule where the axis's first choice is
// the live one, and the same rule read over another window, band or trigger for the others. Pure functions of
// one name's series, so a test holds each to the live function it stands beside.
// see: Code owns every number
public static class SweepReadings
{
    // The places among the members' returns, the share of the others strictly lower with a tie counted at half,
    // by sorting rather than by comparing every pair, which over five hundred members a night for eight years
    // is the difference between seconds and minutes. It gives the live function's figures exactly.
    public static Dictionary<int, double> Places(IReadOnlyList<(int Member, double Return)> returns)
    {
        var places = new Dictionary<int, double>(returns.Count);

        if (returns.Count < 2)
        {
            return places;
        }

        var ordered = returns.OrderBy(pair => pair.Return).ToArray();
        var others = returns.Count - 1;
        var at = 0;

        while (at < ordered.Length)
        {
            var end = at;

            while (end + 1 < ordered.Length && ordered[end + 1].Return == ordered[at].Return)
            {
                end++;
            }

            var tied = end - at;

            for (var member = at; member <= end; member++)
            {
                places[ordered[member].Member] = (at + (tied / 2.0)) / others;
            }

            at = end + 1;
        }

        return places;
    }

    // The high of the last `window` sessions ending tonight, the newest session making it, the close's depth
    // below it in typical moves and the median volume since it against the fifty-day average, as the swing
    // readings take them over twenty sessions. None where the series is short of the window.
    public static (double? Depth, double? DryUp) PullbackOver(IReadOnlyList<SweepBar> bars, int end, int window, double? typicalMove, double? volumeAverage)
    {
        if (end + 1 < window)
        {
            return (null, null);
        }

        var start = end - window + 1;
        var top = bars[start].High;
        var at = start;

        for (var index = start; index <= end; index++)
        {
            if (bars[index].High >= top)
            {
                top = bars[index].High;
                at = index;
            }
        }

        double? depth = typicalMove is > 0 and var move ? Statistic.FromPrice(top - bars[end].Close) / move : null;
        double? dryUp = null;

        if (at < end && volumeAverage is > 0 and var average)
        {
            dryUp = SwingReadings.Median([.. Enumerable.Range(at + 1, end - at).Select(index => Statistic.FromVolume(bars[index].Volume))]) / average;
        }

        return (depth, dryUp);
    }

    // A return over a span: the close against the close that many sessions before it, in per cent.
    public static double? ReturnOver(IReadOnlyList<SweepBar> bars, int end, int sessions, int lagging = 0)
    {
        var from = end - sessions;
        var to = end - lagging;

        if (from < 0 || bars[from].Close <= 0)
        {
            return null;
        }

        return Statistic.FromRatio((bars[to].Close - bars[from].Close) / bars[from].Close) * 100;
    }

    // The support band a kind of support reads as the setup's band, the highest holding the close: anchored bands,
    // as the live pullback reads it, or any support band.
    public static FilterBand? BandHolding(IReadOnlyList<FilterBand> bands, decimal close, bool anchoredOnly) =>
        bands
            .Where(band => band.Role == SwingGates.SupportRole && (!anchoredOnly || band.HasNonAverageAnchor) && band.LowEdge < close && close <= band.HighEdge)
            .OrderByDescending(band => band.LowEdge)
            .FirstOrDefault();

    // The average support reads: the higher of the 20-day and the 50-day averages whose zone of half a typical
    // move either side holds the close, as a band of those edges carrying the strength of the band the average
    // sits in, where one holds it, and none where none does. An average the series is too short for is NaN.
    public static (decimal Average, FilterBand Zone)? AverageHolding(IReadOnlyList<FilterBand> bands, decimal close, double twenty, double fifty, decimal typicalMove)
    {
        foreach (var average in new[] { twenty, fifty }.Where(value => !double.IsNaN(value)).Select(Statistic.ToPrice).OrderByDescending(value => value))
        {
            var low = PriceForm.Round(average - (typicalMove / 2), Statistic.Places);
            var high = PriceForm.Round(average + (typicalMove / 2), Statistic.Places);

            if (low < close && close <= high)
            {
                var strength = bands
                    .Where(band => band.LowEdge <= average && average <= band.HighEdge)
                    .Select(band => band.Strength)
                    .DefaultIfEmpty(0)
                    .Max();

                return (average, new FilterBand(low, high, SwingGates.SupportRole, strength, true));
            }
        }

        return null;
    }

    // The trigger's event on one session: the trigger's own close test, or the close back inside or above the
    // setup's band after a session closing below its low edge. None where the session has none before it.
    public static bool? Event(TriggerKind trigger, SweepBar tonight, SweepBar? before, FilterBand? band)
    {
        if (before is not { } previous)
        {
            return null;
        }

        var fired = trigger switch
        {
            TriggerKind.AbovePreviousHigh => tonight.Close > previous.High,
            TriggerKind.AbovePreviousClose => tonight.Close > previous.Close,
            _ => tonight.High > tonight.Low && (tonight.Close - tonight.Low) * 4 >= (tonight.High - tonight.Low) * 3,
        };

        return fired || (band is not null && previous.Close < band.LowEdge && tonight.Close >= band.LowEdge);
    }

    // The plan at the nearest bands: entered at the close, stopped at the setup band's low edge, or at the stop
    // handed in, and won at the lowest low edge of a band above the close.
    public static SweepPlan? Nearest(IReadOnlyList<FilterBand> bands, decimal close, decimal stop, double? typicalMove)
    {
        var above = bands.Where(band => band.LowEdge > close).Select(band => band.LowEdge).DefaultIfEmpty(0).Min();

        if (above <= 0 || close <= stop)
        {
            return null;
        }

        return Plan(close, stop, above, typicalMove);
    }

    // Section 10's plan: entered at the close, stopped at the setup band's low edge, or at the next support band
    // beneath it where that is less than a typical move below the entry, and won at the lowest low edge of a band
    // two typical moves or more above. A stop handed in, as the average's is, is taken as it stands.
    public static SweepPlan? Clear(IReadOnlyList<FilterBand> bands, decimal close, FilterBand? setupBand, decimal? fixedStop, double? typicalMove)
    {
        if (typicalMove is not { } move || move <= 0)
        {
            return null;
        }

        decimal? stop = fixedStop;

        if (stop is null && setupBand is not null)
        {
            stop = Statistic.FromPrice(close - setupBand.LowEdge) / move < SwingGates.ClearStopMoves
                ? bands
                    .Where(band => band.Role == SwingGates.SupportRole && band.LowEdge < setupBand.LowEdge)
                    .MaxBy(band => band.LowEdge)?.LowEdge
                : setupBand.LowEdge;
        }

        if (stop is not { } placed || close <= placed)
        {
            return null;
        }

        var target = bands
            .Where(band => band.LowEdge > close && Statistic.FromPrice(band.LowEdge - close) / move >= SwingGates.ClearTargetMoves)
            .MinBy(band => band.LowEdge)?.LowEdge;

        return target is { } above ? Plan(close, placed, above, typicalMove) : null;
    }

    static SweepPlan? Plan(decimal entry, decimal stop, decimal target, double? typicalMove)
    {
        if (typicalMove is not { } move || move <= 0 || entry <= stop)
        {
            return null;
        }

        return new SweepPlan(
            entry,
            stop,
            target,
            Math.Round((target - entry) / (entry - stop), PriceForm.Places, MidpointRounding.AwayFromZero),
            Statistic.FromPrice(entry - stop) / move);
    }
}
