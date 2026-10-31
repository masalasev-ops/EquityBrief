using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Quality;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.5: the two book families as the engines read them. Each book's walk sells a holding at its first
// close under its 200-day average where the engines ask, from 21.16 a dial the rule reads, and keeps only the members an
// arrangement keeps where a proposal's hooks set one, each worked by hand over the history tests' own tapes. The book menu
// ranks its changes inside each fold above the rule's own book at the month floors, the k-th proposal the k-th change,
// each stating its setting's key, the 200-day sale's with the sale on after the grid's dials; and the engine hands a
// change's hooks to the book's walk as an arrangement and none where they read no reading.
// see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
// see: Every change the engines propose is one its family's night reads, so each is put to the operator
public partial class FixtureExpectations
{
    [Fact]
    public void ABooksWalkSellsUnderTheTwoHundredDayAverageAndKeepsWhatAnArrangementKeepsWorkedByHand()
    {
        // Quality at a fair price, a book of two: A's 200-day average stands at 105 on the third session, over its close of
        // 104, so it is sold there; B is sold at the second reshuffle, fallen to 6 of 9, and A, qualifying again, and C fill
        // the two places. A's result is 104 / 100 - 1 = 4 per cent against the four members over the same sessions, A 4, B
        // -4, C 10 and D 0 per cent, a mean of 2.5: its edge 1.5 points.
        var tape = QualityTape();

        tape.Average200[0][2] = 105;

        var setting = QualityRule.Starting with { Holdings = 2 };
        var (sold, _) = QualityLay.Walk(tape, QualityReshuffles, QualityReadings(), NoFilings(), setting, soldUnderAverage: true);

        Assert.Equal(["A|0|2|" + QualityLay.UnderTheAverage, "B|0|3|" + QualityFamily.NoLongerQualifies, "A|3|open|held", "C|3|open|held"], Held(tape, sold));
        Assert.Equal(0.015, sold[0].Edge!.Value, 12);

        // Not asked, the same tape holds A through as before.
        Assert.Equal(["B|0|3|" + QualityFamily.NoLongerQualifies, "A|0|open|held", "C|3|open|held"], Held(tape, QualityLay.Walk(tape, QualityReshuffles, QualityReadings(), NoFilings(), setting).Trades));

        // An arrangement leaving A out: B alone at the first reshuffle, and C alone at the second.
        IReadOnlyList<int> WithoutA(int session, IReadOnlyList<int> ranked) => [.. ranked.Where(name => name != 0)];

        Assert.Equal(["B|0|3|" + QualityFamily.NoLongerQualifies, "C|3|open|held"], Held(tape, QualityLay.Walk(QualityTape(), QualityReshuffles, QualityReadings(), NoFilings(), setting, arrange: WithoutA).Trades));

        // The Fundamentals family: A, bought on the second session, closes 104 on the third under an average of 105 and is
        // sold there, where it held to the sixth before; an arrangement leaving A out buys nothing.
        var improvingTape = ImprovingTape();
        var (filings, scores) = ImprovingReadings();

        improvingTape.Average200[0][2] = 105;

        Assert.Equal(["A|1|2|" + QualityLay.UnderTheAverage], Traded(improvingTape, ImprovingLay.Walk(improvingTape, filings, scores, ImprovingGrid.Starting, soldUnderAverage: true).Trades));
        Assert.Equal(["A|1|5|" + ImprovingRule.ScoreFell], Traded(improvingTape, ImprovingLay.Walk(improvingTape, filings, scores, ImprovingGrid.Starting).Trades));
        Assert.Empty(ImprovingLay.Walk(ImprovingTape(), filings, scores, ImprovingGrid.Starting, arrange: WithoutA).Trades);
    }

    [Fact]
    public void TheBookMenuRanksItsChangesInEachFoldAndPutsEachAsTheChangeTheNightReads()
    {
        // Weekdays from 2019 to October 2026, each month's first session a month's start, and three books of months: the
        // rule's own at a tenth of a point a month, the 200-day sale at three tenths and the filing's sale at two tenths.
        var calendar = Weekdays(new DateOnly(2019, 1, 2), 2035);
        var read = new LoopRead("MID", null!, [], [], null!, null!, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(), calendar, 0, LoopFolds.Of(calendar, calendar[^1]));
        int[] starts = [.. Enumerable.Range(0, calendar.Length).Where(at => at == 0 || calendar[at].Month != calendar[at - 1].Month)];

        IReadOnlyList<(int Opens, double? Edge)> Months(double edge) => [.. starts.SkipLast(1).Select(start => (start, (double?)edge))];

        var onFiling = QualityRule.Starting with { SellOnFiling = true };
        var underAverage = QualityRule.Starting with { SoldUnderAverage = true };
        BookProcedures.MenuChange[] menu =
        [
            new("a holding also sold at a close under its 200-day average", Months(0.003), new LoopChange(null, underAverage.Key, null, LoopChange.NoHooks)),
            new("a holding also sold at the close of the night a filing drops its score below the floor", Months(0.002), new LoopChange(null, onFiling.Key, null, LoopChange.NoHooks)),
        ];

        // By hand: a fold learns the months whose close, the next month's start, falls before its year, so the fold testing
        // 2022 learns on 35 months of 2019 to 2021, the 36th, December's, closing on its year's first session; each a tenth
        // of a point for the rule's own.
        var fold = read.Folds[0];
        var (learned, edge) = BookProcedures.Learned(Months(0.001), starts, calendar, fold);

        Assert.Equal(2022, fold.Year);
        Assert.Equal(35, learned.Held);
        Assert.Equal(0.001, edge!.Value, 12);

        // Every fold ranks the 200-day sale first and the filing's second, both above the rule's own and meeting the floors;
        // from 21.16 each states its setting by its key, the 200-day sale's the starting setting's with the sale on after it.
        var proposals = BookProcedures.Menu(read, QualityRule.Name, "the quality rule today", Months(0.001), starts, menu, _ => { });

        Assert.Equal(2, proposals.Count);
        Assert.Equal(["exit: " + menu[0].Words, "exit: " + menu[1].Words], proposals.Select(one => one.Words));
        Assert.All(proposals, one => Assert.Equal(read.Folds.Count, one.StableFolds));
        Assert.Equal(QualityRule.Starting.Key + "|average=on", proposals[0].Change!.Key);
        Assert.Equal($"{menu[0].Words} read +0.30 points a month on the years it learned on against the rule's own +0.10", proposals[0].Finding);
        Assert.Equal(onFiling.Key, proposals[1].Change!.Key);
        Assert.All(proposals, one => Assert.Equal(LoopProcedures.Points, one.Unit));

        // The engine hands a condition's hooks to the book's walk as an arrangement, and none where its hooks read no reading.
        var handed = new List<bool>();

        IReadOnlyList<(int Opens, double? Edge)> Walked(Func<int, IReadOnlyList<int>, IReadOnlyList<int>>? arrange)
        {
            handed.Add(arrange is not null);

            return Months(0.001);
        }

        var engine = BookProcedures.Engine(read, QualityRule.Name, "the quality rule today", [], null!, LoopChange.NoHooks, Walked);

        engine.Walk(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_above"] = 50 }, null);

        Assert.Equal([false, true], handed);
        Assert.Equal((LoopProcedures.Points, "setup", 100.0), (engine.Unit, engine.Thing, engine.Scale));
    }
}
