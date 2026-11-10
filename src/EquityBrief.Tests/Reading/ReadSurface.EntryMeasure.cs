using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using Xunit;

namespace EquityBrief.Tests.Reading;

// see: The entry check's measurement is read-only over the stored years and registers nothing
public sealed partial class ReadSurface
{
    static EntryReading Banded(
        double? days = null,
        double? share = null,
        double? gain = null,
        double? rsi = null,
        bool? largest = null,
        double? volume = null,
        decimal? gapHigh = null,
        decimal? gapLow = null,
        double? typical = 2,
        DateOnly? since = null,
        DateOnly? start = null,
        decimal? older = null,
        double? olderDays = null,
        DateOnly? release = null,
        decimal? amount = null,
        bool? tied = null,
        int? direction = null,
        decimal? beat = null) =>
        new("HAND", EntryNight, 100m, typical, 1, days, share, gain, rsi, largest, volume, gapHigh, gapLow, since, start, older, older + 1, null, olderDays, release, null, amount, tied, direction, beat);

    static string BandOf(EntryReading reading, string name, double? stopDays = null) =>
        EntryMeasure.BandsOf(reading, stopDays).Single(one => one.Reading == name).Band;

    [Theory]
    [InlineData(1.9, "under 2")]
    [InlineData(2.0, "2 to 4")]
    [InlineData(4.0, "2 to 4")]
    [InlineData(4.1, "over 4")]
    public void AStretchAndAStopAreBandedAtTwoAndFourTypicalDays(double value, string band)
    {
        Assert.Equal(band, BandOf(Banded(days: value), EntryMeasure.Stretch));
        Assert.Equal(band, BandOf(Banded(), EntryMeasure.StopDistance, value));
    }

    [Fact]
    public void EachOtherReadingIsBandedAtItsEdges()
    {
        Assert.Equal(EntryMeasure.Unread, BandOf(Banded(), EntryMeasure.Stretch));
        Assert.Equal(EntryMeasure.Unread, BandOf(Banded(days: double.NaN), EntryMeasure.Stretch));

        Assert.Equal("under 10%", BandOf(Banded(gain: 9.9), EntryMeasure.GainTwenty));
        Assert.Equal("10% to 25%", BandOf(Banded(gain: 25), EntryMeasure.GainTwenty));
        Assert.Equal("over 25%", BandOf(Banded(gain: 25.1), EntryMeasure.GainTwenty));
        Assert.Equal("under 60", BandOf(Banded(rsi: 59), EntryMeasure.Rsi));
        Assert.Equal("60 to 70", BandOf(Banded(rsi: 70), EntryMeasure.Rsi));
        Assert.Equal("over 70", BandOf(Banded(rsi: 71), EntryMeasure.Rsi));
        Assert.Equal("under 1.5", BandOf(Banded(volume: 1.4), EntryMeasure.VolumeRatio));
        Assert.Equal("1.5 to 3", BandOf(Banded(volume: 3), EntryMeasure.VolumeRatio));
        Assert.Equal("over 3", BandOf(Banded(volume: 3.5), EntryMeasure.VolumeRatio));

        Assert.Equal("yes", BandOf(Banded(largest: true), EntryMeasure.LargestGain));
        Assert.Equal("no", BandOf(Banded(largest: false), EntryMeasure.LargestGain));
        Assert.Equal(EntryMeasure.Unread, BandOf(Banded(), EntryMeasure.LargestGain));

        // A share of 5% with the close above its average is at the card's line, and 5.1% is not; below its average is never stretched.
        Assert.Equal("yes", BandOf(Banded(days: 1, share: 5), EntryMeasure.StretchedAtTheLine));
        Assert.Equal("no", BandOf(Banded(days: 1, share: 5.1), EntryMeasure.StretchedAtTheLine));
        Assert.Equal("no", BandOf(Banded(days: -1, share: 2), EntryMeasure.StretchedAtTheLine));
        Assert.Equal(EntryMeasure.Unread, BandOf(Banded(days: 1), EntryMeasure.StretchedAtTheLine));

        // A gap of 2 at a typical day of 2 is one day; 2.2 is over; no typical day reads none of it.
        Assert.Equal("no gap", BandOf(Banded(), EntryMeasure.GapSize));
        Assert.Equal("up to 1", BandOf(Banded(gapHigh: 98m, gapLow: 100m), EntryMeasure.GapSize));
        Assert.Equal("over 1", BandOf(Banded(gapHigh: 97.8m, gapLow: 100m), EntryMeasure.GapSize));
        Assert.Equal(EntryMeasure.Unread, BandOf(Banded(gapHigh: 98m, gapLow: 100m, typical: null), EntryMeasure.GapSize));

        Assert.Equal("none above", BandOf(Banded(), EntryMeasure.OlderResistance));
        Assert.Equal("under 3", BandOf(Banded(older: 110m, olderDays: 2.9), EntryMeasure.OlderResistance));
        Assert.Equal("3 to 10", BandOf(Banded(older: 110m, olderDays: 10), EntryMeasure.OlderResistance));
        Assert.Equal("over 10", BandOf(Banded(older: 110m, olderDays: 10.1), EntryMeasure.OlderResistance));
    }

    [Fact]
    public void AHighestCloseIsBandedUnderOneOneToThreeAndOverThreeYears()
    {
        Assert.Equal("under 1 year", BandOf(Banded(since: EntryNight.AddDays(-364)), EntryMeasure.HighestClose));
        Assert.Equal("1 to 3 years", BandOf(Banded(since: EntryNight.AddDays(-365)), EntryMeasure.HighestClose));
        Assert.Equal("1 to 3 years", BandOf(Banded(since: EntryNight.AddDays(-1095)), EntryMeasure.HighestClose));
        Assert.Equal("over 3 years", BandOf(Banded(since: EntryNight.AddDays(-1096)), EntryMeasure.HighestClose));

        // A close never matched reads from where the history starts, so two months of history is no long lookback.
        Assert.Equal("under 1 year", BandOf(Banded(start: EntryNight.AddDays(-60)), EntryMeasure.HighestClose));
        Assert.Equal("over 3 years", BandOf(Banded(start: EntryNight.AddDays(-2000)), EntryMeasure.HighestClose));
        Assert.Equal(EntryMeasure.Unread, BandOf(Banded(), EntryMeasure.HighestClose));
    }

    [Fact]
    public void AOneOffIsBandedOverHalfOfTheBeatOrNot()
    {
        var on = EntryNight.AddDays(-10);

        Assert.Equal(EntryMeasure.NoRelease, BandOf(Banded(), EntryMeasure.OneOffShare));
        Assert.Equal(EntryMeasure.NoItem, BandOf(Banded(release: on), EntryMeasure.OneOffShare));

        // SWK's own: 0.67 of a 0.77 beat. Exactly half is not over it.
        Assert.Equal(EntryMeasure.OverHalf, BandOf(Banded(release: on, amount: 0.67m, tied: true, direction: 1, beat: 0.77m), EntryMeasure.OneOffShare));
        Assert.Equal(EntryMeasure.HalfOrLess, BandOf(Banded(release: on, amount: 0.385m, tied: true, direction: 1, beat: 0.77m), EntryMeasure.OneOffShare));

        // An item untied, of no stated way, with no beat or a miss has no share read.
        Assert.Equal(EntryMeasure.NoShare, BandOf(Banded(release: on, amount: 0.67m, tied: false, direction: 1, beat: 0.77m), EntryMeasure.OneOffShare));
        Assert.Equal(EntryMeasure.NoShare, BandOf(Banded(release: on, amount: 0.67m, tied: true, beat: 0.77m), EntryMeasure.OneOffShare));
        Assert.Equal(EntryMeasure.NoShare, BandOf(Banded(release: on, amount: 0.67m, tied: true, direction: 1), EntryMeasure.OneOffShare));
        Assert.Equal(EntryMeasure.NoShare, BandOf(Banded(release: on, amount: 0.67m, tied: true, direction: 1, beat: -0.10m), EntryMeasure.OneOffShare));
    }

    [Fact]
    public void TheSpansSplitAt2025()
    {
        Assert.Equal(EntryMeasure.BeforeRecent, EntryMeasure.EraOf(new DateOnly(2024, 12, 31)));
        Assert.Equal(EntryMeasure.FromRecent, EntryMeasure.EraOf(new DateOnly(2025, 1, 2)));
    }

    // Bars after the pick, at bar 0: opens 105, 103, 99, 96; lows 104, 101, 98, 95.
    static readonly double[] WaitOpens = [100, 105, 103, 99, 96];
    static readonly double[] WaitLows = [99, 104, 101, 98, 95];

    [Fact]
    public void AWaitEntryFillsOnTheFirstSessionThatTradesIntoItsLevel()
    {
        // A level of 102: bar 1's low of 104 stays above it; bar 2 opens at 103 above it and its low of 101 reaches it, so it fills at the level.
        Assert.Equal(new WaitFill(2, 102), EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 102, 90, 3));

        // A level of 100: bar 2's low of 101 stays above; bar 3 opens at 99 under it, so it fills at that open, not at the level.
        Assert.Equal(new WaitFill(3, 99), EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 100, 90, 3));

        // The window counts sessions after the pick: the same level with only two sessions never fills.
        Assert.Null(EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 100, 90, 2));

        // A session opening exactly at the level fills at it, and one reaching it exactly fills at it.
        Assert.Equal(new WaitFill(2, 103), EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 103, 90, 3));
        Assert.Equal(new WaitFill(1, 104), EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 104, 90, 3));

        // The series ending inside the window is no fill, and nor is anything on the pick's own session.
        Assert.Null(EntryMeasure.FillOf(WaitOpens, WaitLows, 3, 90, 80, 10));
        Assert.Null(EntryMeasure.FillOf(WaitOpens, WaitLows, 4, 200, 80, 10));
    }

    [Fact]
    public void AWaitEntryAtOrUnderItsStopIsNotTaken()
    {
        // A level at or under the stop is no plan at all.
        Assert.Null(EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 102, 102, 3));
        Assert.Null(EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 102, 103, 3));

        // A session opening under the level and at or under the stop has already broken the plan: bar 3's open of 99 against a stop of 99 and of 99.5.
        Assert.Null(EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 100, 99, 3));
        Assert.Null(EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 100, 99.5, 3));
        Assert.Equal(new WaitFill(3, 99), EntryMeasure.FillOf(WaitOpens, WaitLows, 0, 100, 98.9, 3));

        // An open not held is passed over rather than read as a fill at nothing.
        Assert.Equal(new WaitFill(2, 102), EntryMeasure.FillOf([100, 0, 103], [99, 0, 101], 0, 102, 90, 3));
    }

    [Fact]
    public void AResultInItsOwnRiskIsRestatedInTheRulesRisk()
    {
        // 2 risks of 8 dollars is 16 dollars, which is 4 of the rule's risks of 4 dollars.
        Assert.Equal(4, EntryMeasure.InTheRulesRisk(2, 8, 4), 6);
        Assert.Equal(2, EntryMeasure.InTheRulesRisk(2, 8, 0), 6);
    }

    [Fact]
    public void AGroupReadsItsMeansAndRatesOverItsOwnTrades()
    {
        // Results 2, -1 and 2 mean 1; one edge not read leaves the mean of 0.5 and 1.5, 1.0 over two; two of three at the target and one at the
        // stop. Percents 10 and 20 mean 15 over the two held, and own risks of 1, 3 and 5 mean 3.
        var group = EntryMeasure.GroupOf([new(2, 0.5, SetupEnds.Target, 10, 1), new(-1, null, SetupEnds.Stop, null, 3), new(2, 1.5, SetupEnds.Target, 20, 5)]);

        Assert.Equal((3, 2), (group.Trades, group.Edges));
        Assert.Equal(1, group.MeanResult!.Value, 6);
        Assert.Equal(1, group.MeanEdge!.Value, 6);
        Assert.Equal(200d / 3, group.TargetRate!.Value, 6);
        Assert.Equal(100d / 3, group.StopRate!.Value, 6);
        Assert.Equal(0, group.TrailRate!.Value, 6);
        Assert.Equal(15, group.MeanPercent!.Value, 6);
        Assert.Equal(3, group.MeanOwnRisk!.Value, 6);

        // A trail counts as neither a target nor a stop, and a cap as none of the three.
        var ends = EntryMeasure.GroupOf([new(1, null, SetupEnds.Trail), new(1, null, SetupEnds.Cap)]);

        Assert.Equal((0d, 0d, 50d), (ends.TargetRate!.Value, ends.StopRate!.Value, ends.TrailRate!.Value));
        Assert.Null(ends.MeanEdge);
        Assert.Null(ends.MeanPercent);
        Assert.Null(ends.MeanOwnRisk);

        var none = EntryMeasure.GroupOf([]);

        Assert.Equal(0, none.Trades);
        Assert.Null(none.MeanResult);
    }
}
