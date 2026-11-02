using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Web.App;

namespace EquityBrief.Web.Cloudflare;

// One thing the read-only copy must not carry because it is the method: what it is, its text, which is no secret, whether
// it is found in any case, and whether it is read in a report's written prose, where the two phrases a threshold is stated
// in are a company's words about its business rather than a rule's.
public sealed record MethodNeedle(string Kind, string Text, bool AnyCase = false, bool InProse = true);

// From 22.17 the read-only copy keeps out of search and out of the method. Every page carries the robots meta and the copy
// a robots file refusing every crawler; a target's close the code writes is said in the copy's words; and before anything
// is uploaded every file the copy wrote is read for a route to the Loop or the Ledger, the name of a rule, version or
// variant the register holds, a live rule's setting in its own registration's words, a version's label, a settings line,
// and the two phrases a threshold is stated in outside a report's written prose. A leak names its file and the text.
// see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
public static partial class SiteCopy
{
    public const string NoIndex = "<meta name=\"robots\" content=\"noindex, nofollow\">";

    public const string RobotsFile = "robots.txt";

    public const string Robots = "User-agent: *\nDisallow: /\n";

    // The needles: the Loop's and the Ledger's routes, a version's label, a settings line and the two threshold phrases,
    // each name the register holds standing, and each setting a live rule's registration states.
    public static IReadOnlyList<MethodNeedle> MethodNeedles(IEnumerable<string> standing, IEnumerable<string> live) =>
    [
        new("a Loop route", SinglePageApp.LoopRoute),
        new("a Ledger route", SinglePageApp.LedgerRoute),
        new("a version's label", "Version: "),
        new("a settings line", "settings: ", AnyCase: true),
        new("a threshold's words", "floor of", AnyCase: true, InProse: false),
        new("a threshold's words", "at or above", AnyCase: true, InProse: false),
        .. standing.Distinct(StringComparer.Ordinal).Select(name => new MethodNeedle("a rule, version or variant name", name)),
        .. live.SelectMany(SettingWords).Distinct(StringComparer.Ordinal).Select(words => new MethodNeedle("a live rule's setting words", words)),
    ];

    // The settings a registration states in its own words: each part between its commas carrying a figure and three words or
    // more, so a part as short as a count of sessions, which plain prose carries, is not read as one.
    public static IEnumerable<string> SettingWords(string registration) =>
        registration.Split(", ")
            .Select(part => part.Trim())
            .Where(part => part.Any(char.IsAsciiDigit) && part.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 3);

    // Every leak in one file, each needle its text carries as a reader reads it, its entities decoded; a needle read outside
    // prose is read with every report's written section taken out first.
    public static IReadOnlyList<string> Leaks(string file, string text, IReadOnlyList<MethodNeedle> needles)
    {
        var read = WebUtility.HtmlDecode(text);
        var outside = WebUtility.HtmlDecode(Removed(text, "section", start => HasClass(start, WrittenSection)));

        return
        [
            .. needles
                .Where(needle => (needle.InProse ? read : outside).Contains(needle.Text, needle.AnyCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                .Select(needle => $"{file} carries {needle.Kind}: \"{needle.Text}\""),
        ];
    }

    // The class a report's written section is drawn in, its prose the model's under the claim checker.
    public const string WrittenSection = "written-section";

    // A screen with the target's close the code writes said in the copy's words: "a close at or above 22.13" as "a close at
    // 22.13 or higher", and the words list's "at or above it" as "at it or higher"; a report's written prose is left as the
    // claim checker accepted it.
    public static string Worded(string fragment)
    {
        var kept = new List<string>();
        var marked = Removed(fragment, "section", start => HasClass(start, WrittenSection), section =>
        {
            kept.Add(section);

            return "\u0001" + (kept.Count - 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\u0001";
        });
        var worded = AtOrAbove().Replace(marked, "at ${what} or higher").Replace("The price a close at or above ends it at a gain", "The price at which a close ends it at a gain", StringComparison.Ordinal);

        return Regex.Replace(worded, "\u0001(?<at>[0-9]+)\u0001", held => kept[int.Parse(held.Groups["at"].Value, System.Globalization.CultureInfo.InvariantCulture)]);
    }

    [GeneratedRegex(@"at or above (?<what>it\b|the target of [0-9][0-9,.]*[0-9]|\$?[0-9][0-9,]*(?:\.[0-9]+)?)")]
    private static partial Regex AtOrAbove();
}
