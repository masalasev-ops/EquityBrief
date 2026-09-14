using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 6.9: the theme research runner over the fixture's recordings. One
// theme pass serving two members of one industry, what a search keeps and drops before
// anything is stored, the markers read against what the tool returned, a refresh refused at
// peak, and the two ways a theme refresh fails while a name's pass depends on it.
public partial class FixtureExpectations
{
    static readonly DateOnly ThemeNight = new(2026, 9, 8);

    // Members of the recorded theme's industry added to the replayed membership, since the
    // fixture's four members are in industries whose theme searches over the list returned
    // nothing.
    static void Members(TemporaryStore store, IReadOnlyList<string> tickers, string industry)
    {
        foreach (var ticker in tickers)
        {
            store.Execute(
                "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector, industry) " +
                $"VALUES ('GSPC', '{ticker}', '2020-01-02', NULL, '2026-09-08T21:00:00Z', 'Technology', '{industry.Replace("'", "''", StringComparison.Ordinal)}');");
        }
    }

    static IReadOnlyList<string> IndustryList() =>
        SourceLists.Read(Path.Combine(Repository.Root, SourceLists.FileName)).Industry;

    // ---- one pass, every member ----

    [Fact]
    public async Task OneThemePassWritesTheRecordEveryMemberOfItsIndustryReads()
    {
        var expected = Expected("theme-record");
        var theme = expected.GetProperty("theme").GetString()!;
        var members = Listed(expected.GetProperty("members"));

        using var store = await FixtureReplay.ReplayedForResearchAsync();

        Members(store, members, theme);

        var search = new RecordedSearchFeed(Folder());
        var firstPaid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var secondPaid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());

        // The first member's pass finds its industry's theme missing and refreshes it before
        // anything of its own; the second member's pass, the same evening, finds it accepted
        // today and neither searches nor calls for it.
        var first = await FixtureReplay.Researcher(store, ResearchClock, paid: firstPaid, archive: new NoRelease(), news: new NoArticles(), search: search).RunAsync(members[0], "theme-first");
        var second = await FixtureReplay.Researcher(store, ResearchClock, paid: secondPaid, archive: new NoRelease(), news: new NoArticles(), search: search).RunAsync(members[1], "theme-second");

        var calls = expected.GetProperty("calls");

        Assert.Equal(calls.GetProperty("searches").GetInt32(), search.Requests);
        Assert.Equal(calls.GetProperty("paid").GetInt32(), firstPaid.Requests);
        Assert.Equal(0, secondPaid.Requests);

        Assert.Contains(ClaimRules.CycleSection, first.Warranted);
        Assert.DoesNotContain(ClaimRules.CycleSection, second.Warranted);
        Assert.DoesNotContain(first.NotWritten.Concat(second.NotWritten), line => line.Section == ClaimRules.CycleSection);

        // One theme pass row, under the first member's run, and none under the second's.
        Assert.Equal([$"theme-first|{ThemeResearchRunner.Written}"], Query(store, "SELECT run_id, outcome FROM run_log WHERE stage = 'theme research' ORDER BY rowid;"));

        // One record, for the theme and the industries that map to it, in the lane figure 12.2
        // puts the cycle in, accepted.
        var version = expected.GetProperty("versions").EnumerateArray().Single();
        var industries = JsonSerializer.Serialize(Listed(expected.GetProperty("industries")));

        Assert.Equal(
            [$"{theme}|{version.GetProperty("section").GetString()}|{version.GetProperty("version").GetInt32()}|{expected.GetProperty("night").GetString()}|{version.GetProperty("status").GetString()}|{industries}"],
            Query(store, "SELECT theme, section, version, as_of, status, industries FROM theme_section;"));

        Assert.Equal("paid", version.GetProperty("lane").GetString());
        Assert.Equal([Providers.ResearchModelFeedTests.Shipped().Identity], Query(store, "SELECT model FROM theme_section;"));

        // The search the pass asked, over the window the file states, and what it kept: the
        // result from a site the list does not carry dropped and named, no result short of a
        // document, and every page it kept stored and admitted.
        var window = expected.GetProperty("window");
        var asked = search.Asked.Single();

        Assert.Equal(window.GetProperty("from").GetString(), Iso(asked.From));
        Assert.Equal(window.GetProperty("to").GetString(), Iso(asked.To));

        // Section 17's four parameters on the request the pass made, read off the body the
        // feed sends for it: the industry and no member's ticker, the two dates, the industry
        // list and no other site, and the page's text with its publish date.
        using (var sent = JsonDocument.Parse(TavilySearchFeed.Body(asked)))
        {
            var body = sent.RootElement;

            Assert.Equal(theme + " industry", body.GetProperty("query").GetString());
            Assert.All(members.Append("KEYS"), ticker => Assert.DoesNotContain(ticker, body.GetProperty("query").GetString()!, StringComparison.Ordinal));
            Assert.Equal(window.GetProperty("from").GetString(), body.GetProperty("start_date").GetString());
            Assert.Equal(window.GetProperty("to").GetString(), body.GetProperty("end_date").GetString());
            Assert.Equal(IndustryList(), Listed(body.GetProperty("include_domains")));
            Assert.Equal(TavilySearchFeed.RawContent, body.GetProperty("include_raw_content").GetString());
            Assert.True(body.GetProperty("include_published_date").GetBoolean());
        }
        Assert.Equal(expected.GetProperty("results").GetInt32(), TavilySearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedSearchFeed.FileFor(asked)))).Results.Count);

        var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE stage = 'theme research';").Single()).RootElement;

        Assert.Equal(Listed(expected.GetProperty("offList")), Listed(detail.GetProperty("offList")));
        Assert.Equal(Listed(expected.GetProperty("shortOfADocument")), Listed(detail.GetProperty("shortOfADocument")));

        var documents = expected.GetProperty("documents");

        Assert.Equal(documents.GetProperty("stored").GetInt32().ToString(CultureInfo.InvariantCulture), Query(store, "SELECT COUNT(*) FROM source_document;").Single());
        Assert.Equal(documents.GetProperty("admitted").GetInt32().ToString(CultureInfo.InvariantCulture), Query(store, "SELECT COUNT(*) FROM source_document WHERE admissibility = 'accepted';").Single());

        // The cycle is handed every page kept, in the order the tool ranked them, and its
        // prose is the recording its request is keyed on, byte for byte.
        var kept = TavilySearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedSearchFeed.FileFor(asked)))).Results
            .Where(result => ThemeSearch.OnList(result.Url, IndustryList()) && !ThemeSearch.ShortOfADocument(result))
            .Select(result => SourceDocuments.Id(result.Url))
            .ToArray();

        Assert.Equal([JsonSerializer.Serialize(kept)], Query(store, "SELECT source_ids FROM theme_section;"));

        var request = firstPaid.Asked.Single();

        Assert.Equal(SectionPrompt.PaidLane, request.Lane);
        Assert.Equal(ClaimRules.CycleSection, request.Section);
        Assert.Equal(kept, request.DocumentIds);
        Assert.StartsWith("Industry: " + theme + "\n", request.Prompt, StringComparison.Ordinal);
        Assert.Contains("Facts:\n\n", request.Prompt, StringComparison.Ordinal);

        var recorded = OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(request))), request.Section).Text;

        Assert.Equal([recorded], Query(store, "SELECT prose FROM theme_section;"));

        // What the one call cost, off the row the spend cap wrote.
        Assert.Equal(
            decimal.Parse(expected.GetProperty("spend").GetString()!, CultureInfo.InvariantCulture),
            decimal.Parse(Query(store, "SELECT spend FROM run_log WHERE run_id = 'theme-first' AND stage = 'research call: The industry cycle';").Single(), CultureInfo.InvariantCulture));
    }

    // ---- what a search keeps ----

    [Fact]
    public async Task AResultFromASiteTheListDoesNotCarryIsDroppedBeforeItsTextIsReadAndNamedOnTheRunLog()
    {
        using var store = new TemporaryStore().Migrated();

        var cap = new SpendCap(new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped()), Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);

        await FixtureReplay.Themer(store, ResearchClock, cap, new ClaimChecker(ResearchClock, store.DatabaseFile)).RunAsync(FixtureReplay.RecordedTheme, "theme-off-list");

        // The page from the research vendor the list does not carry is nowhere in the store,
        // and the run log names its site.
        Assert.Equal("0", Query(store, "SELECT COUNT(*) FROM source_document WHERE url LIKE '%mordorintelligence%';").Single());
        Assert.Equal(["www.mordorintelligence.com"], Listed(JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE stage = 'theme research';").Single()).RootElement.GetProperty("offList")));

        // Over the company search the tool answered against the company-news list: seven of
        // its ten results come from sites the list does not carry. Two of those came back
        // with no text, and they are named for their site rather than for their text, which is
        // the list's gate running before the text is read. The one result short of a document
        // is the on-list release that came back with none.
        var company = TavilySearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), Providers.SearchFeedTests.KeysightCompany)));
        var intake = ThemeSearch.Of(company, SourceLists.Read(Path.Combine(Repository.Root, SourceLists.FileName)).CompanyNews);

        Assert.Equal(
            ["finance.yahoo.com", "finance.yahoo.com", "finance.yahoo.com", "finance.yahoo.com", "www.quiverquant.com", "finance.yahoo.com", "ca.finance.yahoo.com"],
            intake.OffList);
        Assert.Equal(
            ["https://www.businesswire.com/news/home/20260721366524/en/Keysight-Addresses-Cross-Domain-Physics-Issues-That-Leave-Electronic-Designs-Vulnerable-to-Late-Stage-Failure"],
            intake.Snippets);
        Assert.Equal(["markets.ft.com", "www.prnewswire.com"], intake.Fetched.Select(document => new Uri(document.Url).Host).ToArray());
        Assert.All(intake.Fetched, document => Assert.Equal(DocumentChannel.SearchTool, document.Channel));

        // A subdomain of a listed site is that site's, and a host that merely ends with one
        // is not.
        Assert.True(ThemeSearch.OnList("https://press.spglobal.com/a", ["spglobal.com"]));
        Assert.False(ThemeSearch.OnList("https://notspglobal.com/a", ["spglobal.com"]));
    }

    [Fact]
    public async Task AResultShortOfADocumentIsNotStoredAndTheRunLogRecordsItsAddress()
    {
        using var store = new TemporaryStore().Migrated();
        using var folder = new TemporaryDirectory();

        // A search that answered with two results from sites the list carries, one with no
        // text and one whose text is shorter than its own snippet, which is what two results
        // the searches at 6.9 returned looked like.
        var query = ThemeSearch.For(FixtureReplay.RecordedTheme, ThemeNight, IndustryList());

        File.WriteAllText(
            Path.Combine(folder.Path, RecordedSearchFeed.FileFor(query)),
            """
            {"query":"Semiconductors industry","results":[
              {"url":"https://www.trendforce.com/news/a","title":"A","content":"A snippet the tool wrote about the page.","raw_content":null,"published_date":"Tue, 01 Sep 2026 00:00:00 GMT"},
              {"url":"https://www.iea.org/reports/b","title":"B","content":"A snippet longer than the text the tool could read.","raw_content":"Too short.","published_date":"Tue, 01 Sep 2026 00:00:00 GMT"}
            ]}
            """);

        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var cap = new SpendCap(paid, Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);

        var outcome = await FixtureReplay.Themer(store, ResearchClock, cap, new ClaimChecker(ResearchClock, store.DatabaseFile), new RecordedSearchFeed(folder.Path)).RunAsync(FixtureReplay.RecordedTheme, "theme-snippets");

        // Nothing stored, nothing called for, and no row a reader could take for a theme.
        Assert.Equal(ThemeResearchRunner.Written, outcome.Outcome);
        Assert.Equal("0", Query(store, "SELECT COUNT(*) FROM source_document;").Single());
        Assert.Empty(Query(store, "SELECT theme FROM theme_section;"));
        Assert.Equal(0, paid.Requests);

        // The run log records each address and that it was short of a document, and the cycle
        // is absent with the reason every candidate failed that way.
        var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE stage = 'theme research';").Single()).RootElement;

        Assert.Equal(["https://www.trendforce.com/news/a", "https://www.iea.org/reports/b"], Listed(detail.GetProperty("shortOfADocument")));
        Assert.Equal(
            [$"{ClaimRules.CycleSection}|the search returned 2 result(s), 2 {ThemeResearchRunner.ShortOfADocument} and 0 from a site the industry list does not carry, and nothing that could be stored"],
            detail.GetProperty("notWritten").EnumerateArray().Select(line => $"{line.GetProperty("section").GetString()}|{line.GetProperty("reason").GetString()}").ToArray());
    }

    [Fact]
    public async Task EveryPageAThemeSearchStoresComesFromTheListThatGovernsIt()
    {
        var expected = Expected("theme-record");

        using var store = await FixtureReplay.ReplayedWholeAsync();

        // The whole replay's theme pass: the nine pages the file states, each from a site the
        // industry list carries, beside the fourteen documents the name's pass read from the
        // licensed feed and the archive, which no list governs.
        var hosts = Query(store, "SELECT url FROM source_document;")
            .Select(url => new Uri(url))
            .Where(url => url.Host.EndsWith("semiconductors.org", StringComparison.Ordinal) || url.Host.EndsWith("mordorintelligence.com", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(expected.GetProperty("documents").GetProperty("stored").GetInt32(), hosts.Length);
        Assert.All(hosts, url => Assert.True(ThemeSearch.OnList(url.ToString(), IndustryList()), $"{url} is stored from a theme search and its site is not on the industry list."));

        // Both lists carry a review date and sit under the tool's domain limit.
        using var lists = JsonDocument.Parse(File.ReadAllText(Path.Combine(Repository.Root, SourceLists.FileName)));

        Assert.True(DateOnly.TryParseExact(lists.RootElement.GetProperty("reviewBy").GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _));
        Assert.InRange(SourceLists.Read(Path.Combine(Repository.Root, SourceLists.FileName)).CompanyNews.Count, 1, TavilySearchFeed.MostDomains);
        Assert.InRange(IndustryList().Count, 1, TavilySearchFeed.MostDomains);
    }

    // ---- the markers, against what the tool returns ----

    [Fact]
    public void TheDeniedCategoryMarkersAreReadAgainstWhatTheSearchToolReturned()
    {
        // owes: The denied-category markers tested against what the search tool returns
        var expected = Expected("search-admissibility");
        var window = expected.GetProperty("window");
        var from = DateOnly.ParseExact(window.GetProperty("from").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var to = DateOnly.ParseExact(window.GetProperty("to").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var results = new[] { Providers.SearchFeedTests.KeysightCompany, Providers.SearchFeedTests.SemiconductorsTheme }
            .SelectMany(file => TavilySearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), file))).Results)
            .ToArray();

        var verdicts = expected.GetProperty("verdicts").EnumerateObject().ToDictionary(verdict => verdict.Name, verdict => verdict.Value.GetString()!, StringComparer.Ordinal);

        // Every result the two searches returned has a verdict worked out from the page, and
        // the test reaches each of them.
        Assert.Equal(20, results.Length);
        Assert.Equal(verdicts.Keys.Order(StringComparer.Ordinal), results.Select(result => result.Url).Order(StringComparer.Ordinal));

        foreach (var result in results)
        {
            var document = new FetchedDocument(
                DocumentChannel.SearchTool,
                result.Url,
                result.Title,
                result.Published is { } published ? DateOnly.FromDateTime(published.UtcDateTime) : null,
                result.Text);

            Assert.True(
                verdicts[result.Url] == Admissibility.Judge(document, from, to),
                $"{result.Url} is judged {Admissibility.Judge(document, from, to)} and reading the page gives {verdicts[result.Url]}.");
        }

        // Both directions, counted: the refusable page refused, and no page among the rest
        // refused. The two refusable pages admitted are named in the file's readings, and
        // each is from a site no list carries, which is the gate a theme pass reaches first.
        Assert.Equal(1, verdicts.Values.Count(verdict => verdict == Admissibility.QuotePage));
        Assert.Equal(expected.GetProperty("ordinaryArticlesRefused").GetInt32(), verdicts.Values.Count(verdict => Admissibility.DeniedCategories.Contains(verdict)) - 1);

        var lists = SourceLists.Read(Path.Combine(Repository.Root, SourceLists.FileName));

        foreach (var reading in expected.GetProperty("readings").EnumerateObject().Where(reading => reading.Value.GetString()!.Contains("a person would refuse it", StringComparison.Ordinal)))
        {
            Assert.Equal(Admissibility.Accepted, verdicts[reading.Name]);
            Assert.False(ThemeSearch.OnList(reading.Name, lists.CompanyNews) || ThemeSearch.OnList(reading.Name, lists.Industry));
        }
    }

    // ---- at peak ----

    [Fact]
    public async Task AThemeRefreshAtPeakStartsNothingAndSaysWhenTheWindowCloses()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // Tuesday 02:30 UTC, inside the shipped prices' first peak window, which closes at
        // 04:00 UTC.
        var peak = new DateTimeOffset(2026, 9, 8, 2, 30, 0, TimeSpan.Zero);
        IClock atPeak = FixedClock.At(peak, SessionZones.UnitedStates);

        Assert.True(Providers.ResearchModelFeedTests.Shipped().Pricing.IsPeak(peak));

        var search = new NoResults();
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var cap = new SpendCap(paid, Core.Spending.SpendCaps.Default, atPeak, store.DatabaseFile);

        var outcome = await FixtureReplay.Themer(store, atPeak, cap, new ClaimChecker(atPeak, store.DatabaseFile), search).RunAsync(FixtureReplay.RecordedTheme, "theme-at-peak");

        var line = ThemeResearchRunner.PeakLine(new DateTimeOffset(2026, 9, 8, 4, 0, 0, TimeSpan.Zero));

        Assert.Equal(ThemeResearchRunner.AtPeak, outcome.Outcome);
        Assert.Equal("research rates are at peak, and a theme refresh runs off-peak, from 2026-09-08 04:00 UTC", line);
        Assert.Equal(line, outcome.Reason);
        Assert.Equal(0, search.Requests + paid.Probes + paid.Requests);
        Assert.Empty(Query(store, "SELECT theme FROM theme_section;"));
        Assert.Equal([$"{ThemeResearchRunner.AtPeak}|2026-09-08T02:30:00Z|0"], Query(store, "SELECT outcome, started_at, network_requests FROM run_log WHERE stage = 'theme research';"));

        // A name opened then is written without it: the cycle named with the line, and the
        // name's own pass going on to its sections.
        var name = await FixtureReplay.Researcher(store, atPeak, localModel: new NothingAnsweringLocal(), archive: new NoRelease(), news: new NoArticles(), search: new NoResults()).RunAsync("KEYS", "research-at-peak");

        Assert.Contains(name.NotWritten, written => written.Section == ClaimRules.CycleSection && written.Reason == ResearchRunner.ThemeNotRefreshed + line);
        Assert.NotEqual(ResearchRunner.Unavailable, name.Outcome);

        // And the run log's own timestamps: over the whole replay, the one paid call a theme
        // pass made started outside every peak window the configuration states.
        using var whole = await FixtureReplay.ReplayedWholeAsync();

        var started = Query(whole, "SELECT started_at FROM run_log WHERE run_id = 'replay-theme' AND stage = 'research call: The industry cycle';").Single();

        Assert.False(Providers.ResearchModelFeedTests.Shipped().Pricing.IsPeak(DateTimeOffset.Parse(started, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal)));
    }

    // ---- a refresh that fails while a name depends on it ----

    [Fact]
    public async Task ASearchToolThatDoesNotAnswerLeavesTheThemeAsItWasAndTheNameWrittenWithoutItsCycle()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // The name's industry holds a theme whose cycle was left out on an earlier day, which
        // a pass would write again.
        store.Execute(
            "INSERT INTO theme_section VALUES ('Scientific & Technical Instruments', 'The industry cycle', 1, '2026-06-01', 'a writer', 'fallback', '', '[]', 'no admissible source was found', '[\"Scientific & Technical Instruments\"]');");

        var before = Query(store, "SELECT * FROM theme_section;");
        var unavailable = "The search tool could not be reached: HttpRequestException: No such host is known.";

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, search: new RecordedSearchFeed(Folder(), unavailable)).RunAsync("KEYS", "research-search-down");

        // The theme pass did not start: the stored record is as it was, with no row added.
        Assert.Equal(before, Query(store, "SELECT * FROM theme_section;"));
        Assert.Equal([$"{ThemeResearchRunner.Unavailable}|2"], Query(store, "SELECT outcome, network_requests FROM run_log WHERE run_id = 'research-search-down' AND stage = 'theme research';"));

        // The name is written from its own filings and news, every section but the cycle, and
        // the cycle is absent with the reason.
        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(
            ClaimRules.Sections.Where(section => section != ClaimRules.CycleSection).Order(StringComparer.Ordinal),
            Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS' ORDER BY section;").Order(StringComparer.Ordinal));
        Assert.Equal(
            [$"{ClaimRules.CycleSection}|{ResearchRunner.ThemeNotRefreshed}{unavailable}"],
            outcome.NotWritten.Select(line => $"{line.Section}|{line.Reason}").ToArray());
    }

    [Fact]
    public async Task AThemeRefreshThatFailsLeavesTheNamesOtherSectionsWrittenAndTheRecordAsItWas()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A tool that answers with a refusal, in the words the captured refusal carries.
        var refused = new RefusingSearch("The search tool refused the search with status 401: Unauthorized: missing or invalid API key.");

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, search: refused).RunAsync("KEYS", "research-theme-fails");

        Assert.Equal(1, refused.Requests);
        Assert.Empty(Query(store, "SELECT theme FROM theme_section;"));

        // Every section of the name's own is written and stored, the cycle is omitted with the
        // one line saying the theme could not be refreshed, and the theme record is as it was.
        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(8, Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS';").Count);
        Assert.DoesNotContain(ClaimRules.CycleSection, Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS';"));
        Assert.Equal(
            [$"{ClaimRules.CycleSection}|{ResearchRunner.ThemeNotRefreshed}{refused.Line}"],
            outcome.NotWritten.Select(line => $"{line.Section}|{line.Reason}").ToArray());
    }

    // ---- once a day, and what closes it ----

    [Fact]
    public async Task AThemeIsResearchedOnceADayAndOnlyAPassThatRanToTheEndClosesIt()
    {
        using var store = new TemporaryStore().Migrated();

        ThemeResearchRunner Themer(ISearchFeed search, RecordedResearchModelFeed? model = null) =>
            FixtureReplay.Themer(
                store,
                ResearchClock,
                new SpendCap(model ?? new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped()), Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile),
                new ClaimChecker(ResearchClock, store.DatabaseFile),
                search);

        // A search tool that did not answer: the pass did not run to the end, so it does not
        // close the day, and the next pass that evening searches.
        var down = new RecordedSearchFeed(Folder(), "The search tool could not be reached: nothing is listening.");

        Assert.Equal(ThemeResearchRunner.Unavailable, (await Themer(down).RunAsync("Scientific & Technical Instruments", "theme-down")).Outcome);

        // The next finds nothing to write from and runs to the end, which does close it: a
        // search that found nothing is not run again that day.
        var found = new RecordedSearchFeed(Folder());

        Assert.Equal(ThemeResearchRunner.Written, (await Themer(found).RunAsync("Scientific & Technical Instruments", "theme-nothing")).Outcome);
        Assert.Equal(1, found.Requests);

        var again = new RecordedSearchFeed(Folder());
        var second = await Themer(again).RunAsync("Scientific & Technical Instruments", "theme-nothing-again");

        Assert.Equal(ThemeResearchRunner.NotWarranted, second.Outcome);
        Assert.Equal(ThemeResearchRunner.WrittenToday, second.Reason);
        Assert.Equal(0, again.Requests);

        // And a cycle a pass left accepted today, with no row saying so on the run log, as a
        // pass stopped between its insert and its row would leave it, is not bought again: the
        // theme's own rows say it was written today. The 6.9 sweep found both halves of the
        // once-a-day rule unasserted.
        store.Execute(
            "INSERT INTO theme_section VALUES ('Semiconductors', 'The industry cycle', 1, '2026-09-08', 'deepseek-flash', 'accepted', 'A cycle [D1].', '[]', NULL, '[\"Semiconductors\"]');");

        var bought = new RecordedSearchFeed(Folder());

        Assert.Equal(ThemeResearchRunner.NotWarranted, (await Themer(bought).RunAsync("Semiconductors", "theme-accepted-today")).Outcome);
        Assert.Equal(0, bought.Requests);
    }

    // ---- what the cycle is written from ----

    [Fact]
    public async Task ACycleWhoseKeptPagesWereAllRefusedIsLeftOutWithoutACall()
    {
        using var store = new TemporaryStore().Migrated();
        using var folder = new TemporaryDirectory();

        // One page from a site the list carries, with its text, published before the quarter
        // the search asked for, which admissibility refuses.
        var query = ThemeSearch.For(FixtureReplay.RecordedTheme, ThemeNight, IndustryList());

        File.WriteAllText(
            Path.Combine(folder.Path, RecordedSearchFeed.FileFor(query)),
            """
            {"results":[{"url":"https://www.semiconductors.org/an-old-report","title":"An old report","content":"A snippet.","raw_content":"A report on the industry, published long before the quarter the search asked for, and carrying enough text to be a page.","published_date":"Mon, 02 Mar 2026 00:00:00 GMT"}]}
            """);

        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var cap = new SpendCap(paid, Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);

        await FixtureReplay.Themer(store, ResearchClock, cap, new ClaimChecker(ResearchClock, store.DatabaseFile), new RecordedSearchFeed(folder.Path)).RunAsync(FixtureReplay.RecordedTheme, "theme-refused-only");

        // Stored with its refusal and no body, a cycle inserted empty citing it, no call paid
        // for, and the checker leaving it out for having no admissible source, which the 6.9
        // sweep found no test reaching.
        Assert.Equal([$"{Admissibility.OutsideTheWindow}|null"], Query(store, "SELECT admissibility, body FROM source_document;"));
        Assert.Equal(0, paid.Requests);

        var row = Query(store, "SELECT prose, status, reject_reason FROM theme_section;").Single().Split('|');

        Assert.Equal(string.Empty, row[0]);
        Assert.Equal(ClaimChecker.Fallback, row[1]);
        Assert.Contains(ClaimRules.NoAdmissibleSource, row[2], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACycleRefusedOnceIsWrittenAgainInThePassToldWhy()
    {
        using var store = new TemporaryStore().Migrated();
        using var folder = new TemporaryDirectory();

        var query = ThemeSearch.For(FixtureReplay.RecordedTheme, ThemeNight, IndustryList());

        File.WriteAllText(
            Path.Combine(folder.Path, RecordedSearchFeed.FileFor(query)),
            """
            {"results":[{"url":"https://www.semiconductors.org/sales","title":"Sales","content":"A snippet.","raw_content":"Global sales rose again in July as demand for advanced chips kept climbing across the industry.","published_date":"Fri, 04 Sep 2026 00:00:00 GMT"}]}
            """);

        // A first draft quoting a figure no facts file holds, and a second in words.
        var model = new ScriptedModel(
            "Sales across the industry rose 35.1% in the quarter [D1].",
            "Sales across the industry kept rising as demand for advanced chips climbed [D1].");

        var cap = new SpendCap(model, Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);

        var outcome = await FixtureReplay.Themer(store, ResearchClock, cap, new ClaimChecker(ResearchClock, store.DatabaseFile), new RecordedSearchFeed(folder.Path)).RunAsync(FixtureReplay.RecordedTheme, "theme-retry");

        // Two calls, the second told why the first was refused, and two versions: the first
        // refused for the figure, the second accepted. The 6.9 sweep found the retry unasserted.
        Assert.Equal(2, model.Asked.Count);
        Assert.DoesNotContain("previous draft", model.Asked[0].Prompt, StringComparison.Ordinal);
        Assert.Contains($"Your previous draft of this section was refused by the checker for: {ClaimRules.UnmatchedFigure}", model.Asked[1].Prompt, StringComparison.Ordinal);

        Assert.Equal(
            [$"1|{ClaimChecker.Rejected}", $"2|{ClaimChecker.Accepted}"],
            Query(store, "SELECT version, status FROM theme_section ORDER BY version;"));
        Assert.Equal(ThemeResearchRunner.Written, outcome.Outcome);
        Assert.Contains($"research call: {ClaimRules.CycleSection}, {ResearchRunner.SecondRound}", Query(store, "SELECT stage FROM run_log;"));
    }

    // ---- which theme a name's pass reads, and when it refreshes it ----

    [Fact]
    public async Task AMemberWithNoIndustryIsToldWhyItHasNoCycleAndNoThemeIsSearched()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        store.Execute("UPDATE membership SET industry = NULL WHERE ticker = 'KEYS';");

        var search = new NoResults();

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, localModel: new NothingAnsweringLocal(), archive: new NoRelease(), news: new NoArticles(), search: search).RunAsync("KEYS", "research-no-industry");

        Assert.Equal(0, search.Requests);
        Assert.DoesNotContain(ClaimRules.CycleSection, outcome.Warranted);
        Assert.Contains(outcome.NotWritten, line => line.Section == ClaimRules.CycleSection && line.Reason == ResearchRunner.NoIndustry);
    }

    [Fact]
    public async Task AnAcceptedThemeStandsUntilATriggerFiredForTheNameIsDatedAfterIt()
    {
        // A theme accepted in August for KEYS's industry.
        const string Theme = "INSERT INTO theme_section VALUES ('Scientific & Technical Instruments', 'The industry cycle', 1, '2026-08-01', 'deepseek-flash', 'accepted', 'A cycle [D1].', '[]', NULL, '[\"Scientific & Technical Instruments\"]');";

        // A name opened for the first time fires nothing, and reads the theme its industry
        // already holds rather than buying it again.
        using (var fresh = await FixtureReplay.ReplayedForResearchAsync())
        {
            fresh.Execute(Theme);

            var search = new NoResults();

            var outcome = await FixtureReplay.Researcher(fresh, ResearchClock, localModel: new NothingAnsweringLocal(), archive: new NoRelease(), news: new NoArticles(), search: search).RunAsync("KEYS", "research-theme-stands");

            Assert.Equal(0, search.Requests);
            Assert.DoesNotContain(ClaimRules.CycleSection, outcome.Warranted);
        }

        // A name whose own research predates its newest filing has a trigger fired for it
        // dated after the theme too, so its pass refreshes the theme. The 6.9 sweep found
        // neither direction asserted.
        using var stale = await FixtureReplay.ReplayedForResearchAsync();

        stale.Execute(Theme);
        stale.Execute("INSERT INTO research_section VALUES ('KEYS', 'What the company sells', 1, '2026-07-01', 'a writer', 'accepted', 'prose', '[]', NULL);");

        var asked = new NoResults();

        var refreshed = await FixtureReplay.Researcher(stale, ResearchClock, localModel: new NothingAnsweringLocal(), archive: new NoRelease(), news: new NoArticles(), search: asked).RunAsync("KEYS", "research-theme-refreshed");

        Assert.Equal(1, asked.Requests);
        Assert.Contains(ClaimRules.CycleSection, refreshed.Warranted);
    }

    [Fact]
    public async Task ANamesRowCountsTheRequestsItMadeAndNotItsThemes()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        var news = new RecordedNameNewsFeed(Folder());
        var archive = new RecordedFilingsArchiveFeed(Folder());
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var search = new RecordedSearchFeed(Folder());

        await FixtureReplay.Researcher(store, ResearchClock, paid: paid, archive: archive, news: news, search: search).RunAsync("KEYS", "research-counted");

        // Two probes were made through the one cap, the name's and its theme's. The theme's
        // row counts its probe and its search, and the name's counts its own probe beside its
        // news and its archive requests, and not the theme's, which the 6.9 sweep found no
        // test telling apart.
        Assert.Equal(2, paid.Probes);
        Assert.Equal(["2"], Query(store, "SELECT network_requests FROM run_log WHERE run_id = 'research-counted' AND stage = 'theme research';"));
        Assert.Equal(
            [(news.Requests + archive.Requests + 1).ToString(CultureInfo.InvariantCulture)],
            Query(store, "SELECT network_requests FROM run_log WHERE run_id = 'research-counted' AND stage = 'research';"));
    }

    // A research model that answers each call with the next of its texts, for a pass whose
    // drafts a test chooses rather than a provider recorded.
    internal sealed class ScriptedModel(params string[] texts) : IResearchModelFeed
    {
        readonly ResearchModelSettings settings = Providers.ResearchModelFeedTests.Shipped();

        public int Requests { get; private set; }

        public int Probes { get; private set; }

        public string Identity => settings.Identity;

        public List<ModelRequest> Asked { get; } = [];

        public Task<string?> UnreachableAsync(CancellationToken cancellation = default)
        {
            Probes++;

            return Task.FromResult<string?>(null);
        }

        public Task<ResearchAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Asked.Add(request);

            var text = texts[Requests++];

            return Task.FromResult(new ResearchAnswer(settings.Model, text, 100, 0, 100, 50, 0, "stop", DateTimeOffset.Parse("2026-09-08T21:10:30Z", CultureInfo.InvariantCulture)));
        }

        public decimal Price(ResearchAnswer answer) => settings.Pricing.Price(answer);

        public decimal Ceiling(ModelRequest request) => settings.Pricing.Ceiling(request, settings.AnswerTokens);
    }

    // A search tool that answers every search with a refusal.
    internal sealed class RefusingSearch(string line) : ISearchFeed
    {
        public string Line => line;

        public int Requests { get; private set; }

        public Task<SearchAnswer> SearchAsync(SearchQuery query, CancellationToken cancellation = default)
        {
            Requests++;

            throw new ProviderRefusal(line, transient: false);
        }
    }
}
