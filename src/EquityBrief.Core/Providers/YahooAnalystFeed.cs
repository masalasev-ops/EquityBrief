using System.Net;
using System.Net.Http;

namespace EquityBrief.Core.Providers;

// Yahoo's quote summary of one stock's analysts over the network, free and keyless: a cookie taken from Yahoo's own page,
// the crumb that cookie is answered with, and the summary asked under that crumb, three requests an ask. Each request is
// tried once more on a transient failure and bounded by twenty seconds. A symbol Yahoo does not know is answered 404 with
// a summary holding no result, which reads as no analysts rather than as a failure. The parser is `YahooAnalysts.Parse`.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
public sealed class YahooAnalystFeed(HttpClient yahoo, HttpClient cookie, ProviderRequest? request = null) : IAnalystFeed
{
    public const string Address = "https://query1.finance.yahoo.com/";

    // The page whose answer sets the cookie the crumb is asked under; it answers 404 and sets the cookie all the same.
    public const string CookieAddress = "https://fc.yahoo.com/";

    // Yahoo answers a request carrying no browser's name with a refusal.
    const string Agent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0 Safari/537.36";

    public static RetryPolicy Policy { get; } = RetryPolicy.Standard with { Attempts = 2, Timeout = TimeSpan.FromSeconds(20) };

    readonly ProviderRequest request = request ?? new ProviderRequest(Policy);

    public int Requests { get; private set; }

    // Two clients over one cookie jar, so the crumb and the summary are asked with the cookie Yahoo's page set.
    public static YahooAnalystFeed Live()
    {
        var handler = new HttpClientHandler { CookieContainer = new CookieContainer(), UseCookies = true };

        return new(Client(handler, Address), Client(handler, CookieAddress));
    }

    static HttpClient Client(HttpClientHandler handler, string address)
    {
        var client = new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri(address), Timeout = Policy.Timeout + Policy.Timeout };

        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", Agent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json, text/plain, */*");

        return client;
    }

    public async Task<YahooAnalystAnswer?> AnalystsAsync(string ticker, CancellationToken cancellation = default)
    {
        await GetAsync(cookie, string.Empty, "Yahoo's page", answered: _ => true, cancellation).ConfigureAwait(false);

        var crumb = (await GetAsync(yahoo, "v1/test/getcrumb", "Yahoo's crumb", answered: status => status == HttpStatusCode.OK, cancellation).ConfigureAwait(false)).Trim();

        if (crumb.Length == 0 || crumb.Contains('<', StringComparison.Ordinal))
        {
            throw new ProviderRefusal("Yahoo's crumb came back empty, so the summary was not asked.", transient: true);
        }

        var body = await GetAsync(
            yahoo,
            $"v10/finance/quoteSummary/{Uri.EscapeDataString(ticker.Replace('.', '-'))}?modules={YahooAnalysts.Modules}&crumb={Uri.EscapeDataString(crumb)}",
            "Yahoo's quote summary",
            answered: status => status is HttpStatusCode.OK or HttpStatusCode.NotFound,
            cancellation).ConfigureAwait(false);

        return YahooAnalysts.Parse(body);
    }

    // One request, its answer's text where the status is one the caller reads and a refusal naming the status otherwise.
    async Task<string> GetAsync(HttpClient client, string path, string named, Func<HttpStatusCode, bool> answered, CancellationToken cancellation)
    {
        Requests++;

        return await request
            .SendAsync(
                async token =>
                {
                    try
                    {
                        using var response = await client.GetAsync(path, token).ConfigureAwait(false);

                        return answered(response.StatusCode)
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
