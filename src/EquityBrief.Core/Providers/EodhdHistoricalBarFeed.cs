using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The per-ticker historical endpoint, over the network.
//
// One request per name, once, and never again for a name that already holds its
// year. That is the opposite shape from the nightly fetch on purpose: the bulk
// endpoint is priced per request at a weight of a hundred and this one at one,
// so a year of past sessions costs 500 calls this way and 25,000 through the
// bulk one (see: Bars come from EODHD, bulk nightly and per ticker for the backfill).
//
// It is on the carved-out side of the per-name rule rather than in breach of it.
// The backfill makes one request per name holding no history, which is every
// name on the first run and a new joiner afterwards, and section 17's limits row
// names that carve-out rather than the rule being loosened.
public sealed class EodhdHistoricalBarFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : IHistoricalBarFeed
{
    public const string Endpoint = "eod";

    // The exchange suffix the provider wants on a ticker. Every store in this
    // system keys on the ticker alone, so the suffix is added at the request and
    // never carried, which is the same rule the news parser applies in reverse
    // when it strips one off an attribution.
    public const string ExchangeSuffix = ".US";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    public static EodhdHistoricalBarFeed Live(
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

    public async Task<IReadOnlyList<ProviderBar>> BarsAsync(
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        // Counted per name, because the backfill's whole argument is that it
        // costs one request per name and the run log records the figure.
        Requests++;

        var body = await request
            .SendAsync(token => FetchAsync(ticker, from, to, token), cancellationToken)
            .ConfigureAwait(false);

        // Parsed by the reader the double uses, and filtered to the window the
        // caller asked for. The provider honours the range, so the filter is a
        // guard rather than the mechanism: a payload wider than the request
        // would otherwise store sessions the backfill did not ask for.
        return
        [
            .. RecordedHistoricalBarFeed.Parse(body, ticker)
                .Where(bar => bar.SessionDate >= from && bar.SessionDate <= to)
                .OrderBy(bar => bar.SessionDate),
        ];
    }

    async Task<string> FetchAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        FormattableString.Invariant($"{Endpoint}/{ticker}{ExchangeSuffix}?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}") +
                        "&period=d&fmt=json",
                        credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, $"historical bar feed for {ticker}");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable($"historical bar feed for {ticker}", failure, credentials);
        }
    }
}
