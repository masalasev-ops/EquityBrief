using System.Net.Http;
using EquityBrief.Core.Quotes;

namespace EquityBrief.Core.Providers;

// WTI crude's daily closes for the market strip, from Yahoo's daily chart of its front contract, keyless and free and
// not the provider's, which files no price of crude: one request a series over the window asked, read by the chart
// parser the second sources share, a session's close its own with no adjustment to it.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public sealed class YahooMarketSeriesFeed(HttpClient yahoo, ProviderRequest? request = null) : IMarketSeriesFeed
{
    readonly ProviderRequest request = request ?? new ProviderRequest(YahooNasdaqBarFeed.Policy);

    public int Requests { get; private set; }

    public static YahooMarketSeriesFeed Live() => new(YahooNasdaqBarFeed.Client(YahooNasdaqBarFeed.YahooAddress));

    // The symbol Yahoo files a series under, of the strip's tiles.
    public static string SymbolOf(string series) =>
        MarketStrip.Tiles.FirstOrDefault(tile => tile.Series == series).Yahoo
            ?? throw new ArgumentException($"Yahoo is asked for no series named {series}.", nameof(series));

    public async Task<IReadOnlyList<ProviderBar>> SeriesAsync(string series, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        var symbol = SymbolOf(series);

        Requests++;

        var start = new DateTimeOffset(from.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
        var end = new DateTimeOffset(to.AddDays(1).ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero).ToUnixTimeSeconds();
        var body = await YahooNasdaqBarFeed.GetAsync(
            request,
            yahoo,
            FormattableString.Invariant($"v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1={start}&period2={end}&interval=1d"),
            "Yahoo's chart",
            cancellationToken).ConfigureAwait(false);

        return
        [
            .. SubstituteBars.YahooChart(body)
                .Where(bar => bar.Session >= from && bar.Session <= to)
                .OrderBy(bar => bar.Session)
                .Select(bar => new ProviderBar(bar.Session, bar.Open, bar.High, bar.Low, bar.Close, bar.Close, bar.Volume)),
        ];
    }
}
