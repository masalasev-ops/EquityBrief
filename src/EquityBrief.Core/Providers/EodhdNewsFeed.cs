using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The news feed, over the network.
//
// One dated request for the whole market rather than one per name. The endpoint
// takes a ticker and this feed does not send one, which is the whole of the
// zero-per-name rule applied here: the payload names the tickers each article is
// about, so five hundred names are reached by reading an attribution rather than
// by making five hundred requests
// (see: News is one dated query, paged to cover the day, and attributed to names locally).
//
// The 1.7 capture was taken with a ticker, because the measurement it fed was
// about coverage per name. That query is in the fixture's manifest and is not
// the query a night makes, which is a difference worth knowing when the two are
// read side by side.
public sealed class EodhdNewsFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : INewsFeed
{
    public const string Endpoint = "news";

    // The provider's own ceiling on one request. Asked for in full, because a
    // window returning more articles than one request can carry is a fact the
    // caller has to be able to see: the count comes back at the limit and the
    // window was too wide, rather than the feed quietly paging and turning one
    // request into ten.
    public const int Limit = 1000;

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    public static EodhdNewsFeed Live(
        string baseAddress,
        ProviderCredentials credentials,
        ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;

        return new(
            new HttpClient
            {
                BaseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/"),
                Timeout = policy.Timeout + policy.Timeout,
            },
            credentials,
            request);
    }

    // One dated query, paged until the day is covered.
    //
    // 2.5 measured one request for a single session coming back at exactly 1,000
    // articles, which is the provider's cap, over 3,232 distinct symbols. So one
    // request does not carry a day, and a count taken from it is a count over
    // whatever the cap happened to include, which is a figure over a mixed
    // population and is not stated at all.
    // see: News is one dated query, paged to cover the day, and attributed to names locally
    //
    // Every page is counted, and the page count follows the day's news volume
    // rather than the size of the universe: one request already reached 3,232
    // symbols against an index of 503, so adding names adds no articles. That is
    // what keeps a paged query something other than a per-name call.
    //
    // A day that reaches the maximum refuses rather than truncating, because a
    // truncated count reported as a whole one is the failure this exists to
    // prevent.
    public const int MostPages = 20;

    public async Task<IReadOnlyList<NewsArticle>> ArticlesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default)
    {
        var articles = new List<NewsArticle>();
        var offset = 0;

        for (var page = 0; page < MostPages; page++)
        {
            Requests++;

            var at = offset;
            var body = await request
                .SendAsync(token => FetchAsync(from, to, at, token), cancellation)
                .ConfigureAwait(false);

            // Parsed by the reader the double uses, then filtered to the window.
            // The provider honours the range, so the filter is a guard rather
            // than the mechanism: an article outside it would otherwise be
            // counted against a night it does not belong to.
            var parsed = RecordedNewsFeed.Parse(body);

            articles.AddRange(parsed
                .Where(article => DateOnly.FromDateTime(article.Published.UtcDateTime) >= from
                    && DateOnly.FromDateTime(article.Published.UtcDateTime) <= to));

            // A page short of the cap is the last page. A page at the cap means
            // there is more, so the next one is asked for.
            if (parsed.Count < Limit)
            {
                return articles;
            }

            offset += parsed.Count;
        }

        throw new InvalidOperationException(
            $"The news query for {from:yyyy-MM-dd} to {to:yyyy-MM-dd} reached {MostPages} pages of " +
            $"{Limit} and the provider still had more. A count taken from a truncated day is a count " +
            "over whatever the pages happened to include, so the night refuses rather than storing one.");
    }

    async Task<string> FetchAsync(DateOnly from, DateOnly to, int offset, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        $"{Endpoint}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&limit={Limit}&offset={offset}&fmt=json",
                        credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "news");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("news", failure, credentials);
        }
    }
}
