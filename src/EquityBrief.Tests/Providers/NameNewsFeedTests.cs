using System.Globalization;
using System.Net;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Providers;

// One name's news, which a research pass reads and the night never does.
//
// The night's news is one dated query for the whole market, and a pass is the one place a
// ticker goes on a news query, which is why this feed is its own interface: the scan that
// keeps per-name requests off the night names this file as one that may hold a client,
// and the pass counts its pages among its requests.
// see: Everything expensive happens when a name is opened
public class NameNewsFeedTests
{
    const string Key = "demo-key-not-a-real-one";
    const string Base = "https://eodhd.example/api/";

    // One month, the most the feed asks in one piece, so a window is paged on its own.
    static readonly DateOnly From = new(2026, 8, 9);
    static readonly DateOnly To = new(2026, 9, 8);

    sealed class Answering(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        internal List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);

            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    static string Folder() => Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

    static EodhdNameNewsFeed Live(Answering handler) =>
        new(
            new HttpClient(handler) { BaseAddress = new Uri(Base) },
            new ProviderCredentials(Key),
            new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

    // One page of articles for a name inside the window.
    static string Page(int count, string symbol) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(at =>
            $$"""
            {"date":"2026-09-01T12:00:00+00:00","title":"page item {{at}}","content":"text",
             "link":"https://example.test/{{symbol}}/{{at}}","symbols":["{{symbol}}"]}
            """)) + "]";

    [Fact]
    public async Task OneNamesQueryCarriesTheTickerOnItsExchangeAndTheWindowAndIsPagedUntilTheWindowIsCovered()
    {
        string[] pages = [Page(EodhdNewsFeed.Limit, "KEYS.US"), Page(7, "KEYS.US")];
        var at = 0;
        var handler = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(pages[Math.Min(at++, pages.Length - 1)]) });
        var feed = Live(handler);

        var articles = await feed.ArticlesAsync("KEYS", From, To);

        Assert.Equal(2, handler.Asked.Count);
        Assert.Equal(2, feed.Requests);
        Assert.Equal(EodhdNewsFeed.Limit + 7, articles.Count);

        var first = handler.Asked[0];

        Assert.Equal("/api/news", first.AbsolutePath);
        Assert.Contains("s=KEYS.US", first.Query, StringComparison.Ordinal);
        Assert.Contains("from=2026-08-09", first.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-09-08", first.Query, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"limit={EodhdNewsFeed.Limit}"), first.Query, StringComparison.Ordinal);
        Assert.Contains("offset=0", first.Query, StringComparison.Ordinal);
        Assert.Contains(FormattableString.Invariant($"offset={EodhdNewsFeed.Limit}"), handler.Asked[1].Query, StringComparison.Ordinal);

        // A window the provider still had more of at the last page is refused rather than handing a
        // pass part of a window as the whole of it, and refused as a refusal the pass records rather
        // than as an error that stops it, which a pass for MSFT stopped on at 6.11.
        var endless = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page(EodhdNewsFeed.Limit, "KEYS.US")) });
        var refused = await Assert.ThrowsAsync<ProviderRefusal>(() => Live(endless).ArticlesAsync("KEYS", From, To));

        Assert.Contains("still had more", refused.Message, StringComparison.Ordinal);
        Assert.False(refused.Transient);
        Assert.Equal(EodhdNewsFeed.MostPages, endless.Asked.Count);
    }

    // A window asked a month at a time, since the provider's time to answer grows with the articles a window holds.
    // see: A name's news is asked of the provider a month at a time, and a window it does not answer in time is read as unread
    [Fact]
    public async Task AWindowIsAskedAMonthAtATimeEachPieceFromItsFirstDayToTheDayBeforeTheSameDateAMonthOn()
    {
        // Worked by hand. QCOM's window since its filing, 2026-07-29 to 2026-10-02, is three pieces; a piece starting on
        // the last day of a long month ends the day before the same date a month on in a short one; and a week is one.
        Assert.Equal(
            [(new DateOnly(2026, 7, 29), new DateOnly(2026, 8, 28)), (new DateOnly(2026, 8, 29), new DateOnly(2026, 9, 28)), (new DateOnly(2026, 9, 29), new DateOnly(2026, 10, 2))],
            EodhdNameNewsFeed.Months(new DateOnly(2026, 7, 29), new DateOnly(2026, 10, 2)));
        Assert.Equal(
            [(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 27)), (new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 1))],
            EodhdNameNewsFeed.Months(new DateOnly(2026, 1, 31), new DateOnly(2026, 3, 1)));
        Assert.Equal([(new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 2))], EodhdNameNewsFeed.Months(new DateOnly(2026, 9, 26), new DateOnly(2026, 10, 2)));

        // The feed asks for each piece in turn, its dates in the query, and keeps each piece's articles inside it: the
        // three dated 2026-09-01 the provider answers every piece with are kept by the second piece alone.
        var handler = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Page(3, "QCOM.US")) });
        var articles = await Live(handler).ArticlesAsync("QCOM", new DateOnly(2026, 7, 29), new DateOnly(2026, 10, 2));

        Assert.Equal(3, handler.Asked.Count);
        Assert.Contains("from=2026-07-29&to=2026-08-28", handler.Asked[0].Query, StringComparison.Ordinal);
        Assert.Contains("from=2026-08-29&to=2026-09-28", handler.Asked[1].Query, StringComparison.Ordinal);
        Assert.Contains("from=2026-09-29&to=2026-10-02", handler.Asked[2].Query, StringComparison.Ordinal);
        Assert.Equal(3, articles.Count);
    }

    [Fact]
    public async Task TheLiveFeedAndTheDoubleReadTheNamesCaptureTheSameWayInsideTheWindow()
    {
        var captured = File.ReadAllText(Path.Combine(Folder(), RecordedNameNewsFeed.FileFor("KEYS")));
        var handler = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(captured) });

        // A window that leaves out part of the capture, so both are shown to cut it.
        var all = RecordedNewsFeed.Parse(captured);
        var cut = all.Select(article => DateOnly.FromDateTime(article.Published.UtcDateTime)).Order().ElementAt(all.Count / 2);

        var live = await Live(handler).ArticlesAsync("KEYS", cut, To);
        var recorded = new RecordedNameNewsFeed(Folder());
        var replayed = await recorded.ArticlesAsync("KEYS", cut, To);

        Assert.Equal(13, all.Count);
        Assert.InRange(live.Count, 1, all.Count - 1);
        Assert.Equal(
            live.Select(article => $"{article.Published.ToString("O", CultureInfo.InvariantCulture)}|{article.Title}|{string.Join(",", article.Symbols)}"),
            replayed.Select(article => $"{article.Published.ToString("O", CultureInfo.InvariantCulture)}|{article.Title}|{string.Join(",", article.Symbols)}"));
        Assert.All(live, article => Assert.True(DateOnly.FromDateTime(article.Published.UtcDateTime) >= cut));
        Assert.Equal(1, recorded.Requests);
    }

    [Fact]
    public async Task ADoubleHoldingNoCaptureForTheNameRefusesByNameRatherThanAnsweringWithNothing()
    {
        var recorded = new RecordedNameNewsFeed(Folder());

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => recorded.ArticlesAsync("MSFT", From, To));

        Assert.Contains("MSFT", refused.Message, StringComparison.Ordinal);
        Assert.Contains("refused rather than answered with no articles", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoFailureCarriesTheKeyOrTheAddress()
    {
        var failing = new Answering(_ => throw new HttpRequestException($"refused for {Base}news?s=KEYS.US&api_token={Key}&fmt=json"));
        var failure = await Assert.ThrowsAsync<ProviderRefusal>(() => Live(failing).ArticlesAsync("KEYS", From, To));

        var refusing = new Answering(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var refusal = await Assert.ThrowsAsync<ProviderRefusal>(() => Live(refusing).ArticlesAsync("KEYS", From, To));

        foreach (var text in new[] { failure.Message, failure.ToString(), refusal.Message, refusal.ToString() })
        {
            Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
            Assert.DoesNotContain("api_token", text, StringComparison.Ordinal);
            Assert.DoesNotContain("eodhd.example", text, StringComparison.Ordinal);
        }

        Assert.Contains("name news", refusal.Message, StringComparison.Ordinal);
    }
}
