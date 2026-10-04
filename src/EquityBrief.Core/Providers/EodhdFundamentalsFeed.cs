using System.Net.Http;

namespace EquityBrief.Core.Providers;

// One name's fundamentals, over the network.
//
// One request per name, on demand, and never on the nightly path. The night's
// per-name request count is zero and this endpoint is the reason the fundamentals
// fetcher runs when a name is opened rather than when the night runs: at a weight
// of 10 a name, the whole index would cost 5,030 weighted calls a night against
// an allowance of 100,000, and it would buy nothing, since a name nobody opens
// needs no numbers section.
// see: The nightly run is arithmetic only
// see: Everything expensive happens when a name is opened
//
// The parser is `RecordedFundamentalsFeed.Parse`, shared, and it was written
// against four captured responses at 6.1 before this existed.
//
// The history pull asks the same endpoint for a company's classification and
// counts with a filter, one request a name at the same weight, which is read by
// `CompanyAnswers.Parse` because a filtered answer is shaped as no other is.
public sealed class EodhdFundamentalsFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IFundamentalsFeed, ICompanyFeed
{
    public const string Endpoint = "fundamentals";

    public const string ExchangeSuffix = ".US";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    public static EodhdFundamentalsFeed Live(
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

    public async Task<CompanyFundamentals> FundamentalsAsync(
        string ticker,
        CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync($"{Endpoint}/{ticker}{ExchangeSuffix}?fmt=json", "company fundamentals", token), cancellation)
            .ConfigureAwait(false);

        return RecordedFundamentalsFeed.Parse(body, ticker);
    }

    public async Task<CompanyAnswer> CompanyAsync(
        string ticker,
        CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync($"{Endpoint}/{ticker}{ExchangeSuffix}?filter={string.Join(',', CompanyAnswers.Filter)}", "company classification", token), cancellation)
            .ConfigureAwait(false);

        return CompanyAnswers.Parse(body, ticker);
    }

    async Task<string> FetchAsync(string path, string what, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(EodhdQuery.WithKey(path, credentials), cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, what);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable(what, failure, credentials);
        }
    }
}
