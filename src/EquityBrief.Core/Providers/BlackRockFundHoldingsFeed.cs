using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The S&P 400's and 600's funds' holdings files over the network, one request a fund, from iShares' own site, which
// asks for no key and weighs nothing on the provider's allowance. A file is tried as every feed's request is.
// see: The S&P 400's and 600's members are read each night from their funds' own holdings files
public sealed class BlackRockFundHoldingsFeed(HttpClient client, ProviderRequest? request = null) : IFundHoldingsFeed
{
    public const string DefaultBaseAddress = "https://www.ishares.com/";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public IReadOnlyList<string> Indices => FundHoldings.Indices;

    public static BlackRockFundHoldingsFeed Live(string? baseAddress = null, ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;
        var address = string.IsNullOrWhiteSpace(baseAddress) ? DefaultBaseAddress : baseAddress;

        return new(
            new HttpClient
            {
                BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/"),
                Timeout = policy.Timeout + policy.Timeout,
            },
            request);
    }

    public async Task<FundHoldingsFile> HoldingsAsync(string indexCode, CancellationToken cancellation = default)
    {
        if (!FundHoldings.Funds.TryGetValue(indexCode, out var fund))
        {
            throw new ProviderRefusal($"No fund's holdings file is read for the {indexCode} index.", transient: false);
        }

        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(fund.Fund, fund.Path, token), cancellation)
            .ConfigureAwait(false);

        return FundHoldings.Parse(body, fund.Fund);
    }

    async Task<string> FetchAsync(string fund, string path, CancellationToken cancellation)
    {
        try
        {
            using var response = await client.GetAsync(path, cancellation).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw new ProviderRefusal(
                    $"The {fund} holdings file answered {(int)response.StatusCode}.",
                    RetryPolicy.Transient((int)response.StatusCode));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw new ProviderRefusal($"The {fund} holdings file could not be reached. {failure.GetType().Name}: {failure.Message}", transient: true);
        }
    }
}
