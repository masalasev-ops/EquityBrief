using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Providers;

// The search tool's feed, read against the responses captured from it before any of it was
// written: the theme search a pass builds for the one industry the fixture researches, the
// same search for the industry the list covers, a company search, and a refusal.
// see: Theme material comes from a search tool, and per-name material never does
public class SearchFeedTests
{
    const string NotAKey = "a search key no test sends";
    const string Base = "https://search.example/";

    static readonly DateOnly Night = new(2026, 9, 8);

    // The captures by what they answer. Each is named for the request's key, and the test
    // below holds the name to the request a theme pass builds.
    internal const string InstrumentsTheme = "search-150201fc99d04011ffbc59c51496a023.json";
    internal const string SemiconductorsTheme = "search-d760e4f3838af335c7eeec409c810adf.json";
    internal const string KeysightCompany = "search-d03660a12e10e32f3ce3c191af07077a.json";
    internal const string Refusal = "search-refusal-401.json";

    // The industry the fixture's replay researches beside the name's own.
    const string FixtureReplayTheme = Checks.FixtureReplay.RecordedTheme;

    internal static string Folder() => Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder);

    internal static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    internal static SourceLists Lists() => SourceLists.Read(Path.Combine(Repository.Root, SourceLists.FileName));

    sealed class Seeing(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public string Address { get; private set; } = string.Empty;

        public string Method { get; private set; } = string.Empty;

        public string Sent { get; private set; } = string.Empty;

        public string Scheme { get; private set; } = string.Empty;

        public string Credential { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
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

    // One attempt and no wait, so a failure is asserted without spending the schedule.
    static TavilySearchFeed Feed(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };

        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", NotAKey);

        return new TavilySearchFeed(
            client,
            new ProviderRequest(new RetryPolicy(1, TimeSpan.Zero, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)), (_, _) => Task.CompletedTask));
    }

    [Fact]
    public async Task TheThemeSearchIsScopedByItsFourParametersOnTheRequestTheFeedSends()
    {
        // Section 17's row asserted on the bytes the feed sent rather than on the prose that
        // describes them: the industry and not a ticker, the window's two dates, one site of the
        // industry list, and the page's text rather than a snippet. A pass makes one such
        // search for every site on the list, in the list's own order.
        var industry = Lists().Industry;
        var queries = ThemeSearch.For("Semiconductors", Night, industry);

        Assert.Equal(industry, queries.Select(query => Assert.Single(query.Domains)).ToArray());

        var seen = new Seeing(HttpStatusCode.OK, Captured(SemiconductorsTheme));
        var answer = await Feed(seen).SearchAsync(queries[1]);

        using var sent = JsonDocument.Parse(seen.Sent);
        var body = sent.RootElement;

        Assert.Equal("Semiconductors industry", body.GetProperty("query").GetString());

        foreach (var ticker in new[] { "AAPL", "KEYS", "MSFT", "NFLX" })
        {
            Assert.DoesNotContain(ticker, body.GetProperty("query").GetString()!, StringComparison.Ordinal);
        }

        // A quarter back from the night, both dates stated.
        Assert.Equal("2026-06-08", body.GetProperty("start_date").GetString());
        Assert.Equal("2026-09-08", body.GetProperty("end_date").GetString());
        Assert.Equal(Night.AddMonths(-ThemeSearch.WindowMonths).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), body.GetProperty("start_date").GetString());

        // The one site of the industry list this search is for, and nothing else.
        Assert.Equal([industry[1]], body.GetProperty("include_domains").EnumerateArray().Select(site => site.GetString()!).ToArray());

        // The page's text, its publish date, and nothing the tool writes itself.
        Assert.Equal("text", body.GetProperty("include_raw_content").GetString());
        Assert.True(body.GetProperty("include_published_date").GetBoolean());
        Assert.False(body.GetProperty("include_answer").GetBoolean());
        Assert.Equal("general", body.GetProperty("topic").GetString());
        Assert.Equal(ThemeSearch.ResultsASite, body.GetProperty("max_results").GetInt32());

        // Posted to the search path with the key in the header, and never in the address or
        // the body.
        Assert.Equal("POST", seen.Method);
        Assert.Equal(Base + TavilySearchFeed.Path, seen.Address);
        Assert.Equal("Bearer", seen.Scheme);
        Assert.Equal(NotAKey, seen.Credential);
        Assert.DoesNotContain(NotAKey, seen.Address, StringComparison.Ordinal);
        Assert.DoesNotContain(NotAKey, seen.Sent, StringComparison.Ordinal);

        Assert.Equal(10, answer.Results.Count);
    }

    [Fact]
    public void TheCapturedAnswersAreReadAsTheToolSentThem()
    {
        // The one search over the whole list for the fixture's own industry found nothing, and
        // that is an answer.
        Assert.Empty(TavilySearchFeed.Parse(Captured(InstrumentsTheme)).Results);

        // The industry the list covers: ten results, each with its page's text and a publish
        // date read with its time, one of them from a site the request did not name.
        var semiconductors = TavilySearchFeed.Parse(Captured(SemiconductorsTheme)).Results;

        Assert.Equal(10, semiconductors.Count);
        Assert.All(semiconductors, result => Assert.NotNull(result.Text));
        Assert.All(semiconductors, result => Assert.NotNull(result.Published));
        Assert.Equal(new DateTimeOffset(2026, 7, 10, 0, 0, 0, TimeSpan.Zero), semiconductors[0].Published);
        Assert.Contains(semiconductors, result => result.Published == new DateTimeOffset(2026, 7, 27, 9, 0, 35, TimeSpan.Zero));
        Assert.Equal(["www.mordorintelligence.com"], semiconductors.Select(result => new Uri(result.Url).Host).Where(host => !host.EndsWith("semiconductors.org", StringComparison.Ordinal)).ToArray());

        // The company search: three results came back with no text although the request
        // asked for it, read off the capture as null rather than as an empty page.
        var company = TavilySearchFeed.Parse(Captured(KeysightCompany)).Results;

        Assert.Equal(10, company.Count);
        Assert.Equal(
            [
                "https://finance.yahoo.com/technology/articles/why-keysight-technologies-keys-6-011045642.html",
                "https://finance.yahoo.com/technology/ai/articles/keysight-keys-raised-outlook-ai-224547726.html",
                "https://www.businesswire.com/news/home/20260721366524/en/Keysight-Addresses-Cross-Domain-Physics-Issues-That-Leave-Electronic-Designs-Vulnerable-to-Late-Stage-Failure",
            ],
            company.Where(result => result.Text is null).Select(result => result.Url).ToArray());
    }

    [Fact]
    public async Task ARefusalCarriesTheToolsOwnWordsAndNothingListeningIsItsOwnFailure()
    {
        // A refusal is an answer, in the words the tool writes under its detail.
        var refused = await Assert.ThrowsAsync<ProviderRefusal>(() =>
            Feed(new Seeing(HttpStatusCode.Unauthorized, Captured(Refusal))).SearchAsync(ThemeSearch.For("Semiconductors", Night, Lists().Industry)[0]));

        Assert.Equal("The search tool refused the search with status 401: Unauthorized: missing or invalid API key.", refused.Message);
        Assert.False(refused.Transient);

        // Nothing listening is not a refusal, and only the feed can tell the two apart.
        var gone = await Assert.ThrowsAsync<SearchToolUnavailable>(() =>
            Feed(new Unreachable()).SearchAsync(ThemeSearch.For("Semiconductors", Night, Lists().Industry)[0]));

        Assert.StartsWith("The search tool could not be reached: HttpRequestException: ", gone.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(NotAKey, gone.Message, StringComparison.Ordinal);

        // A search that found nothing is neither.
        var nothing = await Feed(new Seeing(HttpStatusCode.OK, Captured(InstrumentsTheme))).SearchAsync(ThemeSearch.For("Scientific & Technical Instruments", Night, Lists().Industry)[0]);

        Assert.Empty(nothing.Results);

        // And a blank key is refused by name before any request.
        var blank = Assert.Throws<InvalidOperationException>(() => TavilySearchFeed.Live(" "));

        Assert.Contains(TavilySearchFeed.ApiKeyName, blank.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRecordingAnswersTheSearchesAPassBuildsAndRefusesAnyOtherByName()
    {
        var industry = Lists().Industry;
        var from = Night.AddMonths(-ThemeSearch.WindowMonths);

        // Every search a theme pass builds for the two industries the fixture's replay researches
        // has a capture named for its request, so a pass over the fixture reads these files and
        // no others.
        foreach (var theme in new[] { "Scientific & Technical Instruments", FixtureReplayTheme })
        {
            Assert.All(
                ThemeSearch.For(theme, Night, industry),
                query => Assert.True(File.Exists(Path.Combine(Folder(), RecordedSearchFeed.FileFor(query))), $"No capture answers the search for {theme} on {query.Domains[0]}."));
        }

        // The captures of the one search over the whole list, which a pass made until 6.11, and
        // the company search, each named for the request it answered.
        Assert.Equal(InstrumentsTheme, RecordedSearchFeed.FileFor(new SearchQuery("Scientific & Technical Instruments industry", from, Night, industry, 10)));
        Assert.Equal(SemiconductorsTheme, RecordedSearchFeed.FileFor(new SearchQuery("Semiconductors industry", from, Night, industry, 10)));
        Assert.Equal(KeysightCompany, RecordedSearchFeed.FileFor(new SearchQuery("Keysight Technologies", from, Night, Lists().CompanyNews, 10)));

        var recorded = new RecordedSearchFeed(Folder());
        var statista = ThemeSearch.For(FixtureReplayTheme, Night, industry).Single(query => query.Domains[0] == "statista.com");

        Assert.Equal(ThemeSearch.ResultsASite, (await recorded.SearchAsync(statista)).Results.Count);
        Assert.Single(recorded.Asked);

        // A day later is a window nobody recorded, refused rather than answered.
        var later = await Assert.ThrowsAsync<InvalidOperationException>(() => recorded.SearchAsync(ThemeSearch.For(FixtureReplayTheme, Night.AddDays(1), industry)[0]));

        Assert.StartsWith("No recording answers the search for 'Semiconductors industry'", later.Message, StringComparison.Ordinal);
        Assert.Equal(2, recorded.Requests);

        // And a recording standing for a tool that does not answer says so.
        var down = new RecordedSearchFeed(Folder(), "The search tool could not be reached: nothing is listening.");

        await Assert.ThrowsAsync<SearchToolUnavailable>(() => down.SearchAsync(statista));
    }

    [Fact]
    public void APublishDateInAFormNobodyCapturedIsRefusedByName()
    {
        // Every captured result carries an HTTP date. A calendar date is a shape nobody
        // captured, and it is refused rather than read.
        var refused = Assert.Throws<InvalidOperationException>(() =>
            TavilySearchFeed.Parse("""{"results":[{"url":"https://www.semiconductors.org/a","title":"a","content":"a","raw_content":"a page","published_date":"2026-07-10"}]}"""));

        Assert.Contains("'2026-07-10'", refused.Message, StringComparison.Ordinal);

        // An answer with no list of results is not a search that found nothing.
        Assert.Throws<InvalidOperationException>(() => TavilySearchFeed.Parse("""{"detail":{"error":"a refusal read as an answer"}}"""));
    }

    [Fact]
    public void AnEmptyPageTextIsReadAsNoTextAtAll()
    {
        // The captures carry a missing page text as null. An empty or blank one is the same
        // absence written another way, and a page of nothing is not a document a pass could
        // store, which the 6.9 sweep found no test reading.
        var results = TavilySearchFeed.Parse(
            """{"results":[{"url":"https://www.iea.org/a","title":"a","content":"a snippet","raw_content":"","published_date":"Tue, 01 Sep 2026 00:00:00 GMT"},{"url":"https://www.iea.org/b","title":"b","content":"a snippet","raw_content":"   ","published_date":"Tue, 01 Sep 2026 00:00:00 GMT"}]}""").Results;

        Assert.All(results, result => Assert.Null(result.Text));
        Assert.All(results, result => Assert.True(ThemeSearch.ShortOfADocument(result)));
    }

    [Fact]
    public void AListFileWithNoSitesIsRefusedByName()
    {
        // A search restricted to no site returns nothing, which would read as an industry
        // nobody writes about, so a file without a list is refused rather than read. The
        // 6.9 sweep found only the committed file read, which carries both lists.
        using var folder = new TemporaryDirectory();

        var file = Path.Combine(folder.Path, SourceLists.FileName);

        File.WriteAllText(file, """{"companyNews":{"sites":["reuters.com"]},"industry":{"sites":[]}}""");

        var refused = Assert.Throws<InvalidOperationException>(() => SourceLists.Read(file));

        Assert.Contains("'industry'", refused.Message, StringComparison.Ordinal);

        File.WriteAllText(file, """{"companyNews":{"sites":["reuters.com",""]},"industry":{"sites":["iea.org"]}}""");

        Assert.Contains("'companyNews'", Assert.Throws<InvalidOperationException>(() => SourceLists.Read(file)).Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnOpensFeedsHoldTheSearchToolAndAFixtureOneReachesNoNetwork()
    {
        var feeds = OnDemandFeeds.FromFixture(Folder(), ResearchModelFeedTests.Shipped());

        Assert.IsType<RecordedSearchFeed>(feeds.Search);
        Assert.False(feeds.ReachesTheNetwork);

        // A live set with the search tool live is one that reaches the network, however the
        // other five were resolved.
        Assert.True((feeds with { Search = TavilySearchFeed.Live(NotAKey) }).ReachesTheNetwork);
    }
}
