using System.Net;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Worker;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Providers;

// DeepSeek's search tool's feed, read against the answers 21.19's trial run captured from it before its reader was
// tested: a search of one site that returned three pages, one that returned nothing, and the refusal of the whole list
// in one query.
// see: Theme material comes from a search tool, and per-name material never does
public class DeepSeekSearchFeedTests
{
    const string NotAKey = "a search key no test sends";
    const string Base = "https://search.example/";

    // The day the trial run searched for, so a query built here is the one it sent.
    static readonly DateOnly Day = new(2026, 11, 1);

    internal const string Trendforce = "deepseek-search-trendforce.json";
    internal const string Empty = "deepseek-search-empty.json";
    internal const string Refusal = "deepseek-search-refusal-400.json";

    sealed class Seeing(HttpStatusCode status, string body, List<string>? events = null) : HttpMessageHandler
    {
        public string Address { get; private set; } = string.Empty;

        public string Method { get; private set; } = string.Empty;

        public string Sent { get; private set; } = string.Empty;

        public string Scheme { get; private set; } = string.Empty;

        public string Credential { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            events?.Add("send");
            Address = request.RequestUri!.ToString();
            Method = request.Method.Method;
            Sent = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Scheme = request.Headers.Authorization?.Scheme ?? string.Empty;
            Credential = request.Headers.Authorization?.Parameter ?? string.Empty;

            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    sealed class Unreachable : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
    }

    // One attempt and no wait between attempts, the pause between requests recorded rather than slept.
    static DeepSeekSearchFeed Feed(HttpMessageHandler handler, List<string>? events = null)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NotAKey);

        return new DeepSeekSearchFeed(
            client,
            new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)), (_, _) => Task.CompletedTask),
            (gap, _) =>
            {
                events?.Add("pause " + gap.TotalMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture));

                return Task.CompletedTask;
            });
    }

    static string Captured(string file) => SearchFeedTests.Captured(file);

    [Fact]
    public async Task TheRequestNamesTheIndustryTheSiteAndTheWindowInItsQueryAndAsksForThePagesText()
    {
        // The tool takes no field for a site or a window, so the theme search's scope is in the query's own words: the
        // industry and no ticker, the one site this search is for, and the quarter, the day after its end named since
        // the tool does not say whether `before:` takes its own day. The query is the one the trial run sent.
        var industry = SearchFeedTests.Lists().Industry;
        var query = ThemeSearch.For("Semiconductors", Day, industry).Single(query => query.Domains[0] == "trendforce.com");
        var seen = new Seeing(HttpStatusCode.OK, Captured(Trendforce));

        var answer = await Feed(seen).SearchAsync(query);

        using var sent = JsonDocument.Parse(seen.Sent);
        var body = sent.RootElement;

        Assert.Equal("Semiconductors industry site:trendforce.com after:2026-08-01 before:2026-11-02", body.GetProperty("query").GetString());
        Assert.Equal(ThemeSearch.ResultsASite, body.GetProperty("count").GetInt32());
        Assert.True(body.GetProperty("need_content").GetBoolean());
        Assert.Equal(["count", "need_content", "query"], body.EnumerateObject().Select(field => field.Name).Order(StringComparer.Ordinal).ToArray());

        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            Assert.DoesNotContain(ticker, body.GetProperty("query").GetString()!, StringComparison.Ordinal);
        }

        // Posted to the search path with the key in the header, and never in the address or the body.
        Assert.Equal("POST", seen.Method);
        Assert.Equal(Base + DeepSeekSearchFeed.Path, seen.Address);
        Assert.Equal("Bearer", seen.Scheme);
        Assert.Equal(NotAKey, seen.Credential);
        Assert.DoesNotContain(NotAKey, seen.Address, StringComparison.Ordinal);
        Assert.DoesNotContain(NotAKey, seen.Sent, StringComparison.Ordinal);

        Assert.Equal(3, answer.Results.Count);

        // The hook the measurement keeps captures through is handed the request's body and the answer as they went, and
        // no header.
        var keptAs = new List<(string Sent, int Status, string Body)>();
        var keeping = new DeepSeekSearchFeed(
            new HttpClient(new Seeing(HttpStatusCode.OK, Captured(Trendforce))) { BaseAddress = new Uri(Base) },
            kept: (sent, status, body, _) =>
            {
                keptAs.Add((sent, status, body));

                return Task.CompletedTask;
            });

        await keeping.SearchAsync(query);

        var one = Assert.Single(keptAs);

        Assert.Equal(seen.Sent, one.Sent);
        Assert.Equal(200, one.Status);
        Assert.Equal(Captured(Trendforce), one.Body);
    }

    [Fact]
    public void TheCapturedAnswersAreReadAsTheToolSentThem()
    {
        var results = DeepSeekSearchFeed.Parse(Captured(Trendforce)).Results;

        // Three pages, each with its text and a date, its authority tier and its rerank score.
        Assert.Equal(3, results.Count);
        Assert.All(results, result => Assert.False(ThemeSearch.ShortOfADocument(result)));
        Assert.Equal([4136, 5855, 3768], results.Select(result => result.Text!.Length).ToArray());
        Assert.Equal([3, 3, 3], results.Select(result => result.Authority!.Value).ToArray());
        Assert.Equal(1.8122471570968628, results[0].Rerank!.Value, 12);

        // The date as sent, 16:00 UTC, read in Beijing's offset, so the publish date is the day the report's own address
        // gives it, 2026/10/14, and not the UTC day before.
        Assert.Equal(new DateTimeOffset(2026, 10, 13, 16, 0, 0, TimeSpan.Zero), results[0].Published);
        Assert.Equal(DeepSeekSearchFeed.DatedAt, results[0].Published!.Value.Offset);
        Assert.Contains("/2026/10/14/", results[0].Url, StringComparison.Ordinal);

        var intake = ThemeSearch.Of(new SearchAnswer(results), ["trendforce.com"]);

        Assert.Equal(new DateOnly(2026, 10, 14), intake.Fetched[0].PublishedOn);
        Assert.Equal([new DateOnly(2026, 10, 14), new DateOnly(2026, 9, 9), new DateOnly(2026, 9, 25)], intake.Fetched.Select(page => page.PublishedOn!.Value).ToArray());

        // A search that found nothing is an answer with no results.
        Assert.Empty(DeepSeekSearchFeed.Parse(Captured(Empty)).Results);
    }

    [Fact]
    public void AResultWithNoTextIsShortOfADocumentAndADateInAFormNobodyCapturedIsRefused()
    {
        // The captured shape with the page's text empty, as the tool sends it where need_content is false or the page
        // gives none: no text, so the pass drops it as short of a document rather than storing a snippet.
        var results = DeepSeekSearchFeed.Parse(
            """{"results":[{"rerank_score":1.2,"title":"a","url":"https://www.iea.org/a","snippet":"a snippet","summary":"a summary","content":"","site_name":"IEA","site_icon":null,"date":"2026-10-13T16:00:00+00:00","authority":4},{"rerank_score":0.4,"title":"b","url":"https://www.iea.org/b","snippet":"a snippet","summary":"","content":"a page","site_name":null,"site_icon":null,"date":null,"authority":-1}]}""").Results;

        Assert.Null(results[0].Text);
        Assert.True(ThemeSearch.ShortOfADocument(results[0]));
        Assert.Null(results[1].Published);
        Assert.Equal(-1, results[1].Authority);

        var refused = Assert.Throws<InvalidOperationException>(() =>
            DeepSeekSearchFeed.Parse("""{"results":[{"url":"https://www.iea.org/a","title":"a","snippet":"a","content":"a page","date":"2026-10-13"}]}"""));

        Assert.Contains("'2026-10-13'", refused.Message, StringComparison.Ordinal);

        // An answer with no list of results is not a search that found nothing.
        Assert.Throws<InvalidOperationException>(() => DeepSeekSearchFeed.Parse(Captured(Refusal)));
    }

    [Fact]
    public async Task ARefusalCarriesTheToolsOwnWordsAndNothingListeningIsItsOwnFailure()
    {
        var industry = SearchFeedTests.Lists().Industry;
        var whole = new SearchQuery("Semiconductors industry", Day.AddMonths(-ThemeSearch.WindowMonths), Day, industry, ThemeSearch.MostPages);

        // The whole list in one query is past the most the tool reads, which is why a search of the list together is
        // the list packed; the tool's refusal of it is in its own words, and is not one a retry changes.
        Assert.Equal(308, DeepSeekSearchFeed.Query(whole).Length);

        var refused = await Assert.ThrowsAsync<ProviderRefusal>(() => Feed(new Seeing(HttpStatusCode.BadRequest, Captured(Refusal))).SearchAsync(whole));

        Assert.Equal(
            "DeepSeek's search refused the search with status 400: 'query' must be at most 128 characters (request_id: 2d09d83e-130b-4ff6-80a9-fcad3f03030c)",
            refused.Message);
        Assert.False(refused.Transient);

        // Too many requests is a refusal a wait may change.
        Assert.True((await Assert.ThrowsAsync<ProviderRefusal>(() =>
            Feed(new Seeing(HttpStatusCode.TooManyRequests, """{"error":{"message":"rate limited"}}""")).SearchAsync(whole))).Transient);

        // Nothing listening is not a refusal.
        var gone = await Assert.ThrowsAsync<SearchToolUnavailable>(() => Feed(new Unreachable()).SearchAsync(whole));

        Assert.StartsWith("DeepSeek's search could not be reached: HttpRequestException: ", gone.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NotAKey, gone.Message, StringComparison.Ordinal);

        // A search that found nothing is neither, and is not billed.
        var empty = Feed(new Seeing(HttpStatusCode.OK, Captured(Empty)));

        Assert.Empty((await empty.SearchAsync(whole)).Results);
        Assert.Equal(0, empty.Billed);

        // And a blank key is refused by name before any request, naming where it is read.
        var blank = Assert.Throws<InvalidOperationException>(() => DeepSeekSearchFeed.Live(" "));

        Assert.Contains(ModelProfiles.Field(DeepSeekSearchFeed.KeyProfile, ModelProfiles.KeyField), blank.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFeedPausesAQuarterOfASecondBeforeEveryRequestButTheFirstSoItSendsUnderFiveASecond()
    {
        // Under five a second is a gap of more than a fifth of a second before each request after the first.
        Assert.True(DeepSeekSearchFeed.Spacing > TimeSpan.FromSeconds(1) / 5);

        var events = new List<string>();
        var feed = Feed(new Seeing(HttpStatusCode.OK, Captured(Trendforce), events), events);

        foreach (var query in ThemeSearch.For("Semiconductors", Day, SearchFeedTests.Lists().Industry).Take(4))
        {
            await feed.SearchAsync(query);
        }

        Assert.Equal(["send", "pause 250", "send", "pause 250", "send", "pause 250", "send"], events);
        Assert.Equal(4, feed.Requests);
        Assert.Equal(4, feed.Billed);
    }

    [Fact]
    public void TheListPacksIntoTheFewestQueriesTheToolTakesInTheListsOrder()
    {
        var industry = SearchFeedTests.Lists().Industry;
        var packed = DeepSeekSearchFeed.Packed("Semiconductors industry", Day.AddMonths(-ThemeSearch.WindowMonths), Day, industry, ThemeSearch.MostPages);

        // Worked by hand from the trial run's four queries, of 127, 109, 116 and 121 characters: four sites, two, three
        // and three, each one more site past the 128 the tool reads.
        Assert.Equal([4, 2, 3, 3], packed.Select(query => query.Domains.Count).ToArray());
        Assert.Equal([127, 109, 116, 121], packed.Select(query => DeepSeekSearchFeed.Query(query).Length).ToArray());
        Assert.Equal(industry, packed.SelectMany(query => query.Domains).ToArray());
        Assert.Equal(
            "Semiconductors industry site:spglobal.com OR site:eia.gov OR site:bls.gov OR site:census.gov after:2026-08-01 before:2026-11-02",
            DeepSeekSearchFeed.Query(packed[0]));
    }

    [Fact]
    public void TheKeyIsTheOneTheSecretsFileHoldsForTheDeepseekProfile()
    {
        var settings = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ModelProfiles.Field(DeepSeekSearchFeed.KeyProfile, ModelProfiles.KeyField)] = "Research",
            [ModelProfiles.KeyPath("Research")] = NotAKey,
        };

        Assert.Equal("EquityBrief:Models:Research:ApiKey", ModelProfiles.KeyPath("Research"));
        Assert.Equal(NotAKey, DeepSeekSearchFeed.KeyFrom(key => settings.GetValueOrDefault(key)));
        Assert.Null(DeepSeekSearchFeed.KeyFrom(_ => null));
    }

    [Fact]
    public void TheMeasurementCountsEachFormAsItsRulesSayWorkedByHand()
    {
        var sites = new[] { "iea.org", "eia.gov" };
        var at = new DateTimeOffset(2026, 11, 1, 7, 0, 0, TimeSpan.Zero);
        var dated = new DateTimeOffset(2026, 10, 14, 0, 0, 0, DeepSeekSearchFeed.DatedAt);
        var page = "The semiconductor industry's capacity, in a page about semiconductors and nothing else. ";

        static SearchResult Result(string url, string? text, DateTimeOffset? published) => new(url, "Semiconductors", "a snippet", text, published);

        // The first search, for iea.org: a page admitted and about the industry, the same page from the second search,
        // which a pass keeps once, a page from a site the search did not ask for, a page with no text, and one with no
        // date. The second, for eia.gov, the first page again and one dated before the window. The third refused, the
        // fourth answered with nothing.
        MeasuredToolSearch[] searches =
        [
            new(["iea.org"], new SearchAnswer(
            [
                Result("https://www.iea.org/one", page + page, dated),
                Result("https://www.reuters.com/two", page, dated),
                Result("https://www.iea.org/three", null, dated),
                Result("https://www.iea.org/four", page, null),
            ]), null),
            new(["eia.gov"], new SearchAnswer(
            [
                Result("https://www.iea.org/one", page + page, dated),
                Result("https://www.eia.gov/five", page, new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero)),
            ]), null),
            new(["iea.org", "eia.gov"], null, "DeepSeek's search refused the search with status 400."),
            new(["eia.gov"], new SearchAnswer([]), null),
        ];

        var count = ToolMeasurement.Count(SearchMeasurementRun.DeepSeek, SearchMeasurementRun.EachSite, "Semiconductors", searches, sites, new DateOnly(2026, 11, 1), at);

        Assert.Equal(4, count.Searches);
        Assert.Equal(1, count.Refused);
        Assert.Equal(1, count.Empty);

        // Six results returned. From the site its search asked for: iea.org's one, three and four, and eia.gov's five.
        // From other sites: reuters.com's, and iea.org's first page as eia.gov's search returned it. With text, all but
        // iea.org's three; with a date, all but its four.
        Assert.Equal(4, count.FromSiteAsked);
        Assert.Equal(2, count.FromOtherSites);
        Assert.Equal(5, count.WithText);
        Assert.Equal(5, count.WithDate);

        // Kept as a pass keeps them: four distinct pages on the list, the one with no text dropped before storing, so
        // three judged, of which the undated page and the one before the window are refused and one admitted.
        Assert.Equal(1, count.Admitted);
        Assert.Equal(1, count.AboutTheIndustry);
    }

    [Fact]
    public void TheThemeSearchIsAnsweredByTavilyAlone()
    {
        // DeepSeek's search did not admit more pages than Tavily's in the measurement, so the feed a name's open
        // resolves for the theme search is Tavily's, and DeepSeek's is built and reached by the measurement alone.
        var feeds = OnDemandFeeds.Live(
            null,
            NotAKey,
            "measurement@example.com",
            NotAKey,
            new LocalModelSettings("gemma-4", "http://127.0.0.1:1234/v1/", "gemma", 30, 50176, 30),
            ResearchModelFeedTests.Pinned());

        Assert.IsType<TavilySearchFeed>(feeds.Search);
    }
}
