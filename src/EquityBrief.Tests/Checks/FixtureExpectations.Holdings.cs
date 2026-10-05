using System.Text;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 15.3: membership as it stood, rebuilt from the funds' quarter-end holdings the SEC holds. A
// captured filing and a captured symbol list read as they were sent; the N-Q and the annual report before the first
// public N-PORT read for each fund's complete schedule; the holdings pull over constructed answers storing each snapshot
// and each holding of common stock with the code it matched by ISIN, by name or by neither, a name match kept only where
// its code traded at the quarter's end, a quarter an earlier pull stored matched again, and naming each filing it could
// not read; a holding a reused ticker would mislead matched to its own security; and a name read as a member on both
// sides of a quarter end its snapshots straddle.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
// see: A holding matched by name is kept only where its code traded at the quarter's end
// see: The funds' holdings before their first public N-PORT are read from their N-Q of 2018-12-31 and their annual report of 2019-03-31
public partial class FixtureExpectations
{
    // The rows the history for the sweeps adds that this check reaches: section 18's two.
    internal static readonly string[] HoldingsClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A fund's holdings filing the history pull cannot read"),
        CheckReach.Key(Scope.FailureTable, "A fund's holding matched to no provider code"),
    ];

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

    [Fact]
    public void TheTwoFilingsBeforeTheFirstPublicNportAreReadForEachFundsCompleteScheduleOfCommonStock()
    {
        var folder = Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");
        var quarter = File.ReadAllText(Path.Combine(folder, "phase15-nq-2018-12-31.htm"));
        var year = File.ReadAllText(Path.Combine(folder, "phase15-ncsr-2019-03-31.htm"));

        FundSnapshot Read(string document, string index, FundSchedule filing) =>
            FundSchedules.Parse(document, FundSnapshots.Series[index], FundSchedules.Titles[index], filing);

        // Each page's heading names its date and its fund, and a fund's own pages are read and no other's: each holding of
        // common stock by its company's name alone, the footnote marks after it taken off, with no identifier.
        var mid = Read(quarter, "MID", FundSchedules.Filings[0]);

        Assert.Equal((FundSnapshots.Series["MID"], new DateOnly(2018, 12, 31), "0001193125-19-059323"), (mid.Series, mid.Period, mid.Accession));
        Assert.Equal(["Curtiss-Wright Corp.", "Esterline Technologies Corp.", "Teledyne Technologies Inc."], mid.Holdings.Select(holding => holding.Name));
        Assert.All(mid.Holdings, holding => Assert.Equal<(string?, string?, string?)>((null, null, FundSnapshots.CommonEquity), (holding.Cusip, holding.Isin, holding.Category)));
        Assert.Equal(["AAR Corp.", "Aerojet Rocketdyne Holdings Inc."], Read(quarter, "SML", FundSchedules.Filings[0]).Holdings.Select(holding => holding.Name));

        // The annual report's summary of the mid-cap fund's schedule, Teledyne and the rest as other securities, is passed
        // over for the complete schedule further on, which no longer holds Esterline, bought by TransDigm in March 2019.
        Assert.Equal(["Curtiss-Wright Corp.", "Teledyne Technologies Inc.", "XPO Logistics Inc."], Read(year, "MID", FundSchedules.Filings[1]).Holdings.Select(holding => holding.Name));
        Assert.Equal(["AAR Corp.", "Aerojet Rocketdyne Holdings Inc."], Read(year, "SML", FundSchedules.Filings[1]).Holdings.Select(holding => holding.Name));

        // A document holding the fund's summary alone, or no schedule of it as of the quarter's end, is refused rather than
        // read as holding nothing.
        var summary = year[..year.IndexOf("Small-Cap", StringComparison.Ordinal)];

        Assert.Contains("only a summary of it", Assert.Throws<FormatException>(() => Read(summary, "MID", FundSchedules.Filings[1])).Message, StringComparison.Ordinal);
        Assert.DoesNotContain("summary", Assert.Throws<FormatException>(() => Read(quarter, "MID", FundSchedules.Filings[1])).Message, StringComparison.Ordinal);

        // A fund's schedule ends at its own total: one whose pages run into another fund's before it closes is refused
        // rather than read as holding the other fund's stocks, which are still read for that fund.
        var both = Schedules("December 31, 2018", (FundSchedules.Titles["MID"], ["Hotel Inc."]), (FundSchedules.Titles["SML"], ["Aegion Corp."]));
        var total = both.IndexOf("<tr><td><b>Total Common Stocks", StringComparison.Ordinal);
        var unclosed = both.Remove(total, both.IndexOf("</tr>", total, StringComparison.Ordinal) + "</tr>".Length - total);

        Assert.Throws<FormatException>(() => Read(unclosed, "MID", FundSchedules.Filings[0]));
        Assert.Equal(["Aegion Corp."], Read(unclosed, "SML", FundSchedules.Filings[0]).Holdings.Select(holding => holding.Name));
    }

    [Fact]
    public void AHoldingMatchedByNameIsKeptOnlyWhereItsCodeTradedAtTheQuarterEnd()
    {
        // McDermott International's shares before its 2020 bankruptcy, the provider's MDR, and the shares issued after it,
        // MCDIF, carry one name; the fund held the first in 2019, when the second did not trade.
        var matcher = new HoldingMatcher(
            [
                new ListedSymbol("MCDIF", "McDermott International Ltd", "PINK", ProviderSymbols.CommonStock, "PAP4401V1095", Delisted: false),
                new ListedSymbol("MDR", "McDermott International Inc", "NYSE", ProviderSymbols.CommonStock, null, Delisted: true),
                new ListedSymbol("SAM", "Boston Beer Company Inc", "NYSE", ProviderSymbols.CommonStock, null, Delisted: false),
                new ListedSymbol("SCI", "Service Corporation International", "NYSE", ProviderSymbols.CommonStock, null, Delisted: false),
                new ListedSymbol("WTRG", "Essential Utilities Inc", "NYSE", ProviderSymbols.CommonStock, "US29670G1022", Delisted: false),
            ],
            [("AQUA AMERICA INC", "WTRG")]);

        var mcdermott = new FiledHolding("McDermott International Inc.", null, null, FundSnapshots.CommonEquity);

        (string?, string?) Read(HoldingMatch match) => (match.Ticker, match.By);

        Assert.Equal(("MDR", HoldingMatcher.ByName), Read(matcher.Match(mcdermott, code => code == "MDR")));
        Assert.Equal(["MCDIF", "MDR"], matcher.NameCandidates(mcdermott));

        // Neither traded at the quarter's end, and which traded not known: no match by name either way.
        Assert.Equal((null, null), Read(matcher.Match(mcdermott, _ => false)));
        Assert.Equal((null, null), Read(matcher.Match(mcdermott)));

        // A company renamed since, whose old name the provider no longer carries, is read by the name its fund's own
        // filings carried beside an ISIN, and only where that code traded.
        var aqua = new FiledHolding("Aqua America Inc.", null, null, FundSnapshots.CommonEquity);

        Assert.Equal(("WTRG", HoldingMatcher.ByName), Read(matcher.Match(aqua, _ => true)));
        Assert.Equal((null, null), Read(matcher.Match(aqua, code => code != "WTRG")));

        // A non-voting class's mark and a place after a slash are no part of a company's name.
        Assert.Equal("SAM", matcher.Match(new FiledHolding("Boston Beer Co. Inc. (The), Class A, NVS", null, null, FundSnapshots.CommonEquity), _ => true).Ticker);
        Assert.Equal("SCI", matcher.Match(new FiledHolding("Service Corp. International/U.S", null, null, FundSnapshots.CommonEquity), _ => true).Ticker);

        // Noble Corp plc, which the S&P 600's fund held in 2019 and which the provider carries as a delisted listing on the
        // NYSE, shares its name's key with Noble Group's lines over the counter, still listed and trading then: the index
        // admits only stocks listed on a main exchange, so the delisted NYSE listing is read before them.
        var noble = new HoldingMatcher(
        [
            new ListedSymbol("NE_old", "Noble Corp plc", "NYSE", ProviderSymbols.CommonStock, null, Delisted: true),
            new ListedSymbol("NOBGF", "Noble Group Holdings Limited", "PINK", ProviderSymbols.CommonStock, null, Delisted: false),
            new ListedSymbol("NOBGY", "Noble Group Holdings Ltd", "PINK", ProviderSymbols.CommonStock, null, Delisted: false),
        ]);

        Assert.Equal("NE_old", noble.Match(new FiledHolding("Noble Corp plc", null, null, FundSnapshots.CommonEquity), _ => true).Ticker);
        Assert.Equal("NOBGF", noble.Match(new FiledHolding("Noble Corp plc", null, null, FundSnapshots.CommonEquity), code => code != "NE_old").Ticker);
    }

    // A filing's document as the archive serves one, each fund's schedule a page headed by its date and the fund's name,
    // its common stocks a row each of name, shares and value, closing at their total.
    static string Schedules(string date, params (string Title, string[] Names)[] funds) =>
        string.Concat(funds.Select(fund =>
            $"<p>Schedule&nbsp;of&nbsp;Investments</p><p>{date}</p><p><b>iShares</b><sup>&reg;</sup><b> {fund.Title.Replace("&", "&amp;", StringComparison.Ordinal)}</b></p>"
            + "<table><tr><td><i>Security</i></td><td><i>Shares</i></td><td><i>Value</i></td></tr><tr><td><b>Common Stocks</b></td><td>&nbsp;</td></tr>"
            + string.Concat(fund.Names.Select(name => $"<tr><td>{name}<sup>(a)</sup></td><td>1,000</td><td>$</td><td>50,000</td></tr>"))
            + "<tr><td><b>Total Common Stocks &#151; 99.8%</b></td><td>1,000,000</td></tr></table>"));

    // A provider answering each code with a session on each of the days given within what it is asked for, and none for a
    // code it holds nothing for, recording each code it is asked about.
    sealed class SessionsOn(IReadOnlyDictionary<string, DateOnly[]> sessions) : IHistoricalBarFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;
            Asked.Add(FormattableString.Invariant($"{ticker} {to:yyyy-MM-dd}"));

            return Task.FromResult<IReadOnlyList<ProviderBar>>(
                [.. (sessions.GetValueOrDefault(ticker) ?? []).Where(day => day >= from && day <= to).Select(day => new ProviderBar(day, 10m, 11m, 9m, 10m, 10m, 1_000))]);
        }
    }

    // A pulled bar of a code on a day, which says the code traded then.
    static void PulledOn(TemporaryStore store, string ticker, string day) =>
        store.Execute($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('{ticker}', '{day}', '10', '11', '9', '10', '10', 1000, 'history-pull-bars-1');");

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
    // amendment, which the pull does not read; the N-Q and the annual report before them, each carrying the S&P 600's
    // fund's schedule and the first the S&P 400's beside it; and the symbol lists the holdings match against.
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
                    ("Foxtrott Financial Partners In", "555555555", "US5555555558", FundSnapshots.CommonEquity),
                    ("Romeo Inc", "888888888", "US8888888880", FundSnapshots.CommonEquity)),
                ["0000000001-20-000002"] = Snapshot(
                    Small,
                    "2019-12-31",
                    ("Aegion Corp", "00770F104", null, FundSnapshots.CommonEquity),
                    ("Bravo Holdings Inc", "111111111", "US1111111116", FundSnapshots.CommonEquity),
                    ("Echo Industries Inc", "444444444", "US4444444440", FundSnapshots.CommonEquity),
                    ("Golf Inc", "666666666", "US6666666663", FundSnapshots.CommonEquity)),
                ["0000000001-20-000003"] = Snapshot(FundSnapshots.Series["MID"], "2020-03-31", ("Hotel Inc", "777777777", "US7777777779", FundSnapshots.CommonEquity)),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"{FundSchedules.Filings[0].Accession}/{FundSchedules.Filings[0].Document}"] = Schedules(
                    "December 31, 2018",
                    (FundSchedules.Titles["MID"], ["Hotel Inc."]),
                    (FundSchedules.Titles["SML"], ["Aegion Corp.", "Kilo Inc., Class A, NVS", "Lima Bancorp/Rockland MA", "Romeo Inc.", "Sierra Co."])),
                [$"{FundSchedules.Filings[1].Accession}/{FundSchedules.Filings[1].Document}"] = Schedules(
                    "March 31, 2019",
                    (FundSchedules.Titles["SML"], ["Aegion Corp.", "Kilo Inc., Class A, NVS", "Mike Corp."])),
            }),
        new RecordedSymbolListFeed(
            """
            [
              { "Code": "BRVO", "Name": "Bravo Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
              { "Code": "ECHO", "Name": "Echo Industries Inc", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": "US4444444440" },
              { "Code": "FXFP", "Name": "Foxtrott Financial Partners Inc", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": "US9999999995" },
              { "Code": "GOLF", "Name": "Golf Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US6666666663" },
              { "Code": "GOLFX", "Name": "Golf Fund", "Exchange": "NMFQS", "Type": "FUND", "Isin": "US6666666663" },
              { "Code": "JULT", "Name": "Juliet Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US8888888880" },
              { "Code": "KILO", "Name": "Kilo Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
              { "Code": "LIMA", "Name": "Lima Bancorp", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": null },
              { "Code": "MIKE", "Name": "Mike Corp", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
              { "Code": "SIER", "Name": "Sierra Co", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null }
            ]
            """,
            """
            [
              { "Code": "AEGN", "Name": "Aegion Corp", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": "US00770F1049" },
              { "Code": "ECHO_old", "Name": "Echo Industries", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US4444444440" },
              { "Code": "MIKEQ", "Name": "Mike Corp", "Exchange": "PINK", "Type": "Common Stock", "Isin": null }
            ]
            """)
    );

    // The bars the history pull holds near each quarter's end, and the sessions the provider answers for the codes it
    // holds none of: LIMA trading on 2018-12-27, MIKEQ and not MIKE on 2019-03-28, and SIER on none.
    static void PulledNearTheQuarterEnds(TemporaryStore store)
    {
        foreach (var (ticker, day) in new[]
        {
            ("AEGN", "2018-12-31"), ("AEGN", "2019-03-29"), ("KILO", "2018-12-28"), ("KILO", "2019-03-29"), ("JULT", "2018-12-31"),
            ("BRVO", "2019-09-27"), ("BRVO", "2019-12-31"), ("FXFP", "2019-09-30"),
        })
        {
            PulledOn(store, ticker, day);
        }
    }

    static SessionsOn ProviderSessions() => new(new Dictionary<string, DateOnly[]>(StringComparer.Ordinal)
    {
        ["LIMA"] = [new(2018, 12, 27)],
        ["MIKEQ"] = [new(2019, 3, 28)],
    });

    [Fact]
    public async Task TheHoldingsPullStoresEachSnapshotWithItsHoldingsMatchedByIsinByNameOrByNeitherAndNamesWhatItCouldNotRead()
    {
        using var store = new TemporaryStore().Migrated();

        PulledNearTheQuarterEnds(store);

        var (holdings, symbols) = HoldingAnswers();
        var prices = ProviderSessions();
        var outcome = await HistoryPull.PullHoldingsAsync(holdings, symbols, PullClock(), store.DatabaseFile, "SML", "history-pull-holdings-1", prices: prices);

        // Aegion by the ISIN its CUSIP makes, Echo by its ISIN between its listed code and its delisted one, Golf by its
        // ISIN among common stock alone, the fund carrying it passed over, and Romeo by its ISIN under the code its company
        // trades as since its rename; Bravo by name alone, its ISIN on no list; Foxtrott by the words of a name the form
        // cut at its width; Charlie by neither; and the bond not at all. Before the first N-PORT, each of the S&P 600's
        // fund's holdings by its name alone and no other fund's: Kilo past its class's non-voting mark, Lima past the place
        // after its slash, Romeo by the name the fund's own N-PORT carries beside its ISIN, Lima and Mike traded as the
        // provider answered, Mike by the delisted code that traded and not the listed one, and Sierra by none, its code
        // trading on no day the provider answers in the days to the quarter's end.
        Assert.Equal(
            [
                "SML|2018-12-31|Aegion Corp.|null|null|AEGN|name",
                "SML|2018-12-31|Kilo Inc., Class A, NVS|null|null|KILO|name",
                "SML|2018-12-31|Lima Bancorp/Rockland MA|null|null|LIMA|name",
                "SML|2018-12-31|Romeo Inc.|null|null|JULT|name",
                "SML|2018-12-31|Sierra Co.|null|null|null|null",
                "SML|2019-03-31|Aegion Corp.|null|null|AEGN|name",
                "SML|2019-03-31|Kilo Inc., Class A, NVS|null|null|KILO|name",
                "SML|2019-03-31|Mike Corp.|null|null|MIKEQ|name",
                "SML|2019-09-30|Aegion Corp|00770F104|US00770F1049|AEGN|isin",
                "SML|2019-09-30|Bravo Holdings Inc|111111111|US1111111116|BRVO|name",
                "SML|2019-09-30|Charlie Co|222222222|US2222222224|null|null",
                "SML|2019-09-30|Echo Industries Inc|444444444|US4444444440|ECHO|isin",
                "SML|2019-09-30|Foxtrott Financial Partners In|555555555|US5555555558|FXFP|name",
                "SML|2019-09-30|Romeo Inc|888888888|US8888888880|JULT|isin",
                "SML|2019-12-31|Aegion Corp|00770F104|US00770F1049|AEGN|isin",
                "SML|2019-12-31|Bravo Holdings Inc|111111111|US1111111116|BRVO|name",
                "SML|2019-12-31|Echo Industries Inc|444444444|US4444444440|ECHO|isin",
                "SML|2019-12-31|Golf Inc|666666666|US6666666663|GOLF|isin",
            ],
            FamilyRows(store, "SELECT index_code, period, name, cusip, isin, ticker, matched_by FROM pulled_holding ORDER BY period, name;"));
        Assert.Equal(
            [
                $"SML|2018-12-31|{FundSchedules.Filings[0].Accession}|2019-03-01|5|5|history-pull-holdings-1",
                $"SML|2019-03-31|{FundSchedules.Filings[1].Accession}|2019-06-07|3|3|history-pull-holdings-1",
                "SML|2019-09-30|0000000001-19-000001|2019-11-25|7|6|history-pull-holdings-1",
                "SML|2019-12-31|0000000001-20-000002|2020-02-27|4|4|history-pull-holdings-1",
            ],
            FamilyRows(store, "SELECT * FROM pulled_snapshot ORDER BY period;"));

        // The provider asked about each code holding no pulled bar near its quarter's end, once a code a quarter.
        Assert.Equal(["LIMA 2018-12-31", "SIER 2018-12-31", "MIKE 2019-03-31", "MIKEQ 2019-03-31"], prices.Asked);
        Assert.Equal((6, 4, 4, 18, 6, 10, 2, 2, 0, 4, 13), (outcome.Filings, outcome.Snapshots, outcome.Stored, outcome.Equity, outcome.ByIsin, outcome.ByName, outcome.Unmatched, outcome.Between, outcome.Again, outcome.Asked, outcome.Requests));

        var detail = HistoryPull.Detail(outcome);

        Assert.Contains("SML: 4 snapshot(s) of 6 filing(s), 4 new, from 2018-12-31 to 2019-12-31, 18 holding(s) of common stock, 6 matched by ISIN, 10 by name alone and 2 by neither, 2 standing between codes, 0 holding(s) of quarters an earlier pull stored matched again to another code or to none, 4 code(s) asked of the provider whether they traded at a quarter's end; 13 request(s)", detail, StringComparison.Ordinal);
        Assert.Contains($"not read: 0000000001-20-000003: the filing is for {FundSnapshots.Series["MID"]} and not {Small}; 0000000001-20-000004: No recorded holdings document for the filing 0000000001-20-000004.", detail, StringComparison.Ordinal);
        Assert.Contains("matched by neither: Sierra Co. on 2018-12-31; Charlie Co on 2019-09-30", detail, StringComparison.Ordinal);
        Assert.Equal(
            "history-pull-holdings|partial",
            Assert.Single(FamilyRows(store, "SELECT stage, outcome FROM run_log WHERE run_id = 'history-pull-holdings-1';")));

        // The names every other pull of the index asks for are its members today and every code its snapshots matched.
        store.Execute("INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('SML', 'ECHO', 'US', 'Echo Industries', NULL, NULL, 'history-pull-members-1'), ('SML', 'ZULU', 'US', 'Zulu Inc', NULL, NULL, 'history-pull-members-1');");

        string[] named = ["AEGN", "BRVO", "ECHO", "FXFP", "GOLF", "JULT", "KILO", "LIMA", "MIKEQ", "ZULU"];

        var companies = new ConstructedCompanyFeed(
            named.ToDictionary(ticker => ticker, ticker => (Func<CompanyAnswer>)(() => new(ticker, null, null, null, null, null, null, [], 0))));

        await HistoryPull.PullCompaniesAsync(companies, PullClock(), store.DatabaseFile, "SML", PullFrom, "history-pull-companies-1");

        Assert.Equal(named, companies.Asked);

        // And the sweep's history reads membership as it stood off what the pull stored, from the N-Q's quarter end: AEGN,
        // KILO, LIMA and JULT held by it read from the history's start, AEGN let go after the newest, KILO after the annual
        // report and LIMA after the N-Q, JULT held by the N-Q and the first N-PORT read between them; MIKEQ joining at the
        // annual report; BRVO, ECHO and FXFP joining at the first N-PORT, ECHO a member today left open; GOLF joining at
        // the second, ZULU a member today held by none, and the holdings matched by nothing in no span.
        var spans = await new SweepHistory(store.DatabaseFile).AsItStoodAsync("SML");

        Assert.Equal(
            [
                "AEGN||2020-01-01", "BRVO|2019-09-30|2020-01-01", "ECHO|2019-09-30|", "FXFP|2019-09-30|2019-10-01", "GOLF|2019-12-31|2020-01-01",
                "JULT||2019-10-01", "KILO||2019-04-01", "LIMA||2019-01-01", "MIKEQ|2019-03-31|2019-04-01", "ZULU|2020-01-01|",
            ],
            spans.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => FormattableString.Invariant($"{pair.Key}|{pair.Value.Joined:yyyy-MM-dd}|{pair.Value.Left:yyyy-MM-dd}")));

        // A second pull stores no snapshot again and matches each quarter's holdings again under the rule as it stands:
        // with KILO's bar near the annual report's quarter end gone and the provider sending no session for it either, its
        // holding there is matched to none, kept as first stored and under the first pull.
        store.Execute("DELETE FROM pulled_bar WHERE ticker = 'KILO' AND session_date = '2019-03-29';");

        var (again, againSymbols) = HoldingAnswers();
        var second = await HistoryPull.PullHoldingsAsync(again, againSymbols, PullClock(), store.DatabaseFile, "SML", "history-pull-holdings-2", prices: ProviderSessions());

        Assert.Equal((4, 0, 1), (second.Snapshots, second.Stored, second.Again));
        Assert.Equal(
            "SML|2019-03-31|Kilo Inc., Class A, NVS|null|null|history-pull-holdings-1",
            Assert.Single(FamilyRows(store, "SELECT index_code, period, name, ticker, matched_by, pull FROM pulled_holding WHERE name LIKE 'Kilo%' AND period = '2019-03-31';")));
        Assert.Equal(18, FamilyRows(store, "SELECT * FROM pulled_holding WHERE pull = 'history-pull-holdings-1';").Count);

        var purged = await HistoryPull.PurgeAsync(PullClock(), store.DatabaseFile, "history-pull-holdings-1", "history-purge-holdings-1");

        Assert.Equal((4, 18), (purged.Snapshots, purged.Holdings));
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
