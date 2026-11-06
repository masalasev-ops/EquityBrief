using EquityBrief.Core.Ledger;
using EquityBrief.Core.Report;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// A quarter's lines drawn from its filing, then its results release's own tables, and otherwise not on file until the
// filing, the provider's income and cash figures drawn for no quarter. AIG's rows are copied from the store, where its page
// drew the provider's revenue of $1.1B and net income of -$1.7B for the quarter its stored 10-Q states at $7,443M and $849M.
// see: A quarter's lines are drawn from its filing where stored and then from its results release's own tables, and are not on file until the filing otherwise
public partial class ReadSurface
{
    static QuarterRow AigFetched(int year, int month, int day, decimal revenue, decimal operating, decimal net, DateOnly reported, decimal? cash = null) =>
        new(new DateOnly(year, month, day), reported, revenue, revenue, operating, net, cash, cash, null, null, null, null, null, true);

    // The provider's newest fetch of AIG on the night, its quarter to September as the provider stored it.
    static IReadOnlyList<QuarterRow> AigQuarters() =>
    [
        AigFetched(2025, 9, 30, 6_402_000_000m, 714_000_000m, 519_000_000m, new DateOnly(2025, 11, 4), 1_343_000_000m),
        AigFetched(2025, 12, 31, 6_557_000_000m, 661_000_000m, 735_000_000m, new DateOnly(2026, 2, 10)),
        AigFetched(2026, 3, 31, 6_650_000_000m, 987_000_000m, 763_000_000m, new DateOnly(2026, 4, 30)),
        AigFetched(2026, 6, 30, 7_085_000_000m, 1_364_000_000m, 948_000_000m, new DateOnly(2026, 8, 6)),
        AigFetched(2026, 9, 30, 1_100_000_000m, -2_351_000_000m, -1_711_000_000m, new DateOnly(2026, 11, 3), 2_691_000_000m) with { EpsActual = 2.06m, EpsEstimate = 1.85m },
    ];

    static FiledFactRow AigFiled(string concept, string start, string end, decimal value, string filed) =>
        new(concept, DateOnly.Parse(start, System.Globalization.CultureInfo.InvariantCulture), DateOnly.Parse(end, System.Globalization.CultureInfo.InvariantCulture), value, DateOnly.Parse(filed, System.Globalization.CultureInfo.InvariantCulture), "10-Q", "0000005272-" + filed);

    // AIG's filed facts for the two quarters to September, its 2026 six and nine months' cash among them.
    static IReadOnlyList<FiledFactRow> AigFacts(bool newest = true) =>
    [
        AigFiled("Revenues", "2025-07-01", "2025-09-30", 6_351_000_000m, "2025-11-05"),
        AigFiled("NetIncomeLoss", "2025-07-01", "2025-09-30", 519_000_000m, "2025-11-05"),
        AigFiled("ProfitLoss", "2025-07-01", "2025-09-30", 524_000_000m, "2025-11-05"),
        .. newest
            ? new[]
            {
                AigFiled("Revenues", "2026-07-01", "2026-09-30", 7_443_000_000m, "2026-11-04"),
                AigFiled("ProfitLoss", "2026-07-01", "2026-09-30", 849_000_000m, "2026-11-04"),
                AigFiled("NetCashProvidedByUsedInOperatingActivities", "2026-01-01", "2026-06-30", 1_871_000_000m, "2026-08-07"),
                AigFiled("NetCashProvidedByUsedInOperatingActivities", "2026-01-01", "2026-09-30", 4_562_000_000m, "2026-11-04"),
            }
            : [],
    ];

    // AIG's results release for the quarter, its financial summary as the archive's reader flattened it: the year before's
    // column first.
    const string AigRelease = "1 FINANCIAL SUMMARY Three Months Ended September 30, ($ and shares in millions, except per share amounts) 2025 2026 Net income attributable to AIG common shareholders $ 519 $ 844 Net income per diluted share attributable to AIG common shareholders $ 0.93 $ 1.60 Net investment income $ 772 $ 1,100 3 GENERAL INSURANCE Three Months Ended September 30, ($ in millions) 2025 2026 Change Gross premiums written $ 8,686 $ 9,174 6 % Net premiums written $ 6,230 $ 6,651 7 %";

    // A constructed store's quarters given the SEC's facts at the figures its newest fetch states, a quarter a three months'
    // span filed on its report day, under a filer the stock's company row names, so a page reading each quarter's lines
    // from its filing reads the figures the test wrote. A stock no company row names a filer for is given one.
    internal static void FiledAsFetched(Action<string> execute, string ticker)
    {
        var cik = string.Concat(ticker.Select(letter => ((int)letter % 100).ToString("00", System.Globalization.CultureInfo.InvariantCulture))).PadLeft(10, '0')[..10];

        execute($"INSERT INTO company (ticker, fetched_at, cik) SELECT '{ticker}', '2000-01-01T00:00:00.000Z', '{cik}' WHERE NOT EXISTS (SELECT 1 FROM company WHERE ticker = '{ticker}' AND cik IS NOT NULL);");

        foreach (var (concept, column) in new[] { ("Revenues", "revenue"), ("GrossProfit", "gross_profit"), ("OperatingIncomeLoss", "operating_income"), ("NetIncomeLoss", "net_income"), ("NetCashProvidedByUsedInOperatingActivities", "operating_cash_flow") })
        {
            execute(
                $"INSERT OR IGNORE INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) "
                + $"SELECT (SELECT c.cik FROM company c WHERE c.ticker = q.ticker AND c.cik IS NOT NULL ORDER BY c.fetched_at DESC LIMIT 1), '{concept}', date(q.period_end, '-3 months', '+1 day'), q.period_end, q.{column}, COALESCE(q.report_date, q.period_end), '10-Q', 'test-' || q.period_end, 'test' "
                + $"FROM reported_quarter q WHERE q.ticker = '{ticker}' AND q.{column} IS NOT NULL AND q.fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = '{ticker}');");
        }
    }

    static string QuarterOf(IReadOnlyList<QuarterRow> quarters, IReadOnlyList<FiledFactRow> facts, string? release = null, DateOnly? releasedOn = null)
    {
        var sourced = QuarterSources.Of(quarters, facts, release, releasedOn);
        var latest = QuarterLines.Latest(sourced.Quarters, null, "Financials", sourced)!;

        return new MarkRenderer().QuarterTable("AIG", new ReportView(latest, [], new DateOnly(2026, 11, 6), "USD", [], null, null, null, null));
    }

    [Fact]
    public void AigsQuarterDrawsItsFilingsFiguresWhereThePageOf20261105DrewTheProviders()
    {
        // The page as it read: the provider's quarter, unsourced.
        var before = new MarkRenderer().QuarterTable("AIG", new ReportView(QuarterLines.Latest(AigQuarters(), null, "Financials"), [], new DateOnly(2026, 11, 6), "USD", [], null, null, null, null));

        Assert.Contains("data-reported=\"1100000000\"", before, StringComparison.Ordinal);

        var sourced = QuarterSources.Of(AigQuarters(), AigFacts());
        var drawn = QuarterOf(AigQuarters(), AigFacts());

        Assert.Equal(QuarterSource.Filing, sourced.SourceOf(new DateOnly(2026, 9, 30)));
        Assert.Equal(new DateOnly(2026, 11, 4), sourced.FiledOn[new DateOnly(2026, 9, 30)]);
        Assert.Contains("data-reported=\"7443000000\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-reported=\"849000000\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("1100000000", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("1711000000", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("2351000000", drawn, StringComparison.Ordinal);

        // The year before is the filing's too, so the change on a year is 7,443 over 6,351, +17.2%, and net income 849 over
        // 519; cash from operations is the nine months less the six, 4,562 less 1,871.
        Assert.Contains("data-year-earlier=\"6351000000\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-on-the-year=\"17.19", drawn, StringComparison.Ordinal);
        Assert.Contains("data-reported=\"2691000000\"", drawn, StringComparison.Ordinal);

        // A line the filing does not state says so, never the provider's, and free cash flow says why it is not read.
        Assert.Contains(QuarterSources.NotInFiling, drawn, StringComparison.Ordinal);
        Assert.Contains(QuarterSources.NoCapitalSpending, drawn, StringComparison.Ordinal);

        // Earnings a share stay the calendar's, against its estimate.
        Assert.Contains("data-reported=\"2.06\"", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void AQuarterWhoseFilingIsNotStoredDrawsItsReleasesOwnFiguresMarkedAndTheRestNotOnFile()
    {
        var drawn = QuarterOf(AigQuarters(), AigFacts(newest: false), AigRelease, new DateOnly(2026, 11, 3));

        Assert.Contains("data-reported=\"844000000\"", drawn, StringComparison.Ordinal);
        Assert.Contains("data-from=\"release\">from the results release</small>", drawn, StringComparison.Ordinal);
        Assert.Contains(QuarterSources.NotOnFile, drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("1100000000", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("1711000000", drawn, StringComparison.Ordinal);

        // A release older than the quarter's end, or more than 120 days after it, is no report of it.
        Assert.DoesNotContain("844000000", QuarterOf(AigQuarters(), AigFacts(newest: false), AigRelease, new DateOnly(2026, 9, 29)), StringComparison.Ordinal);
        Assert.DoesNotContain("844000000", QuarterOf(AigQuarters(), AigFacts(newest: false), AigRelease, new DateOnly(2027, 1, 29)), StringComparison.Ordinal);

        // Neither a filing nor a release: every income and cash line is not on file, and the provider's none.
        var neither = QuarterOf(AigQuarters(), AigFacts(newest: false));

        Assert.Equal(6, System.Text.RegularExpressions.Regex.Matches(neither, "data-reported=\"absent\">" + QuarterSources.NotOnFile).Count);
        Assert.Contains("data-reported=\"2.06\"", neither, StringComparison.Ordinal);
    }

    [Fact]
    public void AReleaseRowIsReadOnlyWhereItsYearEarlierColumnIsTheFiledQuarterAndItsShapeIsAPairOrATrend()
    {
        var aig = new YearEarlierFiled(null, null, null, 519_000_000m);

        // Columns ordered by the heading's years, the later second.
        Assert.Equal(844_000_000m, ReleaseFigures.Read(AigRelease, new DateOnly(2026, 9, 30), aig)!.NetIncome);

        // No year-earlier quarter filed: nothing is read.
        Assert.Null(ReleaseFigures.Read(AigRelease, new DateOnly(2026, 9, 30), new YearEarlierFiled(null, null, null, null)));

        // A year-earlier column off the filed quarter by more than a per cent: not read.
        Assert.Null(ReleaseFigures.Read(AigRelease, new DateOnly(2026, 9, 30), new YearEarlierFiled(null, null, null, 530_000_000m)));

        // A change in per cent closing the row is no figure: "$ 8,686 $ 9,174 6 %" is a pair. A row opening straight after
        // the heading's words is not a row the reader starts on.
        const string Changes = "($ in millions) 2025 2026 Change Gross premiums written $ 8,500 $ 9,000 6 % Total revenues $ 8,686 $ 9,174 6 %";

        Assert.Equal(9_174_000_000m, ReleaseFigures.Read(Changes, new DateOnly(2026, 9, 30), new YearEarlierFiled(8_686_000_000m, null, null, null))!.Revenue);
        Assert.Null(ReleaseFigures.Read("($ in millions) 2025 2026 Change Total revenues $ 8,686 $ 9,174 6 %", new DateOnly(2026, 9, 30), new YearEarlierFiled(8_686_000_000m, null, null, null)));

        // A trend of three quarters and two years to date, the year-earlier quarter first: not read, where its second
        // column, the quarter before, sat beside the filed year-earlier figure.
        Assert.Null(ReleaseFigures.Read("(in millions) Other revenues 6 4 3 16 11 Total Revenues 9,823 11,153 4,739 22,185 20,951 Expenses", new DateOnly(2026, 9, 30), new YearEarlierFiled(9_823_000_000m, null, null, null)));

        // Five quarters newest first, the fifth the filed year-earlier quarter: the first is read.
        Assert.Equal(407_000_000m, ReleaseFigures.Read("(Dollars in millions) Total noninterest income (276) 211 195 212 215 Total revenue 407 887 862 888 889 Noninterest expense", new DateOnly(2026, 9, 30), new YearEarlierFiled(889_000_000m, null, null, null))!.Revenue);

        // A segment's row naming the line's words inside a longer name is not read; the company's own row is.
        const string Packaging = "(dollars in millions) Three Months Ended September 30, 2026 2025 Packaging Segment operating income $ 397.8 $ 327.5 Paper 29.0 35.6 Corporate and Other (41.5 ) (38.6 ) Income from operations 385.3 324.5 927.7 938.5 Non-operating pension income";

        Assert.Equal(385_300_000m, ReleaseFigures.Read(Packaging, new DateOnly(2026, 9, 30), new YearEarlierFiled(null, null, 324_500_000m, null))!.OperatingIncome);

        // No unit note before the row: not read.
        Assert.Null(ReleaseFigures.Read("2025 2026 Total revenues $ 8,686 $ 9,174", new DateOnly(2026, 9, 30), new YearEarlierFiled(8_686_000_000m, null, null, null)));

        // The noncontrolling interests' share is never the company's net income.
        Assert.Null(ReleaseFigures.Read("(in millions) 2026 2025 Net income attributable to noncontrolling interests 1,034 923", new DateOnly(2026, 9, 30), new YearEarlierFiled(null, null, null, 923_000_000m)));
    }

    // HBAN's revenue concept states its fees alone, $484M beside net income above it, and UCB's a negative figure: each is a
    // part of the company's revenue and drawn for neither the quarter nor the year before, where the provider's gross figure
    // stood; a bank stating its revenue net of interest expense draws it.
    [Fact]
    public void AFiledRevenueBelowTheQuartersNetIncomeOrAtNoughtIsAPartOfTheCompanyAndIsNotDrawn()
    {
        QuarterRow Bank(int year, decimal revenue) => new(new DateOnly(year, 9, 30), new DateOnly(year, 10, 22), revenue, null, null, 600_000_000m, null, null, null, null, null, null, null, true);
        FiledFactRow Filed(int year, string concept, decimal value) => new(concept, new DateOnly(year, 7, 1), new DateOnly(year, 9, 30), value, new DateOnly(year, 10, 27), "10-Q", "h" + year);

        IReadOnlyList<QuarterRow> quarters = [Bank(2025, 3_900_000_000m), Bank(2026, 4_111_000_000m)];
        var parted = QuarterSources.Of(quarters, [Filed(2025, "Revenues", 470_000_000m), Filed(2025, "NetIncomeLoss", 590_000_000m), Filed(2026, "Revenues", 484_000_000m), Filed(2026, "NetIncomeLoss", 629_000_000m)]);
        var latest = QuarterLines.Latest(parted.Quarters, null, "Financials", parted)!;
        var revenue = latest.Lines.Single(line => line.Line == QuarterLines.Revenue);

        Assert.Null(revenue.Reported);
        Assert.Equal(QuarterSources.PartOfTheCompany, revenue.NotRead);
        Assert.Equal(QuarterSources.PartOfTheCompany, revenue.YearEarlierAbsent);
        Assert.Equal(629_000_000m, latest.Lines.Single(line => line.Line == QuarterLines.NetIncome).Reported);

        // At or below nought, whatever the net income.
        Assert.Contains(new DateOnly(2026, 9, 30), QuarterSources.Of(quarters, [Filed(2026, "Revenues", -11_966_000m)]).PartRevenue);

        // A revenue at the net income or above it is the company's, and stands.
        var whole = QuarterSources.Of(quarters, [Filed(2026, "RevenuesNetOfInterestExpense", 1_990_000_000m), Filed(2026, "NetIncomeLoss", 629_000_000m)]);

        Assert.Equal(1_990_000_000m, whole.Quarters.Single(quarter => quarter.PeriodEnd.Year == 2026).Revenue);
        Assert.Empty(whole.PartRevenue);
        Assert.Equal(629_000_000m, QuarterSources.Of(quarters, [Filed(2026, "Revenues", 629_000_000m), Filed(2026, "NetIncomeLoss", 629_000_000m)]).Quarters[1].Revenue);
    }

    [Fact]
    public void AFiscalQuarterEndingWeeksFromItsLabelReadsItsFilingAndAFiledQuarterNearerAnotherLabelIsNotDrawnTwice()
    {
        // PEP's quarter ending on the fifth of the month, which the provider labels with the month's last day.
        QuarterRow Pep(int month, int day, decimal revenue) => new(new DateOnly(2026, month, day), new DateOnly(2026, 10, 8), revenue, null, null, null, null, null, null, null, null, null, null, true);

        var sourced = QuarterSources.Of(
            [Pep(6, 30, 23_000_000_000m), Pep(9, 30, 1m)],
            [
                new("Revenues", new DateOnly(2026, 3, 22), new DateOnly(2026, 6, 13), 22_726_000_000m, new DateOnly(2026, 7, 9), "10-Q", "a"),
                new("Revenues", new DateOnly(2026, 6, 14), new DateOnly(2026, 9, 5), 25_274_000_000m, new DateOnly(2026, 10, 8), "10-Q", "b"),
            ]);

        Assert.Equal(25_274_000_000m, sourced.Quarters.Single(quarter => quarter.PeriodEnd == new DateOnly(2026, 9, 30)).Revenue);
        Assert.Equal(22_726_000_000m, sourced.Quarters.Single(quarter => quarter.PeriodEnd == new DateOnly(2026, 6, 30)).Revenue);

        // A label with no filed quarter of its own does not borrow the one beside it.
        var gap = QuarterSources.Of([Pep(6, 30, 23_000_000_000m), Pep(9, 30, 1m)], [new("Revenues", new DateOnly(2026, 3, 22), new DateOnly(2026, 6, 13), 22_726_000_000m, new DateOnly(2026, 7, 9), "10-Q", "a")]);

        Assert.Equal(QuarterSource.NotOnFile, gap.SourceOf(new DateOnly(2026, 9, 30)));
    }
}
