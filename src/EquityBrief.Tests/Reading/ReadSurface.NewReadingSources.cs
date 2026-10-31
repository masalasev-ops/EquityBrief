using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.6: the Run page's line counting the ratings refresh's runs against the five its cap is read on, with
// the asks they made, the failed among them and the stocks holding an answer from Yahoo; and the Ledger page's line
// saying where the readings whose sources keep no history start, each said not held yet where the store holds none.
// see: The ratings refresh asks Yahoo for the members whose stored answer is oldest or none after the labeller, under the chart page's daily cap less the page's reserve
// see: A new reading joins the ledger with its history where its source keeps one, and from the night it is first stored where it keeps none
public partial class ReadSurface
{
    // The rows of sections 15.10 and 15.19 this check reaches from 21.6.
    internal static readonly string[] NewReadingSourceClaims =
    [
        CheckReach.Key("15.10 Run", "What else is waiting on a count, the ratings refresh's runs against five with the asks they made and the failed among them and the stocks holding a stored answer"),
        CheckReach.Key(Scope.LedgerPage, "The readings' sources"),
    ];

    static void AnalystAnswer(TemporaryStore store, string ticker, string askedAt) =>
        store.Execute(
            "INSERT INTO analyst_view (ticker, asked_at, answered_at, months, actions) "
            + $"VALUES ('{ticker}', '{askedAt}', '{askedAt}', '[]', '[]');");

    [Fact]
    public async Task TheRunPageCountsTheRatingsRefreshsRunsAgainstFiveWithTheirAsksAndTheStocksHoldingAnAnswer()
    {
        using var store = new TemporaryStore().Migrated();

        // Two runs of the refresh, the first asking 80 of which 2 failed and the second stopping after 3 failed asks in a
        // row, a run of another stage that is not counted, and three stocks holding answers, one of them two.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES "
            + "('analysts-refresh-20261031T040600Z', 'analysts-refresh', '2026-10-31T04:06:00Z', '2026-10-31T04:12:00Z', 'ok', 78, 0, 240, '0', '{\"members\":1505,\"asked\":80,\"answered\":78,\"noAnalysts\":0,\"failed\":2,\"cap\":80,\"stopped\":\"cap reached\",\"each\":[]}'),"
            + "('analysts-refresh-20261101T040600Z', 'analysts-refresh', '2026-11-01T04:06:00Z', '2026-11-01T04:07:00Z', 'stopped', 0, 0, 9, '0', '{\"members\":1505,\"asked\":3,\"answered\":0,\"noAnalysts\":0,\"failed\":3,\"cap\":80,\"stopped\":\"Yahoo refused\",\"each\":[]}'),"
            + "('analysts-20261031T040600Z-AAPL', 'analysts', '2026-10-31T04:06:00Z', '2026-10-31T04:06:02Z', 'answered', 1, 0, 3, '0', '{\"ticker\":\"AAPL\",\"askedAt\":\"2026-10-31T04:06:00Z\",\"detail\":\"\"}');");
        AnalystAnswer(store, "AAPL", "2026-10-31T04:06:00Z");
        AnalystAnswer(store, "AAPL", "2026-10-20T15:00:00Z");
        AnalystAnswer(store, "MSFT", "2026-10-31T04:06:04Z");
        AnalystAnswer(store, "KEYS", "2026-10-31T04:06:08Z");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var region = Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-10-30")), "<section class=\"calibration\".*?</section>"));
        var line = Regex.Match(region, "<li data-trigger=\"ratings refresh\" data-count=\"(\\d+)\" data-of=\"(\\d+)\">(.*?)</li>");

        Assert.True(line.Success, "The Run page draws no line for the ratings refresh.");
        Assert.Equal(("2", "5"), (line.Groups[1].Value, line.Groups[2].Value));
        Assert.Equal(
            "2 of 5: run(s) of the ratings refresh, against the five its cap of 80 asks of Yahoo a New York day is settled from: 83 ask(s) made, 5 of them failed, and 3 stock(s) holding a stored answer",
            line.Groups[3].Value);
    }

    [Fact]
    public async Task TheLedgerPageSaysWhereTheReadingsWithNoHistoryOfTheirOwnStartAndThatNoneIsHeldWhereTheStoreHoldsNone()
    {
        using var store = new TemporaryStore().Migrated();

        LedgerYearRow(store, "pullback", 2025, 40, 10, 0, 0, 40, "0.1", "0.05", "-1,-0.5,0,0.25,0.5,1,1.5,2,3");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<string> Line()
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/ledger?universe=500&family=pullback"));

            return Assert.Single(Blocks(page, "<p class=\"ledger-sources\".*?</p>"));
        }

        // Nothing stored: each source said not held yet.
        Assert.Equal(
            "<p class=\"ledger-sources\" data-first-fetch=\"none\" data-first-night=\"none\" data-first-settlement=\"none\" data-analyst-stocks=\"0\">"
            + "The estimate revision reads the analysts' consensus, which its source states only on the day it is asked, so it holds nothing before a stock's first quarters fetch, the first on no day yet, and from the night the night first asks it the night's own ask of every member. "
            + "The rating actions read each stock's newest answer from Yahoo, 0 stocks holding one, and none on a trading day after its ask. "
            + "FINRA's short interest reads every settlement from the first the store holds, none yet.</p>",
            await Line());

        // A quarters fetch filing the current year's trend and a later one, a quarter's trend fetched before both, the
        // night's own ask of two members on two nights, two settlements of FINRA's and one stock holding an answer.
        store.Execute(
            "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end) VALUES "
            + "('AAPL', '2026-09-28T01:10:00Z', '0y', '2026-09-30'), ('MSFT', '2026-10-02T01:10:00Z', '0y', '2027-06-30'), ('KEYS', '2026-09-20T01:10:00Z', '0q', '2026-10-31');"
            + "INSERT INTO estimate_trend_night (ticker, session_date, period_end, run_id) VALUES "
            + "('AAPL', '2026-10-30', '2026-09-30', 'night-a'), ('MSFT', '2026-11-02', '2027-06-30', 'night-b');"
            + "INSERT INTO short_interest (ticker, settlement_date, published_on, shares_short, observed_at, run_id) VALUES "
            + "('AAPL', '2017-12-29', '2018-01-09', 100, '2026-10-30T12:00:00Z', 'short-interest-history-x'), ('AAPL', '2018-01-12', '2018-01-23', 90, '2026-10-30T12:00:00Z', 'short-interest-history-x');");
        AnalystAnswer(store, "HPQ", "2026-10-22T15:00:00Z");

        Assert.Equal(
            "<p class=\"ledger-sources\" data-first-fetch=\"28 Sep 2026\" data-first-night=\"30 Oct 2026\" data-first-settlement=\"29 Dec 2017\" data-analyst-stocks=\"1\">"
            + "The estimate revision reads the analysts' consensus, which its source states only on the day it is asked, so it holds nothing before a stock's first quarters fetch, the first on 28 Sep 2026, and from 30 Oct 2026 the night's own ask of every member. "
            + "The rating actions read each stock's newest answer from Yahoo, 1 stock holding one, and none on a trading day after its ask. "
            + "FINRA's short interest reads every settlement from 29 Dec 2017.</p>",
            await Line());
    }
}
