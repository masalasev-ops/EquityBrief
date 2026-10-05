using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The provider's symbol lists for the US exchanges, over the network: the listed and, asked with the flag, the delisted,
// one request a list at a weight of one, read by `ProviderSymbols`. Asked by the history pull alone, never on a night.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
public sealed class EodhdSymbolListFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : ISymbolListFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static EodhdSymbolListFeed Live(
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

    public async Task<IReadOnlyList<ListedSymbol>> SymbolsAsync(bool delisted, CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(delisted, token), cancellation)
            .ConfigureAwait(false);

        return ProviderSymbols.Parse(body, delisted);
    }

    async Task<string> FetchAsync(bool delisted, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey($"exchange-symbol-list/{ProviderSymbols.Exchange}?fmt=json{(delisted ? "&delisted=1" : string.Empty)}", credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "symbol list");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("symbol list", failure, credentials);
        }
    }
}
