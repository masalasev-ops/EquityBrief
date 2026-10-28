using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Cloudflare;
using EquityBrief.Web.App;

namespace EquityBrief.Web.Cloudflare;

// The read-only copy of the site on Cloudflare Pages: the read surface's own screens saved as files, each with every
// control and every address of the surface taken out and every link resolved against the routes the copy holds, read
// through a shell of its own that fetches only those files and names itself a copy on every page.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens and holds no control
public static partial class SiteCopy
{
    // The folder inside the copy holding the screens.
    public const string Screens = "screens";

    // The shell's own file, and the page a route the copy does not hold is answered with.
    public const string ShellFile = "index.html";

    public const string MissingFile = "404.html";

    public const string NotHeld = "Not in this copy";

    // The keys of a route's query that only open a fold, pick a row or light a reason: a link carrying one opens the screen
    // it names without it, since the copy holds a screen as it is first drawn.
    public static readonly IReadOnlyList<string> PlaceKeys = ["close", "name", "reason"];

    // The screens a stock's own pages are, which read no index from their link.
    static readonly string[] StockScreens = ["#/chart/", "#/name/"];

    // The routes each menu entry opens, in the bar's order, with the menu each sits in where it sits in one.
    public static readonly IReadOnlyList<(string Route, string Words, string View, string? Menu)> Menu =
    [
        ("#/", "Tonight", "tonight", null),
        (SinglePageApp.UniverseRoute, "Universe", "universe", null),
        (SinglePageApp.PaperRoute, "Paper trading", "paper", null),
        (SinglePageApp.LoopRoute, "Loop", "loop", null),
        (SinglePageApp.ResearchedRoute, "Reports", "researched", null),
        (SinglePageApp.PicksRoute, "Past picks", "picks", "Records"),
        (SinglePageApp.LedgerRoute, "Ledger", "ledger", "Records"),
    ];

    // The day a night is named by on the copy, as "27 Oct 2026".
    public static string Day(DateOnly day) => FormattableString.Invariant($"{day:dd MMM yyyy}");

    // The line every page of the copy carries above the screen.
    public static string Banner(DateOnly night) =>
        "<p class=\"copy-banner\" role=\"note\">A read-only copy as of the night of " + Day(night) + ". Paper trading only; nothing here is advice.</p>";

    // What a figure the live pages ask for on opening says in the copy in its place: the store's at the night's close.
    public static string AsOfTheClose(DateOnly night) => "as of the close of " + FormattableString.Invariant($"{night:dd MMM}");

    // A hash route as the copy keys it: its path, and its query's keys sorted with the keys that only carry a place left
    // out, and the S&P 500's own index word, since a route naming no index reads the S&P 500, and any index on a stock's
    // own pages, which read none from the link. A key named twice is read once, by its first value.
    public static string Canonical(string hash)
    {
        var cut = hash.IndexOf('?', StringComparison.Ordinal);
        var path = cut < 0 ? hash : hash[..cut];

        if (path is "" or "#")
        {
            path = "#/";
        }

        var stock = StockScreens.Any(screen => path.StartsWith(screen, StringComparison.Ordinal));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new List<(string Key, string Value)>();

        foreach (var pair in (cut < 0 ? string.Empty : hash[(cut + 1)..]).Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = pair.IndexOf('=', StringComparison.Ordinal);
            var key = Unescaped(equals < 0 ? pair : pair[..equals]);
            var value = equals < 0 ? string.Empty : Unescaped(pair[(equals + 1)..]);

            if (PlaceKeys.Contains(key)
                || (key == Universes.Query && (stock || value == Universes.Large.Word))
                || !seen.Add(key))
            {
                continue;
            }

            pairs.Add((key, value));
        }

        pairs.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));

        return path + (pairs.Count == 0 ? string.Empty : "?" + string.Join("&", pairs.Select(pair => Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(pair.Value))));
    }

    static string Unescaped(string part) => Uri.UnescapeDataString(part.Replace('+', ' '));

    // The path of a route, its query left off.
    public static string PathOf(string route) =>
        route.IndexOf('?', StringComparison.Ordinal) is var cut and >= 0 ? route[..cut] : route;

    // Whether a route can be held: every key in its query a word of letters, digits, dots, hyphens and underscores, and
    // every value those and spaces, which the shell's own key of a link escapes byte for byte as this one does.
    public static bool Holdable(string canonical) =>
        canonical.IndexOf('?', StringComparison.Ordinal) is var cut && (cut < 0 || canonical[(cut + 1)..].Split('&').All(pair => PlainWord().IsMatch(Unescaped(pair))));

    [GeneratedRegex(@"^[A-Za-z0-9._-]+=[A-Za-z0-9._ -]*$")]
    private static partial Regex PlainWord();

    // The file a held route is written to: the screens folder, a folder for each segment of the route's path but the last,
    // and the last with each of the query's keys and values after a tilde, every character but a letter, a digit, a dot and
    // a hyphen written as an underscore and its code, so no two routes share a file. A name past the longest a file system
    // takes keeps its opening and ends on the hash of its whole, which still names it alone.
    public static string FileOf(string canonical)
    {
        var cut = canonical.IndexOf('?', StringComparison.Ordinal);
        var path = (cut < 0 ? canonical : canonical[..cut]).TrimStart('#').Trim('/');
        var segments = path.Length == 0 ? ["tonight"] : path.Split('/').Select(Safe).ToArray();

        if (cut >= 0)
        {
            segments[^1] += string.Concat(canonical[(cut + 1)..].Split('&').Select(pair =>
            {
                var equals = pair.IndexOf('=', StringComparison.Ordinal);

                return "~" + Safe(Unescaped(pair[..equals])) + "-" + Safe(Unescaped(pair[(equals + 1)..]));
            }));
        }

        return Screens + "/" + string.Join("/", segments.Select(Bounded)) + ".html";
    }

    // The longest a file's name is written, well inside what every file system and the host take.
    public const int LongestName = 100;

    static string Bounded(string segment) =>
        segment.Length <= LongestName
            ? segment
            : segment[..(LongestName - 17)] + "~" + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(segment)))[..16];

    // The names a file may not take on Windows, whatever its extension.
    static readonly string[] Reserved = ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"];

    static string Safe(string part)
    {
        var safe = new StringBuilder();

        foreach (var character in part)
        {
            if (char.IsAsciiLetterOrDigit(character) || character is '.' or '-')
            {
                safe.Append(character);
            }
            else
            {
                safe.Append('_').Append(((int)character).ToString("x2", CultureInfo.InvariantCulture));
            }
        }

        var word = safe.Length == 0 ? "_" : safe.ToString();

        return Reserved.Contains(word.Split('.')[0], StringComparer.OrdinalIgnoreCase) || word.StartsWith('.') ? "_" + word : word;
    }

    // The read surface's route a held route's screen is read from, as the live shell asks for it; none for a route the
    // copy never holds.
    public static string? ServerRoute(string canonical)
    {
        var path = PathOf(canonical);
        var query = canonical.Length > path.Length ? canonical[path.Length..] : string.Empty;

        if (path == "#/")
        {
            return "/screens/tonight" + query;
        }

        foreach (var screen in StockScreens)
        {
            if (path.StartsWith(screen, StringComparison.Ordinal) && path.Length > screen.Length)
            {
                return "/screens/" + screen[2..] + string.Join("/", path[screen.Length..].Split('/').Select(Uri.EscapeDataString)) + query;
            }
        }

        return path is SinglePageApp.UniverseRoute or SinglePageApp.LedgerRoute or SinglePageApp.LoopRoute or SinglePageApp.PicksRoute or SinglePageApp.ResearchedRoute or SinglePageApp.PaperRoute
            ? "/screens/" + path[2..] + query
            : null;
    }

    // Every link a fragment carries to a route of the shell, as the copy keys each.
    public static IReadOnlyList<string> LinksIn(string fragment) =>
    [
        .. Anchor().Matches(fragment)
            .Select(anchor => AttributeOf(anchor.Groups["attrs"].Value, "href"))
            .OfType<string>()
            .Select(href => WebUtility.HtmlDecode(href))
            .Where(href => href.StartsWith("#/", StringComparison.Ordinal))
            .Select(Canonical)
            .Distinct(StringComparer.Ordinal),
    ];

    [GeneratedRegex(@"<a\b(?<attrs>[^>]*)>(?<text>.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex Anchor();

    static string? AttributeOf(string attributes, string name) =>
        Regex.Match(attributes, $@"(?:^|\s){Regex.Escape(name)}=""(?<value>[^""]*)""", RegexOptions.IgnoreCase) is { Success: true } found
            ? found.Groups["value"].Value
            : null;

    // Every link in a fragment resolved against the routes the copy holds. A held route's link names it as the copy keys it;
    // a stock's chart not held opens its stock page where that is held, as every row of the reports index does; a link to
    // the same screen under a view not held is left as its words; a link to a place on the page or to another site stands;
    // and any other link, to a screen or an address of the surface the copy does not hold, reads "Not in this copy" in its
    // place beside its words.
    public static string Relink(string fragment, string page, IReadOnlySet<string> held, bool stockPagesFirst = false) =>
        Anchor().Replace(fragment, anchor =>
        {
            var attributes = anchor.Groups["attrs"].Value;
            var words = anchor.Groups["text"].Value;

            if (AttributeOf(attributes, "href") is not { } href)
            {
                return anchor.Value;
            }

            var link = WebUtility.HtmlDecode(href);

            if (link.StartsWith("#/", StringComparison.Ordinal))
            {
                var route = Canonical(link);

                if (Resolved(route, page, held, stockPagesFirst) is { } resolved)
                {
                    return "<a" + Regex.Replace(attributes, @"(^|\s)href=""[^""]*""", "$1href=\"" + WebUtility.HtmlEncode(resolved) + "\"", RegexOptions.IgnoreCase) + ">" + words + "</a>";
                }

                return PathOf(route) == PathOf(page)
                    ? "<span class=\"unlinked\">" + words + "</span>"
                    : NotHeldLink(words);
            }

            return link.StartsWith('#') || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                ? anchor.Value
                : NotHeldLink(words);
        });

    static string NotHeldLink(string words) =>
        "<span class=\"not-held\">" + words + "<span class=\"not-held-note\">" + NotHeld + "</span></span>";

    static string? Resolved(string route, string page, IReadOnlySet<string> held, bool stockPagesFirst)
    {
        if (route.StartsWith("#/chart/", StringComparison.Ordinal) && !route.Contains('?', StringComparison.Ordinal))
        {
            var stockPage = "#/name/" + route["#/chart/".Length..];

            if ((stockPagesFirst || !held.Contains(route)) && held.Contains(stockPage) && stockPage != page)
            {
                return stockPage;
            }
        }

        return held.Contains(route) ? route : null;
    }

    // A fragment with every control the copy cannot answer taken out: every form with its presses and its fields, a
    // choice of a view the copy does not hold left as the words of the one chosen, the folds' Hide control, the night's
    // date arrows and calendar and when the next night runs, the export link, every line stating research spend against
    // its caps, the controls that write a report again, a card's and a brief's line asking for the account's settings,
    // Yahoo's firm actions and the TipRanks section; and every attribute naming an address of the surface, a live quote's
    // session or a row's pick.
    public static string Clean(string fragment)
    {
        var text = Removed(fragment, "form", _ => true);

        text = Removed(text, "select", start => HasAttribute(start, "data-rule-choice") || HasAttribute(start, "data-merge-key") || HasAttribute(start, "data-compare"), Chosen);
        text = Removed(text, "button", start => HasClass(start, "fold-hide"));
        text = Removed(text, "span", start => HasClass(start, "night-picker"));
        text = Removed(text, "p", start => HasClass(start, "export") || HasClass(start, "research-cost") || (HasClass(start, "card-money") && AttributeOf(start, "data-account") == "unset"));
        text = Removed(text, "li", start => AttributeOf(start, "data-line") == "size");
        text = Removed(text, "div", start => HasClass(start, "rs-controls") || HasClass(start, "firm-actions"));
        text = Removed(text, "section", start => AttributeOf(start, "data-graph") == "ratings");
        text = NightDue().Replace(text, string.Empty);

        return SurfaceAttribute().Replace(text, string.Empty);
    }

    // When the next night runs, which the date line names before it runs and the copy, made after it, does not.
    [GeneratedRegex(@" · tonight's night runs at <time class=""night-due""[^>]*>[^<]*</time>")]
    private static partial Regex NightDue();

    // An attribute the copy carries none of: one whose value is an address of the surface, and the live quote's session,
    // its asking interval and the instant it last asked, a row's pick and the analysts' ask.
    [GeneratedRegex(@"\s(?:data-[A-Za-z-]+=""/[^""]*""|data-(?:session-open|session-close|interval-minutes|asked|select-href|ask)=""[^""]*"")")]
    private static partial Regex SurfaceAttribute();

    static bool HasAttribute(string start, string name) => AttributeOf(start, name) is not null;

    static bool HasClass(string start, string name) =>
        AttributeOf(start, "class") is { } classes && classes.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(name, StringComparer.Ordinal);

    // A choice of a view the copy does not hold, as the words of the option it stood at.
    static string Chosen(string select)
    {
        var options = Regex.Matches(select, @"<option\b(?<attrs>[^>]*)>(?<text>.*?)</option>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var chosen = options.FirstOrDefault(option => Regex.IsMatch(option.Groups["attrs"].Value, @"(^|\s)selected(\s|=|$)", RegexOptions.IgnoreCase)) ?? options.FirstOrDefault();

        return "<span class=\"chosen\">" + (chosen?.Groups["text"].Value ?? string.Empty) + "</span>";
    }

    // Every element of a tag whose opening matches, taken out through its own closing tag with the same tag nested inside
    // it counted, or put in the place of what the replacement makes of it; an element whose closing tag is not found is
    // left as it stands, for the folder's check to name.
    static string Removed(string html, string tag, Func<string, bool> matches, Func<string, string>? replacement = null)
    {
        var tags = new Regex($@"<(?<close>/?){tag}\b[^>]*>", RegexOptions.IgnoreCase);
        var kept = new StringBuilder();
        var at = 0;

        for (var opening = tags.Match(html); opening.Success; opening = opening.NextMatch())
        {
            if (opening.Index < at || opening.Groups["close"].Value == "/" || !matches(opening.Value))
            {
                continue;
            }

            var depth = 0;
            var end = -1;

            for (var next = opening; next.Success; next = next.NextMatch())
            {
                depth += next.Groups["close"].Value == "/" ? -1 : 1;

                if (depth == 0)
                {
                    end = next.Index + next.Length;

                    break;
                }
            }

            if (end < 0)
            {
                continue;
            }

            kept.Append(html, at, opening.Index - at);
            kept.Append(replacement?.Invoke(html[opening.Index..end]) ?? string.Empty);
            at = end;
        }

        return kept.Append(html, at, html.Length - at).ToString();
    }

    // A stock's page or chart page with the price it drew marked as the store's at the night's close, before the line
    // saying when it was, and the analysts' section marked the same beneath its heading.
    public static string MarkedAtTheClose(string fragment, DateOnly night)
    {
        var words = AsOfTheClose(night);
        var marked = fragment.Replace("<span class=\"m-when\">", "<span class=\"copy-asof\">" + char.ToUpperInvariant(words[0]) + words[1..] + ".</span> <span class=\"m-when\">", StringComparison.Ordinal);

        return Regex.Replace(
            marked,
            @"(<section class=""chart-graph"" data-graph=""analysts""[^>]*><h3>[^<]*</h3>)",
            "$1<p class=\"copy-asof\">The analysts " + words + ", as the store held them when this copy was made; nothing here asks for them again.</p>");
    }

    // The list of a report's dated versions a stock's page carries in the copy, newest first: the newest on the stock's
    // own page, each earlier one on the page of the night that first drew it, and the version the page shows named and not
    // linked.
    public static string Versions(string ticker, IReadOnlyList<(DateOnly Written, string Route)> versions, string shown)
    {
        if (versions.Count == 0)
        {
            return string.Empty;
        }

        var entries = versions.Select((version, at) =>
        {
            var words = Day(version.Written) + (at == 0 ? ", the newest" : string.Empty);

            return version.Route == shown
                ? "<li><b>" + words + "</b>, this page</li>"
                : "<li><a href=\"" + WebUtility.HtmlEncode(version.Route) + "\">" + words + "</a></li>";
        });

        return FormattableString.Invariant($"<nav class=\"copy-versions\" aria-label=\"Versions of this report\" data-ticker=\"{WebUtility.HtmlEncode(ticker)}\" data-versions=\"{versions.Count}\"><span class=\"lbl\">Versions of {WebUtility.HtmlEncode(ticker)}'s report</span><ol>")
            + string.Concat(entries) + "</ol></nav>";
    }

    // The stylesheet the copy adds to the page's own: the banner and the brand's tag saying it is a copy, a link the copy
    // does not hold and the marks it adds.
    public const string CopyCss =
        ".copy-banner{margin:0;padding:8px 16px;background:var(--warn-fill);color:var(--ink);border-bottom:1px solid var(--hair-2);font-size:14px;text-align:center}"
        + ".copy-tag{margin-left:.5em;padding:1px 8px;border-radius:999px;background:var(--warn-fill);color:var(--ink);font-size:12px;font-weight:600;vertical-align:middle}"
        + ".not-held-note{margin-left:.4em;font-size:.8em;font-style:italic;color:var(--soft)}"
        + ".unlinked{color:inherit}"
        + ".copy-asof{font-size:13px;color:var(--soft);font-style:italic}"
        + "p.copy-asof{margin:4px 0 8px}"
        + ".copy-versions{margin:14px 0 0;padding:10px 14px;border:1px solid var(--hair-2);border-radius:8px;background:var(--surface);font-size:14px}"
        + ".copy-versions .lbl{font-weight:600;margin-right:.6em}"
        + ".copy-versions ol{display:inline;margin:0;padding:0;list-style:none}"
        + ".copy-versions li{display:inline}"
        + ".copy-versions li+li::before{content:\" \\00b7 \";color:var(--soft)}"
        + ".copy-missing{margin:24px 0}";

    // The brand the bar draws, naming the page a read-only copy.
    const string Brand = "EquityBrief<span class=\"copy-tag\">read-only copy</span>";

    // The page a route the copy does not hold is answered with, which stops the host answering every missing file with the
    // shell.
    public static string Missing(DateOnly night) =>
        "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n<title>" + NotHeld + " · " + PublishedSite.Name + "</title>\n<style>\n"
        + Stylesheet.Css + CopyCss
        + "\n</style>\n</head>\n<body>\n<header class=\"mast\"><div class=\"wrap\"><a class=\"m-brand\" href=\"./\">" + Brand + "</a></div></header>\n" + Banner(night)
        + "\n<main class=\"wrap\"><p class=\"notice copy-missing\">" + NotHeld + ". <a href=\"./\">Tonight</a></p></main>\n</body>\n</html>\n";

    // The copy's shell: the page's stylesheet, a bar naming the page a read-only copy and holding the screens the copy holds,
    // "Reports" opening the reports index, the banner, and a script that reads the route from the address, finds the file
    // the copy holds for it in its own list and puts that file's markup on the screen. It fetches nothing but those files,
    // sends nothing and asks no other address, and what it does beyond that is drawn in the browser alone: the palette, the
    // menu, a fold or a pick's card opened, a place on the page scrolled to, a tile bringing its card into view, the chart's
    // levels and its candle under the pointer.
    public static string Shell(DateOnly night, IReadOnlyDictionary<string, string> files)
    {
        var bar = new StringBuilder("<nav class=\"m-nav\" aria-label=\"Screens\">");

        foreach (var group in Menu.GroupBy(entry => entry.Menu))
        {
            var links = string.Concat(group.Select(entry => $"<a href=\"{entry.Route}\" data-view=\"{entry.View}\">{entry.Words}</a>"));

            bar.Append(group.Key is { } menu
                ? $"<div class=\"m-group\" data-group=\"{menu.ToLowerInvariant()}\"><button type=\"button\" class=\"m-group-btn\" aria-expanded=\"false\">{menu}</button><div class=\"m-drop\">{links}</div></div>"
                : links);
        }

        bar.Append("</nav>");

        var held = System.Text.Json.JsonSerializer.Serialize(files.OrderBy(file => file.Key, StringComparer.Ordinal).ToDictionary(file => file.Key, file => file.Value));
        var place = System.Text.Json.JsonSerializer.Serialize(PlaceKeys);

        return $$$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
            <title>{{{PublishedSite.Name}}}</title>
            <style>
            {{{Stylesheet.Css}}}
            {{{CopyCss}}}
            </style>
            <script>
            try { const kept = localStorage.getItem('eb-theme'); if (kept === 'dark' || kept === 'light') { document.documentElement.setAttribute('data-eb-theme', kept); } } catch (error) { }
            </script>
            </head>
            <body>
            <header class="mast" id="mast"><div class="wrap"><a class="m-brand" href="#/">{{{Brand}}}</a><div class="m-panel" id="menu">{{{bar}}}<button type="button" class="theme" id="theme">Dark palette</button></div><div class="m-index" id="index-slot"></div><button type="button" class="m-menu" id="menu-button" aria-expanded="false" aria-controls="menu">Menu</button></div></header>
            {{{Banner(night)}}}
            <div class="wrap page-head" id="identity"></div>
            <main class="wrap" id="screen"></main>
            <script>
            const held = {{{held}}};
            const place = {{{place}}};
            const screen = document.getElementById('screen');
            const identity = document.getElementById('identity');
            // A link as the copy keys it, read as the copy's own list was keyed when it was written.
            function canonical(hash) {
              const cut = hash.indexOf('?');
              let path = cut < 0 ? hash : hash.slice(0, cut);
              if (path === '' || path === '#') { path = '#/'; }
              const stock = path.startsWith('#/chart/') || path.startsWith('#/name/');
              const seen = new Set();
              const pairs = [];
              for (const [key, value] of new URLSearchParams(cut < 0 ? '' : hash.slice(cut + 1))) {
                if (place.includes(key) || (key === '{{{Universes.Query}}}' && (stock || value === '{{{Universes.Large.Word}}}')) || seen.has(key)) { continue; }
                seen.add(key);
                pairs.push([key, value]);
              }
              pairs.sort((left, right) => left[0] < right[0] ? -1 : left[0] > right[0] ? 1 : 0);
              return path + (pairs.length ? '?' + pairs.map((pair) => encodeURIComponent(pair[0]) + '=' + encodeURIComponent(pair[1])).join('&') : '');
            }
            async function show() {
              const route = canonical(location.hash);
              const file = held[route];
              let markup = null;
              if (file) {
                try { const answer = await fetch(file); markup = answer.ok ? await answer.text() : null; } catch (error) { markup = null; }
              }
              screen.innerHTML = markup !== null ? markup : '<p class="notice copy-missing" role="status">{{{NotHeld}}}. <a href="#/">Tonight</a></p>';
              const head = screen.querySelector('.screen-mast');
              identity.innerHTML = head ? head.innerHTML : '';
              const view = route === '#/' || route.startsWith('#/?') ? 'tonight' : route.slice(2).split(/[/?]/)[0];
              for (const link of document.querySelectorAll('.m-nav a')) {
                if (link.dataset.view === view) { link.setAttribute('aria-current', 'page'); } else { link.removeAttribute('aria-current'); }
              }
              for (const group of document.querySelectorAll('.m-group')) {
                group.classList.toggle('current', group.querySelector('a[aria-current="page"]') !== null);
              }
              const pick = screen.querySelector('label.universe-pick');
              document.getElementById('index-slot').replaceChildren(...(pick ? [pick] : []));
              closeMenu();
              const named = head ? head.getAttribute('data-title') : null;
              document.title = (named ? named + ' · ' : '') + '{{{PublishedSite.Name}}}';
              scrollTo(0, 0);
              paintTheme();
            }
            addEventListener('hashchange', show);
            // The index a screen reads, chosen in its own menu, opens that index's file of the same screen.
            document.addEventListener('change', (event) => {
              const universe = event.target.closest ? event.target.closest('select[data-universe-route]') : null;
              if (universe) { location.hash = universe.getAttribute('data-universe-route') + '?{{{Universes.Query}}}=' + encodeURIComponent(universe.value); }
            });
            document.addEventListener('mouseover', placePop);
            document.addEventListener('focusin', placePop);
            function placePop(event) {
              const cell = event.target.closest ? event.target.closest('.peers-table td.peer, .universe-table td.c-nm, th.tipped, .list-table .reason, .business, .news-why') : null;
              const pop = cell ? cell.querySelector('.peer-pop, .head-tip, .why, .says') : null;
              if (!pop) { return; }
              const box = cell.getBoundingClientRect();
              const tall = pop.offsetHeight || 150;
              pop.style.left = Math.max(8, Math.min(box.left, innerWidth - (pop.offsetWidth || 340) - 8)) + 'px';
              pop.style.top = (box.bottom + tall + 8 < innerHeight ? box.bottom + 4 : Math.max(8, box.top - tall - 4)) + 'px';
            }
            document.addEventListener('click', (event) => {
              // A count of the stocks one gate short opens their card and brings it into view.
              const close = event.target.closest('a.close-open');
              const closeCard = close ? document.getElementById('{{{EquityBrief.Web.Marks.MarkRenderer.CloseToBuyId}}}') : null;
              if (closeCard) {
                event.preventDefault();
                const details = closeCard.querySelector('details');
                if (details) { details.open = true; }
                closeCard.scrollIntoView({ block: 'start' });
                return;
              }
              // A link to a place on this screen is scrolled to below the bar rather than taken as a route.
              const jump = event.target.closest('a[href^="#"]:not([href^="#/"])');
              if (jump) {
                event.preventDefault();
                const target = document.getElementById(decodeURIComponent(jump.getAttribute('href').slice(1)));
                const mast = document.querySelector('.mast');
                if (target) { scrollTo({ top: target.getBoundingClientRect().top + scrollY - (mast ? mast.offsetHeight : 0) - 12 }); }
                return;
              }
              const paperKey = event.target.closest('.paper-key[data-line]');
              if (paperKey) {
                const shown = paperKey.getAttribute('aria-pressed') !== 'true';
                paperKey.setAttribute('aria-pressed', shown ? 'true' : 'false');
                for (const line of screen.querySelectorAll('.paper-chart path[data-account="' + CSS.escape(paperKey.getAttribute('data-line')) + '"]')) { line.style.display = shown ? '' : 'none'; }
                return;
              }
              if (event.target.closest('#theme')) {
                const next = currentTheme() === 'dark' ? 'light' : 'dark';
                document.documentElement.setAttribute('data-eb-theme', next);
                try { localStorage.setItem('eb-theme', next); } catch (error) { }
                paintTheme();
                return;
              }
              const toggle = event.target.closest('.card-toggle');
              if (toggle) { flip(toggle); return; }
              const row = event.target.closest('.list-table tr[data-ticker][data-fold]');
              if (row && !event.target.closest('a, button')) {
                const control = row.querySelector('.card-toggle[aria-controls="' + CSS.escape(row.getAttribute('data-fold')) + '"]');
                if (control) { flip(control); }
                return;
              }
              const named = event.target.closest('.m-group-btn');
              if (named) {
                const group = named.closest('.m-group');
                const open = !group.classList.contains('open');
                closeGroups(group);
                group.classList.toggle('open', open);
                named.setAttribute('aria-expanded', open ? 'true' : 'false');
                return;
              }
              if (!event.target.closest('.m-drop')) { closeGroups(null); }
              const bands = event.target.closest('[data-bands-toggle]');
              if (bands) {
                const figure = bands.closest('.card').querySelector('.chart-fig');
                const on = !figure.classList.contains('bands-on');
                figure.classList.toggle('bands-on', on);
                bands.setAttribute('aria-pressed', on ? 'true' : 'false');
                bands.textContent = on ? 'Hide the night\'s levels' : 'Show the night\'s levels';
                return;
              }
              const tile = event.target.closest('.setup-tile[data-card]');
              if (tile) {
                const family = tile.getAttribute('data-card');
                const card = family === '{{{EquityBrief.Core.Families.HeavyweightRule.Name}}}'
                  ? screen.querySelector('section.card[data-card="heavyweights"]')
                  : (screen.querySelector('section.family-card[data-family="' + CSS.escape(family) + '"]') || { closest: () => null }).closest('section.card');
                if (card) { card.scrollIntoView({ block: 'start' }); }
              }
            });
            function flip(toggle) {
              const opened = document.getElementById(toggle.getAttribute('aria-controls'));
              if (opened) {
                opened.hidden = !opened.hidden;
                toggle.setAttribute('aria-expanded', opened.hidden ? 'false' : 'true');
              }
            }
            function readCandle(candle) {
              const chart = candle.closest('.candle-chart');
              const reading = candle.closest('.card').querySelector('.chart-reading');
              if (reading) { reading.innerHTML = candle.getAttribute('data-reading'); }
              for (const label of chart.querySelectorAll('[data-legend]')) {
                const words = candle.getAttribute('data-' + label.getAttribute('data-legend'));
                if (words) { label.textContent = words; }
              }
            }
            document.addEventListener('pointerover', (event) => {
              const candle = event.target.closest && event.target.closest('.candle-chart .candle');
              if (candle) { readCandle(candle); }
            });
            document.addEventListener('pointerout', (event) => {
              const chart = event.target.closest && event.target.closest('.candle-chart');
              if (!chart || (event.relatedTarget && chart.contains(event.relatedTarget))) { return; }
              const candles = chart.querySelectorAll('.candle');
              if (candles.length > 0) { readCandle(candles[candles.length - 1]); }
            });
            function currentTheme() {
              const own = document.documentElement.getAttribute('data-eb-theme');
              if (own) { return own; }
              return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
            }
            function paintTheme() {
              document.getElementById('theme').textContent = currentTheme() === 'dark' ? 'Light palette' : 'Dark palette';
            }
            const menuButton = document.getElementById('menu-button');
            const menuPanel = document.getElementById('menu');
            function closeMenu() { menuButton.setAttribute('aria-expanded', 'false'); menuPanel.classList.remove('open'); closeGroups(null); }
            function closeGroups(kept) {
              for (const group of document.querySelectorAll('.m-group.open')) {
                if (group !== kept) { group.classList.remove('open'); group.querySelector('.m-group-btn').setAttribute('aria-expanded', 'false'); }
              }
            }
            menuButton.addEventListener('click', () => {
              const open = menuButton.getAttribute('aria-expanded') !== 'true';
              menuButton.setAttribute('aria-expanded', open ? 'true' : 'false');
              menuPanel.classList.toggle('open', open);
            });
            paintTheme();
            show();
            </script>
            </body>
            </html>
            """;
    }
}
