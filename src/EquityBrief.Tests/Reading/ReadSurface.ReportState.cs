using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 11.3: tonight's rows and the selected name's region state what the queue holds
// for a name, being written or queued and when, beside or in place of what it holds written.
public partial class ReadSurface
{
    // A row's own state, off the link that opens the name, which every row draws once.
    static IReadOnlyDictionary<string, string> RowStates(string page) =>
        Regex.Matches(page, "href=\"#/name/([A-Z]+)\" data-report-state=\"([^\"]*)\" data-researched=")
            .ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal);

    [Fact]
    public void EachStateOfANamesReportIsDrawnOnItsRowAndInTheSelectedRegion()
    {
        var night = new DateOnly(2026, 9, 22);
        ListingCell[] rows =
        [
            new("ZZZA", night, 4, 10, 10m, [ShortlistSeries.AtEntryZone], ResearchedOn: new DateOnly(2026, 9, 20)),
            new("ZZZB", night, 3, 10, 10m, [ShortlistSeries.AtEntryZone], Queue: new QueueState(QueueState.Writing, "2026-09-22T23:40:00Z", "being written since the instant")),
            new("ZZZC", night, 2, 10, 10m, [ShortlistSeries.AtEntryZone], Queue: new QueueState(QueueState.Queued, "2026-09-23T04:00:00Z", "queued, and starts at the instant")),
            new("ZZZD", night, 1, 10, 10m, [ShortlistSeries.AtEntryZone]),
        ];

        var list = new MarkRenderer().TonightList(rows, SinglePageApp.TonightDrawn);

        Assert.Equal(
            new Dictionary<string, string> { ["ZZZA"] = "written", ["ZZZB"] = "writing", ["ZZZC"] = "queued", ["ZZZD"] = "unwritten" },
            RowStates(list));

        // A name queued or being written says when, and no row but the one holding nothing
        // written and nothing queued carries the control asking for a report.
        Assert.Contains("<span class=\"report-state\" data-report-state=\"writing\" data-at=\"2026-09-22T23:40:00Z\">being written since the instant</span>", list, StringComparison.Ordinal);
        Assert.Contains("<span class=\"report-state\" data-report-state=\"queued\" data-at=\"2026-09-23T04:00:00Z\">queued, and starts at the instant</span>", list, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.AskForAReport("ZZZD"), list, StringComparison.Ordinal);

        foreach (var ticker in new[] { "ZZZA", "ZZZB", "ZZZC" })
        {
            Assert.DoesNotContain(MarkRenderer.AskForAReport(ticker), list, StringComparison.Ordinal);
        }

        // And the selected name's region says the same, in place of the line saying none is
        // written and the control, where the queue holds a request for it.
        var writing = SelectedCard(TonightWith(rows, "ZZZB"));

        Assert.Contains("A report for ZZZB is being written since the instant.", WordsOf(writing), StringComparison.Ordinal);
        Assert.Contains("data-report-state=\"writing\" data-at=\"2026-09-22T23:40:00Z\"", writing, StringComparison.Ordinal);
        Assert.DoesNotContain("research-control", writing, StringComparison.Ordinal);
        Assert.DoesNotContain("No report is written", WordsOf(writing), StringComparison.Ordinal);

        var queued = SelectedCard(TonightWith(rows, "ZZZC"));

        Assert.Contains("A report for ZZZC is queued, and starts at the instant.", WordsOf(queued), StringComparison.Ordinal);
        Assert.DoesNotContain("research-control", queued, StringComparison.Ordinal);

        Assert.Contains("Open the full report for ZZZA, written 2026-09-20", WordsOf(SelectedCard(TonightWith(rows, "ZZZA"))), StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.AskForAReport("ZZZD"), SelectedCard(TonightWith(rows, "ZZZD")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachStateOfANamesReportIsReadBackOffBothSurfacesAgainstTheStoreInBothDirections()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        // The fixture's night draws two names, KEYS and MSFT, so the four states are read over two
        // arrangements of the store. First KEYS is being written by a pass started at 21:50 and
        // MSFT holds a written report its request wrote; then KEYS is queued with nothing ahead of
        // it and MSFT holds nothing. One pass of four minutes ran to its end earlier, and the page
        // is drawn at 22:00.
        store.Execute(
            "INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids) VALUES "
            + "('MSFT', 'The short version', 1, '2026-09-08', 'recorded', 'accepted', 'Written.', '[]');"
            + "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state, settled_at, run_id) VALUES "
            + "('MSFT', '2026-09-05T12:00:00Z', 'name', 'paid', 'written', '2026-09-05T12:04:00Z', 'research-20260905T120000Z-MSFT'),"
            + "('KEYS', '2026-09-08T21:49:00Z', 'list', 'paid', 'writing', NULL, NULL);"
            + "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend) VALUES "
            + "('research-20260901T100000Z-DGX', 'research', '2026-09-01T10:03:00Z', '2026-09-01T10:04:00Z', 'ok', 0, 0, 0, '0'),"
            + "('research-20260908T215000Z-KEYS', 'fundamentals', '2026-09-08T21:50:00Z', '2026-09-08T21:51:00Z', 'ok', 0, 0, 1, '0');");

        using var host = new PassHost(store.Root) { Clock = FixedClock.At(UtcAt("2026-09-08T22:00:00Z"), SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        var drawn = new List<string>();

        foreach (var arrangement in new[] { 1, 2 })
        {
            if (arrangement == 2)
            {
                store.Execute(
                    "UPDATE research_request SET state = 'outstanding' WHERE ticker = 'KEYS';"
                    + "DELETE FROM research_section WHERE ticker = 'MSFT';");
            }

            // Each name tonight's list draws, being every name that fired on the newest night,
            // in the state the store holds it in, read off the store rather than off the page.
            var expected = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var row in Rows(store, "SELECT ticker FROM listing WHERE fired_count > 0 AND session_date = (SELECT MAX(session_date) FROM listing);"))
            {
                var ticker = row[0];
                var waiting = Rows(store, $"SELECT state FROM research_request WHERE ticker = '{ticker}' AND state IN ('outstanding', 'writing');");
                var written = Rows(store, $"SELECT COUNT(*) FROM research_section WHERE ticker = '{ticker}' AND status = 'accepted' AND section <> 'The key under each figure';").Single()[0] != "0";

                expected[ticker] = waiting.Count > 0
                    ? waiting.Single()[0] == "writing" ? QueueState.Writing : QueueState.Queued
                    : written ? "written" : "unwritten";
            }

            var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/tonight"));

            // Every listed name drawn in the store's state, and no state drawn the store does not hold.
            Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), RowStates(page).OrderBy(pair => pair.Key, StringComparer.Ordinal));

            drawn.AddRange(expected.Values);

            // The selected name's region, name by name, against the same states.
            foreach (var (ticker, state) in expected)
            {
                var card = SelectedCard(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight?name={ticker}")));

                switch (state)
                {
                    case QueueState.Writing:
                        Assert.Contains("data-report-state=\"writing\" data-at=\"2026-09-08T21:50:00Z\">being written since 2026-09-08 17:50 New York (UTC-04:00), 21:50 UTC</span>", page, StringComparison.Ordinal);
                        Assert.Contains($"A report for {ticker} is being written since 2026-09-08 17:50 New York (UTC-04:00), 21:50 UTC.", WordsOf(card), StringComparison.Ordinal);
                        Assert.DoesNotContain("research-control", card, StringComparison.Ordinal);
                        break;
                    case QueueState.Queued:
                        Assert.Contains("data-report-state=\"queued\" data-at=\"2026-09-08T22:00:00Z\">queued, and starts now</span>", page, StringComparison.Ordinal);
                        Assert.Contains($"A report for {ticker} is queued, and starts now.", WordsOf(card), StringComparison.Ordinal);
                        Assert.DoesNotContain("research-control", card, StringComparison.Ordinal);
                        break;
                    case "written":
                        Assert.Contains($"Open the full report for {ticker}, written 2026-09-08", WordsOf(card), StringComparison.Ordinal);
                        Assert.DoesNotContain("research-control", card, StringComparison.Ordinal);
                        break;
                    default:
                        Assert.Contains($"No report is written for {ticker} yet.", WordsOf(card), StringComparison.Ordinal);
                        Assert.Contains(MarkRenderer.AskForAReport(ticker), card, StringComparison.Ordinal);
                        break;
                }
            }
        }

        // The two arrangements between them drew each of the four states.
        Assert.Equal(["queued", "unwritten", "writing", "written"], drawn.Order(StringComparer.Ordinal));
    }
}
