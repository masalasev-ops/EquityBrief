using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using EquityBrief.Core.History;

namespace EquityBrief.Core.Providers;

// One answer of a second source: the sessions it sent, none where it holds the symbol over nothing asked, and what it said.
public sealed record SourceAnswer(IReadOnlyList<SourceBar> Bars, string Outcome);

// The three second sources the fill asks for a hole inside a code's own prices, none of them the provider: Yahoo's daily
// chart and Stooq's daily file, free and keyless, and Tiingo's meta and daily prices on the operator's key. One request
// each call, a Tiingo request made once and never retried so each one is counted against its limits.
// see: A hole inside a code's own prices is filled from a second source's traded prices where they hold one ratio to the store's on each side, never interpolated
public interface ISecondSourceFeed
{
    Task<SourceAnswer> YahooAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default);

    Task<SourceAnswer> StooqAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default);

    // The first and last session Tiingo holds the symbol over, or none where it holds it over none.
    Task<(DateOnly First, DateOnly Last)?> TiingoMetaAsync(string symbol, CancellationToken cancellation = default);

    Task<SourceAnswer> TiingoAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default);
}

// The operator's Tiingo key, read by the worker from its appsettings.Secrets.json and sent in the request's header alone,
// never in an address, a command or a log; anything about to be written down passes through `Redact` first.
// see: A hole inside a code's own prices is filled from a second source's traded prices where they hold one ratio to the store's on each side, never interpolated
public sealed class TiingoCredentials(string apiKey)
{
    public const string ApiKeyName = "EquityBrief:Providers:Tiingo:ApiKey";

    public string ApiKey { get; } = string.IsNullOrWhiteSpace(apiKey)
        ? throw new InvalidOperationException($"No Tiingo key. Set '{ApiKeyName}' in the worker's appsettings.Secrets.json, or in the environment.")
        : apiKey.Trim();

    public override string ToString() => Withheld;

    public const string Withheld = "TiingoCredentials(key withheld)";

    public string Redact(string text) =>
        string.IsNullOrEmpty(text)
            ? text
            : Addresses.Replace(text.Replace(ApiKey, ProviderCredentials.KeyWithheld, StringComparison.Ordinal), ProviderCredentials.AddressWithheld);

    static readonly Regex Addresses = new(@"[a-zA-Z][a-zA-Z0-9+.\-]*://\S*", RegexOptions.Compiled);
}

public sealed class SecondSourcePriceFeed(HttpClient yahoo, HttpClient stooq, HttpClient tiingo, TiingoCredentials? key) : ISecondSourceFeed
{
    public const string StooqAddress = "https://stooq.com/";

    public const string TiingoAddress = "https://api.tiingo.com/";

    static readonly RetryPolicy Free = RetryPolicy.Standard with { Attempts = 2, Timeout = TimeSpan.FromSeconds(30) };

    static readonly RetryPolicy Counted = RetryPolicy.Standard with { Attempts = 1, Timeout = TimeSpan.FromSeconds(60) };

    public static SecondSourcePriceFeed Live(TiingoCredentials? key) =>
        new(YahooNasdaqBarFeed.Client(YahooNasdaqBarFeed.YahooAddress), YahooNasdaqBarFeed.Client(StooqAddress), Plain(TiingoAddress), key);

    static HttpClient Plain(string address)
    {
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = TimeSpan.FromSeconds(120) };

        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");

        return client;
    }

    public async Task<SourceAnswer> YahooAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default)
    {
        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero).ToUnixTimeSeconds();
        var (status, body) = await GetAsync(yahoo, FormattableString.Invariant($"v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={start}&period2={end}&interval=1d&includeAdjustedClose=true"), null, Free, "Yahoo's chart", cancellation).ConfigureAwait(false);

        return status == HttpStatusCode.NotFound
            ? new SourceAnswer([], "Yahoo holds no such symbol")
            : Answer([.. SecondSources.YahooChart(body).Where(bar => bar.Session >= from && bar.Session <= to)], "Yahoo");
    }

    public async Task<SourceAnswer> StooqAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default)
    {
        var (status, body) = await GetAsync(stooq, $"q/d/l/?s={Uri.EscapeDataString(symbol)}&d1={from.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}&d2={to.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}&i=d", null, Free, "Stooq's daily file", cancellation).ConfigureAwait(false);

        return status == HttpStatusCode.NotFound
            ? new SourceAnswer([], "Stooq holds no such symbol")
            : Answer(SecondSources.StooqDaily(body), "Stooq");
    }

    public async Task<(DateOnly First, DateOnly Last)?> TiingoMetaAsync(string symbol, CancellationToken cancellation = default)
    {
        var (status, body) = await GetAsync(tiingo, $"tiingo/daily/{Uri.EscapeDataString(symbol)}", Token(), Counted, "Tiingo's meta", cancellation).ConfigureAwait(false);

        return status == HttpStatusCode.NotFound ? null : SecondSources.TiingoMeta(body);
    }

    public async Task<SourceAnswer> TiingoAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default)
    {
        var (status, body) = await GetAsync(tiingo, $"tiingo/daily/{Uri.EscapeDataString(symbol)}/prices?startDate={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&endDate={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&format=json", Token(), Counted, "Tiingo's daily prices", cancellation).ConfigureAwait(false);

        return status == HttpStatusCode.NotFound
            ? new SourceAnswer([], "Tiingo holds no such symbol")
            : Answer(SecondSources.TiingoPrices(body), "Tiingo");
    }

    string Token() => "Token " + (key ?? throw new InvalidOperationException($"No Tiingo key. Set '{TiingoCredentials.ApiKeyName}' in the worker's appsettings.Secrets.json, or in the environment.")).ApiKey;

    static SourceAnswer Answer(IReadOnlyList<SourceBar> bars, string named) =>
        new(bars, bars.Count == 0 ? named + " sent no session" : FormattableString.Invariant($"{named} sent {bars.Count} session(s)"));

    // One request, its key in the header alone; a symbol the source holds none of answers not found, any other refusal
    // named for the source with the key taken out of its words.
    async Task<(HttpStatusCode Status, string Body)> GetAsync(HttpClient client, string path, string? authorization, RetryPolicy policy, string named, CancellationToken cancellation) =>
        await new ProviderRequest(policy).SendAsync(
            async token =>
            {
                try
                {
                    using var message = new HttpRequestMessage(HttpMethod.Get, path);

                    if (authorization is not null)
                    {
                        message.Headers.TryAddWithoutValidation("Authorization", authorization);
                    }

                    using var response = await client.SendAsync(message, token).ConfigureAwait(false);

                    if (response.StatusCode == HttpStatusCode.NotFound)
                    {
                        return (response.StatusCode, string.Empty);
                    }

                    return response.IsSuccessStatusCode
                        ? (response.StatusCode, await response.Content.ReadAsStringAsync(token).ConfigureAwait(false))
                        : throw new ProviderRefusal($"{named} answered {(int)response.StatusCode}.", transient: RetryPolicy.Transient((int)response.StatusCode));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (HttpRequestException failure)
                {
                    throw new ProviderRefusal($"{named} could not be reached: " + Redact(failure.Message), transient: true);
                }
            },
            cancellation).ConfigureAwait(false);

    string Redact(string text) => key?.Redact(text) ?? text;
}
