using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 15.2: the readings the S&P 400's and 600's rules and their sweeps read, each worked by hand as it
// stood on a constructed session, the trade's cost in every band of the published table and at double, and the
// companies pull storing each quarter's income as filed.
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
public partial class FixtureExpectations
{
    // The rows the readings add that this check reaches: section 17's three and section 18's.
    internal static readonly string[] ReadingsClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Liquidity floors of the 400's and 600's rules"),
        CheckReach.Key(Scope.LimitsTable, "Trade cost of the 400's and 600's rules"),
        CheckReach.Key(Scope.LimitsTable, "Profit gate and coverage of the 400's and 600's rules"),
        CheckReach.Key(Scope.FailureTable, "A company filing no income statement with its date"),
    ];

    // Collver (2014), Table 4, the mean relative effective spread in per cent of 2013's trades, read off the paper's own
    // page and written here apart from the code's table: a row a price band, $2 to 6, $6 to 10, $10 to 20, $20 to 40 and
    // $40 and over, and a column a value band, $250 to 500 million, $500 million to $1 billion, $1 to 2 billion and $2 to
    // 5 billion.
    static readonly decimal[][] PublishedSpread =
    [
        [0.301m, 0.244m, 0.235m, 0.221m],
        [0.237m, 0.144m, 0.127m, 0.121m],
        [0.267m, 0.139m, 0.088m, 0.074m],
        [0.344m, 0.170m, 0.089m, 0.056m],
        [0.514m, 0.251m, 0.129m, 0.072m],
    ];

    // A value inside each value band and a price inside each price band.
    static readonly decimal[] ValueIn = [300_000_000m, 750_000_000m, 1_500_000_000m, 3_000_000_000m];
    static readonly decimal[] PriceIn = [4m, 8m, 15m, 30m, 60m];

    [Fact]
    public void ATradesCostIsHalfThePublishedSpreadAtEachEndInEveryBandAndAtDouble()
    {
        // In every band, a buy and a sale at one price pay the band's spread once in per cent of the buy, and twice that
        // at double.
        for (var price = 0; price < PriceIn.Length; price++)
        {
            for (var value = 0; value < ValueIn.Length; value++)
            {
                var spread = PublishedSpread[price][value];

                Assert.Equal((double)spread, TradeCost.InPercent(ValueIn[value], PriceIn[price], PriceIn[price]), 9);
                Assert.Equal((double)(spread * 2m), TradeCost.InPercent(ValueIn[value], PriceIn[price], PriceIn[price], TradeCost.Doubled), 9);
            }
        }

        // Worked by hand across two price bands: a $1.5 billion company bought at $9 and sold at $12 pays half 0.127 per
        // cent of 9 and half 0.088 of 12, 0.005715 and 0.00528 dollars, 0.010995 a share, which against a stop at $8 is
        // 0.010995 of its one-dollar risk.
        Assert.Equal(0.010995m, TradeCost.RoundTrip(1_500_000_000m, 9m, 12m));
        Assert.Equal(0.010995, TradeCost.InRisk(1_500_000_000m, 9m, 8m, 12m), 12);
        Assert.Equal(0.02199, TradeCost.InRisk(1_500_000_000m, 9m, 8m, 12m, TradeCost.Doubled), 12);

        // Each band closed below and open above: $500 million is the second band, $499,999,999 the first; $10 is the
        // third price band and $9.99 the second.
        Assert.Equal((1, 0), (TradeCost.ValueBand(500_000_000m), TradeCost.ValueBand(499_999_999m)));
        Assert.Equal((2, 1), (TradeCost.PriceBand(10m), TradeCost.PriceBand(9.99m)));

        // A company above $5 billion is read in the last band and one below $250 million in the first, a price under $2
        // in the first price band, and a company with no count in the $1 to 2 billion band.
        Assert.Equal((3, 0, 0), (TradeCost.ValueBand(400_000_000_000m), TradeCost.ValueBand(100_000_000m), TradeCost.PriceBand(1.5m)));
        Assert.Equal(TradeCost.NoCountBand, TradeCost.ValueBand(null));
        Assert.Equal((double)PublishedSpread[2][2], TradeCost.InPercent(null, 15m, 15m), 9);

        // A stop at or above the buy has no risk to count the cost in.
        Assert.True(double.IsNaN(TradeCost.InRisk(1_500_000_000m, 9m, 9m, 12m)));
    }

    [Fact]
    public void TheFloorsAreTheCloseAndTheMeanDollarVolumeOverFiftySessionsAtEachIndexsFigure()
    {
        // Fifty sessions at $20 on 600,000 shares hold a mean of $12 million; forty-nine hold none.
        var fifty = Enumerable.Repeat((20m, 600_000L), 50).ToArray();

        Assert.Equal(12_000_000m, MemberReadings.DollarVolume(fifty));
        Assert.Null(MemberReadings.DollarVolume(fifty[1..]));

        // Over more than fifty the oldest are not read: a first session at $1,000 a share moves nothing.
        Assert.Equal(12_000_000m, MemberReadings.DollarVolume([(1_000m, 600_000L), .. fifty]));

        // And the fiftieth back is read: at $70 on 600,000 shares, $42 million, it lifts the mean to 42 and 49 times 12
        // million, 630 million, over fifty, $12.6 million.
        Assert.Equal(12_600_000m, MemberReadings.DollarVolume([(1_000m, 600_000L), (70m, 600_000L), .. fifty[1..]]));

        // The 400's floor at $10 million and the 600's at $5 million, each at its figure and a dollar under it, and the
        // close at $5 and a cent under it; the S&P 500's rules carry no floor.
        Assert.True(MemberReadings.ClearsTheFloors("MID", 5m, 10_000_000m));
        Assert.False(MemberReadings.ClearsTheFloors("MID", 5m, 9_999_999m));
        Assert.False(MemberReadings.ClearsTheFloors("MID", 4.99m, 10_000_000m));
        Assert.True(MemberReadings.ClearsTheFloors("SML", 5m, 5_000_000m));
        Assert.False(MemberReadings.ClearsTheFloors("SML", 5m, 4_999_999m));
        Assert.False(MemberReadings.ClearsTheFloors("SML", 30m, null));
        Assert.True(MemberReadings.ClearsTheFloors("GSPC", 1m, null));
    }

    // Four quarters of one company: the year to 2025-12-31, each filed about six weeks after it closed.
    static readonly FiledIncome[] FourQuarters =
    [
        new(PullDate(2025, 3, 31), PullDate(2025, 5, 8), 10m, 30m, 5m),
        new(PullDate(2025, 6, 30), PullDate(2025, 8, 7), -40m, 30m, 5m),
        new(PullDate(2025, 9, 30), PullDate(2025, 11, 6), 20m, 30m, 5m),
        new(PullDate(2025, 12, 31), PullDate(2026, 2, 12), 15m, 30m, 5m),
    ];

    [Fact]
    public void TheProfitGateAndTheCoverageAreReadFromTheQuartersFiledBeforeTheSession()
    {
        // On 2026-02-13 the four are filed and sum to 5 above nothing; on 2026-02-12, the day the fourth was filed, it is
        // not read, three are, and three do not pass.
        Assert.True(MemberReadings.Profit(FourQuarters, PullDate(2026, 2, 13)));
        Assert.False(MemberReadings.Profit(FourQuarters, PullDate(2026, 2, 12)));

        // Three quarters filed fail however much they earned: 10, 40 and 20 is no fourth quarter.
        Assert.False(MemberReadings.Profit([FourQuarters[0], FourQuarters[1] with { NetIncome = 40m }, FourQuarters[2]], PullDate(2026, 3, 1)));

        // A fifth quarter, the year's first, filed with a loss of 6 moves the oldest out: 20 - 40 + 15 - 6 is -11.
        FiledIncome[] five = [.. FourQuarters, new(PullDate(2026, 3, 31), PullDate(2026, 5, 7), -6m, 30m, 5m)];

        Assert.False(MemberReadings.Profit(five, PullDate(2026, 5, 8)));
        Assert.True(MemberReadings.Profit(five, PullDate(2026, 5, 7)));

        // A quarter stating no net income is not one filed, and a quarter filed again later is read as first filed.
        Assert.False(MemberReadings.Profit([.. FourQuarters[..3], FourQuarters[3] with { NetIncome = null }], PullDate(2026, 3, 1)));
        Assert.True(MemberReadings.Profit([.. FourQuarters, FourQuarters[3] with { FilingDate = PullDate(2026, 3, 2), NetIncome = -100m }], PullDate(2026, 3, 9)));

        // The coverage at its floor: 15 of operating income and 7.5 of interest a quarter, 60 over 30, is exactly twice and
        // passes; 7.51 a quarter, 30.04, asks 60.08 of the 60 and does not.
        FiledIncome[] covered = [.. FourQuarters.Select(quarter => quarter with { OperatingIncome = 15m, InterestExpense = 7.5m })];

        Assert.True(MemberReadings.Coverage(covered, PullDate(2026, 2, 13), GicsSectors.Industrials));
        Assert.False(MemberReadings.Coverage([.. covered.Select(quarter => quarter with { InterestExpense = 7.51m })], PullDate(2026, 2, 13), GicsSectors.Industrials));

        // The provider files interest expense with either sign, and it is read as a cost either way.
        Assert.True(MemberReadings.Coverage([.. covered.Select(quarter => quarter with { InterestExpense = -7.5m })], PullDate(2026, 2, 13), GicsSectors.Industrials));

        // A company filing no interest expense passes, a financial company passes whatever it files, and one quarter
        // stating no operating income or fewer than four filed does not.
        Assert.True(MemberReadings.Coverage([.. FourQuarters.Select(quarter => quarter with { InterestExpense = null })], PullDate(2026, 2, 13), GicsSectors.Industrials));
        Assert.True(MemberReadings.Coverage([.. covered.Select(quarter => quarter with { OperatingIncome = 1m })], PullDate(2026, 2, 13), GicsSectors.Financials));
        Assert.False(MemberReadings.Coverage([.. covered[..3], covered[3] with { OperatingIncome = null }], PullDate(2026, 3, 1), GicsSectors.Industrials));
        Assert.False(MemberReadings.Coverage(covered, PullDate(2026, 2, 12), GicsSectors.Industrials));
    }

    [Fact]
    public async Task TheCompaniesPullStoresEachQuartersIncomeAsFiledAndThePurgeTakesItWhole()
    {
        // An answer as the provider files it under the filter: two dated statements, one stating no interest expense, and
        // one carrying no filing date, which cannot be read as it stood.
        var answer = CompanyAnswers.Parse(
            """
            {
              "General::Code": "AAA",
              "General::GicSector": "Industrials",
              "Financials::Balance_Sheet::quarterly": {},
              "Financials::Income_Statement::quarterly": {
                "2026-06-30": { "date": "2026-06-30", "filing_date": "2026-08-06", "netIncome": "1200000.00", "operatingIncome": "2500000.00", "interestExpense": "-300000.00" },
                "2026-03-31": { "date": "2026-03-31", "filing_date": "2026-05-07", "netIncome": "-500000.00", "operatingIncome": "100000.00", "interestExpense": null },
                "2025-12-31": { "date": "2025-12-31", "filing_date": null, "netIncome": "900000.00" }
              }
            }
            """,
            "AAA");

        Assert.Equal(
            [
                new FiledIncome(PullDate(2026, 3, 31), PullDate(2026, 5, 7), -500_000m, 100_000m, null),
                new FiledIncome(PullDate(2026, 6, 30), PullDate(2026, 8, 6), 1_200_000m, 2_500_000m, -300_000m),
            ],
            answer.Income);
        Assert.Equal(1, answer.StatementsUndated);
        Assert.Contains(CompanyAnswers.Statements, CompanyAnswers.Filter);

        using var store = PullStore();

        var feed = new ConstructedCompanyFeed(new Dictionary<string, Func<CompanyAnswer>>
        {
            ["AAA"] = () => answer,
            ["BBB"] = () => new("BBB", "0000000002", GicsSectors.Financials, null, null, null, null, [], 0),
            ["CCC"] = () => throw new ProviderRefusal("The company classification feed answered 404.", transient: false),
            ["EEE"] = () => new("EEE", null, null, null, null, null, null, [], 0),
        });

        var outcome = await HistoryPull.PullCompaniesAsync(feed, PullClock(), store.DatabaseFile, "GSPC", PullFrom, "history-pull-income-1");

        Assert.Equal((2, 1), (outcome.IncomeWritten, outcome.StatementsUndated));
        Assert.Equal(["BBB", "EEE"], outcome.NoIncome);
        Assert.Equal(
            [
                "AAA|2026-03-31|2026-05-07|-500000.00|100000.00|null|history-pull-income-1",
                "AAA|2026-06-30|2026-08-06|1200000.00|2500000.00|-300000.00|history-pull-income-1",
            ],
            FamilyRows(store, "SELECT * FROM pulled_income ORDER BY ticker, period_end;"));
        Assert.Contains("2 quarter(s) of income stored, 1 income statement(s) carrying no filing date, 2 filing no income statement with its date", HistoryPull.Detail(outcome), StringComparison.Ordinal);

        // A second pull keeps the first's rows, and the purge takes them whole.
        var again = await HistoryPull.PullCompaniesAsync(feed, PullClock(), store.DatabaseFile, "GSPC", PullFrom, "history-pull-income-2");

        Assert.Equal(0, again.IncomeWritten);

        var purged = await HistoryPull.PurgeAsync(PullClock(), store.DatabaseFile, "history-pull-income-1", "history-purge-income-1");

        Assert.Equal(2, purged.Income);
        Assert.Empty(FamilyRows(store, "SELECT * FROM pulled_income;"));
    }
}
