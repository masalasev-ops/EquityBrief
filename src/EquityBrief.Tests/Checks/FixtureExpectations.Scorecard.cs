using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Fundamentals;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 22.8: the fundamentals scorecard. Each of the nine checks is worked by hand on both sides of its
// threshold over a constructed filer whose fiscal year is the calendar's and whose newest quarter is its second, so the
// four quarters of every measure read the six-month span the second quarter's cash flow statement alone states: net
// income 110 against 95 a year before, cash from operations 150, sales 1,200 against 1,020 and gross profit 580 against
// 470, on total assets of 2,000, 1,800 and 1,700 at the quarter's end, a year before and two years before. A filing made
// on the session is read from the next session's, a financial company is scored on seven checks, the word's cut points
// are worked at 4, 5, 7 and 8 of nine and at the shares of seven, and a split between the two counts moves no share check.
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
public partial class FixtureExpectations
{
    // The rows 22.8 adds that this check reaches: section 17's numbers and section 18's three.
    internal static readonly string[] ScorecardClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The fundamentals scorecard's numbers"),
        CheckReach.Key(Scope.FailureTable, "A quarter reported and not yet filed"),
        CheckReach.Key(Scope.FailureTable, "A line the scorecard needs not filed"),
        CheckReach.Key(Scope.FailureTable, "A financial company on the scorecard"),
    ];

    // The session the scorecard is read on, after the newest quarter's report was filed on 2026-08-05.
    static readonly DateOnly NineSession = new(2026, 8, 20);

    static readonly DateOnly NineQuarter = new(2026, 6, 30);

    static readonly DateOnly NineYearBefore = new(2025, 6, 30);

    static FiledFactRow NineFact(string concept, DateOnly start, DateOnly end, decimal value, DateOnly filed) =>
        new(concept, start, end, value, filed, start == end ? "10-Q" : "10-K", FormattableString.Invariant($"{concept}-{start:yyyyMMdd}-{end:yyyyMMdd}"));

    // A measure over the three spans its four quarters read on either side: the six months to 2026-06-30, filed
    // 2026-08-05; the year 2025, filed 2026-02-20; the six months to 2025-06-30, filed 2025-08-05; the year 2024, filed
    // 2025-02-20; and the six months to 2024-06-30, filed 2024-08-05.
    static IEnumerable<FiledFactRow> NineFlow(string concept, decimal six2026, decimal year2025, decimal six2025, decimal year2024, decimal six2024) =>
    [
        NineFact(concept, new(2026, 1, 1), new(2026, 6, 30), six2026, new(2026, 8, 5)),
        NineFact(concept, new(2025, 1, 1), new(2025, 12, 31), year2025, new(2026, 2, 20)),
        NineFact(concept, new(2025, 1, 1), new(2025, 6, 30), six2025, new(2025, 8, 5)),
        NineFact(concept, new(2024, 1, 1), new(2024, 12, 31), year2024, new(2025, 2, 20)),
        NineFact(concept, new(2024, 1, 1), new(2024, 6, 30), six2024, new(2024, 8, 5)),
    ];

    // A balance sheet line at the newest quarter's end, a year before and two years before, each filed with its quarter.
    static IEnumerable<FiledFactRow> NineLine(string concept, decimal now, decimal yearBefore, decimal? twoBefore = null) =>
    [
        NineFact(concept, NineQuarter, NineQuarter, now, new(2026, 8, 5)),
        NineFact(concept, NineYearBefore, NineYearBefore, yearBefore, new(2025, 8, 5)),
        .. twoBefore is { } older ? [NineFact(concept, new(2024, 6, 30), new(2024, 6, 30), older, new(2024, 8, 5))] : Array.Empty<FiledFactRow>(),
    ];

    // The covers' counts, standing for the 25th of July of each year and filed with that quarter's report.
    static IEnumerable<FiledFactRow> NineCounts(decimal now, decimal yearBefore) =>
    [
        NineFact(FiledFacts.SharesOutstanding[0], new(2026, 7, 25), new(2026, 7, 25), now, new(2026, 8, 5)),
        NineFact(FiledFacts.SharesOutstanding[0], new(2025, 7, 25), new(2025, 7, 25), yearBefore, new(2025, 8, 5)),
    ];

    // The constructed filer: every check passing, each figure stated as the comment above the class gives it.
    static List<FiledFactRow> NineFacts(
        decimal income2026 = 60m,
        decimal cash2026 = 80m,
        decimal assetsYearBefore = 1800m,
        decimal debtNow = 400m,
        decimal currentAssetsNow = 900m,
        decimal sharesNow = 100m,
        decimal gross2026 = 300m,
        decimal sales2026 = 600m,
        bool grossFiled = true) =>
    [
        .. NineFlow("NetIncomeLoss", income2026, 100m, 50m, 90m, 45m),
        .. NineFlow("NetCashProvidedByUsedInOperatingActivities", cash2026, 130m, 60m, 120m, 55m),
        .. NineFlow("Revenues", sales2026, 1100m, 500m, 1000m, 480m),
        .. grossFiled
            ? NineFlow("GrossProfit", gross2026, 520m, 240m, 460m, 230m)
            : NineFlow("CostOfRevenue", sales2026 - gross2026, 1100m - 520m, 500m - 240m, 1000m - 460m, 480m - 230m),
        .. NineLine("Assets", 2000m, assetsYearBefore, 1700m),
        .. NineLine("LongTermDebtNoncurrent", debtNow, 420m),
        .. NineLine("AssetsCurrent", currentAssetsNow, 800m),
        .. NineLine("LiabilitiesCurrent", 600m, 560m),
        .. NineCounts(sharesNow, 100m),
    ];

    // Closes at the store's scale and as traded, the same on every session where no split stands between the counts;
    // where a split of two for one came on 2026-01-15 the sessions before it traded at twice their scaled close, and
    // where a dividend came then the older traded three per cent over theirs, a step no split takes.
    static PriceHistory NinePrices(bool split = false, bool dividend = false) => new(
        [new DateOnly(2025, 7, 25), new DateOnly(2026, 1, 14), new DateOnly(2026, 1, 15), new DateOnly(2026, 7, 24), NineSession],
        [50, 54, 55, 60, 61],
        [split ? 100 : dividend ? 51.5 : 50, split ? 108 : dividend ? 55.62 : 54, 55, 60, 61]);

    static Scorecard Nine(List<FiledFactRow> facts, bool financial = false, bool split = false, DateOnly? session = null, bool dividend = false) =>
        FundamentalScorecard.Read(facts, session ?? NineSession, financial, NinePrices(split, dividend));

    static CheckAnswer NineAnswer(Scorecard card, int number) => card.Checks.Single(check => check.Number == number).Answer;

    [Fact]
    public void EveryCheckPassesOnTheConstructedFilerAndItsFiguresAreWorkedByHandFromItsSecondQuarter()
    {
        var card = Nine(NineFacts());

        Assert.Equal(NineQuarter, card.Quarter);
        Assert.Equal(NineYearBefore, card.YearBefore);
        Assert.Equal(9, card.Checks.Count);
        Assert.All(card.Checks, check => Assert.Equal(CheckAnswer.Passed, check.Answer));
        Assert.Equal(9, card.Passed);
        Assert.Equal(9, card.Applied);
        Assert.Equal(ScorecardWord.Strong, card.Word);

        // Net income: 60 + 100 - 50 = 110. Cash: 80 + 130 - 60 = 150, read from the six months a second quarter's cash
        // flow statement alone states.
        Assert.Equal(110m, card.Checks[0].Amount);
        Assert.Equal(150m, card.Checks[1].Amount);
        Assert.Equal(150m, card.Checks[3].Amount);
        Assert.Equal(110m, card.Checks[3].Against);
        Assert.Equal(150m, FundamentalScorecard.FourQuarters(NineFacts(), FiledFacts.OperatingCash, NineQuarter));
        Assert.Null(FundamentalScorecard.FourQuarters([.. NineFacts().Where(fact => !(fact.Concept.StartsWith("NetCash", StringComparison.Ordinal) && fact.End == NineYearBefore))], FiledFacts.OperatingCash, NineQuarter));

        // Return on assets: 110 / 1,800 against 95 / 1,700, the year before's four quarters 50 + 90 - 45 = 95.
        Assert.Equal(110.0 / 1800, card.Checks[2].Now!.Value, 12);
        Assert.Equal(95.0 / 1700, card.Checks[2].YearBefore!.Value, 12);

        // Long-term debt over the year's average assets: 400 / 1,900 against 420 / 1,750. The current ratio: 900 / 600
        // against 800 / 560.
        Assert.Equal(400.0 / 1900, card.Checks[4].Now!.Value, 12);
        Assert.Equal(0.24, card.Checks[4].YearBefore!.Value, 12);
        Assert.Equal(1.5, card.Checks[5].Now!.Value, 12);
        Assert.Equal(800.0 / 560, card.Checks[5].YearBefore!.Value, 12);

        // The shares: 100 against 100, no change.
        Assert.Equal(0.0, card.Checks[6].Now!.Value, 12);

        // The gross margin: 580 / 1,200 against 470 / 1,020. Sales over assets: 1,200 / 1,800 against 1,020 / 1,700.
        Assert.Equal(580.0 / 1200, card.Checks[7].Now!.Value, 12);
        Assert.Equal(470.0 / 1020, card.Checks[7].YearBefore!.Value, 12);
        Assert.Equal(1200.0 / 1800, card.Checks[8].Now!.Value, 12);
        Assert.Equal(1020.0 / 1700, card.Checks[8].YearBefore!.Value, 12);

        // Gross profit over the total assets at the quarter's end: 580 / 2,000.
        Assert.Equal(0.29, card.GrossProfitability!.Value, 12);

        // The same worked figures as the fixture states them, derived by hand and not from a run.
        var expected = Expected("scorecard");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.StartsWith("22.8's nine checks worked by hand", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);

        var filer = expected.GetProperty("filer");

        Assert.Equal(filer.GetProperty("income").GetDecimal(), card.Checks[0].Amount);
        Assert.Equal(filer.GetProperty("cash").GetDecimal(), card.Checks[1].Amount);
        Assert.Equal(filer.GetProperty("incomeYearBefore").GetDecimal(), FundamentalScorecard.FourQuarters(NineFacts(), FiledFacts.NetIncome, NineYearBefore));
        Assert.Equal(filer.GetProperty("grossProfitability").GetDouble(), card.GrossProfitability!.Value, 12);

        foreach (var (name, number) in new[] { ("returnOnAssets", 3), ("debtOverAssets", 5), ("currentRatio", 6), ("grossMargin", 8), ("turnover", 9) })
        {
            var pair = filer.GetProperty(name).EnumerateArray().Select(value => value.GetDouble()).ToArray();
            var check = card.Checks.Single(one => one.Number == number);

            Assert.Equal(pair[0], check.Now!.Value, 9);
            Assert.Equal(pair[1], check.YearBefore!.Value, 9);
        }
    }

    [Fact]
    public void EachCheckIsWorkedOnBothSidesOfItsThreshold()
    {
        // (1) Net income above nothing: 60 makes 110 and passes; -50 makes nothing and fails.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(income2026: -50m)), 1));

        // (2) Cash from operations above nothing: -70 makes nothing and fails.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(cash2026: -70m)), 2));

        // (3) Return on assets higher than a year before: assets of 1,800 a year before make 110 / 1,800, 0.0611, against
        // 95 / 1,700, 0.0559, and pass; 2,100 make 0.0524 and fail; and 95 / 1,700, the year before's own share, fails too.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(assetsYearBefore: 2100m)), 3));
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(income2026: 45m, assetsYearBefore: 1700m)), 3));

        // (4) Cash from operations above net income: 150 against 110 passes; 40 makes 110 against 110 and fails.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(cash2026: 40m)), 4));

        // (5) Long-term debt over the year's average assets lower than a year before: 400 / 1,900 passes against 420 /
        // 1,750, 0.24; 500 / 1,900 fails, and 456 / 1,900, 0.24 exactly, the same share, fails too.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(debtNow: 500m)), 5));
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(debtNow: 456m)), 5));
        Assert.Equal(CheckAnswer.Passed, NineAnswer(Nine(NineFacts(debtNow: 455m)), 5));

        // (6) The current ratio higher than a year before: 900 / 600 passes against 800 / 560, 1.4286; 850 / 600, 1.4167,
        // fails.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(currentAssetsNow: 850m)), 6));

        // (7) No new shares: 102 against 100 stands at the tolerance and passes; 103 fails.
        Assert.Equal(CheckAnswer.Passed, NineAnswer(Nine(NineFacts(sharesNow: 102m)), 7));
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(sharesNow: 103m)), 7));

        // (8) The gross margin higher than a year before: 300 makes 580 / 1,200 and passes; 254 makes 534 / 1,200, 0.445,
        // under 470 / 1,020, and fails.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(gross2026: 254m)), 8));

        // (9) Sales over assets higher than a year before: 1,200 / 1,800 passes against 1,020 / 1,700, 0.6; with assets of
        // 2,100 a year before, 1,200 / 2,100, 0.5714, fails; and sales of 420 make 1,020 / 1,800, 0.5667, and fail.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(assetsYearBefore: 2100m)), 9));
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(sales2026: 420m)), 9));
    }

    [Fact]
    public void GrossProfitIsSalesLessTheCostOfSalesWhereNoneIsFiled()
    {
        var card = Nine(NineFacts(grossFiled: false));

        Assert.Equal(CheckAnswer.Passed, NineAnswer(card, 8));
        Assert.Equal(580.0 / 1200, card.Checks[7].Now!.Value, 12);
        Assert.Equal(0.29, card.GrossProfitability!.Value, 12);
    }

    [Fact]
    public void AFilingMadeOnTheSessionIsReadFromTheNextSession()
    {
        // A third quarter's report filed on the session itself, its nine months holding a loss that would fail the first
        // check, is not read on that session and is on the next.
        var filedOn = new DateOnly(2026, 10, 30);
        var facts = NineFacts();

        facts.Add(NineFact("NetIncomeLoss", new(2026, 1, 1), new(2026, 9, 30), -200m, filedOn));
        facts.Add(NineFact("NetIncomeLoss", new(2025, 1, 1), new(2025, 9, 30), 75m, new(2025, 11, 5)));

        var onTheSession = Nine(facts, session: filedOn);
        var next = Nine(facts, session: filedOn.AddDays(1));

        Assert.Equal(NineQuarter, onTheSession.Quarter);
        Assert.Equal(CheckAnswer.Passed, NineAnswer(onTheSession, 1));
        Assert.Equal(new DateOnly(2026, 9, 30), next.Quarter);

        // -200 + 100 - 75 = -175.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(next, 1));
        Assert.Equal(-175m, next.Checks[0].Amount);
    }

    [Fact]
    public void AFinancialCompanyIsScoredOnTheSevenChecksThatApplyToIt()
    {
        var card = Nine(NineFacts(), financial: true);

        Assert.Equal(CheckAnswer.NotApplying, NineAnswer(card, 6));
        Assert.Equal(CheckAnswer.NotApplying, NineAnswer(card, 8));
        Assert.Equal(7, card.Applied);
        Assert.Equal(7, card.Passed);
        Assert.Equal(ScorecardWord.Strong, card.Word);

        // One of the seven failing leaves six of seven, mixed, and the family's floor of seven in nine still cleared.
        var six = Nine(NineFacts(sharesNow: 103m), financial: true);

        Assert.Equal(6, six.Passed);
        Assert.Equal(7, six.Applied);
        Assert.Equal(ScorecardWord.Mixed, six.Word);
        Assert.True(FundamentalScorecard.Clears(six.Passed, six.Applied, 7));
        Assert.True(FundamentalScorecard.IsFinancial("Financials"));
        Assert.False(FundamentalScorecard.IsFinancial("Information Technology"));
    }

    [Fact]
    public void ALineNotFiledLeavesItsCheckUnreadAndNamesTheLine()
    {
        var withoutDebt = Nine([.. NineFacts().Where(fact => fact.Concept != "LongTermDebtNoncurrent")]);
        var debt = withoutDebt.Checks.Single(check => check.Number == 5);

        Assert.Equal(CheckAnswer.NotFiled, debt.Answer);
        Assert.Equal(FundamentalScorecard.DebtLine, debt.Missing);
        Assert.Equal(8, withoutDebt.Applied);

        // Long-term debt under a later concept in its order is read where the first is not stated at both ends.
        var underLater = Nine([.. NineFacts().Select(fact => fact.Concept == "LongTermDebtNoncurrent" ? fact with { Concept = "LongTermDebt" } : fact)]);

        Assert.Equal(CheckAnswer.Passed, NineAnswer(underLater, 5));

        // Net income, cash and total assets alone read four checks, the first four: no word.
        var four = Nine([.. NineFacts().Where(fact => fact.Concept is "NetIncomeLoss" or "NetCashProvidedByUsedInOperatingActivities" or "Assets")]);

        Assert.Equal(4, four.Applied);
        Assert.Null(four.Word);
        Assert.Equal(FundamentalScorecard.SalesLine, four.Checks.Single(check => check.Number == 9).Missing);
    }

    [Fact]
    public void TheWordsCutPointsAreWorkedAtFourFiveSevenAndEightOfNineAndAtTheSharesOfSeven()
    {
        Assert.Equal(ScorecardWord.Weak, FundamentalScorecard.WordOf(4, 9));
        Assert.Equal(ScorecardWord.Mixed, FundamentalScorecard.WordOf(5, 9));
        Assert.Equal(ScorecardWord.Mixed, FundamentalScorecard.WordOf(7, 9));
        Assert.Equal(ScorecardWord.Strong, FundamentalScorecard.WordOf(8, 9));
        Assert.Equal(ScorecardWord.Strong, FundamentalScorecard.WordOf(9, 9));

        // Of seven: strong at seven, mixed from four to six and weak at three.
        Assert.Equal(ScorecardWord.Strong, FundamentalScorecard.WordOf(7, 7));
        Assert.Equal(ScorecardWord.Mixed, FundamentalScorecard.WordOf(6, 7));
        Assert.Equal(ScorecardWord.Mixed, FundamentalScorecard.WordOf(4, 7));
        Assert.Equal(ScorecardWord.Weak, FundamentalScorecard.WordOf(3, 7));

        // Under five checks read, no word; and the floor of seven in nine is six of seven and not five.
        Assert.Null(FundamentalScorecard.WordOf(4, 4));
        Assert.True(FundamentalScorecard.Clears(7, 9, 7));
        Assert.False(FundamentalScorecard.Clears(6, 9, 7));
        Assert.True(FundamentalScorecard.Clears(6, 7, 7));
        Assert.False(FundamentalScorecard.Clears(5, 7, 7));
        Assert.False(FundamentalScorecard.Clears(4, 4, 7));
    }

    [Fact]
    public void ASplitBetweenTheTwoCountsMovesNoShareCheck()
    {
        // Two hundred shares after a split of two for one, the older count's day trading at twice its scaled close, stand
        // as one hundred at the store's scale: no new shares.
        var card = Nine(NineFacts(sharesNow: 200m), split: true);
        var shares = card.Checks.Single(check => check.Number == 7);

        Assert.Equal(CheckAnswer.Passed, shares.Answer);
        Assert.Equal(0.0, shares.Now!.Value, 12);

        // The same two hundred with no split are an offering doubling the shares.
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(sharesNow: 200m)), 7));

        // A dividend's step of three per cent is no split: 103 shares against 100 stand three per cent more and fail, as
        // they do with no dividend.
        Assert.Equal(1.0, FundamentalScorecard.SplitsBetween(NinePrices(dividend: true), new(2025, 7, 25), new(2026, 7, 25)), 12);
        Assert.Equal(0.5, FundamentalScorecard.SplitsBetween(NinePrices(split: true), new(2025, 7, 25), new(2026, 7, 25)), 12);
        Assert.Equal(CheckAnswer.Failed, NineAnswer(Nine(NineFacts(sharesNow: 103m), dividend: true), 7));
    }

    [Fact]
    public void TheArchivesBalanceSheetLinesAreReadForTheDayEachStandsForAndKeptAsFirstFiled()
    {
        const string answer = """
            {"cik": 1, "facts": {"us-gaap": {
              "Assets": {"units": {"USD": [
                {"end": "2025-06-30", "val": 1800, "accn": "a-1", "fy": 2025, "fp": "Q2", "form": "10-Q", "filed": "2025-08-05"},
                {"end": "2025-06-30", "val": 1810, "accn": "a-2", "fy": 2026, "fp": "Q2", "form": "10-Q", "filed": "2026-08-05"},
                {"start": "2025-01-01", "end": "2025-06-30", "val": 5, "accn": "a-3", "fy": 2025, "fp": "Q2", "form": "10-Q", "filed": "2025-08-05"}]}},
              "NetCashProvidedByUsedInOperatingActivities": {"units": {"USD": [
                {"start": "2026-01-01", "end": "2026-06-30", "val": 80, "accn": "c-1", "fy": 2026, "fp": "Q2", "form": "10-Q", "filed": "2026-08-05"},
                {"start": "2026-01-01", "end": "2026-03-31", "val": 30, "accn": "c-0", "fy": 2026, "fp": "Q1", "form": "10-Q", "filed": "2026-05-05"}]}}}}}
            """;

        var read = ConceptAnswers.FromFacts(answer, "1", FiledFacts.Concepts);

        Assert.Equal(2, read["Assets"].Count);
        Assert.All(read["Assets"], fact => Assert.Equal(fact.Start, fact.End));

        var stored = FiledFacts.Stored(read);
        var assets = stored.Single(row => row.Concept == "Assets");

        // The day's figure as first filed, and the cash flow's six months kept beside its quarter.
        Assert.Equal(1800m, assets.Value);
        Assert.Equal("a-1", assets.Accession);
        Assert.Contains(stored, row => row.Concept == "NetCashProvidedByUsedInOperatingActivities" && row.Start == new DateOnly(2026, 1, 1) && row.End == new DateOnly(2026, 6, 30));
        Assert.Contains(stored, row => row.Concept == "NetCashProvidedByUsedInOperatingActivities" && row.End == new DateOnly(2026, 3, 31));
    }

    [Fact]
    public void TheBusinessReadingsAndTheValuationReadingAreTheSameOverFactsHoldingTheNewLinesAndSpans()
    {
        // Business first reads the ledger's business readings, and the valuation reading reads net income's quarters:
        // the balance sheet's lines, the cost of sales and every six-month span leave both as they were.
        foreach (var cash in BusinessFirstHistory.Cash.Values)
        {
            var before = BusinessFirstHistory.Facts(cash);
            var after = before
                .Concat(before
                    .Where(fact => fact.Concept is "Revenues" or "NetIncomeLoss" or "GrossProfit" or "OperatingIncomeLoss")
                    .Select(fact => fact with { Start = fact.End.AddDays(-181), Accession = fact.Accession + "-six" }))
                .Concat(before.Select(fact => fact.End).Distinct().Select(end => new FiledFactRow("Assets", end, end, 1000m, end.AddDays(40), "10-Q", FormattableString.Invariant($"a-{end:yyyyMMdd}"))))
                .Concat(before.Where(fact => fact.Concept == "Revenues").Select(fact => fact with { Concept = "CostOfRevenue", Value = fact.Value / 2 }))
                .ToList();
            var session = new DateOnly(2026, 1, 1);

            Assert.Equal(FiledFacts.Read(before, session), FiledFacts.Read(after, session));
            Assert.Equal(ValuationReading.PartsOf(before, session).Earnings, ValuationReading.PartsOf(after, session).Earnings);
        }
    }

    // The constructed history of the dip's point-in-time check moved to start on 2025-09-01, so BBB's dip setup on its
    // 265th weekday, 2026-09-07, stands after the constructed filer's newest report, and BBB's filer is the constructed
    // one: every one of the scorecard's nine readings is read on the setup, each worked by hand from the filer's figures,
    // and the point-in-time check rebuilds the setup over the history cut at its session with every reading the same.
    // see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
    [Fact]
    public void EachScorecardReadingIsWorkedByHandAtAConstructedSessionAndRebuiltTheSameOverTheHistoryCutAtIt()
    {
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2025, 9, 1); calendar.Count < 270; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                calendar.Add(day);
            }
        }

        Worker.Sweep.SweepName Member(string ticker, decimal start, decimal step) =>
            new(
                ticker,
                [.. calendar.Select((day, at) => start + (step * at) + ((at % 5) - 2)).Select((close, at) => new Core.Sweep.SweepBar(calendar[at], close + 1, close - 1, close, 1_000_000 + (at * 1_000), close, close))],
                [(new DateOnly(2020, 1, 2), null)],
                []);

        var inputs = new Worker.Sweep.SweepHistoryInputs(calendar[^1], [.. calendar], [Member("AAA", 100, 0.5m), Member("BBB", 80, -0.2m), Member("CCC", 50, 0.1m)], 0, 0, 0, 0, "constructed");
        var market = new Worker.Ledger.LedgerMarket(new Dictionary<string, IReadOnlyList<(DateOnly Session, double Close)>>(StringComparer.Ordinal)
        {
            [Worker.Ledger.LedgerMarket.Index] = [.. calendar.Select((day, at) => (day, 4000.0 + (at * 3)))],
            [Worker.Ledger.LedgerMarket.Mid] = [.. calendar.Select((day, at) => (day, 300.0 + (at * 0.2) + (at % 3)))],
            [Worker.Ledger.LedgerMarket.Vix] = [.. calendar.Select((day, at) => (day, 15.0 + (at % 7)))],
        });
        var sectors = new Dictionary<string, string?>(StringComparer.Ordinal) { ["AAA"] = "Energy", ["BBB"] = "Energy", ["CCC"] = "Energy" };
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal) { ["BBB"] = NineFacts() };
        var breadth = calendar.Select((day, at) => (day, 0.4 + (0.001 * at))).ToDictionary(pair => pair.day, pair => pair.Item2);
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var series = inputs.Names.Select(name => Worker.Sweep.SweepColumns.Series(name, sessionAt)).ToArray();
        var columns = Worker.Sweep.SweepColumns.Sessions(series, inputs.Sessions);
        var members = Worker.Sweep.SweepBenchmark.On(series, calendar.Count);
        var (highs, lows) = Worker.Sweep.SweepIdeas.HighsAndLows(series, members, calendar.Count);
        var at = 265;
        var context = new Worker.Ledger.LedgerContext(new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(StringComparer.Ordinal), sectors, highs, lows, facts)
        {
            Ranks = new Worker.Ledger.FactorRanks(series, members),
            LargeBreadth = breadth,
        };
        var stored = Worker.Ledger.LedgerSetups.On("MID", series, columns, members, calendar, at, market, context)
            .Single(setups => setups.Family == Core.Families.DipRule.Name)
            .Rows
            .Single(row => row.Ticker == "BBB");

        Assert.Equal(new DateOnly(2026, 9, 7), calendar[at]);

        // Every check passing on the constructed filer, nine of nine; gross profit 580 over the quarter's end assets of
        // 2,000; return on assets 110 / 1,800, and less a year before's 95 / 1,700; the debt over the year's average assets
        // falling from 420 / 1,750 to 400 / 1,900; the current ratio from 800 / 560 to 900 / 600; the shares unchanged;
        // sales over the year's starting assets from 1,020 / 1,700 to 1,200 / 1,800; and the gross margin from 470 / 1,020
        // to 580 / 1,200.
        Assert.Equal(1.0, stored.Reading("fundamentals_score")!.Value, 12);
        Assert.Equal(0.29, stored.Reading("gross_profitability")!.Value, 12);
        Assert.Equal(110.0 / 1800, stored.Reading("return_on_assets")!.Value, 12);
        Assert.Equal((110.0 / 1800) - (95.0 / 1700), stored.Reading("return_on_assets_change")!.Value, 12);
        Assert.Equal(0.24 - (400.0 / 1900), stored.Reading("leverage_fall")!.Value, 12);
        Assert.Equal(1.5 - (800.0 / 560), stored.Reading("current_ratio_change")!.Value, 12);
        Assert.Equal(0.0, stored.Reading("share_fall")!.Value, 12);
        Assert.Equal((1200.0 / 1800) - 0.6, stored.Reading("turnover_change")!.Value, 12);
        Assert.Equal((580.0 / 1200) - (470.0 / 1020), stored.Reading("gross_margin_year_change")!.Value, 12);

        // A member with no filer reads none of the nine.
        var aaa = Worker.Ledger.LedgerSetups.On("MID", series, columns, members, calendar, at, market, context)
            .SelectMany(setups => setups.Rows)
            .FirstOrDefault(row => row.Ticker == "AAA");

        Assert.True(aaa is null || LedgerReadings.All.Skip(LedgerReadings.IndexOf(LedgerReadings.ScorecardFirst)).All(reading => aaa.Reading(reading.Column) is null));

        // Rebuilt by the dip's own gates over the history cut at its session, every reading the same.
        var cut = Worker.Ledger.LedgerPointInTime.Cut(inputs, calendar[at]);
        var cutMarket = Worker.Ledger.LedgerPointInTime.Cut(market, calendar[at]);
        var (held, rebuilt) = Worker.Ledger.LedgerPointInTime.Setup("MID", cut, cutMarket, new Dictionary<string, IReadOnlyList<Core.Readings.FiledIncome>>(StringComparer.Ordinal), sectors, facts, Core.Families.DipRule.Name, "BBB", largeBreadth: breadth);

        Assert.True(held);
        Assert.Empty(Worker.Ledger.LedgerPointInTime.Differences(stored.Readings, rebuilt!));
        Assert.All(LedgerReadings.All.Skip(LedgerReadings.IndexOf(LedgerReadings.ScorecardFirst)), reading => Assert.NotNull(rebuilt![LedgerReadings.IndexOf(reading.Column)]));
    }

    // The sources 22.8 writes.
    static readonly string[] ScorecardSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/BriefScreen.cs",
        "src/EquityBrief.Api/Reading/FundamentalsScreen.cs",
        "src/EquityBrief.Api/Reading/NameScreen.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Fundamentals.cs",
        "src/EquityBrief.Api/Reading/ReadApi.cs",
        "src/EquityBrief.Api/Reading/RunScreen.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Core/Ledger/FiledFacts.cs",
        "src/EquityBrief.Core/Ledger/FundamentalScorecard.cs",
        "src/EquityBrief.Core/Ledger/LedgerHeadings.cs",
        "src/EquityBrief.Core/Ledger/LedgerReadings.cs",
        "src/EquityBrief.Core/Ledger/ScorecardText.cs",
        "src/EquityBrief.Core/Loop/RidgeScore.cs",
        "src/EquityBrief.Core/Providers/IFiledRevenueFeed.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Chart.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Fundamentals.cs",
        "src/EquityBrief.Worker/Fundamentals/FundamentalScorer.cs",
        "src/EquityBrief.Worker/Ledger/LedgerPointInTime.cs",
        "src/EquityBrief.Worker/Ledger/LedgerSetups.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.cs",
        "src/EquityBrief.Worker/Loop/ConditionProcedures.cs",
        "src/EquityBrief.Worker/Loop/IdeaProcedures.cs",
        "src/EquityBrief.Worker/Loop/LoopReadings.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.Links.cs",
        "src/EquityBrief.Worker/Loop/WalkForwardTester.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Worker/Program.cs",
    ];

    // No source a rule's version pins is one 22.8 writes, so no existing rule's version moves and no record restarts, the
    // reader shown to find a source a rule pins; and the revenue rule every S&P 400 and 600 drift rule pins is among those
    // pinned and not among those 22.8 writes. From 22.11 the quality rules pin the scorecard and the readings their step
    // reads, and no rule registered before them did.
    // see: A quality rule is registered as the family on its index and kept by the family's own step alone
    [Fact]
    public void NoSourceARulesVersionPinsIsOne22Point8WritesSoNoExistingRulesVersionMoves()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .Where(evaluator => evaluator is not (EquityBrief.Core.Candidates.QualityCandidate or EquityBrief.Core.Candidates.ImprovingCandidate))
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(ScorecardSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(ScorecardSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Core/Families/FirstFiledRevenue.cs", pinned);
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }

    // The night the step is run on, a Friday.
    static readonly DateOnly ScoredNight = new(2026, 10, 30);

    // The constructed filer's net income as quarters beside its spans, 25 and 25 in the second half of 2025 and 30 and 30
    // in the first of 2026, summing to the 110 its four quarters read, so the valuation reading reads its earnings.
    static IEnumerable<FiledFactRow> NineQuarters() =>
    [
        NineFact("NetIncomeLoss", new(2025, 7, 1), new(2025, 9, 30), 25m, new(2025, 11, 5)),
        NineFact("NetIncomeLoss", new(2025, 10, 1), new(2025, 12, 31), 25m, new(2026, 2, 20)),
        NineFact("NetIncomeLoss", new(2026, 1, 1), new(2026, 3, 31), 30m, new(2026, 5, 5)),
        NineFact("NetIncomeLoss", new(2026, 4, 1), new(2026, 6, 30), 30m, new(2026, 8, 5)),
    ];

    sealed record StoredScore(
        string Index,
        string? Quarter,
        string? YearBefore,
        string? Awaited,
        long Financial,
        long Passed,
        long Applied,
        string? Word,
        IReadOnlyList<ScorecardCheck> Checks,
        double? Gross,
        long? Rank,
        long? Ranked,
        double? Multiple,
        string? Industry,
        double? IndustryMultiple);

    static StoredScore? ScoreOf(TemporaryStore store, string ticker, DateOnly night)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT index_code, quarter, year_before, awaited, financial, passed, applied, word, checks, gross_profitability, gross_rank, gross_ranked, multiple, industry, industry_multiple " +
            "FROM fundamental_score WHERE session_date = $session AND ticker = $ticker;";
        command.Parameters.AddWithValue("$session", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ticker", ticker);

        using var reader = command.ExecuteReader();

        if (!reader.Read())
        {
            return null;
        }

        string? Text(int at) => reader.IsDBNull(at) ? null : reader.GetString(at);
        double? Real(int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);
        long? Whole(int at) => reader.IsDBNull(at) ? null : reader.GetInt64(at);

        return new StoredScore(
            reader.GetString(0), Text(1), Text(2), Text(3), reader.GetInt64(4), reader.GetInt64(5), reader.GetInt64(6), Text(7),
            ScorecardText.ChecksOf(reader.GetString(8)), Real(9), Whole(10), Whole(11), Real(12), Text(13), Real(14));
    }

    // The night's step over a constructed store of nine members, eight of the S&P 500 and one of the S&P 400, each scored
    // through the core's one scorecard over its filer's facts as first filed: AAA, GGG and DDD industrials in the same
    // industry, GGG's gross profit higher and DDD's shares 3 per cent more than a year before; BBB and HHH banks; CCC
    // filing only its net income, its cash and its assets; EEE with no company row; FFF holding no bar on the night; and
    // MMM on the S&P 400, its gross margin lower than a year before. The calendar dates AAA's quarter 45 days after the
    // one scored, CCC's 46 and DDD's the next quarter, reported two days before the night.
    // see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
    [Fact]
    public async Task TheNightsStepScoresEveryMemberHoldingABarOnItsFilingsAndStoresItsChecksItsQuartersAndItsPlaces()
    {
        using var store = new TemporaryStore().Migrated();
        var night = ScoredNight;
        var cik = 0;

        void Member(string index, string ticker, string? sector, string? industry, decimal close, IEnumerable<FiledFactRow> facts, bool onTheNight = true)
        {
            cik++;
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{index}', '{ticker}', '2025-01-02', NULL, '2026-10-06T21:00:00Z');");

            if (sector is not null)
            {
                store.Execute($"INSERT INTO company (ticker, fetched_at, cik, sector, industry) VALUES ('{ticker}', '2026-09-01T00:00:00Z', '{cik}', '{sector}', '{industry}');");
            }

            foreach (var fact in facts)
            {
                store.Execute(
                    "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES " +
                    FormattableString.Invariant($"('{SecEdgarArchive.Padded(cik.ToString(CultureInfo.InvariantCulture))}', '{fact.Concept}', '{fact.Start:yyyy-MM-dd}', '{fact.End:yyyy-MM-dd}', '{fact.Value}', '{fact.Filed:yyyy-MM-dd}', '{fact.Form}', '{fact.Accession}', 'filings-first');"));
            }

            DateOnly[] sessions = onTheNight ? [new(2026, 7, 24), new(2026, 10, 29), night] : [new(2026, 7, 24), new(2026, 10, 29)];

            foreach (var session in sessions)
            {
                store.Execute(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES " +
                    FormattableString.Invariant($"('{ticker}', '{session:yyyy-MM-dd}', '{close}', '{close}', '{close}', '{close}', 1000, 'bulk', '{session:yyyy-MM-dd}T21:10:00Z', '{close}');"));
            }
        }

        void Reported(string ticker, string day, string quarter) =>
            store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('{ticker}', '{day}', 'earnings', 'after', '{{\"periodEnd\":\"{quarter}\"}}', '{day}T21:00:00Z');");

        Member("GSPC", "AAA", "Industrials", "Machinery", 22m, [.. NineFacts(), .. NineQuarters()]);
        Member("GSPC", "BBB", "Financials", "Banks", 11m, [.. NineFacts(), .. NineQuarters()]);
        Member("GSPC", "CCC", "Industrials", "Machinery", 40m, [.. NineFacts().Where(fact => fact.Concept is "NetIncomeLoss" or "NetCashProvidedByUsedInOperatingActivities" or "Assets")]);
        Member("GSPC", "DDD", "Industrials", "Machinery", 33m, [.. NineFacts(sharesNow: 103m), .. NineQuarters()]);
        Member("GSPC", "EEE", null, null, 50m, []);
        Member("GSPC", "FFF", "Industrials", "Machinery", 60m, [.. NineFacts(), .. NineQuarters()], onTheNight: false);
        Member("GSPC", "GGG", "Industrials", "Machinery", 27.5m, [.. NineFacts(gross2026: 400m), .. NineQuarters()]);
        Member("GSPC", "HHH", "Financials", "Banks", 13.2m, [.. NineFacts(), .. NineQuarters()]);
        Member("MID", "MMM", "Industrials", "Machinery", 16.5m, [.. NineFacts(gross2026: 254m), .. NineQuarters()]);

        Reported("AAA", "2026-07-30", "2026-08-14");
        Reported("AAA", "2026-11-05", "2026-09-30");
        Reported("CCC", "2026-08-20", "2026-08-15");
        Reported("DDD", "2026-10-28", "2026-09-30");

        // An earlier session's row, which the night leaves.
        store.Execute("INSERT INTO fundamental_score (index_code, session_date, ticker, financial, passed, applied, checks) VALUES ('GSPC', '2026-10-29', 'AAA', 0, 1, 1, '[]');");

        var clock = FixedClock.At(new DateTimeOffset(night.ToDateTime(new TimeOnly(23, 0)), TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FundamentalScorer(clock, store.DatabaseFile).RunAsync("GSPC", "scores-first", wider: ["MID", "SML"]);

        // Nine members read, eight holding a bar on the night, six given a word: FFF unscored, and CCC and EEE given none.
        Assert.Equal((night, 9, 8, 6), (outcome.Night, outcome.Members, outcome.Scored, outcome.Worded));
        Assert.Null(ScoreOf(store, "FFF", night));

        // AAA: every check passing on the constructed filer; gross profit over assets 580 / 2,000, 0.29, second of the five
        // S&P 500 members holding it, GGG's higher and the ties placed by ticker; 22 over earnings of 110 on 100 shares, 20
        // times; the median of its industry's three members with a multiple, 20, 25 and 30.9, 25; and the calendar's
        // quarter to 2026-08-14 no newer quarter, ending 45 days after the one scored and not more.
        var aaa = ScoreOf(store, "AAA", night)!;

        Assert.Equal(("GSPC", "2026-06-30", "2025-06-30", (string?)null, 0L, 9L, 9L, "strong"), (aaa.Index, aaa.Quarter, aaa.YearBefore, aaa.Awaited, aaa.Financial, aaa.Passed, aaa.Applied, aaa.Word));
        Assert.Equal(0.29, aaa.Gross!.Value, 12);
        Assert.Equal((2L, 5L), (aaa.Rank!.Value, aaa.Ranked!.Value));
        Assert.Equal(20.0, aaa.Multiple!.Value, 9);
        Assert.Equal("Machinery", aaa.Industry);
        Assert.Equal(25.0, aaa.IndustryMultiple!.Value, 9);
        Assert.Equal(FundamentalScorecard.Read([.. NineFacts(), .. NineQuarters()], night, false, NinePrices()).Checks, aaa.Checks);
        Assert.Equal((150m, 110m), (aaa.Checks[3].Amount, aaa.Checks[3].Against));

        // BBB: a financial company, the current ratio and the gross margin not applying, seven of seven; 11 over 1.10 a
        // share, 10 times; and its industry's two members with a multiple too few for a median.
        var bbb = ScoreOf(store, "BBB", night)!;

        Assert.Equal((1L, 7L, 7L, "strong"), (bbb.Financial, bbb.Passed, bbb.Applied, bbb.Word));
        Assert.Equal(CheckAnswer.NotApplying, bbb.Checks[5].Answer);
        Assert.Equal(CheckAnswer.NotApplying, bbb.Checks[7].Answer);
        Assert.Equal((3L, 5L), (bbb.Rank!.Value, bbb.Ranked!.Value));
        Assert.Equal(10.0, bbb.Multiple!.Value, 9);
        Assert.Null(bbb.IndustryMultiple);

        // CCC: four checks read, no word; the five it could not read naming their lines, its sales among them; no gross
        // profit, no place and no multiple, no count being filed; its industry's median still drawn; and its quarter to
        // 2026-08-15, 46 days after the one scored, awaited.
        var ccc = ScoreOf(store, "CCC", night)!;

        Assert.Equal(("2026-06-30", "2026-08-15", 4L, 4L, (string?)null), (ccc.Quarter, ccc.Awaited, ccc.Passed, ccc.Applied, ccc.Word));
        Assert.Equal(
            [FundamentalScorecard.DebtLine, FundamentalScorecard.CurrentAssetsLine, FundamentalScorecard.SharesLine, FundamentalScorecard.SalesLine, FundamentalScorecard.SalesLine],
            ccc.Checks.Where(check => check.Answer == CheckAnswer.NotFiled).Select(check => check.Missing));
        Assert.Equal(((double?)null, (long?)null, (double?)null), (ccc.Gross, ccc.Rank, ccc.Multiple));
        Assert.Equal(25.0, ccc.IndustryMultiple!.Value, 9);

        // DDD: 103 shares against 100 failing the share check, eight of nine and still strong; 33 over 110 / 103 a share,
        // 30.9 times; and its quarter to 2026-09-30, reported on 2026-10-28 and not yet filed, awaited beside the score of the
        // one before.
        var ddd = ScoreOf(store, "DDD", night)!;

        Assert.Equal(("2026-06-30", "2026-09-30", 8L, 9L, "strong"), (ddd.Quarter, ddd.Awaited, ddd.Passed, ddd.Applied, ddd.Word));
        Assert.Equal(CheckAnswer.Failed, ddd.Checks[6].Answer);
        Assert.Equal(0.03, ddd.Checks[6].Now!.Value, 12);
        Assert.Equal(30.9, ddd.Multiple!.Value, 9);
        Assert.Equal((4L, 5L), (ddd.Rank!.Value, ddd.Ranked!.Value));

        // EEE: no filer, so no quarter and every check not filed.
        var eee = ScoreOf(store, "EEE", night)!;

        Assert.Equal(((string?)null, 0L, 0L, (string?)null, (string?)null), (eee.Quarter, eee.Passed, eee.Applied, eee.Word, eee.Industry));
        Assert.All(eee.Checks, check => Assert.Equal(CheckAnswer.NotFiled, check.Answer));

        // GGG first on gross profit, 680 / 2,000, 0.34, and 27.5 over 1.10, 25 times; HHH last of the five, 12 times.
        var ggg = ScoreOf(store, "GGG", night)!;
        var hhh = ScoreOf(store, "HHH", night)!;

        Assert.Equal(0.34, ggg.Gross!.Value, 12);
        Assert.Equal((1L, 5L), (ggg.Rank!.Value, ggg.Ranked!.Value));
        Assert.Equal(25.0, ggg.Multiple!.Value, 9);
        Assert.Equal((5L, 5L, 1L), (hhh.Rank!.Value, hhh.Ranked!.Value, hhh.Financial));
        Assert.Equal(12.0, hhh.Multiple!.Value, 9);

        // MMM on the S&P 400: its gross margin, 534 / 1,200 against 470 / 1,020, lower, eight of nine; placed first of one on
        // its own index; and its industry there holding no other member.
        var mmm = ScoreOf(store, "MMM", night)!;

        Assert.Equal(("MID", 8L, 9L, CheckAnswer.Failed), (mmm.Index, mmm.Passed, mmm.Applied, mmm.Checks[7].Answer));
        Assert.Equal(534.0 / 2000, mmm.Gross!.Value, 12);
        Assert.Equal((1L, 1L), (mmm.Rank!.Value, mmm.Ranked!.Value));
        Assert.Equal(15.0, mmm.Multiple!.Value, 9);
        Assert.Null(mmm.IndustryMultiple);

        // The run's row, and the earlier session's row left; run again, the night's rows replaced and no other's.
        Assert.Equal(
            ["9 member(s) read for 2026-10-30, 8 holding a bar on it scored, 6 given a word, 2 financial, 2 with a newer quarter reported and not yet filed"],
            Rows(store, $"SELECT detail FROM run_log WHERE stage = '{FundamentalScorer.Stage}';"));

        await new FundamentalScorer(clock, store.DatabaseFile).RunAsync("GSPC", "scores-again", wider: ["MID", "SML"]);

        Assert.Equal(["2026-10-29 1", "2026-10-30 8"], Rows(store, "SELECT session_date || ' ' || COUNT(*) FROM fundamental_score GROUP BY session_date ORDER BY session_date;"));
    }
}
