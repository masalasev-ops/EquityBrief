using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Reading;

// read-surface, 19.8: Tonight's one card of the stocks one gate short of a buy point across every setup, on the operator's
// addendum of 2026-10-15. Its count is the market line's and the sum of the family cards' own tables on every index, its
// rows run in the family order each family's as its own card orders them, its press is the stock's page's own, and a night
// before the newest draws no press and no link to Universe.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
// see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
public partial class ReadSurface
{
    // The row 19.8 adds that this check reaches.
    internal static readonly string[] CloseToBuyRows =
    [
        CheckReach.Key("15.7 Tonight", "Tonight's stocks one gate short"),
    ];

    static string CloseCard(string page) =>
        Assert.Single(Blocks(page, "<section class=\"card folded close-to-buy\".*?</section>"));

    // The rows a family's card draws behind its own expander, in its order, and the count the expander states.
    static (IReadOnlyList<string> Rows, int Count) FamilyCloseRows(string page, string family)
    {
        var card = Blocks(page, $"<section class=\"family-card\" data-family=\"{family}\".*?</section>");
        var close = card.Count == 0 ? [] : Blocks(card[0], "<details class=\"expander close-rows\".*?</details>");

        return close.Count == 0
            ? ([], 0)
            : ([.. Regex.Matches(close[0], "<tr data-ticker=\"([^\"]+)\" data-gate=\"[^\"]+\">").Select(match => match.Groups[1].Value)], int.Parse(Regex.Match(close[0], "data-close=\"(\\d+)\"").Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task TheCardOfTheStocksOneGateShortCountsWhatTheMarketLineAndTheFamilyCardsCountOnEveryIndex()
    {
        // The S&P 500: N1 one gate short of the pullback, its trade, and K3 of the breakout, its volume.
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));
        var card = CloseCard(page);
        var line = Assert.Single(Blocks(page, "<p class=\"market-line\".*?</p>"));
        var families = SetupFamilies.InPageOrder.Select(family => (family: family.Name, FamilyCloseRows(page, family.Name))).ToArray();

        Assert.Contains("data-count=\"2\"", card, StringComparison.Ordinal);
        Assert.Contains("data-close=\"2\"", line, StringComparison.Ordinal);
        Assert.Equal(2, families.Sum(family => family.Item2.Count));

        // Its rows: the family order, each family's rows as its own card orders them.
        Assert.Equal(
            families.SelectMany(family => family.Item2.Rows.Select(ticker => $"{family.family}|{ticker}")),
            Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\" data-family=\"([^\"]+)\"").Select(match => $"{match.Groups[2].Value}|{match.Groups[1].Value}"));
        Assert.Equal(["pullback|N1", "breakout|K3"], Regex.Matches(card, "<tr data-ticker=\"([^\"]+)\" data-family=\"([^\"]+)\"").Select(match => $"{match.Groups[2].Value}|{match.Groups[1].Value}"));

        // Folded to its line by default, the gate's words on the row, a press a row, the key and the Universe link.
        Assert.Contains("<details class=\"close-to-buy\" data-link-key=\"close\"><summary class=\"card-one-line\">2 stocks one gate short of a buy point</summary>", card, StringComparison.Ordinal);
        Assert.Contains("<td class=\"missed-gate\"><b>volume</b></td><td class=\"close-had\">the volume against its average</td>", card, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(card, "<form class=\"watch-control close-watch\" method=\"post\" action=\"/watch/\"").Count);
        Assert.Contains($"<p class=\"forming-key\">{MarkRenderer.CloseToBuyGuide}</p>", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"close-universe\"><a href=\"#/universe?page=1&universe=500&short=1\">See them with their readings on Universe</a></p>", card, StringComparison.Ordinal);

        // The market line's count opens it, and the link keeps it open.
        Assert.Contains($"<a class=\"close-open\" href=\"#/night/{TheSwitch}?universe=500&close=open\">2 close to a buy point</a>", line, StringComparison.Ordinal);
        Assert.Contains("<details class=\"close-to-buy\" data-link-key=\"close\" open>", CloseCard(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?close=open"))), StringComparison.Ordinal);

        // The S&P 400 and 600, whose nights store only the first part each member failed: the card says it reads none,
        // the market line counts none and no family card draws a table.
        using var indices = UniversesStore();

        indices.Execute(
            "INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced) VALUES " +
            $"('SML', '{IndexNight}', 2, 0.5, 1, '{IndexFamilies.Settings("SML")}', 0);");

        using var indexHost = new Host(indices.Root);
        using var indexClient = indexHost.CreateClient();

        foreach (var word in new[] { "400", "600" })
        {
            var index = WebUtility.HtmlDecode(await indexClient.GetStringAsync($"/screens/tonight/{IndexNight}?universe={word}"));

            Assert.Contains("data-count=\"none\"", CloseCard(index), StringComparison.Ordinal);
            Assert.Contains("this index's night stores only the first part each member failed, so no stock is read as one gate short", CloseCard(index), StringComparison.Ordinal);
            Assert.Contains("data-close=\"none\"", Assert.Single(Blocks(index, "<p class=\"market-line\".*?</p>")), StringComparison.Ordinal);
            Assert.Equal(0, SetupFamilies.InPageOrder.Sum(family => FamilyCloseRows(index, family.Name).Count));
        }
    }

    [Fact]
    public async Task TheCardsWatchPressAddsANameReadsWatchingForOneHeldAndIsRefusedOnATwentyFirstAndWithoutTheHeader()
    {
        using var store = await FamilyPagesStore();

        // N1 reports on Wednesday 2026-10-07, three sessions after the Friday night: the Monday, the Tuesday and the day.
        store.Execute("INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('N1', '2026-10-07', 'earnings', 'after', '{}', '2026-10-02T21:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        async Task<HttpStatusCode> Press(string ticker, bool fromThePage = true)
        {
            using var response = await WatchPress(client, SinglePageApp.WatchPostRoute + ticker, fromThePage);

            return response.StatusCode;
        }

        async Task<string> Page() => WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?close=open"));

        Assert.Equal(HttpStatusCode.Forbidden, await Press("N1", fromThePage: false));
        Assert.Empty(Strings(store, "SELECT ticker FROM watch_list;"));
        Assert.Contains("<span class=\"market-watch\" data-watching=\"0\">", await Page(), StringComparison.Ordinal);

        // A press adds the name: its row reads Watching and the market line's count moves.
        Assert.Equal(HttpStatusCode.OK, await Press("N1"));
        Assert.Equal(["N1"], Strings(store, "SELECT ticker FROM watch_list;"));

        var watching = await Page();
        var row = Assert.Single(Blocks(CloseCard(watching), "<tr data-ticker=\"N1\".*?</tr>"));

        Assert.Contains("data-watched=\"true\"", row, StringComparison.Ordinal);
        Assert.Contains("<span class=\"watching\" data-watching=\"true\">Watching</span>", row, StringComparison.Ordinal);
        Assert.DoesNotContain("<form", row, StringComparison.Ordinal);
        Assert.Contains("<span class=\"market-watch\" data-watching=\"1\">", watching, StringComparison.Ordinal);

        // Its sessions to its report on the card and on the watch list, counted alike; K3 holds no dated report.
        Assert.Contains("<td class=\"r num\" data-sessions=\"3\">3 sessions</td>", row, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r\"><span class=\"degraded\">no date</span></td>", Assert.Single(Blocks(CloseCard(watching), "<tr data-ticker=\"K3\".*?</tr>")), StringComparison.Ordinal);
        Assert.Contains("data-sessions=\"3\">3 session(s)</td>", Assert.Single(Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/watch")), "<tr data-ticker=\"N1\".*?</tr>")), StringComparison.Ordinal);

        // Again for a name held: refused, nothing written twice.
        Assert.Equal(HttpStatusCode.Conflict, await Press("N1"));
        Assert.Equal(["N1"], Strings(store, "SELECT ticker FROM watch_list;"));

        // A twenty-first is refused with its line, the list as it was.
        store.Execute("INSERT INTO watch_list (ticker, added_at) VALUES " + string.Join(", ", Enumerable.Range(1, 19).Select(at => $"('W{at:00}', '2026-10-02T21:00:00Z')")) + ";");

        using var refused = await WatchPress(client, SinglePageApp.WatchPostRoute + "K3", fromThePage: true);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("K3 was not added: the watch list holds 20, its limit, so take one out first.", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal(["20"], Strings(store, "SELECT COUNT(*) FROM watch_list;"));
    }

    [Fact]
    public async Task ANightBeforeTheNewestDrawsTheCardWithNoPressAndNoLinkToUniverse()
    {
        using var store = await FamilyPagesStore();

        // A later night stored, so the switch's night is no longer the newest.
        store.Execute("INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('N1', '2026-10-05', '[]', 0, '[]', '[]');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var card = CloseCard(WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?close=open")));

        Assert.Contains("data-count=\"2\" data-newest=\"false\"", card, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(card, "<tr data-ticker=").Count);
        Assert.DoesNotContain("watch-control", card, StringComparison.Ordinal);
        Assert.DoesNotContain(">Watch<", card, StringComparison.Ordinal);
        Assert.DoesNotContain("close-universe", card, StringComparison.Ordinal);
    }

    // The shell keeps the card's open state in the page's link with no redraw, and a count pressed opens the card and
    // brings it into view, read off the shell's script, the suite having no browser to run it in.
    [Fact]
    public async Task TheShellKeepsTheCardsOpenStateInTheLinkAndACountPressedOpensIt()
    {
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var shell = await client.GetStringAsync("/");

        Assert.Contains("if (details.open) { params.set(details.dataset.linkKey, 'open'); } else { params.delete(details.dataset.linkKey); }", shell, StringComparison.Ordinal);
        Assert.Contains("if (next !== location.hash) { history.replaceState(null, '', next); }", shell, StringComparison.Ordinal);
        Assert.Contains("const link = event.target instanceof Element ? event.target.closest('a.close-open') : null;", shell, StringComparison.Ordinal);
        Assert.Contains($"const card = link ? document.getElementById('{MarkRenderer.CloseToBuyId}') : null;", shell, StringComparison.Ordinal);
        Assert.Contains("card.scrollIntoView({ block: 'start' });", shell, StringComparison.Ordinal);
    }
}
