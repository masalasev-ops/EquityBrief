using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The index membership feed, over the network.
//
// The whole index in one call rather than one call per name, which is what the
// zero-per-name rule requires of everything on the nightly path
// (see: The nightly run is arithmetic only).
//
// The parser is `RecordedIndexMembershipFeed.Parse`, shared rather than written
// again, and it is the one 1.2 rewrote after the first version read a field the
// provider does not send. Sharing it is what makes the double and the provider
// the same reader: the spans are in HistoricalTickerComponents and not in
// Components, and a second parser would be a second chance to get that wrong.
public sealed class EodhdIndexMembershipFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IIndexMembershipFeed
{
    // The constituents come through the fundamentals endpoint, which is what
    // RUNBOOK's weight table says and why they cost ten rather than one.
    public const string Endpoint = "fundamentals";

    public const string IndexSuffix = ".INDX";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    public static EodhdIndexMembershipFeed Live(
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

    public async Task<IReadOnlyList<IndexConstituent>> ConstituentsAsync(
        string indexCode,
        CancellationToken cancellationToken = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(indexCode, token), cancellationToken)
            .ConfigureAwait(false);

        return RecordedIndexMembershipFeed.Parse(body, indexCode);
    }

    async Task<string> FetchAsync(string indexCode, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey($"{Endpoint}/{indexCode}{IndexSuffix}?fmt=json", credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "index membership");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("index membership", failure, credentials);
        }
    }
}
