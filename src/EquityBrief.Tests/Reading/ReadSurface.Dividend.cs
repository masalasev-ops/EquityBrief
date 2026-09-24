using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Fundamentals;

namespace EquityBrief.Tests.Reading;

// read-surface, 11.8: the dividend the provider files, drawn in the numbers section from the newest
// filing alone, read back off the page against the store.
public partial class ReadSurface
{
    [Fact]
    public async Task APayerDrawsItsDividendAsFiledAndANonPayerSaysTheProviderFilesNone()
    {
        using var store = await WithLadders();
        var clock = FixedClock.At(Instant, SessionZones.UnitedStates);

        foreach (var ticker in new[] { "AAPL", "MSFT", "KEYS", "NFLX" })
        {
            await new FundamentalsFetcher(RecordedFundamentalsFeed.FromFolder(FixtureFolder()), clock, store.DatabaseFile, null)
                .RunAsync(ticker, null, $"run-fundamentals-{ticker}");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The two payers: every value on its element as the store holds it, drawn at the places a
        // reader reads it, and the provider named for the part.
        foreach (var ticker in new[] { "AAPL", "MSFT" })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            using var payload = JsonDocument.Parse(StoredPayload(store, ticker));

            var dividend = payload.RootElement.GetProperty("dividend");
            var drawn = Regex.Match(page, "<div class=\"numbers-dividend\" data-dividend=\"paid\" data-source=\"([^\"]*)\">(.*?)</p></div>", RegexOptions.Singleline);

            Assert.True(drawn.Success, $"{ticker}'s dividend is not drawn");
            Assert.Equal(FundamentalsFetcher.Provider, drawn.Groups[1].Value);
            Assert.Contains($"From {FundamentalsFetcher.Provider}", drawn.Groups[2].Value, StringComparison.Ordinal);

            foreach (var part in new[] { "forwardAnnualRate", "forwardYield", "payoutRatio", "exDividendDate", "payDate" })
            {
                Assert.Contains($"data-{part}=\"{dividend.GetProperty(part).GetString()}\"", drawn.Groups[2].Value, StringComparison.Ordinal);
            }

            decimal Stored(string part) => decimal.Parse(dividend.GetProperty(part).GetString()!, CultureInfo.InvariantCulture);

            Assert.Contains($">{Figures.PerShare(Stored("forwardAnnualRate"))}</td>", drawn.Groups[2].Value, StringComparison.Ordinal);
            Assert.Contains($">{Figures.Percent(Stored("forwardYield"))}</td>", drawn.Groups[2].Value, StringComparison.Ordinal);
            Assert.Contains($">{Figures.Percent(Stored("payoutRatio"))}</td>", drawn.Groups[2].Value, StringComparison.Ordinal);
            Assert.Contains($">{dividend.GetProperty("exDividendDate").GetString()}</td>", drawn.Groups[2].Value, StringComparison.Ordinal);
        }

        // The two non-payers, in one line each, naming the provider.
        foreach (var ticker in new[] { "KEYS", "NFLX" })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            Assert.Contains(
                $"<p class=\"numbers-dividend\" data-dividend=\"none\" data-source=\"{FundamentalsFetcher.Provider}\">The provider files no dividend for {ticker}. From {FundamentalsFetcher.Provider}.</p>",
                page,
                StringComparison.Ordinal);
            Assert.DoesNotContain("numbers-dividend-table", page, StringComparison.Ordinal);
        }
    }
}
