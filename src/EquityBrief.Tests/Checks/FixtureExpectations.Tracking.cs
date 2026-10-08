using System.Globalization;
using System.Text;
using EquityBrief.Core.Providers;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 15.3 correction of 2026-10-05: a holding's code is kept only where its closes move in step
// with the fund's values a share over the quarters it was matched, the holding's codes by name read in place of one that
// does not, and a company renamed since matched to a code its fund held by ISIN within a year where that code's close
// equals its value a share to the cent at two quarter ends or more. And the 15.3 correction of 2026-10-08: a name's
// code read over several quarters held to the level at each as well as in step, a code the fund held by ISIN beside the
// holding at its quarter ends not read as the holding renamed, and 17.1's carry-back of a schedule's holding to the code
// the next coded quarter's identical name matched, with each holding's shares and value stored as filed.
// see: A holding's code is kept only where its closes move in step with the fund's values a share, and a match by name is held to the level at each quarter end as well
// see: A code the fund held by ISIN beside a holding at one quarter end is another holding and never that holding renamed
// see: A holding of a schedule filed with no identifier carries the code the next coded quarter's holding of the identical name matched, where the closes move in step across them
// see: A renamed company is matched to a code its fund held by ISIN within a year where its close equals the value a share to the cent at two quarter ends
public partial class FixtureExpectations
{
    static readonly string[] TrackedQuarters = ["2019-09-30", "2019-12-31", "2020-03-31", "2020-06-30"];

    // A fund's holdings document with each holding's ISIN and the value a share the fund filed, a thousand shares each.
    static string Valued(string period, params (string Name, string Isin, decimal PerShare)[] holdings)
    {
        var document = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><edgarSubmission xmlns=\"http://www.sec.gov/edgar/nport\"><formData><genInfo><seriesId>{Small}</seriesId><repPdDate>{period}</repPdDate></genInfo><invstOrSecs>");

        foreach (var (name, isin, perShare) in holdings)
        {
            document.Append($"<invstOrSec><name>{name}</name><cusip>000000000</cusip><identifiers><isin value=\"{isin}\"/></identifiers>");
            document.Append(FormattableString.Invariant($"<balance>1000.00000000</balance><units>{FundSnapshots.SharesHeld}</units><curCd>USD</curCd><valUSD>{perShare * 1000m:0.00}</valUSD>"));
            document.Append($"<assetCat>{FundSnapshots.CommonEquity}</assetCat></invstOrSec>");
        }

        return document.Append("</invstOrSecs></formData></edgarSubmission>").ToString();
    }

    // Four quarters of the S&P 600's fund. Wander, valued at $50, its ISIN filed by the provider under WNDR_old, whose
    // closes wander against it at $50, $80, $30 and $65; the company's own delisted listing, WNDRQ, trades at $50 and the
    // pulled history holds none of it. Spun, valued at $50, its code SPUN by ISIN at $25 twice and then $50 twice, the
    // provider's closes before a spin-off divided down, filed at the last quarter end as Spun Two Inc under the same ISIN,
    // so the quarters filed as Spun Inc end one quarter after the step. Steady Steel, valued at $50, its name's code STDY at
    // $40, $41, $39.50 and $40.50, another security's price moving with the holding's about a fifth under, its ratio
    // running 1.25, 1.2195, 1.2658 and 1.2346. Exact, valued at $60, $66, $54 and $72, its name's code EXCT at $20, $22,
    // $18 and $24, a third of it at every quarter end as the provider's closes divided for a corporate action stand.
    // Level, valued at $50, its name's
    // code LEVL at $50, $51, $49.50 and $50, at the level and in step. Old Name, held the first two quarters at $37.21
    // and $41.05, renamed New Name and held under its new ISIN the last two at $44.10 and $39.99, its code NEWN by that
    // ISIN at those closes in all four. Lone, held the first two at $25.00 and $25.30, SPUN's close to the cent in the
    // first alone and moving in step with it. And the annual report's schedule of 2019-03-31, filed with no identifier:
    // Spun Inc, valued at $50 where SPUN closed at $25, the ratio its coded quarters hold; Steady Steel Co, whose coded
    // quarters match no code; and Wander Corp, whose code by name holds no close on that day.
    static (RecordedFundSnapshotFeed Holdings, RecordedSymbolListFeed Symbols) TrackedAnswers() =>
    (
        new RecordedFundSnapshotFeed(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Small] = Filings(
                    ("0000000002-20-000014", "2020-08-27", FundSnapshots.Form),
                    ("0000000002-20-000013", "2020-05-28", FundSnapshots.Form),
                    ("0000000002-20-000012", "2020-02-27", FundSnapshots.Form),
                    ("0000000002-19-000011", "2019-11-25", FundSnapshots.Form)),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000002-19-000011"] = Valued(TrackedQuarters[0], ("Wander Corp", "US1000000003", 50m), ("Spun Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("Level Co", "US7000000007", 50m), ("Old Name Inc", "US4000000000", 37.21m), ("Lone Co", "US6000000008", 25m), ("Mapped Co", "US8000000005", 30m), ("Astray Co", "US9000000004", 30m), ("Exact Co", "US1100000006", 60m)),
                ["0000000002-20-000012"] = Valued(TrackedQuarters[1], ("Wander Corp", "US1000000003", 50m), ("Spun Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("Level Co", "US7000000007", 50m), ("Old Name Inc", "US4000000000", 41.05m), ("Lone Co", "US6000000008", 25.30m), ("Mapped Co", "US8000000005", 30.50m), ("Astray Co", "US9000000004", 30m), ("Exact Co", "US1100000006", 66m)),
                ["0000000002-20-000013"] = Valued(TrackedQuarters[2], ("Wander Corp", "US1000000003", 50m), ("Spun Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("Level Co", "US7000000007", 50m), ("New Name Inc", "US5000000009", 44.10m), ("Mapped Co", "US8000000005", 29.80m), ("Astray Co", "US9000000004", 30m), ("Exact Co", "US1100000006", 54m)),
                ["0000000002-20-000014"] = Valued(TrackedQuarters[3], ("Wander Corp", "US1000000003", 50m), ("Spun Two Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("Level Co", "US7000000007", 50m), ("New Name Inc", "US5000000009", 39.99m), ("Mapped Co", "US8000000005", 30m), ("Astray Co", "US9000000004", 30m), ("Exact Co", "US1100000006", 72m)),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"{FundSchedules.Filings[0].Accession}/{FundSchedules.Filings[0].Document}"] = Schedules("December 31, 2018", (FundSchedules.Titles["SML"], [])),
                [$"{FundSchedules.Filings[1].Accession}/{FundSchedules.Filings[1].Document}"] = Schedules("March 31, 2019", (FundSchedules.Titles["SML"], ["Spun Inc", "Steady Steel Co", "Wander Corp", "Level Co"])),
            }),
        new RecordedSymbolListFeed(
            """
            [
              { "Code": "SPUN", "Name": "Spun Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US2000000002" },
              { "Code": "STDY", "Name": "Steady Steel Co", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
              { "Code": "LEVL", "Name": "Level Co", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
              { "Code": "EXCT", "Name": "Exact Co", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
              { "Code": "NEWN", "Name": "New Name Inc", "Exchange": "NASDAQ", "Type": "Common Stock", "Isin": "US5000000009" }
            ]
            """,
            """
            [
              { "Code": "WNDR_old", "Name": "Wander Corp", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US1000000003" },
              { "Code": "WNDRQ", "Name": "Wander Corp", "Exchange": "PINK", "Type": "Common Stock", "Isin": null }
            ]
            """)
    );

    // OpenFIGI's answer, in the shape of its captured answers, for the one request the pull makes over the five
    // identifiers still matched to none after their names are read, in their order: Steady Steel, Old Name and Lone
    // found under no identifier, Mapped Co traded under MAPD on one venue and a placeholder on the composite, and Astray
    // Co under ASTR.
    static RecordedOpenFigiMappingFeed TrackedMappings() =>
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [RecordedOpenFigiMappingFeed.Key(["US3000000001", "US4000000000", "US6000000008", "US8000000005", "US9000000004"])] =
                """
                [
                  { "warning": "No identifier found." },
                  { "warning": "No identifier found." },
                  { "warning": "No identifier found." },
                  { "data": [
                    { "figi": "BBG000000001", "name": "MAPPED CO", "ticker": "9990001D", "exchCode": "US", "compositeFIGI": "BBG000000001", "securityType": "Common Stock", "marketSector": "Equity", "shareClassFIGI": "BBG001000001", "securityType2": "Common Stock", "securityDescription": "9990001D" },
                    { "figi": "BBG000000002", "name": "MAPPED CO", "ticker": "MAPD", "exchCode": "UN", "compositeFIGI": "BBG000000001", "securityType": "Common Stock", "marketSector": "Equity", "shareClassFIGI": "BBG001000001", "securityType2": "Common Stock", "securityDescription": "MAPD" },
                    { "figi": "BBG000000003", "name": "MAPPED CO", "ticker": "MPD", "exchCode": "GR", "compositeFIGI": "BBG000000003", "securityType": "Common Stock", "marketSector": "Equity", "shareClassFIGI": "BBG001000001", "securityType2": "Common Stock", "securityDescription": "MPD" }
                  ] },
                  { "data": [
                    { "figi": "BBG000000004", "name": "ASTRAY CO", "ticker": "ASTR", "exchCode": "UW", "compositeFIGI": "BBG000000004", "securityType": "Common Stock", "marketSector": "Equity", "shareClassFIGI": "BBG001000002", "securityType2": "Common Stock", "securityDescription": "ASTR" }
                  ] }
                ]
                """,
        });

    [Fact]
    public async Task AHoldingsCodeIsKeptOnlyWhereItsClosesMoveInStepAndARenamedCompanyIsMatchedToTheCodeItsFundHeldByIsin()
    {
        using var store = new TemporaryStore().Migrated();

        string[] wander = ["50", "80", "30", "65"];
        string[] spun = ["25", "25", "50", "50"];
        string[] level = ["50", "51", "49.5", "50"];
        string[] renamed = ["37.21", "41.05", "44.10", "39.99"];
        string[] mapped = ["30", "30.5", "29.8", "30"];
        string[] steady = ["40", "41", "39.5", "40.5"];
        string[] exact = ["20", "22", "18", "24"];
        string[] astray = ["20", "20.5", "19.8", "20.3"];

        for (var at = 0; at < TrackedQuarters.Length; at++)
        {
            PulledOn(store, "WNDR_old", TrackedQuarters[at], wander[at]);
            PulledOn(store, "SPUN", TrackedQuarters[at], spun[at]);
            PulledOn(store, "STDY", TrackedQuarters[at], steady[at]);
            PulledOn(store, "EXCT", TrackedQuarters[at], exact[at]);
            PulledOn(store, "LEVL", TrackedQuarters[at], level[at]);
            PulledOn(store, "NEWN", TrackedQuarters[at], renamed[at]);
            PulledOn(store, "MAPD", TrackedQuarters[at], mapped[at]);
            PulledOn(store, "ASTR", TrackedQuarters[at], astray[at]);
        }

        // SPUN's close in the days to the annual report's quarter end, on the basis before its spin-off, and LEVL's at a
        // fifth of the schedule's value a share, another ratio than its coded quarters hold.
        PulledOn(store, "SPUN", "2019-03-29", "25");
        PulledOn(store, "LEVL", "2019-03-29", "10");

        var (holdings, symbols) = TrackedAnswers();
        var prices = new SessionsOn(new Dictionary<string, DateOnly[]>(StringComparer.Ordinal)
        {
            ["WNDRQ"] = [.. TrackedQuarters.Select(quarter => DateOnly.ParseExact(quarter, "yyyy-MM-dd", CultureInfo.InvariantCulture))],
        });
        var mappings = TrackedMappings();
        var outcome = await HistoryPull.PullHoldingsAsync(holdings, symbols, PullClock(), store.DatabaseFile, "SML", "history-pull-holdings-1", prices: prices, mappings: mappings);

        // Wander's code by ISIN wanders and is matched to none, its delisted listing by name moving in step in its place;
        // Spun's steps once to a level it holds and is kept; Steady Steel's name's code sits about a fifth under its value a
        // share at every quarter end, in step, off the level and at no one ratio, its ratios 3.8 per cent apart, so it is
        // matched to none where the rule before this kept it, and SPUN, which the fund held by ISIN beside it at every one
        // of its quarter ends, closing at its $50 to the cent twice and stepping once, is another holding and is not read
        // as Steady Steel renamed; Exact's name's code is off the level too, at one ratio of three at all four quarter
        // ends, and is kept by name; Level's name's code sits at the level and in step and is kept; Old Name is matched to the code its fund held by
        // its new ISIN, equal to the cent twice; Lone, equal once, to none. Mapped Co, which no symbol list carries, is
        // matched to MAPD, the one ticker OpenFIGI answered on a US venue, its placeholder and its German listing passed
        // over, at the level and in step; Astray Co's ASTR closes about a third under its value a share at no one ratio
        // and is matched to none.
        // Of the annual report's four, filed with no identifier, Spun carries SPUN from its next coded quarter, its value
        // a share over SPUN's close the ratio those quarters hold; Steady Steel's coded quarters match no code, Wander's
        // code holds no close on the day, and Level's value a share stands at five times LEVL's close where its coded
        // quarters stand at one, a join the step the in-step reading allows would have let through, so each stays
        // matched to none.
        Assert.Equal(
            [
                "2019-03-31 Level Co |", "2019-03-31 Spun Inc SPUN|carried", "2019-03-31 Steady Steel Co |", "2019-03-31 Wander Corp |",
                "2019-09-30 Astray Co |", "2019-09-30 Exact Co EXCT|name", "2019-09-30 Level Co LEVL|name", "2019-09-30 Lone Co |", "2019-09-30 Mapped Co MAPD|figi", "2019-09-30 Old Name Inc NEWN|held", "2019-09-30 Spun Inc SPUN|isin", "2019-09-30 Steady Steel Co |", "2019-09-30 Wander Corp WNDRQ|name",
                "2019-12-31 Astray Co |", "2019-12-31 Exact Co EXCT|name", "2019-12-31 Level Co LEVL|name", "2019-12-31 Lone Co |", "2019-12-31 Mapped Co MAPD|figi", "2019-12-31 Old Name Inc NEWN|held", "2019-12-31 Spun Inc SPUN|isin", "2019-12-31 Steady Steel Co |", "2019-12-31 Wander Corp WNDRQ|name",
                "2020-03-31 Astray Co |", "2020-03-31 Exact Co EXCT|name", "2020-03-31 Level Co LEVL|name", "2020-03-31 Mapped Co MAPD|figi", "2020-03-31 New Name Inc NEWN|isin", "2020-03-31 Spun Inc SPUN|isin", "2020-03-31 Steady Steel Co |", "2020-03-31 Wander Corp WNDRQ|name",
                "2020-06-30 Astray Co |", "2020-06-30 Exact Co EXCT|name", "2020-06-30 Level Co LEVL|name", "2020-06-30 Mapped Co MAPD|figi", "2020-06-30 New Name Inc NEWN|isin", "2020-06-30 Spun Two Inc SPUN|isin", "2020-06-30 Steady Steel Co |", "2020-06-30 Wander Corp WNDRQ|name",
            ],
            TextRows(store, "SELECT period || ' ' || name || ' ' || COALESCE(ticker, '') || '|' || COALESCE(matched_by, '') FROM pulled_holding ORDER BY period, name;"));

        // One mapping request over the five identifiers still matched to none after their names were read, in their
        // order, and no second.
        Assert.Equal((1, 5, 4), (mappings.Requests, outcome.Mapped, outcome.ByMapping));

        // Each holding's shares and value stored as the filing states them, a thousand shares at the value a share with
        // the decimals the N-PORT files, and the schedule's as its row states them.
        Assert.Equal(
            ["2019-03-31 Level Co 1000|50000", "2019-03-31 Spun Inc 1000|50000", "2019-09-30 Level Co 1000.00000000|50000.00", "2019-09-30 Old Name Inc 1000.00000000|37210.00", "2019-09-30 Spun Inc 1000.00000000|50000.00"],
            TextRows(store, "SELECT period || ' ' || name || ' ' || shares || '|' || value_usd FROM pulled_holding WHERE name IN ('Spun Inc', 'Level Co', 'Old Name Inc') AND period <= '2019-09-30' ORDER BY period, name;"));

        Assert.Equal(["WNDR_old for Wander Corp, 4 quarter(s)"], outcome.NotInStep);
        Assert.Equal((2, 1), (outcome.Held, outcome.Carried));
        Assert.Contains("38 holding(s) of common stock, 6 matched by ISIN, 12 by name alone, 0 of them by its wider reading, 2 by a code its fund held by ISIN within a year, 1 carried from the next coded quarter's holding of the identical name, and 13 by none", HistoryPull.Detail(outcome), StringComparison.Ordinal);
        Assert.Contains("; 5 identifier(s) mapped through OpenFIGI in 1 request(s), 4 holding(s) matched by a ticker it answered", HistoryPull.Detail(outcome), StringComparison.Ordinal);
        Assert.Contains("; not in step: WNDR_old for Wander Corp, 4 quarter(s)", HistoryPull.Detail(outcome), StringComparison.Ordinal);

        // The provider asked about WNDRQ at each quarter's end and at the annual report's, the one code a holding's name
        // read that the pulled history holds no close of, and about no code the fund held by ISIN.
        string[] askedAt = ["2019-03-31", .. TrackedQuarters];

        Assert.Equal(askedAt.Select(quarter => $"WNDRQ {quarter}"), prices.Asked.Where(asked => asked.StartsWith("WNDRQ", StringComparison.Ordinal)).Order(StringComparer.Ordinal));
    }

    // The tracking check itself, worked by hand: a ratio held, a step to a level held after, a step at the last quarter
    // end, a wander, a code read once within and beyond the tolerance, and one read at none.
    [Fact]
    public void ACodesClosesMoveInStepWhereTheRatioHoldsOrStepsToALevelItHolds()
    {
        static (decimal, QuarterEndClose)[] At(decimal price, params decimal[] closes) => [.. closes.Select(close => (price, new QuarterEndClose(close)))];

        Assert.True(HoldingMatcher.Tracks(At(50m, 50m, 50.5m, 49.8m, 50m)));
        Assert.True(HoldingMatcher.Tracks(At(50m, 25m, 25m, 50m, 50m)));
        Assert.True(HoldingMatcher.Tracks(At(50m, 40m, 40m, 40m)));
        Assert.False(HoldingMatcher.Tracks(At(50m, 25m, 25m, 50m)));
        Assert.False(HoldingMatcher.Tracks(At(50m, 50m, 80m, 30m, 65m)));
        Assert.False(HoldingMatcher.Tracks(At(50m, 50m, 80m, 30m, 31m)));
        Assert.True(HoldingMatcher.Tracks(At(50m, 52m)));
        Assert.False(HoldingMatcher.Tracks(At(50m, 60m)));
        Assert.Null(HoldingMatcher.Tracks([]));

        // A close the provider sends divided by a later one-for-fifteen split is read with the split undone.
        Assert.True(HoldingMatcher.Tracks([(50m, new QuarterEndClose(750m, 50m)), (50m, new QuarterEndClose(50m))]));

        // To the cent, and not a cent off.
        Assert.True(HoldingMatcher.ToTheCent(37.21m, new QuarterEndClose(37.21m)));
        Assert.False(HoldingMatcher.ToTheCent(37.21m, new QuarterEndClose(37.22m)));

        // At the level, the value a share within the tolerance of the close on either side and not a cent past it, as
        // sent or with a later split undone.
        Assert.True(HoldingMatcher.AtTheLevel(50m, new QuarterEndClose(52.63m)));
        Assert.False(HoldingMatcher.AtTheLevel(50m, new QuarterEndClose(52.64m)));
        Assert.True(HoldingMatcher.AtTheLevel(50m, new QuarterEndClose(47.62m)));
        Assert.False(HoldingMatcher.AtTheLevel(50m, new QuarterEndClose(47.61m)));
        Assert.True(HoldingMatcher.AtTheLevel(50m, new QuarterEndClose(750m, 50m)));
        Assert.False(HoldingMatcher.AtTheLevel(50m, new QuarterEndClose(40m)));

        // One ratio: the same at two quarter ends or more, at half a per cent apart and not a cent past it, a later split
        // undone where that is the nearer basis, and never at a single quarter end.
        Assert.True(HoldingMatcher.OneRatio([(50m, new QuarterEndClose(40m)), (50m, new QuarterEndClose(40m))]));
        Assert.True(HoldingMatcher.OneRatio([(50m, new QuarterEndClose(40m)), (50.25m, new QuarterEndClose(40m))]));
        Assert.False(HoldingMatcher.OneRatio([(50m, new QuarterEndClose(40m)), (50.26m, new QuarterEndClose(40m))]));
        Assert.True(HoldingMatcher.OneRatio([(60m, new QuarterEndClose(20m)), (66m, new QuarterEndClose(2.2m, 22m)), (54m, new QuarterEndClose(18m))]));
        Assert.False(HoldingMatcher.OneRatio([(50m, new QuarterEndClose(40m))]));
        Assert.False(HoldingMatcher.OneRatio([]));
        Assert.Equal(0.005m, HoldingMatcher.RatioTolerance);
    }

    // The 27 identifiers the live store's holdings matched to none carried on 2026-10-08, in the order they were asked,
    // ten a request.
    static readonly string[] MappedIdentifiers =
    [
        "US0025353006", "US1276861036", "US1924791031", "US21870Q1058", "US4001101025", "US6907684038", "US00508X2036", "US232CNT0145", "US12709P1030", "US12739A1007",
        "US2539221083", "US2836778546", "KYG3402M1024", "GB00BN4HT335", "US4883602074", "US5270641096", "US5290431015", "US63935N1072", "US68218J2024", "US68218J3014",
        "US74051N1028", "US74971D1019", "US7589321071", "US78645L1008", "US808CVR1040", "US98421B1008", "US98390M1036",
    ];

    // OpenFIGI's captured answers read as sent: asked with the unlisted flag, each identifier's tickers on the US venues
    // are the ones worked by hand off the capture, a delisted security's placeholder of digits and a D left out, a
    // listing elsewhere left out, and an identifier it found nothing for carrying its warning; asked under the
    // composite's exchange code, every one of the 27 is a warning, which is why the pull asks the other way. An answer
    // whose count differs from the identifiers asked, or that is no list, is refused.
    [Fact]
    public void OpenFigisCapturedAnswersAreReadAsSentWithEachIdentifiersTickersOnTheUnitedStatesVenues()
    {
        var folder = Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");
        var unlisted = Enumerable.Range(1, 3)
            .SelectMany(request => OpenFigiMappings.Parse(
                File.ReadAllText(Path.Combine(folder, $"openfigi-mapping-unlisted-{request}.json")),
                MappedIdentifiers[((request - 1) * 10)..Math.Min(request * 10, MappedIdentifiers.Length)]))
            .ToArray();

        Assert.Equal(MappedIdentifiers, unlisted.Select(mapping => mapping.Identifier));

        var byIdentifier = unlisted.ToDictionary(mapping => mapping.Identifier, StringComparer.Ordinal);

        Assert.Equal(["COHR"], byIdentifier["US1924791031"].Tickers);
        Assert.Equal(["COR"], byIdentifier["US21870Q1058"].Tickers);
        Assert.Equal(["GRUB"], byIdentifier["US4001101025"].Tickers);
        Assert.Equal(["EPAC"], byIdentifier["US00508X2036"].Tickers);
        Assert.Equal(["CCMP"], byIdentifier["US12709P1030"].Tickers);
        Assert.Equal(["CADE"], byIdentifier["US12739A1007"].Tickers);
        Assert.Equal(["DCOM"], byIdentifier["US2539221083"].Tickers);
        Assert.Equal(["FG"], byIdentifier["KYG3402M1024"].Tickers);
        Assert.Equal(["INDV"], byIdentifier["GB00BN4HT335"].Tickers);
        Assert.Equal(["KEM"], byIdentifier["US4883602074"].Tickers);
        Assert.Equal(["LXP"], byIdentifier["US5290431015"].Tickers);
        Assert.Equal(["NCI"], byIdentifier["US63935N1072"].Tickers);
        Assert.Equal(["RPT"], byIdentifier["US74971D1019"].Tickers);
        Assert.Equal(["RGS"], byIdentifier["US7589321071"].Tickers);
        Assert.Equal(["XPER"], byIdentifier["US98421B1008"].Tickers);
        Assert.Equal(("COHERENT INC", "GRUBHUB INC"), (byIdentifier["US1924791031"].Name, byIdentifier["US4001101025"].Name));

        // Found and listed under a placeholder alone, on every US venue: Aaron's, Caesars, Owens-Illinois, El Paso
        // Electric, OmniAb's two earn-outs, Premier and Safehold, each with no ticker and no warning.
        foreach (var placeholderOnly in new[] { "US0025353006", "US1276861036", "US6907684038", "US2836778546", "US68218J2024", "US68218J3014", "US74051N1028", "US78645L1008" })
        {
            Assert.Equal((0, null), (byIdentifier[placeholderOnly].Tickers.Count, byIdentifier[placeholderOnly].Warning));
        }

        // Found under no identifier: the two contra lines, Leslie's and Xperi Holding.
        foreach (var notFound in new[] { "US232CNT0145", "US808CVR1040", "US5270641096", "US98390M1036" })
        {
            Assert.Equal((0, "No identifier found."), (byIdentifier[notFound].Tickers.Count, byIdentifier[notFound].Warning));
        }

        Assert.Equal(15, unlisted.Count(mapping => mapping.Tickers.Count > 0));

        var composite = Enumerable.Range(1, 3)
            .SelectMany(request => OpenFigiMappings.Parse(
                File.ReadAllText(Path.Combine(folder, $"openfigi-mapping-composite-{request}.json")),
                MappedIdentifiers[((request - 1) * 10)..Math.Min(request * 10, MappedIdentifiers.Length)]))
            .ToArray();

        Assert.Equal(27, composite.Length);
        Assert.All(composite, mapping => Assert.Equal((0, "No identifier found."), (mapping.Tickers.Count, mapping.Warning)));

        // The placeholder's shape on both sides, and the request's body as sent.
        Assert.True(OpenFigiMappings.IsPlaceholder("9990620D"));
        Assert.False(OpenFigiMappings.IsPlaceholder("COHR"));
        Assert.False(OpenFigiMappings.IsPlaceholder("D"));
        Assert.False(OpenFigiMappings.IsPlaceholder("12D3"));
        Assert.Equal("""[{"idType":"ID_ISIN","idValue":"US1924791031","includeUnlistedEquities":true}]""", OpenFigiMappings.Request(["US1924791031"]));

        var refusedCount = Assert.Throws<FormatException>(() => OpenFigiMappings.Parse("""[{"warning":"No identifier found."}]""", ["US1924791031", "US21870Q1058"]));
        Assert.Contains("answered 1 entries for 2 identifiers", refusedCount.Message, StringComparison.Ordinal);
        Assert.Throws<FormatException>(() => OpenFigiMappings.Parse("""{"warning":"No identifier found."}""", ["US1924791031"]));
        Assert.Throws<FormatException>(() => OpenFigiMappings.Parse("not json", ["US1924791031"]));
        Assert.Throws<ArgumentOutOfRangeException>(() => OpenFigiMappings.Request([.. MappedIdentifiers.Take(11)]));
    }
}
