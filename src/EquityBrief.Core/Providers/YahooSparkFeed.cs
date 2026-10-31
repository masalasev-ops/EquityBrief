using System.Net.Http;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One symbol's delayed quote as Yahoo's spark answers it: its price, the close before its session and the instant of
// the price.
public sealed record SparkQuote(string Symbol, decimal Price, decimal PreviousClose, DateTimeOffset At);

// Yahoo's spark of many symbols in one request, keyless and free and not the provider's, asked by the read API alone for
// the market strip while the regular session is open.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public interface IStripQuoteFeed
{
    int Requests { get; }

    Task<IReadOnlyDictionary<string, SparkQuote>> QuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellation = default);
}

// The spark's answer as sent: `spark.result`, a symbol each, its `response[0].meta` holding `regularMarketPrice`,
// `chartPreviousClose` and `regularMarketTime` in seconds. A symbol whose meta lacks any of the three is left out, so its
// tile draws the store's close; an answer holding no `spark.result` is refused.
public static class YahooSpark
{
    public static IReadOnlyDictionary<string, SparkQuote> Parse(string json)
    {
        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("spark", out var spark)
            || !spark.TryGetProperty("result", out var result)
            || result.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("Yahoo's spark answered no spark.result, so it is not the quotes asked for.");
        }

        var quotes = new Dictionary<string, SparkQuote>(StringComparer.Ordinal);

        foreach (var one in result.EnumerateArray())
        {
            if (one.TryGetProperty("symbol", out var symbol) && symbol.GetString() is { } named
                && one.TryGetProperty("response", out var response) && response.ValueKind == JsonValueKind.Array && response.GetArrayLength() > 0
                && response[0].TryGetProperty("meta", out var meta)
                && Number(meta, "regularMarketPrice") is { } price
                && Number(meta, "chartPreviousClose") is { } previous
                && meta.TryGetProperty("regularMarketTime", out var time) && time.ValueKind == JsonValueKind.Number)
            {
                quotes[named] = new SparkQuote(named, price, previous, DateTimeOffset.FromUnixTimeSeconds(time.GetInt64()));
            }
        }

        return quotes;
    }

    static decimal? Number(JsonElement meta, string name) =>
        meta.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var figure) ? figure : null;
}

// The spark over the network, one request for every symbol asked, tried once more on a transient failure and bounded by
// twenty seconds as the second sources' requests are.
public sealed class YahooSparkFeed(HttpClient yahoo, ProviderRequest? request = null) : IStripQuoteFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(YahooNasdaqBarFeed.Policy);

    public int Requests { get; private set; }

    public static YahooSparkFeed Live() => new(YahooNasdaqBarFeed.Client(YahooNasdaqBarFeed.YahooAddress));

    public async Task<IReadOnlyDictionary<string, SparkQuote>> QuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellation = default)
    {
        Requests++;

        var body = await YahooNasdaqBarFeed.GetAsync(
            request,
            yahoo,
            "v7/finance/spark?symbols=" + string.Join(",", symbols.Select(Uri.EscapeDataString)) + "&range=1d&interval=1d",
            "Yahoo's spark",
            cancellation).ConfigureAwait(false);

        return YahooSpark.Parse(body);
    }
}
