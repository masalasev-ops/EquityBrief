using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The index's and the VIX's daily series, over the network.
//
// The provider files both under its index exchange rather than under a listing, so the suffix is the
// index one and never the stock one: a series asked for as a stock is a ticker the provider does not
// list, refused rather than answered with the index. The endpoint is the historical one the backfill
// asks, one request a series over the whole span at its weight of one.
// see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
public sealed class EodhdMarketSeriesFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IMarketSeriesFeed
{
    // The exchange the provider files an index under.
    public const string IndexSuffix = ".INDX";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static EodhdMarketSeriesFeed Live(
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

    public async Task<IReadOnlyList<ProviderBar>> SeriesAsync(
        string series,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(series, from, to, token), cancellationToken)
            .ConfigureAwait(false);

        // Read by the reader the bar feeds use, the index's adjusted close being its close, and held to
        // the window asked for.
        return
        [
            .. RecordedHistoricalBarFeed.Parse(body, series)
                .Where(bar => bar.SessionDate >= from && bar.SessionDate <= to)
                .OrderBy(bar => bar.SessionDate),
        ];
    }

    async Task<string> FetchAsync(string series, DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        FormattableString.Invariant($"{EodhdHistoricalBarFeed.Endpoint}/{series}{IndexSuffix}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}") +
                        "&period=d&fmt=json",
                        credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, $"market series feed for {series}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable($"market series feed for {series}", failure, credentials);
        }
    }
}
