using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Swings;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The seven conditions, each read as the data stood on the session over constructed bars, and Wilder's averages
// seeded where the night's year of bars begins: the readings the sweep's columns take are the night's own
// function over the same window, worked by hand at each condition's edge.
public partial class FixtureExpectations
{
    // A year and a half of weekday sessions from 2024-01-02, each bar's prices a function of its place so a
    // reading can be worked by hand.
    static SweepBar[] SweepBars(int count, Func<int, (decimal Open, decimal High, decimal Low, decimal Close, long Volume)> shape)
    {
        var bars = new List<SweepBar>();

        for (var day = new DateOnly(2024, 1, 2); bars.Count < count; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            {
                continue;
            }

            var (open, high, low, close, volume) = shape(bars.Count);

            bars.Add(new SweepBar(day, high, low, close, volume, open));
        }

        return [.. bars];
    }

    static SweepSeries SweepSeriesOf(SweepBar[] bars, string? sector = null, IReadOnlyList<SweepSurprise>? surprises = null)
    {
        var sessionAt = bars.Select((bar, at) => (bar.Session, at)).ToDictionary(pair => pair.Session, pair => pair.at);

        return SweepColumns.Series(new SweepName("CON", bars, [(null, null)], [], sector, surprises), sessionAt);
    }

    [Fact]
    public void TheSwingsHeldOnASessionAreTheOnesTheSwingFinderGivesOverTheNightsYearAlone()
    {
        // Eight hundred sessions with a random walk. On each bar past the first year the night's series is the
        // year of bars to it, and its swing finder judges none of that series' first three sessions, so a swing
        // made among them, which the whole history's finder sees, is one the night never held. The sweep's
        // swings on the bar are exactly the finder's over the year's bars, confirmed by the bar, and differ from
        // the whole history's where the year's first three sessions hold a swing.
        var random = new Random(20260930);
        var close = 100m;
        var bars = SweepBars(800, _ =>
        {
            close = Math.Max(10m, close + (decimal)Math.Round((random.NextDouble() - 0.5) * 4, 2));

            return (close, close + 1, close - 1, close, 1_000);
        });
        var series = SweepSeriesOf(bars);
        var compared = 0;
        var differed = 0;

        for (var bar = 260; bar < 800; bar += 7)
        {
            var start = series.WindowStart[bar];
            var window = bars.Skip(start).Take(bar - start + 1).Select(one => new SwingBar(one.Session, one.High, one.Low)).ToArray();
            var night = SwingSeries.For(window).Where(swing => swing.ConfirmedOn <= bars[bar].Session).OrderBy(swing => swing.SessionDate).ThenBy(swing => swing.Direction, StringComparer.Ordinal).ToArray();
            var sweep = SweepCandidates.SwingsHeld(series, bar).OrderBy(swing => swing.SessionDate).ThenBy(swing => swing.Direction, StringComparer.Ordinal).ToArray();
            var whole = series.Swings.AsSpan(0, series.Confirmed[bar]).ToArray().Where(swing => swing.SessionDate >= bars[start].Session).Count();

            Assert.Equal(night, sweep);

            compared++;
            differed += whole != sweep.Length ? 1 : 0;
        }

        Assert.Equal(78, compared);
        Assert.True(differed > 0, "the year's first three sessions never held a swing, so the rule was not read.");
    }

    [Fact]
    public void WildersAveragesAreSeededWhereTheNightsYearOfBarsBeginsAndMatchTheNightsOwnFunctionOverThatWindow()
    {
        // Eight hundred sessions with a random walk, so the seed differs from the whole series' seed. On each bar
        // from the 260th, the night's year holds about 252 bars; the sweep's ATR and RSI equal the indicator
        // series' own over exactly those bars, and differ from the whole series' where the window has moved.
        // Wilder's smoothing forgets its seed by thirteen fourteenths a session, so the difference is in the
        // low digits and shows at the end of a series a year longer than the window.
        var random = new Random(20260930);
        var close = 100m;
        var bars = SweepBars(800, _ =>
        {
            close = Math.Max(10m, close + (decimal)Math.Round((random.NextDouble() - 0.48) * 3, 2));

            return (close, close + 1.5m, close - 1.5m, close, 1_000);
        });
        var series = SweepSeriesOf(bars);
        var whole = IndicatorSeries.For([.. bars.Select(bar => new SeriesBar(bar.Session, Statistic.FromPrice(bar.High), Statistic.FromPrice(bar.Low), Statistic.FromPrice(bar.Close), Statistic.FromVolume(bar.Volume)))]);
        var compared = 0;
        var differed = 0;

        foreach (var bar in new[] { 20, 100, 259, 300, 520, 799 })
        {
            var start = series.WindowStart[bar];

            Assert.True(bars[start].Session >= bars[bar].Session.AddYears(-1));
            Assert.True(start == 0 || bars[start - 1].Session < bars[bar].Session.AddYears(-1));

            var window = IndicatorSeries.For([.. bars.Skip(start).Take(bar - start + 1).Select(one => new SeriesBar(one.Session, Statistic.FromPrice(one.High), Statistic.FromPrice(one.Low), Statistic.FromPrice(one.Close), Statistic.FromVolume(one.Volume)))]);
            var atr = window.Single(point => point.Name == IndicatorSeries.Atr14 && point.SessionDate == bars[bar].Session).Value;
            var rsi = window.Single(point => point.Name == IndicatorSeries.Rsi14 && point.SessionDate == bars[bar].Session).Value;

            Assert.Equal(atr ?? double.NaN, series.Atr[bar], 12);
            Assert.Equal(rsi ?? double.NaN, series.Rsi[bar], 12);

            var wholeAtr = whole.Single(point => point.Name == IndicatorSeries.Atr14 && point.SessionDate == bars[bar].Session).Value;

            compared++;
            differed += start > 0 && (wholeAtr ?? double.NaN) != series.Atr[bar] ? 1 : 0;
        }

        Assert.Equal(6, compared);
        Assert.True(differed > 0, "the whole series' seed and the year's never differed, so the window was not read.");

        // The averages and the fifty-day volume are the same over either window, being means of the last bars.
        Assert.Equal(whole.Single(point => point.Name == IndicatorSeries.Sma50 && point.SessionDate == bars[799].Session).Value, series.Sma50[799]);
    }

    [Fact]
    public void EachConditionsReadingIsWorkedByHandAtItsEdgeOverConstructedBars()
    {
        // 1. The 52-week high: 300 sessions rising a point a session to 350 at the 250th, then falling a point a
        // session, each bar opening at the close before it so the ramp holds no gap. On the 299th bar the highest
        // high of the last 252 sessions is 351 (the high sits a point over the close) and the close is 301, a
        // ratio of 301/351; on a bar before the 252nd there is no reading.
        static decimal RampClose(int at) => at <= 250 ? 100m + at : 350m - (at - 250);

        var ramp = SweepBars(300, at =>
        {
            var close = RampClose(at);

            return (at == 0 ? close : RampClose(at - 1), close + 1, close - 1, close, 1_000 + (at == 280 ? 4_000 : 0));
        });
        var series = SweepSeriesOf(ramp);

        Assert.Equal(351, series.High252[299]);
        Assert.True(double.IsNaN(series.High252[250]));
        Assert.Equal(Statistic.FromPrice(301m) / 351, SweepWindows.HighestHigh(ramp, 299, 252) > 0 ? Statistic.FromPrice(ramp[299].Close) / series.High252[299] : 0, 12);

        // 3. The turn-up day's volume: the 280th bar's volume, 5,000, over the fifty-session average to the 279th,
        // 1,000: a ratio of 5; the bar after reads 1,000 over an average now carrying the spike.
        Assert.Equal(5.0, series.VolumeRatio[280], 9);
        Assert.Equal(1_000.0 / ((49 * 1_000 + 5_000) / 50.0), series.VolumeRatio[281], 9);

        // 7. The pullback's shape at the 20-session high: on the 260th bar the high was made on the 250th, ten
        // sessions back; with no gap the largest gap down is nought. A bar opening two points under the close
        // before, inside the pullback, reads a gap of two over the typical move.
        Assert.Equal(10, series.SinceHigh[(260 * 3) + 1]);
        Assert.Equal(0, series.GapDown[(260 * 3) + 1]);

        var gapped = SweepBars(300, at =>
        {
            var close = RampClose(at);

            return (at == 255 ? close - 2 : at == 0 ? close : RampClose(at - 1), close + 1, close - 1, close, 1_000);
        });
        var gap = SweepSeriesOf(gapped);

        Assert.Equal(Statistic.FromPrice(gapped[254].Close - gapped[255].Open) / gap.Atr[260], gap.GapDown[(260 * 3) + 1], 9);
        Assert.Equal(Statistic.FromPrice(3m) / gap.Atr[260], gap.GapDown[(260 * 3) + 1], 9);

        // 4. The momentum reset: the RSI's lowest since the high and whether it rose on the session; on the ramp
        // down the RSI falls session by session and never rises, and the lowest since the high is the bar's own.
        Assert.Equal(series.Rsi[260], series.RsiLow[(260 * 3) + 1], 12);
        Assert.False(series.RsiUp[260]);

        // On a random walk the RSI rises on some sessions and not others, and the flag reads exactly that.
        var walkRandom = new Random(7);
        var walkClose = 100m;
        var walk = SweepSeriesOf(SweepBars(120, _ =>
        {
            walkClose = Math.Max(10m, walkClose + (decimal)Math.Round((walkRandom.NextDouble() - 0.5) * 4, 2));

            return (walkClose, walkClose + 1, walkClose - 1, walkClose, 1_000);
        }));

        Assert.Contains(true, walk.RsiUp);
        Assert.Contains(false, walk.RsiUp.Skip(20));
        Assert.All(Enumerable.Range(16, 104), bar => Assert.Equal(walk.Rsi[bar] > walk.Rsi[bar - 1], walk.RsiUp[bar]));

        // 6. The tightness is the swing reading's own function over the bars to the session.
        var window = ramp.Take(261).Select(bar => new ReadingBar(bar.Session, bar.High, bar.Low, bar.Close, bar.Volume)).ToArray();

        Assert.Equal(SwingReadings.Tightness(window)!.Value, series.Tightness[260], 12);

        // 5. The surprise: a print reported after the close on the 240th bar's date moves the 241st bar, one
        // before the open on the 245th moves that bar, and each bar reads the newest print on or before it.
        var surprised = SweepSeriesOf(ramp, "Technology", [new SweepSurprise(ramp[240].Session, true, 4.5), new SweepSurprise(ramp[245].Session, false, -2)]);

        Assert.Equal([241, 245], surprised.SurpriseBar);
        Assert.Equal(-1, surprised.NewestSurprise[240]);
        Assert.Equal(0, surprised.NewestSurprise[241]);
        Assert.Equal(0, surprised.NewestSurprise[244]);
        Assert.Equal(1, surprised.NewestSurprise[245]);
        Assert.Equal(1, surprised.NewestSurprise[299]);

        // 2. The sector's rank: three sectors' medians of the 126-session return, the highest first, a name with
        // no sector in none.
        var flat = SweepBars(300, at => (100m, 101m, 99m, 100m, 1_000));
        var rising = SweepBars(300, at => (100m + at, 101m + at, 99m + at, 100m + at, 1_000));
        var falling = SweepBars(300, at => (400m - at, 401m - at, 399m - at, 400m - at, 1_000));
        var names = new[] { SweepSeriesOf(rising, "Technology"), SweepSeriesOf(flat, "Utilities"), SweepSeriesOf(falling, "Energy"), SweepSeriesOf(rising, null) };
        var ranks = SweepColumns.SectorRanks(names, [.. names.Select((name, at) => (at, name.Return126[299]))]);

        Assert.Equal(new Dictionary<string, int> { ["Technology"] = 1, ["Utilities"] = 2, ["Energy"] = 3 }, ranks);

        // Each condition's pass at its edge: the ratio at the level passes and a hundredth under does not; a rank
        // of 3 passes the top 3 and 4 does not; a reading that is not available fails a condition that is on.
        var readings = new ConditionSetting.Readings(0.90f, 3, 1.5f, 34f, true, 20, 5f, 0.75f, 15, 1.0f);

        Assert.True(SweepConditions.OnAtTheMiddle(1).Passes(readings with { HighRatio = 0.85f }));
        Assert.False(SweepConditions.OnAtTheMiddle(1).Passes(readings with { HighRatio = 0.849f }));
        Assert.False(SweepConditions.OnAtTheMiddle(1).Passes(readings with { HighRatio = float.NaN }));
        Assert.True((ConditionSetting.Off with { Sector = 3 }).Passes(readings with { SectorRank = 3 }));
        Assert.False((ConditionSetting.Off with { Sector = 3 }).Passes(readings with { SectorRank = 4 }));
        Assert.False((ConditionSetting.Off with { Sector = 3 }).Passes(readings with { SectorRank = -1 }));
        Assert.True((ConditionSetting.Off with { Volume = 2 }).Passes(readings with { TurnVolume = 1.5f }));
        Assert.False((ConditionSetting.Off with { Volume = 2 }).Passes(readings with { TurnVolume = 1.49f }));
        Assert.True((ConditionSetting.Off with { Rsi = 2 }).Passes(readings with { RsiLow = 34.9f, RsiUp = true }));
        Assert.False((ConditionSetting.Off with { Rsi = 2 }).Passes(readings with { RsiLow = 35f, RsiUp = true }));
        Assert.False((ConditionSetting.Off with { Rsi = 2 }).Passes(readings with { RsiLow = 30f, RsiUp = false }));
        Assert.True((ConditionSetting.Off with { BeatWindow = 2, BeatSize = 1 }).Passes(readings with { SurpriseSessions = 20, SurprisePercent = 5f }));
        Assert.False((ConditionSetting.Off with { BeatWindow = 2, BeatSize = 1 }).Passes(readings with { SurpriseSessions = 21, SurprisePercent = 5f }));
        Assert.False((ConditionSetting.Off with { BeatWindow = 2, BeatSize = 1 }).Passes(readings with { SurpriseSessions = 20, SurprisePercent = 4.99f }));
        Assert.True((ConditionSetting.Off with { BeatWindow = 2, BeatSize = 0 }).Passes(readings with { SurpriseSessions = 20, SurprisePercent = 0.01f }));
        Assert.False((ConditionSetting.Off with { BeatWindow = 2, BeatSize = 0 }).Passes(readings with { SurpriseSessions = 20, SurprisePercent = 0f }));
        Assert.False((ConditionSetting.Off with { BeatWindow = 2, BeatSize = 0 }).Passes(readings with { SurpriseSessions = -1 }));
        Assert.True((ConditionSetting.Off with { Tightness = 1 }).Passes(readings with { Tightness = 0.75f }));
        Assert.False((ConditionSetting.Off with { Tightness = 1 }).Passes(readings with { Tightness = 0.751f }));
        Assert.True((ConditionSetting.Off with { Length = 1 }).Passes(readings with { PullbackSessions = 15 }));
        Assert.False((ConditionSetting.Off with { Length = 1 }).Passes(readings with { PullbackSessions = 16 }));
        Assert.True((ConditionSetting.Off with { Gap = 1 }).Passes(readings with { GapMoves = 1.49f }));
        Assert.False((ConditionSetting.Off with { Gap = 1 }).Passes(readings with { GapMoves = 1.5f }));
        Assert.True(ConditionSetting.Off.Passes(readings with { HighRatio = float.NaN, SectorRank = -1, SurpriseSessions = -1 }));

        // The beyond values read through the same index: a 52-week high of 1.0 is the fifth value.
        Assert.True((ConditionSetting.Off with { High = 4 }).Passes(readings with { HighRatio = 1.0f }));
        Assert.False((ConditionSetting.Off with { High = 4 }).Passes(readings with { HighRatio = 0.999f }));
    }
}
