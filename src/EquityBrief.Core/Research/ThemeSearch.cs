using System.Text.Json;
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

// The search a theme pass makes, and what it keeps of the answer.
//
// The query names the industry and never a ticker, carries the window's two dates, is
// restricted to the industry list, and asks for the page's text, which the feed sends
// with every request (see: A theme search is scoped by parameter, not by hope).
public static class ThemeSearch
{
    // A quarter back from the day of the pass. An industry's prices are read for where they
    // are now, and a year-old outlook carrying a previous year's pricing is one of the
    // results the decision above was written against.
    public const int WindowMonths = 3;

    // The results a search returns. The captures asked for ten, and ten over a quarter
    // held nine pages from the list with their text for the industry the list covers.
    public const int MostResults = 10;

    public static SearchQuery For(string industry, DateOnly asOf, IReadOnlyList<string> industryList) =>
        new(industry + " industry", asOf.AddMonths(-WindowMonths), asOf, industryList, MostResults);

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
