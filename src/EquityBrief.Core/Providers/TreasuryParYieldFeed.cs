using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The Treasury's daily par yield curve, over the network: one request for a calendar year's table, keyless and free, at
// the Treasury's own site rather than the provider's, so it counts against no allowance. The parser is
// `TreasuryYields.Parse`, shared.
// see: The Treasury's 10-year par yield is read once a night after the close and kept a session a row
public sealed class TreasuryParYieldFeed(HttpClient client, ProviderRequest? request = null) : ITreasuryYieldFeed
{
    public const string BaseAddress = "https://home.treasury.gov/";

    public const string Endpoint = "resource-center/data-chart-center/interest-rates/daily-treasury-rates.csv";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static TreasuryParYieldFeed Live(ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;

        return new(
            new HttpClient
            {
                BaseAddress = new Uri(BaseAddress),
                Timeout = policy.Timeout + policy.Timeout,
            },
            request);
    }

    public async Task<IReadOnlyList<TreasuryYield>> YearAsync(int year, CancellationToken cancellation = default)
    {
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(year, token), cancellation)
            .ConfigureAwait(false);

        return TreasuryYields.Parse(body);
    }

    async Task<string> FetchAsync(int year, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    FormattableString.Invariant($"{Endpoint}/{year}/all?type=daily_treasury_yield_curve&field_tdr_date_value={year}&page&_format=csv"),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw new ProviderRefusal(
                    FormattableString.Invariant($"The Treasury's par yield table answered {(int)response.StatusCode} for {year}."),
                    transient: (int)response.StatusCode >= 500);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (HttpRequestException failure)
        {
            throw new ProviderRefusal("The Treasury's par yield table could not be reached: " + failure.Message, transient: true);
        }
    }
}
