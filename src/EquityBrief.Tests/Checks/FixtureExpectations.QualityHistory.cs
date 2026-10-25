using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Quality;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// Quality at a fair price's history test, from 22.10: the grid's 72 settings; one setting's walk over a constructed tape
// of four members and two reshuffles worked by hand, its holdings' results and their benchmark, the edge its figures read
// and the months the tester judges; the floor, the valuation and the trend on both sides at a reshuffle; and a sale on a
// filing that drops the score, worked by hand beside the same walk selling at reshuffles only.
// see: Quality at a fair price's history test walks the book over the pulled history on 72 settings an index and registers nothing
// see: A quality holding is sold at a reshuffle where it no longer qualifies and at its last close as a member, and is bought at the next open
public partial class FixtureExpectations
{
    // The rows 22.10 adds that this check reaches: section 17's grid.
    internal static readonly string[] QualityHistoryClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Quality at a fair price's history test"),
    ];

    // Six sessions, the first and the fourth each month's first. A closes at 100 rising 2 a session, B at 50 falling 1, C at
    // 20 rising 1, D flat at 10; every one a member throughout.
    static HeavyweightTape QualityTape()
    {
        double[][] close =
        [
            [100, 102, 104, 106, 108, 110],
            [50, 49, 48, 47, 46, 45],
            [20, 21, 22, 23, 24, 25],
            [10, 10, 10, 10, 10, 10],
        ];
        bool[][] member = [.. close.Select(row => row.Select(_ => true).ToArray())];

        return new HeavyweightTape
        {
            Tickers = ["A", "B", "C", "D"],
            Calendar = [new(2026, 1, 2), new(2026, 1, 5), new(2026, 1, 6), new(2026, 2, 2), new(2026, 2, 3), new(2026, 2, 4)],
            Close = close,
            Average200 = [.. close.Select(row => row.Select(_ => 0.0).ToArray())],
            Member = member,
            LastMemberClose = [.. close.Select((row, name) => HeavyweightTape.LastMemberCloseOf(member[name], row))],
            Index = [.. Enumerable.Repeat(double.NaN, 6)],
            First = 0,
        };
    }

    static readonly IReadOnlyList<int> QualityReshuffles = [0, 3];

    // At the first reshuffle A scores 8 of 9 and is cheaper than in 80 per cent of its quarters, B 7 of 9 at 60, C 6 of 9 at
    // 90, under the floor, and D 9 of 9 at 40, dearer than its median; at the second B falls to 6 of 9 and C rises to 7.
    static Dictionary<int, QualityReading?[]> QualityReadings(bool aAboveAverage = true) => new()
    {
        [0] = [new(8, 9, 0.8, aAboveAverage), new(7, 9, 0.6, true), new(6, 9, 0.9, true), new(9, 9, 0.4, true)],
        [3] = [new(8, 9, 0.8, true), new(6, 9, 0.6, true), new(7, 9, 0.9, true), new(9, 9, 0.4, true)],
    };

    static IReadOnlyList<IReadOnlyDictionary<int, QualityReading>> NoFilings() => [.. Enumerable.Range(0, 4).Select(_ => new Dictionary<int, QualityReading>())];

    static string[] Held(HeavyweightTape tape, IEnumerable<HeavyweightTrade> trades) =>
        [.. trades.Select(trade => FormattableString.Invariant($"{tape.Tickers[trade.Name]}|{trade.Entry}|{(trade.End is { } end ? end.ToString(System.Globalization.CultureInfo.InvariantCulture) : "open")}|{trade.Reason ?? "held"}"))];

    [Fact]
    public void TheQualityGridHoldsSeventyTwoSettingsTheStartingOneAmongThem()
    {
        Assert.Equal(72, QualityRule.Grid.Count);
        Assert.Equal(72, QualityRule.Grid.Select(setting => setting.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(QualityRule.Starting, QualityRule.Grid);
        Assert.Equal("floor=7|valuation=median|trend=off|book=10|sale=reshuffle", QualityRule.Starting.Key);
        Assert.Equal(2, (QualityRule.Starting with { ScoreFloorInNine = 8, Holdings = 20 }).Changes(QualityRule.Starting));
    }

    // One setting's walk worked by hand: a book of two at the starting floor and valuation. The first reshuffle chooses A
    // then B by the score's share, C under the floor and D dearer than its median. At the second B, fallen to 6 of 9, is
    // sold at its close of 47, and C, risen to 7 of 9, fills its place. B's result is 47 / 50 - 1 = -6 per cent; its
    // benchmark the four members answered on its night from 0 to 3, A 6, B -6, C 15 and D 0 per cent, a mean of 3.75; its
    // edge -9.75 points. A and C are still held at the history's end.
    [Fact]
    public void AQualityWalksTradesAreWorkedByHandAndTheTesterReadsTheSameMonths()
    {
        var tape = QualityTape();
        var setting = QualityRule.Starting with { Holdings = 2 };
        var (trades, rebalances) = QualityLay.Walk(tape, QualityReshuffles, QualityReadings(), NoFilings(), setting);
        var expected = Expected("quality-history").GetProperty("reshuffle");

        Assert.Equal(["B|0|3|" + QualityFamily.NoLongerQualifies, "A|0|open|held", "C|3|open|held"], Held(tape, trades));

        var sold = trades[0];

        Assert.Equal(expected.GetProperty("result").GetDouble(), sold.Result!.Value, 12);
        Assert.Equal(expected.GetProperty("benchmark").GetDouble(), sold.Cut!.Value, 12);
        Assert.Equal(expected.GetProperty("edge").GetDouble(), sold.Edge!.Value, 12);
        Assert.Equal(["A", "B"], rebalances[0].Sectors[0].Leaders.Select(name => tape.Tickers[name]));
        Assert.Equal(["C"], rebalances[3].Sectors[0].Leaders.Select(name => tape.Tickers[name]));
        Assert.Equal([0, 1, 2, 3], rebalances[0].Sectors[0].Cut);

        // The figures read the same edge over the one holding ended, and the tester's month from the first reshuffle to the
        // second reads A's and B's returns less the members' over it, a mean of (6 - 3.75 - 6 - 3.75) / 2 = -3.75 points.
        Assert.Equal(expected.GetProperty("edge").GetDouble(), HeavyweightSweep.Figures(setting.Key, trades).Edge!.Value, 12);

        double? Close(int name, int from, int to) => (tape.Close[name][to] / tape.Close[name][from]) - 1.0;

        var holdings = trades.Select(trade => new BookHolding(
            trade.Entry,
            trade.End,
            0,
            (from, to) => Close(trade.Name, from, to),
            (from, to) => rebalances[trade.Entry].Sectors[0].Cut.Average(one => Close(one, from, to) ?? 0.0))).ToArray();

        Assert.Equal(expected.GetProperty("month").GetDouble(), BookMonths.Edges(QualityReshuffles, holdings)[0].Edge!.Value, 12);
    }

    // The floor, the valuation and the trend each on both sides at the first reshuffle: at a floor of 6 C's 6 of 9 qualifies
    // and its place of 90 ranks it before B's 60 at 7 of 9 only after the share; with the valuation off D's 9 of 9 comes
    // first; and with the trend read A, closing under its average, is passed over for B and C's turn never comes at 7.
    [Fact]
    public void AQualityReshufflesFloorValuationAndTrendAreWorkedByHandOnBothSides()
    {
        var tape = QualityTape();
        var two = QualityRule.Starting with { Holdings = 2 };

        string[] Chosen(QualitySettings setting, bool aAboveAverage = true) =>
            [.. QualityLay.Walk(tape, QualityReshuffles, QualityReadings(aAboveAverage), NoFilings(), setting).Rebalances[0].Sectors[0].Leaders.Select(name => tape.Tickers[name])];

        Assert.Equal(["A", "B"], Chosen(two));
        Assert.Equal(["A", "B", "C"], Chosen(two with { ScoreFloorInNine = 6, Holdings = 3 }));
        Assert.Equal(["A"], Chosen(two with { ScoreFloorInNine = 8 }));
        Assert.Equal(["D", "A"], Chosen(two with { Valuation = QualityValuation.Off }));
        Assert.Equal(["A"], Chosen(two with { Valuation = QualityValuation.CheapestThird }));
        Assert.Equal(["A", "B"], Chosen(two with { Trend = true }));
        Assert.Equal(["B"], Chosen(two with { Trend = true }, aAboveAverage: false));
    }

    // A sale on a filing worked by hand: A's first session after a filing, the second, reads 5 of 9, under the floor. Sold
    // on a filing, A goes at that close of 102, 2 per cent, against the members' 2, -2, 5 and 0 from its night, 1.25; sold
    // at reshuffles only it is kept, and is still held at the history's end. A filing read on its own night sells nothing,
    // and nor does one holding the score at the floor.
    [Fact]
    public void AQualitySaleOnAFilingThatDropsTheScoreIsWorkedByHand()
    {
        var tape = QualityTape();
        var expected = Expected("quality-history").GetProperty("filing");
        IReadOnlyList<IReadOnlyDictionary<int, QualityReading>> filings =
        [
            new Dictionary<int, QualityReading> { [0] = new(5, 9, 0.8, true), [1] = new(5, 9, 0.8, true) },
            new Dictionary<int, QualityReading> { [2] = new(7, 9, 0.6, true) },
            new Dictionary<int, QualityReading>(),
            new Dictionary<int, QualityReading>(),
        ];

        var (onFiling, _) = QualityLay.Walk(tape, QualityReshuffles, QualityReadings(), filings, QualityRule.Starting with { Holdings = 2, SellOnFiling = true });
        var (reshuffleOnly, _) = QualityLay.Walk(tape, QualityReshuffles, QualityReadings(), filings, QualityRule.Starting with { Holdings = 2 });
        var sold = onFiling.Single(trade => tape.Tickers[trade.Name] == "A" && trade.End is not null);

        Assert.Equal(1, sold.End);
        Assert.Equal(QualityLay.OnAFiling, sold.Reason);
        Assert.Equal(expected.GetProperty("result").GetDouble(), sold.Result!.Value, 12);
        Assert.Equal(expected.GetProperty("benchmark").GetDouble(), sold.Cut!.Value, 12);
        Assert.Contains("A|0|open|held", Held(tape, reshuffleOnly));

        // B's filing read on the third session holds its score at 7 of 9, at the floor, so it sells nothing: B is sold
        // only at the second reshuffle, where its score has fallen.
        Assert.Contains("B|0|3|" + QualityFamily.NoLongerQualifies, Held(tape, onFiling));
    }
}
