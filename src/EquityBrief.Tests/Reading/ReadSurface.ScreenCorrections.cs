using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cards;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the corrections of 2026-10-13 to 19.2, 19.3 and 19.4, read back off the rendered page: Tonight's top on a
// night the families drew it holding no night card and no watch list card, its market line carrying what ran and the
// watch list's line, and its buy points one line where none lists; a phone's date line the night's short date; a press
// that redraws the screen to change one card keeping that card where it stood, read off the shell's script; and paper
// trading walking every account on the example account until the operator saves one, writing it nowhere.
// see: The bar's index menu chooses the index every page reads, each choice naming its cap and its members
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public partial class ReadSurface
{
    // The 19.2 correction of 2026-10-17: a share under its floor is never drawn at or above it, the S&P 600's breadth of
    // 0.4497 under a floor of 0.45 reading 44.9% beside "below its floor of 45%", where a share at or above its floor and
    // one rounding under it are drawn as written; on the market line, on the closed market's lines and on a card's line.
    [Fact]
    public void ABreadthUnderItsFloorIsNeverDrawnAtOrAboveIt()
    {
        Assert.Equal(
            ["44.9", "44.9", "44.9", "44.8", "45.0", "45.0", "55.0"],
            new[] { (0.4497, 0.45), (0.44999, 0.45), (0.4495, 0.45), (0.448, 0.45), (0.45, 0.45), (0.4504, 0.45), (0.55, 0.45) }
                .Select(pair => FloorShare.Percent(pair.Item1, pair.Item2)));

        var closed = WebUtility.HtmlDecode(new MarkRenderer().MarketLine(new MarketLineView(false, 0.4497, 0.45, 0, 0, 4, 32, 0, "S&P 600", 1, 600, true, "600")));
        var open = WebUtility.HtmlDecode(new MarkRenderer().MarketLine(new MarketLineView(true, 0.45, 0.45, 0, 0, 4, 32, 0, "S&P 600", 1, 600, true, "600")));

        Assert.Contains("the S&P 600's breadth: 44.9% of its members closed above their 200-day average, below its floor of 45%", closed, StringComparison.Ordinal);
        Assert.DoesNotContain("45.0%", closed, StringComparison.Ordinal);
        Assert.Contains("the S&P 600's breadth: 45.0% of its members closed above their 200-day average, at or above its floor of 45%", open, StringComparison.Ordinal);
        Assert.Contains(
            "S&P 600's breadth is 44.9% against its floor of 45%",
            CardLines.Market(new MarketReading(0.4497, 0.45, true), null, "S&P 600", CardSettings.Defaults).Words,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TonightOnAFamiliesNightDrawsNoNightCardNorWatchCardItsMarketLineCarryingWhatRanAndTheWatchList()
    {
        using var store = await FamilyPagesStore();

        store.Execute("INSERT INTO watch_list (ticker, added_at) VALUES ('F1', '2026-09-01T12:00:00Z'), ('F2', '2026-09-01T12:00:00Z');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        // The tiles carry the passes and the market line the breadth, so neither card the night drew before the families
        // stands; beneath the market line, what ran tonight and the watch list in one line.
        Assert.DoesNotContain("data-card=\"night\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card=\"watch\"", page, StringComparison.Ordinal);
        Assert.Contains($"</span></p><p class=\"market-links\"><a class=\"market-run\" href=\"{SinglePageApp.RunRoute}{TheSwitch}\">What ran tonight</a> · <span class=\"market-watch\" data-watching=\"2\">2 watched · <a href=\"{SinglePageApp.WatchRoute}\">open the watch list</a></span></p>", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("<p class=\"market-links\">", StringComparison.Ordinal) < page.IndexOf("class=\"setup-tiles\"", StringComparison.Ordinal));

        // The night drew buy points, so the table stands and no line in its place.
        Assert.Contains("<table class=\"list-table buy-points\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("buy-points-none", page, StringComparison.Ordinal);
    }

    [Fact]
    public void ANightNoSetupListsDrawsOneLineInTheBuyPointsPlaceWithTheMembersOneGateShortAndItsGuideBehindItsPress()
    {
        var night = new DateOnly(2026, 10, 12);
        var line = new MarketLineView(true, 0.48, 0.45, 0, 0, 4, 32, 0, "S&P 600", 1, 600, true, "600");
        var region = new SinglePageApp().IndexTonightRegion(new MarkRenderer(), night, Universes.Small, string.Empty, [night], line, [], null);

        Assert.Contains(
            $"<section class=\"card folded\" data-card=\"buy-points\"><div class=\"card-b\"><div class=\"card-one-line\"><p class=\"buy-points-none\" data-buy-points=\"0\" data-one-short=\"32\">No buy points tonight · 32 one gate short</p><details class=\"guide\"><summary title=\"How to read this\" aria-label=\"How to read this\">?</summary><p class=\"guide-b\">{Cards.Escaped(SinglePageApp.BuyPointsGuide)}</p></details></div></div></section>",
            region,
            StringComparison.Ordinal);
        Assert.DoesNotContain("list-table buy-points", region, StringComparison.Ordinal);

        // The S&P 600's market line links what ran on its own Run page and draws no watch list, which is the S&P 500's.
        Assert.Contains($"<p class=\"market-links\"><a class=\"market-run\" href=\"{SinglePageApp.RunRoute}2026-10-12?universe=600\">What ran tonight</a></p>", region, StringComparison.Ordinal);

        // A night whose close to a buy point was not read says none.
        var unread = new SinglePageApp().IndexTonightRegion(new MarkRenderer(), night, Universes.Small, string.Empty, [night], line with { CloseRead = false }, [], null);

        Assert.Contains("<p class=\"buy-points-none\" data-buy-points=\"0\" data-one-short=\"none\">No buy points tonight</p>", unread, StringComparison.Ordinal);

        // From the 19.8 correction of 2026-10-21 a night reading no stock one gate short draws no card of them, and the two
        // counts that would open it read nought with no link, on the market line and on the line in the buy points' place,
        // where a night reading some links both to the card.
        var href = $"{SinglePageApp.NightRoute}2026-10-12?universe=600&close=open";
        var none = new SinglePageApp().IndexTonightRegion(new MarkRenderer(), night, Universes.Small, string.Empty, [night], line with { Close = 0, CloseHref = href }, [], null);
        var some = new SinglePageApp().IndexTonightRegion(new MarkRenderer(), night, Universes.Small, string.Empty, [night], line with { CloseHref = href }, [], null);

        Assert.Contains("data-one-short=\"0\">No buy points tonight · 0 one gate short</p>", none, StringComparison.Ordinal);
        Assert.Contains("0 close to a buy point · ", none, StringComparison.Ordinal);
        Assert.DoesNotContain("close-open", none, StringComparison.Ordinal);
        Assert.DoesNotContain("close-to-buy", none, StringComparison.Ordinal);
        Assert.Empty(new MarkRenderer().CloseToBuy([], false, true, "500"));
        Assert.Contains($"No buy points tonight · <a class=\"close-open\" href=\"{href}\">32 one gate short</a></p>", WebUtility.HtmlDecode(some), StringComparison.Ordinal);
        Assert.Contains($"<a class=\"close-open\" href=\"{href}\">32 close to a buy point</a> · ", WebUtility.HtmlDecode(some), StringComparison.Ordinal);
    }

    [Fact]
    public void OnAPhoneTheTitleHoldsOneLineAndTheDateLineIsTheNightsShortDateBetweenItsArrows()
    {
        var picker = Cards.NightPicker(new DateOnly(2026, 10, 12), [new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12)], SinglePageApp.NightRoute, "#/");

        Assert.Contains("<summary class=\"np-date\" aria-label=\"Choose a night to view\"><span class=\"np-full\">2026-10-12</span><span class=\"np-short\">Oct 12</span></summary>", picker, StringComparison.Ordinal);

        var css = Stylesheet.Css;
        var block = css[css.IndexOf("@media (max-width:560px){\n .page-head", StringComparison.Ordinal)..];

        block = block[..(block.IndexOf("\n}", StringComparison.Ordinal) + 2)];

        Assert.Contains(".night-picker .np-short{display:none}", css, StringComparison.Ordinal);
        Assert.Contains(" .page-head .m-screen{max-width:100%;white-space:nowrap;overflow:hidden;text-overflow:ellipsis}", block, StringComparison.Ordinal);
        Assert.Contains(" .page-head .asof-words{display:none}", block, StringComparison.Ordinal);
        Assert.Contains(" .night-picker .np-full{display:none}", block, StringComparison.Ordinal);
        Assert.Contains(" .night-picker .np-short{display:inline}", block, StringComparison.Ordinal);

        // No rule holds a masthead's date line to one line or cuts it: the night picker's line is one line by its short
        // date, and every line drawing no arrows wraps whole, a screen's sentence ending on its date, as Paper trading's
        // "as of the close of 2026-10-20", and a stock's masthead line with its previous close, its sector's link and the
        // name page's link back to its chart page among them.
        Assert.Empty(Regex.Matches(css, @"\.m-asof[^{]*\{[^}]*(nowrap|overflow:hidden|text-overflow)"));

        var stock = SinglePageApp.Masthead(
            "AAA",
            [new ChartBar(new DateOnly(2026, 10, 9), 100m, 102m, 99m, 101m, 1_000), new ChartBar(new DateOnly(2026, 10, 12), 101m, 104m, 100m, 103.5m, 1_000)],
            new NameMast("Alpha Inc", "Industrials", "Machinery", 0.0248),
            null,
            null,
            false,
            null,
            null,
            null,
            " · <a class=\"m-chart\" href=\"#/chart/AAA\">Its chart</a>");
        var line = Regex.Match(stock, "<span class=\"m-asof\">(.*)</span></div>$", RegexOptions.Singleline).Groups[1].Value;

        Assert.StartsWith("<span class=\"m-when\">As of the close of 2026-10-12", line, StringComparison.Ordinal);
        Assert.EndsWith("<a href=\"#/universe?sector=Industrials\">Industrials</a>, Machinery · <a class=\"m-chart\" href=\"#/chart/AAA\">Its chart</a>", line, StringComparison.Ordinal);
    }

    // The 19.7 correction of 2026-10-21: a date in a cell of Past picks', the Watch list's or Researched's table is drawn
    // whole, where each broke at a hyphen, "2026-10-" above "21", the Watch list's and Researched's even at 1440 px; and on
    // a phone those two tables scroll sideways in their box at a width their words read at, where they squeezed a gate's
    // pill into four lines and a row to 460 px, the pill held to one line.
    [Fact]
    public void ADateInATablesCellIsDrawnWholeAndTheWatchListAndResearchedScrollOnAPhone()
    {
        var css = Stylesheet.Css;

        Assert.Contains(".picks-table td.num:first-child,.watch-table td.num,.researched-table td.num{white-space:nowrap}", css, StringComparison.Ordinal);
        Assert.Contains(".watch-table{min-width:1040px}", css, StringComparison.Ordinal);
        Assert.Contains(".watch-table .chip{white-space:nowrap}", css, StringComparison.Ordinal);
        Assert.Contains(".researched-table{min-width:900px}", css, StringComparison.Ordinal);
        Assert.Contains(".tbl-wrap{overflow-x:auto}", css, StringComparison.Ordinal);

        // The first cell of a Past picks table, with the stock's column and without, is the night listed as a figure.
        var pick = new PickCell("AAA", "Alpha Inc", new DateOnly(2026, 10, 12), "swing", 100m, 96m, 110m, PickStatus.Open, 2, null, 1.02, null, null, new DateOnly(2026, 10, 14), 102m, 0.3);

        foreach (var named in new[] { true, false })
        {
            Assert.Matches("<tbody><tr [^>]*><td class=\"num\">(?:<a [^>]*>)?2026-10-12(?:</a>)?</td>", new MarkRenderer().PicksTable([pick], named));
        }
    }

    // The 16.2 correction of 2026-10-21: on a phone the account form, which paper trading and the account page both open
    // with, stands each setting's words whole above its box, one setting beneath another, where it ran as one paragraph and
    // broke "Risk a trade, per cent" across two lines; each label holds its words and its box, and the form's rule there
    // outranks the one every posted form is drawn inline by.
    [Fact]
    public void OnAPhoneTheAccountFormStandsEachSettingsWordsWholeAboveItsBox()
    {
        var form = SinglePageApp.AccountForm(null, null, null, 0.2m);

        Assert.Equal(
            ["Account size", "Risk a trade, per cent", "Position cap, share of the account"],
            Regex.Matches(form, "<label>([^<]+) <input name=\"(?:size|risk|cap)\"").Select(match => match.Groups[1].Value));

        var css = Stylesheet.Css;
        var block = css[css.IndexOf("@media (max-width:560px){\n form.account-control", StringComparison.Ordinal)..];

        block = block[..(block.IndexOf("\n}", StringComparison.Ordinal) + 2)];

        Assert.Contains(" form.account-control{display:block;margin-right:0}", block, StringComparison.Ordinal);
        Assert.Contains(" .account-control label{display:block;margin:0 0 10px}", block, StringComparison.Ordinal);
        Assert.Contains(" .account-control input{display:block;width:100%;margin-top:4px}", block, StringComparison.Ordinal);
        Assert.Contains("form[method='post']{display:inline-block;", css, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TonightsDateLineWordsStandApartFromItsPickerSoAPhoneDrawsThePickerAlone()
    {
        using var store = await FamilyPagesStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        Assert.Contains($"<span class=\"m-asof\"><span class=\"asof-words\">Night of {TheSwitch}, computed after the close", page, StringComparison.Ordinal);
        Assert.Contains("</span><span class=\"night-picker\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public void APressRedrawingTheScreenToChangeOneCardKeepsThatCardWhereItStoodReadOffTheShellsScript()
    {
        var shell = new SinglePageApp().Shell("EquityBrief");

        // A tab finds its card again by the family it names; a chip, a sector's tile, a column's heading or the search box
        // by its card's own mark; each keeps how far down the window the card stood.
        Assert.Contains("function keepPlace(control, find) {", shell, StringComparison.Ordinal);
        Assert.Contains("keptPlace = card ? { find: find, top: card.getBoundingClientRect().top, at: Date.now() } : null;", shell, StringComparison.Ordinal);
        Assert.Contains("keepPlace(tab, 'a[data-rule-tab=\"' + CSS.escape(tab.getAttribute('data-rule-tab')) + '\"]');", shell, StringComparison.Ordinal);
        Assert.Contains("const keeper = event.target.closest('a.chip[data-filter], a.sector-tile, a.sort');", shell, StringComparison.Ordinal);
        Assert.Contains("if (keeperCard) { keepPlace(keeper, 'section.card[data-card=\"' + CSS.escape(keeperCard.getAttribute('data-card')) + '\"]'); }", shell, StringComparison.Ordinal);
        Assert.Contains("if (searched) { keepPlace(form, 'section.card[data-card=\"' + CSS.escape(searched.getAttribute('data-card')) + '\"]'); }", shell, StringComparison.Ordinal);

        // The redraw puts the card back where it stood before any other place a screen is scrolled to, within ten seconds
        // of the press, and forgets it after.
        var placed = shell.IndexOf("if (placedCard) { scrollTo(0, placedCard.getBoundingClientRect().top + scrollY - keptPlace.top); }", StringComparison.Ordinal);

        Assert.True(placed >= 0, "The redraw does not put the kept card back.");
        Assert.Contains("const placed = keptPlace && Date.now() - keptPlace.at < 10000 ? screen.querySelector(keptPlace.find) : null;", shell, StringComparison.Ordinal);
        Assert.True(placed < shell.IndexOf("else if (!fresh && scrolls[hash] != null) { scrollTo(0, scrolls[hash]); }", StringComparison.Ordinal));
        Assert.True(shell.IndexOf("keptPlace = null;", placed, StringComparison.Ordinal) > placed);
    }

    [Fact]
    public async Task PaperTradingWalksEveryAccountOnTheExampleUntilOneIsSavedAndWritesTheExampleNowhere()
    {
        Assert.Equal(new AccountSettings(100_000m, 1m, 0.2m), AccountSettings.Example);

        using var store = await FamilyPagesStore();

        var before = Directory.GetFiles(store.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper"));

        // No settings are kept, so the page names the example at its top, walks the accounts on it and draws the chart and
        // the table, the form left empty for the operator's own.
        Assert.Contains("data-walked=\"true\" data-example=\"true\">", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"notice paper-example\" data-example=\"true\"><b>Example account</b>: 100,000 dollars, 1 per cent of it a trade and at most 20 per cent of it in one position. Save your own below and every account is walked on yours.</p>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("data-paper-chart=\"unset\"", page, StringComparison.Ordinal);
        Assert.Contains("class=\"paper-chart\"", page, StringComparison.Ordinal);
        Assert.Contains("data-walked=\"true\"><thead>", page, StringComparison.Ordinal);
        Assert.DoesNotContain("set your account to walk it", page, StringComparison.Ordinal);
        Assert.DoesNotContain("value=\"100000\"", page, StringComparison.Ordinal);

        // And it is written nowhere: no settings file, and no file under the data root the page's read did not find there,
        // the store's own journal aside.
        Assert.Null(AccountFile.Read(store.Root));
        Assert.False(File.Exists(AccountFile.PathIn(store.Root)));
        string[] written = [.. Directory.GetFiles(store.Root, "*", SearchOption.AllDirectories).Except(before).Where(path => !path.EndsWith("-journal", StringComparison.Ordinal) && !path.EndsWith("-wal", StringComparison.Ordinal) && !path.EndsWith("-shm", StringComparison.Ordinal))];

        Assert.True(written.Length == 0, $"The page wrote under the data root: {string.Join(", ", written)}.");

        // Once the operator saves an account, every account is walked on it and the example is named nowhere.
        AccountFile.Write(store.Root, new AccountSettings(50_000m, 0.5m, 0.1m));

        var saved = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper"));

        Assert.Contains("data-walked=\"true\" data-example=\"false\">", saved, StringComparison.Ordinal);
        Assert.DoesNotContain("paper-example", saved, StringComparison.Ordinal);
    }

    [Fact]
    public void AnAccountsRecordCountsItsTradesAndThoseDecidedAgainstTheTestsCountInOneLine()
    {
        Assert.Equal("12 trades · 4 decided of 300", EquityBrief.Core.Loop.RuleVerdict.CountLine(false, 12, 4, 3, yours: false));
        Assert.Equal("1 trade · 0 decided of 300", EquityBrief.Core.Loop.RuleVerdict.CountLine(false, 1, 0, 0, yours: false));
        Assert.Equal("5 holdings · 2 of 36 months", EquityBrief.Core.Loop.RuleVerdict.CountLine(true, 5, 1, 2, yours: false));
        Assert.Equal("3 trades · 1 decided", EquityBrief.Core.Loop.RuleVerdict.CountLine(false, 3, 1, 0, yours: true));
    }
}
