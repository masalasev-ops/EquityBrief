using EquityBrief.Core.Providers;

namespace EquityBrief.Core.Research;

// One search a form made, as the tool measurement keeps it: the sites it asked for, and what came back or the refusal.
public sealed record MeasuredToolSearch(IReadOnlyList<string> Sites, SearchAnswer? Answer, string? Refused);

// One form of searching one industry on one tool, as 21.19's measurement counts it. The searches, the refusals and the
// searches returning nothing count searches; the pages from the site asked, from other sites, with text and with a
// publish date count every result the searches returned; the pages admitted and those about the industry count the
// distinct pages a theme pass would keep of them, taken as a pass takes them and tested by admissibility.
public sealed record ToolFormCount(
    string Tool,
    string Form,
    string Industry,
    int Searches,
    int Refused,
    int Empty,
    int FromSiteAsked,
    int FromOtherSites,
    int WithText,
    int WithDate,
    int Admitted,
    int AboutTheIndustry);

// The measurement a search tool is held to before it answers the theme search: the queries of one form searched on one
// tool for one industry, each result counted, and what a theme pass would admit of them judged as the pass judges it,
// writing nothing to the store.
// see: Theme material comes from a search tool, and per-name material never does
public static class ToolMeasurement
{
    public static async Task<(ToolFormCount Count, IReadOnlyList<MeasuredToolSearch> Searches)> MeasureAsync(
        ISearchFeed feed,
        string tool,
        string form,
        string industry,
        IReadOnlyList<SearchQuery> queries,
        IReadOnlyList<string> industryList,
        DateOnly asOf,
        DateTimeOffset at,
        CancellationToken cancellation = default)
    {
        var searches = new List<MeasuredToolSearch>();

        // A refusal is counted and the form goes on, so a form the tool refuses is measured as refused rather than
        // stopping the measurement of the others.
        foreach (var query in queries)
        {
            try
            {
                searches.Add(new MeasuredToolSearch(query.Domains, await feed.SearchAsync(query, cancellation), null));
            }
            catch (Exception refused) when (refused is ProviderRefusal or SearchToolUnavailable)
            {
                searches.Add(new MeasuredToolSearch(query.Domains, null, refused.Message));
            }
        }

        return (Count(tool, form, industry, searches, industryList, asOf, at), searches);
    }

    public static ToolFormCount Count(
        string tool,
        string form,
        string industry,
        IReadOnlyList<MeasuredToolSearch> searches,
        IReadOnlyList<string> industryList,
        DateOnly asOf,
        DateTimeOffset at)
    {
        var answered = searches.Where(search => search.Answer is not null).ToArray();
        var results = answered.SelectMany(search => search.Answer!.Results.Select(result => (search.Sites, Result: result))).ToArray();
        var intake = ThemeSearch.Of(ThemeSearch.Merged([.. answered.Select(search => search.Answer!)]), industryList);
        var admitted = SourceDocuments.Of(intake.Fetched, asOf.AddMonths(-ThemeSearch.WindowMonths), asOf, at).Rows
            .Where(row => row.Admitted)
            .ToArray();

        return new ToolFormCount(
            tool,
            form,
            industry,
            searches.Count,
            searches.Count - answered.Length,
            answered.Count(search => search.Answer!.Results.Count == 0),
            results.Count(page => ThemeSearch.OnList(page.Result.Url, page.Sites)),
            results.Count(page => !ThemeSearch.OnList(page.Result.Url, page.Sites)),
            results.Count(page => !ThemeSearch.ShortOfADocument(page.Result)),
            results.Count(page => page.Result.Published is not null),
            admitted.Length,
            admitted.Count(row => ThemeSearch.AboutTheIndustry(row, industry)));
    }
}
