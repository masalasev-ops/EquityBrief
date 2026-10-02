using System.Net;
using System.Net.Http;
using EquityBrief.Core.Providers;

namespace EquityBrief.Tests.Providers;

// The index's and the VIX's series feed: the one request it makes for a series, under the provider's index
// exchange and never a stock listing's, and the sessions it reads back from what the provider sends.
// see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
public class EodhdMarketSeriesFeedTests
{
    const string Key = "demo-key-not-a-real-one";
    const string Base = "https://eodhd.example/api/";

    // A handler that answers from a script and remembers what it was asked.
    sealed class Answering(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        internal List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);

            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    // Three sessions as the provider's daily endpoint sends an index, its adjusted close being its close.
    const string Payload = """
        [
          {"date":"2026-09-02","open":6400.1,"high":6450.2,"low":6390.3,"close":6440.4,"adjusted_close":6440.4,"volume":3500000000},
          {"date":"2026-09-03","open":6440.4,"high":6460.5,"low":6420.5,"close":6455.6,"adjusted_close":6455.6,"volume":3600000000},
          {"date":"2026-09-04","open":6455.6,"high":6470.7,"low":6430.8,"close":6465.9,"adjusted_close":6465.9,"volume":3700000000}
        ]
        """;

    [Fact]
    public async Task OneRequestASeriesNamesTheIndexExchangeAndNeverTheStockOneAndReadsTheSessionsSent()
    {
        var handler = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Payload) });
        var feed = new EodhdMarketSeriesFeed(
            new HttpClient(handler) { BaseAddress = new Uri(Base) },
            new ProviderCredentials(Key),
            new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

        var bars = await feed.SeriesAsync("GSPC", new DateOnly(2026, 9, 2), new DateOnly(2026, 9, 4));

        var asked = Assert.Single(handler.Asked);

        Assert.Equal("/api/eod/GSPC.INDX", asked.AbsolutePath);
        Assert.DoesNotContain(EodhdHistoricalBarFeed.ExchangeSuffix, asked.AbsolutePath, StringComparison.Ordinal);
        Assert.Contains("from=2026-09-02&to=2026-09-04&period=d&fmt=json", asked.Query, StringComparison.Ordinal);
        Assert.Equal(1, feed.Requests);
        Assert.Equal(
            [
                (new DateOnly(2026, 9, 2), 6400.1m, 6450.2m, 6390.3m, 6440.4m),
                (new DateOnly(2026, 9, 3), 6440.4m, 6460.5m, 6420.5m, 6455.6m),
                (new DateOnly(2026, 9, 4), 6455.6m, 6470.7m, 6430.8m, 6465.9m),
            ],
            bars.Select(bar => (bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close)));
    }
}
