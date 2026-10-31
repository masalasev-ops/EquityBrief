using System.Globalization;
using System.Net.Http;

namespace EquityBrief.Core.Providers;

// The two free sources over the network, keyless and neither the provider's: Yahoo's daily chart for a session's bar,
// one request, and Nasdaq's quote for its high, low and close, its dated history first and, where that holds no row for
// the session yet, as it does on the evening of the session, the quote stating the session it last closed. Each request
// is tried once more on a transient failure and bounded by twenty seconds, so twenty members asked cannot hold the
// night past the minutes its deadline allows. The parsers are `SubstituteBars`', shared with the double.
// see: A member's day bar the provider sends impossible is skipped and asked of Yahoo with Nasdaq's check once every file is read
public sealed class YahooNasdaqBarFeed(HttpClient yahoo, HttpClient nasdaq, ProviderRequest? request = null) : ISubstituteBarFeed
{
    public const string YahooAddress = "https://query1.finance.yahoo.com/";

    public const string NasdaqAddress = "https://api.nasdaq.com/";

    // Both answer a request carrying no browser's name with a refusal or nothing at all.
    const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    public static RetryPolicy Policy { get; } = RetryPolicy.Standard with { Attempts = 2, Timeout = TimeSpan.FromSeconds(20) };

    readonly ProviderRequest request = request ?? new ProviderRequest(Policy);

    public int Requests { get; private set; }

    public static YahooNasdaqBarFeed Live() => new(Client(YahooAddress), Client(NasdaqAddress));

    internal static HttpClient Client(string address)
    {
        var client = new HttpClient { BaseAddress = new Uri(address), Timeout = Policy.Timeout + Policy.Timeout };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Agent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

        return client;
    }

    // The week to the session, so a chart answers the session among the few before it and a session it holds no bar for
    // is read as none rather than as the bar before it.
    public async Task<DayBar?> BarAsync(string ticker, DateOnly session, CancellationToken cancellation = default)
    {
        var from = new DateTimeOffset(session.AddDays(-7).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var to = new DateTimeOffset(session.AddDays(1).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero).ToUnixTimeSeconds();
        var body = await GetAsync(
            yahoo,
            FormattableString.Invariant($"v8/finance/chart/{Uri.EscapeDataString(ticker.Replace('.', '-'))}?period1={from}&period2={to}&interval=1d"),
            "Yahoo's chart",
            cancellation);

        return SubstituteBars.YahooChart(body).LastOrDefault(bar => bar.Session == session);
    }

    // The history over the week to the session: asked for the session alone, the table answers that the date is less
    // than the date it starts from and holds no row.
    public async Task<DayRange?> RangeAsync(string ticker, DateOnly session, CancellationToken cancellation = default)
    {
        var from = session.AddDays(-7).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var history = await GetAsync(
            nasdaq,
            $"api/quote/{Uri.EscapeDataString(ticker)}/historical?assetclass=stocks&fromdate={from}&todate={day}&limit=10",
            "Nasdaq's history",
            cancellation);

        if (SubstituteBars.NasdaqHistory(history).FirstOrDefault(range => range.Session == session) is { } dated)
        {
            return dated;
        }

        var info = await GetAsync(nasdaq, $"api/quote/{Uri.EscapeDataString(ticker)}/info?assetclass=stocks", "Nasdaq's quote", cancellation);

        return SubstituteBars.NasdaqInfo(info) is { } closed && closed.Session == session ? closed : null;
    }

    async Task<string> GetAsync(HttpClient client, string path, string named, CancellationToken cancellation)
    {
        Requests++;

        return await GetAsync(request, client, path, named, cancellation).ConfigureAwait(false);
    }

    // One request of either source, its refusal named for the source, shared with the strip's quotes and the oil's chart.
    internal static async Task<string> GetAsync(ProviderRequest request, HttpClient client, string path, string named, CancellationToken cancellation)
    {
        return await request
            .SendAsync(
                async token =>
                {
                    try
                    {
                        using var response = await client.GetAsync(path, token).ConfigureAwait(false);

                        return response.IsSuccessStatusCode
                            ? await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)
                            : throw new ProviderRefusal(
                                $"{named} answered {(int)response.StatusCode}.",
                                transient: RetryPolicy.Transient((int)response.StatusCode));
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (HttpRequestException failure)
                    {
                        throw new ProviderRefusal($"{named} could not be reached: " + failure.Message, transient: true);
                    }
                },
                cancellation)
            .ConfigureAwait(false);
    }
}
