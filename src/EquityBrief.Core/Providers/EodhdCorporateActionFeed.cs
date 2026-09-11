using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The splits and dividends feed, over the network.
//
// Two requests a night, one per kind, and both counted. The per-name endpoints
// exist and are not used on this path: they would put one call per name on a
// night for information the bulk route returns in one
// (see: The nightly run is arithmetic only).
//
// The endpoint is the bulk end-of-day one with a `type` parameter, which is the
// same route the price file comes down and a different answer. The parser is
// `RecordedCorporateActionFeed.Parse`, shared, and it is the one that learned at
// 1.6 that a split's value is a ratio in a string and that this endpoint calls
// the exchange `exchange` where the price file calls it `exchange_short_name`.
// One reader for both would have silently dropped every row of one of them.
public sealed class EodhdCorporateActionFeed(
    HttpClient client,
    ProviderCredentials credentials,
    ProviderRequest? request = null) : ICorporateActionFeed
{
    public const string Endpoint = "eod-bulk-last-day";

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public int Attempts => request.Attempts;

    public static EodhdCorporateActionFeed Live(
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

    public async Task<IReadOnlyList<CorporateAction>> ActionsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default)
    {
        var actions = new List<CorporateAction>();

        foreach (var kind in new[] { ActionKind.Split, ActionKind.Dividend })
        {
            // Counted per kind. The night's claim is that its cost does not grow
            // with the universe, and two is a constant.
            Requests++;

            var body = await request
                .SendAsync(token => FetchAsync(exchange, session, kind, token), cancellation)
                .ConfigureAwait(false);

            actions.AddRange(RecordedCorporateActionFeed.Parse(body, exchange, kind)
                .Where(action => action.Date == session));
        }

        return [.. actions.OrderBy(action => action.Ticker, StringComparer.Ordinal).ThenBy(action => action.Kind)];
    }

    // The provider's own name for each kind, which is not the enum's. Written
    // once here rather than at the call site, because a plural spelled wrong
    // returns an empty array and an empty array reads as a day on which nothing
    // split and nothing paid.
    static string Parameter(ActionKind kind) => kind == ActionKind.Split ? "splits" : "dividends";

    async Task<string> FetchAsync(
        string exchange,
        DateOnly session,
        ActionKind kind,
        CancellationToken cancellation)
    {
        try
        {
            using var response = await client
                .GetAsync(
                    EodhdQuery.WithKey(
                        FormattableString.Invariant($"{Endpoint}/{exchange}?type={Parameter(kind)}&date={session:yyyy-MM-dd}&fmt=json"),
                        credentials),
                    cancellation)
                .ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, $"{Parameter(kind)} feed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable($"{Parameter(kind)} feed", failure, credentials);
        }
    }
}
