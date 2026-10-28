using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Cloudflare;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cloudflare;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;

namespace EquityBrief.Tests.Reading;

// read-surface: the read-only copy of the site on Cloudflare Pages. The surface's own screens saved as files with every
// control and every address of the surface taken out, every link resolved against what the copy holds, and a shell that
// fetches only the copy's files and names itself a copy on every page.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens and holds no control
public partial class ReadSurface
{
    static readonly DateOnly CopyNight = new(2026, 10, 27);

    [Fact]
    public void TheCopyKeysALinkByItsPathAndTheKeysThatChooseWhatItDraws()
    {
        // The place keys go, the S&P 500's own word goes, a stock's pages read no index, the rest sort, and a key named
        // twice is read by its first value.
        Assert.Equal("#/", SiteCopy.Canonical(""));
        Assert.Equal("#/", SiteCopy.Canonical("#"));
        Assert.Equal("#/", SiteCopy.Canonical("#/?close=open&name=AAPL&universe=500"));
        Assert.Equal("#/?universe=400", SiteCopy.Canonical("#/?close=open&universe=400"));
        Assert.Equal("#/researched?sort=stale&universe=600", SiteCopy.Canonical("#/researched?universe=600&sort=stale"));
        Assert.Equal("#/picks?status=open", SiteCopy.Canonical("#/picks?status=open&status=closed&reason=x"));
        Assert.Equal("#/chart/AAPL", SiteCopy.Canonical("#/chart/AAPL?universe=400"));
        Assert.Equal("#/chart/AAPL?interval=weekly", SiteCopy.Canonical("#/chart/AAPL?universe=600&interval=weekly"));
        Assert.Equal("#/name/BRK.B/2026-10-11", SiteCopy.Canonical("#/name/BRK.B/2026-10-11"));
        Assert.Equal("#/universe?page=2&universe=400", SiteCopy.Canonical("#/universe?page=2&universe=400"));

        // A held route's query is plain words and spaces alone, so the shell keys it as the copy did.
        Assert.True(SiteCopy.Holdable("#/universe?page=2&universe=400"));
        Assert.True(SiteCopy.Holdable(SiteCopy.Canonical("#/universe?sector=Information+Technology")));
        Assert.Equal("#/universe?sector=Information%20Technology", SiteCopy.Canonical("#/universe?sector=Information+Technology"));
        Assert.False(SiteCopy.Holdable("#/universe?sector=" + Uri.EscapeDataString("R&D/Labs")));
    }

    // The store's file read whole while the fixture holds it open, so a test can say no byte of it moved.
    static byte[] StoreBytesHash(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        return SHA256.HashData(stream);
    }

    [Fact]
    public void EveryHeldRouteHasAFileOfItsOwnUnderTheScreensFolder()
    {
        Assert.Equal("screens/tonight.html", SiteCopy.FileOf("#/"));
        Assert.Equal("screens/tonight~universe-400.html", SiteCopy.FileOf("#/?universe=400"));
        Assert.Equal("screens/researched~sort-stale~universe-600.html", SiteCopy.FileOf("#/researched?sort=stale&universe=600"));
        Assert.Equal("screens/chart/BRK.B.html", SiteCopy.FileOf("#/chart/BRK.B"));
        Assert.Equal("screens/name/AAPL/2026-10-11.html", SiteCopy.FileOf("#/name/AAPL/2026-10-11"));

        // A name Windows refuses a file is moved aside, and a character outside the plain set is written as its code.
        Assert.Equal("screens/chart/_CON.html", SiteCopy.FileOf("#/chart/CON"));
        Assert.Equal("screens/name/A_5fB.html", SiteCopy.FileOf("#/name/A_B"));

        // A paper account's long name keeps its file's name within bounds, two that differ only past the bound kept apart.
        var account = "the-s-p-400-pullback-rule-at-a-strength-of-0-75-1-to-3-typical-moves-deep-a-dry-up-under-1-5-from-version-";
        var longer = SiteCopy.FileOf("#/picks?account=" + account + "1&universe=400");

        Assert.True(Path.GetFileName(longer).Length <= SiteCopy.LongestName + ".html".Length, longer);
        Assert.StartsWith("screens/picks~account-the-s-p-400-pullback", longer, StringComparison.Ordinal);
        Assert.NotEqual(longer, SiteCopy.FileOf("#/picks?account=" + account + "2&universe=400"));

        string[] routes = ["#/", "#/?universe=400", "#/universe", "#/universe?page=2", "#/universe?page=2&universe=400", "#/chart/A", "#/name/A", "#/name/A/2026-10-11", "#/name/A-B", "#/name/A_B"];

        Assert.Equal(routes.Length, routes.Select(SiteCopy.FileOf).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        // And each is read from the route the live shell asks the surface for.
        Assert.Equal("/screens/tonight?universe=400", SiteCopy.ServerRoute("#/?universe=400"));
        Assert.Equal("/screens/name/AAPL/2026-10-11", SiteCopy.ServerRoute("#/name/AAPL/2026-10-11"));
        Assert.Equal("/screens/paper?universe=600", SiteCopy.ServerRoute("#/paper?universe=600"));
        Assert.Null(SiteCopy.ServerRoute("#/run/2026-10-27"));
        Assert.Null(SiteCopy.ServerRoute("#/watch"));
        Assert.Null(SiteCopy.ServerRoute("#/queue"));
        Assert.Null(SiteCopy.ServerRoute(SinglePageApp.AccountRoute));
    }

    [Fact]
    public void TheCopyTakesOutEveryControlAndEveryAddressOfTheSurface()
    {
        const string Fragment =
            "<section class=\"name\"><div class=\"screen-mast\" data-title=\"ROP\" data-quote-route=\"/quotes/ROP\" data-session-open=\"2026-10-28T13:30:00Z\" data-session-close=\"2026-10-28T20:00:00Z\" data-interval-minutes=\"5\" data-asked=\"\">"
            + "<span class=\"m-price\"><span class=\"m-px\">434.48</span></span><span class=\"m-when\">As of the close of 2026-10-27</span>"
            + "<form class=\"watch-control name-watch\" method=\"post\" action=\"/watch/\" data-ticker=\"ROP\"><button type=\"submit\">Add to watch list</button></form></div>"
            + "<span class=\"night-picker\" data-route=\"#/night/\"><a class=\"np-move\" href=\"#/night/2026-10-26\">&#8249;</a><span class=\"np-cal\"><span class=\"np-wd\">Mo</span></span></span><span class=\"kept-span\">kept</span>"
            + "<p class=\"export\"><a href=\"/exports/name/ROP\" download>Export</a></p>"
            + "<div class=\"report-standing\"><p class=\"research-cost\" data-spend=\"7.2\">Research may spend: $0.38 of the $10.00 day cap</p><div class=\"rs-controls\"><form class=\"research-control\" method=\"post\" action=\"/passes/ROP\"><button>Regenerate Report</button></form></div></div>"
            + "<label>Showing <select data-rule-choice=\"pullback\"><option value=\"live\">Live</option><option value=\"v2\" selected>Version 2</option></select></label>"
            + "<details><summary>Fold</summary><p>inside</p><button type=\"button\" class=\"fold-hide\">Hide</button></details>"
            + "<section class=\"chart-graph\" data-graph=\"analysts\" data-analysts-route=\"/analysts/ROP\" data-ask=\"no\"><h3>The analysts</h3><div class=\"fig\"><div class=\"firm-actions\"><div class=\"tbl-wrap\"><table></table></div></div></div></section>"
            + "<section class=\"chart-graph\" data-graph=\"ratings\" data-ratings-route=\"/analyst-ratings/ROP\"><section class=\"inner\"></section><form class=\"ratings-press\"><button>Get analyst ratings</button></form></section>"
            + "<button type=\"button\" class=\"card-toggle\" aria-controls=\"c1\">Open plan</button><tr data-select-href=\"#/?name=ROP\" data-ticker=\"ROP\"></tr>"
            + "<p class=\"card-money degraded\" data-account=\"unset\">Your account is not set: <a href=\"#/account\">set your account's size and risk</a>.</p><p class=\"card-money\" data-account=\"set\">kept money</p>"
            + "<ul><li data-line=\"stop\">Its stop</li><li data-line=\"size\">Your account is not set: <a href=\"#/account\">set it</a></li></ul>"
            + "<p class=\"date-line\">Night of 2026-10-27 · tonight's night runs at <time class=\"night-due\" datetime=\"2026-10-28T23:00Z\" title=\"23:00 UTC\">19:00 ET</time></p></section>";

        var copied = SiteCopy.Clean(Fragment);

        foreach (var gone in new[] { "<form", "action=", "/quotes/", "/watch/", "/passes/", "/exports/", "/analysts/", "/analyst-ratings/", "data-session-open", "data-interval-minutes", "data-asked", "data-select-href", "data-ask=", "night-picker", "np-move", "Research may spend", "fold-hide", "<select", "firm-actions", "data-graph=\"ratings\"", "Get analyst ratings", "Add to watch list", "Regenerate Report", "rs-controls", "Your account is not set", "#/account", "night-due", "tonight's night runs at" })
        {
            Assert.DoesNotContain(gone, copied, StringComparison.Ordinal);
        }

        // What reads in the browser alone stays: the pick's card's control, the fold, the analysts' figure, a span beside the
        // removed picker, the row and the version the card was drawn at, in words.
        foreach (var kept in new[] { "<span class=\"kept-span\">kept</span>", "class=\"card-toggle\"", "<details><summary>Fold</summary><p>inside</p></details>", "<h3>The analysts</h3><div class=\"fig\"></div>", "data-ticker=\"ROP\"", "Showing <span class=\"chosen\">Version 2</span>", "<p class=\"card-money\" data-account=\"set\">kept money</p>", "<p class=\"date-line\">Night of 2026-10-27</p>", "<ul><li data-line=\"stop\">Its stop</li></ul>" })
        {
            Assert.Contains(kept, copied, StringComparison.Ordinal);
        }

        // The price and the analysts marked as the store held them at the night's close.
        var marked = SiteCopy.MarkedAtTheClose(copied, CopyNight);

        Assert.Contains("<span class=\"copy-asof\">As of the close of 27 Oct.</span> <span class=\"m-when\">", marked, StringComparison.Ordinal);
        Assert.Contains("<h3>The analysts</h3><p class=\"copy-asof\">The analysts as of the close of 27 Oct, as the store held them when this copy was made; nothing here asks for them again.</p>", marked, StringComparison.Ordinal);
    }

    [Fact]
    public void EveryLinkResolvesToAFileTheCopyHoldsOrSaysItIsNotInTheCopy()
    {
        var held = new HashSet<string>(["#/", "#/researched", "#/researched?sort=stale", "#/chart/HELD", "#/name/HELD", "#/name/PAGE"], StringComparer.Ordinal);
        const string Fragment =
            "<a href=\"#/chart/HELD\">HELD</a>|<a href=\"#/chart/PAGE\">PAGE</a>|<a href=\"#/chart/NONE\">NONE</a>|"
            + "<a class=\"sort\" href=\"#/researched?universe=500&amp;sort=stale\">Staleness</a>|<a class=\"sort\" href=\"#/researched?sort=written&amp;page=2\">Written</a>|"
            + "<a href=\"#/run/2026-10-27\">What ran tonight</a>|<a href=\"#move-1\">a move</a>|<a href=\"https://www.sec.gov/x.htm\">the release</a>|<a href=\"/proof/report-pages\">proof</a>";

        var linked = SiteCopy.Relink(Fragment, "#/researched", held).Split('|');

        // A held chart stays a chart; a stock whose page alone is held opens its page; neither held says so.
        Assert.Equal("<a href=\"#/chart/HELD\">HELD</a>", linked[0]);
        Assert.Equal("<a href=\"#/name/PAGE\">PAGE</a>", linked[1]);
        Assert.Equal("<span class=\"not-held\">NONE<span class=\"not-held-note\">Not in this copy</span></span>", linked[2]);

        // A held view of the same screen is keyed as the copy keys it, and one not held is left as its words.
        Assert.Equal("<a class=\"sort\" href=\"#/researched?sort=stale\">Staleness</a>", linked[3]);
        Assert.Equal("<span class=\"unlinked\">Written</span>", linked[4]);

        // Another screen not held, a place on the page, a source's own site and an address of the surface.
        Assert.Equal("<span class=\"not-held\">What ran tonight<span class=\"not-held-note\">Not in this copy</span></span>", linked[5]);
        Assert.Equal("<a href=\"#move-1\">a move</a>", linked[6]);
        Assert.Equal("<a href=\"https://www.sec.gov/x.htm\">the release</a>", linked[7]);
        Assert.Equal("<span class=\"not-held\">proof<span class=\"not-held-note\">Not in this copy</span></span>", linked[8]);

        // The reports index opens every stock's page, a held chart among them, and a stock's own page never links itself.
        Assert.Equal("<a href=\"#/name/HELD\">HELD</a>", SiteCopy.Relink("<a href=\"#/chart/HELD\">HELD</a>", "#/researched", held, stockPagesFirst: true));
        Assert.Equal("<span class=\"not-held\">Its chart<span class=\"not-held-note\">Not in this copy</span></span>", SiteCopy.Relink("<a href=\"#/chart/PAGE\">Its chart</a>", "#/name/PAGE", held));
    }

    [Fact]
    public void TheCopysShellFetchesOnlyItsOwnFilesAndNamesItselfACopy()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal) { ["#/"] = "screens/tonight.html", ["#/name/AAPL"] = "screens/name/AAPL.html" };
        var shell = SiteCopy.Shell(CopyNight, files);

        Assert.Contains("<p class=\"copy-banner\" role=\"note\">A read-only copy as of the night of 27 Oct 2026. Paper trading only; nothing here is advice.</p>", shell, StringComparison.Ordinal);
        Assert.Contains("<title>" + PublishedSite.Name + "</title>", shell, StringComparison.Ordinal);
        Assert.Contains("EquityBrief<span class=\"copy-tag\">read-only copy</span>", shell, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/researched\" data-view=\"researched\">Reports</a>", shell, StringComparison.Ordinal);

        // Its list of files is the routes the copy holds, and its one fetch reads a file from that list.
        var listed = Regex.Match(shell, "const held = (?<files>\\{.*?\\});").Groups["files"].Value;

        Assert.Equal(files, JsonSerializer.Deserialize<Dictionary<string, string>>(listed));
        Assert.Equal(["fetch(file)"], Regex.Matches(shell, @"fetch\([^)]*\)").Select(match => match.Value).ToArray());
        Assert.Contains($"const place = {JsonSerializer.Serialize(SiteCopy.PlaceKeys)};", shell, StringComparison.Ordinal);

        // No press, no form, no address of the surface, no other site, and none of the screens the copy does not hold.
        foreach (var never in new[] { SinglePageApp.PassHeader, "POST", "method:", "<form", "/screens/", "/passes/", "/quotes/", "/watch/", "http://", "https://", "#/run/", "#/queue", "#/watch", SinglePageApp.AccountRoute, "find" })
        {
            Assert.DoesNotContain(never, shell, StringComparison.Ordinal);
        }

        var missing = SiteCopy.Missing(CopyNight);

        Assert.Contains(SiteCopy.Banner(CopyNight), missing, StringComparison.Ordinal);
        Assert.Contains(SiteCopy.NotHeld, missing, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", missing, StringComparison.Ordinal);
    }

    [Fact]
    public void AReportsVersionsAreListedNewestFirstWithThePageShownNamed()
    {
        IReadOnlyList<(DateOnly, string)> versions = [(new DateOnly(2026, 10, 22), "#/name/ROP"), (new DateOnly(2026, 10, 11), "#/name/ROP/2026-10-12")];

        var newest = SiteCopy.Versions("ROP", versions, "#/name/ROP");
        var earlier = SiteCopy.Versions("ROP", versions, "#/name/ROP/2026-10-12");

        Assert.Contains("data-versions=\"2\"", newest, StringComparison.Ordinal);
        Assert.Contains("<li><b>22 Oct 2026, the newest</b>, this page</li><li><a href=\"#/name/ROP/2026-10-12\">11 Oct 2026</a></li>", newest, StringComparison.Ordinal);
        Assert.Contains("<li><a href=\"#/name/ROP\">22 Oct 2026, the newest</a></li><li><b>11 Oct 2026</b>, this page</li>", earlier, StringComparison.Ordinal);
        Assert.Equal(string.Empty, SiteCopy.Versions("ROP", [], "#/name/ROP"));
    }

    [Fact]
    public async Task TheReadOnlyCopyHoldsEachScreenWithNoControlAndEveryLinkInItResolvesToAFileItHolds()
    {
        using var store = await FixtureReplay.ResearchedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();
        using var folder = new TemporaryDirectory();
        var before = StoreBytesHash(store.DatabaseFile);
        var said = new List<string>();

        var copied = await SiteCopyCommand.WriteAsync(client, new ReadApi(store.DatabaseFile, SystemClock.ForUnitedStatesSessions()), folder.Path, said.Add);

        // Nothing refused, and the store's file not touched by a byte.
        Assert.Empty(copied.Refused);
        Assert.Equal(before, StoreBytesHash(store.DatabaseFile));

        // Tonight, Past picks, Paper trading, the Ledger, the reports index, the universe list and the Loop on each index,
        // and the researched stock's own page.
        foreach (var index in new[] { string.Empty, "?universe=400", "?universe=600" })
        {
            foreach (var screen in SiteCopyCommand.IndexScreens)
            {
                Assert.True(copied.Files.ContainsKey(SiteCopy.Canonical(screen + index)), $"{screen}{index} is not held.");
            }
        }

        Assert.True(copied.Files.ContainsKey("#/name/KEYS"));
        Assert.Contains(copied.Reports, report => report.Reports > 0);

        var shell = File.ReadAllText(Path.Combine(folder.Path, SiteCopy.ShellFile));
        var listed = JsonSerializer.Deserialize<Dictionary<string, string>>(Regex.Match(shell, "const held = (?<files>\\{.*?\\});").Groups["files"].Value)!;

        Assert.Equal(copied.Files.OrderBy(file => file.Key, StringComparer.Ordinal), listed.OrderBy(file => file.Key, StringComparer.Ordinal));
        Assert.Contains("connect-src 'self'", File.ReadAllText(Path.Combine(folder.Path, PublishedSite.HeadersFile)), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(folder.Path, SiteCopy.MissingFile)));

        foreach (var (route, file) in copied.Files)
        {
            var page = File.ReadAllText(Path.Combine(folder.Path, file));

            Assert.DoesNotContain("<form", page, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(SinglePageApp.PassHeader, page, StringComparison.Ordinal);
            Assert.DoesNotMatch("(?:action|data-[A-Za-z-]+)=\"/", page);

            // Every link to a screen names a route the copy holds.
            foreach (var link in SiteCopy.LinksIn(page))
            {
                Assert.True(copied.Files.ContainsKey(link), $"{route} links {link}, which the copy does not hold.");
            }
        }

        // The researched stock's page lists its report's versions.
        Assert.Contains("class=\"copy-versions\"", File.ReadAllText(Path.Combine(folder.Path, copied.Files["#/name/KEYS"])), StringComparison.Ordinal);
    }
}
