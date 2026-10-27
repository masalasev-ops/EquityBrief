using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Releases;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The Fundamentals family's history test: the grid's 144 settings; one filing's trade walked over a constructed tape of
// four members worked by hand, its result and its benchmark, the edge its figures read and the month the tester judges;
// each dial on both sides, the buy the night's own at the starting setting; the sales on a falling score, at the time
// limit and on leaving the index; and a constructed filer replayed night by night, its filing read on the first session
// after its day and a flag standing from its release the night before.
// see: The Fundamentals family's history test walks its trades over the pulled history on 144 settings an index and registers nothing
// see: A Fundamentals trade is held while its score stays at seven in nine, and judged against an equal-weighted hold of its index's members after its round trip
public partial class FixtureExpectations
{
    // The rows 22.14 adds that this check reaches: section 17's grid.
    internal static readonly string[] ImprovingHistoryClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The Fundamentals family's history test"),
    ];

    // Eight sessions, the first and the fifth each month's first. A closes at 100 rising 2 a session, B at 50 falling 1, C
    // at 20 rising 1, D flat at 10; each a member throughout, but for B from the seventh session where it leaves.
    static HeavyweightTape ImprovingTape(bool bLeaves = false)
    {
        double[][] close =
        [
            [100, 102, 104, 106, 108, 110, 112, 114],
            [50, 49, 48, 47, 46, 45, 44, 43],
            [20, 21, 22, 23, 24, 25, 26, 27],
            [10, 10, 10, 10, 10, 10, 10, 10],
        ];
        bool[][] member = [.. close.Select((row, name) => row.Select((_, session) => !(bLeaves && name == 1 && session >= 6)).ToArray())];

        return new HeavyweightTape
        {
            Tickers = ["A", "B", "C", "D"],
            Calendar = [new(2026, 1, 2), new(2026, 1, 5), new(2026, 1, 6), new(2026, 1, 7), new(2026, 2, 2), new(2026, 2, 3), new(2026, 2, 4), new(2026, 2, 5)],
            Close = close,
            Average200 = [.. close.Select(row => row.Select(_ => 0.0).ToArray())],
            Member = member,
            LastMemberClose = [.. close.Select((row, name) => HeavyweightTape.LastMemberCloseOf(member[name], row))],
            Index = [.. Enumerable.Repeat(double.NaN, 8)],
            First = 0,
        };
    }

    static readonly IReadOnlyList<int> ImprovingMonths = [0, 4];

    // On the second session A's newly read filing scores 8 of 9 against 6 before, its sales growth speeding up, and B's 7
    // of 9 against 5; on the third C's 9 of 9 against 9; on the sixth A's next filing reads 6 of 9. A flag stood for none
    // the night before, unless the case says so.
    static (IReadOnlyList<IReadOnlyList<ImprovingFiling>> Filings, IReadOnlyList<IReadOnlyDictionary<int, ImprovingScore>> Scores) ImprovingReadings(
        double aFaster = 0.02,
        bool aFlagged = false,
        int aLater = 6,
        int aLaterApplied = 9)
    {
        IReadOnlyList<IReadOnlyList<ImprovingFiling>> filings =
        [
            [new(1, 8, 9, 6, 9, aFaster, aFlagged), new(5, aLater, aLaterApplied, 8, 9, 0.01, false)],
            [new(1, 7, 9, 5, 9, 0.03, false)],
            [new(2, 9, 9, 9, 9, 0.04, false)],
            [],
        ];
        IReadOnlyList<IReadOnlyDictionary<int, ImprovingScore>> scores =
        [
            new Dictionary<int, ImprovingScore> { [1] = new(8, 9), [5] = new(aLater, aLaterApplied) },
            new Dictionary<int, ImprovingScore> { [1] = new(7, 9) },
            new Dictionary<int, ImprovingScore> { [2] = new(9, 9) },
            new Dictionary<int, ImprovingScore>(),
        ];

        return (filings, scores);
    }

    static string[] Traded(HeavyweightTape tape, IEnumerable<HeavyweightTrade> trades) =>
        [.. trades.Select(trade => FormattableString.Invariant($"{tape.Tickers[trade.Name]}|{trade.Entry}|{(trade.End is { } end ? end.ToString(System.Globalization.CultureInfo.InvariantCulture) : "open")}|{trade.Reason ?? "held"}"))];

    [Fact]
    public void TheFundamentalsGridHoldsOneHundredAndFortyFourSettingsTheStartingOneAmongThem()
    {
        Assert.Equal(144, ImprovingGrid.Settings.Count);
        Assert.Equal(144, ImprovingGrid.Settings.Select(setting => setting.Key).Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(ImprovingGrid.Starting, ImprovingGrid.Settings);
        Assert.Equal("buy=8|flag=off|rise=on|faster=on|hold=7|limit=126", ImprovingGrid.Starting.Key);
        Assert.Equal(
            (ImprovingRule.Starting.BuyInNine, ImprovingRule.Starting.HoldInNine, ImprovingRule.Starting.Sessions),
            (ImprovingGrid.Starting.BuyInNine, ImprovingGrid.Starting.HoldInNine, ImprovingGrid.Starting.Sessions));
        Assert.Equal(2, (ImprovingGrid.Starting with { FlagFirst = true, Sessions = 252 }).Changes(ImprovingGrid.Starting));
        Assert.Equal(72, ImprovingGrid.Settings.Count(setting => setting.FlagFirst));
    }

    // One filing's trade worked by hand at the starting setting. On the second session A's filing, 8 of 9 above 6 with its
    // growth speeding up, is bought; B's 7 of 9 is under the bar and C's 9 of 9 no higher than before. On the sixth A's
    // next filing reads 6 of 9, under the hold of 7, and A is sold at that close of 110: 110 / 102 - 1 = 7.84 per cent,
    // against the four members answered on its night from the second session to the sixth, A 7.84, B -8.16, C 19.05 and
    // D 0 per cent, a mean of 4.68, an edge of 3.16 points.
    [Fact]
    public void AFundamentalsWalksTradeIsWorkedByHandAndTheTesterReadsTheSameMonth()
    {
        var tape = ImprovingTape();
        var (filings, scores) = ImprovingReadings();
        var (trades, chosen) = ImprovingLay.Walk(tape, filings, scores, ImprovingGrid.Starting);
        var expected = Expected("improving-history").GetProperty("filing");

        Assert.Equal(["A|1|5|" + ImprovingRule.ScoreFell], Traded(tape, trades));

        var sold = trades[0];

        Assert.Equal(expected.GetProperty("result").GetDouble(), sold.Result!.Value, 12);
        Assert.Equal(expected.GetProperty("benchmark").GetDouble(), sold.Cut!.Value, 12);
        Assert.Equal(expected.GetProperty("edge").GetDouble(), sold.Edge!.Value, 12);
        Assert.Equal([0, 1, 2, 3], chosen[1].Sectors[0].Cut);
        Assert.Equal(expected.GetProperty("edge").GetDouble(), HeavyweightSweep.Figures(ImprovingGrid.Starting.Key, trades).Edge!.Value, 12);

        // The tester's month from the first session to the fifth reads A's return from its night, 108 / 102 - 1 = 5.88 per
        // cent, less the members' over the same sessions, 5.88, -6.12, 14.29 and 0, a mean of 3.51: 2.37 points.
        double? Close(int name, int from, int to) => (tape.Close[name][to] / tape.Close[name][from]) - 1.0;

        var holdings = trades.Select(trade => new BookHolding(
            trade.Entry,
            trade.End,
            0,
            (from, to) => Close(trade.Name, from, to),
            (from, to) => chosen[trade.Entry].Sectors[0].Cut.Average(one => Close(one, from, to) ?? 0.0))).ToArray();

        Assert.Equal(expected.GetProperty("month").GetDouble(), BookMonths.Edges(ImprovingMonths, holdings)[0].Edge!.Value, 12);
    }

    // Each dial on both sides over the same tape: the bar of 7 buys B and of 8 does not; a flag asked first buys A only
    // where one stood the night before; the rise asked passes over C's 9 of 9 and not asked buys it; the growth asked passes
    // over A slowing and not asked buys it; a hold of 6 keeps A at 6 of 9 where 7 sells it; and the starting setting buys
    // exactly where the night's own answer does.
    [Fact]
    public void EachDialIsWorkedByHandOnBothSidesAndTheStartingSettingBuysAsTheNightDoes()
    {
        var tape = ImprovingTape();
        var (filings, scores) = ImprovingReadings();

        string[] Bought(ImprovingSweepSetting setting, (IReadOnlyList<IReadOnlyList<ImprovingFiling>> Filings, IReadOnlyList<IReadOnlyDictionary<int, ImprovingScore>> Scores)? readings = null)
        {
            var (read, held) = readings ?? (filings, scores);

            return
            [
                .. ImprovingLay.Walk(tape, read, held, setting).Chosen
                    .OrderBy(pair => pair.Key)
                    .SelectMany(pair => pair.Value.Sectors[0].Leaders.Select(name => tape.Tickers[name])),
            ];
        }

        Assert.Equal(["A"], Bought(ImprovingGrid.Starting));
        Assert.Equal(["A", "B"], Bought(ImprovingGrid.Starting with { BuyInNine = 7 }));
        Assert.Empty(Bought(ImprovingGrid.Starting with { FlagFirst = true }));
        Assert.Equal(["A"], Bought(ImprovingGrid.Starting with { FlagFirst = true }, ImprovingReadings(aFlagged: true)));
        Assert.Equal(["A", "C"], Bought(ImprovingGrid.Starting with { Rise = false }));
        Assert.Empty(Bought(ImprovingGrid.Starting, ImprovingReadings(aFaster: -0.01)));
        Assert.Equal(["A"], Bought(ImprovingGrid.Starting with { Faster = false }, ImprovingReadings(aFaster: -0.01)));

        Assert.Equal(["A|1|open|held"], Traded(tape, ImprovingLay.Walk(tape, filings, scores, ImprovingGrid.Starting with { HoldInNine = 6 }).Trades));

        foreach (var (passed, applied, before, beforeApplied, change) in new (int, int, int, int, double?)[]
        {
            (8, 9, 6, 9, 0.02), (8, 9, 8, 9, 0.02), (7, 9, 5, 9, 0.02), (8, 9, 6, 9, null), (8, 9, 6, 9, -0.01), (4, 4, 0, 0, 0.02), (7, 8, 6, 9, 0.02), (7, 7, 5, 7, 0.02),
        })
        {
            Assert.Equal(
                ImprovingRule.Answer(passed, applied, before, beforeApplied, change, ImprovingRule.Starting).Bought,
                ImprovingGrid.Buys(passed, applied, before, beforeApplied, change, false, ImprovingGrid.Starting));
        }
    }

    // The sales over the same tape: at the starting setting A falls to 6 of 9 on the sixth session and is sold at that
    // close; a scorecard reading no check sells nothing; a time limit of three sessions sells A at the close of the fifth,
    // its third held; and B, bought at a bar of 7, leaves the index on the seventh and is sold at its last close as a
    // member, the sixth's.
    [Fact]
    public void ATradeIsSoldOnAFallingScoreAtItsTimeLimitAndOnLeavingTheIndex()
    {
        var (filings, scores) = ImprovingReadings();
        var (blank, blankScores) = ImprovingReadings(aLater: 0, aLaterApplied: 0);

        Assert.Equal(["A|1|5|" + ImprovingRule.ScoreFell], Traded(ImprovingTape(), ImprovingLay.Walk(ImprovingTape(), filings, scores, ImprovingGrid.Starting).Trades));
        Assert.Equal(["A|1|open|held"], Traded(ImprovingTape(), ImprovingLay.Walk(ImprovingTape(), blank, blankScores, ImprovingGrid.Starting).Trades));
        Assert.Equal(["A|1|4|" + ImprovingRule.TimeLimit], Traded(ImprovingTape(), ImprovingLay.Walk(ImprovingTape(), filings, scores, ImprovingGrid.Starting with { Sessions = 3 }).Trades));

        var leaves = ImprovingTape(bLeaves: true);
        var (trades, _) = ImprovingLay.Walk(leaves, filings, scores, ImprovingGrid.Starting with { BuyInNine = 7 });

        Assert.Equal(["A|1|5|" + ImprovingRule.ScoreFell, "B|1|5|" + ImprovingRule.LeftTheIndex], Traded(leaves, trades));
        Assert.Equal(Expected("improving-history").GetProperty("left").GetProperty("result").GetDouble(), trades[1].Result!.Value, 12);
    }

    // A constructed filer replayed night by night: its annual report for one year and its next filed on a Thursday and
    // on a Friday, each stating net income and cash from operations for the year, and a release of its second year
    // accepted before the second report, reading better with its outlook raised. The first report is read on the
    // session after its day with no scorecard before it; the second on the Monday after, 3 of 3 against 3 of 3 before,
    // the flag standing the night before from the release, which reports a year newer than the stored one within 63
    // sessions.
    [Fact]
    public void AFilingIsReadOnTheFirstNightAfterItsDayWithTheFlagThatStoodTheNightBefore()
    {
        var calendar = Enumerable.Range(0, 730).Select(day => new DateOnly(2025, 1, 2).AddDays(day)).Where(ExchangeClosures.IsSession).ToArray();
        var first = new DateOnly(2025, 2, 20);
        var second = new DateOnly(2026, 2, 20);

        FiledFactRow Year(string concept, int year, decimal value, DateOnly filed) =>
            new(concept, new DateOnly(year, 1, 1), new DateOnly(year, 12, 31), value, filed, "10-K", "k" + year);

        IReadOnlyList<FiledFactRow> facts =
        [
            Year("NetIncomeLoss", 2024, 100m, first),
            Year("NetCashProvidedByUsedInOperatingActivities", 2024, 150m, first),
            Year("NetIncomeLoss", 2025, 120m, second),
            Year("NetCashProvidedByUsedInOperatingActivities", 2025, 160m, second),
        ];
        var accepted = new DateTimeOffset(2026, 2, 5, 21, 10, 0, TimeSpan.Zero);
        IReadOnlyList<ImprovingRelease> releases = [new(accepted, new DateOnly(2026, 2, 5), new ReleaseAnswer(ImprovingRule.Better, "raised", "confident", "stronger"))];

        var (read, scores) = ImprovingLay.Replay(facts, releases, calendar, 0, _ => false, PriceHistory.None);

        Assert.Equal(
            ["2025-02-21|3/3|0/0|False", "2026-02-23|3/3|3/3|True"],
            read.Select(one => FormattableString.Invariant($"{calendar[one.Session]:yyyy-MM-dd}|{one.Passed}/{one.Applied}|{one.PassedBefore}/{one.AppliedBefore}|{one.FlaggedBefore}")));
        Assert.Equal(new ImprovingScore(3, 3), scores[read[1].Session]);

        // A release the night cannot read yet, or one reading worse, flags nothing; and the sessions a flag reads are the
        // history's own, the 63rd back from a night standing and the 64th not, as the exchange's table counts them.
        Assert.True(ImprovingLay.FlagStood(facts, releases, calendar, second, false, PriceHistory.None));
        Assert.False(ImprovingLay.FlagStood(facts, releases, calendar, new DateOnly(2026, 2, 4), false, PriceHistory.None));
        Assert.False(ImprovingLay.FlagStood(facts, [new(accepted, new DateOnly(2026, 2, 5), new ReleaseAnswer("worse", "raised", "confident", "stronger"))], calendar, second, false, PriceHistory.None));

        var night = new DateOnly(2026, 10, 26);

        Assert.Equal(ImprovingRule.SessionsFrom(new DateOnly(2026, 7, 29), night), ImprovingLay.SessionsBetween(calendar, new DateOnly(2026, 7, 29), night));
        Assert.Equal(63, ImprovingLay.SessionsBetween(calendar, new DateOnly(2026, 7, 29), night));
        Assert.Equal(64, ImprovingLay.SessionsBetween(calendar, new DateOnly(2026, 7, 28), night));
    }
}
