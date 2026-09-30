using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// One page a site's search returned for an industry, as the measurement read it: whether admissibility admitted
// it, and how densely its title and text name the industry it was searched for.
public sealed record MeasuredPage(string Site, string Industry, string Url, string Title, bool Admitted, double Mentions);

// What one site's search for one industry came to, the pages kept among the results.
public sealed record MeasuredSearch(string Site, string Industry, int Results, int OffList, int Snippets, IReadOnlyList<MeasuredPage> Pages);

// The measurement a proposed sector site is held to before it joins the industry list: the product's own theme
// search, admissibility and density rule, run over the site for the declined industries of its sector, writing
// nothing to the store. A site joins where, for at least one of those industries, it returned at least one page
// admissibility admitted and the density rule reads as about that industry; a page about another industry, one
// admissibility refused, and one only mentioning the industry do not count.
// see: A theme search adds its sector's sites, and a site joins the list only where a measurement found industry material on it
public static class SourceMeasurement
{
    // The pages about a declined industry a site returns before it joins: one, since the fault the sites are added
    // for is a theme pass handed one to five pages where the two written cycles were handed about ten.
    public const int PagesToJoin = 1;

    public static bool Joins(string site, IReadOnlyList<MeasuredPage> pages, IReadOnlyList<string> industries) =>
        pages.Count(page =>
            string.Equals(page.Site, site, StringComparison.OrdinalIgnoreCase)
            && page.Admitted
            && industries.Contains(page.Industry, StringComparer.Ordinal)
            && page.Mentions >= ThemeSearch.MentionsPerTenThousand) >= PagesToJoin;

    // One site searched for one industry as a theme pass searches it, over the quarter to the day asked, what it
    // returned judged as the pass judges it.
    public static async Task<MeasuredSearch> MeasureAsync(ISearchFeed search, string site, string industry, DateOnly asOf, DateTimeOffset at, CancellationToken cancellation = default)
    {
        var answer = await search.SearchAsync(ThemeSearch.For(industry, asOf, [site]).Single(), cancellation);
        var intake = ThemeSearch.Of(answer, [site]);
        var rows = SourceDocuments.Of(intake.Fetched, asOf.AddMonths(-ThemeSearch.WindowMonths), asOf, at).Rows;

        return new MeasuredSearch(
            site,
            industry,
            answer.Results.Count,
            intake.OffList.Count,
            intake.Snippets.Count,
            [.. rows.Select(row => new MeasuredPage(site, industry, row.Url, row.Title, row.Admitted, row.Admitted ? ThemeSearch.Mentions(row, industry) : 0))]);
    }
}
