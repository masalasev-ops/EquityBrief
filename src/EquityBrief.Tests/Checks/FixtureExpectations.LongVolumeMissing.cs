using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction on the operator's answers of 2026-11-23 at 14:00: a bar with no volume whose
// close moved traded with its volume missing, one whose close stood did not; two codes whose shared days carry no volume are
// priced on the same days and their closes decide; the table's volume readings read none on such a session and its liquidity
// leaves the day out; and the spans of the three codes the operator ended run to their last traded price, each worked by hand.
// see: A bar whose close moved with no volume traded with its volume missing
// see: The spans of AM_old, BEL and PSD past their last traded price end there on the operator's word
public partial class FixtureExpectations
{
    static SweepBar Quiet(int day, decimal close, long volume, decimal raw) =>
        new(new DateOnly(2012, 1, 3).AddDays(day), close, close, close, volume, close, raw);

    // Closes 10, 11, 11, 12 and 12, volume on the first alone: the second and fourth moved and traded with their volume
    // missing, the third and fifth stood and did not; a first bar with no volume has none before it and did not; a stored close
    // that moved while the close traded at stood did not.
    [Fact]
    public void ACloseThatMovedWithNoVolumeTradedWithItsVolumeMissingAndOneThatStoodDidNot()
    {
        SweepBar[] bars = [Quiet(0, 10m, 500, 10m), Quiet(1, 11m, 0, 11m), Quiet(2, 11m, 0, 11m), Quiet(3, 12m, 0, 12m), Quiet(4, 12.5m, 0, 12m)];

        Assert.Equal([bars[0].Session, bars[1].Session, bars[3].Session], LongTraded.Of(bars).Select(bar => bar.Session));
        Assert.False(LongTraded.VolumeMissing(bars, 0));
        Assert.True(LongTraded.VolumeMissing(bars, 1));
        Assert.False(LongTraded.Traded(bars, 2));
        Assert.True(LongTraded.VolumeMissing(bars, 3));
        Assert.False(LongTraded.Traded(bars, 4));
        Assert.Empty(LongTraded.Of([Quiet(0, 10m, 0, 10m)]));
    }

    // Forty days the other code prices at twice the close with no volume: before, the two read as never priced on the same day
    // and joined; now they are priced together, their closes in one ratio join them and closes drifting apart do not.
    [Fact]
    public void TwoCodesWhoseSharedDaysCarryNoVolumeArePricedTogetherAndTheirClosesDecide()
    {
        SweepBar[] own = [.. Enumerable.Range(0, 40).Select(day => Quiet(day, 10m + (day % 5), 1000, 10m + (day % 5)))];
        SweepBar[] other = [.. own.Select(bar => bar with { High = bar.Close * 2m, Low = bar.Close * 2m, Close = bar.Close * 2m, Open = bar.Close * 2m, RawClose = bar.Close * 2m, Volume = 0 })];
        SweepBar[] drifting = [.. other.Select((bar, day) => bar with { Close = bar.Close * (1m + (day * 0.001m)), RawClose = bar.RawClose * (1m + (day * 0.001m)) })];

        Assert.Equal(LongRenames.ConstantRatio, LongRenames.Test(own, other));
        Assert.Null(LongRenames.Test(own, drifting));
    }

    // A window of the liquidity's sessions at a raw close of 20 on 1,000 shares reads 20,000 dollars a day; a day whose close
    // moved with no volume is left out and the mean stands, and a day whose close stood with none is read as nothing traded.
    // The table's volume readings read none on the day whose volume is missing and keep their value on a day that carries one.
    [Fact]
    public void TheTablesVolumeReadingsReadNoneWhereTheVolumeIsMissing()
    {
        var count = LedgerReadings.LiquiditySessions + 1;
        SweepBar[] bars = [.. Enumerable.Range(0, count).Select(day => Quiet(day, 20m + (day % 2), 1000, 20m))];
        bars[^1] = bars[^1] with { Volume = 0, Close = 25m, RawClose = 25m };

        SweepBar[] stood = [.. bars];
        stood[^1] = stood[^2] with { Session = stood[^1].Session, Volume = 0 };

        Assert.Equal(Math.Log10(20_000), TableReadings.Liquidity(bars, count - 1)!.Value, 9);
        Assert.True(TableReadings.Liquidity(stood, count - 1)!.Value < Math.Log10(20_000));
        Assert.All(TableReadings.VolumeColumns, column => Assert.True(LedgerReadings.IndexOf(column) >= 0, column));

        var sessionAt = bars.Select((bar, at) => (bar.Session, at)).ToDictionary(pair => pair.Session, pair => pair.at);
        var series = SweepColumns.Series(new SweepName("AAA", bars, [(null, null)], []), sessionAt);
        var members = new SweepBenchmark.Members([.. bars.Select(_ => new[] { 0 })], [.. bars.Select((_, at) => new[] { at })]);

        double?[] Read(int bar)
        {
            var readings = Enumerable.Repeat((double?)1.0, LedgerReadings.All.Count).ToArray();

            TableOwnReadings.Apply(readings, new TradedRanks([series], members), 0, series, bar);

            return readings;
        }

        var missing = Read(count - 1);
        var carried = Read(count - 2);

        Assert.All(TableReadings.VolumeColumns, column => Assert.Null(missing[LedgerReadings.IndexOf(column)]));
        Assert.Equal(1.0, carried[LedgerReadings.IndexOf("volume_ratio")]);
        Assert.Equal(1.0, missing[LedgerReadings.IndexOf("rsi")]);
    }

    // A span running 199 sessions past its code's last traded price with its filer reporting on keeps its sessions; on the
    // operator's word it ends on the session after that price. The operator ended AM_old, BEL and PSD.
    [Fact]
    public void ASpanTheOperatorEndedRunsToItsLastTradedPrice()
    {
        DateOnly[] calendar = [.. Enumerable.Range(0, 300).Select(day => new DateOnly(2013, 1, 2).AddDays(day))];
        (DateOnly? Joined, DateOnly? Left)[] spans = [(calendar[0], calendar[^1])];

        Assert.Equal(spans, LongMarks.EndedAtLastPrice(spans, calendar[100], calendar, _ => false));
        Assert.Equal([(calendar[0], (DateOnly?)calendar[101])], LongMarks.EndedAtLastPrice(spans, calendar[100], calendar, _ => false, onTheWord: true));
        Assert.Equal(["AM_old", "BEL", "PSD"], LongMarks.EndedOnTheWord.Order(StringComparer.Ordinal));
    }
}
