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

    // A book's months, a run of years from 2019 each holding the same count of months, each month's edge given.
    static List<(DateOnly Opens, double? Edge)> BookMonthsOver(int years, int perYear, Func<int, int, double?> edge) =>
        [.. from year in Enumerable.Range(0, years) from month in Enumerable.Range(0, perYear) select (new DateOnly(2019 + year, month + 1, 2), edge(year, month))];

    // The family's floors in months worked by hand, from the 22.11 ruling. Six months held in each of the eight years from
    // 2019, 48 in all, each month's edge one point above nothing in the first six years and one below in the last two: 48
    // months and 6 years meet the floors. A month fewer, 47, does not; nor does a sixth year whose months' mean is nought,
    // three a point above and three a point below, since a year stands above nothing only above it. A year held in no month
    // counts as not above. Inside a fold learning on three years the floors read 48 x 3 / 8 = 18 months and 6 x 3 / 8 = 2.25
    // years, rounded up to 3: 18 months over three years each above nothing meet them, 17 do not, and two years above do not.
    // see: A quality book meets its floors in months held, and a fold reads them in proportion to the years it learns on
    [Fact]
    public void AQualityBooksFloorsAreReadInItsMonthsHeldOnBothSidesAndInProportionInAFold()
    {
        var expected = Expected("quality-history").GetProperty("floors");
        var meeting = BookMonthsOver(8, 6, (year, _) => year < 6 ? 0.01 : -0.01);
        var read = QualityRule.Months(meeting, 2019);

        Assert.Equal((expected.GetProperty("months").GetInt32(), expected.GetProperty("years").GetInt32()), (read.Held, read.YearsAbove));
        Assert.True(QualityRule.MeetsFloors(read));
        Assert.False(QualityRule.MeetsFloors(QualityRule.Months(meeting.Skip(1), 2019)));

        var even = QualityRule.Months(BookMonthsOver(8, 6, (year, month) => year == 5 ? (month % 2 == 0 ? 0.01 : -0.01) : year < 6 ? 0.01 : -0.01), 2019);

        Assert.Equal((48, 5), (even.Held, even.YearsAbove));
        Assert.False(QualityRule.MeetsFloors(even));

        // 2019 held in no month: seven months a year, 49 held, its year none and not above.
        var empty = QualityRule.Months(BookMonthsOver(8, 7, (year, _) => year == 0 ? null : year < 7 ? 0.01 : -0.01), 2019);

        Assert.Equal((49, 6), (empty.Held, empty.YearsAbove));
        Assert.Null(empty.YearEdge[0]);

        // A fold learning on three years.
        var three = QualityRule.Months(BookMonthsOver(3, 6, (_, _) => 0.01), 2019);

        Assert.Equal((expected.GetProperty("foldMonths").GetInt32(), expected.GetProperty("foldYears").GetInt32()), (three.Held, three.YearsAbove));
        Assert.True(QualityRule.MeetsFloors(three, 3));
        Assert.False(QualityRule.MeetsFloors(three));
        Assert.False(QualityRule.MeetsFloors(QualityRule.Months(BookMonthsOver(3, 6, (_, _) => 0.01).Skip(1), 2019), 3));
        Assert.False(QualityRule.MeetsFloors(QualityRule.Months(BookMonthsOver(3, 6, (year, _) => year < 2 ? 0.01 : -0.01), 2019), 3));
    }

    // The sources the 22.11 ruling's first two items write or edit, filed under 22.10.
    static readonly string[] MonthsAndPullSources =
    [
        "src/EquityBrief.Core/Families/QualityRule.cs",
        "src/EquityBrief.Worker/Sweep/QualitySweep.cs",
        "src/EquityBrief.Worker/Sweep/IndexSweepRunner.Quality.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Worker/Bars/HistoryPull.cs",
        "src/EquityBrief.Worker/Bars/PullComparison.cs",
        "src/EquityBrief.Worker/Sweep/SweepHistory.cs",
        "src/EquityBrief.Worker/Moves/PageHistoryReader.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Chart.cs",
        "src/EquityBrief.Worker/Program.cs",
    ];

    // No source a rule's version pins is one the months floors and the deeper pull write, so no rule's version moves, the
    // reader shown to find a source a rule pins. From 22.11 the quality rules pin their family's rule, being rules of it,
    // and no rule registered before them did.
    // see: A quality rule is registered as the family on its index and kept by the family's own step alone
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheMonthsFloorsAndTheDeeperPullWrite()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .Where(evaluator => evaluator is not EquityBrief.Core.Candidates.QualityCandidate)
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(MonthsAndPullSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(MonthsAndPullSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Families/HeavyweightBook.cs", "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    static HeavyweightFigures EdgeOnly(string key, double edge) => new(key, 70, 10, edge, null, null, null, null, [], [], 0, null, null, null, null, null);

    // A fold learns on a month only once its close has passed: of a book's months opening on 2021-11-01 and 2021-12-01, the
    // first closes on 2021-12-01 and is learned on by the fold testing 2022, and the second closes on that fold's first
    // session, 2022-01-03, and is not. The proposal and a fold's choice take the best edge a holding among the settings
    // whose months meet the floors, passing over a better edge whose months fall a month short.
    // see: A quality book meets its floors in months held, and a fold reads them in proportion to the years it learns on
    [Fact]
    public void AFoldReadsTheMonthsClosedBeforeItsYearAndTheProposalTakesOnlyTheSettingsMeetingTheFloors()
    {
        DateOnly[] calendar = [new(2021, 11, 1), new(2021, 12, 1), new(2022, 1, 3), new(2022, 2, 1)];
        int[] reshuffles = [0, 1, 2, 3];
        var book = new QualityBook(QualityRule.Starting, [], [], [(0, 0.02), (1, 0.03), (2, null)]);
        var fold = new LoopFold(2022, new DateOnly(2022, 1, 3), new DateOnly(2022, 12, 30), true);

        Assert.Equal(2, QualityLay.MonthsOf(book, calendar, reshuffles).Held);

        var learned = QualityLay.MonthsOf(book, calendar, reshuffles, fold);

        Assert.Equal((1, (double?)0.02), (learned.Held, learned.YearEdge[2]));

        var other = QualityRule.Starting with { ScoreFloorInNine = 6 };
        var meets = QualityRule.Months(BookMonthsOver(8, 6, (year, _) => year < 6 ? 0.01 : -0.01), 2019);
        var short47 = QualityRule.Months(BookMonthsOver(8, 6, (year, _) => year < 6 ? 0.01 : -0.01).Skip(1), 2019);

        Assert.Equal(QualityRule.Starting, QualityLay.Propose([(QualityRule.Starting, EdgeOnly(QualityRule.Starting.Key, 0.01), meets), (other, EdgeOnly(other.Key, 0.05), short47)]));
        Assert.Equal(other, QualityLay.Propose([(QualityRule.Starting, EdgeOnly(QualityRule.Starting.Key, 0.01), meets), (other, EdgeOnly(other.Key, 0.05), meets)]));
        Assert.Null(QualityLay.Propose([(QualityRule.Starting, EdgeOnly(QualityRule.Starting.Key, 0.01), short47)]));

        var three = QualityRule.Months(BookMonthsOver(3, 6, (_, _) => 0.01), 2019);
        var threeShort = QualityRule.Months(BookMonthsOver(3, 6, (_, _) => 0.01).Skip(1), 2019);

        Assert.Equal(QualityRule.Starting, QualityLay.Choose([(QualityRule.Starting, EdgeOnly(QualityRule.Starting.Key, 0.01), three), (other, EdgeOnly(other.Key, 0.05), threeShort)], 3));
    }
}
