using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The provider's earnings trends, over the network: one request for a page of symbols at the provider's weight of one a
// symbol, measured at 21.2 against the account's own counter. The parser is `EstimateTrends.Parse`, shared.
// see: The night asks every member's estimate trend once a night in requests of a page of symbols
public sealed class EodhdEstimateTrendsFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IEstimateTrendsFeed
{
    public const string Endpoint = "calendar/trends";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Symbols { get; private set; }

    // A blank address is the provider's own, as every other feed of the provider reads one.
    public static EodhdEstimateTrendsFeed Live(
        string? baseAddress,
        ProviderCredentials credentials,
        ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;
        var address = string.IsNullOrWhiteSpace(baseAddress) ? EodhdBulkPriceFeed.DefaultBaseAddress : baseAddress;

        return new(
            new HttpClient
            {
                BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/"),
                Timeout = policy.Timeout + policy.Timeout,
            },
            credentials,
            request);
    }

    public async Task<IReadOnlyList<NightTrend>> TrendsAsync(IReadOnlyList<string> tickers, CancellationToken cancellation = default)
    {
        if (tickers.Count == 0)
        {
            return [];
        }

        Requests++;
        Symbols += tickers.Count;

        var symbols = string.Join(",", tickers.Select(ticker => ticker + EstimateTrends.ExchangeSuffix));
        var body = await request
            .SendAsync(token => FetchAsync(symbols, token), cancellation)
            .ConfigureAwait(false);

        return EstimateTrends.Parse(body);
    }

    async Task<string> FetchAsync(string symbols, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(EodhdQuery.WithKey($"{Endpoint}?symbols={symbols}&fmt=json", credentials), cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "estimate trends feed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("estimate trends feed", failure, credentials);
        }
    }
}
