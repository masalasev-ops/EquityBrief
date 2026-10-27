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
    public async Task APayerDrawsItsDividendAsFiledAndANonPayerDrawsNone()
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
        // reader reads it, and the provider kept on the element and named nowhere in the page's words.
        // see: The numbers open on a snapshot of the newest filing with every other filed figure but the segments folded beneath it, and the report names no provider
        foreach (var ticker in new[] { "AAPL", "MSFT" })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            using var payload = JsonDocument.Parse(StoredPayload(store, ticker));

            var dividend = payload.RootElement.GetProperty("dividend");
            var drawn = Regex.Match(page, "<div class=\"numbers-dividend\" data-dividend=\"paid\" data-source=\"([^\"]*)\">(.*?)</p></div>", RegexOptions.Singleline);

            Assert.True(drawn.Success, $"{ticker}'s dividend is not drawn");
            Assert.Equal(FundamentalsFetcher.Provider, drawn.Groups[1].Value);
            Assert.Contains("<p class=\"dividend-source\">As of the newest fetch of the company's figures.", drawn.Groups[2].Value, StringComparison.Ordinal);
            Assert.DoesNotContain(FundamentalsFetcher.Provider, Regex.Replace(page, "<[^>]+>", " "), StringComparison.OrdinalIgnoreCase);

            foreach (var part in new[] { "forwardAnnualRate", "payoutRatio", "exDividendDate", "payDate" })
            {
                Assert.Contains($"data-{part}=\"{dividend.GetProperty(part).GetString()}\"", drawn.Groups[2].Value, StringComparison.Ordinal);
            }

            decimal Stored(string part) => decimal.Parse(dividend.GetProperty(part).GetString()!, CultureInfo.InvariantCulture);

            // The yield is worked at the stored close over the rate, as every place on the page draws it, and the provider's
            // own stands beside it only where the price it was struck at stands far from the close.
            // see: Each concept a page draws holds one value worked at the stored close beside the provider's own marked stale where struck far from it, and a check flags any concept drawn twice
            var yieldCell = Regex.Match(drawn.Groups[2].Value, "<td class=\"num\" data-concept=\"yield-at-the-close\" data-concept-value=\"([0-9.]+%)\">([0-9.]+%)");

            Assert.True(yieldCell.Success, $"{ticker}'s yield is not worked at the close");
            Assert.Equal(yieldCell.Groups[1].Value, yieldCell.Groups[2].Value);
            Assert.Contains("<th>Yield at the close of ", drawn.Groups[2].Value, StringComparison.Ordinal);
            Assert.Contains($">{Figures.PerShare(Stored("forwardAnnualRate"))}</td>", drawn.Groups[2].Value, StringComparison.Ordinal);
            Assert.Contains($">{Figures.Percent(Stored("payoutRatio"))}</td>", drawn.Groups[2].Value, StringComparison.Ordinal);
            Assert.Contains($">{dividend.GetProperty("exDividendDate").GetString()}</td>", drawn.Groups[2].Value, StringComparison.Ordinal);
        }

        // The two non-payers draw none of it, while the numbers section around it is drawn.
        foreach (var ticker in new[] { "KEYS", "NFLX" })
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));

            Assert.Contains($"<section class=\"numbers\" data-ticker=\"{ticker}\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("numbers-dividend", page, StringComparison.Ordinal);
            Assert.DoesNotContain("no dividend", page, StringComparison.Ordinal);
        }

        // A row holding no dividend part, as one fetched before the part existed, says the part is
        // absent and why rather than drawing nothing, since that absence is the store's and a payer
        // drawn as paying none would be wrong. A fetch that old stored no copy of the parts as of the
        // fetch either, since the copies came later still.
        store.Execute("UPDATE fundamentals SET payload = json_remove(payload, '$.dividend') WHERE ticker = 'AAPL';");
        store.Execute("DELETE FROM fundamentals_snapshot WHERE ticker = 'AAPL';");

        var absent = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/AAPL"));

        Assert.Contains("The dividend is absent rather than empty, and the row says why", absent, StringComparison.Ordinal);
        Assert.DoesNotContain("numbers-dividend", absent, StringComparison.Ordinal);
    }
}
