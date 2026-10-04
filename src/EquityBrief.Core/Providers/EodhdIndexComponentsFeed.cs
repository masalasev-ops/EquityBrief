using System.Net.Http;

namespace EquityBrief.Core.Providers;

// An index's components today, over the network: the index's fundamentals answer, asked once an index at the endpoint's
// weight, read by the components' own reader. The membership feed reads the same endpoint for the S&P 500's spans and
// refuses an answer carrying the components alone; this reads the components, which is all the S&P 400's and 600's
// answers carry on this key.
// see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
public sealed class EodhdIndexComponentsFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IIndexComponentsFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static EodhdIndexComponentsFeed Live(
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

    public async Task<IReadOnlyList<IndexComponent>> ComponentsAsync(string indexCode, CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(indexCode, token), cancellation)
            .ConfigureAwait(false);

        return IndexComponents.Parse(body, indexCode);
    }

    async Task<string> FetchAsync(string indexCode, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey($"{EodhdIndexMembershipFeed.Endpoint}/{indexCode}{EodhdIndexMembershipFeed.IndexSuffix}?fmt=json", credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "index components");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("index components", failure, credentials);
        }
    }
}
