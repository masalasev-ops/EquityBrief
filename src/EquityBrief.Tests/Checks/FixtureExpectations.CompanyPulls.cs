using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 14.2: the readings the sector heavyweights and the context checks are measured by, each
// worked by hand, and the four pulls storing what they read, each over constructed answers, with the answers the
// readers were written against read as the provider and the archive sent them.
// see: A company's value on a session is the newest share count filed before it times the session's close on the count's split basis
// see: A company's sector on a session is the GICS sector the provider files, with the fourteen moves of 2023-03-17 read by date
// see: Companies are ranked by CIK with one listing held, the class that traded the more dollars over fifty sessions
// see: A quarter's revenue is read as first filed, a fiscal fourth quarter being the year less its first nine months
// see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
public partial class FixtureExpectations
{
    // The rows the pulls and their readings add that this check reaches: section 17's five and section 18's four.
    internal static readonly string[] CompanyPullClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Company value"),
        CheckReach.Key(Scope.LimitsTable, "Sector on a session"),
        CheckReach.Key(Scope.LimitsTable, "Rank by company"),
        CheckReach.Key(Scope.LimitsTable, "Revenue as first filed"),
        CheckReach.Key(Scope.LimitsTable, "Archive requests"),
        CheckReach.Key(Scope.FailureTable, "A name or a filer a companies, splits or revenue pull asks for is not served"),
        CheckReach.Key(Scope.FailureTable, "A company filing no sector, no count with its date or no CIK"),
        CheckReach.Key(Scope.FailureTable, "One of the fourteen moves of 2023-03-17 filed in another sector or under another filer"),
        CheckReach.Key(Scope.FailureTable, "A filer stating its revenue under none of the concepts"),
        // The 14.2 correction: a spin-off or a merger the provider files as a split.
        CheckReach.Key(Scope.FailureTable, "A spin-off or a merger the provider files as a split"),
    ];

    // Every row the pulls add, whichever check reaches it, which the phase's pair names apart.
    internal static readonly string[] CompanyPullRows = [.. CompanyPullClaims];

    static DateOnly PullDate(int year, int month, int day) => new(year, month, day);

    // The session the counts below were asked on, whose split basis each is restated to.
    static readonly DateOnly CountBasis = PullDate(2026, 10, 2);

    // Three balance sheets of one company, every count on the split basis of 2026-10-02: the quarter to 2024-03-31
    // filed on 2024-04-30 at 1,000 shares, the quarter to 2024-06-30 filed on 2024-07-31 at 1,100, and the quarter
    // to 2024-09-30 filed on 2024-11-01 at 1,200.
    static readonly FiledCount[] ThreeSheets =
    [
        new(PullDate(2024, 3, 31), PullDate(2024, 4, 30), 1_000m, CountBasis),
        new(PullDate(2024, 6, 30), PullDate(2024, 7, 31), 1_100m, CountBasis),
        new(PullDate(2024, 9, 30), PullDate(2024, 11, 1), 1_200m, CountBasis),
    ];

    [Fact]
    public void ASessionReadsTheCountOfTheNewestSheetFiledBeforeItAndValuesItOnTheCountsSplitBasis()
    {
        // 2024-08-01 reads the second sheet, filed the day before it; 2024-10-31 reads the second as well, the third being
        // filed the day after it. The second's own filing day reads the first, a sheet filed on a session not being read on
        // it, and the first's filing day reads none.
        Assert.Equal(1_100m, CompanyValue.CountOn(PullDate(2024, 8, 1), ThreeSheets)!.Shares);
        Assert.Equal(1_100m, CompanyValue.CountOn(PullDate(2024, 10, 31), ThreeSheets)!.Shares);
        Assert.Equal(1_200m, CompanyValue.CountOn(PullDate(2024, 11, 4), ThreeSheets)!.Shares);
        Assert.Equal(1_000m, CompanyValue.CountOn(PullDate(2024, 7, 31), ThreeSheets)!.Shares);
        Assert.Null(CompanyValue.CountOn(PullDate(2024, 4, 30), ThreeSheets));

        // A two for one split on 2024-09-03 falls between the second sheet's filing and 2024-10-31, whose close of 50 is
        // already on the new basis and is read as it stands: 1,100 times 50, 55,000. Before the split, on 2024-08-30, the
        // close of 100 is divided by the two one share became: 1,100 times 50 again. The same company at the same price
        // either side of the split is worth the same.
        FiledSplit[] split = [new(PullDate(2024, 9, 3), 2m, 1m)];

        Assert.Equal(55_000m, CompanyValue.On(new SessionClose(PullDate(2024, 10, 31), 50m, 50m), ThreeSheets, split));
        Assert.Equal(55_000m, CompanyValue.On(new SessionClose(PullDate(2024, 8, 30), 100m, 100m), ThreeSheets, split));

        // A split after the session the counts were asked on is on no count's basis and divides nothing, and a three for
        // two divides by one and a half.
        Assert.Equal(55_000m, CompanyValue.On(new SessionClose(PullDate(2024, 8, 30), 100m, 100m), ThreeSheets, [.. split, new(PullDate(2026, 11, 2), 3m, 1m)]));
        Assert.Equal(1_100m * 30m, CompanyValue.On(new SessionClose(PullDate(2024, 8, 30), 45m, 45m), ThreeSheets, [new(PullDate(2025, 1, 2), 3m, 2m)]));

        // A company paying dividends: its adjusted close stands below its unadjusted one by what it has paid since. At the
        // same unadjusted close and count it is worth what a company paying none is.
        var payer = CompanyValue.On(new SessionClose(PullDate(2024, 10, 31), 38.50m, 50m), ThreeSheets, split);
        var paysNone = CompanyValue.On(new SessionClose(PullDate(2024, 10, 31), 50m, 50m), ThreeSheets, split);

        Assert.Equal((55_000m, 55_000m), (payer!.Value, paysNone!.Value));

        // No count filed before the session, no value.
        Assert.Null(CompanyValue.On(new SessionClose(PullDate(2024, 4, 30), 10m, 10m), ThreeSheets, split));
    }

    [Fact]
    public void ASpinOffTheProviderFilesAsASplitChangesNoSharesAndAPlainSplitOfEachKindDoes()
    {
        // Plain: two for one, three for two, one for fifty, five for four, and three for one as the provider files CSX's,
        // 959,692 for 319,897, three millionths from three. A spin-off's or a merger's adjustment: GE's 1,281
        // for 1,000, DTE's 47 for 40, HON's 1,907 for 2,000, AIRC's one for 1.031190, MTCH's 1,751 for 500, two
        // thousandths from seven for two, and DISH's 763 for 760, four thousandths from one.
        static FiledSplit Filed(decimal made, decimal from) => new(PullDate(2025, 1, 2), made, from);

        Assert.Equal(
            [true, true, true, true, true, false, false, false, false, false, false],
            new[]
            {
                Filed(2m, 1m), Filed(3m, 2m), Filed(1m, 50m), Filed(5m, 4m), Filed(959_692m, 319_897m),
                Filed(1_281m, 1_000m), Filed(47m, 40m), Filed(1_907m, 2_000m), Filed(1m, 1.031190m), Filed(1_751m, 500m), Filed(763m, 760m),
            }.Select(split => split.Plain));
        Assert.Equal((10, 0.0001m), (FiledSplit.PlainMost, FiledSplit.PlainWithin));

        // On 2024-08-30 a spin-off's adjustment after it divides nothing: 1,100 shares at 100, 110,000. A two for one beside
        // it divides by two, 55,000, and a one for fifty multiplies by fifty, 5,500,000.
        var session = new SessionClose(PullDate(2024, 8, 30), 100m, 100m);

        Assert.Equal(110_000m, CompanyValue.On(session, ThreeSheets, [new(PullDate(2025, 1, 2), 1_281m, 1_000m)]));
        Assert.Equal(55_000m, CompanyValue.On(session, ThreeSheets, [new(PullDate(2025, 1, 2), 1_281m, 1_000m), new(PullDate(2025, 3, 3), 2m, 1m)]));
        Assert.Equal(5_500_000m, CompanyValue.On(session, ThreeSheets, [new(PullDate(2025, 3, 3), 1m, 50m)]));
    }

    [Fact]
    public void CompaniesAreRankedOnTheirCountsAsTheyStoodAndTwoClassesOfOneCompanyOnce()
    {
        // Two companies at a close of 10 on 2019-06-03: X filed 1,000 shares for 2019's first quarter and 3,000 for
        // 2026's second, Y 2,000 and then 1,500. As they stood, Y is worth 20,000 and X 10,000; on today's counts X would
        // lead at 30,000.
        FiledCount[] x = [new(PullDate(2019, 3, 31), PullDate(2019, 5, 1), 1_000m, CountBasis), new(PullDate(2026, 6, 30), PullDate(2026, 7, 31), 3_000m, CountBasis)];
        FiledCount[] y = [new(PullDate(2019, 3, 31), PullDate(2019, 5, 2), 2_000m, CountBasis), new(PullDate(2026, 6, 30), PullDate(2026, 7, 30), 1_500m, CountBasis)];
        var session = new SessionClose(PullDate(2019, 6, 3), 10m, 10m);

        var asTheyStood = CompanyRank.Ranked(
        [
            new("X", CompanyRank.CompanyOf("X", "0000000011"), CompanyValue.On(session, x, [])!.Value, 1m),
            new("Y", CompanyRank.CompanyOf("Y", "0000000012"), CompanyValue.On(session, y, [])!.Value, 1m),
        ]);

        Assert.Equal(["Y 20000", "X 10000"], asTheyStood.Select(listing => FormattableString.Invariant($"{listing.Ticker} {listing.Value}")));

        // The dollars a listing traded are its newest fifty sessions' unadjusted closes times their volumes: GOOGL's 2,000
        // shares a session at 100, 10,000,000, and GOOG's 1,500, 7,500,000, ten older sessions of a million shares counted
        // in neither.
        (decimal, long)[] older = [.. Enumerable.Repeat((100m, 1_000_000L), 10)];
        var googl = CompanyRank.DollarVolume([.. older, .. Enumerable.Repeat((100m, 2_000L), 50)]);
        var goog = CompanyRank.DollarVolume([.. older, .. Enumerable.Repeat((100m, 1_500L), 50)]);

        Assert.Equal((10_000_000m, 7_500_000m), (googl, goog));

        // Alphabet's two classes under one CIK hold one place, GOOGL's as the more traded, at GOOGL's value though GOOG's
        // is the larger. Two classes trading the same dollars hold it by the ticker first in order, FOX before FOXA. Two
        // listings filing no CIK are a company each.
        var ranked = CompanyRank.Ranked(
        [
            new("GOOG", CompanyRank.CompanyOf("GOOG", "0001652044"), 2_000m, goog),
            new("GOOGL", CompanyRank.CompanyOf("GOOGL", "0001652044"), 1_990m, googl),
            new("AAA", CompanyRank.CompanyOf("AAA", "0000000001"), 1_995m, 1m),
            new("NOCIK", CompanyRank.CompanyOf("NOCIK", null), 500m, 1m),
            new("NOCIKB", CompanyRank.CompanyOf("NOCIKB", null), 400m, 1m),
            new("FOXA", CompanyRank.CompanyOf("FOXA", "0001754301"), 101m, 5m),
            new("FOX", CompanyRank.CompanyOf("FOX", "0001754301"), 100m, 5m),
        ]);

        Assert.Equal(
            ["AAA 1995", "GOOGL 1990", "NOCIK 500", "NOCIKB 400", "FOX 100"],
            ranked.Select(listing => FormattableString.Invariant($"{listing.Ticker} {listing.Value}")));
        Assert.Equal(50, CompanyRank.DollarVolumeSessions);
    }

    [Fact]
    public void ASectorIsReadOnASessionWithTheFourteenMovesOf2023ReadByDate()
    {
        // The published fourteen: eight from Information Technology to Financials, three from it to Industrials and three
        // from Consumer Discretionary to Consumer Staples, each named once.
        Assert.Equal(14, GicsSectors.Moves.Count);
        Assert.Equal(14, GicsSectors.Moves.Select(move => move.Ticker).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            (8, 3, 3),
            (GicsSectors.Moves.Count(move => move.From == GicsSectors.InformationTechnology && move.To == GicsSectors.Financials),
             GicsSectors.Moves.Count(move => move.From == GicsSectors.InformationTechnology && move.To == GicsSectors.Industrials),
             GicsSectors.Moves.Count(move => move.From == GicsSectors.ConsumerDiscretionary && move.To == GicsSectors.ConsumerStaples)));
        Assert.Equal(GicsSectors.Eleven.Order(StringComparer.Ordinal), GicsSectors.Funds.Keys.Order(StringComparer.Ordinal));

        var last = PullDate(2023, 3, 17);
        var next = PullDate(2023, 3, 20);

        string Either(string ticker, string filed) => $"{GicsSectors.On(ticker, filed, last)} then {GicsSectors.On(ticker, filed, next)}";

        // Visa reads the sector it left on the close it moved after and the one it joined on the next session; Target and
        // ADP the same; Apple, which the table does not name, the sector filed on both.
        Assert.Equal("Information Technology then Financials", Either("V", GicsSectors.Financials));
        Assert.Equal("Consumer Discretionary then Consumer Staples", Either("TGT", GicsSectors.ConsumerStaples));
        Assert.Equal("Information Technology then Industrials", Either("ADP", GicsSectors.Industrials));
        Assert.Equal("Information Technology then Information Technology", Either("AAPL", GicsSectors.InformationTechnology));

        // The eleven were formed after the close of 2018-09-21: the first session after it reads, that one reads none. A
        // sector filed outside the eleven, as the store's own scheme names one, and none filed, read none.
        Assert.Equal(GicsSectors.InformationTechnology, GicsSectors.On("V", GicsSectors.Financials, PullDate(2018, 9, 24)));
        Assert.Null(GicsSectors.On("V", GicsSectors.Financials, PullDate(2018, 9, 21)));
        Assert.Null(GicsSectors.On("AAA", "Technology", next));
        Assert.Null(GicsSectors.On("AAA", null, next));
    }

    // One company as a companies pull is answered with it, filed in a sector under a filer with no counts.
    static CompanyAnswer CompanyAnswerOf(string ticker, string? sector, string? cik, params SheetCount[] counts) =>
        new(ticker, cik, sector, null, null, null, null, counts, 0);

    [Fact]
    public void TheCompaniesPullReadsTheFourteenMovesAgainstTheSectorsAndFilersTheProviderFiles()
    {
        CompanyAnswer[] filed = [.. GicsSectors.Moves.Select(move => CompanyAnswerOf(move.Ticker, move.To, move.Cik))];

        Assert.Equal((14, 0), (HistoryPull.MovesAgainst(filed).Filed, HistoryPull.MovesAgainst(filed).Not.Count));

        // Target filed in the sector it left, Mastercard under another filer and Dollar General not answered: eleven filed
        // where they moved, the three named in the table's order. Fiserv filing no CIK, as the provider files it, is read
        // by its sector alone and counted.
        CompanyAnswer[] moved =
        [
            .. filed
                .Where(answer => answer.Ticker != "DG")
                .Select(answer => answer.Ticker switch
                {
                    "TGT" => answer with { Sector = GicsSectors.ConsumerDiscretionary },
                    "MA" => answer with { Cik = "0000000009" },
                    "FISV" => answer with { Cik = null },
                    _ => answer,
                }),
        ];
        var (count, not) = HistoryPull.MovesAgainst(moved);

        Assert.Equal(11, count);
        Assert.Equal(
            ["MA: filed under CIK 0000000009, not 0001141391", "TGT: filed in Consumer Discretionary, not Consumer Staples", "DG: not answered"],
            not);
    }

    [Fact]
    public void AQuartersRevenueIsReadAsFirstFiledAndAFiscalFourthQuarterAsTheYearLessItsNineMonths()
    {
        const string Revenues = "Revenues";
        const string Contracts = "RevenueFromContractWithCustomerExcludingAssessedTax";
        const string Net = "RevenuesNetOfInterestExpense";

        FiledRevenue[] facts =
        [
            // The quarter to 2023-03-31, first filed on 2023-05-01 at 100, restated on 2023-08-01 at 104, and stated again
            // at 105 as the year-earlier column of the quarter to 2024-03-31, filed on 2024-05-01.
            new(Revenues, PullDate(2023, 1, 1), PullDate(2023, 3, 31), 100m, PullDate(2023, 5, 1), "10-Q", "a-1"),
            new(Revenues, PullDate(2023, 1, 1), PullDate(2023, 3, 31), 104m, PullDate(2023, 8, 1), "10-Q/A", "a-2"),
            new(Revenues, PullDate(2023, 1, 1), PullDate(2023, 3, 31), 105m, PullDate(2024, 5, 1), "10-Q", "a-5"),

            // The nine months to 2023-09-30, first filed on 2023-11-01 at 330, and the year to 2023-12-31 on 2024-02-20 at
            // 450, with no quarter of its own: the fourth quarter is 450 less 330, 120, first filed the day the year was.
            new(Revenues, PullDate(2023, 1, 1), PullDate(2023, 9, 30), 330m, PullDate(2023, 11, 1), "10-Q", "a-3"),
            new(Revenues, PullDate(2023, 1, 1), PullDate(2023, 12, 31), 450m, PullDate(2024, 2, 20), "10-K", "a-4"),

            // The quarter to 2024-03-31 under two concepts in one filing: revenue, 130, before revenue from contracts, 125.
            new(Revenues, PullDate(2024, 1, 1), PullDate(2024, 3, 31), 130m, PullDate(2024, 5, 1), "10-Q", "a-5"),
            new(Contracts, PullDate(2024, 1, 1), PullDate(2024, 3, 31), 125m, PullDate(2024, 5, 1), "10-Q", "a-5"),

            // A concept the reading does not name states no revenue.
            new("CostOfRevenue", PullDate(2024, 4, 1), PullDate(2024, 6, 30), 70m, PullDate(2024, 8, 1), "10-Q", "a-6"),
        ];

        Assert.Equal(
            [
                "2023-01-01 2023-03-31 100 2023-05-01 Revenues stated",
                "2023-10-01 2023-12-31 120 2024-02-20 Revenues year less nine months",
                "2024-01-01 2024-03-31 130 2024-05-01 Revenues stated",
            ],
            FirstFiledRevenue.Quarters(facts).Select(RevenueLine));

        // A bank's filing stating revenue net of its interest expense and revenue: the net figure is read.
        FiledRevenue[] bank =
        [
            new(Revenues, PullDate(2024, 1, 1), PullDate(2024, 3, 31), 200m, PullDate(2024, 5, 2), "10-Q", "b-1"),
            new(Net, PullDate(2024, 1, 1), PullDate(2024, 3, 31), 150m, PullDate(2024, 5, 2), "10-Q", "b-1"),
        ];

        Assert.Equal(150m, Assert.Single(FirstFiledRevenue.Quarters(bank)).Value);

        // A year and its nine months filed under two concepts give no fourth quarter.
        FiledRevenue[] mixed =
        [
            new(Revenues, PullDate(2023, 1, 1), PullDate(2023, 9, 30), 330m, PullDate(2023, 11, 1), "10-Q", "c-1"),
            new(Contracts, PullDate(2023, 1, 1), PullDate(2023, 12, 31), 450m, PullDate(2024, 2, 20), "10-K", "c-2"),
        ];

        Assert.Empty(FirstFiledRevenue.Quarters(mixed));

        // A quarter spans 80 to 100 days, both ends in: 2024-01-01 to 2024-03-20 and to 2024-04-09 are quarters, a day
        // shorter or longer is neither.
        Assert.Equal((80, 100, 260, 285, 350, 380, 6), (FirstFiledRevenue.QuarterFewest, FirstFiledRevenue.QuarterMost, FirstFiledRevenue.NineMonthsFewest, FirstFiledRevenue.NineMonthsMost, FirstFiledRevenue.YearFewest, FirstFiledRevenue.YearMost, FirstFiledRevenue.Concepts.Count));

        DateOnly[] ends = [PullDate(2024, 3, 19), PullDate(2024, 3, 20), PullDate(2024, 4, 9), PullDate(2024, 4, 10)];

        Assert.Equal(
            [false, true, true, false],
            ends.Select(end => FirstFiledRevenue.Quarters([new(Revenues, PullDate(2024, 1, 1), end, 1m, PullDate(2024, 5, 1), "10-Q", "d-1")]).Count == 1));
    }

    static string RevenueLine(QuarterRevenue quarter) =>
        FormattableString.Invariant($"{quarter.Start:yyyy-MM-dd} {quarter.End:yyyy-MM-dd} {quarter.Value} {quarter.Filed:yyyy-MM-dd} {quarter.Concept} {(quarter.YearLessNineMonths ? "year less nine months" : "stated")}");

    [Fact]
    public void TheCapturedCompanySplitsAndConceptAnswersAreReadAsTheyWereSent()
    {
        string Capture(string file) => File.ReadAllText(Path.Combine(Folder(), file));

        // Apple as a member, its two balance sheets kept, the counts on today's split basis.
        var apple = CompanyAnswers.Parse(Capture("phase14-company-AAPL.json"), "AAPL");

        Assert.Equal(
            "0000320193 | Information Technology | Technology Hardware & Equipment | Technology Hardware, Storage & Peripherals | Technology Hardware, Storage & Peripherals | listed | 0",
            string.Join(" | ", apple.Cik, apple.Sector, apple.IndustryGroup, apple.Industry, apple.SubIndustry, apple.DelistedOn is null ? "listed" : "delisted", apple.SheetsUncounted));
        Assert.Equal(
            ["2019-03-31 2019-05-01 18802584000.00", "2026-06-30 2026-07-31 14750302000.00"],
            apple.Counts.Select(count => FormattableString.Invariant($"{count.PeriodEnd:yyyy-MM-dd} {count.FilingDate:yyyy-MM-dd} {count.Shares}")));

        // Activision, delisted in 2023, filed as a company still.
        var activision = CompanyAnswers.Parse(Capture("phase14-company-ATVI.json"), "ATVI");

        Assert.Equal("0000718877 | Communication Services | 2023-10-13", FormattableString.Invariant($"{activision.Cik} | {activision.Sector} | {activision.DelistedOn:yyyy-MM-dd}"));
        Assert.Equal(
            ["2019-03-31 2019-05-02 770000000.00", "2023-06-30 2023-07-31 794000000.00"],
            activision.Counts.Select(count => FormattableString.Invariant($"{count.PeriodEnd:yyyy-MM-dd} {count.FilingDate:yyyy-MM-dd} {count.Shares}")));

        // NVIDIA's six splits, a three for two among them; the two after 2019-03-29 divide its close of 179.56 by forty.
        var nvidia = SplitAnswers.Parse(Capture("phase14-splits-NVDA.json"), "NVDA");

        Assert.Equal(
            ["2000-06-27 2/1", "2001-09-12 2/1", "2006-04-07 2/1", "2007-09-11 3/2", "2021-07-20 4/1", "2024-06-10 10/1"],
            nvidia.Select(split => FormattableString.Invariant($"{split.ExDate:yyyy-MM-dd} {split.NewShares:0.##}/{split.OldShares:0.##}")));
        Assert.Equal(4.489m, CompanyValue.OnBasis(PullDate(2019, 3, 29), 179.56m, CountBasis, [.. nvidia.Select(split => new FiledSplit(split.ExDate, split.NewShares, split.OldShares))]));

        // Apple's revenue under the concept most filers use, and JPMorgan's under a bank's own.
        var appleRevenue = ConceptAnswers.Parse(Capture("phase14-concept-AAPL.json"), "0000320193", "RevenueFromContractWithCustomerExcludingAssessedTax");
        var bankRevenue = ConceptAnswers.Parse(Capture("phase14-concept-JPM.json"), "0000019617", "RevenuesNetOfInterestExpense");

        Assert.Equal((21, 13), (appleRevenue.Count, bankRevenue.Count));

        var appleQuarters = FirstFiledRevenue.Quarters(appleRevenue.Select(fact => new FiledRevenue("RevenueFromContractWithCustomerExcludingAssessedTax", fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)));

        // The fiscal first quarter of 2019 as the 10-Q filed on 2019-01-30 stated it, not as the 10-K's column of
        // 2019-10-31; the fiscal fourth quarter as the 10-K states it, which the year less its nine months agrees with.
        Assert.Contains("2018-09-30 2018-12-29 84310000000 2019-01-30 RevenueFromContractWithCustomerExcludingAssessedTax stated", appleQuarters.Select(RevenueLine));
        Assert.Contains("2019-06-30 2019-09-28 64040000000 2019-10-31 RevenueFromContractWithCustomerExcludingAssessedTax stated", appleQuarters.Select(RevenueLine));
        Assert.Equal(64_040_000_000m, 260_174_000_000m - 196_134_000_000m);

        var bankQuarters = FirstFiledRevenue.Quarters(bankRevenue.Select(fact => new FiledRevenue("RevenuesNetOfInterestExpense", fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)));

        // JPMorgan's first quarter of 2019 as first filed, and the fourth of 2018 worked out as its year less nine months,
        // first filed when the later of the two was, the capture holding the nine months' column of 2019 alone.
        Assert.Contains("2019-01-01 2019-03-31 29123000000 2019-05-02 RevenuesNetOfInterestExpense stated", bankQuarters.Select(RevenueLine));
        Assert.Contains("2018-10-01 2018-12-31 26109000000 2019-11-04 RevenuesNetOfInterestExpense year less nine months", bankQuarters.Select(RevenueLine));

        // Activision's facts, the two concepts it moved its revenue between: the first quarter of 2018 as the 10-Q of
        // 2018-05-03 stated it under revenue, and not as the 10-K and 10-Q of 2019 stated it again under revenue from
        // contracts with customers.
        var activisionFacts = ConceptAnswers.FromFacts(Capture("phase14-sec-facts-ATVI.json"), "0000718877", FirstFiledRevenue.Concepts);

        Assert.Equal(
            (17, 25, 0),
            (activisionFacts["Revenues"].Count, activisionFacts["RevenueFromContractWithCustomerExcludingAssessedTax"].Count, activisionFacts["SalesRevenueNet"].Count));
        Assert.Contains(
            "2018-01-01 2018-03-31 1965000000 2018-05-03 Revenues stated",
            FirstFiledRevenue.Quarters(activisionFacts.SelectMany(concept => concept.Value.Select(fact => new FiledRevenue(concept.Key, fact.Start, fact.End, fact.Value, fact.Filed, fact.Form, fact.Accession)))).Select(RevenueLine));

        // A concept the archive holds no dollars under, sent as an empty object where the figures would be, reads as none,
        // as Coca-Cola's revenue came back from the endpoint for one concept; anything else in that place is refused.
        Assert.Empty(ConceptAnswers.Parse("{\"units\":{\"USD\":{}}}", "0000021344", "Revenues"));
        Assert.Empty(ConceptAnswers.FromFacts("{\"facts\":{\"us-gaap\":{\"Revenues\":{\"units\":{\"USD\":{}}}}}}", "0000021344", ["Revenues"])["Revenues"]);
        Assert.Throws<FormatException>(() => ConceptAnswers.Parse("{\"units\":{\"USD\":{\"val\":1}}}", "0000021344", "Revenues"));

        // Answers that cannot be read are refused rather than read as nothing: a company answer that is not one object, a
        // splits answer that is not an array or whose ratio is not two numbers, and a concept answer that is not one object.
        Assert.Throws<FormatException>(() => CompanyAnswers.Parse("[]", "AAA"));
        Assert.Throws<FormatException>(() => SplitAnswers.Parse("{}", "AAA"));
        Assert.Throws<FormatException>(() => SplitAnswers.Parse("[{\"date\":\"2020-01-02\",\"split\":\"4\"}]", "AAA"));
        Assert.Throws<FormatException>(() => ConceptAnswers.Parse("[]", "0000000001", "Revenues"));
        Assert.Empty(ConceptAnswers.Parse("{\"units\":{}}", "0000000001", "Revenues"));

        // A company filing nothing in a field, the provider's NA, reads none; a sheet with no filing date, one with no
        // count and one counting nothing are counted as unread, and the CIK sent as a number is padded.
        var bare = CompanyAnswers.Parse(
            """
            {"General::Code":"AAA","General::CIK":1234,"General::GicSector":"NA","General::GicGroup":null,"General::DelistedDate":"NA",
             "Financials::Balance_Sheet::quarterly":{
               "2024-03-31":{"date":"2024-03-31","filing_date":null,"commonStockSharesOutstanding":"100.00"},
               "2024-06-30":{"date":"2024-06-30","filing_date":"2024-07-30","commonStockSharesOutstanding":null},
               "2024-09-30":{"date":"2024-09-30","filing_date":"2024-10-30","commonStockSharesOutstanding":"0.00"},
               "2024-12-31":{"date":"2024-12-31","filing_date":"2025-02-03","commonStockSharesOutstanding":"120.00"}}}
            """,
            "AAA");

        Assert.Equal("0000001234", bare.Cik);
        Assert.Null(bare.Sector);
        Assert.Null(bare.IndustryGroup);
        Assert.Null(bare.DelistedOn);
        Assert.Equal((3, 1), (bare.SheetsUncounted, bare.Counts.Count));
    }

    // ---- the pulls over constructed answers -----------------------------------

    // A Friday evening in New York, so tonight's session, where every span ends, is 2026-09-04.
    static readonly DateTimeOffset PullInstant = new(2026, 9, 4, 21, 10, 0, TimeSpan.Zero);

    static readonly DateOnly PullFrom = PullDate(2026, 7, 1);

    static IClock PullClock() => FixedClock.At(PullInstant, SessionZones.UnitedStates);

    // Four names held over the span, and one that left before it: AAA a member throughout, BBB leaving on 2026-08-03,
    // CCC joining on 2026-08-17 and EEE a member throughout; DDD left on 2026-06-30 and is never asked for.
    static TemporaryStore PullStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at) VALUES
                ('GSPC', 'AAA', NULL, NULL, '2026-09-04T21:10:00Z'),
                ('GSPC', 'BBB', NULL, '2026-08-03', '2026-09-04T21:10:00Z'),
                ('GSPC', 'CCC', '2026-08-17', NULL, '2026-09-04T21:10:00Z'),
                ('GSPC', 'DDD', NULL, '2026-06-30', '2026-09-04T21:10:00Z'),
                ('GSPC', 'EEE', NULL, NULL, '2026-09-04T21:10:00Z');");

        return store;
    }

    sealed class ConstructedCompanyFeed(IReadOnlyDictionary<string, Func<CompanyAnswer>> answers) : ICompanyFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<CompanyAnswer> CompanyAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;
            Asked.Add(ticker);

            return Task.FromResult(answers[ticker]());
        }
    }

    sealed class ConstructedSplitFeed(IReadOnlyDictionary<string, Func<IReadOnlyList<SplitAnswer>>> answers) : ISplitHistoryFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<SplitAnswer>> SplitsAsync(string ticker, DateOnly from, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(answers[ticker]());
        }
    }

    sealed class ConstructedRevenueFeed(Func<string, IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>> answer) : IFiledRevenueFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>> RevenueAsync(string cik, IReadOnlyList<string> concepts, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(answer(cik));
        }
    }

    sealed class ConstructedFundFeed(Func<string, IReadOnlyList<ProviderBar>> answer) : IHistoricalBarFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;

            return Task.FromResult(answer(ticker));
        }
    }

    [Fact]
    public async Task TheCompaniesPullStoresEachAnsweredNameAndNamesEveryNameNotServedAndEveryFieldNotFiled()
    {
        using var store = PullStore();

        // AAA answers with two dated counts and a sheet carrying none; BBB is refused, CCC answers a page that is not
        // JSON and EEE answers filing no sector, no count and no CIK.
        var feed = new ConstructedCompanyFeed(new Dictionary<string, Func<CompanyAnswer>>
        {
            ["AAA"] = () => new("AAA", "0000000001", GicsSectors.InformationTechnology, "Software & Services", "Software", "Application Software", null, [new(PullDate(2026, 3, 31), PullDate(2026, 4, 30), 1_000m), new(PullDate(2026, 6, 30), PullDate(2026, 7, 31), 990.50m)], 1),
            ["BBB"] = () => throw new ProviderRefusal("The company classification feed answered 404.", transient: false),
            ["CCC"] = () => throw new JsonException("'<' is an invalid start of a value."),
            ["EEE"] = () => new("EEE", null, null, null, null, null, PullDate(2026, 8, 20), [], 2),
        });

        var outcome = await HistoryPull.PullCompaniesAsync(feed, PullClock(), store.DatabaseFile, "GSPC", PullFrom, "history-pull-companies-1");

        Assert.Equal(["AAA", "BBB", "CCC", "EEE"], feed.Asked);
        Assert.Equal((4, 2, 2, 2, 4), (outcome.Names, outcome.Answered, outcome.CompaniesWritten, outcome.CountsWritten, outcome.Requests));
        Assert.Equal(
            ["BBB: The company classification feed answered 404.", "CCC: its answer could not be read: '<' is an invalid start of a value."],
            outcome.Unanswered);
        Assert.Equal(["EEE"], outcome.NoSector);
        Assert.Equal(["EEE"], outcome.NoCount);
        Assert.Equal(["EEE"], outcome.NoCik);
        Assert.Equal(3, outcome.SheetsUncounted);

        // None of the fourteen answered here, so none is filed where it moved and each is named.
        Assert.Equal((0, 14), (outcome.MovesFiled, outcome.MovesNot.Count));

        Assert.Equal(
            [
                "AAA|0000000001|Information Technology|Software & Services|Software|Application Software|null|history-pull-companies-1",
                "EEE|null|null|null|null|null|2026-08-20|history-pull-companies-1",
            ],
            FamilyRows(store, "SELECT * FROM pulled_company ORDER BY ticker;"));
        Assert.Equal(
            ["AAA|2026-03-31|2026-04-30|1000|2026-09-04|history-pull-companies-1", "AAA|2026-06-30|2026-07-31|990.50|2026-09-04|history-pull-companies-1"],
            FamilyRows(store, "SELECT * FROM pulled_shares ORDER BY ticker, period_end;"));
        Assert.Equal(
            ["history-pull-companies|partial|4|4"],
            FamilyRows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'history-pull-companies-1';"));

        // A second pull adds only what no earlier pull holds, and a purge takes a pull's rows from each table whole.
        var again = await HistoryPull.PullCompaniesAsync(feed, PullClock(), store.DatabaseFile, "GSPC", PullFrom, "history-pull-companies-2");

        Assert.Equal((0, 0), (again.CompaniesWritten, again.CountsWritten));

        var purged = await HistoryPull.PurgeAsync(PullClock(), store.DatabaseFile, "history-pull-companies-1", "history-purge-1");

        Assert.Equal((2, 2, 0, 0, 0), (purged.Companies, purged.Shares, purged.Splits, purged.Revenue, purged.Bars));
        Assert.Empty(FamilyRows(store, "SELECT * FROM pulled_company;"));
        Assert.Empty(FamilyRows(store, "SELECT * FROM pulled_shares;"));

        // A date on or after tonight's session is refused before any request.
        await Assert.ThrowsAsync<ArgumentException>(() => HistoryPull.PullCompaniesAsync(feed, PullClock(), store.DatabaseFile, "GSPC", PullDate(2026, 9, 4), "history-pull-companies-3"));
        Assert.Equal(8, feed.Requests);
    }

    [Fact]
    public async Task TheSplitsPullStoresEachNamesSplitsOverTheSpanAndNamesANameNotServed()
    {
        using var store = PullStore();

        var feed = new ConstructedSplitFeed(new Dictionary<string, Func<IReadOnlyList<SplitAnswer>>>
        {
            // One split before the span, which the provider sends past the date asked and the pull keeps out, and one in it.
            ["AAA"] = () => [new(PullDate(2026, 6, 15), 2m, 1m), new(PullDate(2026, 8, 10), 3m, 2m)],
            ["BBB"] = () => throw new ProviderRefusal("The splits history feed for BBB feed answered 500.", transient: true),
            ["CCC"] = () => [],
            ["EEE"] = () => [],
        });

        var outcome = await HistoryPull.PullSplitsAsync(feed, PullClock(), store.DatabaseFile, "GSPC", PullFrom, "history-pull-splits-1");

        Assert.Equal((4, 3, 1, 1, 4, 1), (outcome.Names, outcome.Answered, outcome.WithASplit, outcome.Written, outcome.Requests, outcome.Plain));
        Assert.Equal(["BBB: The splits history feed for BBB feed answered 500."], outcome.Unanswered);
        Assert.Equal(["AAA|2026-08-10|3|2|history-pull-splits-1"], FamilyRows(store, "SELECT * FROM pulled_split;"));
        Assert.Equal(["history-pull-splits|partial|1|4"], FamilyRows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'history-pull-splits-1';"));

        var purged = await HistoryPull.PurgeAsync(PullClock(), store.DatabaseFile, "history-pull-splits-1", "history-purge-1");

        Assert.Equal(1, purged.Splits);
    }

    [Fact]
    public async Task TheSectorFundsPullStoresEachFundsSeriesBesideTheMarketSeriesAndNamesAFundNotServed()
    {
        using var store = PullStore();

        // Every fund answers two sessions but XLC, refused, and XLRE, which sends none.
        var feed = new ConstructedFundFeed(fund => fund switch
        {
            "XLC" => throw new ProviderRefusal("The historical bar feed for XLC feed answered 404.", transient: false),
            "XLRE" => [],
            _ => [new(PullDate(2026, 9, 3), 10m, 11m, 9m, 10.5m, 10.4m, 100), new(PullDate(2026, 9, 4), 10.5m, 12m, 10m, 11m, 10.9m, 120)],
        });

        var outcome = await HistoryPull.PullSectorFundsAsync(feed, PullClock(), store.DatabaseFile, PullFrom, "history-pull-funds-1");

        Assert.Equal(["XLB", "XLC", "XLE", "XLF", "XLI", "XLK", "XLP", "XLRE", "XLU", "XLV", "XLY"], HistoryPull.SectorFunds);
        Assert.Equal((9, 18, 11), (outcome.Stored.Count, outcome.Written, outcome.Requests));
        Assert.Equal(["XLC: The historical bar feed for XLC feed answered 404.", "XLRE: the provider sent no session"], outcome.Refused);
        Assert.Equal(["XLK|2026-09-03|10|11|9|10.5", "XLK|2026-09-04|10.5|12|10|11"], FamilyRows(store, "SELECT series, session_date, open, high, low, close FROM pulled_market_bar WHERE series = 'XLK' ORDER BY session_date;"));
        Assert.Equal(["history-pull-sector-funds|partial|18|11"], FamilyRows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'history-pull-funds-1';"));
    }

    [Fact]
    public async Task TheRevenuePullAsksEachPulledFilerForItsFactsATenthOfASecondApartAndStoresEveryFigureWithItsFilingDay()
    {
        using var store = PullStore();

        // Five pulled companies: AAA and its second class GGG under one filer, FFF and HHH under one each, and EEE under
        // none.
        store.Execute(@"
            INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull) VALUES
                ('AAA', '0000000001', 'Information Technology', NULL, NULL, NULL, NULL, 'history-pull-companies-1'),
                ('GGG', '0000000001', 'Information Technology', NULL, NULL, NULL, NULL, 'history-pull-companies-1'),
                ('FFF', '0000000002', 'Financials', NULL, NULL, NULL, NULL, 'history-pull-companies-1'),
                ('HHH', '0000000003', 'Financials', NULL, NULL, NULL, NULL, 'history-pull-companies-1'),
                ('EEE', NULL, NULL, NULL, NULL, NULL, NULL, 'history-pull-companies-1');");

        // The first filer's facts state two figures under revenue and one under revenue from contracts; the archive
        // refuses the second's, and the third's state none under any of the concepts.
        var feed = new ConstructedRevenueFeed(cik => cik switch
        {
            "0000000001" => new Dictionary<string, IReadOnlyList<ConceptFact>>
            {
                ["Revenues"] =
                [
                    new(PullDate(2026, 1, 1), PullDate(2026, 3, 31), 100m, PullDate(2026, 5, 1), "10-Q", "0000000001-26-000001"),
                    new(PullDate(2026, 4, 1), PullDate(2026, 6, 30), 110m, PullDate(2026, 8, 1), "10-Q", "0000000001-26-000002"),
                ],
                ["RevenueFromContractWithCustomerExcludingAssessedTax"] =
                    [new(PullDate(2026, 4, 1), PullDate(2026, 6, 30), 105m, PullDate(2026, 8, 1), "10-Q", "0000000001-26-000002")],
            },
            "0000000002" => throw new ProviderRefusal("The archive refused CompanyFacts with status 429.", transient: true),
            _ => new Dictionary<string, IReadOnlyList<ConceptFact>>(),
        });

        var waits = new List<TimeSpan>();
        var outcome = await HistoryPull.PullRevenueAsync(
            feed,
            PullClock(),
            store.DatabaseFile,
            "history-pull-revenue-1",
            (wait, _) =>
            {
                waits.Add(wait);

                return Task.CompletedTask;
            });

        // Three filers, one request each, each followed by a tenth of a second.
        Assert.Equal((3, 3, 3), (outcome.Filers, outcome.Written, outcome.Requests));
        Assert.Equal(Enumerable.Repeat(TimeSpan.FromMilliseconds(100), 3), waits);
        Assert.Equal(10, HistoryPull.ArchiveRequestsASecond);
        Assert.Equal(["EEE"], outcome.WithoutAFiler);
        Assert.Equal(["0000000003"], outcome.NoRevenue);
        Assert.Equal(["CIK 0000000002: The archive refused CompanyFacts with status 429."], outcome.Unanswered);
        Assert.Equal(
            ["RevenuesNetOfInterestExpense 0 0", "Revenues 1 2", "RevenueFromContractWithCustomerExcludingAssessedTax 1 1", "RevenueFromContractWithCustomerIncludingAssessedTax 0 0", "RegulatedAndUnregulatedOperatingRevenue 0 0", "SalesRevenueNet 0 0"],
            outcome.ByConcept.Select(concept => FormattableString.Invariant($"{concept.Concept} {concept.Filers} {concept.Figures}")));
        Assert.Equal(
            [
                "0000000001|RevenueFromContractWithCustomerExcludingAssessedTax|2026-04-01|2026-06-30|0000000001-26-000002|105|2026-08-01|10-Q|history-pull-revenue-1",
                "0000000001|Revenues|2026-01-01|2026-03-31|0000000001-26-000001|100|2026-05-01|10-Q|history-pull-revenue-1",
                "0000000001|Revenues|2026-04-01|2026-06-30|0000000001-26-000002|110|2026-08-01|10-Q|history-pull-revenue-1",
            ],
            FamilyRows(store, "SELECT * FROM pulled_revenue ORDER BY concept, period_start;"));
        Assert.Equal(["history-pull-revenue|partial|3|3"], FamilyRows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'history-pull-revenue-1';"));

        var purged = await HistoryPull.PurgeAsync(PullClock(), store.DatabaseFile, "history-pull-revenue-1", "history-purge-1");

        Assert.Equal(3, purged.Revenue);

        // With no pulled company carrying a filer there is no one to ask, and the pull is refused before any request.
        using var empty = PullStore();

        await Assert.ThrowsAsync<ArgumentException>(() => HistoryPull.PullRevenueAsync(feed, PullClock(), empty.DatabaseFile, "history-pull-revenue-2", (_, _) => Task.CompletedTask));
        Assert.Equal(3, feed.Requests);
    }

    [Fact]
    public async Task TheVerbRunsEachPullByItsOptionAndRefusesOneItWasHandedNoFeedFor()
    {
        using var store = PullStore();
        using var output = new StringWriter();
        using var error = new StringWriter();

        var feed = new ConstructedCompanyFeed(new Dictionary<string, Func<CompanyAnswer>>
        {
            ["AAA"] = () => CompanyAnswerOf("AAA", GicsSectors.Financials, "0000000001"),
            ["BBB"] = () => CompanyAnswerOf("BBB", GicsSectors.Financials, "0000000002"),
            ["CCC"] = () => CompanyAnswerOf("CCC", GicsSectors.Financials, "0000000003"),
            ["EEE"] = () => CompanyAnswerOf("EEE", GicsSectors.Financials, "0000000004"),
        });

        var companies = await HistoryPull.RunAsync(
            ["--companies", "--from", "2026-07-01"],
            () => throw new InvalidOperationException("the night's feeds are not asked by a companies pull"),
            PullClock(),
            store.DatabaseFile,
            output,
            error,
            companies: () => feed);

        Assert.Equal(0, companies);
        Assert.StartsWith("pull " + HistoryPull.RunPrefix, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("4 of 4 name(s) answered, 4 compan(ies) and 0 share count(s) stored", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("4 filing no count with its date", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("the moves of 2023-03-17: 0 of 14 filed in the sector they moved to", output.ToString(), StringComparison.Ordinal);

        // The revenue pull takes no date, and a pull handed no feed for itself writes nothing and fails.
        Assert.Equal(1, await HistoryPull.RunAsync(["--revenue"], () => throw new InvalidOperationException("unasked"), PullClock(), store.DatabaseFile, output, error));
        Assert.Equal(1, await HistoryPull.RunAsync(["--splits", "--from", "2026-07-01"], () => throw new InvalidOperationException("unasked"), PullClock(), store.DatabaseFile, output, error));
        Assert.Contains("this command was handed no revenue feed to ask.", error.ToString(), StringComparison.Ordinal);
        Assert.Contains("this command was handed no splits feed to ask.", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(["history-pull-companies"], FamilyRows(store, "SELECT stage FROM run_log;"));
    }
}
