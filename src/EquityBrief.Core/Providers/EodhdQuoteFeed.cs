using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The delayed quote, over the network: one request a listing, at the provider's weight of one. The provider states the
// quote is delayed by 15 to 20 minutes, and its answer carries the instant it is as of, which is what the page states.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
//
// The parser is `ProviderQuotes.Parse`, shared.
public sealed class EodhdQuoteFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IQuoteFeed
{
    public const string Endpoint = "real-time";

    public const string ExchangeSuffix = ".US";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static EodhdQuoteFeed Live(
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

    public async Task<ProviderQuote?> QuoteAsync(string ticker, CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(ticker, token), cancellation)
            .ConfigureAwait(false);

        return ProviderQuotes.Parse(body);
    }

    async Task<string> FetchAsync(string ticker, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(EodhdQuery.WithKey($"{Endpoint}/{ticker}{ExchangeSuffix}?fmt=json", credentials), cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "quote feed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("quote feed", failure, credentials);
        }
    }
}
