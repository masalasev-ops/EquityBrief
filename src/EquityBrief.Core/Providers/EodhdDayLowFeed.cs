using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One stock's day so far as the provider's live quote states it: the stock, the instant it is as of and the day's low.
public sealed record DayLow(string Ticker, DateTimeOffset At, decimal Low);

// The day's lows of many stocks, a page of symbols a request.
public interface IDayLowFeed
{
    int Requests { get; }

    Task<IReadOnlyList<DayLow>> LowsAsync(IReadOnlyList<string> tickers, CancellationToken cancellation = default);
}

// The provider's live quote for many listings at once: the first symbol in the path and the rest under `s`, a page of 15
// symbols a request, at the provider's weight of one a symbol. A stock it states no low for is left out.
// see: The fill check asks EODHD's live quotes after the close on its own run, one request a page of symbols
public sealed class EodhdDayLowFeed(HttpClient client, ProviderCredentials credentials, ProviderRequest? request = null) : IDayLowFeed
{
    // The symbols a request carries.
    public const int Page = 15;

    readonly ProviderRequest request = request ?? new ProviderRequest(RetryPolicy.Standard);

    public int Requests { get; private set; }

    public static EodhdDayLowFeed Live(string? baseAddress, ProviderCredentials credentials, ProviderRequest? request = null)
    {
        var policy = request?.Policy ?? RetryPolicy.Standard;
        var address = string.IsNullOrWhiteSpace(baseAddress) ? EodhdBulkPriceFeed.DefaultBaseAddress : baseAddress;

        return new(new HttpClient { BaseAddress = new Uri(address.EndsWith('/') ? address : address + "/"), Timeout = policy.Timeout + policy.Timeout }, credentials, request);
    }

    public async Task<IReadOnlyList<DayLow>> LowsAsync(IReadOnlyList<string> tickers, CancellationToken cancellation = default)
    {
        var lows = new List<DayLow>();

        foreach (var page in tickers.Distinct(StringComparer.Ordinal).Chunk(Page))
        {
            Requests++;

            var path = $"{EodhdQuoteFeed.Endpoint}/{page[0]}{EodhdQuoteFeed.ExchangeSuffix}?fmt=json"
                + (page.Length > 1 ? "&s=" + string.Join(',', page.Skip(1).Select(ticker => ticker + EodhdQuoteFeed.ExchangeSuffix)) : string.Empty);
            var body = await request.SendAsync(token => FetchAsync(path, token), cancellation).ConfigureAwait(false);

            lows.AddRange(Parse(body));
        }

        return lows;
    }

    async Task<string> FetchAsync(string path, CancellationToken cancellation)
    {
        try
        {
            using var response = await client.GetAsync(EodhdQuery.WithKey(path, credentials), cancellation).ConfigureAwait(false);

            return response.IsSuccessStatusCode
                ? await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false)
                : throw EodhdQuery.Refused((int)response.StatusCode, "live quote");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception failure) when (failure is not ProviderRefusal)
        {
            throw EodhdQuery.Unreachable("live quote", failure, credentials);
        }
    }

    // The answer as the provider sends it: one object for one symbol, an array for several, each naming its listing under
    // `code`, its instant in seconds under `timestamp` and the day's low under `low`, "NA" or none where it states none.
    public static IReadOnlyList<DayLow> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var rows = document.RootElement.ValueKind == JsonValueKind.Array ? [.. document.RootElement.EnumerateArray()] : new[] { document.RootElement };
        var lows = new List<DayLow>();

        foreach (var row in rows)
        {
            if (row.ValueKind != JsonValueKind.Object || !row.TryGetProperty("code", out var code) || code.GetString() is not { Length: > 0 } listing
                || !row.TryGetProperty("low", out var low) || Number(low) is not { } price || !(price > 0)
                || !row.TryGetProperty("timestamp", out var stamp) || stamp.ValueKind != JsonValueKind.Number)
            {
                continue;
            }

            var ticker = listing.EndsWith(EodhdQuoteFeed.ExchangeSuffix, StringComparison.Ordinal) ? listing[..^EodhdQuoteFeed.ExchangeSuffix.Length] : listing;

            lows.Add(new DayLow(ticker, DateTimeOffset.FromUnixTimeSeconds(stamp.GetInt64()), price));
        }

        return lows;
    }

    static decimal? Number(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
        _ => null,
    };
}
