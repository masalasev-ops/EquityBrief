using System.Net;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Cloudflare;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.13: the market strip read back off Tonight over a constructed store, atop the page on every index,
// from the night's closes outside the session and from Yahoo's quotes, read from a capture, inside it, asked once and kept
// five minutes; and atop every page the copy holds, the closes alone, each screen's own strip taken out.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public partial class ReadSurface
{
    // The rows 21.13 adds that this check reaches: Tonight's strip and the copy's.
    internal static string[] StripRows =>
    [
        CheckReach.Key("15.7 Tonight", "The market strip"),
        CheckReach.Key("15.7 Tonight", "The market strip on the read-only copy"),
    ];

    // Yahoo's spark as the capture holds it, counting each ask.
    sealed class CapturedQuotes : IStripQuoteFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyDictionary<string, SparkQuote>> QuotesAsync(IReadOnlyList<string> symbols, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(YahooSpark.Parse(File.ReadAllText(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05", "strip-yahoo-spark.json"))));
        }
    }

    // A clock a test moves, for the five minutes the quotes are kept.
    sealed class StripClock(DateTimeOffset start) : IClock
    {
        public DateTimeOffset UtcNow { get; private set; } = start;

        public TimeZoneInfo SessionZone { get; } = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);

        public void Advance(TimeSpan by) => UtcNow += by;
    }

    static void StripCloses(TemporaryStore store)
    {
        store.Execute(
            "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
            $"('GSPC', '2026-10-01', '7500', '7500', '7500', '7500', 'test'), ('GSPC', '{TheSwitch}', '7575', '7575', '7575', '7575', 'test'), " +
            $"('HYG', '2026-10-01', '78', '78', '78', '78', 'test'), ('HYG', '{TheSwitch}', '77.22', '77.22', '77.22', '77.22', 'test');");
    }

    [Fact]
    public async Task TonightDrawsTheMarketStripAtopTheNightsCardsFromItsClosesOutsideTheSession()
    {
        using var store = await FamilyPagesStore();

        StripCloses(store);

        // A Saturday, outside any session: nothing is asked of Yahoo.
        var quotes = new CapturedQuotes();
        using var host = new Host(store.Root, FixedClock.At(new DateTimeOffset(2026, 10, 3, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), quotes);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        Assert.Equal(0, quotes.Requests);
        Assert.True(page.IndexOf("data-card=\"market-strip\"", StringComparison.Ordinal) < page.IndexOf("data-card=\"one-minute\"", StringComparison.Ordinal));
        Assert.Contains("<p class=\"strip-line\" data-quoted=\"false\">Prices as of the close of 2 Oct</p>", page, StringComparison.Ordinal);
        Assert.Contains("<b class=\"st-level\">7,575.00</b><span class=\"st-change gain\"><span class=\"st-arrow\" aria-label=\"up\">▲</span> +1.00%</span>", page, StringComparison.Ordinal);
        Assert.Contains("<b class=\"st-level\">77.22</b><span class=\"st-change loss\"><span class=\"st-arrow\" aria-label=\"down\">▼</span> -1.00%</span>", page, StringComparison.Ordinal);
        Assert.Contains("<div class=\"strip-tile\" data-tile=\"nasdaq\" data-change=\"none\" data-quoted=\"false\" data-prior=\"false\"><span class=\"st-label\">QQQ</span><b class=\"st-level degraded\">not stored</b></div>", page, StringComparison.Ordinal);
        Assert.Contains("The S&P 500 rose 1.0% on the day", page, StringComparison.Ordinal);

        // On the S&P 400 page the same strip, the share above the 200-day the index's own.
        var mid = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}?universe=400"));

        Assert.Contains("of S&P 400 stocks above their 200-day average", mid, StringComparison.Ordinal);
        Assert.Contains("each setup on this index reads its own floor", mid, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InTheSessionTonightsStripDrawsYahoosQuotesAskedOnceForFiveMinutes()
    {
        using var store = await FamilyPagesStore();

        StripCloses(store);

        // A Monday at 14:35 New York, inside the session: Yahoo asked once for every tile, and the page opened again a
        // minute on reads the same answer without asking again.
        var quotes = new CapturedQuotes();
        var clock = new StripClock(new DateTimeOffset(2026, 10, 5, 18, 35, 0, TimeSpan.Zero));
        using var host = new Host(store.Root, clock, quotes);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/tonight/{TheSwitch}"));

        Assert.Contains("<p class=\"strip-line\" data-quoted=\"true\">Delayed prices, 2:35 PM ET</p>", page, StringComparison.Ordinal);
        Assert.Contains("<b class=\"st-level\">7,580.07</b><span class=\"st-change loss\">", page, StringComparison.Ordinal);
        Assert.Equal(1, quotes.Requests);

        clock.Advance(TimeSpan.FromMinutes(1));
        await client.GetStringAsync($"/screens/tonight/{TheSwitch}");

        Assert.Equal(1, quotes.Requests);

        // Five minutes on it asks again.
        clock.Advance(TimeSpan.FromMinutes(5));
        await client.GetStringAsync($"/screens/tonight/{TheSwitch}");

        Assert.Equal(2, quotes.Requests);
    }

    [Fact]
    public void TheCopyDrawsTheStripAtopEveryPageAndTakesEachScreensOwnOut()
    {
        var strip = EquityBrief.Web.App.Cards.Computed("Markets", "<p class=\"card-summary\" data-summary>The S&amp;P 500 rose 1.0% on the day.</p>", title: "The markets", region: MarkRenderer.StripRegion);

        Assert.DoesNotContain("market-strip", SiteCopy.Clean("<div>" + strip + "<p>after</p></div>"), StringComparison.Ordinal);
        Assert.Contains("<div class=\"wrap copy-strip\" id=\"strip\">" + strip + "</div>", SiteCopy.Shell(new DateOnly(2026, 10, 2), new Dictionary<string, string>(), strip), StringComparison.Ordinal);
    }
}
