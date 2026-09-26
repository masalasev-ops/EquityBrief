using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// Section 15.16's watch list: the operator's own, up to twenty names of the index, drawn on a page of its
// own with what the swing filter said of each, and counted by one line on tonight's page.
// see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
public partial class ReadSurface
{
    static List<string> WatchRows(TemporaryStore store, string query)
    {
        var rows = new List<string>();

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile};Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = query;

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(reader.IsDBNull(0) ? "none" : Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture)!);
        }

        return rows;
    }

    static async Task<HttpResponseMessage> WatchPress(HttpClient client, string route, bool fromThePage = true)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, route) { Content = new StringContent(string.Empty) };

        if (fromThePage)
        {
            request.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);
        }

        return await client.SendAsync(request);
    }

    // Over the fixture's members: a press without the page's own header writes nothing, a name that is
    // not of the index is refused, a name is put on once and refused the second time, and taken off once
    // and refused the second time, each line saying what happened. Every screen tells the browser to keep
    // no copy, so a screen drawn again after a press reads the store again.
    [Fact]
    public async Task EachWatchListPressNeedsThePagesOwnHeaderAndAddsOnlyANameOfTheIndexOnce()
    {
        using var store = await FixtureExpectations.WithListings();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var member = WatchRows(store, "SELECT MIN(ticker) FROM listing WHERE session_date = (SELECT MAX(session_date) FROM listing);").Single();

        async Task<(HttpStatusCode Status, string Said)> Said(string route, bool fromThePage = true)
        {
            using var response = await WatchPress(client, route, fromThePage);

            return (response.StatusCode, WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
        }

        var unheaded = await Said(SinglePageApp.WatchPostRoute + member, fromThePage: false);

        Assert.Equal(HttpStatusCode.Forbidden, unheaded.Status);
        Assert.Empty(WatchRows(store, "SELECT ticker FROM watch_list;"));

        var outsider = await Said(SinglePageApp.WatchPostRoute + "ZZZZ");

        Assert.Equal((HttpStatusCode.Conflict, true), (outsider.Status, outsider.Said.Contains("ZZZZ was not added: it is not a member of the index tonight.", StringComparison.Ordinal)));
        Assert.Empty(WatchRows(store, "SELECT ticker FROM watch_list;"));

        var added = await Said(SinglePageApp.WatchPostRoute + member.ToLowerInvariant());

        Assert.Equal((HttpStatusCode.OK, true), (added.Status, added.Said.Contains($"{member} is on the watch list.", StringComparison.Ordinal)));
        Assert.Equal([member], WatchRows(store, "SELECT ticker FROM watch_list;"));

        var again = await Said(SinglePageApp.WatchPostRoute + member);

        Assert.Equal((HttpStatusCode.Conflict, true), (again.Status, again.Said.Contains($"{member} was already on the watch list.", StringComparison.Ordinal)));

        Assert.Equal(HttpStatusCode.Forbidden, (await Said(SinglePageApp.UnwatchPostRoute + member, fromThePage: false)).Status);
        Assert.Equal([member], WatchRows(store, "SELECT ticker FROM watch_list;"));

        var removed = await Said(SinglePageApp.UnwatchPostRoute + member);

        Assert.Equal((HttpStatusCode.OK, true), (removed.Status, removed.Said.Contains($"{member} is off the watch list.", StringComparison.Ordinal)));
        Assert.Empty(WatchRows(store, "SELECT ticker FROM watch_list;"));
        Assert.Equal(HttpStatusCode.Conflict, (await Said(SinglePageApp.UnwatchPostRoute + member)).Status);

        // No screen is kept by the browser.
        using var page = await client.GetAsync("/screens/watch");

        Assert.Equal("no-store", page.Headers.CacheControl?.ToString());
    }

    // Twenty names are held and a twenty-first is refused with the line saying the limit, nothing written.
    [Fact]
    public async Task TheWatchListHoldsTwentyNamesAndRefusesATwentyFirst()
    {
        using var store = new TemporaryStore().Migrated();

        var api = new ReadApi(store.DatabaseFile, FixedClock.At(Instant, SessionZones.UnitedStates));
        var members = Enumerable.Range(1, 21).Select(at => $"Z{at:00}").ToHashSet(StringComparer.Ordinal);

        foreach (var ticker in members.Order(StringComparer.Ordinal).Take(20))
        {
            Assert.True((await api.WatchAsync(ticker, members)).Written, ticker);
        }

        var refused = await api.WatchAsync("Z21", members);

        Assert.Equal((false, "Z21 was not added: the watch list holds 20, its limit, so take one out first."), (refused.Written, refused.Line));
        Assert.Equal(ReadApi.WatchLimit, (await api.WatchedAsync()).Count);
        Assert.Equal(20, ReadApi.WatchLimit);
    }

    // The page over the fixture's night, four names watched and added a day apart in an order that is not
    // the tickers': one the filter listed, one it stopped, one that passed every gate and was excluded, and
    // one with no gate row. Each row is read against the store: its company off the membership, its close
    // and day change worked from the stored bars, its trend off the newest ladder, its reward to risk off
    // its gate row, and the filter's word for it. The box is offered at four of twenty, and at twenty the
    // line saying the limit is drawn in its place. Tonight's page counts the names and links here, a name's
    // own page offers to stop watching a name watched and to watch one that is not, and the masthead links
    // here.
    [Fact]
    public async Task TheWatchListPageDrawsEachNameWithWhatTheSwingFilterSaidOfIt()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var members = WatchRows(store, $"SELECT ticker FROM listing WHERE session_date = '{night}' ORDER BY ticker;");

        Assert.True(members.Count >= 4, $"the fixture's night holds {members.Count} listing row(s), expected at least 4.");

        string passed = members[0], stopped = members[1], excluded = members[2], unanswered = members[3];
        const string exclusion = "earnings inside the holding window";

        Assert.Empty(WatchRows(store, $"SELECT ticker FROM gate_result WHERE session_date = '{night}';"));

        GateRow(store, night, new Member(passed, Passed: true, Rank: 1, RewardToRisk: 2.5));
        GateRow(store, night, new Member(stopped, Trend: false, RewardToRisk: 1.75));
        GateRow(store, night, new Member(excluded, RewardToRisk: 3.25, Exclusions: [exclusion]));
        store.Execute(
            "INSERT INTO watch_list (ticker, added_at) VALUES " +
            $"('{stopped}', '2026-09-01T12:00:00Z'), ('{passed}', '2026-09-02T12:00:00Z'), ('{excluded}', '2026-09-03T12:00:00Z'), ('{unanswered}', '2026-09-04T12:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/watch"));

        Assert.Contains($"<section class=\"watch\" data-night=\"{night}\" data-watched=\"4\" data-limit=\"20\">", page, StringComparison.Ordinal);
        Assert.Contains($"4 of 20 names, night of {night}", page, StringComparison.Ordinal);
        Assert.Contains("<form class=\"watch-control watch-add\" method=\"post\" action=\"/watch/\"><input name=\"ticker\" list=\"findable\"", page, StringComparison.Ordinal);
        Assert.Equal([stopped, passed, excluded, unanswered], Regex.Matches(page, "<tr data-ticker=\"([^\"]+)\" data-listed=").Select(match => match.Groups[1].Value));

        string RowOf(string ticker)
        {
            var at = page.IndexOf($"<tr data-ticker=\"{ticker}\" ", StringComparison.Ordinal);

            return page[at..page.IndexOf("</tr>", at, StringComparison.Ordinal)];
        }

        // Every row, each figure worked from the store rather than read back through the page's reader.
        foreach (var (ticker, added, ratio) in new[] { (stopped, "2026-09-01", "1.75"), (passed, "2026-09-02", "2.50"), (excluded, "2026-09-03", "3.25"), (unanswered, "2026-09-04", "none") })
        {
            var row = RowOf(ticker);
            var closes = WatchRows(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' AND session_date <= '{night}' ORDER BY session_date DESC LIMIT 2;")
                .Select(close => decimal.Parse(close, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();
            var change = (double)((closes[0] - closes[1]) / closes[1]) * 100;
            var trend = WatchRows(store, $"SELECT trend_state FROM ladder WHERE ticker = '{ticker}' AND as_of <= '{night}' ORDER BY as_of DESC LIMIT 1;").Single();
            var company = WatchRows(store, $"SELECT MAX(name) FROM membership WHERE ticker = '{ticker}';").Single();

            Assert.Contains($"<a href=\"{SinglePageApp.NameRoute}{ticker}\"><b>{ticker}</b></a>", row, StringComparison.Ordinal);
            Assert.Contains(company == "none" ? "</b></a></td>" : $"<span class=\"co\">{company}</span>", row, StringComparison.Ordinal);
            Assert.Contains(FormattableString.Invariant($"<td class=\"r num\">{closes[0]:0.00}</td><td class=\"r num\">{change:+0.00;-0.00;0.00}%</td><td>{trend}</td>"), row, StringComparison.Ordinal);
            Assert.Contains(ratio == "none" ? "</td><td class=\"r\"><span class=\"degraded\">none</span></td><td class=" : $"<td class=\"r num\">{ratio}</td>", row, StringComparison.Ordinal);
            Assert.Contains($"<td class=\"num\">{added}</td>", row, StringComparison.Ordinal);
            Assert.Contains($"action=\"{SinglePageApp.UnwatchPostRoute}\" data-ticker=\"{ticker}\"", row, StringComparison.Ordinal);
        }

        // What the filter said of each, read against the gate rows written above.
        Assert.Contains("data-listed=\"true\"><td class=\"num\">2</td>", RowOf(passed), StringComparison.Ordinal);
        Assert.Contains("<td class=\"listed\" data-filter=\"listed, number 1\">listed, number 1</td>", RowOf(passed), StringComparison.Ordinal);
        Assert.Contains("<td class=\"stopped\" data-filter=\"stopped at trend and strength: not an uptrend\">", RowOf(stopped), StringComparison.Ordinal);
        Assert.Contains($"<td class=\"stopped\" data-filter=\"excluded: {exclusion}\">", RowOf(excluded), StringComparison.Ordinal);
        Assert.Contains("<td class=\"stopped\" data-filter=\"no filter answer is stored for this night\">", RowOf(unanswered), StringComparison.Ordinal);

        foreach (var ticker in new[] { stopped, excluded, unanswered })
        {
            Assert.Contains("data-listed=\"false\"", RowOf(ticker), StringComparison.Ordinal);
        }

        // Tonight's line, a name page's press to stop watching, and the masthead's link.
        var tonight = await client.GetStringAsync("/screens/tonight");

        Assert.Contains($"<p class=\"watch-line\" data-watching=\"4\">4 name(s) watched. <a href=\"{SinglePageApp.WatchRoute}\">Open the watch list</a></p>", tonight, StringComparison.Ordinal);
        Assert.Contains($"action=\"{SinglePageApp.UnwatchPostRoute}\" data-ticker=\"{passed}\" data-watched=\"true\"><button type=\"submit\" class=\"btn-2\">Stop watching</button>", await client.GetStringAsync($"/screens/name/{passed}"), StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{SinglePageApp.WatchRoute}\" data-view=\"watch\">Watch list</a>", await client.GetStringAsync("/"), StringComparison.Ordinal);

        // At twenty, the line in place of the box.
        store.Execute("DELETE FROM watch_list;");
        store.Execute("INSERT INTO watch_list (ticker, added_at) VALUES " + string.Join(", ", Enumerable.Range(1, 20).Select(at => $"('Z{at:00}', '2026-09-01T12:00:00Z')")) + ";");

        var full = await client.GetStringAsync("/screens/watch");

        Assert.Contains("The watch list holds 20, its limit; take one out to add another.", full, StringComparison.Ordinal);
        Assert.DoesNotContain("watch-add", full, StringComparison.Ordinal);

        // None watched: tonight's line says so and still links here, and a name's page offers to watch it.
        store.Execute("DELETE FROM watch_list;");

        Assert.Contains($"action=\"{SinglePageApp.WatchPostRoute}\" data-ticker=\"{passed}\" data-watched=\"false\"><button type=\"submit\" class=\"btn-2\">Watch</button>", await client.GetStringAsync($"/screens/name/{passed}"), StringComparison.Ordinal);

        Assert.Contains($"<p class=\"watch-line\" data-watching=\"0\">No name is watched yet. <a href=\"{SinglePageApp.WatchRoute}\">Add names on the watch list</a></p>", await client.GetStringAsync("/screens/tonight"), StringComparison.Ordinal);
    }
}
