using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Web.Cloudflare;

// One block of a page laid out in a browser: what it is, by its tag, id and classes or by the card it draws, and its height.
public sealed record LaidBlock(string Key, double Height);

// A page laid out in a browser: the window's width and the page's, the blocks of its body beneath the bar but the copy's
// banner, the sections of its screen that stand in no other section, and Tonight's plan rows by how each is laid out.
public sealed record LaidOut(double Width, double ScrollWidth, IReadOnlyList<LaidBlock> Blocks, IReadOnlyList<LaidBlock> Sections, IReadOnlyList<LaidBlock> Rows);

// From the operator's correction of 2026-11-26 the copy is read against the app before anything is uploaded: every link of
// every page resolving inside the copy or kept not-held with the classes it had, no page holding a section its page in the
// app does not, and Home and Tonight laid out in a browser as the app lays them out, the same blocks and sections in the
// same order, each within a few pixels of the app's height, Tonight's plan rows as rows and no page wider than its window.
// Each fault names its page.
// see: The read-only copy draws each page as the app draws it, and a link it does not hold keeps its element
public static partial class SiteCopy
{
    // How far a section's or a plan row's height may stand from the app's, in pixels.
    public const double LayoutPixels = 4;

    // The two windows Home and Tonight are laid out in: a desktop's, and a phone's at its true size and density.
    public static readonly IReadOnlyList<(string Name, int Width, int Height, double Scale, bool Mobile)> Windows =
    [
        ("1440", 1440, 900, 1, false),
        ("390", 390, 844, 3, true),
    ];

    // Each link of a page whose element or classes the copy did not keep, and each that neither opens a page the copy holds,
    // a place on the page or another site nor is kept not-held, read from the page as drawn and the file the copy wrote.
    public static IReadOnlyList<string> LinkFaults(string file, string drawn, string written, IReadOnlySet<string> held)
    {
        var was = Anchor().Matches(drawn);
        var now = Anchor().Matches(written);

        if (was.Count != now.Count)
        {
            return [FormattableString.Invariant($"{file} carries {now.Count} links where its page as drawn carries {was.Count}")];
        }

        var faults = new List<string>();

        for (var at = 0; at < was.Count; at++)
        {
            var before = was[at].Groups["attrs"].Value;
            var after = now[at].Groups["attrs"].Value;
            var named = FormattableString.Invariant($"{file}: link {at + 1}, \"{Words(now[at].Groups["text"].Value)}\",");
            var kept = ClassesOf(after);

            foreach (var lost in ClassesOf(before).Where(one => !kept.Contains(one)))
            {
                faults.Add($"{named} lost its class {lost}");
            }

            if (AttributeOf(after, "href") is not { } href)
            {
                if (AttributeOf(before, "href") is not null && !kept.Contains(NotHeldClass))
                {
                    faults.Add($"{named} lost its link and is not marked {NotHeldClass}");
                }

                continue;
            }

            var link = WebUtility.HtmlDecode(href);

            if (link.StartsWith("#/", StringComparison.Ordinal) ? !held.Contains(link) : !(link.StartsWith('#') || link.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || link.StartsWith("http://", StringComparison.OrdinalIgnoreCase)))
            {
                faults.Add($"{named} opens {link}, which the copy does not hold");
            }
        }

        return faults;
    }

    // Each section a page of the copy holds more often than its page as the app draws it, by the card it draws or its class.
    public static IReadOnlyList<string> SectionFaults(string file, string drawn, string written)
    {
        var app = SectionsOf(drawn);

        return
        [
            .. SectionsOf(written)
                .Where(section => section.Value > app.GetValueOrDefault(section.Key))
                .Select(section => $"{file} holds the section {section.Key}, which its page in the app does not hold"),
        ];
    }

    static Dictionary<string, int> SectionsOf(string html) =>
        SectionTag().Matches(html)
            .Select(section => section.Groups["attrs"].Value)
            .Select(attributes => AttributeOf(attributes, "data-card") ?? AttributeOf(attributes, "class") ?? AttributeOf(attributes, "id") ?? "section")
            .GroupBy(key => key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

    static HashSet<string> ClassesOf(string attributes) =>
        new((AttributeOf(attributes, "class") ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    static string Words(string html)
    {
        var words = Regex.Replace(WebUtility.HtmlDecode(Regex.Replace(html, "<[^>]*>", " ")), @"\s+", " ").Trim();

        return words.Length <= 40 ? words : words[..40] + "...";
    }

    [GeneratedRegex(@"<section\b(?<attrs>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex SectionTag();

    // Each way a page laid out in the copy stands apart from the same page laid out in the app, in one window.
    public static IReadOnlyList<string> LayoutFaults(string page, LaidOut app, LaidOut copy)
    {
        var faults = new List<string>();

        if (!app.Blocks.Select(block => block.Key).SequenceEqual(copy.Blocks.Select(block => block.Key), StringComparer.Ordinal))
        {
            faults.Add($"{page}: the page's blocks are {Listed(copy.Blocks)} in the copy and {Listed(app.Blocks)} in the app");
        }

        if (!app.Sections.Select(section => section.Key).SequenceEqual(copy.Sections.Select(section => section.Key), StringComparer.Ordinal))
        {
            faults.Add($"{page}: its sections are {Listed(copy.Sections)} in the copy and {Listed(app.Sections)} in the app");
        }
        else
        {
            faults.AddRange(Heights(page, "the section", app.Sections, copy.Sections));
        }

        if (app.Rows.Count != copy.Rows.Count)
        {
            faults.Add(FormattableString.Invariant($"{page}: it draws {copy.Rows.Count} plan rows in the copy and {app.Rows.Count} in the app"));
        }
        else
        {
            for (var at = 0; at < app.Rows.Count; at++)
            {
                if (app.Rows[at].Key != copy.Rows[at].Key)
                {
                    faults.Add(FormattableString.Invariant($"{page}: plan row {at + 1} is laid out as {copy.Rows[at].Key} in the copy and as {app.Rows[at].Key} in the app"));
                }
            }

            faults.AddRange(Heights(page, "plan row", app.Rows.Select((row, at) => row with { Key = (at + 1).ToString(CultureInfo.InvariantCulture) }).ToArray(), copy.Rows.Select((row, at) => row with { Key = (at + 1).ToString(CultureInfo.InvariantCulture) }).ToArray()));
        }

        if (copy.ScrollWidth > copy.Width + 1)
        {
            faults.Add(FormattableString.Invariant($"{page}: the copy's page is {copy.ScrollWidth:0} px wide in a window {copy.Width:0} px wide, so it scrolls sideways"));
        }

        return faults;
    }

    static IEnumerable<string> Heights(string page, string what, IReadOnlyList<LaidBlock> app, IReadOnlyList<LaidBlock> copy) =>
        app.Zip(copy)
            .Where(pair => Math.Abs(pair.First.Height - pair.Second.Height) > LayoutPixels)
            .Select(pair => FormattableString.Invariant($"{page}: {what} {pair.Second.Key} stands {pair.Second.Height:0} px tall in the copy and {pair.First.Height:0} px in the app"));

    static string Listed(IReadOnlyList<LaidBlock> blocks) => "[" + string.Join(", ", blocks.Select(block => block.Key)) + "]";

    // What a browser reads off a page once its screen is drawn, as a JSON object of LaidOut's fields: the blocks of the body
    // but its scripts, its bar and the copy's banner; the sections of the screen in no other section, by the card each draws,
    // its id or its classes; and Tonight's plan rows, each by how it is laid out.
    public const string LayoutScript = """
        (async () => {
          const until = Date.now() + 20000;
          const screen = document.getElementById('screen');
          while (Date.now() < until && !(screen && screen.children.length > 0)) { await new Promise((done) => setTimeout(done, 100)); }
          if (document.fonts && document.fonts.ready) { await document.fonts.ready; }
          await new Promise((done) => requestAnimationFrame(() => requestAnimationFrame(done)));
          await new Promise((done) => setTimeout(done, 300));
          const key = (one) => one.tagName.toLowerCase() + (one.id ? '#' + one.id : '') + [...one.classList].sort().map((name) => '.' + name).join('');
          const height = (one) => Math.round(one.getBoundingClientRect().height * 10) / 10;
          const blocks = [...document.body.children].filter((one) => !['SCRIPT', 'STYLE', 'HEADER'].includes(one.tagName) && !one.classList.contains('copy-banner')).map((one) => ({ Key: key(one), Height: height(one) }));
          const sections = [...screen.querySelectorAll('section')].filter((one) => !one.parentElement.closest('section')).map((one) => ({ Key: one.getAttribute('data-card') || one.id || [...one.classList].sort().join('.') || 'section', Height: height(one) }));
          const rows = [...screen.querySelectorAll('.pl-row')].map((one) => ({ Key: getComputedStyle(one).display, Height: height(one) }));
          return JSON.stringify({ Width: innerWidth, ScrollWidth: document.documentElement.scrollWidth, Blocks: blocks, Sections: sections, Rows: rows });
        })()
        """;
}
