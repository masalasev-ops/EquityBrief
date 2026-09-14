using System.Net.Http;

namespace EquityBrief.Core.Providers;

// One name's own news, over the network.
//
// The same endpoint as the night's news, asked for one name over a window, and only
// when that name is opened. At a weight of 5 a page, the whole index read this way
// would cost 2,515 weighted calls a night for articles the night already reads from one
// dated query, which is why this is not reachable from the night's feeds.
// see: The nightly run is arithmetic only
// see: Everything expensive happens when a name is opened
//
// Paged the way the night's query is, because one name's year can pass the provider's
// cap on one request: 6.8 measured KEYS's stored year at 656 articles in one page, and
// a larger company's year is larger.
public sealed class EodhdNameNewsFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : INameNewsFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    public static EodhdNameNewsFeed Live(
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
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default)
    {
        var articles = new List<NewsArticle>();
        var offset = 0;

        for (var page = 0; page < EodhdNewsFeed.MostPages; page++)
        {
            Requests++;

            var at = offset;
            var body = await request
                .SendAsync(token => FetchAsync(ticker, from, to, at, token), cancellation)
                .ConfigureAwait(false);

            var parsed = RecordedNewsFeed.Parse(body);

            articles.AddRange(parsed
                .Where(article => DateOnly.FromDateTime(article.Published.UtcDateTime) >= from
                    && DateOnly.FromDateTime(article.Published.UtcDateTime) <= to));

            if (parsed.Count < EodhdNewsFeed.Limit)
            {
                return articles;
            }

            offset += parsed.Count;
        }

        // A refusal of the window rather than an error, so a pass records it as unread on its row and
        // writes from the windows it could read. It was an error until 6.11, and a pass for MSFT over
        // the stored year stopped on it with nothing on its row.
        throw new ProviderRefusal(
            FormattableString.Invariant($"The news query for {ticker} over {from:yyyy-MM-dd} to {to:yyyy-MM-dd} reached {EodhdNewsFeed.MostPages} pages of ") +
            $"{EodhdNewsFeed.Limit} and the provider still had more. A pass that read a truncated window would rest its sections " +
            "on whatever the pages happened to include, so the window is refused rather than read.",
            transient: false);
    }

    async Task<string> FetchAsync(string ticker, DateOnly from, DateOnly to, int offset, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        FormattableString.Invariant($"{EodhdNewsFeed.Endpoint}?s={ticker}{EodhdFundamentalsFeed.ExchangeSuffix}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&limit={EodhdNewsFeed.Limit}&offset={offset}&fmt=json"),
                        credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "name news");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("name news", failure, credentials);
        }
    }
}
