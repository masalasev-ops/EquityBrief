using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 23.10's second part: the entry check's readings join the ledger's catalogue, each read as of the setup's session through the
// entry check's own function, and the one-off item's share off the newest results release accepted by the session; the
// conditions the entry check's measurement named stand on readings the catalogue holds; and a frozen rule's walk over its own
// sweep keeps the listings its conditions pass. Each worked by hand over a constructed member.
// see: The entry check's readings join the ledger's catalogue at the second restart, read as of each setup's session
public partial class FixtureExpectations
{
    [Fact]
    public void TheEntryChecksReadingsStandInTheCatalogueReadAsOfEachSetupsSession()
    {
        // Three hundred sessions of one member climbing a tenth a day from 100 with a range of a dollar each side, its volume a
        // million; on the last it gaps up to close at 140, its low 135 over the 130.8 high the day before, on three million.
        var calendar = Enumerable.Range(0, 300).Select(at => new DateOnly(2025, 1, 6).AddDays(at)).ToArray();
        var bars = calendar.Select((session, at) =>
        {
            var close = at == 299 ? 140m : 100m + (at / 10m);

            return at == 299
                ? new SweepBar(session, 141m, 135m, close, 3_000_000, 136m, close)
                : new SweepBar(session, close + 1m, close - 1m, close, 1_000_000, close, close);
        }).ToArray();
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new[] { SweepColumns.Series(new SweepName("AAA", bars, [(null, null)], [calendar[70]]), sessionAt) };
        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var closes = series[0].Bars.Select(one => Statistic.FromPrice(one.Close)).ToArray();
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);
        var market = new LedgerMarket(new Dictionary<string, IReadOnlyList<(DateOnly, double)>>(StringComparer.Ordinal)
        {
            [LedgerMarket.Index] = [.. calendar.Select(session => (session, 100.0))],
        });

        // Two releases: one on the 290th session naming a one-off item of 10 cents a share tied to earnings a share and raising
        // them, beside a 40-cent beat; and one on the 295th naming none.
        var context = new LedgerContext(new Dictionary<string, IReadOnlyList<FiledIncome>>(), new Dictionary<string, string?>(), highs, lows)
        {
            OneOffs = new Dictionary<string, IReadOnlyList<LedgerOneOff>>(StringComparer.Ordinal)
            {
                ["AAA"] = [new(calendar[290], 0.10m, true, 1, 0.40m), new(calendar[295], null, null, null, 0.05m)],
            },
        };

        double?[] Read(int bar) => LedgerSetups.Readings("GSPC", 0, series[0], closes, bar, sessions[bar], members, calendar, bar, market, context);

        static double? Of(double?[] readings, string column) => readings[LedgerReadings.IndexOf(column)];

        var night = Read(299);
        var before = Read(298);
        var start = series[0].WindowStart[299];
        var entry = EntryReadings.Of(
            "AAA",
            [.. bars[start..300].Select(one => new EntryBar(one.Session, one.High, one.Low, one.Close, one.Volume))],
            [.. bars[..start].Select(one => new EntryBar(one.Session, one.High, one.Low, one.Close, one.Volume))],
            series[0].Atr[299],
            series[0].Rsi[299],
            series[0].Volume50[299])!;

        // The stretch, its share and the gain over 20 sessions are the entry check's own figures: the gain 140 over 127.9, the
        // close twenty sessions before, less one, in per cent.
        Assert.Equal((entry.StretchDays, entry.StretchShare, entry.GainTwenty), (Of(night, "stretch_days"), Of(night, "stretch_share"), Of(night, "gain_twenty")));
        Assert.Equal(((140.0 / 127.9) - 1) * 100, Of(night, "gain_twenty")!.Value, 9);

        // The gap day is the year's largest gain, the day before none; the volume three million over the 50 sessions' mean.
        Assert.Equal((1.0, 0.0), (Of(night, "largest_gain"), Of(before, "largest_gain")));
        Assert.Equal(3_000_000 / series[0].Volume50[299], Of(night, "night_volume_ratio")!.Value, 9);

        // The gap is 135 less 130.8, 4.2, in the night's typical moves; the day before stood no gap, so nought.
        Assert.Equal(4.2 / series[0].Atr[299], Of(night, "gap_days")!.Value, 9);
        Assert.Equal(0.0, Of(before, "gap_days"));

        // Every close before the night stood under 140, so the lookback runs to the history's first session, 299 days before.
        Assert.Equal(299.0 / 365, Of(night, "highest_since_years")!.Value, 9);
        Assert.Equal(entry.OlderDays, Of(night, "older_days"));

        // The one-off item's share: none before the first release, a quarter between the two, and nought once the newest names none.
        Assert.Null(Of(Read(289), "one_off_share"));
        Assert.Equal(0.25, Of(Read(293), "one_off_share")!.Value, 9);
        Assert.Equal(0.0, Of(night, "one_off_share"));
    }

    [Fact]
    public void TheDriftConditionsTheEntryChecksMeasurementNamedReadTheCataloguesReadingsAndHoldEachAlone()
    {
        // Each condition parses as the engines' hooks and reads a reading the catalogue holds, each tested alone.
        foreach (var (_, hooks) in RuleTests.LargeDriftConditions.Concat(RuleTests.MidDriftConditions).Concat(RuleTests.SmallDriftConditions))
        {
            var read = RuleHooks.Of(hooks);

            Assert.NotEmpty(read.Also);
            Assert.All(read.Also, condition => Assert.True(LedgerReadings.IndexOf(condition.Column) >= 0));
            Assert.True(read.Score.Count == 0 && read.Exit == 0 && read.Plan is null && read.Exposure is null);
        }

        Assert.Equal(3, RuleTests.LargeDriftConditions.Count);
        Assert.Single(RuleTests.MidDriftConditions);
        Assert.Equal(2, RuleTests.SmallDriftConditions.Count);

        // The stretch of 2 to 4 holds at 3 and at its two ends, and neither under 2 nor over 4.
        var stretch = RuleHooks.Of(RuleTests.LargeDriftConditions[2].Hooks);
        var at = LedgerReadings.IndexOf("stretch_days");

        bool Holds(double? value)
        {
            var readings = new double?[LedgerReadings.Count];

            readings[at] = value;

            return stretch.Passes(readings);
        }

        Assert.Equal([false, true, true, true, false, false], new double?[] { 1.99, 2, 3, 4, 4.01, null }.Select(Holds));
    }
}
