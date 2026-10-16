using System.Globalization;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Insiders;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Insiders;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.13: the insiders' trades read from their Form 4 filings, each figure worked by hand. Each
// captured filing parsed to the rows its document states; the data sets' cut read as the archive sent it, to the two
// places it states; awards and withholding in neither total; a filing on or after a session and one before its ninety
// days read by no reading of it over a constructed store; Keysight's four readings on the fixture's session worked by
// hand from the cut; an amendment standing in its original's place in every figure and one holding no row of the table
// of shares leaving its original standing; the history and the archive's filings compared field by field, a difference
// past two places named apart; and a filing the reader cannot read named on the step's row with its day still read.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public partial class FixtureExpectations
{
    // The rows 19.13 adds that this check reaches: section 17's row and section 18's.
    internal static readonly string[] InsiderClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Insiders' trades"),
        CheckReach.Key(Scope.FailureTable, "An insider's filing the refresh cannot read"),
    ];

    static readonly DateOnly InsiderSession = new(2026, 9, 8);

    static InsiderFiling CapturedForm(string accession) =>
        SecEdgarOwnership.Parse(File.ReadAllText(Path.Combine(Folder(), $"ownership-{accession}.txt")));

    // A constructed Form 4 or 4/A as the archive serves it whole: the header's accession and day filed, then the ownership
    // document between <XML> and </XML> in the captured filings' shape, each holding direct with 1000 shares after it.
    internal static string Submission(
        string accession,
        string filed,
        string issuerCik,
        string symbol,
        (string Cik, string Name, bool Director, bool Officer, string? Title)[] owners,
        (string Traded, string Code, string Shares, string? Price, bool Acquired)[] trades,
        string form = "4",
        string? box = "0",
        string? original = null)
    {
        var xml = new StringBuilder();

        xml.Append("<?xml version=\"1.0\"?>\n<ownershipDocument>\n<schemaVersion>X0508</schemaVersion>\n");
        xml.Append("<documentType>").Append(form).Append("</documentType>\n");
        xml.Append(original is null ? string.Empty : "<dateOfOriginalSubmission>" + original + "</dateOfOriginalSubmission>\n");
        xml.Append("<issuer>\n<issuerCik>").Append(issuerCik).Append("</issuerCik>\n<issuerName>A COMPANY</issuerName>\n<issuerTradingSymbol>").Append(symbol).Append("</issuerTradingSymbol>\n</issuer>\n");

        foreach (var owner in owners)
        {
            xml.Append("<reportingOwner>\n<reportingOwnerId>\n<rptOwnerCik>").Append(owner.Cik).Append("</rptOwnerCik>\n<rptOwnerName>").Append(owner.Name).Append("</rptOwnerName>\n</reportingOwnerId>\n");
            xml.Append("<reportingOwnerRelationship>\n<isDirector>").Append(owner.Director ? '1' : '0').Append("</isDirector>\n<isOfficer>").Append(owner.Officer ? '1' : '0').Append("</isOfficer>\n");
            xml.Append(owner.Title is null ? string.Empty : "<officerTitle>" + owner.Title + "</officerTitle>\n");
            xml.Append("</reportingOwnerRelationship>\n</reportingOwner>\n");
        }

        xml.Append(box is null ? string.Empty : "<aff10b5One>" + box + "</aff10b5One>\n");
        xml.Append("<nonDerivativeTable>\n");

        foreach (var trade in trades)
        {
            xml.Append("<nonDerivativeTransaction>\n<securityTitle>\n<value>Common Stock</value>\n</securityTitle>\n");
            xml.Append("<transactionDate>\n<value>").Append(trade.Traded).Append("</value>\n</transactionDate>\n");
            xml.Append("<transactionCoding>\n<transactionFormType>4</transactionFormType>\n<transactionCode>").Append(trade.Code).Append("</transactionCode>\n<equitySwapInvolved>0</equitySwapInvolved>\n</transactionCoding>\n");
            xml.Append("<transactionAmounts>\n<transactionShares>\n<value>").Append(trade.Shares).Append("</value>\n</transactionShares>\n");
            xml.Append(trade.Price is null
                ? "<transactionPricePerShare>\n<footnoteId id=\"F1\"/>\n</transactionPricePerShare>\n"
                : "<transactionPricePerShare>\n<value>" + trade.Price + "</value>\n</transactionPricePerShare>\n");
            xml.Append("<transactionAcquiredDisposedCode>\n<value>").Append(trade.Acquired ? 'A' : 'D').Append("</value>\n</transactionAcquiredDisposedCode>\n</transactionAmounts>\n");
            xml.Append("<postTransactionAmounts>\n<sharesOwnedFollowingTransaction>\n<value>1000</value>\n</sharesOwnedFollowingTransaction>\n</postTransactionAmounts>\n");
            xml.Append("<ownershipNature>\n<directOrIndirectOwnership>\n<value>D</value>\n</directOrIndirectOwnership>\n</ownershipNature>\n</nonDerivativeTransaction>\n");
        }

        xml.Append("</nonDerivativeTable>\n</ownershipDocument>");

        return "<SEC-DOCUMENT>" + accession + ".txt : " + filed + "\n<SEC-HEADER>" + accession + ".hdr.sgml : " + filed + "\n"
            + "ACCESSION NUMBER:\t\t" + accession + "\nCONFORMED SUBMISSION TYPE:\t" + form + "\nPUBLIC DOCUMENT COUNT:\t\t1\n"
            + "FILED AS OF DATE:\t\t" + filed + "\n</SEC-HEADER>\n<DOCUMENT>\n<TYPE>" + form + "\n<SEQUENCE>1\n<TEXT>\n<XML>\n"
            + xml + "\n</XML>\n</TEXT>\n</DOCUMENT>\n</SEC-DOCUMENT>\n";
    }

    // A filing's transactions as the readers read them back from the store, its owners as one insider.
    static IReadOnlyList<InsiderRow> RowsOf(InsiderFiling filing, string? amends = null) =>
    [
        .. filing.Transactions.Select(row => new InsiderRow(
            filing.Accession,
            filing.Filed,
            filing.Form,
            amends,
            filing.OriginalFiled,
            filing.OwnerKey,
            string.Join("; ", filing.Owners.Select(owner => owner.Name)),
            filing.Owners.Any(owner => owner.Director),
            filing.Owners.Any(owner => owner.Officer),
            filing.Owners.Select(owner => owner.Title).FirstOrDefault(title => title is not null),
            filing.Owners.Any(owner => owner.TenPercent),
            row.Sequence,
            row.Traded,
            row.Code,
            row.Shares,
            row.Price,
            row.Acquired,
            filing.Planned,
            row.Direct)),
    ];

    // Every weekday a year before and after the fixture's session held, so a figure is read wherever its filings fall.
    static readonly InsiderCoverage Whole = new([(new DateOnly(2025, 1, 1), new DateOnly(2027, 1, 1))]);

    // The dollars themselves, so a reading's figure is the dollars it read before any value divides them.
    static double? Dollars(decimal dollars) => Statistic.FromPrice(dollars);

    [Fact]
    public void EachCapturedFormFourParsesToTheRowsItsDocumentStates()
    {
        // Vistra's chief executive buying twice in the open market through a partnership, worked by hand from the document:
        // 2200 shares at 135.99 on 2026-08-31 leaving 1,141,887 held, and 4465 at 135.25 on 2026-09-01 leaving 1,146,352,
        // each held indirectly; the filing ticks no plan's box.
        var purchase = CapturedForm("0001268406-26-000009");
        const string partnership = "By JAMEB, LP, a limited partnership jointly owned by Reporting Person and his spouse";

        Assert.Equal(
            ("0001268406-26-000009", new DateOnly(2026, 9, 2), "4", "0001692819", (string?)"VST", (DateOnly?)null, (bool?)false),
            (purchase.Accession, purchase.Filed, purchase.Form, purchase.IssuerCik, purchase.Symbol, purchase.OriginalFiled, purchase.Planned));
        Assert.Equal(new InsiderOwner("0001268406", "BURKE JAMES A", true, true, "President and CEO", false), Assert.Single(purchase.Owners));
        Assert.Equal(
            [
                new InsiderTransaction(1, "Common Stock", new DateOnly(2026, 8, 31), "P", 2200m, 135.99m, true, 1141887m, false, partnership),
                new InsiderTransaction(2, "Common Stock", new DateOnly(2026, 9, 1), "P", 4465m, 135.25m, true, 1146352m, false, partnership),
            ],
            purchase.Transactions);

        // Microsoft's chief executive selling eight times on 2026-09-01 under a Rule 10b5-1 plan, the box written 1: 86,525
        // shares for 43,388,532.4067 dollars, the first 1040 at 498.2396 and the last 1360 at 505.1997, in the document's order.
        var plan = CapturedForm("0000789019-26-000161");

        Assert.Equal((bool?)true, plan.Planned);
        Assert.Equal(8, plan.Transactions.Count);
        Assert.All(plan.Transactions, row => Assert.Equal(("S", false, true), (row.Code, row.Acquired, row.Direct)));
        Assert.Equal((1040m, 498.2396m, 572247.534m), (plan.Transactions[0].Shares, plan.Transactions[0].Price!.Value, plan.Transactions[0].HeldAfter!.Value));
        Assert.Equal((1360m, 505.1997m, 486762.534m), (plan.Transactions[7].Shares, plan.Transactions[7].Price!.Value, plan.Transactions[7].HeldAfter!.Value));
        Assert.Equal((86525m, 43388532.4067m), (plan.Transactions.Sum(row => row.Shares), plan.Transactions.Sum(row => row.Shares * row.Price!.Value)));

        // An officer's award of 29,724 shares at no price and the 18,080.548 withheld for its tax at 513.53, the box written 0.
        var award = CapturedForm("0000789019-26-000153");

        Assert.Equal(
            [("A", 29724m, 0m, true), ("F", 18080.548m, 513.53m, false)],
            award.Transactions.Select(row => (row.Code, row.Shares, row.Price!.Value, row.Acquired)));
        Assert.Equal((bool?)false, award.Planned);

        // Apple's filing holding only the derivatives' table reads no transaction, its box written false read as no plan;
        // its sale whose box is written true reads as planned.
        var derivatives = CapturedForm("0001140361-26-035362");

        Assert.Empty(derivatives.Transactions);
        Assert.Equal((bool?)false, derivatives.Planned);
        Assert.Equal((bool?)true, CapturedForm("0001140361-26-035636").Planned);

        // Procter & Gamble's amendment states its original's day and not its accession, and restates the sale's day.
        var amendment = CapturedForm("0000080424-26-000160");

        Assert.Equal(("4/A", (DateOnly?)new DateOnly(2026, 8, 24), new DateOnly(2026, 8, 20)), (amendment.Form, amendment.OriginalFiled, Assert.Single(amendment.Transactions).Traded));

        // The archive's path for a filing under the CIK its index line names, a document no reader can read refused, and
        // each way a box or a flag is written read.
        Assert.Equal("Archives/edgar/data/320193/0001140361-26-035362.txt", SecEdgarOwnership.PathOf("0000320193", "0001140361-26-035362"));
        Assert.Throws<FormatException>(() => SecEdgarOwnership.Parse("ACCESSION NUMBER:\t\t0000000001-26-000001\nFILED AS OF DATE:\t\t20261001\n<TEXT>no document</TEXT>"));
        Assert.Throws<FormatException>(() => SecEdgarOwnership.Truth("yes"));
        Assert.Equal([true, true, false, false], new[] { "1", "true", "0", "false" }.Select(SecEdgarOwnership.Truth));
    }

    [Fact]
    public async Task TheDataSetsCutIsReadAsTheArchiveSentItToTheTwoPlacesItStates()
    {
        var feed = RecordedInsiderDataSetFeed.FromFolder(Folder());
        var quarters = SecEdgarInsiderDataSet.Quarters((await feed.ListingAsync())!);

        // The listing's six quarters, each linked under the folder its quarter was posted in.
        Assert.Equal(["2006q1", "2017q4", "2018q1", "2026q1", "2026q2", "2026q3"], quarters.Keys);
        Assert.Equal("files/datastandardsinnovation/data/insider-transactions-data-sets/2026q3_form345.zip", quarters["2026q3"]);
        Assert.Equal("files/structureddata/data/insider-transactions-data-sets/2018q1_form345.zip", quarters["2018q1"]);
        Assert.Equal((new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30)), SecEdgarInsiderDataSet.Span("2026q3"));

        var issuers = new HashSet<string>(["0000789019", "0000080424", "0000027904", "0001692819"], StringComparer.Ordinal);

        using var zip = new MemoryStream((await feed.QuarterAsync(quarters["2026q3"]))!);

        var filings = SecEdgarInsiderDataSet.Read(zip, issuers);

        // Microsoft's 44 Form 4s and Vistra's, Procter & Gamble's and Delta's pairs, and nothing of an issuer not asked for or
        // of a Form 3 or 5.
        Assert.Equal(44 + 1 + 2 + 2, filings.Count);
        Assert.All(filings, filing => Assert.Contains(filing.Form, InsiderFiling.Forms));
        Assert.All(filings, filing => Assert.Contains(filing.IssuerCik, issuers));

        // The withholding its filing states to three places held to two, and the award beside it in the filing's order.
        var award = filings.Single(filing => filing.Accession == "0000789019-26-000153");

        Assert.Equal([("A", 29724m), ("F", 18080.55m)], award.Transactions.Select(row => (row.Code, row.Shares)));
        Assert.Equal((bool?)false, award.Planned);

        // The chief executive's eight sales in the filing's order and not the file's, each price to two places, his
        // relationship's comma list read as a director and an officer, and the box written 1.
        var plan = filings.Single(filing => filing.Accession == "0000789019-26-000161");

        Assert.Equal([1040m, 5080m, 24723m, 32901m, 4520m, 7067m, 9834m, 1360m], plan.Transactions.Select(row => row.Shares));
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], plan.Transactions.Select(row => row.Sequence));
        Assert.Equal(498.24m, plan.Transactions[0].Price);
        Assert.Equal(new InsiderOwner("0001513142", "Nadella Satya", true, true, "Chief Executive Officer", false), Assert.Single(plan.Owners));
        Assert.Equal((bool?)true, plan.Planned);

        // The amendment's original day read, and Delta's amendment holding no row of the table of shares read with none.
        Assert.Equal((DateOnly?)new DateOnly(2026, 8, 24), filings.Single(filing => filing.Accession == "0000080424-26-000160").OriginalFiled);
        Assert.Empty(filings.Single(filing => filing.Accession == "0001683168-26-006059").Transactions);

        // Vistra's purchase as the filing states it: the data set agrees with the document to the cent.
        Assert.Equal(
            [("P", 2200m, (decimal?)135.99m, false), ("P", 4465m, (decimal?)135.25m, false)],
            filings.Single(filing => filing.Accession == "0001268406-26-000009").Transactions.Select(row => (row.Code, row.Shares, row.Price, row.Direct)));
    }

    [Fact]
    public void AwardsAndWithholdingAreCountedInNeitherTotalAndAPurchasesBuyerIsCountedOnce()
    {
        var night = new DateOnly(2026, 9, 2);

        // The award and its withholding: drawn as neither a purchase nor a sale, counted on the region's last line, and
        // in neither total nor any reading.
        var award = RowsOf(CapturedForm("0000789019-26-000153"));
        var view = InsiderView.Of(award, Whole, night)!;

        Assert.Empty(view.Lines);
        Assert.Equal((0m, 0m, 0, 0), (view.Year.Bought, view.Year.Sold, view.Year.Buyers, view.Year.Sellers));
        Assert.Equal((1, 0, 1, 0), (view.Awards, view.Exercises, view.Withholding, view.Others));
        Assert.Equal(new InsiderFour(0, 0, 0, 0), InsiderReadings.Read(award, Whole, night.AddDays(1), Dollars));

        // Vistra's chief executive's two purchases: one buyer, an officer, 2200 at 135.99 and 4465 at 135.25, 903,069.25
        // dollars, the newest purchase the second's day traded.
        var purchase = RowsOf(CapturedForm("0001268406-26-000009"));
        var bought = InsiderView.Of(purchase, Whole, night)!;

        Assert.Equal(2, bought.Lines.Count);
        Assert.All(bought.Lines, line => Assert.True(line.Purchase));
        Assert.Equal((903069.25m, 0m, 1), (bought.Ninety.Bought, bought.Ninety.Sold, bought.Ninety.Buyers));
        Assert.Equal((DateOnly?)new DateOnly(2026, 9, 1), bought.NewestPurchase);
        Assert.Equal(new InsiderFour(903069.25, 1, 1, 0), InsiderReadings.Read(purchase, Whole, night.AddDays(1), Dollars));

        // The chief executive's sales under a plan: sold, and none of it unplanned.
        var plan = RowsOf(CapturedForm("0000789019-26-000161"));
        var sold = InsiderReadings.Read(plan, Whole, night.AddDays(1), Dollars);

        Assert.Equal(-43388532.4067, sold.NetBuy!.Value, 4);
        Assert.Equal((0.0, 0.0, 0.0), (sold.Buyers!.Value, sold.OfficerBuy!.Value, sold.UnplannedSale!.Value));
        Assert.All(InsiderView.Of(plan, Whole, night)!.Lines, line => Assert.True(line.Planned));
    }

    [Fact]
    public void TheReadingsAreNoneWhereTheHistoryDoesNotHoldTheirDaysAndTheBoxNoneBeforeItsDay()
    {
        var session = new DateOnly(2026, 9, 8);
        var purchase = RowsOf(CapturedForm("0001268406-26-000009"));

        // For the Tuesday session after Labor Day: spans ending on the Friday before, the Monday eve not held, read none, as
        // does a store holding no span; spans from the ninetieth day before to the eve read a figure, and spans starting a
        // day later, the ninetieth day not held, read none.
        Assert.Equal(InsiderFour.None, InsiderReadings.Read(purchase, new InsiderCoverage([(new DateOnly(2026, 6, 1), new DateOnly(2026, 9, 4))]), session, Dollars));
        Assert.Equal(InsiderFour.None, InsiderReadings.Read(purchase, InsiderCoverage.None, session, Dollars));
        Assert.NotEqual(InsiderFour.None, InsiderReadings.Read(purchase, new InsiderCoverage([(new DateOnly(2026, 6, 10), new DateOnly(2026, 9, 7))]), session, Dollars));
        Assert.Equal(InsiderFour.None, InsiderReadings.Read(purchase, new InsiderCoverage([(new DateOnly(2026, 6, 11), new DateOnly(2026, 9, 7))]), session, Dollars));

        // The box joined the form on 2023-04-01: a session whose ninety days reach the day before it reads no unplanned
        // sale, and the session a day later reads one, while the other three are read on both.
        var one = Assert.Single(RowsOf(CapturedForm("0000080424-26-000158")));
        InsiderRow[] sale = [one with { Filed = new DateOnly(2023, 5, 1), Traded = new DateOnly(2023, 4, 28) }];
        var early = new InsiderCoverage([(new DateOnly(2023, 1, 1), new DateOnly(2023, 12, 31))]);
        var before = InsiderReadings.Read(sale, early, new DateOnly(2023, 6, 29), Dollars);
        var after = InsiderReadings.Read(sale, early, new DateOnly(2023, 6, 30), Dollars);

        Assert.Null(before.UnplannedSale);
        Assert.Equal(-438990.87, before.NetBuy!.Value, 6);
        Assert.Equal(438990.87, after.UnplannedSale!.Value, 6);
        Assert.Equal(new DateOnly(2023, 4, 1), InsiderReadings.PlanBoxFrom);
        Assert.Equal(90, InsiderReadings.WindowDays);
    }

    [Fact]
    public void AnAmendmentStandsInItsOriginalsPlaceInEveryFigureAndOneHoldingNoRowOfTheTableOfSharesLeavesItStanding()
    {
        var night = new DateOnly(2026, 9, 1);
        var original = RowsOf(CapturedForm("0000080424-26-000158"));
        var amendment = RowsOf(CapturedForm("0000080424-26-000160"), amends: "0000080424-26-000158");

        // Procter & Gamble's sale of 3053 at 143.79, 438,990.87 dollars, filed 2026-08-24 as traded on 2026-08-19 and amended
        // on 2026-08-27 to 2026-08-20: read once, from the amendment, on the original's day, in the readings and the region.
        var both = original.Concat(amendment).ToArray();
        var effect = InsiderReadings.InEffect(both, night.AddDays(1));

        Assert.Equal([(new DateOnly(2026, 8, 24), "0000080424-26-000160")], effect.Select(one => (one.Day, one.Row.Accession)));
        Assert.Equal(-438990.87, InsiderReadings.Read(both, Whole, night.AddDays(1), Dollars).NetBuy!.Value, 6);

        var view = InsiderView.Of(both, Whole, night)!;

        Assert.Equal((new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 24)), (Assert.Single(view.Lines).Traded, view.Lines[0].Filed));
        Assert.Equal((438990.87m, 1), (view.Year.Sold, view.Year.Sellers));

        // A session before the amendment was filed reads the original as it stood.
        Assert.Equal([(new DateOnly(2026, 8, 24), "0000080424-26-000158")], InsiderReadings.InEffect(both, new DateOnly(2026, 8, 26)).Select(one => (one.Day, one.Row.Accession)));

        // An amendment matched to no original stands on its own rows and its own day.
        Assert.Equal([new DateOnly(2026, 8, 27)], InsiderReadings.InEffect(RowsOf(CapturedForm("0000080424-26-000160")), night.AddDays(1)).Select(one => one.Day));

        // Delta's amendment corrects its derivatives' table alone and holds no row of the table of shares, so it stores
        // nothing and the original's exercise and sale stand: 35,000 sold at 92.719, 3,245,165 dollars.
        Assert.Empty(CapturedForm("0001683168-26-006059").Transactions);

        var delta = RowsOf(CapturedForm("0001683168-26-006001"));

        Assert.Equal(["M", "S"], InsiderReadings.InEffect(delta, night.AddDays(1)).Select(one => one.Row.Code));
        Assert.Equal(-3245165.0, InsiderReadings.Read(delta, Whole, night.AddDays(1), Dollars).NetBuy!.Value, 6);
    }

    [Fact]
    public async Task TheStoreMatchesAnAmendmentToItsOneOriginalByIssuerOwnersAndDay()
    {
        using var store = new TemporaryStore().Migrated();
        await using var connection = store.Open();

        // The original stored first, then its amendment, which names it; then an amendment of a filing the store does not
        // hold, which names none; and Delta's amendment, which stores no row at all.
        Assert.Equal(1, await InsiderTrades.StoreAsync(connection, null, CapturedForm("0000080424-26-000158"), InsiderTrades.FilingSource, "run-a", CancellationToken.None));
        Assert.Equal(1, await InsiderTrades.StoreAsync(connection, null, CapturedForm("0000080424-26-000160"), InsiderTrades.FilingSource, "run-a", CancellationToken.None));
        Assert.Equal(0, await InsiderTrades.StoreAsync(connection, null, CapturedForm("0001683168-26-006059"), InsiderTrades.FilingSource, "run-a", CancellationToken.None));

        var lonely = CapturedForm("0000080424-26-000160") with { Accession = "0000080424-26-000999", OriginalFiled = new DateOnly(2026, 8, 25) };

        Assert.Equal(1, await InsiderTrades.StoreAsync(connection, null, lonely, InsiderTrades.FilingSource, "run-a", CancellationToken.None));

        // A second original by the same owner on the same day, and then an amendment stating that day: matched to two, it
        // names none.
        var twin = CapturedForm("0000080424-26-000158") with { Accession = "0000080424-26-000777" };
        var ambiguous = CapturedForm("0000080424-26-000160") with { Accession = "0000080424-26-000888" };

        Assert.Equal(1, await InsiderTrades.StoreAsync(connection, null, twin, InsiderTrades.FilingSource, "run-a", CancellationToken.None));
        Assert.Equal(1, await InsiderTrades.StoreAsync(connection, null, ambiguous, InsiderTrades.FilingSource, "run-a", CancellationToken.None));

        Assert.Equal(
            ["0000080424-26-000158|4||", "0000080424-26-000160|4/A|0000080424-26-000158|2026-08-24", "0000080424-26-000777|4||", "0000080424-26-000888|4/A||2026-08-24", "0000080424-26-000999|4/A||2026-08-25"],
            Query(store, "SELECT accession || '|' || form || '|' || COALESCE(amends, '') || '|' || COALESCE(original_filed, '') FROM insider_trade ORDER BY accession;"));

        // A stored filing is not stored twice.
        Assert.Equal(0, await InsiderTrades.StoreAsync(connection, null, CapturedForm("0000080424-26-000158"), InsiderTrades.FilingSource, "run-b", CancellationToken.None));
        Assert.Equal(["5"], Query(store, "SELECT COUNT(*) FROM insider_trade;"));
    }

    [Fact]
    public async Task AFilingOnOrAfterTheSessionAndOneBeforeItsNinetyDaysAreReadByNoReadingOfIt()
    {
        using var store = new TemporaryStore().Migrated();

        // A member valued at 100 a share on 10,000,000 shares, a billion dollars, its count filed before the session and its
        // closes as traded, with the night's setup and its member readings stored and the history holding the year.
        var session = new DateOnly(2026, 9, 8);
        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute("INSERT INTO company (ticker, fetched_at, cik) VALUES ('INS', '2026-01-02T00:00:00.000Z', '0000000201');");
        store.Execute(
            "INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES "
            + "('0000000201', 'EntityCommonStockSharesOutstanding', '2026-07-31', '2026-07-31', '10000000', '2026-08-03', '10-Q', 'x', 'refresh');");

        foreach (var day in EquityBrief.Core.Bars.ExchangeClosures.SessionsBetween(new DateOnly(2026, 6, 30), session.AddDays(1)))
        {
            store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('INS', '{Day(day)}', '100', '100', '100', '100', 1000, 'eod', '2026-09-08T23:00:00Z', '100');");
        }

        store.Execute(
            "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin) "
            + $"VALUES ('GSPC', 'pullback', 'INS', '{Day(session)}', 'the live rule', 0, '100', '95', 21, 'open', 0, 'night', '{LedgerReadings.Version}');");
        store.Execute(
            "INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES "
            + $"('GSPC', '{Day(session)}', 'INS', '{JsonSerializer.Serialize(new double?[LedgerReadings.Count])}', '{LedgerReadings.Version}');");
        store.Execute("INSERT INTO insider_read (first_day, last_day, source, run_id, read_at, filings, stored) VALUES ('2026-01-01', '2026-09-30', 'data-set', 'history', '2026-10-16T00:00:00Z', 0, 0);");

        // Five purchases of 1,000 shares at 100, a hundred thousand dollars each, by five insiders, filed the day before
        // the session, on the ninetieth day before it, on the ninety-first, on the session and on the day after it: the
        // first two read, the others not.
        await using (var connection = store.Open())
        {
            foreach (var (filed, owner, officer) in new[] { ("20260907", "0000009001", true), ("20260610", "0000009002", false), ("20260609", "0000009003", true), ("20260908", "0000009004", true), ("20260909", "0000009005", true) })
            {
                var submission = Submission($"0000009999-26-{owner[^3..]}000", filed, "0000000201", "INS", [(owner, "AN INSIDER " + owner[^1], false, officer, officer ? "Chief Officer" : null)], [("2026-06-01", "P", "1000", "100", true)]);

                await InsiderTrades.StoreAsync(connection, null, SecEdgarOwnership.Parse(submission), InsiderTrades.FilingSource, "constructed", CancellationToken.None);
            }
        }

        var ledger = new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile);

        Assert.Equal(2, await ledger.InsidersAgainAsync(session));

        // Two hundred thousand dollars over a billion, two buyers, an officer among them, and no sale.
        Assert.Equal(
            ["0.0002|2.0|1.0|0.0"],
            Query(store, "SELECT ROUND(insider_net_buy, 10) || '|' || insider_buyers || '|' || insider_officer_buy || '|' || insider_unplanned_sale FROM setup;"));

        var readings = JsonSerializer.Deserialize<double?[]>(Query(store, "SELECT readings FROM ledger_reading;").Single())!;

        Assert.Equal(
            (0.0002, 2.0, 1.0, 0.0),
            (Math.Round(readings[LedgerReadings.IndexOf("insider_net_buy")]!.Value, 10), readings[LedgerReadings.IndexOf("insider_buyers")]!.Value, readings[LedgerReadings.IndexOf("insider_officer_buy")]!.Value, readings[LedgerReadings.IndexOf("insider_unplanned_sale")]!.Value));

        // With the history's span taken away the four read none.
        store.Execute("DELETE FROM insider_read;");

        await ledger.InsidersAgainAsync(session);

        Assert.Equal(["|||"], Query(store, "SELECT COALESCE(insider_net_buy, '') || '|' || COALESCE(insider_buyers, '') || '|' || COALESCE(insider_officer_buy, '') || '|' || COALESCE(insider_unplanned_sale, '') FROM setup;"));
    }

    [Fact]
    public async Task KeysightsFourReadingsOnTheFixturesSessionAreWorkedByHandFromTheCut()
    {
        var expected = Expected("insiders");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["insider_trade", "insider_read"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!).ToArray());

        using var store = await FixtureReplay.ReplayedAsync();

        // The two quarters the history read as spans of their own, each a quarter's first and last day, beside the days
        // the refresh read, and the four companies the store knows a filer of, no other.
        var quarters = expected.GetProperty("quartersRead").EnumerateArray().Select(quarter => SecEdgarInsiderDataSet.Span(quarter.GetString()!)).ToArray();

        Assert.Equal(
            quarters.Select(span => FormattableString.Invariant($"{span.First.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|{span.Last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}|data-set")),
            expected.GetProperty("spansRead").EnumerateArray().Select(span => span.GetString()!));
        Assert.Equal(
            expected.GetProperty("spansRead").EnumerateArray().Select(span => span.GetString()!).ToArray(),
            Query(store, "SELECT first_day || '|' || last_day || '|' || source FROM insider_read WHERE source = 'data-set' ORDER BY first_day;"));
        Assert.Equal(
            expected.GetProperty("issuersStored").EnumerateArray().Select(cik => cik.GetString()!).ToArray(),
            Query(store, "SELECT DISTINCT cik FROM insider_trade ORDER BY cik;"));
        Assert.Equal(
            ["2026-09-01|filing", "2026-09-02|filing", "2026-09-03|filing", "2026-09-04|filing", "2026-09-07|filing"],
            Query(store, "SELECT first_day || '|' || source FROM insider_read WHERE source = 'filing' ORDER BY first_day;"));

        // The window worked by the suite from the cut itself: Keysight's Forms 4 filed from ninety days before the session
        // to the day before it, each sale and the ones whose filing ticks no box, read off the cut's own files.
        var session = DateOnly.Parse(expected.GetProperty("session").GetString()!, CultureInfo.InvariantCulture);
        var ticker = expected.GetProperty("ticker").GetString()!;
        var from = session.AddDays(-InsiderReadings.WindowDays);

        Assert.Equal(expected.GetProperty("windowFrom").GetString(), from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var (sales, unplanned, purchases) = (0m, 0m, 0);

        foreach (var quarter in new[] { "2026q2", "2026q3" })
        {
            var folder = Path.Combine(Folder(), "insider-data-set-" + quarter);
            var submissions = File.ReadAllLines(Path.Combine(folder, "SUBMISSION.tsv")).Skip(1).Select(line => line.Split('\t'))
                .Where(fields => fields[11] == ticker && fields[8] == "4")
                .Select(fields => (Accession: fields[0], Filed: DateOnly.ParseExact(fields[1], "dd-MMM-yyyy", CultureInfo.InvariantCulture), Box: fields.Length > 13 ? fields[13] : string.Empty))
                .Where(filing => filing.Filed >= from && filing.Filed < session)
                .ToDictionary(filing => filing.Accession, StringComparer.Ordinal);

            foreach (var fields in File.ReadAllLines(Path.Combine(folder, "NONDERIV_TRANS.tsv")).Skip(1).Select(line => line.Split('\t')).Where(fields => submissions.ContainsKey(fields[0])))
            {
                var dollars = decimal.Parse(fields[14], CultureInfo.InvariantCulture) * decimal.Parse(fields[16], CultureInfo.InvariantCulture);

                if (fields[9] == "S")
                {
                    sales += dollars;
                    unplanned += submissions[fields[0]].Box is "1" or "true" ? 0m : dollars;
                }

                purchases += fields[9] == "P" ? 1 : 0;
            }
        }

        // No purchase at all in the window, so no buyer and no officer buying.
        Assert.Equal(decimal.Parse(expected.GetProperty("salesDollars").GetString()!, CultureInfo.InvariantCulture), sales);
        Assert.Equal(decimal.Parse(expected.GetProperty("unplannedSalesDollars").GetString()!, CultureInfo.InvariantCulture), unplanned);
        Assert.Equal((0, 0, 0), (purchases, expected.GetProperty("buyers").GetInt32(), expected.GetProperty("officerBuy").GetInt32()));

        // The company's value as the suite works it from the stored rows: the newest share count Keysight's facts filed
        // before the session for a day before it, times the session's close brought to that count's basis by the close as
        // traded on the count's day against the close at the store's scale that day.
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var count = Query(store,
            "SELECT period_end || '|' || dollars FROM filed_fact WHERE cik = '0001601046' AND concept = 'EntityCommonStockSharesOutstanding' "
            + "AND filed < '" + day + "' AND period_end < '" + day + "' ORDER BY period_end DESC, filed, accession LIMIT 1;").Single().Split('|');
        var countDay = count[0];
        var shares = decimal.Parse(count[1], CultureInfo.InvariantCulture);
        var onCount = Query(store, $"SELECT close || '|' || raw_close FROM bar WHERE ticker = '{ticker}' AND session_date <= '{countDay}' ORDER BY session_date DESC LIMIT 1;").Single().Split('|');
        var atSession = decimal.Parse(Query(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' AND session_date = '" + day + "';").Single(), CultureInfo.InvariantCulture);
        var value = shares * decimal.Parse(onCount[1], CultureInfo.InvariantCulture) * atSession / decimal.Parse(onCount[0], CultureInfo.InvariantCulture);

        // Read back off the member's stored readings, read again by the night once the refresh had stored the session's eve.
        var readings = JsonSerializer.Deserialize<double?[]>(Query(store, $"SELECT readings FROM ledger_reading WHERE ticker = '{ticker}' AND session_date = '" + day + "';").Single())!;

        Assert.Equal(Statistic.FromRatio(-sales / value), readings[LedgerReadings.IndexOf("insider_net_buy")]!.Value, 12);
        Assert.Equal(Statistic.FromRatio(unplanned / value), readings[LedgerReadings.IndexOf("insider_unplanned_sale")]!.Value, 12);
        Assert.Equal((double)expected.GetProperty("buyers").GetInt32(), readings[LedgerReadings.IndexOf("insider_buyers")]);
        Assert.Equal((double)expected.GetProperty("officerBuy").GetInt32(), readings[LedgerReadings.IndexOf("insider_officer_buy")]);
    }

    [Fact]
    public async Task TheHistoryAndTheArchivesFilingsAreComparedFieldByFieldAndADifferencePastTwoPlacesIsNamedApart()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var stored = Query(store, "SELECT COUNT(*) FROM insider_trade;").Single();
        var feed = RecordedFilingsRefreshFeed.FromFolder(Folder());
        var output = new StringWriter();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        var compared = await new InsiderTrades(clock, store.DatabaseFile, (_, _) => Task.CompletedTask)
            .DaysAsync(feed, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 4), compare: true, "insiders-compare", output);

        // The four members' filings the history holds rows of read again, the three holding no row of the table of shares
        // passed over: Apple's sale equal to the cent; Microsoft's withholding of 18,080.548 held as 18,080.55, its chief
        // executive's prices of 498.2396 and the rest held to two places, and each filing's shares held after to two,
        // every one named as a difference past two places and none as any other.
        Assert.Equal((4, 4, 1), (compared.Spans, compared.Asked, compared.Equal));
        Assert.Equal(
            ["held after past two places|3", "price past two places|1", "shares past two places|1"],
            compared.Differences.Select(pair => FormattableString.Invariant($"{pair.Key}|{pair.Value}")));
        Assert.Contains(compared.Named, words => words == "0000789019-26-000153 shares: '18080.548' read, '18080.55' stored");
        Assert.Contains(compared.Named, words => words == "0000789019-26-000161 price: '498.2396' read, '498.24' stored");
        Assert.Equal(
            ["0000789019-26-000153", "0000789019-26-000154", "0000789019-26-000161", "0001140361-26-035636"],
            feed.Asked.Where(asked => asked.Length == 20).Order(StringComparer.Ordinal));

        // Comparing writes nothing, its own run log row among it, and prints what it found.
        Assert.Equal([stored], Query(store, "SELECT COUNT(*) FROM insider_trade;"));
        Assert.Empty(Query(store, "SELECT detail FROM run_log WHERE run_id = 'insiders-compare';"));
        Assert.Contains("fields that differed: held after past two places on 3 filing(s), price past two places on 1 filing(s), shares past two places on 1 filing(s)", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFilingTheReaderCannotReadIsNamedOnTheStepsRowWithWhyAndItsDayStillRead()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'EEE', '2020-01-02', NULL, '2026-09-01T23:30:00Z');");
        store.Execute("INSERT INTO company (ticker, fetched_at, cik, sector) VALUES ('EEE', '2026-09-01T23:40:00.000Z', '0000000105', 'Industrials');");

        var tuesday = new DateOnly(2026, 10, 6);

        // On the Tuesday the member's insiders file two Form 4s: one the reader reads, a purchase, and one whose document
        // carries no ownership document between its markers.
        var feed = RecordedFilingsRefreshFeed.Of(
            new Dictionary<DateOnly, string>
            {
                [tuesday] = NightlyCost.DayIndex(tuesday, ("0000000105", "EEE INC", "4", "0000000105-26-000001"), ("0000000105", "EEE INC", "4", "0000000105-26-000002")),
            },
            ownerships: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000105-26-000001"] = Submission("0000000105-26-000001", "20261006", "0000000105", "EEE", [("0000009101", "A BUYER", true, false, null)], [("2026-10-05", "P", "100", "50", true)]),
                ["0000000105-26-000002"] = "ACCESSION NUMBER:\t\t0000000105-26-000002\nFILED AS OF DATE:\t\t20261006\n<TEXT>no ownership document</TEXT>\n",
            });

        var night = FixedClock.At(new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FilingsRefresher(feed, night, store.DatabaseFile, pause: (_, _) => Task.CompletedTask).NightAsync("GSPC", "night-insiders");

        // Both asked, the purchase stored, the other named with why, the day read and recorded whole, and the row partial
        // in nothing.
        Assert.Equal((2, 1), (outcome.Ownerships, outcome.Trades));
        Assert.Contains(outcome.Unread!, line => line.StartsWith("0000000105-26-000002: ", StringComparison.Ordinal) && line.Contains("carries no ownership document", StringComparison.Ordinal));
        Assert.Contains("2026-10-06", Query(store, "SELECT day FROM filing_day WHERE posted = 1;"));
        Assert.Contains("2026-10-06|filing|2|1", Query(store, "SELECT first_day || '|' || source || '|' || filings || '|' || stored FROM insider_read;"));
        Assert.Equal(["0000000105-26-000001|P|100|50"], Query(store, "SELECT accession || '|' || code || '|' || shares || '|' || price FROM insider_trade;"));

        var row = Query(store, $"SELECT outcome || '|' || detail FROM run_log WHERE run_id = 'night-insiders' AND stage = '{FilingsRefresher.Stage}';").Single();

        Assert.StartsWith("ok|", row, StringComparison.Ordinal);
        Assert.Contains("0000000105-26-000002", row, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AJointFilingsOwnersInEitherOrderAreOneInsiderAndAnAmendmentStillFindsItsOriginal()
    {
        // From the 19.13 correction of 2026-10-16: the data sets list a joint filing's owners in an order of their own, as
        // Woltosz's of 2026-07-06 lists the wife before the husband the filing names first.
        (string Cik, string Name, bool Director, bool Officer, string? Title) husband = ("0001222526", "WOLTOSZ WALTER S", true, true, "Chief Executive Officer");
        (string Cik, string Name, bool Director, bool Officer, string? Title) wife = ("0001222527", "WOLTOSZ VIRGINIA E", false, false, null);

        var first = SecEdgarOwnership.Parse(Submission("0001222526-26-000101", "20260810", "0001023459", "SLP", [husband, wife], [("2026-08-07", "P", "1000", "20", true)]));
        var second = SecEdgarOwnership.Parse(Submission("0001222526-26-000102", "20260824", "0001023459", "SLP", [wife, husband], [("2026-08-21", "P", "500", "22", true)]));
        var both = RowsOf(first).Concat(RowsOf(second)).ToArray();

        // Two purchases by the same two owners, listed in two orders: one insider, in the readings and in the region.
        Assert.NotEqual(first.OwnerKey, second.OwnerKey);
        Assert.Equal(1.0, InsiderReadings.Read(both, Whole, new DateOnly(2026, 9, 1), Dollars).Buyers);
        Assert.Equal(1, InsiderView.Of(both, Whole, new DateOnly(2026, 8, 31))!.Ninety.Buyers);
        Assert.Equal("0001222526,0001222527", InsiderReadings.Insider(second.OwnerKey));

        // The amendment of the first, its owners listed as the data set lists them, finds its original.
        using var store = new TemporaryStore().Migrated();
        await using var connection = store.Open();
        var amendment = SecEdgarOwnership.Parse(Submission("0001222526-26-000103", "20260812", "0001023459", "SLP", [wife, husband], [("2026-08-07", "P", "1000", "20.5", true)], form: "4/A", original: "2026-08-10"));

        await InsiderTrades.StoreAsync(connection, null, first, InsiderTrades.DataSetSource, "run-a", CancellationToken.None);
        await InsiderTrades.StoreAsync(connection, null, amendment, InsiderTrades.FilingSource, "run-a", CancellationToken.None);

        Assert.Equal(["0001222526-26-000101"], Query(store, "SELECT amends FROM insider_trade WHERE accession = '0001222526-26-000103';"));

        // And the comparison names no difference where the two readers list the same owners in two orders, read back as the
        // comparison reads a filing's stored rows.
        var held = new List<string[]>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT sequence, security, traded, code, shares, price, acquired, COALESCE(held_after, ''), COALESCE(planned, ''), direct, COALESCE(nature, ''), filed, form, COALESCE(original_filed, ''), owners, owner_ciks, director, officer, COALESCE(title, ''), ten_percent, cik FROM insider_trade WHERE accession = '0001222526-26-000101' ORDER BY sequence;";

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                held.Add([.. Enumerable.Range(0, reader.FieldCount).Select(at => Convert.ToString(reader.GetValue(at), CultureInfo.InvariantCulture)!)]);
            }
        }

        var reordered = first with { Owners = [first.Owners[1], first.Owners[0]] };

        Assert.DoesNotContain(InsiderTrades.Compared(reordered, held), one => one.Field is "owners" or "owner ciks");
        Assert.Contains(InsiderTrades.Compared(reordered with { Owners = [first.Owners[0]] }, held), one => one.Field == "owner ciks");
    }

    [Fact]
    public async Task TheDaysReadByHandReadEveryCompanyTheStoreKnowsAsTheHistoryDoes()
    {
        // From the 19.13 correction of 2026-10-16: a company of the S&P 400 the store's membership joins on 2026-10-02,
        // whose insider files on 2026-10-01, is read on that day by hand, as the history reads every company the store
        // knows and as the night of the next session would read it with that session's members; and a company the store
        // knows a filer of and no membership holds is read too.
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'LATE', '2026-10-02', NULL, '2026-10-02T23:30:00Z');");

        foreach (var (ticker, cik) in new[] { ("LATE", "0000000301"), ("KNOWN", "0000000302") })
        {
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik, sector) VALUES ('{ticker}', '2026-10-02T23:40:00.000Z', '{cik}', 'Industrials');");
        }

        var day = new DateOnly(2026, 10, 1);
        var feed = RecordedFilingsRefreshFeed.Of(
            new Dictionary<DateOnly, string>
            {
                [day] = NightlyCost.DayIndex(day, ("0000000301", "LATE INC", "4", "0000000301-26-000001"), ("0000000302", "KNOWN INC", "4", "0000000302-26-000001"), ("0000000999", "UNKNOWN INC", "4", "0000000999-26-000001")),
            },
            ownerships: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000301-26-000001"] = Submission("0000000301-26-000001", "20261001", "0000000301", "LATE", [("0000009301", "A DIRECTOR", true, false, null)], [("2026-09-30", "P", "100", "10", true)]),
                ["0000000302-26-000001"] = Submission("0000000302-26-000001", "20261001", "0000000302", "KNOWN", [("0000009302", "AN OFFICER", false, true, "Chief Financial Officer")], [("2026-09-30", "S", "200", "30", false)]),
            });

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var read = await new InsiderTrades(clock, store.DatabaseFile, (_, _) => Task.CompletedTask).DaysAsync(feed, day, day, compare: false, "insiders-by-hand", TextWriter.Null);

        // Both companies' filings asked and stored, nothing of a filer the store knows no company of, and the day recorded.
        Assert.Equal((1, 2, 2, 2), (read.Spans, read.Filings, read.Asked, read.Stored));
        Assert.Equal(["0000000301", "0000000302"], Query(store, "SELECT DISTINCT cik FROM insider_trade ORDER BY cik;"));
        Assert.Equal(["2026-10-01|filing|2|2"], Query(store, "SELECT first_day || '|' || source || '|' || filings || '|' || stored FROM insider_read;"));
        Assert.DoesNotContain("0000000999-26-000001", feed.Asked);
    }

    [Fact]
    public async Task TheHistoryReadsEachQuarterOnceAndRecordsItOnlyOnceAllOfItIsStored()
    {
        using var store = new TemporaryStore().Migrated();

        foreach (var (ticker, cik) in new[] { ("MSFT", "0000789019"), ("KEYS", "0001601046") })
        {
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-09-01T23:40:00.000Z', '{cik}');");
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var feed = RecordedInsiderDataSetFeed.FromFolder(Folder());
        var first = await new InsiderTrades(clock, store.DatabaseFile, (_, _) => Task.CompletedTask).HistoryAsync(feed, "2026q2", "2026q3", "history-first", TextWriter.Null);

        // The two quarters read, the page and each quarter one request, every Form 4 and 4/A of the two companies stored a
        // row a transaction, and each quarter recorded as a span.
        Assert.Equal((2, 3), (first.Spans, first.Documents));
        Assert.Equal(["insider-data-sets.htm", "2026q2", "2026q3"], feed.Asked);
        Assert.Equal(["2026-04-01|2026-06-30", "2026-07-01|2026-09-30"], Query(store, "SELECT first_day || '|' || last_day FROM insider_read ORDER BY first_day;"));
        Assert.Equal(["0000789019", "0001601046"], Query(store, "SELECT DISTINCT cik FROM insider_trade ORDER BY cik;"));
        Assert.Equal([first.Stored.ToString(CultureInfo.InvariantCulture)], Query(store, "SELECT COUNT(*) FROM insider_trade;"));

        // A second read asks for the page alone and stores nothing.
        var again = RecordedInsiderDataSetFeed.FromFolder(Folder());
        var second = await new InsiderTrades(clock, store.DatabaseFile, (_, _) => Task.CompletedTask).HistoryAsync(again, "2026q2", "2026q3", "history-second", TextWriter.Null);

        Assert.Equal((0, 0), (second.Spans, second.Stored));
        Assert.Equal(["insider-data-sets.htm"], again.Asked);
        Assert.Equal(["insiders-history|ok", "insiders-history|ok"], Query(store, "SELECT stage || '|' || outcome FROM run_log ORDER BY run_id;"));
    }
}
