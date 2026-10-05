using System.Text;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 15.3: membership as it stood, rebuilt from the funds' quarter-end holdings the SEC holds. A
// captured filing and a captured symbol list read as they were sent; the holdings pull over constructed answers storing
// each snapshot and each holding of common stock with the code it matched by ISIN, by name or by neither and naming each
// filing it could not read; a holding a reused ticker would mislead matched to its own security; and a name read as a
// member on both sides of a quarter end its snapshots straddle.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
public partial class FixtureExpectations
{
    [Fact]
    public void AFundsCapturedFilingIsReadWithEachHoldingsCusipIsinAndCategoryAndTheCapturedSymbolsAsSent()
    {
        var folder = Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");
        var snapshot = FundSnapshots.ParseSnapshot(File.ReadAllText(Path.Combine(folder, "phase15-nport-IJR-2019-09-30.xml")), "0001752724-19-177734");

        Assert.Equal((FundSnapshots.Series["SML"], new DateOnly(2019, 9, 30)), (snapshot.Series, snapshot.Period));
        Assert.Equal(3, snapshot.Holdings.Count);
        Assert.Equal(new FiledHolding("Aegion Corp", "00770F104", "US00770F1049", FundSnapshots.CommonEquity), snapshot.Holdings[0]);
        Assert.All(snapshot.Holdings, holding => Assert.Equal(FundSnapshots.CommonEquity, holding.Category));

        // The ISIN the CUSIP makes, worked by ISO 6166's check digit, is the one the fund filed beside it: the country's
        // letters, the nine characters, and the digit that closes them.
        Assert.Equal("US00770F1049", FundSnapshots.FromCusip("00770F104"));
        Assert.Equal("US00770F1049", FundSnapshots.IsinOf(snapshot.Holdings[0] with { Isin = null }));

        var symbols = ProviderSymbols.Parse(File.ReadAllText(Path.Combine(folder, "phase15-symbols-US-delisted.json")), delisted: true);

        Assert.Equal(["AAALY", "AAAP_old", "AABA", "AAAB", "AAAGY"], symbols.Select(symbol => symbol.Code));
        Assert.All(symbols, symbol => Assert.True(symbol.Delisted));
        Assert.Equal(("Altaba Inc", "US0213461017", ProviderSymbols.CommonStock), (symbols[2].Name, symbols[2].Isin, symbols[2].Type));

        // A list that is no list, and one listing nothing, are refused rather than read as matching nothing.
        Assert.Throws<FormatException>(() => ProviderSymbols.Parse("{}", delisted: false));
        Assert.Throws<FormatException>(() => ProviderSymbols.Parse("[]", delisted: false));
        Assert.Throws<FormatException>(() => FundSnapshots.ParseSnapshot("<edgarSubmission><formData><genInfo><seriesId>S000004313</seriesId></genInfo></formData></edgarSubmission>", "undated"));
    }

    [Fact]
    public void AHoldingIsMatchedToItsOwnSecurityByIsinWhereItsTickerWasLaterReused()
    {
        // Bed Bath & Beyond, which the S&P 600's fund held in 2019 and which went bankrupt in 2023, is the provider's
        // BBBYQ; the company trading as BBBY today is the former Overstock, carrying another ISIN. A match by name or by
        // ticker would read the fund's 2019 holding as today's company.
        var matcher = new HoldingMatcher(
        [
            new ListedSymbol("BBBY", "Bed Bath & Beyond, Inc.", "NYSE", ProviderSymbols.CommonStock, "US6903701018", Delisted: false),
            new ListedSymbol("BBBYQ", "Bed Bath & Beyond Inc.", "PINK", ProviderSymbols.CommonStock, "US0758961009", Delisted: true),
        ]);

        var held = new FiledHolding("Bed Bath & Beyond Inc", "075896100", null, FundSnapshots.CommonEquity);
        var match = matcher.Match(held);

        Assert.Equal(("BBBYQ", HoldingMatcher.ByIsin), (match.Ticker, match.By));
        Assert.Equal(["BBBYQ"], match.Between);
    }

    // A fund's holdings document as the archive serves it, its series and quarter end with each holding's name, CUSIP,
    // ISIN where one is given and category.
    static string Snapshot(string series, string period, params (string Name, string? Cusip, string? Isin, string Category)[] holdings)
    {
        var document = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><edgarSubmission xmlns=\"http://www.sec.gov/edgar/nport\"><formData><genInfo><seriesId>{series}</seriesId><repPdDate>{period}</repPdDate></genInfo><invstOrSecs>");

        foreach (var (name, cusip, isin, category) in holdings)
        {
            document.Append($"<invstOrSec><name>{System.Security.SecurityElement.Escape(name)}</name><cusip>{cusip ?? "000000000"}</cusip>");
            document.Append(isin is null ? "<identifiers/>" : $"<identifiers><isin value=\"{isin}\"/></identifiers>");
            document.Append($"<assetCat>{category}</assetCat></invstOrSec>");
        }

        return document.Append("</invstOrSecs></formData></edgarSubmission>").ToString();
    }

    // The archive's list of a fund's filings, newest first, each entry's fields inside its content as the archive files them.
    static string Filings(params (string Accession, string Filed, string Form)[] filings) =>
        "<?xml version=\"1.0\" encoding=\"ISO-8859-1\" ?><feed xmlns=\"http://www.w3.org/2005/Atom\">"
        + string.Concat(filings.Select(filing => $"<entry><content type=\"text/xml\"><accession-number>{filing.Accession}</accession-number><filing-date>{filing.Filed}</filing-date><filing-type>{filing.Form}</filing-type></content></entry>"))
        + "</feed>";

    static readonly string Small = FundSnapshots.Series["SML"];

    // Two snapshots of the S&P 600's fund, a filing for the S&P 400's, one the archive holds no document for, and an
    // amendment, which the pull does not read; and the symbol lists the holdings match against.
    static (RecordedFundSnapshotFeed Holdings, RecordedSymbolListFeed Symbols) HoldingAnswers() =>
    (
        new RecordedFundSnapshotFeed(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Small] = Filings(
                    ("0000000001-20-000005", "2020-09-01", "NPORT-P/A"),
                    ("0000000001-20-000004", "2020-08-27", FundSnapshots.Form),
                    ("0000000001-20-000003", "2020-05-28", FundSnapshots.Form),
                    ("0000000001-20-000002", "2020-02-27", FundSnapshots.Form),
                    ("0000000001-19-000001", "2019-11-25", FundSnapshots.Form)),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000001-19-000001"] = Snapshot(
                    Small,
                    "2019-09-30",
                    ("Aegion Corp", "00770F104", null, FundSnapshots.CommonEquity),
                    ("Bravo Holdings Inc", "111111111", "US1111111116", FundSnapshots.CommonEquity),
                    ("Charlie Co", "222222222", "US2222222224", FundSnapshots.CommonEquity),
                    ("Delta Corp 5% 2030", "333333333", "US3333333332", "DBT"),
                    ("Echo Industries Inc", "444444444", "US4444444440", FundSnapshots.CommonEquity),
                    ("Foxtrott Financial Partners In", "555555555", "US5555555558", FundSnapshots.CommonEquity)),
                ["0000000001-20-000002"] = Snapshot(
                    Small,
                    "2019-12-31",
                    ("Aegion Corp", "00770F104", null, FundSnapshots.CommonEquity),
                    ("Bravo Holdings Inc", "111111111", "US1111111116", FundSnapshots.CommonEquity),
                    ("Echo Industries Inc", "444444444", "US4444444440", FundSnapshots.CommonEquity),
                    ("Golf Inc", "666666666", "US6666666663", FundSnapshots.CommonEquity)),
                ["0000000001-20-000003"] = Snapshot(FundSnapshots.Series["MID"], "2020-03-31", ("Hotel Inc", "777777777", "US7777777779", FundSnapshots.CommonEquity)),
            }),
        new RecordedSymbolListFeed(
            """
            [
              { "Code": "BRVO", "Name": "Bravo Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
              { "Code": "ECHO", "Name": "Echo Industries Inc", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": "US4444444440" },
              { "Code": "FXFP", "Name": "Foxtrott Financial Partners Inc", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": "US9999999995" },
              { "Code": "GOLF", "Name": "Golf Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US6666666663" },
              { "Code": "GOLFX", "Name": "Golf Fund", "Exchange": "NMFQS", "Type": "FUND", "Isin": "US6666666663" }
            ]
            """,
            """
            [
              { "Code": "AEGN", "Name": "Aegion Corp", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": "US00770F1049" },
              { "Code": "ECHO_old", "Name": "Echo Industries", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US4444444440" }
            ]
            """)
    );

    [Fact]
    public async Task TheHoldingsPullStoresEachSnapshotWithItsHoldingsMatchedByIsinByNameOrByNeitherAndNamesWhatItCouldNotRead()
    {
        using var store = new TemporaryStore().Migrated();

        var (holdings, symbols) = HoldingAnswers();
        var outcome = await HistoryPull.PullHoldingsAsync(holdings, symbols, PullClock(), store.DatabaseFile, "SML", "history-pull-holdings-1");

        // Aegion by the ISIN its CUSIP makes, Echo by its ISIN between its listed code and its delisted one, Golf by its
        // ISIN among common stock alone, the fund carrying it passed over; Bravo by name alone, its ISIN on no list;
        // Foxtrott by the words of a name the form cut at its width; Charlie by neither; and the bond not at all.
        Assert.Equal(
            [
                "SML|2019-09-30|Aegion Corp|00770F104|US00770F1049|AEGN|isin",
                "SML|2019-09-30|Bravo Holdings Inc|111111111|US1111111116|BRVO|name",
                "SML|2019-09-30|Charlie Co|222222222|US2222222224|null|null",
                "SML|2019-09-30|Echo Industries Inc|444444444|US4444444440|ECHO|isin",
                "SML|2019-09-30|Foxtrott Financial Partners In|555555555|US5555555558|FXFP|name",
                "SML|2019-12-31|Aegion Corp|00770F104|US00770F1049|AEGN|isin",
                "SML|2019-12-31|Bravo Holdings Inc|111111111|US1111111116|BRVO|name",
                "SML|2019-12-31|Echo Industries Inc|444444444|US4444444440|ECHO|isin",
                "SML|2019-12-31|Golf Inc|666666666|US6666666663|GOLF|isin",
            ],
            FamilyRows(store, "SELECT index_code, period, name, cusip, isin, ticker, matched_by FROM pulled_holding ORDER BY period, name;"));
        Assert.Equal(
            ["SML|2019-09-30|0000000001-19-000001|2019-11-25|6|5|history-pull-holdings-1", "SML|2019-12-31|0000000001-20-000002|2020-02-27|4|4|history-pull-holdings-1"],
            FamilyRows(store, "SELECT * FROM pulled_snapshot ORDER BY period;"));

        Assert.Equal((4, 2, 2, 9, 5, 3, 1, 2, 7), (outcome.Filings, outcome.Snapshots, outcome.Stored, outcome.Equity, outcome.ByIsin, outcome.ByName, outcome.Unmatched, outcome.Between, outcome.Requests));

        var detail = HistoryPull.Detail(outcome);

        Assert.Contains("SML: 2 snapshot(s) of 4 filing(s), 2 new, from 2019-09-30 to 2019-12-31, 9 holding(s) of common stock, 5 matched by ISIN, 3 by name alone and 1 by neither, 2 standing between codes; 7 request(s)", detail, StringComparison.Ordinal);
        Assert.Contains($"not read: 0000000001-20-000003: the filing is for {FundSnapshots.Series["MID"]} and not {Small}; 0000000001-20-000004: No recorded holdings document for the filing 0000000001-20-000004.", detail, StringComparison.Ordinal);
        Assert.Contains("matched by neither: Charlie Co on 2019-09-30", detail, StringComparison.Ordinal);
        Assert.Equal(
            "history-pull-holdings|partial",
            Assert.Single(FamilyRows(store, "SELECT stage, outcome FROM run_log WHERE run_id = 'history-pull-holdings-1';")));

        // The names every other pull of the index asks for are its members today and every code its snapshots matched.
        store.Execute("INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('SML', 'ECHO', 'US', 'Echo Industries', NULL, NULL, 'history-pull-members-1'), ('SML', 'ZULU', 'US', 'Zulu Inc', NULL, NULL, 'history-pull-members-1');");

        var companies = new ConstructedCompanyFeed(
            new[] { "AEGN", "BRVO", "ECHO", "FXFP", "GOLF", "ZULU" }.ToDictionary(ticker => ticker, ticker => (Func<CompanyAnswer>)(() => new(ticker, null, null, null, null, null, null, [], 0))));

        await HistoryPull.PullCompaniesAsync(companies, PullClock(), store.DatabaseFile, "SML", PullFrom, "history-pull-companies-1");

        Assert.Equal(["AEGN", "BRVO", "ECHO", "FXFP", "GOLF", "ZULU"], companies.Asked);

        // And the sweep's history reads membership as it stood off what the pull stored: ECHO held by both snapshots and a
        // member today left open, AEGN and BRVO held by both and let go after the newest, FXFP by the first alone, GOLF
        // joining at the second, ZULU a member today held by neither, and the holding matched by nothing in no span.
        var spans = await new SweepHistory(store.DatabaseFile).AsItStoodAsync("SML");

        Assert.Equal(
            [
                "AEGN||2020-01-01", "BRVO||2020-01-01", "ECHO||", "FXFP||2019-10-01", "GOLF|2019-12-31|2020-01-01", "ZULU|2020-01-01|",
            ],
            spans.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}|{pair.Value.Joined:yyyy-MM-dd}|{pair.Value.Left:yyyy-MM-dd}"));

        // A second pull stores no snapshot again, and the purge takes the first's rows whole.
        var (again, againSymbols) = HoldingAnswers();
        var second = await HistoryPull.PullHoldingsAsync(again, againSymbols, PullClock(), store.DatabaseFile, "SML", "history-pull-holdings-2");

        Assert.Equal((2, 0), (second.Snapshots, second.Stored));

        var purged = await HistoryPull.PurgeAsync(PullClock(), store.DatabaseFile, "history-pull-holdings-1", "history-purge-holdings-1");

        Assert.Equal((2, 9), (purged.Snapshots, purged.Holdings));
        Assert.Empty(FamilyRows(store, "SELECT * FROM pulled_snapshot;"));
        Assert.Empty(FamilyRows(store, "SELECT * FROM pulled_holding;"));
    }

    [Fact]
    public async Task AHoldingsPullWhoseFilingListCannotBeReadStoresNothingAndSaysWhy()
    {
        using var store = new TemporaryStore().Migrated();

        var (_, symbols) = HoldingAnswers();
        var outcome = await HistoryPull.PullHoldingsAsync(
            new RecordedFundSnapshotFeed(new Dictionary<string, string>(), new Dictionary<string, string>()),
            symbols,
            PullClock(),
            store.DatabaseFile,
            "SML",
            "history-pull-holdings-1");

        Assert.Equal($"No recorded list of {Small}'s filings.", outcome.Refused);
        Assert.StartsWith($"SML: nothing was stored, No recorded list of {Small}'s filings.", HistoryPull.Detail(outcome), StringComparison.Ordinal);
        Assert.Empty(FamilyRows(store, "SELECT * FROM pulled_snapshot;"));
        Assert.Empty(FamilyRows(store, "SELECT * FROM pulled_holding;"));
        Assert.Equal("refused", Assert.Single(FamilyRows(store, "SELECT outcome FROM run_log WHERE run_id = 'history-pull-holdings-1';")));
        await Assert.ThrowsAsync<ArgumentException>(() => HistoryPull.PullHoldingsAsync(HoldingAnswers().Holdings, symbols, PullClock(), store.DatabaseFile, "GSPC", "history-pull-holdings-2"));
    }

    [Fact]
    public void ANameIsAMemberOnBothSidesOfAQuarterEndItsSnapshotsStraddle()
    {
        // Three quarter ends. AEGN in all three and a member today; BRVO in the first two; GOLF in the last two and not a
        // member today; CHRL in the middle one alone; NEWC a member today held by none.
        DateOnly[] periods = [new(2019, 9, 30), new(2019, 12, 31), new(2020, 3, 31)];
        (DateOnly, string)[] held =
        [
            (periods[0], "AEGN"), (periods[1], "AEGN"), (periods[2], "AEGN"),
            (periods[0], "BRVO"), (periods[1], "BRVO"),
            (periods[1], "GOLF"), (periods[2], "GOLF"),
            (periods[1], "CHRL"),
        ];

        var spans = HoldingSpans.From(periods, held, new HashSet<string>(["AEGN", "NEWC"], StringComparer.Ordinal));

        Assert.Equal<(DateOnly?, DateOnly?)>((null, null), spans["AEGN"]);
        Assert.Equal<(DateOnly?, DateOnly?)>((null, new DateOnly(2020, 1, 1)), spans["BRVO"]);
        Assert.Equal<(DateOnly?, DateOnly?)>((new DateOnly(2019, 12, 31), new DateOnly(2020, 4, 1)), spans["GOLF"]);
        Assert.Equal<(DateOnly?, DateOnly?)>((new DateOnly(2019, 12, 31), new DateOnly(2020, 1, 1)), spans["CHRL"]);
        Assert.Equal<(DateOnly?, DateOnly?)>((new DateOnly(2020, 4, 1), null), spans["NEWC"]);

        bool Member(string ticker, int year, int month, int day) =>
            new SweepName(ticker, [], [spans[ticker]], []).MemberOn(new DateOnly(year, month, day));

        // On both sides of a quarter end two snapshots straddle, and on the quarter end itself.
        Assert.True(Member("BRVO", 2019, 10, 1) && Member("BRVO", 2019, 12, 31) && Member("BRVO", 2019, 9, 30));
        Assert.True(Member("GOLF", 2020, 1, 2) && Member("GOLF", 2020, 3, 31) && Member("GOLF", 2019, 12, 31));

        // Not past the last snapshot holding it, and not before the first where an earlier snapshot did not hold it.
        Assert.False(Member("BRVO", 2020, 1, 2));
        Assert.False(Member("GOLF", 2019, 12, 30) || Member("GOLF", 2020, 4, 1));
        Assert.True(Member("CHRL", 2019, 12, 31));
        Assert.False(Member("CHRL", 2019, 12, 30) || Member("CHRL", 2020, 1, 2));

        // A name the first snapshot holds is read from the history's start, and one held by the newest and still a member
        // stays open; one a member today that no snapshot holds joins the day after the newest.
        Assert.True(Member("AEGN", 2019, 1, 2) && Member("AEGN", 2026, 10, 2));
        Assert.True(Member("NEWC", 2020, 4, 1) && !Member("NEWC", 2020, 3, 31));
    }
}
