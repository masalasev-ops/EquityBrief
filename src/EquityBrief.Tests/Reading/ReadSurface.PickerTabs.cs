using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Judge;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From 24.49 Tonight and Paper trading each carry the rules' tab and the AI stock picker's, and the picker's trades tab draws every
// list a page of ten rows with one past night open at a time, read back over a constructed store of twelve nights; and the
// picker's old addresses open where the operator's order of 2026-11-28 says.
// see: The AI stock picker's trades are drawn on Paper trading's own tab, every list paged at ten rows and one past night open at a time
public partial class ReadSurface
{
    // The rows 24.49 adds that this check reaches.
    internal static string[] PickerTabRows =>
    [
        CheckReach.Key("15.26 The AI stock picker", "Two tabs"),
        CheckReach.Key("15.26 The AI stock picker", "Its trades"),
    ];

    // Twelve nights from 2 Nov, two picks a night, each a close of 100 and a typical move of 2, so a range of 96 to 101; each opens
    // at 100.5 the session after its night, inside it, and closes at 101 every session after, so none reaches its take-profit of
    // 104 or its stop of 98 and each sells at its week's limit.
    static readonly DateOnly[] TabNights =
    [
        new(2026, 11, 2), new(2026, 11, 3), new(2026, 11, 4), new(2026, 11, 5), new(2026, 11, 6), new(2026, 11, 9),
        new(2026, 11, 10), new(2026, 11, 11), new(2026, 11, 12), new(2026, 11, 13), new(2026, 11, 16), new(2026, 11, 17),
    ];

    static readonly DateOnly[] TabSessions = [.. TabNights, new(2026, 11, 18), new(2026, 11, 19), new(2026, 11, 20)];

    static string TabTicker(int night, int rank) => string.Create(CultureInfo.InvariantCulture, $"{(char)('A' + night)}{(rank == 1 ? "X" : "Y")}");

    static TemporaryStore PickerTabsStore()
    {
        var store = new TemporaryStore().Migrated();
        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (var night = 0; night < TabNights.Length; night++)
        {
            var on = Day(TabNights[night]);

            store.Execute(
                "INSERT INTO judge_run (run_id, session_date, fact_version, pin_id, seed, outcome, started_at, ended_at, screened, kept, asks, asks_failed, detail, index_code) VALUES "
                + $"('judge-tabs-{on}', '{on}', 'v', 1, 20261124, 'picked', '{on}T23:00:00Z', '{on}T23:10:00Z', 500, 7, 2, 0, '', 'GSPC');");

            for (var rank = 1; rank <= 2; rank++)
            {
                var ticker = TabTicker(night, rank);

                store.Execute(
                    "INSERT INTO judge_pick (session_date, rank, run_id, index_code, ticker, hold, take_profit, reason, last_close, typical_move, stop, target, give_up_distance) VALUES "
                    + $"('{on}', {rank}, 'judge-tabs-{on}', 'GSPC', '{ticker}', '1 week', '{JudgeMenu.Whole}', 'A reason.', '100', 2.0, '98', '104', NULL);");
                store.Execute(
                    "INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id) VALUES "
                    + $"('GSPC', '{on}', '{ticker}', 'v', '{{}}', 'r', 's', '{on}T23:00:00Z', 'f');");
                store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('{ticker}', '{on}', 'atr14', 2.0, 14);");

                var bars = TabSessions.Where(session => session >= TabNights[night]).Select(session => session == TabNights[night]
                    ? $"('{ticker}', '{Day(session)}', '100', '100', '100', '100', 1000, 'eod', '{Day(session)}T22:00:00Z')"
                    : $"('{ticker}', '{Day(session)}', '100.5', '101', '100', '101', 1000, 'eod', '{Day(session)}T22:00:00Z')");

                store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES " + string.Join(", ", bars) + ";");
            }
        }

        return store;
    }

    // Every list of the trades tab drawing more rows than a page holds, read off the page as drawn.
    static IReadOnlyList<(string List, int Rows)> UnpagedOn(string page) => SinglePageApp.UnpagedLists(page);

    [Fact]
    public async Task ThePickersTradesTabDrawsEveryListAPageOfTenRowsAndOnePastNightOpen()
    {
        using var store = PickerTabsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge-trades"));

        // Twenty-four picks over twelve nights: no list draws more than ten rows, and the past nights' list says the first ten
        // of twelve, the newest open and the rest folded.
        Assert.Empty(UnpagedOn(page));
        Assert.Contains("<div class=\"ap-pager\" data-pager=\"nights\"><span>1 to 10 of 12</span>", page, StringComparison.Ordinal);
        Assert.Equal(10, Regex.Count(page, "<details class=\"ap-night\" name=\"ap-night\" data-row"));
        Assert.Single(Regex.Matches(page, "<details class=\"ap-night\"[^>]* open>"));
        Assert.Contains("<details class=\"ap-night\" name=\"ap-night\" data-row data-night=\"2026-11-17\" open>", page, StringComparison.Ordinal);

        // Each night's row: its picks as chips and its counts, the marks read off its run.
        Assert.Contains("<span class=\"d\">Tue 17 Nov</span><span class=\"chips\"><i>LX</i><i>LY</i></span><span class=\"r\">rated 7 worth buying · picked 2 ·", page, StringComparison.Ordinal);

        // Every pick stands in one list alone, worked from the store: 24 picks, each bought at 100.5 the session after its night
        // or not bought, those the account had no cash for saying so. How many the account's cash bought is read off its walk
        // as Paper trading walks it: 14, of which 4 are still open on the newest close of 20 Nov and 10 sold at their week's limit.
        var lists = Regex.Matches(page, "<section class=\"card\" data-list=\"(?<list>[a-z]+)\">(?<body>.*?)</section>", RegexOptions.Singleline).ToDictionary(match => match.Groups["list"].Value, match => match.Groups["body"].Value);

        string Heading(string list) => Regex.Match(lists[list], "<h2>(.*?)</h2>").Groups[1].Value;

        Assert.Equal(["open", "waiting", "closed", "nights"], lists.Keys);
        Assert.Equal(("4 open trades", "0 waiting within their 2 days · 10 not bought", "10 closed trades"), (Heading("open"), Heading("waiting"), Heading("closed")));
        Assert.Equal(24, Regex.Count(lists["open"] + lists["waiting"] + lists["closed"], "<tr data-row>"));
        Assert.Equal(10, Regex.Count(lists["waiting"], "no cash left in the account that night"));
        Assert.Equal(10, Regex.Count(lists["closed"], "<td>reached its time limit</td>"));
        Assert.All(Regex.Matches(lists["open"], "<td>(\\d) of 5 days</td>"), held => Assert.InRange(int.Parse(held.Groups[1].Value, CultureInfo.InvariantCulture), 1, 4));
        Assert.Equal(4, Regex.Count(lists["open"], "<td>\\d of 5 days</td>"));

        // A list of more than ten is paged: the waiting list's ten fill its first page, and the past nights' second page holds
        // the two left, a night named in the address opening on its own page.
        Assert.Contains("<span>1 to 10 of 10</span>", lists["waiting"], StringComparison.Ordinal);

        var second = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge-trades?nights=2"));
        var secondNights = Regex.Match(second, "<section class=\"card\" data-list=\"nights\">(?<body>.*?)</section>", RegexOptions.Singleline).Groups["body"].Value;

        Assert.Contains("<span>11 to 12 of 12</span>", secondNights, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Count(secondNights, "<details class=\"ap-night\""));
        Assert.Single(Regex.Matches(secondNights, " open>"));
        Assert.Contains("data-night=\"2026-11-03\" open>", secondNights, StringComparison.Ordinal);

        var named = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge-trades?night=2026-11-02"));

        Assert.Contains("<span>11 to 12 of 12</span>", named, StringComparison.Ordinal);
        Assert.Contains("<details class=\"ap-night\" name=\"ap-night\" data-row data-night=\"2026-11-02\" open>", named, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(named, "<details class=\"ap-night\"[^>]* open>"));
        Assert.Equal(2, Regex.Count(Regex.Match(named, "data-night=\"2026-11-02\" open>.*?(?=<details class=\"ap-night\"|</div></section>)", RegexOptions.Singleline).Value, "<article class=\"home-idea judge-card\""));

        // The tab sits under Paper trading's two folder tabs, the picker's marked as the page.
        Assert.StartsWith(SinglePageApp.FolderTabs(true, true, Universes.Large), page, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"ft ft-ai\" data-tab=\"ai\" href=\"{SinglePageApp.PickerTradesRoute}\" aria-current=\"page\">{SinglePageApp.PickerTradesTab}</a>", page, StringComparison.Ordinal);

        // The picks tab: the picker's page under Tonight's tabs, the fixed 5's card linking to its trades and drawing no card.
        var picks = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/judge"));
        var five = Regex.Match(picks, "<section class=\"card\" data-card=\"judge-fixed-five\">.*?</section>", RegexOptions.Singleline).Value;

        Assert.StartsWith(SinglePageApp.FolderTabs(false, true, Universes.Large), picks, StringComparison.Ordinal);
        Assert.Contains($"<a href=\"{SinglePageApp.PickerTradesRoute}?universe=500&account=fixed-five\">{SinglePageApp.SeeItsTrades}</a>", five, StringComparison.Ordinal);
        Assert.DoesNotContain("<article", five, StringComparison.Ordinal);
        Assert.DoesNotContain("<svg", five, StringComparison.Ordinal);
    }

    // From the 24.49 correction of 2026-11-28 the read-only copy, which holds no page of the AI tabs, draws neither tab strip.
    [Fact]
    public void TheReadOnlyCopyDrawsNoFolderTabs()
    {
        foreach (var paper in new[] { false, true })
        {
            var cleaned = EquityBrief.Web.Cloudflare.SiteCopy.Clean(SinglePageApp.FolderTabs(paper, false, Universes.Large) + "<p>the page</p>");

            Assert.Equal("<p>the page</p>", cleaned);
        }
    }

    [Fact]
    public void TheUnpagedListCheckFindsAListPastTenRows()
    {
        var rows = string.Concat(Enumerable.Repeat("<tr data-row><td>X</td></tr>", 11));
        var nights = string.Concat(Enumerable.Repeat("<details class=\"ap-night\" name=\"ap-night\" data-row data-night=\"2026-11-02\"></details>", 10));

        Assert.Equal([("open", 11)], UnpagedOn($"<section class=\"card\" data-list=\"open\"><table>{rows}</table></section><section class=\"card\" data-list=\"nights\">{nights}</section>"));
    }

    [Fact]
    public async Task ThePickersOldAddressesOpenItsTabsAndRecordsLinksToItsPicksTab()
    {
        using var store = PickerTabsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var shell = await client.GetStringAsync("/");
        var script = Regex.Replace(shell, @"\s+", " ");

        // #/picker and #/judge open Tonight's AI tab, and #/picker/DATE that night on Paper trading's AI tab, each sent on before
        // the routes are read.
        Assert.Contains($"if (path === '{SinglePageApp.JudgeOldRoute}' || path === '{SinglePageApp.JudgeRoute}') {{ path = '{SinglePageApp.PickerTabRoute}'; history.replaceState(null, '', path + (query ? '?' + query : '')); }}", script, StringComparison.Ordinal);
        Assert.Contains($"}} else if (path.startsWith('{SinglePageApp.JudgeNightRoute}')) {{ query = (query ? query + '&' : '') + '{JudgeTradesLink.NightKey}=' + encodeURIComponent(path.slice('{SinglePageApp.JudgeNightRoute}'.length)); path = '{SinglePageApp.PickerTradesRoute}'; history.replaceState(null, '', path + '?' + query); }}", script, StringComparison.Ordinal);
        Assert.Contains($"if (path === '{SinglePageApp.PickerTabRoute}') {{ const judge = await fetch('/screens/judge' + (query ? '?' + query : ''));", script, StringComparison.Ordinal);
        Assert.Contains($"}} else if (path === '{SinglePageApp.PickerTradesRoute}') {{ view = 'paper'; const trades = await fetch('/screens/judge-trades' + (query ? '?' + query : ''));", script, StringComparison.Ordinal);
        Assert.True(script.IndexOf($"path = '{SinglePageApp.PickerTradesRoute}';", StringComparison.Ordinal) < script.IndexOf($"if (path === '{SinglePageApp.PickerTabRoute}') {{", StringComparison.Ordinal), "The old addresses are sent on before the routes are read.");
        Assert.DoesNotContain($"path === '{SinglePageApp.JudgeRoute}' || path.startsWith", script, StringComparison.Ordinal);

        // Records keeps its link, which opens the picks tab.
        Assert.Contains($"<a href=\"{SinglePageApp.PickerTabRoute}\" data-view=\"judge\">AI stock picker</a>", shell, StringComparison.Ordinal);

        // Tonight and Paper trading each open under their tabs, the rules' marked as the page.
        var tonight = await client.GetStringAsync("/screens/tonight");
        var paper = await client.GetStringAsync("/screens/paper");

        Assert.StartsWith(SinglePageApp.FolderTabs(false, false, Universes.Large), tonight, StringComparison.Ordinal);
        Assert.StartsWith(SinglePageApp.FolderTabs(true, false, Universes.Large), paper, StringComparison.Ordinal);
        Assert.Contains($"<a class=\"ft ft-rules\" data-tab=\"rules\" href=\"{SinglePageApp.TonightRoute}\" aria-current=\"page\">{SinglePageApp.RulesPicksTab}</a>", tonight, StringComparison.Ordinal);
    }
}
