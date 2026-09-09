using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The news feed, over the network.
//
// One dated request for the whole market rather than one per name. The endpoint
// takes a ticker and this feed does not send one, which is the whole of the
// zero-per-name rule applied here: the payload names the tickers each article is
// about, so five hundred names are reached by reading an attribution rather than
// by making five hundred requests
// (see: News arrives in one dated feed request and is attributed to names locally).
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

    public async Task<IReadOnlyList<NewsArticle>> ArticlesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(from, to, token), cancellation)
            .ConfigureAwait(false);

        // Parsed by the reader the double uses, then filtered to the window.
        // The provider honours the range, so the filter is a guard rather than
        // the mechanism: an article outside it would otherwise be counted
        // against a night it does not belong to.
        return
        [
            .. RecordedNewsFeed.Parse(body)
                .Where(article => DateOnly.FromDateTime(article.Published.UtcDateTime) >= from
                    && DateOnly.FromDateTime(article.Published.UtcDateTime) <= to),
        ];
    }

    async Task<string> FetchAsync(DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        $"{Endpoint}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&limit={Limit}&fmt=json",
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
