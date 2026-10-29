using System.Text;
using EquityBrief.Api.Reading;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;

namespace EquityBrief.Api.Cloudflare;

// What a copy wrote: the night it is of, each route it holds with its file, and each index's reports and their versions.
public sealed record SiteCopyWritten(
    DateOnly Night,
    IReadOnlyDictionary<string, string> Files,
    IReadOnlyList<(string Index, int Reports, int Versions)> Reports,
    IReadOnlyList<string> Refused);

// The read-only copy on Cloudflare Pages, written by the read surface's own verb. Each screen the copy holds is asked of
// the surface as the live shell asks for it, inside this process and over no network: Tonight, Past picks,
// Paper trading, the Ledger, the reports index, the universe list and the Loop on each index, the views each of those links
// on its own screen and every page of the universe list; a chart page for every stock Tonight or Paper trading links on any
// index; a stock page for each of those and for every stock with a written report, with each earlier dated version of a
// report on the page of the night that first drew it. Each is cleaned of its controls, its links resolved against the
// routes the copy holds, and written as a file under the folder named, beside the copy's shell, the page a missing route is
// answered with and the host's headers. It writes nothing to the store and asks no provider.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens with no control, a link it does not hold drawn as plain text
public static class SiteCopyCommand
{
    // How many views of one screen beside its first the copy holds where the screen links them, so a screen whose links
    // multiply stays bounded.
    public const int ViewsAScreen = 40;

    // How many pages of the universe list, its own and its sectors', the copy holds on one index at most.
    public const int ListPages = 80;

    // The screens each index draws, as first drawn.
    public static readonly IReadOnlyList<string> IndexScreens =
        ["#/", SinglePageApp.PicksRoute, SinglePageApp.PaperRoute, SinglePageApp.LedgerRoute, SinglePageApp.ResearchedRoute, SinglePageApp.UniverseRoute, SinglePageApp.LoopRoute];

    // The screens whose own links to other views of themselves the copy follows.
    static readonly string[] Followed = [SinglePageApp.PicksRoute, SinglePageApp.PaperRoute, SinglePageApp.LedgerRoute, SinglePageApp.ResearchedRoute];

    // The headers the host serves every file with: no request to any address but the copy's own, no form sent anywhere,
    // no page framed elsewhere and no address of the copy handed to a source's site, and the copy kept out of search.
    public const string Headers =
        "/*\n"
        + "  Content-Security-Policy: default-src 'self'; script-src 'self' 'unsafe-inline'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; form-action 'none'; base-uri 'none'; frame-ancestors 'none'\n"
        + "  Referrer-Policy: no-referrer\n"
        + "  X-Robots-Tag: noindex\n";

    public static async Task<SiteCopyWritten> WriteAsync(Func<string, Task<(int Status, string Body)>> surface, ReadApi read, string folder, Action<string> say)
    {
        var refused = new List<string>();

        if (await read.SchemaBehindAsync() is { } behind)
        {
            refused.Add($"the store's schema is behind the build's ({behind.Store} against {behind.Checkout}), so no screen is drawn as the surface draws it");

            return new SiteCopyWritten(DateOnly.MinValue, new Dictionary<string, string>(), [], refused);
        }

        if (await read.NewestNightAsync() is not { } night)
        {
            refused.Add("the store holds no night, so there is nothing to copy");

            return new SiteCopyWritten(DateOnly.MinValue, new Dictionary<string, string>(), [], refused);
        }

        var drawn = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();

        async Task<string?> Ask(string route)
        {
            if (drawn.TryGetValue(route, out var known))
            {
                return known;
            }

            if (!SiteCopy.Holdable(route) || SiteCopy.ServerRoute(route) is not { } server)
            {
                return null;
            }

            var (status, text) = await surface(server);

            if (status is < 200 or >= 300)
            {
                refused.Add($"{route} answered {status}, so it is not held");

                return null;
            }

            drawn[route] = text;
            order.Add(route);

            return text;
        }

        static string OnIndex(string screen, UniverseChoice index) =>
            SiteCopy.Canonical(index == Universes.Large ? screen : screen + "?" + Universes.Query + "=" + index.Word);

        // Each index's screens as first drawn.
        foreach (var index in Universes.Offered)
        {
            foreach (var screen in IndexScreens)
            {
                await Ask(OnIndex(screen, index));
            }

            say($"{index.Name}: its screens drawn, {drawn.Count} held so far");
        }

        // The views each followed screen links on its own first view, one step deep; each paper account's trade log on Past
        // picks that Paper trading links; and every page of the universe list and of each sector's, its sorts and other
        // filters left out since they multiply past any bound; each bounded.
        foreach (var index in Universes.Offered)
        {
            foreach (var screen in Followed)
            {
                if (drawn.TryGetValue(OnIndex(screen, index), out var shown))
                {
                    foreach (var view in SiteCopy.LinksIn(shown).Where(link => SiteCopy.PathOf(link) == screen).Take(ViewsAScreen))
                    {
                        await Ask(view);
                    }
                }
            }

            if (drawn.TryGetValue(OnIndex(SinglePageApp.PaperRoute, index), out var paper))
            {
                foreach (var log in SiteCopy.LinksIn(paper).Where(link => SiteCopy.PathOf(link) == SinglePageApp.PicksRoute).Take(ViewsAScreen))
                {
                    await Ask(log);
                }
            }

            var pages = new Queue<string>([OnIndex(SinglePageApp.UniverseRoute, index)]);
            var seen = new HashSet<string>(pages, StringComparer.Ordinal);

            while (pages.Count > 0 && seen.Count <= ListPages)
            {
                if (await Ask(pages.Dequeue()) is { } page)
                {
                    foreach (var next in SiteCopy.LinksIn(page).Where(link => SiteCopy.PathOf(link) == SinglePageApp.UniverseRoute && PagesAndSectors(link) && seen.Add(link)))
                    {
                        pages.Enqueue(next);
                    }
                }
            }
        }

        say($"the indices' views drawn, {drawn.Count} held so far");

        // Every stock Tonight or Paper trading links on any index, and every stock with a written report.
        var charted = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var index in Universes.Offered)
        {
            foreach (var screen in new[] { "#/", SinglePageApp.PaperRoute })
            {
                if (drawn.TryGetValue(OnIndex(screen, index), out var shown))
                {
                    charted.UnionWith(SiteCopy.LinksIn(shown).Where(link => link.StartsWith("#/chart/", StringComparison.Ordinal) && !link.Contains('?', StringComparison.Ordinal)).Select(link => link["#/chart/".Length..]));
                }
            }
        }

        var reported = new SortedSet<string>((await read.ResearchedAsync()).Select(row => row.Ticker), StringComparer.Ordinal);

        foreach (var ticker in charted)
        {
            await Ask("#/chart/" + ticker);
        }

        say($"{charted.Count} stocks' chart pages drawn");

        foreach (var ticker in charted.Union(reported))
        {
            await Ask("#/name/" + ticker);
        }

        say($"{charted.Union(reported).Count()} stocks' pages drawn");

        // Each report's dated versions: the newest on the stock's own page, and each earlier one on the page of the first
        // night on or after the day it was written, which is the first page that draws it, a night that would be tonight's
        // folding into the stock's own page.
        var nights = await read.NightsAsync();
        var versions = new Dictionary<string, IReadOnlyList<(DateOnly Written, string Route)>>(StringComparer.Ordinal);

        foreach (var ticker in reported)
        {
            var listed = new List<(DateOnly Written, string Route)>();

            foreach (var written in await read.ReportDaysAsync(ticker))
            {
                var first = nights.Where(stored => stored >= written).DefaultIfEmpty(night).Min();
                var route = first >= night ? "#/name/" + ticker : "#/name/" + ticker + "/" + first.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

                if (listed.All(version => version.Route != route) && (route == "#/name/" + ticker || await Ask(route) is not null))
                {
                    listed.Add((written, route));
                }
            }

            versions[ticker] = listed;
        }

        say($"{versions.Values.Sum(listed => listed.Count)} report versions held over {reported.Count} reports");

        // Every held screen cleaned, its links resolved against what is held, and written under the folder.
        var held = new HashSet<string>(order, StringComparer.Ordinal);
        var files = new Dictionary<string, string>(StringComparer.Ordinal);

        Directory.CreateDirectory(folder);

        foreach (var route in order)
        {
            var path = SiteCopy.PathOf(route);
            var markup = SiteCopy.Clean(drawn[route]);

            if (path.StartsWith("#/chart/", StringComparison.Ordinal) || path.StartsWith("#/name/", StringComparison.Ordinal))
            {
                markup = SiteCopy.MarkedAtTheClose(markup, night);
            }

            if (path.StartsWith("#/name/", StringComparison.Ordinal) && versions.TryGetValue(path["#/name/".Length..].Split('/')[0], out var listed))
            {
                markup = SiteCopy.Versions(path["#/name/".Length..].Split('/')[0], listed, route) + markup;
            }

            markup = SiteCopy.Relink(markup, route, held, stockPagesFirst: path == SinglePageApp.ResearchedRoute);

            var file = SiteCopy.FileOf(route);

            files[route] = file;
            Write(folder, file, markup);
        }

        Write(folder, SiteCopy.ShellFile, SiteCopy.Shell(night, files));
        Write(folder, SiteCopy.MissingFile, SiteCopy.Missing(night));
        Write(folder, EquityBrief.Core.Cloudflare.PublishedSite.HeadersFile, Headers);

        // Each index's reports, as its own reports index lists them, and the versions they hold.
        var counts = Universes.Offered
            .Select(index =>
            {
                var listedOn = drawn.TryGetValue(OnIndex(SinglePageApp.ResearchedRoute, index), out var shown)
                    ? SiteCopy.LinksIn(shown).Where(link => link.StartsWith("#/chart/", StringComparison.Ordinal) && !link.Contains('?', StringComparison.Ordinal)).Select(link => link["#/chart/".Length..]).Distinct(StringComparer.Ordinal).ToArray()
                    : [];

                return (index.Name, listedOn.Length, listedOn.Sum(ticker => versions.TryGetValue(ticker, out var listed) ? listed.Count : 0));
            })
            .ToArray();

        return new SiteCopyWritten(night, files, counts, refused);
    }

    // A link to the universe list naming nothing but a page, a sector and the index.
    static bool PagesAndSectors(string route) =>
        route.IndexOf('?', StringComparison.Ordinal) is var cut && cut >= 0
        && route[(cut + 1)..].Split('&').All(pair => pair.StartsWith("page=", StringComparison.Ordinal) || pair.StartsWith("sector=", StringComparison.Ordinal) || pair.StartsWith(Universes.Query + "=", StringComparison.Ordinal));

    // One file of the copy, written whole in UTF-8 under the folder, its folders made first.
    static void Write(string folder, string file, string text)
    {
        var target = Path.Combine([folder, .. file.Split('/')]);

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, new UTF8Encoding(false).GetBytes(text));
    }
}
