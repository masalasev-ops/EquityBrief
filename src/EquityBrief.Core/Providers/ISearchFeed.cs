using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// What a search asks for: the words, the dates the results must fall between, the sites
// they may come from, and how many results come back.
//
// The four things a theme search is scoped by are each a field, so the request the feed
// made is where a test reads them rather than the prose that describes them.
// see: A theme search is scoped by parameter, not by hope
public sealed record SearchQuery(string Query, DateOnly From, DateOnly To, IReadOnlyList<string> Domains, int MostResults)
{
    // The recording's key, over the request canonicalised, for the reason a model call's
    // is: a different window, list or count is a request nobody recorded.
    public string Key =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
        {
            query = Query,
            from = From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            domains = Domains,
            mostResults = MostResults,
        }))))[..32];
}

// One result as the tool returned it. `Text` is the page's full text where the tool
// returned one and null where it returned only the snippet, and `Published` is null where
// it named no publish date, which the admissibility test refuses.
public sealed record SearchResult(string Url, string Title, string Snippet, string? Text, DateTimeOffset? Published);

public sealed record SearchAnswer(IReadOnlyList<SearchResult> Results);

// The search tool did not answer: nothing listening, or no answer inside the timeout.
//
// A type of its own, apart from a refusal, because only the feed can tell a tool that did
// not answer from one that answered with nothing, and the two are different failures: a
// search that found nothing is an answer.
// see: A feed is unavailable when it does not answer, and wrong when it answers with something else
public sealed class SearchToolUnavailable(string message) : Exception(message);

// The open web search a theme pass reads, called by a component and never by a model.
// see: The model never fetches; components fetch and hand it documents
// see: Theme material comes from a search tool, and per-name material never does
public interface ISearchFeed
{
    int Requests { get; }

    Task<SearchAnswer> SearchAsync(SearchQuery query, CancellationToken cancellation = default);
}

// The search tool, answered from recorded responses, one file per search.
//
// Keyed on the whole request, so a different window, list or count is a search nobody
// recorded and is refused by name rather than answered by the network or by another
// recording. The double holds no client. `unavailable` stands for a tool that does not
// answer, so a theme pass can be shown not to start without a network to fail.
public sealed class RecordedSearchFeed(string folder, string? unavailable = null) : ISearchFeed
{
    public const string FilePrefix = "search-";

    public int Requests { get; private set; }

    public IReadOnlyList<SearchQuery> Asked => asked;

    readonly List<SearchQuery> asked = [];

    public static string FileFor(SearchQuery query) => FilePrefix + query.Key + ".json";

    public Task<SearchAnswer> SearchAsync(SearchQuery query, CancellationToken cancellation = default)
    {
        Requests++;
        asked.Add(query);

        if (unavailable is not null)
        {
            throw new SearchToolUnavailable(unavailable);
        }

        var file = Path.Combine(folder, FileFor(query));

        return File.Exists(file)
            ? Task.FromResult(TavilySearchFeed.Parse(File.ReadAllText(file)))
            : throw new InvalidOperationException(
                $"No recording answers the search for '{query.Query}', keyed {query.Key}, in '{folder}'. The recording is " +
                "keyed on the whole request, so a window, a list or a count that changed is a search nobody recorded.");
    }
}
