using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Core.Research;
using EquityBrief.Core.Tiles;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Dividends;
using EquityBrief.Worker.Treasury;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 18.2: what the name page's latest quarter, segments, margins, analysts, dividend safety and
// valuation draw, each worked by hand over constructed quarters, fetches, dividends and members, and the segment rule over
// the fixture's captured segment reports; the estimate trend read off an answer; the Treasury's table read as it sends it
// and kept a session a row; the dividends kept from the night's answer and from the history run; the release before the
// newest handed to what management said alone; and the replay keeping each of them as the captures file it.
// see: The latest quarter is read line by line against the estimate kept before its report and the same quarter a year earlier
// see: The segment table is the segments whose revenue sums to the company's total within rounding, and the raw table where none do
public partial class FixtureExpectations
{
    // The rows 18.2 adds that this check reaches: section 17's two and section 18's six.
    internal static string[] ReportRows =>
    [
        CheckReach.Key(Scope.LimitsTable, "Report windows"),
        CheckReach.Key(Scope.LimitsTable, "Dividend history run"),
        CheckReach.Key(Scope.FailureTable, "The Treasury refuses, or answers in a form that cannot be read"),
        CheckReach.Key(Scope.FailureTable, "A segment table whose segments sum to no total"),
        CheckReach.Key(Scope.FailureTable, "A quarter stored before its cash flow lines were kept"),
        CheckReach.Key(Scope.FailureTable, "A month of the analysts' ratings no fetch fell in"),
        CheckReach.Key(Scope.FailureTable, "The dividends history run past the stop"),
        CheckReach.Key(Scope.FailureTable, "No results release before the newest"),
    ];

    const string KeysightCik = "0001601046";

    static readonly DateTimeOffset ReportFetchedAt = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    // A quarter of one fetch as the quarter's regions read it, every line read unless said otherwise.
    static QuarterRow Filed(
        DateOnly end,
        decimal? revenue,
        decimal? gross = null,
        decimal? operating = null,
        decimal? net = null,
        decimal? cashFromOperations = null,
        decimal? freeCash = null,
        decimal? eps = null,
        decimal? estimate = null,
        decimal? trailing = null,
        decimal? closeAfter = null,
        decimal? dividendsPaid = null,
        bool linesRead = true) =>
        new(end, end.AddDays(30), revenue, gross, operating, net, cashFromOperations, freeCash, dividendsPaid, eps, estimate, trailing, closeAfter, linesRead);

    // The last day of the month the quarter `after` quarters on from June 2023's ends in.
    static DateOnly QuarterEnd(int after) => new DateOnly(2023, 6, 1).AddMonths((3 * after) + 1).AddDays(-1);

    [Fact]
    public void TheLatestQuarterIsReadLineByLineAgainstTheEstimateKeptBeforeItsReportAndAYearEarlier()
    {
        var newest = new DateOnly(2026, 6, 30);
        var yearEarlier = new DateOnly(2025, 6, 30);

        // The newest quarter against the same quarter a year earlier, three quarters between them the lines never read.
        QuarterRow[] quarters =
        [
            Filed(newest, 1100m, 660m, 275m, 220m, 330m, 286m, 2.20m, 2.00m),
            Filed(new DateOnly(2026, 3, 31), 1000m, 600m, 250m, 200m, 300m, 260m, 2.00m, 1.90m),
            Filed(new DateOnly(2025, 12, 31), 950m, 570m, 230m, 180m, 280m, 250m, 1.90m, 1.80m),
            Filed(new DateOnly(2025, 9, 30), 920m, 550m, 200m, 150m, 275m, 230m, 1.70m, 1.60m),
            Filed(yearEarlier, 900m, 495m, 180m, 135m, 270m, 225m, 1.60m, 1.55m),
        ];

        var latest = QuarterLines.Latest(quarters, new RevenueEstimate(1050m, new DateOnly(2026, 7, 15)))!;

        Assert.Equal((newest, yearEarlier), (latest.Quarter, latest.YearEarlier!.Value));
        Assert.Equal(
            [QuarterLines.Revenue, QuarterLines.GrossProfit, QuarterLines.OperatingIncome, QuarterLines.NetIncome, QuarterLines.Eps, QuarterLines.OperatingCashFlow, QuarterLines.FreeCashFlow],
            latest.Lines.Select(line => line.Line));

        var lines = latest.Lines.ToDictionary(line => line.Line);

        // Revenue 1,100 against the estimate of 1,050 kept before the report is 50 above it, 50 / 1,050 = 4.7619%, and
        // against 900 a year earlier 200 / 900 = 22.2222% up.
        var revenue = lines[QuarterLines.Revenue];

        Assert.Equal((1100m, 1050m, 50m, 900m), (revenue.Reported!.Value, revenue.Estimate!.Value, revenue.Difference!.Value, revenue.YearEarlier!.Value));
        Assert.Equal(4.7619, revenue.AgainstEstimate!.Value, 4);
        Assert.Equal(22.2222, revenue.OnTheYear!.Value, 4);
        Assert.Null(revenue.Margin);

        // The three income lines with their margins on revenue now and a year earlier: 660 of 1,100 is 60% against 495 of
        // 900, 55%; 275 is 25% against 180, 20%; 220 is 20% against 135, 15%. On the year 165 / 495, 95 / 180 and 85 / 135.
        void Income(string line, double margin, double before, double onTheYear)
        {
            Assert.Equal(margin, lines[line].Margin!.Value, 9);
            Assert.Equal(before, lines[line].MarginYearEarlier!.Value, 9);
            Assert.Equal(onTheYear, lines[line].OnTheYear!.Value, 4);
            Assert.Null(lines[line].Estimate);
        }

        Income(QuarterLines.GrossProfit, 60.0, 55.0, 33.3333);
        Income(QuarterLines.OperatingIncome, 25.0, 20.0, 52.7778);
        Income(QuarterLines.NetIncome, 20.0, 15.0, 62.963);

        // Earnings a share against the estimate the earnings history files with the quarter: 2.20 against 2.00 is 0.20 and
        // 10% above it, and against 1.60 a year earlier 37.5% up. No margin is read for a figure a share.
        var eps = lines[QuarterLines.Eps];

        Assert.Equal((2.20m, 2.00m, 0.20m), (eps.Reported!.Value, eps.Estimate!.Value, eps.Difference!.Value));
        Assert.Equal(10.0, eps.AgainstEstimate!.Value, 9);
        Assert.Equal(37.5, eps.OnTheYear!.Value, 9);
        Assert.True(eps.PerShare);
        Assert.Null(eps.Margin);

        // The cash lines on a year with no estimate: 330 against 270 is 22.2222% up and 286 against 225 27.1111% up.
        Assert.Equal(22.2222, lines[QuarterLines.OperatingCashFlow].OnTheYear!.Value, 4);
        Assert.Equal(27.1111, lines[QuarterLines.FreeCashFlow].OnTheYear!.Value, 4);
        Assert.Null(lines[QuarterLines.FreeCashFlow].Estimate);

        // No revenue estimate kept before the report reads none, and a change is read only against a figure above nought.
        Assert.Null(QuarterLines.Latest(quarters)!.Lines[0].Estimate);
        Assert.Null(QuarterLines.Latest([quarters[0], quarters[4] with { NetIncome = -5m }])!.Lines.Single(line => line.Line == QuarterLines.NetIncome).OnTheYear);

        // A quarter stored before the fetch kept the gross profit and the cash flow lines reads those lines as not read,
        // and a line the provider filed as none on a fetch that read them as not filed.
        var before = QuarterLines.Latest([quarters[0] with { GrossProfit = null, FreeCashFlow = null, LinesRead = false }, quarters[4]])!.Lines.ToDictionary(line => line.Line);
        var filedNone = QuarterLines.Latest([quarters[0] with { GrossProfit = null }, quarters[4]])!.Lines.ToDictionary(line => line.Line);

        Assert.Equal(QuarterLines.FetchedBefore, before[QuarterLines.GrossProfit].NotRead);
        Assert.Equal(QuarterLines.FetchedBefore, before[QuarterLines.FreeCashFlow].NotRead);
        Assert.Null(before[QuarterLines.Revenue].NotRead);
        Assert.Equal(QuarterLines.NotFiled, filedNone[QuarterLines.GrossProfit].NotRead);

        // A fetch holding no quarter with a revenue or reported earnings reads no latest quarter.
        Assert.Null(QuarterLines.Latest([Filed(newest, null)]));
    }

    [Fact]
    public void TheGrowthReadsTheEightNewestQuartersAndTheMarginsTheTwelveNewest()
    {
        // Thirteen quarters from 2023-06-30 to 2026-06-30, revenue 1,000 rising by 100 a quarter and earnings a share 1.00
        // by 0.10, gross, operating and net income at a half, a fifth and a tenth of revenue.
        QuarterRow[] quarters =
        [
            .. Enumerable.Range(0, 13).Select(at =>
            {
                var revenue = 1000m + (100m * at);

                return Filed(QuarterEnd(at), revenue, revenue / 2, revenue / 5, revenue / 10, eps: 1.00m + (0.10m * at));
            }),
        ];

        Assert.Equal((new DateOnly(2023, 6, 30), new DateOnly(2026, 6, 30)), (quarters[0].PeriodEnd, quarters[12].PeriodEnd));

        // Eight, oldest first: the sixth quarter's 1,500 against 1,100 a year earlier is 36.3636% up, and the newest's 2,200
        // against 1,800 is 22.2222% and its 2.20 a share against 1.80 the same.
        var growth = QuarterLines.Growth(quarters);

        Assert.Equal(8, QuarterLines.GrowthQuarters);
        Assert.Equal(QuarterLines.GrowthQuarters, growth.Count);
        Assert.Equal((quarters[5].PeriodEnd, quarters[12].PeriodEnd), (growth[0].Quarter, growth[^1].Quarter));
        Assert.Equal(36.3636, growth[0].Revenue!.Value, 4);
        Assert.Equal(22.2222, growth[^1].Revenue!.Value, 4);
        Assert.Equal(22.2222, growth[^1].Eps!.Value, 4);

        // Twelve, oldest first, each 50%, 20% and 10%: the oldest of thirteen is not among them.
        var margins = QuarterLines.Margins(quarters);

        Assert.Equal(12, QuarterLines.MarginQuarters);
        Assert.Equal(QuarterLines.MarginQuarters, margins.Count);
        Assert.Equal(quarters[1].PeriodEnd, margins[0].Quarter);
        Assert.All(margins, quarter =>
        {
            Assert.Equal(50.0, quarter.Gross!.Value, 9);
            Assert.Equal(20.0, quarter.Operating!.Value, 9);
            Assert.Equal(10.0, quarter.Net!.Value, 9);
        });

        // A quarter whose year-earlier quarter is not stored reads no growth, and one with no gross profit no gross margin.
        Assert.Null(QuarterLines.Growth(quarters[..5])[0].Revenue);
        Assert.Null(QuarterLines.Margins([quarters[12] with { GrossProfit = null }])[0].Gross);
    }

    [Fact]
    public void EachQuartersMultipleIsTheCloseAfterItsReportOverItsFourQuartersEarnings()
    {
        QuarterRow[] quarters =
        [
            Filed(new DateOnly(2026, 6, 30), 1m, trailing: 8.80m, closeAfter: 220m),
            Filed(new DateOnly(2026, 3, 31), 1m, trailing: 3m, closeAfter: 100m),
            Filed(new DateOnly(2025, 12, 31), 1m, trailing: 0m, closeAfter: 180m),
            Filed(new DateOnly(2025, 9, 30), 1m, trailing: 9m),
        ];

        // 220 over 8.80 is 25; 100 over 3 is 33.333333 to six places; a quarter whose four quarters earned nothing and one
        // with no close after its report read none. Oldest first.
        Assert.Equal(
            [(new DateOnly(2026, 3, 31), 33.333333m), (new DateOnly(2026, 6, 30), 25m)],
            QuarterLines.Multiples(quarters).Select(multiple => (multiple.Quarter, multiple.Multiple)));
    }

    // A table in millions of one quarter, its consolidated revenue and each group's, as the archive's reader gives it.
    static SegmentBreakdown SegmentsOf(decimal total, params (string Label, decimal Revenue)[] groups)
    {
        var quarter = new ReportPeriod(3, new DateOnly(2026, 6, 30));

        return new SegmentBreakdown(
            "R9.htm",
            "Segment Information (Details) - USD ($) $ in Millions",
            1_000_000,
            [quarter],
            [new SegmentFigure("us-gaap_Revenues", "Revenues", null, quarter, total)],
            [.. groups.Select(group => new SegmentGroup(group.Label, "us-gaap_StatementBusinessSegmentsAxis=x", [new SegmentFigure("us-gaap_Revenues", "Revenues", null, quarter, group.Revenue)]))]);
    }

    [Fact]
    public void TheSegmentTableSumsToTheCompanysTotalOverTheCapturesAndAReportSummingToNoneDrawsAsFiled()
    {
        // Apple's five regions, read by hand off the captured report: 45,781, 29,395, 18,816, 6,554 and 8,871 million sum
        // to 109,417, the quarter's net sales, its corporate amounts left out as no segment.
        var apple = SegmentTable.Read(SecEdgarArchive.Breakdown(Captured("segment-report-AAPL-R46.htm"), "R46.htm")!);

        Assert.True(apple.Clean, apple.Raw);
        Assert.Equal(["Americas", "Europe", "Greater China", "Japan", "Rest of Asia Pacific"], apple.Rows.Select(row => row.Segment));
        Assert.Equal((109_417_000_000m, 109_417_000_000m), (apple.Total!.Value, apple.Sum!.Value));
        Assert.Equal(
            [45_781_000_000m, 29_395_000_000m, 18_816_000_000m, 6_554_000_000m, 8_871_000_000m],
            apple.Rows.Select(row => row.Revenue));

        // The Americas, 45,781 against 41,198 a year earlier, is 4,583 / 41,198 = 11.1243% up, its operating income of
        // 21,701 against 16,511 is 5,190 / 16,511 = 31.4336% up, and its share of 109,417 is 41.8408%. Europe's 29,395
        // against 24,014 is 22.4078% up and 26.8651% of the total.
        var americas = apple.Rows[0];

        Assert.Equal(
            (45_781_000_000m, 41_198_000_000m, 21_701_000_000m, 16_511_000_000m),
            (americas.Revenue, americas.RevenueYearEarlier!.Value, americas.Earnings!.Value, americas.EarningsYearEarlier!.Value));
        Assert.Equal(11.1243, americas.RevenueChange!.Value, 4);
        Assert.Equal(31.4336, americas.EarningsChange!.Value, 4);
        Assert.Equal(41.8408, americas.Share, 4);
        Assert.Equal(22.4078, apple.Rows[1].RevenueChange!.Value, 4);
        Assert.Equal(26.8651, apple.Rows[1].Share, 4);

        // Keysight's two groups, each filed under three of the table's groups by one name once the axis is taken off, sum
        // to 1,846, its "Total segments" groups and the axis's own group left out: the communications group's 1,345
        // against 940 is 405 / 940 = 43.0851% up with operating income of 458 against 246, and 72.8602% of the total; the
        // industrial group's 501 against 412 is 21.6019% up with 155 against 92, 68.4783% up.
        var keysight = SegmentTable.Read(SecEdgarArchive.Breakdown(Captured("segment-report-KEYS-R85.htm"), "R85.htm")!);

        Assert.True(keysight.Clean, keysight.Raw);
        Assert.Equal(["Communications Solutions Group", "Electronic Industrial Solutions Group"], keysight.Rows.Select(row => row.Segment));
        Assert.Equal((1_846_000_000m, 1_846_000_000m), (keysight.Total!.Value, keysight.Sum!.Value));
        Assert.Equal(
            (1_345_000_000m, 940_000_000m, 458_000_000m, 246_000_000m),
            (keysight.Rows[0].Revenue, keysight.Rows[0].RevenueYearEarlier!.Value, keysight.Rows[0].Earnings!.Value, keysight.Rows[0].EarningsYearEarlier!.Value));
        Assert.Equal(43.0851, keysight.Rows[0].RevenueChange!.Value, 4);
        Assert.Equal(72.8602, keysight.Rows[0].Share, 4);
        Assert.Equal(
            (501_000_000m, 412_000_000m, 155_000_000m, 92_000_000m),
            (keysight.Rows[1].Revenue, keysight.Rows[1].RevenueYearEarlier!.Value, keysight.Rows[1].Earnings!.Value, keysight.Rows[1].EarningsYearEarlier!.Value));
        Assert.Equal(21.6019, keysight.Rows[1].RevenueChange!.Value, 4);
        Assert.Equal(68.4783, keysight.Rows[1].EarningsChange!.Value, 4);

        // Netflix's narrative table marks each money row with the table's own currency and states one country against the
        // company, 5,100,000 thousand against 12,559,938 thousand, so the one segment sums to no total and the table is
        // drawn as filed with what it summed to and why.
        var netflix = SegmentTable.Read(SecEdgarArchive.Breakdown(Captured("segment-report-NFLX-R65.htm"), "R65.htm")!);

        Assert.False(netflix.Clean);
        Assert.Empty(netflix.Rows);
        Assert.Equal((12_559_938_000m, 5_100_000_000m), (netflix.Total!.Value, netflix.Sum!.Value));
        Assert.Equal("the segments' revenue does not sum to the company's within rounding", netflix.Raw);

        // The tolerance is half the table's unit a figure, the segments' and the total's: two segments in millions may
        // miss the total by a million and not by two.
        Assert.True(SegmentTable.Read(SegmentsOf(10_000_000_000m, ("Upstream", 6_000_000_000m), ("Downstream", 3_999_000_000m))).Clean);
        Assert.False(SegmentTable.Read(SegmentsOf(10_000_000_000m, ("Upstream", 6_000_000_000m), ("Downstream", 3_998_000_000m))).Clean);

        // An elimination, a total and a corporate amount are no segments: a table whose segments sum to the total only
        // with its eliminations is drawn as filed, and a subtotal and a corporate group beside them change nothing.
        var eliminated = SegmentTable.Read(SegmentsOf(
            10_000_000_000m,
            ("Upstream", 6_000_000_000m),
            ("Downstream", 4_500_000_000m),
            ("Intersegment eliminations", -500_000_000m),
            ("Total segments", 10_500_000_000m),
            ("Corporate and other", 0m)));

        Assert.False(eliminated.Clean);
        Assert.Equal(10_500_000_000m, eliminated.Sum!.Value);

        // A table stating no total of its own is summed against the quarter's reported revenue handed to it.
        var unstated = SegmentsOf(10_000_000_000m, ("Upstream", 6_000_000_000m), ("Downstream", 4_000_000_000m)) with { Consolidated = [] };

        Assert.True(SegmentTable.Read(unstated, 10_000_000_000m).Clean);
        Assert.Equal("neither the table nor the quarter states the company's revenue to sum against", SegmentTable.Read(unstated).Raw);
    }

    [Fact]
    public void TheAnalystsReadingIsWorkedByHandAndAMonthNoFetchFellInHoldsNone()
    {
        var night = new DateOnly(2026, 10, 8);

        EstimatePeriod year = new(
            EstimatePeriod.CurrentYear, new DateOnly(2026, 12, 31), 10.00m, 9.00m, 11.00m, 8.00m, 20, 1000m, 950m, 1050m, 800m, 18,
            10.00m, 9.90m, 9.50m, 9.20m, 9.00m, 3, 8, 1, 2);

        RatingFetch[] fetches =
        [
            new(new DateOnly(2026, 7, 1), 5, 5, 10, 0, 0, 2.3, 150m),
            new(new DateOnly(2026, 7, 15), 6, 5, 9, 0, 0, 2.2, 160m),
            new(new DateOnly(2026, 8, 20), null, null, null, null, null, 2.2, 165m),
            new(new DateOnly(2026, 9, 27), 9, 6, 9, 0, 1, 2.1, 180m),
            new(new DateOnly(2026, 10, 9), 9, 6, 9, 0, 1, 2.0, 190m),
        ];

        var analysts = AnalystView.Of(new DateOnly(2026, 9, 27), [year], fetches, night, 150m)!;

        // The year's consensus of 10.00 against 8.00 a year before is 25% growth by 20 analysts, its revenue's 1,000 against
        // 800 25% too, and the consensus now against 30 days before, 10.00 against 9.50, is 0.50 / 9.50 = 5.2632% up.
        var consensus = Assert.Single(analysts.Consensus);

        Assert.Equal(25.0, consensus.EpsGrowth!.Value, 9);
        Assert.Equal(25.0, consensus.RevenueGrowth!.Value, 9);
        Assert.Equal((20, 9.00m, 11.00m), (consensus.EpsAnalysts!.Value, consensus.EpsLow!.Value, consensus.EpsHigh!.Value));

        var revisions = Assert.Single(analysts.Revisions);

        Assert.Equal((3, 1, 8, 2), (revisions.Up7!.Value, revisions.Down7!.Value, revisions.Up30!.Value, revisions.Down30!.Value));
        Assert.Equal(5.2632, revisions.Change30!.Value, 4);

        // The trend's five points, 90, 60, 30 and 7 days before the fetch and at it.
        Assert.Equal([90, 60, 30, 7, 0], AnalystView.TrendDays);
        Assert.Equal(AnalystView.TrendDays, analysts.Trends[EstimatePeriod.CurrentYear].Select(point => point.DaysAgo));
        Assert.Equal([9.00m, 9.20m, 9.50m, 9.90m, 10.00m], analysts.Trends[EstimatePeriod.CurrentYear].Select(point => point.Eps));

        // The mean and target are the newest fetch's on or before the night, not the one after it: 180 against a price of
        // 150 is 20% above it.
        Assert.Equal((2.1, 180m), (analysts.MeanRating!.Value, analysts.Target!.Value));
        Assert.Equal(20.0, analysts.TargetAgainstPrice!.Value, 9);

        // Twelve months to the night's, oldest first: July's newest counted fetch is the fifteenth's, August's fetch filed no
        // counts and October's falls after the night, so neither month holds one, and every month before July holds none.
        var months = analysts.Months;

        Assert.Equal(12, AnalystView.Months);
        Assert.Equal(AnalystView.Months, months.Count);
        Assert.Equal(((2025, 11), (2026, 10)), ((months[0].Year, months[0].Month), (months[^1].Year, months[^1].Month)));
        Assert.Equal(new DateOnly(2026, 7, 15), months.Single(month => month is { Year: 2026, Month: 7 }).Fetch!.FetchedOn);
        Assert.Null(months.Single(month => month is { Year: 2026, Month: 8 }).Fetch);
        Assert.Equal(new DateOnly(2026, 9, 27), months.Single(month => month is { Year: 2026, Month: 9 }).Fetch!.FetchedOn);
        Assert.Null(months[^1].Fetch);
        Assert.Equal(10, months.Count(month => month.Fetch is null));

        // No fetch and no trend read nothing.
        Assert.Null(AnalystView.Of(null, [], [], night, 150m));
    }

    [Fact]
    public void APayersDividendSafetyIsWorkedByHand()
    {
        var night = new DateOnly(2026, 10, 8);

        // Five payments in 2022, four a year from 2023 raised in 2024 and 2025, and three so far in 2026.
        KeptDividend[] kept =
        [
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2022, month, 10), 0.70m)),
            new KeptDividend(new DateOnly(2022, 12, 20), 0.70m),
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2023, month, 10), 0.90m)),
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2024, month, 10), 0.95m)),
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2025, month, 10), 1.00m)),
            .. new[] { 2, 5, 8 }.Select(month => new KeptDividend(new DateOnly(2026, month, 10), 1.00m)),
        ];

        // Four quarters' free cash flow, 100 + 90 + 110 + 100 = 400, and their dividends paid, filed as outflows, 200; a
        // fifth quarter older than the four that the payout never reads.
        CashQuarter[] quarters =
        [
            new(new DateOnly(2026, 6, 30), 100m, -50m),
            new(new DateOnly(2026, 3, 31), 90m, -50m),
            new(new DateOnly(2025, 12, 31), 110m, -50m),
            new(new DateOnly(2025, 9, 30), 100m, -50m),
            new(new DateOnly(2025, 6, 30), 1m, -1000m),
        ];

        (DateOnly, double)[] tenYears = [(new DateOnly(2026, 10, 7), 4.10), (night, 4.20), (new DateOnly(2026, 10, 9), 4.30)];

        var safety = DividendSafety.Of(4.00m, 100m, 8.00m, quarters, kept, tenYears, [(night, 100m)], night)!;

        // A forward rate of 4.00 at a price of 100 yields 4%, against the 10-year of 4.20 on the night, not the session after
        // it: 0.20 points under. The payout on earnings is 4.00 over 8.00, 50%, and on free cash flow 200 over 400, 50%.
        Assert.Equal((4.00m, true), (safety.Rate, safety.RateIsForward));
        Assert.Equal(4.0, safety.Yield!.Value, 9);
        Assert.Equal((4.20, night), (safety.TenYear!.Value, safety.TenYearOn!.Value));
        Assert.Equal(-0.20, safety.Spread!.Value, 9);
        Assert.Equal(50.0, safety.PayoutOnEarnings!.Value, 9);
        Assert.Equal((200m, 400m), (safety.DividendsPaid!.Value, safety.FreeCashFlow!.Value));
        Assert.Equal(50.0, safety.PayoutOnCash!.Value, 9);

        // 2022's five payments are 3.50 and 2023's four 3.60, no raise read across a different count; 2024's 3.80 and 2025's
        // 4.00 are raised; 2026's 3.00 so far is not whole. Two whole years in a row raised.
        Assert.Equal(
            ["2022 3.50 5 True ", "2023 3.60 4 True ", "2024 3.80 4 True True", "2025 4.00 4 True True", "2026 3.00 3 False "],
            safety.Years.Select(year => FormattableString.Invariant($"{year.Year} {year.Total:0.00} {year.Payments} {year.Whole} {year.Raised}")));
        Assert.Equal(2, safety.RaisedInARow);

        // The trailing yield on the night: the dividends of the 365 days to it, 2025-11-10's 1.00 and 2026's three, 4.00 over
        // a close of 100, 4%, beside the 10-year that session.
        var point = Assert.Single(safety.Series);

        Assert.Equal(night, point.Session);
        Assert.Equal(4.0, point.Yield!.Value, 9);
        Assert.Equal(4.20, point.TenYear!.Value, 9);
        Assert.Equal(365, DividendSafety.TrailingDays);

        // A year a raise cut short: 2025 below 2024 is no raise and ends the run.
        Assert.Equal(0, DividendSafety.InARow(DividendSafety.Years([.. kept.Select(paid => paid.ExDate.Year == 2025 ? paid with { Amount = 0.90m } : paid)], night)));

        // With no forward rate filed the rate is the kept dividends of the year to the night summed, 4.00; a company paying
        // none has no reading; and the payout on cash is not read where a quarter of the four holds no dividends paid.
        var summed = DividendSafety.Of(null, 100m, 8m, quarters, kept, tenYears, [], night)!;

        Assert.Equal((4.00m, false), (summed.Rate, summed.RateIsForward));
        Assert.Null(DividendSafety.Of(0m, 100m, 8m, quarters, [], tenYears, [], night));
        Assert.Null(DividendSafety.Of(4m, 100m, 8m, [quarters[0] with { DividendsPaid = null }, .. quarters[1..]], kept, tenYears, [], night)!.PayoutOnCash);
    }

    [Fact]
    public void TheValuationsMembersAreOrderedByTheMultipleWithTheMedianOfEachColumn()
    {
        // Four of the industry's members and the stock's own row, each with what the night read: ordered by the multiple,
        // lowest first and a member reading none last, the stock's own row marked.
        PeerValue[] members =
        [
            new("AAA", 20.0, 2.0, 5.0, false),
            new("BBB", 15.0, null, 10.0, false),
            new("CCC", null, 1.0, null, false),
            new("DDD", 30.0, 0.5, 3.0, false),
            new("OWN", 25.0, 3.0, 7.0, false),
        ];

        var table = PeerValuation.Of("Oil & Gas Integrated", "OWN", members)!;

        Assert.Equal(["BBB", "AAA", "OWN", "DDD", "CCC"], table.Rows.Select(row => row.Ticker));
        Assert.Equal(["OWN"], table.Rows.Where(row => row.Own).Select(row => row.Ticker));

        // The median of four multiples, 15, 20, 25 and 30, is the mean of the middle two, 22.5; of four yields, 0.5, 1, 2
        // and 3, 1.5; of four growths, 3, 5, 7 and 10, 6; and of three, the middle one.
        Assert.Equal((22.5, 4), (table.MedianMultiple!.Value, table.Multiples));
        Assert.Equal((1.5, 4), (table.MedianYield!.Value, table.Yields));
        Assert.Equal((6.0, 4), (table.MedianGrowth!.Value, table.Growths));
        Assert.Equal(20.0, PeerValuation.Median([15.0, 25.0, 20.0])!.Value);
        Assert.Null(PeerValuation.Median([]));

        // No member reads no table.
        Assert.Null(PeerValuation.Of("Oil & Gas Integrated", "OWN", []));
    }

    [Fact]
    public void TheGrowthTileReadsTheYearsConsensusAndFallsBackToSalesAndTheYieldTileCarriesTheTenYear()
    {
        EstimatePeriod year = new(
            EstimatePeriod.CurrentYear, new DateOnly(2026, 12, 31), 12.00m, null, null, 10.00m, 30, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null);
        var next = year with { Period = EstimatePeriod.NextYear, PeriodEnd = new DateOnly(2027, 12, 31) };

        TileQuarter[] quarters =
        [
            new(new DateOnly(2026, 6, 30), 120m, 2.20m, 2.00m),
            new(new DateOnly(2026, 3, 31), 110m, 2.00m, 2.05m),
            new(new DateOnly(2025, 12, 31), 105m, 1.90m, null),
            new(new DateOnly(2025, 9, 30), 100m, 1.80m, 1.80m),
            new(new DateOnly(2025, 6, 30), 100m, 2.00m, 1.90m),
            new(new DateOnly(2025, 3, 31), 95m, 1.70m, 1.60m),
            new(new DateOnly(2024, 12, 31), 90m, 1.60m, 1.50m),
            new(new DateOnly(2024, 9, 30), 85m, 1.50m, 1.40m),
        ];

        // The year's consensus of 12.00 against 10.00 a year before is 20%, by 30 analysts, through the year's end and dated
        // by the fetch.
        var consensus = NameTiles.Growth([year, next], new DateOnly(2026, 9, 27), quarters)!;

        Assert.Equal(
            (GrowthTile.Consensus, new DateOnly(2026, 12, 31), 30, new DateOnly(2026, 9, 27)),
            (consensus.Basis, consensus.Through, consensus.Analysts!.Value, consensus.FetchedOn!.Value));
        Assert.Equal(20.0, consensus.Percent, 9);

        // Without the current year's trend the tile reads the sales growth, 435 against 370, 65 / 370 = 17.5676%.
        var sales = NameTiles.Growth([next], new DateOnly(2026, 9, 27), quarters)!;

        Assert.Equal((GrowthTile.Sales, new DateOnly(2026, 6, 30)), (sales.Basis, sales.Through));
        Assert.Equal(17.5676, sales.Percent, 4);
        Assert.Null(NameTiles.ConsensusGrowth([year with { EpsYearAgo = null }], new DateOnly(2026, 9, 27)));

        // The yield tile carries the newest 10-year on or before the night: 4.20 on 2026-10-08, not 4.30 the session after.
        var tile = NameTiles.YieldBeside(4.00m, 100m, [(new DateOnly(2026, 10, 7), 4.10), (new DateOnly(2026, 10, 8), 4.20), (new DateOnly(2026, 10, 9), 4.30)], new DateOnly(2026, 10, 8))!;

        Assert.Equal(4.0, tile.Percent, 9);
        Assert.Equal((4.20, new DateOnly(2026, 10, 8)), (tile.TenYear!.Value, tile.TenYearOn!.Value));
        Assert.Null(NameTiles.YieldBeside(4.00m, 100m, [], new DateOnly(2026, 10, 8))!.TenYear);
    }

    [Fact]
    public void TheEstimateTrendKeepsTheNewestEndUnderEachWordAndLeavesOutAPeriodTheAnswerFilesNoEntryFor()
    {
        // An answer's trend keyed by each period's end: the current quarter and fiscal year share their key, as a quarter
        // ending with its fiscal year does, the current year is also filed for the year before, a period word outside the
        // four is filed, and no next quarter is filed at all.
        const string Trend = """
            {
              "2026-09-30": { "date": "2026-09-30", "period": "0q", "earningsEstimateAvg": "2.0000", "earningsEstimateNumberOfAnalysts": "21.0000" },
              "2026-09-30": { "date": "2026-09-30", "period": "0y", "earningsEstimateAvg": "8.0000", "earningsEstimateYearAgoEps": "7.0000", "epsTrend30daysAgo": "7.9000", "epsRevisionsUpLast30days": "5.0000" },
              "2025-09-30": { "date": "2025-09-30", "period": "0y", "earningsEstimateAvg": "7.0000" },
              "2027-09-30": { "date": "2027-09-30", "period": "+1y", "earningsEstimateAvg": 9.1 },
              "2026-06-30": { "date": "2026-06-30", "period": "-1q", "earningsEstimateAvg": "1.9000" }
            }
            """;

        using var document = JsonDocument.Parse(Trend);

        var periods = EstimatePeriod.FromTrend(document.RootElement);

        Assert.Equal(
            ["0q 2026-09-30 2.0000", "0y 2026-09-30 8.0000", "+1y 2027-09-30 9.1"],
            periods.Select(period => FormattableString.Invariant($"{period.Period} {period.PeriodEnd:yyyy-MM-dd} {period.EpsAverage}")));
        Assert.Equal((21, 7.0000m, 7.9000m, 5), (periods[0].EpsAnalysts!.Value, periods[1].EpsYearAgo!.Value, periods[1].Eps30DaysAgo!.Value, periods[1].UpLast30Days!.Value));
        Assert.Null(periods[1].DownLast30Days);

        // A count filed with a fraction is no count, and a trend that is no object files nothing.
        using var fractional = JsonDocument.Parse("""{ "a": { "date": "2026-12-31", "period": "0y", "earningsEstimateNumberOfAnalysts": "21.5000" } }""");
        using var listed = JsonDocument.Parse("[]");

        Assert.Null(EstimatePeriod.FromTrend(fractional.RootElement).Single().EpsAnalysts);
        Assert.Empty(EstimatePeriod.FromTrend(listed.RootElement));

        // The fixture's captures hold two periods each, read by a reader of the test's own off each answer: Apple's next
        // quarter ending 2026-12-31 and next year 2027-09-30.
        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            using var captured = JsonDocument.Parse(Captured($"fundamentals-{ticker}.json"));

            var trend = captured.RootElement.GetProperty("Earnings").GetProperty("Trend");
            var byHand = trend.EnumerateObject()
                .Select(entry => (Period: entry.Value.GetProperty("period").GetString()!, End: entry.Value.GetProperty("date").GetString()!))
                .Where(entry => EstimatePeriod.Periods.Contains(entry.Period))
                .OrderBy(entry => EstimatePeriod.Periods.ToList().IndexOf(entry.Period))
                .Select(entry => entry.Period + " " + entry.End)
                .ToArray();

            Assert.Equal(2, byHand.Length);
            Assert.Equal(byHand, EstimatePeriod.FromTrend(trend).Select(period => FormattableString.Invariant($"{period.Period} {period.PeriodEnd:yyyy-MM-dd}")));

            if (ticker == "AAPL")
            {
                Assert.Equal(["+1q 2026-12-31", "+1y 2027-09-30"], byHand);
            }
        }
    }

    // The Treasury's table as the fixture captured it, read by a reader of the test's own: each session and its 10-year.
    static IReadOnlyList<(DateOnly Session, double TenYear)> TreasuryByHand()
    {
        var lines = Captured("treasury-2026.csv").Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var column = Array.IndexOf(lines[0].Split(',').Select(cell => cell.Trim('"')).ToArray(), "10 Yr");

        return
        [
            .. lines.Skip(1)
                .Select(line => line.Split(','))
                .Select(cells => (DateOnly.ParseExact(cells[0], "MM/dd/yyyy", CultureInfo.InvariantCulture), double.Parse(cells[column], CultureInfo.InvariantCulture)))
                .OrderBy(row => row.Item1),
        ];
    }

    [Fact]
    public void TheTreasurysTableIsReadAsItSendsIt()
    {
        var byHand = TreasuryByHand();
        var read = TreasuryYields.Parse(Captured("treasury-2026.csv"));

        // 195 sessions from 2026-01-02 to 2026-10-09, the 10-year 4.19 on the first, 4.80 on the fixture's night of
        // 2026-09-08 and 5.24 on the newest.
        Assert.Equal(195, read.Count);
        Assert.Equal(byHand, read.Select(row => (row.Session, row.TenYear)));
        Assert.Equal((new DateOnly(2026, 1, 2), 4.19), (read[0].Session, read[0].TenYear));
        Assert.Equal(4.80, read.Single(row => row.Session == new DateOnly(2026, 9, 8)).TenYear);
        Assert.Equal((new DateOnly(2026, 10, 9), 5.24), (read[^1].Session, read[^1].TenYear));

        // An empty answer is a year with no session yet, and a session whose 10-year cell is empty is left out.
        Assert.Empty(TreasuryYields.Parse(string.Empty));
        Assert.Equal([(new DateOnly(2026, 10, 8), 5.22)], TreasuryYields.Parse("Date,\"10 Yr\"\n10/09/2026,\n10/08/2026,5.22\n").Select(row => (row.Session, row.TenYear)));

        // An answer naming no 10-year column, one that is no table, a line whose date is not written month first and a
        // yield that is no figure are refused.
        Assert.Throws<FormatException>(() => TreasuryYields.Parse("Date,\"5 Yr\"\n10/09/2026,5.02\n"));
        Assert.Throws<FormatException>(() => TreasuryYields.Parse("<html>maintenance</html>"));
        Assert.Throws<FormatException>(() => TreasuryYields.Parse("Date,\"10 Yr\"\n2026-10-09,5.24\n"));
        Assert.Throws<FormatException>(() => TreasuryYields.Parse("Date,\"10 Yr\"\n10/09/2026,n/a\n"));
    }

    // A Treasury feed that refuses, as the live feed does an answer it was not sent.
    sealed class RefusingTreasuryFeed : ITreasuryYieldFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<TreasuryYield>> YearAsync(int year, CancellationToken cancellation = default)
        {
            Requests++;

            throw new ProviderRefusal("The Treasury answered 503 Service Unavailable.", transient: false);
        }
    }

    [Fact]
    public async Task TheTreasuryReaderKeepsEachSessionToTheNightOnceAndARefusalKeepsNothing()
    {
        using var store = new TemporaryStore().Migrated();

        var session = new DateOnly(2026, 9, 8);
        var clock = FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var feed = new RecordedTreasuryYieldFeed(new Dictionary<int, string> { [2026] = Captured("treasury-2026.csv") });

        // One request for the session's year keeps each session to the night's, 172 to 2026-09-08 with 4.80 on it, and none
        // after it.
        var sessions = TreasuryByHand().Where(row => row.Session <= session).ToArray();
        var read = await new TreasuryReader(clock, store.DatabaseFile).RunAsync(feed, session, "night-0908");

        Assert.Equal(172, sessions.Length);
        Assert.Equal((sessions.Length, sessions.Length, 1), (read.Published, read.Kept, feed.Requests));
        Assert.Equal(4.80, read.TenYear!.Value);
        Assert.Equal(
            sessions.Select(row => FormattableString.Invariant($"{row.Session:yyyy-MM-dd}|{row.TenYear:0.00}")),
            Query(store, "SELECT session_date, printf('%.2f', ten_year) FROM treasury_yield ORDER BY session_date;"));
        Assert.Equal(["ok|1|172"], Query(store, $"SELECT outcome, network_requests, rows_written FROM run_log WHERE run_id = 'night-0908' AND stage = '{TreasuryReader.Stage}';"));

        // The night after keeps its own session and none twice.
        var next = await new TreasuryReader(clock, store.DatabaseFile).RunAsync(feed, new DateOnly(2026, 9, 9), "night-0909");

        Assert.Equal((1, 4.83), (next.Kept, next.TenYear!.Value));
        Assert.Equal(["173"], Query(store, "SELECT COUNT(*) FROM treasury_yield;"));

        // A refusal keeps nothing, stops nothing and says why on the step's row; a night handed no feed reads none and says so.
        var refused = await new TreasuryReader(clock, store.DatabaseFile).RunAsync(new RefusingTreasuryFeed(), new DateOnly(2026, 9, 10), "night-0910");

        Assert.Equal("The Treasury answered 503 Service Unavailable.", refused.NotRead);
        Assert.Equal(["173"], Query(store, "SELECT COUNT(*) FROM treasury_yield;"));
        Assert.Equal(
            ["partial|1|no 10-year was read: The Treasury answered 503 Service Unavailable."],
            Query(store, $"SELECT outcome, network_requests, detail FROM run_log WHERE run_id = 'night-0910' AND stage = '{TreasuryReader.Stage}';"));

        var none = await new TreasuryReader(clock, store.DatabaseFile).RunAsync(null, new DateOnly(2026, 9, 11), "night-0911");

        Assert.Equal("no 10-year was read: the night was given no Treasury feed", TreasuryReader.Detail(none, new DateOnly(2026, 9, 11)));
        Assert.Equal(["173"], Query(store, "SELECT COUNT(*) FROM treasury_yield;"));
    }

    [Fact]
    public async Task TheDividendKeeperKeepsTheNightsDividendsOnceAndTheFirstStands()
    {
        using var store = new TemporaryStore().Migrated();

        var clock = FixedClock.At(new DateTimeOffset(2026, 8, 19, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        DividendPaid[] paid =
        [
            new("CVX", new DateOnly(2026, 8, 19), 1.78m, 1.78m, new DateOnly(2026, 7, 25), new DateOnly(2026, 8, 19), new DateOnly(2026, 9, 10), "Quarterly", "USD"),
            new("KO", new DateOnly(2026, 8, 19), 0.51m, null, null, null, null, null, null),
        ];

        var kept = await new DividendKeeper(clock, store.DatabaseFile).KeepTheNightAsync(paid, "night-0819");

        Assert.Equal((2, 2), (kept.Handed, kept.Kept));
        Assert.Equal(
            ["CVX|2026-08-19|1.78|1.78|2026-07-25|2026-08-19|2026-09-10|Quarterly|USD|night|night-0819", "KO|2026-08-19|0.51|||||||night|night-0819"],
            Query(store, "SELECT ticker, ex_date, amount, IFNULL(unadjusted, ''), IFNULL(declared_on, ''), IFNULL(record_on, ''), IFNULL(paid_on, ''), IFNULL(period, ''), IFNULL(currency, ''), source, run_id FROM dividend_event ORDER BY ticker;"));

        // The same date again, restated, keeps the first and writes nothing, its row saying so, and it asks nothing.
        var again = await new DividendKeeper(clock, store.DatabaseFile).KeepTheNightAsync([paid[0] with { Amount = 1.80m }], "night-0820");

        Assert.Equal((1, 0), (again.Handed, again.Kept));
        Assert.Equal(["1.78"], Query(store, "SELECT amount FROM dividend_event WHERE ticker = 'CVX';"));
        Assert.Equal(["2|0", "0|0"], Query(store, $"SELECT rows_written, network_requests FROM run_log WHERE stage = '{DividendKeeper.Stage}' ORDER BY run_id;"));
    }

    [Fact]
    public async Task TheDividendsHistoryRunStatesItsAsksKeepsEachMembersAndNamesOneNotServed()
    {
        using var store = new TemporaryStore().Migrated();

        // Two members of the S&P 500 today and one that left in January.
        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES " +
            "('GSPC', 'AAA', '2020-01-01', NULL, '2026-10-01T00:00:00Z'), ('GSPC', 'BBB', '2020-01-01', NULL, '2026-10-01T00:00:00Z'), ('GSPC', 'CCC', '2020-01-01', '2026-01-02', '2026-10-01T00:00:00Z');");

        // AAA's answer reaches back before the date asked from and runs to after today; BBB's provider serves nothing.
        var feed = new RecordedDividendHistoryFeed(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["AAA"] = """
                [
                  { "date": "2015-02-10", "value": 0.20, "unadjustedValue": 0.40 },
                  { "date": "2025-02-10", "value": 0.50, "unadjustedValue": 1.00, "declarationDate": "2025-01-20", "recordDate": "2025-02-11", "paymentDate": "2025-03-01", "period": "Quarterly", "currency": "USD" },
                  { "date": "2026-02-10", "value": "0.55", "unadjustedValue": "0.55" },
                  { "date": "2026-11-10", "value": 0.60 }
                ]
                """,
        });

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 10, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var stated = new List<string>();
        var outcome = await new DividendKeeper(clock, store.DatabaseFile).HistoryAsync(feed, "GSPC", new DateOnly(2016, 1, 1), "dividends-history-test", stated.Add);

        // Stated before the first request: two members today at one weighted call each.
        Assert.Equal(1, ProviderWeights.HistoricalPerTicker);
        Assert.Equal([ProviderStop.Stated("2 member(s) of GSPC today, one request each, from 2016-01-01", 2)], stated);
        Assert.Equal((2, 2, 2, 2), (outcome.Asked, outcome.Requests, outcome.Handed, outcome.Kept));
        Assert.Equal(["BBB: No captured dividends for BBB."], outcome.NotServed);
        Assert.Equal(
            ["AAA|2025-02-10|0.50|1.00|2025-01-20|history", "AAA|2026-02-10|0.55|0.55||history"],
            Query(store, "SELECT ticker, ex_date, amount, unadjusted, IFNULL(declared_on, ''), source FROM dividend_event ORDER BY ex_date;"));
        Assert.Equal(["partial|2|2"], Query(store, $"SELECT outcome, rows_written, network_requests FROM run_log WHERE stage = '{DividendKeeper.HistoryStage}';"));

        // Asked again, every dividend it answers is held already and none is written twice.
        var again = await new DividendKeeper(clock, store.DatabaseFile).HistoryAsync(feed, "GSPC", new DateOnly(2016, 1, 1), "dividends-history-again", only: ["AAA"]);

        Assert.Equal((1, 2, 0), (again.Asked, again.Handed, again.Kept));
    }

    [Fact]
    public async Task TheDividendsHistoryRunPastTheStopIsRefusedBeforeItAsks()
    {
        using var store = new TemporaryStore().Migrated();

        // One member more than the stop allows at one weighted call each.
        store.Execute(
            "WITH RECURSIVE n(i) AS (SELECT 1 UNION ALL SELECT i + 1 FROM n WHERE i < " + (ProviderStop.WeightedCalls + 1).ToString(CultureInfo.InvariantCulture) + ") " +
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) SELECT 'GSPC', 'T' || i, '2020-01-01', NULL, '2026-10-01T00:00:00Z' FROM n;");

        var feed = new RecordedDividendHistoryFeed(new Dictionary<string, string>());
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 10, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var stated = new List<string>();

        var refused = await Assert.ThrowsAsync<ArgumentException>(() => new DividendKeeper(clock, store.DatabaseFile).HistoryAsync(feed, "GSPC", new DateOnly(2016, 1, 1), "dividends-history-past", stated.Add));

        Assert.Equal(25_000, ProviderStop.WeightedCalls);
        Assert.Contains("25,001 weighted calls", refused.Message, StringComparison.Ordinal);
        Assert.Contains("Nothing was asked", refused.Message, StringComparison.Ordinal);
        Assert.Single(stated);
        Assert.Equal(0, feed.Requests);
        Assert.Empty(Query(store, "SELECT ticker FROM dividend_event;"));
        Assert.Empty(Query(store, "SELECT run_id FROM run_log;"));
    }

    [Fact]
    public void TheDividendsAnswerIsReadAsTheProviderSendsIt()
    {
        // Figures sent as numbers or as text, each date read as filed and a field not sent read as none, oldest first.
        var read = DividendAnswers.Parse("""
            [
              { "date": "2026-02-17", "value": "1.78", "unadjustedValue": 1.78, "declarationDate": "2026-01-30", "recordDate": "2026-02-17", "paymentDate": "2026-03-10", "period": "Quarterly", "currency": "USD" },
              { "date": "2025-11-18", "value": 1.71 }
            ]
            """, "CVX");

        Assert.Equal(
            [
                new DividendPaid("CVX", new DateOnly(2025, 11, 18), 1.71m, null, null, null, null, null, null),
                new DividendPaid("CVX", new DateOnly(2026, 2, 17), 1.78m, 1.78m, new DateOnly(2026, 1, 30), new DateOnly(2026, 2, 17), new DateOnly(2026, 3, 10), "Quarterly", "USD"),
            ],
            read);

        // An answer that is no array, and a dividend with no date or no amount, cannot be read.
        Assert.Throws<FormatException>(() => DividendAnswers.Parse("""{ "error": "not found" }""", "CVX"));
        Assert.Throws<FormatException>(() => DividendAnswers.Parse("""[ { "value": 1.78 } ]""", "CVX"));
        Assert.Throws<FormatException>(() => DividendAnswers.Parse("""[ { "date": "2026-02-17" } ]""", "CVX"));
    }

    [Fact]
    public async Task TheReleaseBeforeTheNewestIsHandedToWhatManagementSaidAloneAndNoneIsReadWhereTheArchiveListsNone()
    {
        // Keysight's archive as the fixture captured it: its newest results announcement of 2026-08-18 and the one before
        // it of 2026-05-19, whose release is the second quarter's, giving the third quarter's outlook.
        var archive = new RecordedFilingsArchiveFeed(Folder());
        var filings = await archive.FilingsAsync("KEYS", KeysightCik);
        var previous = (await archive.PreviousReleaseAsync("KEYS", KeysightCik, filings.Results))!;

        Assert.Equal(("exhibit991-q226pressrelease.htm", new DateOnly(2026, 5, 19)), (previous.Document, previous.FiledOn));
        Assert.Contains("$1.730 billion to $1.750 billion", previous.Text, StringComparison.Ordinal);
        Assert.Equal(2, archive.Requests);

        // An archive listing one announcement asks for nothing and reads none.
        var asked = 0;
        IndexedFiling[] newestOnly = [.. filings.Results.Where(SecEdgarArchive.Announces).OrderByDescending(filing => filing.FilingDate).Take(1)];

        Assert.Single(newestOnly);
        Assert.Null(await SecEdgarArchive.PreviousReleaseAsync((_, _) => { asked++; return Task.FromResult<string?>(null); }, KeysightCik, newestOnly));
        Assert.Equal(0, asked);

        // What management said is handed the newest release and the one before it, and no other section is handed the one
        // before; a pass with none hands it the newest alone, and its ask compares with the release before only where one
        // is listed.
        StoredDocument Document(string id, string title, DateOnly published) =>
            new(id, "https://www.sec.gov/Archives/edgar/data/1601046/" + id + ".htm", title, published, ReportFetchedAt, "text", Admissibility.Accepted);

        var own = new EvidenceDocument(Document("own", "Results release, q3.htm", new DateOnly(2026, 8, 18)), 1);
        var before = new EvidenceDocument(Document("before", "Previous results release, q2.htm", new DateOnly(2026, 5, 19)), 1);
        var news = new EvidenceDocument(Document("news", "Keysight raises its outlook", new DateOnly(2026, 8, 20)), 1, NamesTheCompany: true);

        var handed = Evidence.ForSections([], [own, before, news], "own", "before");

        Assert.Equal(["own", "before"], handed[Evidence.Management].Select(document => document.Id));
        Assert.All(handed.Where(section => section.Key != Evidence.Management), section => Assert.DoesNotContain(section.Value, document => document.Id == "before"));
        Assert.Equal(["own"], handed[Evidence.Sells].Select(document => document.Id));

        Assert.Equal(["own"], Evidence.ForSections([], [own, news], "own")[Evidence.Management].Select(document => document.Id));
        Assert.Contains("where one is listed, the release before it", SectionPrompt.Asks[Evidence.Management], StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheReplayKeepsTheQuartersLinesTheTrendTheNightsDividendsAndTheTreasurysYieldsAsTheCapturesFileThem()
    {
        var expected = Expected("report-parts");

        using var store = await FixtureReplay.ReplayedAsync();

        static decimal? Stored(string text) => text == "null" ? null : decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
        static decimal? Stated(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : decimal.Parse(value.GetString()!, NumberStyles.Float, CultureInfo.InvariantCulture);
        static string Count(JsonElement value) => value.ValueKind == JsonValueKind.Null ? "null" : value.GetInt32().ToString(CultureInfo.InvariantCulture);

        string[] figures = ["eps", "low", "high", "yearAgo", "revenue", "revenueLow", "revenueHigh", "revenueYearAgo", "now", "days7", "days30", "days60", "days90"];
        string[] counts = ["analysts", "revenueAnalysts", "up7", "up30", "down7", "down30"];

        foreach (var name in expected.GetProperty("names").EnumerateObject())
        {
            var ticker = name.Name;
            var stated = name.Value;

            // The newest quarter's gross profit and cash flow lines, read with them, a line filed as null kept as none.
            var lines = Query(store, $"SELECT IFNULL(gross_profit, 'null'), IFNULL(capital_spending, 'null'), IFNULL(free_cash_flow, 'null'), IFNULL(dividends_paid, 'null'), lines_read FROM reported_quarter WHERE ticker = '{ticker}' AND period_end = '{stated.GetProperty("newest").GetString()}';").Single().Split('|');
            var line = stated.GetProperty("lines");

            Assert.Equal(
                [Stated(line.GetProperty("grossProfit")), Stated(line.GetProperty("capitalSpending")), Stated(line.GetProperty("freeCashFlow")), Stated(line.GetProperty("dividendsPaid"))],
                lines[..4].Select(Stored));
            Assert.Equal("1", lines[4]);
            Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM reported_quarter WHERE ticker = '{ticker}' AND lines_read = 0;"));

            // The analysts' mean and target on the company row.
            Assert.Equal(
                [FormattableString.Invariant($"{stated.GetProperty("ratingMean").GetDouble():0.0000}|{stated.GetProperty("target").GetString()}")],
                Query(store, $"SELECT printf('%.4f', rating_mean), target_price FROM company WHERE ticker = '{ticker}';"));

            // Each period of the trend the answer files, a row a period, each figure and count as filed.
            var trend = Query(
                store,
                "SELECT period, period_end, IFNULL(eps_average, 'null'), IFNULL(eps_low, 'null'), IFNULL(eps_high, 'null'), IFNULL(eps_year_ago, 'null'), " +
                "IFNULL(revenue_average, 'null'), IFNULL(revenue_low, 'null'), IFNULL(revenue_high, 'null'), IFNULL(revenue_year_ago, 'null'), " +
                "IFNULL(eps_now, 'null'), IFNULL(eps_seven_days_ago, 'null'), IFNULL(eps_thirty_days_ago, 'null'), IFNULL(eps_sixty_days_ago, 'null'), IFNULL(eps_ninety_days_ago, 'null'), " +
                "IFNULL(eps_analysts, 'null'), IFNULL(revenue_analysts, 'null'), IFNULL(up_last_seven_days, 'null'), IFNULL(up_last_thirty_days, 'null'), IFNULL(down_last_seven_days, 'null'), IFNULL(down_last_thirty_days, 'null') " +
                $"FROM estimate_trend WHERE ticker = '{ticker}' ORDER BY period_end;");
            var periods = stated.GetProperty("trend").EnumerateArray().ToArray();

            Assert.Equal(periods.Length, trend.Count);

            foreach (var (row, period) in trend.Select(row => row.Split('|')).Zip(periods))
            {
                Assert.Equal((period.GetProperty("period").GetString(), period.GetProperty("end").GetString()), (row[0], row[1]));
                Assert.Equal(figures.Select(figure => Stated(period.GetProperty(figure))), row[2..15].Select(Stored));
                Assert.Equal(counts.Select(count => Count(period.GetProperty(count))), row[15..]);
            }
        }

        // The night's dividends for the names it stores.
        var dividends = Query(store, "SELECT ticker, ex_date, amount, IFNULL(unadjusted, 'null'), IFNULL(declared_on, 'null'), IFNULL(record_on, 'null'), IFNULL(paid_on, 'null'), IFNULL(period, 'null'), IFNULL(currency, 'null'), source FROM dividend_event ORDER BY ticker, ex_date;")
            .Select(row => row.Split('|'))
            .ToArray();
        var statedDividends = expected.GetProperty("dividends").EnumerateArray().ToArray();

        Assert.Equal(statedDividends.Length, dividends.Length);

        foreach (var (row, dividend) in dividends.Zip(statedDividends))
        {
            Assert.Equal((dividend.GetProperty("ticker").GetString(), dividend.GetProperty("exDate").GetString()), (row[0], row[1]));
            Assert.Equal((Stated(dividend.GetProperty("amount")), Stated(dividend.GetProperty("unadjusted"))), (Stored(row[2]), Stored(row[3])));
            Assert.Equal(
                new[] { "declared", "record", "paid", "period", "currency", "source" }.Select(field => dividend.GetProperty(field).GetString() ?? "null"),
                row[4..]);
        }

        // The Treasury's 10-year, every session to the night's and none after it.
        var treasury = expected.GetProperty("treasury");
        var first = treasury.GetProperty("first");
        var onTheNight = treasury.GetProperty("night");

        Assert.Equal(
            [FormattableString.Invariant($"{treasury.GetProperty("sessions").GetInt32()}|{first[0].GetString()}|{onTheNight[0].GetString()}")],
            Query(store, "SELECT COUNT(*), MIN(session_date), MAX(session_date) FROM treasury_yield;"));
        Assert.Equal(
            [first[1].GetString()!, onTheNight[1].GetString()!],
            Query(store, $"SELECT printf('%.2f', ten_year) FROM treasury_yield WHERE session_date IN ('{first[0].GetString()}', '{onTheNight[0].GetString()}') ORDER BY session_date;"));
    }
}
