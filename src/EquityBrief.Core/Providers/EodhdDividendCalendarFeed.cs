using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The dividend calendar, over the network: one request a session for every market's listings going ex-dividend on it,
// a further page asked only where the answer's total passes the page's limit, each page counted. 16.0's 21 answers held
// at most 366 rows a session against a limit of 5,000, so a session costs one request.
// see: A feed that pages counts every page, and a page count that grows with the index is a per-name call
// see: The night asks the dividend calendar for each of the next 21 sessions, one request a session
//
// The parser is `RecordedDividendCalendarFeed.Parse`, shared, written against those captured answers.
public sealed class EodhdDividendCalendarFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IDividendCalendarFeed
{
    public const string Endpoint = "calendar/dividends";

    // The most rows the endpoint answers on one page.
    public const int PageLimit = 5000;

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static EodhdDividendCalendarFeed Live(
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

    public async Task<IReadOnlyList<ExDividend>> ExDividendsAsync(DateOnly session, CancellationToken cancellation = default)
    {
        var rows = new List<ExDividend>();
        var offset = 0;

        while (true)
        {
            Requests++;

            var at = offset;
            var body = await request
                .SendAsync(token => FetchAsync(session, at, token), cancellation)
                .ConfigureAwait(false);
            var (page, total) = RecordedDividendCalendarFeed.Parse(body, session);

            rows.AddRange(page);
            offset += PageLimit;

            if (offset >= total)
            {
                return rows;
            }
        }
    }

    async Task<string> FetchAsync(DateOnly session, int offset, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        FormattableString.Invariant($"{Endpoint}?filter[date_eq]={session:yyyy-MM-dd}&page[limit]={PageLimit}&page[offset]={offset}&fmt=json"),
                        credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "dividend calendar feed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("dividend calendar feed", failure, credentials);
        }
    }
}
