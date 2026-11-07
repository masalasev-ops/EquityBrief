using System.Globalization;
using System.Net;

namespace EquityBrief.Core.Providers;

// The provider's answers the long history's membership is matched over, each as sent: a past session's day file of an
// exchange, its symbol list listed or delisted, and one code's daily prices over a span.
public interface ILongHistoryFeed
{
    int Requests { get; }

    Task<string> DayFileAsync(string exchange, DateOnly session, CancellationToken cancellation = default);

    Task<string> SymbolsAsync(string exchange, bool delisted, CancellationToken cancellation = default);

    Task<string> PricesAsync(string code, DateOnly from, DateOnly through, CancellationToken cancellation = default);

    Task<string> SplitsAsync(string code, DateOnly from, CancellationToken cancellation = default);

    Task<string> DividendsAsync(string code, DateOnly from, CancellationToken cancellation = default);

    Task<string> CalendarAsync(DateOnly from, DateOnly through, CancellationToken cancellation = default);

    Task<string> CompanyAsync(string code, CancellationToken cancellation = default);
}

// The provider's answers for the long history, asked once by hand and never on a night. Its key travels in the query
// string, so an address is built where it is sent and never kept or printed; a busy answer is asked again after a wait.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
public sealed class EodhdLongHistoryFeed(HttpClient client, ProviderCredentials credentials, TimeSpan pause) : ILongHistoryFeed
{
    public static readonly TimeSpan Pause = TimeSpan.FromMilliseconds(500);

    public const int Tries = 4;

    public static readonly TimeSpan Busy = TimeSpan.FromSeconds(5);

    public static EodhdLongHistoryFeed Live(string baseAddress, ProviderCredentials credentials) =>
        new(
            new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All })
            {
                BaseAddress = new Uri(baseAddress.EndsWith('/') ? baseAddress : baseAddress + "/"),
                Timeout = TimeSpan.FromMinutes(5),
            },
            credentials,
            Pause);

    public int Requests { get; private set; }

    public Task<string> DayFileAsync(string exchange, DateOnly session, CancellationToken cancellation = default) =>
        SendAsync($"eod-bulk-last-day/{Uri.EscapeDataString(exchange)}", FormattableString.Invariant($"&date={session:yyyy-MM-dd}"), cancellation);

    public Task<string> SymbolsAsync(string exchange, bool delisted, CancellationToken cancellation = default) =>
        SendAsync($"exchange-symbol-list/{Uri.EscapeDataString(exchange)}", delisted ? "&delisted=1" : string.Empty, cancellation);

    public Task<string> PricesAsync(string code, DateOnly from, DateOnly through, CancellationToken cancellation = default) =>
        SendAsync($"eod/{Uri.EscapeDataString(code)}", FormattableString.Invariant($"&from={from:yyyy-MM-dd}&to={through:yyyy-MM-dd}"), cancellation);

    public Task<string> SplitsAsync(string code, DateOnly from, CancellationToken cancellation = default) =>
        SendAsync($"splits/{Uri.EscapeDataString(code)}", FormattableString.Invariant($"&from={from:yyyy-MM-dd}"), cancellation);

    public Task<string> DividendsAsync(string code, DateOnly from, CancellationToken cancellation = default) =>
        SendAsync($"div/{Uri.EscapeDataString(code)}", FormattableString.Invariant($"&from={from:yyyy-MM-dd}"), cancellation);

    public Task<string> CalendarAsync(DateOnly from, DateOnly through, CancellationToken cancellation = default) =>
        SendAsync("calendar/earnings", FormattableString.Invariant($"&from={from:yyyy-MM-dd}&to={through:yyyy-MM-dd}"), cancellation);

    // The company's general record alone, which carries the filer's number the provider files it under.
    public Task<string> CompanyAsync(string code, CancellationToken cancellation = default) =>
        SendAsync($"fundamentals/{Uri.EscapeDataString(code)}", "&filter=General", cancellation);

    async Task<string> SendAsync(string path, string query, CancellationToken cancellation)
    {
        Requests++;

        for (var tried = 1; ; tried++)
        {
            await Task.Delay(pause, cancellation).ConfigureAwait(false);

            using var response = await client.GetAsync($"{path}?api_token={credentials.ApiKey}&fmt=json{query}", cancellation).ConfigureAwait(false);
            var status = (int)response.StatusCode;

            if (tried < Tries && (status == 429 || status >= 500))
            {
                await Task.Delay(Busy * tried, cancellation).ConfigureAwait(false);

                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new ProviderRefusal(string.Create(CultureInfo.InvariantCulture, $"The provider answered {status} for {path}."), RetryPolicy.Transient(status));
            }

            return await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        }
    }
}
