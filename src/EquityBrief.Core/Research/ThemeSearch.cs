using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// The two open-web lists, read from the file at the checkout's root that carries them with
// their review date.
// see: Two source lists govern the open web, and the licensed feed is a channel rather than a list
public sealed record SourceLists(IReadOnlyList<string> CompanyNews, IReadOnlyList<string> Industry)
{
    public const string FileName = "source-lists.json";

    public static SourceLists Read(string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file));

        return new SourceLists(Sites(document.RootElement, "companyNews", file), Sites(document.RootElement, "industry", file));
    }

    static IReadOnlyList<string> Sites(JsonElement root, string list, string file)
    {
        var sites = root.TryGetProperty(list, out var named) && named.TryGetProperty("sites", out var held) && held.ValueKind == JsonValueKind.Array
            ? held.EnumerateArray().Select(site => site.GetString() ?? string.Empty).ToArray()
            : [];

        // An empty list would restrict a search to nothing and read as a search that found
        // nothing, so a file without one is refused by name.
        return sites.Length > 0 && sites.All(site => site.Length > 0)
            ? sites
            : throw new InvalidOperationException(
                $"'{file}' carries no '{list}' list of sites. A search restricted to no site returns nothing, and that " +
                "would read as an industry nobody writes about rather than as a list that is missing.");
    }
}

// What a theme search's answer comes to before anything is stored: the documents that can
// be tested, the results dropped for coming from a site the list does not carry, and the
// results dropped for being short of a document.
public sealed record ThemeIntake(IReadOnlyList<FetchedDocument> Fetched, IReadOnlyList<string> OffList, IReadOnlyList<string> Snippets);

// The searches a theme pass makes, what it keeps of the answers, and what of that it hands
// the model.
//
// Each query names the industry and never a ticker, carries the window's two dates, is
// restricted to one site of the industry list, and asks for the page's text, which the feed
// sends with every request (see: A theme search is scoped by parameter, not by hope). One
// search a site rather than one over the list, because 6.11 measured the tool answering a
// restriction to more than one site with nothing, with one site's pages, or with pages from
// outside the restriction, and a search restricted to one site returning what that site holds.
// see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return
public static class ThemeSearch
{
    // A quarter back from the day of the pass. An industry's prices are read for where they
    // are now, and a year-old outlook carrying a previous year's pricing is one of the
    // results the decision above was written against.
    public const int WindowMonths = 3;

    // The results each site's search asks for: the fewest at which each of the four
    // industries 6.11 measured had ten pages to hand.
    public const int ResultsASite = 3;

    // The most pages a theme call is handed, which is what the one search over the list
    // returned when it returned anything, and a call over nine of them cost $0.0016 at 6.9.
    public const int MostPages = 10;

    // The characters of each page a theme call carries. It holds 135 of the 172 pages with
    // text 6.11's searches returned whole, the median being 9,785, while a government release
    // of a million and a half characters is carried as its opening, so ten pages are at most
    // 300,000 characters.
    public const int CharactersAPage = 30_000;

    public static IReadOnlyList<SearchQuery> For(string industry, DateOnly asOf, IReadOnlyList<string> industryList) =>
        [.. industryList.Select(site => new SearchQuery(industry + " industry", asOf.AddMonths(-WindowMonths), asOf, [site], ResultsASite))];

    // The answers a pass's searches returned, taken a rank at a time in the list's order:
    // every site's first result before any site's second, so a site whose pages are about
    // something else does not crowd out the rest, and a page two searches returned is kept
    // once.
    public static SearchAnswer Merged(IReadOnlyList<SearchAnswer> answers)
    {
        var merged = new List<SearchResult>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deepest = answers.Count == 0 ? 0 : answers.Max(answer => answer.Results.Count);

        for (var rank = 0; rank < deepest; rank++)
        {
            foreach (var answer in answers)
            {
                if (rank < answer.Results.Count && seen.Add(answer.Results[rank].Url))
                {
                    merged.Add(answer.Results[rank]);
                }
            }
        }

        return new SearchAnswer(merged);
    }

    // How densely a page names its industry before a theme call is handed it: mentions of the
    // industry's words in every ten thousand characters of its title and text.
    // see: A theme page is handed to the model only where its text names the industry
    public const int MentionsPerTenThousand = 5;

    // The characters those mentions are counted over.
    public const int CountedOver = 10_000;

    // The words an industry's name uses to qualify an industry rather than name one, left out of
    // what a page is counted for.
    static readonly string[] Qualifiers =
        ["general", "diversified", "specialty", "regional", "other", "services", "products", "equipment", "and", "the", "of"];

    // The words a page is counted for: the industry's own, lowercased, less its separators and
    // qualifiers, each with a plural it may take.
    public static IReadOnlyList<string> IndustryWords(string industry) =>
    [
        .. industry.ToLowerInvariant()
            .Split([' ', '-', '&', ',', '/'], StringSplitOptions.RemoveEmptyEntries)
            .Where(word => word.Length > 2 && !Qualifiers.Contains(word, StringComparer.Ordinal))
            .Select(word => word.EndsWith("ies", StringComparison.Ordinal) ? word[..^3] + "y"
                : word.EndsWith('s') && word.Length > 3 ? word[..^1]
                : word)
            .Distinct(StringComparer.Ordinal),
    ];

    // Mentions of the industry's words in every ten thousand characters of a page's title and text.
    public static double Mentions(StoredDocument document, string industry)
    {
        var text = document.Title + " " + document.Body;
        var found = IndustryWords(industry).Sum(word => Regex.Matches(
            text,
            @"\b" + (word.EndsWith('y') ? Regex.Escape(word[..^1]) + "(?:y|ies)" : Regex.Escape(word) + "(?:s|es)?") + @"\b",
            RegexOptions.IgnoreCase).Count);

        return 1.0 * found * CountedOver / Math.Max(text.Length, 1);
    }

    // Whether a page is about its industry by that count.
    public static bool AboutTheIndustry(StoredDocument document, string industry) =>
        Mentions(document, industry) >= MentionsPerTenThousand;

    // What a theme call is handed: the admitted pages in the order the pass kept them, at most
    // ten, each carrying its opening characters. The stored document keeps its whole text,
    // which is what a reader following the citation opens.
    public static IReadOnlyList<PromptDocument> Handed(IReadOnlyList<StoredDocument> admitted) =>
    [
        .. admitted
            .Take(MostPages)
            .Select(document => new PromptDocument(document.Id, document.Title, document.PublishedOn, Opening(document.Body!))),
    ];

    // A page's first characters, cut short of a character the cut would split in two.
    public static string Opening(string text)
    {
        if (text.Length <= CharactersAPage)
        {
            return text;
        }

        var cut = char.IsHighSurrogate(text[CharactersAPage - 1]) ? CharactersAPage - 1 : CharactersAPage;

        return text[..cut];
    }

    // Whether a result's site is one the list carries: the host is a listed site, or a
    // subdomain of one, so `press.spglobal.com` is S&P Global's and `market.us` is nobody's.
    public static bool OnList(string url, IReadOnlyList<string> sites) =>
        Uri.TryCreate(url, UriKind.Absolute, out var address)
        && sites.Any(site =>
            string.Equals(address.Host, site, StringComparison.OrdinalIgnoreCase)
            || address.Host.EndsWith("." + site, StringComparison.OrdinalIgnoreCase));

    // A result is short of a document where the tool returned no text for it, or text no
    // longer than its own snippet. The second is measured rather than supposed: two of the
    // results the searches run at 6.9 returned came back with 635 and 612 characters of text
    // against snippets of 641 and 635, which is a page whose body the tool could not read.
    public static bool ShortOfADocument(SearchResult result) =>
        result.Text is null || result.Text.Length <= result.Snippet.Length;

    // The list decides which sites a search may return and admissibility decides whether a
    // page may be believed, so an off-list result is dropped here, before its text is read,
    // at the cheaper of the two gates. A result the tool returned from outside the list is
    // not hypothetical: a company search restricted to thirteen sites returned seven of its ten
    // from sites outside them.
    // see: A list of publishers is a noise filter, never a correctness test
    public static ThemeIntake Of(SearchAnswer answer, IReadOnlyList<string> sites)
    {
        var fetched = new List<FetchedDocument>();
        var offList = new List<string>();
        var snippets = new List<string>();

        foreach (var result in answer.Results)
        {
            if (!OnList(result.Url, sites))
            {
                offList.Add(Uri.TryCreate(result.Url, UriKind.Absolute, out var address) ? address.Host : result.Url);
            }
            else if (ShortOfADocument(result))
            {
                snippets.Add(result.Url);
            }
            else
            {
                fetched.Add(new FetchedDocument(
                    DocumentChannel.SearchTool,
                    result.Url,
                    result.Title,
                    result.Published is { } published ? DateOnly.FromDateTime(published.UtcDateTime) : null,
                    result.Text));
            }
        }

        return new ThemeIntake(fetched, offList, snippets);
    }
}
