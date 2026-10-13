using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.6: the Queue. Settled requests a row a stock and New York day of asking with the newest's outcome and
// how many were asked, over the fourteen New York days to the page's own with the older behind a link; the research
// passes no request names a row a New York day with their reports and cost; and every instant in New York's time with
// UTC beneath the pointer.
// see: The queue page draws settled requests a row a stock and New York day over fourteen days, and the passes no request names a row a day
// see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
public partial class ReadSurface
{
    // The rows 19.6 adds that this check reaches: the queue's passes written by hand and section 17's window.
    internal static readonly string[] QueueDayRows =
    [
        CheckReach.Key("15.15 Queue", "Written by hand"),
        CheckReach.Key(Scope.LimitsTable, "Queue window"),
    ];

    static RequestRow Asked(string ticker, string askedAt, string state, string? settledAt = null, string? runId = null) =>
        new(ticker, UtcAt(askedAt), "list", "paid", state, settledAt is null ? null : UtcAt(settledAt), runId, null);

    [Fact]
    public void SettledRequestsAreARowAStockAndNewYorkDayWithTheNewestOutcomeAndHowManyWereAsked()
    {
        // The page drawn at noon UTC on 2026-10-13, whose New York day is the 13th, so the fourteen days run from
        // 2026-09-30. AAPL asked at 03:30Z and 04:30Z on the 12th, 23:30 on the 11th and 00:30 on the 12th in New York:
        // two days. NVDA asked three times on the 12th in New York, the newest refused. KEYS outstanding and MSFT being
        // written, in no row. OLD asked on 2026-09-29 in New York, the day before the window; EDGE on 2026-09-30, its
        // first day.
        RequestRow[] rows =
        [
            Asked("AAPL", "2026-10-12T03:30:00Z", ResearchRequests.Written, "2026-10-12T03:50:00Z"),
            Asked("AAPL", "2026-10-12T04:30:00Z", ResearchRequests.Written, "2026-10-12T04:50:00Z"),
            Asked("NVDA", "2026-10-12T13:00:00Z", ResearchRequests.Written, "2026-10-12T13:20:00Z"),
            Asked("NVDA", "2026-10-12T15:00:00Z", ResearchRequests.Withdrawn, "2026-10-12T15:01:00Z"),
            Asked("NVDA", "2026-10-12T20:00:00Z", ResearchRequests.Refused, "2026-10-12T20:02:00Z"),
            Asked("KEYS", "2026-10-13T01:00:00Z", ResearchRequests.Outstanding),
            Asked("MSFT", "2026-10-13T02:00:00Z", ResearchRequests.Writing),
            Asked("OLD", "2026-09-30T03:00:00Z", ResearchRequests.Written, "2026-09-30T03:20:00Z"),
            Asked("EDGE", "2026-09-30T04:00:00Z", ResearchRequests.Written, "2026-09-30T04:20:00Z"),
        ];

        var now = UtcAt("2026-10-13T12:00:00Z");
        var first = QueueScreen.FirstDay(now, NewYork);

        Assert.Equal(new DateOnly(2026, 9, 30), first);

        // Within a day the newest asked first, and the newest request's outcome with the count of every request that day.
        Assert.Equal(
            [("NVDA", new DateOnly(2026, 10, 12), ResearchRequests.Refused, 3), ("AAPL", new DateOnly(2026, 10, 12), ResearchRequests.Written, 1), ("AAPL", new DateOnly(2026, 10, 11), ResearchRequests.Written, 1), ("EDGE", new DateOnly(2026, 9, 30), ResearchRequests.Written, 1)],
            QueueScreen.Settled(rows, NewYork, first).Select(day => (day.Ticker, day.Day, day.Latest.State, day.Asked)));

        // Every day where all are asked for, the one before the window among them.
        var every = QueueScreen.Settled(rows, NewYork, null);

        Assert.Equal(5, every.Count);
        Assert.Equal(("OLD", new DateOnly(2026, 9, 29)), (every[^1].Ticker, every[^1].Day));

        // The page's own: the window's four rows and one older behind the link, and every row with no link back but one.
        var window = QueueScreen.Drawn(rows, [], NewYork, now, all: false);
        var all = QueueScreen.Drawn(rows, [], NewYork, now, all: true);

        Assert.Equal((4, 1, false, QueueScreen.Window), (window.Settled.Count, window.Older, window.All, window.Days));
        Assert.Equal((5, 0, true), (all.Settled.Count, all.Older, all.All));

        var drawn = WebUtility.HtmlDecode(new SinglePageApp().QueueRegion([], window));

        Assert.Equal(
            ["NVDA|2026-10-12|refused|3", "AAPL|2026-10-12|written|1", "AAPL|2026-10-11|written|1", "EDGE|2026-09-30|written|1"],
            Regex.Matches(drawn, "<tr data-ticker=\"([^\"]+)\" data-asked-at=\"[^\"]+\" data-state=\"([^\"]+)\" data-day=\"([^\"]+)\" data-asked=\"(\\d+)\">")
                .Select(row => $"{row.Groups[1].Value}|{row.Groups[3].Value}|{row.Groups[2].Value}|{row.Groups[4].Value}"));
        Assert.Contains("<td class=\"q-asked\">3 times</td>", drawn, StringComparison.Ordinal);
        Assert.Contains("<td class=\"q-asked\">once</td>", drawn, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"queue-older\" data-all=\"false\" data-older=\"1\"><a href=\"{SinglePageApp.QueueRoute}?{SinglePageApp.QueueAllKey}=1\">1 older row: every day</a></p>", drawn, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"queue-older\" data-all=\"true\"><a href=\"{SinglePageApp.QueueRoute}\">Only the last 14 New York days</a></p>", new SinglePageApp().QueueRegion([], all), StringComparison.Ordinal);

        // A window holding every row draws no link at all.
        Assert.DoesNotContain("queue-older", new SinglePageApp().QueueRegion([], QueueScreen.Drawn(rows[..5], [], NewYork, now, all: false)), StringComparison.Ordinal);
    }

    [Fact]
    public void ThePassesNoRequestNamesAreARowANewYorkDayWithTheirReportsAndCost()
    {
        // Three passes on the 11th in New York, one of them two paid calls; one on the 12th the night's request names, one
        // a request being written is under and one of NVDA's on the 12th; a theme's run is no research pass. ETN's pass
        // started at 03:00Z on the 12th, 23:00 on the 11th in New York.
        (string RunId, decimal Spend)[] calls =
        [
            ("research-20261011T140000Z-BDX", 0.05m),
            ("research-20261011T140000Z-BDX", 0.02m),
            ("research-20261011T150000Z-GM", 0.06m),
            ("research-20261012T030000Z-ETN", 0.04m),
            ("research-20261012T140000Z-NVDA", 0.07m),
            ("research-20261012T150000Z-DGX", 0.03m),
            ("research-20261012T160000Z-PBI", 0.08m),
            ("theme-20261012T150000Z-semis", 0.09m),
        ];
        RequestRow[] requests =
        [
            Asked("DGX", "2026-10-12T14:59:00Z", ResearchRequests.Written, "2026-10-12T15:20:00Z", "research-20261012T150000Z-DGX"),
            Asked("PBI", "2026-10-12T15:59:00Z", ResearchRequests.Writing),
        ];

        var days = QueueScreen.ByHand(calls, requests, NewYork, new DateOnly(2026, 9, 30));

        Assert.Equal([(new DateOnly(2026, 10, 12), 1, 0.07m), (new DateOnly(2026, 10, 11), 3, 0.17m)], days.Select(day => (day.Day, day.Reports, day.Cost)));
        Assert.Empty(QueueScreen.ByHand(calls, requests, NewYork, new DateOnly(2026, 10, 13)));

        // A pass of PBI's that started before its request was asked is one run by hand.
        Assert.Contains(QueueScreen.ByHand([("research-20261012T155800Z-PBI", 0.01m)], requests, NewYork, null), day => day.Reports == 1 && day.Cost == 0.01m);

        // Drawn as one row a day with its reports and its cost as the run page states money, the cost whole on the row.
        var drawn = new SinglePageApp().QueueRegion([], QueueScreen.Drawn(requests, calls, NewYork, UtcAt("2026-10-13T12:00:00Z"), all: false));

        Assert.Contains("<div class=\"queue-part\" data-region=\"by-hand\" data-rows=\"2\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<tr data-day=\"2026-10-11\" data-reports=\"3\" data-cost=\"0.17\"><td class=\"q-day\">2026-10-11</td><td class=\"num\">3</td><td class=\"num\">$0.17</td></tr>", drawn, StringComparison.Ordinal);
        Assert.Contains("<tr data-day=\"2026-10-12\" data-reports=\"1\" data-cost=\"0.07\">", drawn, StringComparison.Ordinal);

        // And a window holding none says so.
        Assert.Contains("<p class=\"degraded\" data-by-hand=\"none\">No report was written without a request over the last 14 New York days.</p>", new SinglePageApp().QueueRegion([], QueueScreen.Drawn(requests, calls, NewYork, UtcAt("2026-11-30T12:00:00Z"), all: false)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheQueuePageGroupsWhatSettledAndDrawsThePassesByHandOverTheWindowReadBackAgainstTheStore()
    {
        using var store = new TemporaryStore().Migrated();

        // BDX asked three times on 2026-10-12 in New York, the newest refused; a regenerate of GM on the same day with two
        // paid calls and no request; and IT asked on 2026-09-01, older than the window. The page is drawn at 12:00Z on
        // 2026-10-13.
        store.Execute(
            "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state, settled_at, run_id, reason) VALUES "
            + "('BDX', '2026-10-12T13:00:00Z', 'list', 'paid', 'written', '2026-10-12T13:20:00Z', 'research-20261012T130100Z-BDX', NULL),"
            + "('BDX', '2026-10-12T14:00:00Z', 'name', 'paid', 'withdrawn', '2026-10-12T14:01:00Z', NULL, 'taken out of the queue before it was written'),"
            + "('BDX', '2026-10-12T15:00:00Z', 'name', 'paid', 'refused', '2026-10-12T15:30:00Z', 'research-20261012T150100Z-BDX', 'the pass came to unavailable'),"
            + "('IT', '2026-09-01T15:00:00Z', 'list', 'paid', 'written', '2026-09-01T15:20:00Z', 'research-20260901T150100Z-IT', NULL);"
            + "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, model_calls, network_requests, spend) VALUES "
            + "('research-20261012T130100Z-BDX', 'research call: The two cases', '2026-10-12T13:05:00Z', '2026-10-12T13:05:30Z', 'ok', 1, 1, '0.04'),"
            + "('research-20261012T170000Z-GM', 'research call: The two cases', '2026-10-12T17:05:00Z', '2026-10-12T17:05:30Z', 'ok', 1, 1, '0.05'),"
            + "('research-20261012T170000Z-GM', 'research call: The risks', '2026-10-12T17:06:00Z', '2026-10-12T17:06:30Z', 'ok', 1, 1, '0.0125'),"
            + "('research-20261012T170000Z-GM', 'research call: The risks, trial', '2026-10-12T17:07:00Z', '2026-10-12T17:07:30Z', 'ok', 1, 1, '0.5');");

        using var host = new PassHost(store.Root) { Clock = FixedClock.At(UtcAt("2026-10-13T12:00:00Z"), SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/queue"));

        // BDX's day is one row of its newest request, refused at 11:30 in New York, asked three times; IT is behind the link.
        Assert.Matches("<div class=\"queue-part\" data-region=\"settled\" data-rows=\"1\">", page);
        Assert.Matches("<tr data-ticker=\"BDX\" data-asked-at=\"2026-10-12T15:00:00Z\" data-state=\"refused\" data-day=\"2026-10-12\" data-asked=\"3\">.*?<td class=\"q-asked\">3 times</td><td class=\"q-state\">refused</td><td class=\"q-when\"><time datetime=\"2026-10-12T15:30:00Z\" title=\"2026-10-12 15:30 UTC\">2026-10-12 11:30 ET</time></td><td>the pass came to unavailable</td></tr>", page);
        Assert.Contains("data-older=\"1\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-ticker=\"IT\"", page, StringComparison.Ordinal);

        // GM's regenerate is the one pass no request names, its trial's call left out of its cost.
        Assert.Contains("<tr data-day=\"2026-10-12\" data-reports=\"1\" data-cost=\"0.0625\"><td class=\"q-day\">2026-10-12</td><td class=\"num\">1</td><td class=\"num\">$0.06</td></tr>", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, "<tr data-day="));

        // The link draws every day: IT's row and a link back to the window.
        var every = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/queue?{SinglePageApp.QueueAllKey}=1"));

        Assert.Contains("<tr data-ticker=\"IT\" data-asked-at=\"2026-09-01T15:00:00Z\" data-state=\"written\" data-day=\"2026-09-01\" data-asked=\"1\">", every, StringComparison.Ordinal);
        Assert.Contains("data-all=\"true\"", every, StringComparison.Ordinal);

        // And the shell's script hands the link's query to the route.
        Assert.Contains("fetch('/screens/queue' + (query ? '?' + query : ''))", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
    }
}
