using System.Globalization;
using System.Text;
using EquityBrief.Core.Providers;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 15.3 correction of 2026-10-05: a holding's code is kept only where its closes move in step
// with the fund's values a share over the quarters it was matched, the holding's codes by name read in place of one that
// does not, and a company renamed since matched to a code its fund held by ISIN within a year where that code's close
// equals its value a share to the cent at two quarter ends or more.
// see: A holding's code is kept only where its closes move in step with the fund's values a share from one quarter end to the next
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
    // provider's closes before a spin-off divided down. Steady Steel, valued at $50, its name's code STDY at $40 every
    // quarter, its closes before a later corporate action adjusted. Old Name, held the first two quarters at $37.21 and
    // $41.05, renamed New Name and held under its new ISIN the last two at $44.10 and $39.99, its code NEWN by that ISIN at
    // those closes in all four. Lone, held the first two at $25.00 and $25.30, SPUN's close to the cent in the first alone
    // and moving in step with it.
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
                ["0000000002-19-000011"] = Valued(TrackedQuarters[0], ("Wander Corp", "US1000000003", 50m), ("Spun Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("Old Name Inc", "US4000000000", 37.21m), ("Lone Co", "US6000000008", 25m)),
                ["0000000002-20-000012"] = Valued(TrackedQuarters[1], ("Wander Corp", "US1000000003", 50m), ("Spun Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("Old Name Inc", "US4000000000", 41.05m), ("Lone Co", "US6000000008", 25.30m)),
                ["0000000002-20-000013"] = Valued(TrackedQuarters[2], ("Wander Corp", "US1000000003", 50m), ("Spun Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("New Name Inc", "US5000000009", 44.10m)),
                ["0000000002-20-000014"] = Valued(TrackedQuarters[3], ("Wander Corp", "US1000000003", 50m), ("Spun Inc", "US2000000002", 50m), ("Steady Steel Co", "US3000000001", 50m), ("New Name Inc", "US5000000009", 39.99m)),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [$"{FundSchedules.Filings[0].Accession}/{FundSchedules.Filings[0].Document}"] = Schedules("December 31, 2018", (FundSchedules.Titles["SML"], [])),
                [$"{FundSchedules.Filings[1].Accession}/{FundSchedules.Filings[1].Document}"] = Schedules("March 31, 2019", (FundSchedules.Titles["SML"], [])),
            }),
        new RecordedSymbolListFeed(
            """
            [
              { "Code": "SPUN", "Name": "Spun Inc", "Exchange": "NYSE", "Type": "Common Stock", "Isin": "US2000000002" },
              { "Code": "STDY", "Name": "Steady Steel Co", "Exchange": "NYSE", "Type": "Common Stock", "Isin": null },
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

    [Fact]
    public async Task AHoldingsCodeIsKeptOnlyWhereItsClosesMoveInStepAndARenamedCompanyIsMatchedToTheCodeItsFundHeldByIsin()
    {
        using var store = new TemporaryStore().Migrated();

        string[] wander = ["50", "80", "30", "65"];
        string[] spun = ["25", "25", "50", "50"];
        string[] renamed = ["37.21", "41.05", "44.10", "39.99"];

        for (var at = 0; at < TrackedQuarters.Length; at++)
        {
            PulledOn(store, "WNDR_old", TrackedQuarters[at], wander[at]);
            PulledOn(store, "SPUN", TrackedQuarters[at], spun[at]);
            PulledOn(store, "STDY", TrackedQuarters[at], "40");
            PulledOn(store, "NEWN", TrackedQuarters[at], renamed[at]);
        }

        var (holdings, symbols) = TrackedAnswers();
        var prices = new SessionsOn(new Dictionary<string, DateOnly[]>(StringComparer.Ordinal)
        {
            ["WNDRQ"] = [.. TrackedQuarters.Select(quarter => DateOnly.ParseExact(quarter, "yyyy-MM-dd", CultureInfo.InvariantCulture))],
        });
        var outcome = await HistoryPull.PullHoldingsAsync(holdings, symbols, PullClock(), store.DatabaseFile, "SML", "history-pull-holdings-1", prices: prices);

        // Wander's code by ISIN wanders and is matched to none, its delisted listing by name moving in step in its place;
        // Spun's steps once to a level it holds and is kept; Steady Steel's closes sit a fifth under its value a share at
        // every quarter end and move in step, so its name's code is kept where the price alone refused it; Old Name is
        // matched to the code its fund held by its new ISIN, equal to the cent twice; Lone, equal once, to none.
        Assert.Equal(
            [
                "2019-09-30 Lone Co |", "2019-09-30 Old Name Inc NEWN|held", "2019-09-30 Spun Inc SPUN|isin", "2019-09-30 Steady Steel Co STDY|name", "2019-09-30 Wander Corp WNDRQ|name",
                "2019-12-31 Lone Co |", "2019-12-31 Old Name Inc NEWN|held", "2019-12-31 Spun Inc SPUN|isin", "2019-12-31 Steady Steel Co STDY|name", "2019-12-31 Wander Corp WNDRQ|name",
                "2020-03-31 New Name Inc NEWN|isin", "2020-03-31 Spun Inc SPUN|isin", "2020-03-31 Steady Steel Co STDY|name", "2020-03-31 Wander Corp WNDRQ|name",
                "2020-06-30 New Name Inc NEWN|isin", "2020-06-30 Spun Inc SPUN|isin", "2020-06-30 Steady Steel Co STDY|name", "2020-06-30 Wander Corp WNDRQ|name",
            ],
            TextRows(store, "SELECT period || ' ' || name || ' ' || COALESCE(ticker, '') || '|' || COALESCE(matched_by, '') FROM pulled_holding ORDER BY period, name;"));

        Assert.Equal(["WNDR_old for Wander Corp, 4 quarter(s)"], outcome.NotInStep);
        Assert.Equal(2, outcome.Held);
        Assert.Contains("18 holding(s) of common stock, 6 matched by ISIN, 8 by name alone, 0 of them by its wider reading, 2 by a code its fund held by ISIN within a year, and 2 by none", HistoryPull.Detail(outcome), StringComparison.Ordinal);
        Assert.Contains("; not in step: WNDR_old for Wander Corp, 4 quarter(s)", HistoryPull.Detail(outcome), StringComparison.Ordinal);

        // The provider asked about WNDRQ at each quarter's end, the one code a holding's name read that the pulled history
        // holds no close of, and about no code the fund held by ISIN.
        Assert.Equal(TrackedQuarters.Select(quarter => $"WNDRQ {quarter}"), prices.Asked.Order(StringComparer.Ordinal));
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
    }
}
