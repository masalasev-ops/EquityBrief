using EquityBrief.Core.Bars;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Swings;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Worker.Sweep;

// One name's series and the readings the sweep takes of it on every session it holds, each the function the
// night calls or the live rule's own read over another window, computed once over the whole series. Wilder's
// averages are seeded where the night's year of bars begins on each session, as the night seeds them.
public sealed class SweepSeries
{
    public required SweepName Name { get; init; }

    public required SweepBar[] Bars { get; init; }

    // Each bar's place among the history's sessions.
    public required int[] SessionAt { get; init; }

    // The first bar of the year the night held on each bar's session.
    public required int[] WindowStart { get; init; }

    public required double[] Sma20 { get; init; }

    public required double[] Sma50 { get; init; }

    public required double[] Sma200 { get; init; }

    public required double[] Atr { get; init; }

    public required double[] Rsi { get; init; }

    public required double[] Volume50 { get; init; }

    public required Swing[] Swings { get; init; }

    // How many of the swings, in order of the session each was confirmed on, are confirmed by each bar.
    public required int[] Confirmed { get; init; }

    public required bool[] Member { get; init; }

    public required bool[] Gap { get; init; }

    public required string[] Label { get; init; }

    public required byte[] Uptrend { get; init; }

    public required double[] Return63 { get; init; }

    public required double[] Return126 { get; init; }

    public required double[] ReturnTwelveLessOne { get; init; }

    // Depth and dry-up at each reference high, by bar and then by the high's place among the reference highs.
    public required double[] Depth { get; init; }

    public required double[] DryUp { get; init; }

    // The conditions' readings: the highest high of the last 252 sessions, the range's tightness, the session's
    // volume over the fifty-session average before it, whether the RSI rose on the session, and by reference
    // high the sessions since it, the largest gap down inside the pullback and the lowest RSI since the high.
    public required double[] High252 { get; init; }

    public required double[] Tightness { get; init; }

    public required double[] VolumeRatio { get; init; }

    public required bool[] RsiUp { get; init; }

    public required int[] SinceHigh { get; init; }

    public required double[] GapDown { get; init; }

    public required double[] RsiLow { get; init; }

    // Each surprise's reaction bar, the first bar on or after its reaction session, and for each bar the newest
    // surprise whose reaction bar is on or before it, -1 where none.
    public required int[] SurpriseBar { get; init; }

    public required int[] NewestSurprise { get; init; }
}

// The sweep's per-name arithmetic: every reading a design's axes choose between, the bands and setups, the
// triggers' events and arrivals, the plans, and each plan's outcome under every exit with the calibrated bar
// its own paths set. Pure over the history it is handed.
public static class SweepColumns
{
    public static readonly DateOnly FirstScored = new(2019, 1, 2);

    // The arrival window's longest, the fourteen sessions the widest freshness the search may look at reads back.
    public const int LongestWindow = 14;

    // The gap check's reach, the year of sessions the night checks a name's series over.
    public const int GapSessions = 252;

    public static SweepSeries Series(SweepName name, IReadOnlyDictionary<DateOnly, int> sessionAt)
    {
        // A bar on a day the history's calendar does not hold is a day the exchange did not trade.
        var bars = name.Bars.Where(bar => sessionAt.ContainsKey(bar.Session)).ToArray();
        var at = bars.Select(bar => sessionAt[bar.Session]).ToArray();
        var count = bars.Length;

        var sma20 = Nan(count);
        var sma50 = Nan(count);
        var sma200 = Nan(count);
        var volume50 = Nan(count);

        if (count > 0)
        {
            var points = IndicatorSeries.For([.. bars.Select(bar => new SeriesBar(bar.Session, Statistic.FromPrice(bar.High), Statistic.FromPrice(bar.Low), Statistic.FromPrice(bar.Close), Statistic.FromVolume(bar.Volume)))]);
            var index = bars.Select((bar, position) => (bar.Session, position)).ToDictionary(pair => pair.Session, pair => pair.position);

            foreach (var point in points)
            {
                if (point.Value is not { } value)
                {
                    continue;
                }

                var position = index[point.SessionDate];

                switch (point.Name)
                {
                    case IndicatorSeries.Sma20: sma20[position] = value; break;
                    case IndicatorSeries.Sma50: sma50[position] = value; break;
                    case IndicatorSeries.Sma200: sma200[position] = value; break;
                    case IndicatorSeries.VolAvg50: volume50[position] = value; break;
                }
            }
        }

        var windowStart = SweepWindows.WindowStarts(bars);
        var (atr, rsi) = SweepWindows.Wilder(bars, windowStart);

        var swings = SwingSeries.For([.. bars.Select(bar => new SwingBar(bar.Session, bar.High, bar.Low))])
            .OrderBy(swing => swing.ConfirmedOn)
            .ThenBy(swing => swing.SessionDate)
            .ToArray();
        var confirmed = new int[count];

        for (int bar = 0, swing = 0; bar < count; bar++)
        {
            while (swing < swings.Length && swings[swing].ConfirmedOn <= bars[bar].Session)
            {
                swing++;
            }

            confirmed[bar] = swing;
        }

        var member = bars.Select(bar => name.MemberOn(bar.Session)).ToArray();
        var gap = new bool[count];

        // A gap is a session of the year behind the bar, from the series' own first session on, the series does
        // not hold, which the night reads as a gap and excludes the name on.
        for (int bar = 0, first = 0; bar < count; bar++)
        {
            var start = Math.Max(at[0], at[bar] - GapSessions + 1);

            while (at[first] < start)
            {
                first++;
            }

            gap[bar] = bar - first + 1 < at[bar] - start + 1;
        }

        var highs = SweepAxes.ReferenceHighs.Count;
        var label = new string[count];
        var uptrend = new byte[count];
        var return63 = Nan(count);
        var return126 = Nan(count);
        var twelve = Nan(count);
        var depth = Nan(count * highs);
        var dryUp = Nan(count * highs);
        var high252 = Nan(count);
        var tightness = Nan(count);
        var volumeRatio = Nan(count);
        var rsiUp = new bool[count];
        var sinceHigh = new int[count * highs];
        var gapDown = Nan(count * highs);
        var rsiLow = Nan(count * highs);

        for (var bar = 0; bar < count; bar++)
        {
            var close = bars[bar].Close;
            decimal? shortAverage = double.IsNaN(sma50[bar]) ? null : Statistic.ToPrice(sma50[bar]);
            decimal? longAverage = double.IsNaN(sma200[bar]) ? null : Statistic.ToPrice(sma200[bar]);

            // The classifier reads the last two swings of each kind, which are the last confirmed, since a swing
            // is confirmed three sessions after its own, among the swings the night's year of bars holds.
            var oldest = bars[windowStart[bar]].Session;
            var recent = swings.AsSpan(Math.Max(0, confirmed[bar] - 24), Math.Min(24, confirmed[bar])).ToArray().Where(swing => swing.SessionDate >= oldest).TakeLast(12).ToArray();

            label[bar] = TrendSeries.For(close, shortAverage, longAverage, recent).State;

            var before = bar > 0 ? label[bar - 1] : null;
            byte mask = 0;

            void Up(UptrendRule rule, bool holds)
            {
                if (holds)
                {
                    mask |= (byte)(1 << (int)rule);
                }
            }

            Up(UptrendRule.Classifier, label[bar] == TrendState.Uptrend);
            Up(UptrendRule.ClassifierBelowBoth, TrendSeries.Applied(label[bar], close, shortAverage, longAverage, [], TheTrendVersionsRules.BelowBoth) == TrendState.Uptrend);
            Up(UptrendRule.ClassifierBelowBothUnderACross, TrendSeries.Applied(label[bar], close, shortAverage, longAverage, [], TheTrendVersionsRules.BelowBothUnderACross) == TrendState.Uptrend);
            Up(UptrendRule.ClassifierHoldsTwoNights, TrendSeries.Applied(label[bar], close, shortAverage, longAverage, before is null ? [] : [before], TheTrendVersionsRules.HoldsTwoNights) == TrendState.Uptrend);
            Up(UptrendRule.CloseAboveTwoHundred, !double.IsNaN(sma200[bar]) && Statistic.FromPrice(close) > sma200[bar]);
            Up(UptrendRule.FiftyAboveTwoHundred, !double.IsNaN(sma50[bar]) && !double.IsNaN(sma200[bar]) && sma50[bar] > sma200[bar]);
            Up(UptrendRule.RisingTwoHundred, bar >= SweepAxes.RisingSpan && !double.IsNaN(sma200[bar]) && !double.IsNaN(sma200[bar - SweepAxes.RisingSpan]) && sma200[bar] > sma200[bar - SweepAxes.RisingSpan]);

            uptrend[bar] = mask;
            return63[bar] = SweepReadings.ReturnOver(bars, bar, SwingReadings.ReturnShortSessions) ?? double.NaN;
            return126[bar] = SweepReadings.ReturnOver(bars, bar, SwingReadings.ReturnLongSessions) ?? double.NaN;
            twelve[bar] = SweepReadings.ReturnOver(bars, bar, SweepAxes.TwelveMonthSessions, SweepAxes.LatestMonthSessions) ?? double.NaN;
            high252[bar] = SweepWindows.HighestHigh(bars, bar, SweepConditions.HighSessions);
            tightness[bar] = SweepWindows.Tightness(bars, bar);
            volumeRatio[bar] = SweepWindows.VolumeRatio(bars, bar, volume50);
            rsiUp[bar] = bar > 0 && !double.IsNaN(rsi[bar]) && !double.IsNaN(rsi[bar - 1]) && rsi[bar] > rsi[bar - 1];

            for (var high = 0; high < highs; high++)
            {
                var (d, v) = SweepReadings.PullbackOver(bars, bar, SweepAxes.ReferenceHighs[high], Held(atr[bar]), Held(volume50[bar]));
                var since = SweepWindows.SessionsSinceHigh(bars, bar, SweepAxes.ReferenceHighs[high]);

                depth[(bar * highs) + high] = d ?? double.NaN;
                dryUp[(bar * highs) + high] = v ?? double.NaN;
                sinceHigh[(bar * highs) + high] = since;
                gapDown[(bar * highs) + high] = SweepWindows.LargestGapDown(bars, bar, since, atr[bar]);
                rsiLow[(bar * highs) + high] = since < 0 ? double.NaN : LowestOver(rsi, bar - since, bar);
            }
        }

        var surpriseBar = new int[name.Surprises.Count];
        var newestSurprise = new int[count];

        Array.Fill(newestSurprise, -1);

        for (var surprise = 0; surprise < name.Surprises.Count; surprise++)
        {
            var one = name.Surprises[surprise];

            // A report before the open or of unstated timing moves that day's bar, and one after the close the
            // next session's, as the earnings rule reads a print.
            surpriseBar[surprise] = Array.FindIndex(bars, bar => one.After ? bar.Session > one.EventDate : bar.Session >= one.EventDate);
        }

        for (int bar = 0, newest = -1, next = 0; bar < count; bar++)
        {
            while (next < surpriseBar.Length && surpriseBar[next] >= 0 && surpriseBar[next] <= bar)
            {
                newest = next++;
            }

            newestSurprise[bar] = newest;
        }

        return new SweepSeries
        {
            Name = name,
            Bars = bars,
            SessionAt = at,
            WindowStart = windowStart,
            Sma20 = sma20,
            Sma50 = sma50,
            Sma200 = sma200,
            Atr = atr,
            Rsi = rsi,
            Volume50 = volume50,
            Swings = swings,
            Confirmed = confirmed,
            Member = member,
            Gap = gap,
            Label = label,
            Uptrend = uptrend,
            Return63 = return63,
            Return126 = return126,
            ReturnTwelveLessOne = twelve,
            Depth = depth,
            DryUp = dryUp,
            High252 = high252,
            Tightness = tightness,
            VolumeRatio = volumeRatio,
            RsiUp = rsiUp,
            SinceHigh = sinceHigh,
            GapDown = gapDown,
            RsiLow = rsiLow,
            SurpriseBar = surpriseBar,
            NewestSurprise = newestSurprise,
        };
    }

    static double LowestOver(double[] values, int from, int to)
    {
        var lowest = double.NaN;

        for (var at = Math.Max(0, from); at <= to; at++)
        {
            if (!double.IsNaN(values[at]) && (double.IsNaN(lowest) || values[at] < lowest))
            {
                lowest = values[at];
            }
        }

        return lowest;
    }

    // Each session's cross-section: the members read on it, each measure's place among them, the breadth, and
    // each sector's rank by the median 126-session return of its members labelled on the session, 1 the strongest.
    public sealed record Session(double? Breadth, Dictionary<int, double>[] Strength, IReadOnlyDictionary<string, int> SectorRanks);

    // The places and the breadth over every session. The members read on a session are those the index held with
    // a bar on it and no gap, as the night reads its members, and the members counted are every name the index
    // held that night, bar or none, which is what breadth's half is read against.
    public static Session[] Sessions(IReadOnlyList<SweepSeries> series, IReadOnlyList<DateOnly> calendar)
    {
        var read = new List<(int Name, int Bar)>[calendar.Count];
        var members = new int[calendar.Count];

        for (var session = 0; session < calendar.Count; session++)
        {
            read[session] = [];
        }

        for (var name = 0; name < series.Count; name++)
        {
            var one = series[name];

            for (var session = 0; session < calendar.Count; session++)
            {
                members[session] += one.Name.MemberOn(calendar[session]) ? 1 : 0;
            }

            for (var bar = 0; bar < one.Bars.Length; bar++)
            {
                if (one.Member[bar] && !one.Gap[bar])
                {
                    read[one.SessionAt[bar]].Add((name, bar));
                }
            }
        }

        var sessions = new Session[calendar.Count];

        Parallel.For(0, calendar.Count, session => sessions[session] = SessionOf(series, read[session], members[session]));

        return sessions;
    }

    static Session SessionOf(IReadOnlyList<SweepSeries> series, List<(int Name, int Bar)> read, int members)
    {
        var shortReturns = read.Where(pair => !double.IsNaN(series[pair.Name].Return63[pair.Bar])).Select(pair => (pair.Name, series[pair.Name].Return63[pair.Bar])).ToArray();
        var longReturns = read.Where(pair => !double.IsNaN(series[pair.Name].Return126[pair.Bar])).Select(pair => (pair.Name, series[pair.Name].Return126[pair.Bar])).ToArray();
        var twelveReturns = read.Where(pair => !double.IsNaN(series[pair.Name].ReturnTwelveLessOne[pair.Bar])).Select(pair => (pair.Name, series[pair.Name].ReturnTwelveLessOne[pair.Bar])).ToArray();

        var placesShort = SweepReadings.Places(shortReturns);
        var placesLong = SweepReadings.Places(longReturns);
        var placesTwelve = SweepReadings.Places(twelveReturns);

        var live = new Dictionary<int, double>();

        foreach (var (name, place) in placesShort)
        {
            if (placesLong.TryGetValue(name, out var other))
            {
                live[name] = (place + other) / 2;
            }
        }

        var held = read
            .Where(pair => !double.IsNaN(series[pair.Name].Sma200[pair.Bar]))
            .Select(pair => (series[pair.Name].Bars[pair.Bar].Close, series[pair.Name].Sma200[pair.Bar]))
            .ToArray();

        return new Session(SwingReadings.BreadthOf(members, held).Share, [live, placesLong, placesTwelve], SectorRanks(series, longReturns));
    }

    // Each sector's rank by the median of its labelled members' 126-session returns, the highest median first;
    // a name with no sector is in no sector's median and is ranked in none.
    public static IReadOnlyDictionary<string, int> SectorRanks(IReadOnlyList<SweepSeries> series, IReadOnlyList<(int Name, double Return)> returns)
    {
        var bySector = new Dictionary<string, List<double>>(StringComparer.Ordinal);

        foreach (var (name, value) in returns)
        {
            if (series[name].Name.Sector is not { } sector)
            {
                continue;
            }

            if (!bySector.TryGetValue(sector, out var held))
            {
                bySector[sector] = held = [];
            }

            held.Add(value);
        }

        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var rank = 0;

        foreach (var (sector, _) in bySector
            .Select(pair => (Sector: pair.Key, Median: SwingReadings.Median([.. pair.Value]) ?? double.NaN))
            .Where(pair => !double.IsNaN(pair.Median))
            .OrderByDescending(pair => pair.Median)
            .ThenBy(pair => pair.Sector, StringComparer.Ordinal))
        {
            ranks[sector] = ++rank;
        }

        return ranks;
    }

    static double? Held(double value) => double.IsNaN(value) ? null : value;

    static double[] Nan(int count)
    {
        var values = new double[count];

        Array.Fill(values, double.NaN);

        return values;
    }
}

// The trend rule's three versions as the sweep reads them, the ones the version scorer opens by name.
public static class TheTrendVersionsRules
{
    public static TrendRuleSet BelowBoth { get; } = EquityBrief.Worker.Rules.TheTrendVersions.Named(EquityBrief.Worker.Rules.TheTrendVersions.BelowBothAverages)!.Rules;

    public static TrendRuleSet BelowBothUnderACross { get; } = EquityBrief.Worker.Rules.TheTrendVersions.Named(EquityBrief.Worker.Rules.TheTrendVersions.BelowBothUnderACross)!.Rules;

    public static TrendRuleSet HoldsTwoNights { get; } = EquityBrief.Worker.Rules.TheTrendVersions.Named(EquityBrief.Worker.Rules.TheTrendVersions.TheNewLabelHoldsTwoNights)!.Rules;
}
