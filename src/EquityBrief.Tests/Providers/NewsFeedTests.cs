using System.Net;
using System.Net.Http;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Providers;

// The news feed, and the fan-out that is the whole reason it costs one request.
//
// News was the only feed without an interface until 2.5, which made it the only
// one whose request count no contract forced. The 1.7 measurement ran through a
// class the nightly path does not reach, so a live implementation could have
// made one request per name and nothing would have said so.
// see: News is one dated query, paged to cover the day, and attributed to names locally
public class NewsFeedTests
{
    const string Key = "demo-key-not-a-real-one";
    const string Base = "https://eodhd.example/api/";

    static readonly DateOnly From = new(2026, 8, 25);
    static readonly DateOnly To = new(2026, 9, 8);

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

    static string Folder() => Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

    static string Captured() =>
        File.ReadAllText(Directory.GetFiles(Folder(), RecordedNewsFeed.FilePrefix + "*.json").Single());

    static (EodhdNewsFeed Feed, Answering Handler) Feed()
    {
        var handler = new Answering(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Captured()) });

        return (
            new EodhdNewsFeed(
                new HttpClient(handler) { BaseAddress = new Uri(Base) },
                new ProviderCredentials(Key),
                new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask)),
            handler);
    }

    [Fact]
    public async Task ADayWiderThanOnePageIsPagedUntilItIsCoveredAndEveryPageIsCounted()
    {
        // The property 5.0's whole news ruling rests on, and 5.5's own mutation
        // found nothing exercising it: the captured payload is one page under
        // the cap, so a feed that stopped after one page passed the suite. The
        // fixture cannot produce a day wider than a page, which is the
        // unproducible shape, so the pages are constructed.
        //
        // 2.5 measured one request for a single session coming back at exactly
        // 1,000 articles, which is the provider's cap. A count taken from it is
        // a count over whatever the cap happened to include.
        // see: News is one dated query, paged to cover the day, and attributed to names locally
        var pages = new List<string>
        {
            Page(EodhdNewsFeed.Limit, "AAA"),
            Page(EodhdNewsFeed.Limit, "BBB"),
            Page(3, "CCC"),
        };

        var at = 0;
        var handler = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(pages[Math.Min(at++, pages.Count - 1)]),
        });

        var feed = new EodhdNewsFeed(
            new HttpClient(handler) { BaseAddress = new Uri(Base) },
            new ProviderCredentials(Key),
            new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

        var articles = await feed.ArticlesAsync(From, To);

        // Every page is asked for and every page is counted, so the figure the
        // run log carries is the pages rather than the queries.
        Assert.Equal(3, handler.Asked.Count);
        Assert.Equal(3, feed.Requests);
        Assert.Equal((EodhdNewsFeed.Limit * 2) + 3, articles.Count);

        // The offset advances by what arrived rather than by a page size the
        // caller assumed, so a provider that answers short of the cap is not
        // asked for the same rows twice.
        Assert.Contains("offset=0", handler.Asked[0].Query, StringComparison.Ordinal);
        Assert.Contains($"offset={EodhdNewsFeed.Limit}", handler.Asked[1].Query, StringComparison.Ordinal);
        Assert.Contains($"offset={EodhdNewsFeed.Limit * 2}", handler.Asked[2].Query, StringComparison.Ordinal);

        // The last page carries names the first two do not, which is the half a
        // feed that fetched three pages and kept one would also have to pass.
        Assert.Contains(articles, article => article.Symbols.Contains("CCC"));
        Assert.Contains(articles, article => article.Symbols.Contains("AAA"));

        // And a day that reaches the page cap refuses rather than storing a
        // truncated count, because a count over part of a day reported as the
        // whole is the failure this exists to prevent.
        var endless = new Answering(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(Page(EodhdNewsFeed.Limit, "ZZZ")),
        });

        var runaway = new EodhdNewsFeed(
            new HttpClient(endless) { BaseAddress = new Uri(Base) },
            new ProviderCredentials(Key),
            new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(
            () => runaway.ArticlesAsync(From, To));

        Assert.Contains("still had more", refused.Message, StringComparison.Ordinal);
        Assert.Equal(EodhdNewsFeed.MostPages, endless.Asked.Count);
    }

    // One page of articles inside the window, each attributed to one name.
    static string Page(int count, string symbol) =>
        "[" + string.Join(",", Enumerable.Range(0, count).Select(at =>
            $$"""
            {"date":"2026-09-01T12:00:00+00:00","title":"page item {{at}}","content":"text",
             "link":"https://example.test/{{symbol}}/{{at}}","symbols":["{{symbol}}"]}
            """)) + "]";

    [Fact]
    public async Task OneDatedRequestCarriesTheWindowAndNoTicker()
    {
        var (feed, handler) = Feed();

        await feed.ArticlesAsync(From, To);

        var asked = Assert.Single(handler.Asked);

        Assert.Equal("/api/news", asked.AbsolutePath);
        Assert.Contains("from=2026-08-25", asked.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-09-08", asked.Query, StringComparison.Ordinal);

        // No ticker, which is the request this system makes and not the one the
        // 1.7 capture was taken with. That query named NVDA because the
        // measurement it fed was about coverage per name, and it sits in the
        // fixture's manifest where the two can be read side by side.
        Assert.DoesNotContain("s=", asked.Query, StringComparison.Ordinal);
        Assert.Contains("news?s=NVDA.US", File.ReadAllText(Path.Combine(Folder(), "manifest.json")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLiveFeedAndTheDoubleReadOneResponseTheSameWay()
    {
        var (feed, _) = Feed();

        var live = await feed.ArticlesAsync(From, To);
        var recorded = await RecordedNewsFeed.FromFolder(Folder()).ArticlesAsync(From, To);

        Assert.NotEmpty(live);

        // Compared field by field rather than as records. `NewsArticle` carries
        // its attribution as a list, and a record compares a list by reference,
        // so two parses of one payload are never equal however identical their
        // contents. Asserting the records would have been an assertion that
        // could not hold, which is a different failure from one that does not.
        Assert.Equal(Readable(recorded), Readable(live));
    }

    static IReadOnlyList<string> Readable(IEnumerable<NewsArticle> articles) =>
    [
        .. articles.Select(article => string.Join(
            " | ",
            article.Published.ToString("O"),
            article.Title,
            article.Channel,
            string.Join(",", article.Symbols),
            article.Text)),
    ];

    [Fact]
    public async Task TheRequestCountIsMeasuredOnTheInterfaceAndNotOnTheDouble()
    {
        // The property the interface exists for. Both implementations answer the
        // same question, so a count read through `INewsFeed` is a count the live
        // feed has to answer for too.
        INewsFeed live = Feed().Feed;
        INewsFeed recorded = RecordedNewsFeed.FromFolder(Folder());

        Assert.Equal(0, live.Requests);
        Assert.Equal(0, recorded.Requests);

        await live.ArticlesAsync(From, To);
        await recorded.ArticlesAsync(From, To);

        Assert.Equal(1, live.Requests);
        Assert.Equal(1, recorded.Requests);
    }

    [Fact]
    public async Task OneRequestIsFannedOutToNamesInCode()
    {
        // The done condition, and it is what the zero per-name rule buys. One
        // dated request covers the market and every name in it is reached by
        // reading the attribution the payload already carries.
        INewsFeed feed = RecordedNewsFeed.FromFolder(Folder());
        var articles = await feed.ArticlesAsync(From, To);

        Assert.NotEmpty(articles);

        var byName = NewsAttribution.ByName(articles);

        Assert.True(byName.Count > 1, $"One request reached {byName.Count} name(s), expected more than one.");

        // Still one request, however many names it reached. That is the whole
        // claim: the count does not grow with the population.
        Assert.Equal(1, feed.Requests);

        // Every article reaches every name it names, and no name it does not.
        foreach (var article in articles)
        {
            foreach (var symbol in article.Symbols)
            {
                Assert.Contains(article, byName[symbol]);
            }
        }

        Assert.All(byName, pair => Assert.All(pair.Value, article =>
            Assert.Contains(pair.Key, article.Symbols, StringComparer.OrdinalIgnoreCase)));

        // And the sum over names is at least the article count, since an article
        // naming three tickers reaches three. Stated because a fan-out that
        // returned each article once would pass every assertion above.
        Assert.True(
            byName.Sum(pair => pair.Value.Count) >= articles.Count,
            "The fan-out reached fewer name-articles than there are articles, so it is not fanning out.");
    }

    [Fact]
    public async Task NoFailureCarriesTheKeyOrTheAddress()
    {
        var credentials = new ProviderCredentials(Key);
        var handler = new Answering(_ => throw new HttpRequestException(
            $"refused for {Base}news?api_token={Key}&fmt=json"));

        var feed = new EodhdNewsFeed(
            new HttpClient(handler) { BaseAddress = new Uri(Base) },
            credentials,
            new ProviderRequest(RetryPolicy.Standard, (_, _) => Task.CompletedTask));

        var failure = await Assert.ThrowsAsync<ProviderRefusal>(() => feed.ArticlesAsync(From, To));

        foreach (var text in new[] { failure.Message, failure.ToString() })
        {
            Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
            Assert.DoesNotContain("api_token", text, StringComparison.Ordinal);
        }

        Assert.Null(failure.InnerException);
    }

    // The news leg of the night's weighted total is in NightlyCost, folded into
    // the assertion that exercises all five feed roles at once. It stood here
    // and backed no verdict, for the reason the note in LiveFeedTests gives.
}
