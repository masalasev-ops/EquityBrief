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

    // Members of the recorded theme's industry added to the replayed membership, since none of
    // the fixture's four members is in it.
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

    // A theme pass's searches answered from a folder a test writes: the first site's search
    // with the answer given, and every other site's with nothing, so what the pass keeps is
    // the one answer the test chose.
    static void Answered(string folder, string theme, string answer)
    {
        var queries = ThemeSearch.For(theme, ThemeNight, IndustryList());

        File.WriteAllText(Path.Combine(folder, RecordedSearchFeed.FileFor(queries[0])), answer);

        foreach (var query in queries.Skip(1))
        {
            File.WriteAllText(Path.Combine(folder, RecordedSearchFeed.FileFor(query)), """{"results":[]}""");
        }
    }

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

        // Neither member holds a facts file, so each pass writes nothing of its own and its
        // row says whether it refreshed the industry's cycle, which is the sentence the name
        // page reads.
        // see: A pass for a name with no facts file refreshes its industry's theme and says so
        var passes = expected.GetProperty("passes");

        Assert.Equal(passes.GetProperty("outcome").GetString(), first.Outcome);
        Assert.Equal(passes.GetProperty("outcome").GetString(), second.Outcome);
        Assert.Equal(passes.GetProperty("first").GetString(), first.Reason);
        Assert.Equal(passes.GetProperty("second").GetString(), second.Reason);

        // One theme pass row, under the first member's run, and none under the second's.
        Assert.Equal([$"theme-first|{ThemeResearchRunner.Written}"], Query(store, "SELECT run_id, outcome FROM run_log WHERE stage = 'theme research' ORDER BY rowid;"));

        // One record, for the theme and the industries that map to it, in the lane figure 12.2
        // puts the cycle in, each draft with the verdict the expectation reads off its recording.
        var versions = expected.GetProperty("versions").EnumerateArray().ToArray();
        var industries = JsonSerializer.Serialize(Listed(expected.GetProperty("industries")));

        Assert.Equal(
            [.. versions.Select(version => $"{theme}|{version.GetProperty("section").GetString()}|{version.GetProperty("version").GetInt32()}|{expected.GetProperty("night").GetString()}|{version.GetProperty("status").GetString()}|{industries}")],
            Query(store, "SELECT theme, section, version, as_of, status, industries FROM theme_section ORDER BY version;"));

        Assert.All(versions, version => Assert.Equal("paid", version.GetProperty("lane").GetString()));
        Assert.Equal([Providers.ResearchModelFeedTests.Shipped().Identity], Query(store, "SELECT DISTINCT model FROM theme_section;"));

        // The searches the pass asked, one a site in the list's order, each over the window the
        // file states, and what they kept: a result from a site the list does not carry dropped
        // and named, a result short of a document dropped and named, and every page kept stored
        // and admitted.
        var window = expected.GetProperty("window");

        Assert.Equal(IndustryList(), search.Asked.Select(asked => Assert.Single(asked.Domains)).ToArray());

        foreach (var asked in search.Asked)
        {
            Assert.Equal(window.GetProperty("from").GetString(), Iso(asked.From));
            Assert.Equal(window.GetProperty("to").GetString(), Iso(asked.To));

            // Section 17's four parameters on each request the pass made, read off the body the
            // feed sends for it: the industry and no member's ticker, the two dates, one site of
            // the industry list and no other, and the page's text with its publish date.
            using var sent = JsonDocument.Parse(TavilySearchFeed.Body(asked));
            var body = sent.RootElement;

            Assert.Equal(theme + " industry", body.GetProperty("query").GetString());
            Assert.All(members.Append("KEYS"), ticker => Assert.DoesNotContain(ticker, body.GetProperty("query").GetString()!, StringComparison.Ordinal));
            Assert.Equal(window.GetProperty("from").GetString(), body.GetProperty("start_date").GetString());
            Assert.Equal(window.GetProperty("to").GetString(), body.GetProperty("end_date").GetString());
            Assert.Equal([.. asked.Domains], Listed(body.GetProperty("include_domains")));
            Assert.Equal(ThemeSearch.ResultsASite, body.GetProperty("max_results").GetInt32());
            Assert.Equal(TavilySearchFeed.RawContent, body.GetProperty("include_raw_content").GetString());
            Assert.True(body.GetProperty("include_published_date").GetBoolean());
        }

        var answers = search.Asked.Select(asked => TavilySearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedSearchFeed.FileFor(asked))))).ToArray();

        Assert.Equal(expected.GetProperty("results").GetInt32(), answers.Sum(answer => answer.Results.Count));

        var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE stage = 'theme research';").Single()).RootElement;

        Assert.Equal(Listed(expected.GetProperty("offList")), Listed(detail.GetProperty("offList")));
        Assert.Equal(Listed(expected.GetProperty("shortOfADocument")), Listed(detail.GetProperty("shortOfADocument")));

        var documents = expected.GetProperty("documents");

        Assert.Equal(documents.GetProperty("stored").GetInt32().ToString(CultureInfo.InvariantCulture), Query(store, "SELECT COUNT(*) FROM source_document;").Single());
        Assert.Equal(documents.GetProperty("admitted").GetInt32().ToString(CultureInfo.InvariantCulture), Query(store, "SELECT COUNT(*) FROM source_document WHERE admissibility = 'accepted';").Single());

        // The pages kept in the order the rule keeps them, every site's first result before any
        // site's second, read off the captures by the test's own walk rather than the pass's.
        var results = new List<SearchResult>();

        for (var rank = 0; rank < ThemeSearch.ResultsASite; rank++)
        {
            results.AddRange(answers.Where(answer => rank < answer.Results.Count).Select(answer => answer.Results[rank]));
        }

        var kept = results
            .Where(result => ThemeSearch.OnList(result.Url, IndustryList()) && !ThemeSearch.ShortOfADocument(result))
            .ToArray();

        Assert.Equal(documents.GetProperty("stored").GetInt32(), kept.Length);

        // The cycle is handed the kept pages that name the industry, the ones the expectation lists
        // in the order they were kept, each carrying its opening, and each draft's prose is the
        // recording its request is keyed on, byte for byte.
        // see: A theme page is handed to the model only where its text names the industry
        var about = Listed(expected.GetProperty("handedPages"));
        var handed = kept.Where(result => about.Contains(result.Url)).ToArray();
        var ids = handed.Select(result => SourceDocuments.Id(result.Url)).ToArray();

        Assert.Equal(documents.GetProperty("handed").GetInt32(), handed.Length);
        Assert.Equal(about, handed.Select(result => result.Url));
        Assert.Equal([.. firstPaid.Asked.Select(_ => JsonSerializer.Serialize(ids))], Query(store, "SELECT source_ids FROM theme_section ORDER BY version;"));
        Assert.Equal(calls.GetProperty("paid").GetInt32(), firstPaid.Asked.Count);

        foreach (var request in firstPaid.Asked)
        {
            Assert.Equal(SectionPrompt.PaidLane, request.Lane);
            Assert.Equal(ClaimRules.CycleSection, request.Section);
            Assert.Equal(ids, request.DocumentIds);
            Assert.StartsWith("Industry: " + theme + "\n", request.Prompt, StringComparison.Ordinal);
            Assert.Contains("Facts:\n\n", request.Prompt, StringComparison.Ordinal);
        }

        // Every page handed carries its opening, and a page kept and not handed is not in the call:
        // the Federal Reserve's industrial production release, over twice the opening's length,
        // names the industry too seldom to be handed and is stored whole.
        Assert.All(handed, result => Assert.Contains(ThemeSearch.Opening(result.Text!), firstPaid.Asked[0].Prompt, StringComparison.Ordinal));

        var longest = kept.Single(result => result.Url == expected.GetProperty("longPage").GetString());

        Assert.DoesNotContain(longest.Url, about);
        Assert.DoesNotContain(ThemeSearch.Opening(longest.Text!)[..200], firstPaid.Asked[0].Prompt, StringComparison.Ordinal);
        Assert.True(
            int.Parse(Query(store, $"SELECT length(body) FROM source_document WHERE id = '{SourceDocuments.Id(longest.Url)}';").Single(), CultureInfo.InvariantCulture) > ThemeSearch.CharactersAPage * 2,
            "The stored page is its opening rather than its whole text.");

        // The first draft is asked with no refusal to answer.
        Assert.DoesNotContain("previous draft", firstPaid.Asked[0].Prompt, StringComparison.Ordinal);

        Assert.Equal(
            [.. firstPaid.Asked.Select(request => OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(request))), request.Section).Text)],
            Query(store, "SELECT prose FROM theme_section ORDER BY version;"));

        // What the calls cost, off the rows the spend cap wrote.
        Assert.Equal(
            decimal.Parse(expected.GetProperty("spend").GetString()!, CultureInfo.InvariantCulture),
            Query(store, "SELECT spend FROM run_log WHERE run_id = 'theme-first' AND stage LIKE 'research call: The industry cycle%';").Sum(spend => decimal.Parse(spend, CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void SectionSeventeenStatesTheSearchesAThemePassMakesAndThePagesItHandsAsTheCodeDoes()
    {
        // The three figures the limits row states, read off its own cell and held to the
        // constants the pass reads, so neither moves alone.
        var row = ArchitectureTables
            .In(File.ReadAllText(Repository.Architecture))
            .Single(table => table.Heading == Scope.LimitsTable)
            .Body.Single(cells => cells.Count > 1 && cells[0] == "Theme search parameters");

        Assert.Contains($"asks for that site's first {ThemeSearch.ResultsASite} results", row[1], StringComparison.Ordinal);
        Assert.Contains($"handed at most {ThemeSearch.MostPages} of the pages it admitted", row[1], StringComparison.Ordinal);
        Assert.Contains($"at least {ThemeSearch.MentionsPerTenThousand} times in every {ThemeSearch.CountedOver.ToString("N0", CultureInfo.InvariantCulture)} characters", row[1], StringComparison.Ordinal);
        Assert.Contains($"each carried as its first {ThemeSearch.CharactersAPage.ToString("N0", CultureInfo.InvariantCulture)} characters", row[1], StringComparison.Ordinal);

        // A search a site: as many searches as the list has sites, each restricted to one.
        var queries = ThemeSearch.For(FixtureReplay.RecordedTheme, ThemeNight, IndustryList());

        Assert.Equal(IndustryList().Count, queries.Count);
        Assert.All(queries, query => Assert.Single(query.Domains));
    }

    [Fact]
    public void TheAnswersAreTakenARankAtATimeAndAPageIsHandedItsOpening()
    {
        // Over constructed answers, so the order is the rule's rather than whatever the
        // captures happen to hold: the first result of every site before any site's second,
        // a page two searches returned kept once, and an answer with nothing passed over.
        static SearchResult Result(string url) => new(url, url, "a snippet", "a page", null);

        var merged = ThemeSearch.Merged(
        [
            new SearchAnswer([Result("https://a.test/1"), Result("https://a.test/2"), Result("https://a.test/3")]),
            new SearchAnswer([]),
            new SearchAnswer([Result("https://c.test/1"), Result("https://a.test/2")]),
        ]);

        Assert.Equal(["https://a.test/1", "https://c.test/1", "https://a.test/2", "https://a.test/3"], merged.Results.Select(result => result.Url));
        Assert.Empty(ThemeSearch.Merged([]).Results);

        // At most ten pages handed, in the order they were kept.
        var stored = Enumerable.Range(1, 12)
            .Select(at => new StoredDocument($"d{at}", $"https://a.test/{at}", $"page {at}", ThemeNight, DateTimeOffset.UnixEpoch, $"text {at}", Admissibility.Accepted))
            .ToArray();

        Assert.Equal(stored.Take(ThemeSearch.MostPages).Select(document => document.Id), ThemeSearch.Handed(stored).Select(document => document.Id));

        // A page longer than the opening is handed its opening and not the rest.
        var longer = new string('a', ThemeSearch.CharactersAPage) + "the rest";

        Assert.Equal(
            ThemeSearch.Opening(longer),
            Assert.Single(ThemeSearch.Handed([stored[0] with { Body = longer }])).Body);

        // A page at the opening's length is carried whole, one character longer is cut to it,
        // and a cut that would split a character in two stops before it.
        var exact = new string('a', ThemeSearch.CharactersAPage);

        Assert.Equal(exact, ThemeSearch.Opening(exact));
        Assert.Equal(exact, ThemeSearch.Opening(exact + "b"));

        var split = new string('a', ThemeSearch.CharactersAPage - 1) + "\U0001F600" + "tail";

        Assert.Equal(new string('a', ThemeSearch.CharactersAPage - 1), ThemeSearch.Opening(split));
    }

    [Fact]
    public void APageIsAboutItsIndustryWhereItsTextNamesTheIndustrysWordsFiveTimesInTenThousandCharacters()
    {
        // The industry's own words, less the separators and the words that qualify an industry
        // rather than name one, each read with a plural it may take.
        // see: A theme page is handed to the model only where its text names the industry
        Assert.Equal(["semiconductor"], ThemeSearch.IndustryWords("Semiconductors"));
        Assert.Equal(["scientific", "technical", "instrument"], ThemeSearch.IndustryWords("Scientific & Technical Instruments"));
        Assert.Equal(["drug", "manufacturer"], ThemeSearch.IndustryWords("Drug Manufacturers - General"));
        Assert.Equal(["bank"], ThemeSearch.IndustryWords("Banks - Regional"));

        // Over the title and the text together: a title of one character, a space, and the text,
        // ten thousand characters in all.
        static StoredDocument Page(string words) =>
            new("d", "https://a.test/", "t", ThemeNight, DateTimeOffset.UnixEpoch, words + " " + new string('x', 10_000 - 2 - words.Length - 1), Admissibility.Accepted);

        static string Times(string word, int count) => string.Join(" ", Enumerable.Repeat(word, count));

        Assert.Equal(10_000, ("t " + Page(Times("semiconductors", 5)).Body).Length);

        // Five in ten thousand is the floor and is kept; four is not.
        Assert.Equal(ThemeSearch.MentionsPerTenThousand, ThemeSearch.Mentions(Page(Times("semiconductors", 5)), "Semiconductors"));
        Assert.True(ThemeSearch.AboutTheIndustry(Page(Times("semiconductors", 5)), "Semiconductors"));
        Assert.False(ThemeSearch.AboutTheIndustry(Page(Times("semiconductors", 4)), "Semiconductors"));

        // A singular, a plural and any case are the industry's word, and a word it runs into is not.
        Assert.Equal(5.0, ThemeSearch.Mentions(Page("Semiconductor SEMICONDUCTORS semiconductor semiconductors Semiconductors semiconductorless"), "Semiconductors"));

        // Every word of a longer name counts toward the one floor.
        Assert.True(ThemeSearch.AboutTheIndustry(Page("scientific technical instruments instrument Scientific"), "Scientific & Technical Instruments"));
    }

    // ---- what a search keeps ----

    [Fact]
    public async Task AResultFromASiteTheListDoesNotCarryIsDroppedBeforeItsTextIsReadAndNamedOnTheRunLog()
    {
        using var store = new TemporaryStore().Migrated();

        var cap = new SpendCap(new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped()), Core.Spending.SpendCaps.Default, ResearchClock, store.DatabaseFile);

        await FixtureReplay.Themer(store, ResearchClock, cap, new ClaimChecker(ResearchClock, store.DatabaseFile)).RunAsync("Scientific & Technical Instruments", "theme-off-list");

        // The search restricted to worldsteel.org answered for this industry with three pages
        // from three other sites, none of which the list carries. None of them is in the
        // store, and the run log names each site, in the order the pass kept results.
        Assert.Equal("0", Query(store, "SELECT COUNT(*) FROM source_document WHERE url LIKE '%dictionary.com%' OR url LIKE '%science.gov%' OR url LIKE '%merriam-webster.com%';").Single());
        Assert.Equal(
            ["www.dictionary.com", "www.science.gov", "www.merriam-webster.com"],
            Listed(JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE stage = 'theme research';").Single()).RootElement.GetProperty("offList")));

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
        Answered(
            folder.Path,
            FixtureReplay.RecordedTheme,
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
        using var store = await FixtureReplay.ReplayedWholeAsync();

        // The whole replay's two theme passes, for KEYS's industry inside its pass and for
        // Semiconductors after it: the pages their searches kept, worked out from the captures
        // by the rule, each stored and each from a site the industry list carries, beside the
        // documents the name's pass read from the licensed feed and the archive, which no list
        // governs.
        var kept = new[] { "Scientific & Technical Instruments", FixtureReplay.RecordedTheme }
            .SelectMany(theme => ThemeSearch.For(theme, ThemeNight, IndustryList()))
            .SelectMany(query => TavilySearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedSearchFeed.FileFor(query)))).Results)
            .Where(result => ThemeSearch.OnList(result.Url, IndustryList()) && !ThemeSearch.ShortOfADocument(result))
            .Select(result => result.Url)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var stored = Query(store, "SELECT url FROM source_document;");
        var research = Expected("research-record");

        Assert.Equal(
            Expected("theme-record").GetProperty("documents").GetProperty("stored").GetInt32() + research.GetProperty("theme").GetProperty("stored").GetInt32(),
            kept.Length);
        Assert.All(kept, url => Assert.Contains(url, stored));
        Assert.All(kept, url => Assert.True(ThemeSearch.OnList(url, IndustryList()), $"{url} is stored from a theme search and its site is not on the industry list."));
        Assert.Equal(research.GetProperty("documents").GetProperty("fetched").GetInt32(), stored.Count - kept.Length);

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

        // The name is written from its own filings and news, every section but the cycle and the
        // cause of each large move, which the recordings answer with nothing twice, and the cycle
        // is absent with the reason.
        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(
            ClaimRules.Sections.Where(section => section != ClaimRules.CycleSection && section != ClaimRules.CauseSection).Order(StringComparer.Ordinal),
            Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS' ORDER BY section;").Order(StringComparer.Ordinal));
        Assert.Equal(
            [$"{ClaimRules.CycleSection}|{ResearchRunner.ThemeNotRefreshed}{unavailable}", $"{ClaimRules.CauseSection}|{ProseWriter.NoUsableAnswer}"],
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

        // Every section of the name's own is written and stored but the cause of each large move,
        // which the recordings answer with nothing twice, the cycle is omitted with the one line
        // saying the theme could not be refreshed, and the theme record is as it was.
        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(7, Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS';").Count);
        Assert.DoesNotContain(ClaimRules.CycleSection, Query(store, "SELECT DISTINCT section FROM research_section WHERE ticker = 'KEYS';"));
        Assert.Equal(
            [$"{ClaimRules.CycleSection}|{ResearchRunner.ThemeNotRefreshed}{refused.Line}", $"{ClaimRules.CauseSection}|{ProseWriter.NoUsableAnswer}"],
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

        // The next searches every site and runs to the end, its call answered with nothing,
        // which does close it: a pass that ran to the end, whatever it could write, is not run
        // again that day.
        var found = new RecordedSearchFeed(Folder());

        Assert.Equal(ThemeResearchRunner.Written, (await Themer(found).RunAsync("Scientific & Technical Instruments", "theme-nothing")).Outcome);
        Assert.Equal(IndustryList().Count, found.Requests);

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
        Answered(
            folder.Path,
            FixtureReplay.RecordedTheme,
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

        Answered(
            folder.Path,
            FixtureReplay.RecordedTheme,
            """
            {"results":[{"url":"https://www.semiconductors.org/sales","title":"Sales","content":"A snippet.","raw_content":"Global semiconductor sales rose again in July as demand for advanced chips kept climbing across the semiconductor industry.","published_date":"Fri, 04 Sep 2026 00:00:00 GMT"}]}
            """);

        // A page naming its industry, so it is handed. A first draft quoting a figure no facts file
        // holds, and a second in words.
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

        // Both checks under the theme's stage, the retry's with its round, so a name's pass that
        // refreshed this theme has its own two free.
        Assert.Equal(
            [ClaimChecker.ThemeStage, $"{ClaimChecker.ThemeStage}, {ResearchRunner.SecondRound}"],
            Query(store, "SELECT stage FROM run_log WHERE stage LIKE '%claims%' ORDER BY rowid;"));
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

        Assert.Equal(IndustryList().Count, asked.Requests);
        Assert.Contains(ClaimRules.CycleSection, refreshed.Warranted);
    }

    [Fact]
    public async Task ANamesPassWhoseThemeWroteItsCycleChecksItsOwnSectionsOnARowOfItsOwn()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();
        using var folder = new TemporaryDirectory();

        const string Theme = "Scientific & Technical Instruments";

        // One page the theme's search keeps, so its call is made and its cycle is written and
        // checked inside the name's pass.
        Answered(
            folder.Path,
            Theme,
            """
            {"results":[{"url":"https://www.spglobal.com/instruments","title":"Instruments","content":"A snippet.","raw_content":"Orders for test and measurement instruments kept rising as laboratories and factories spent on new equipment.","published_date":"Fri, 04 Sep 2026 00:00:00 GMT"}]}
            """);

        // The cycle in words, and the key under each figure, which is the one section written
        // from the facts file alone and so the one a name with no document still has written.
        var model = new ScriptedModel(
            "Orders for test and measurement instruments kept rising across the industry [D1].",
            "Each line on the chart is drawn from the name's own stored sessions.");

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, lane: [], paid: model, localModel: new NothingAnsweringLocal(), archive: new NoRelease(), news: new NoArticles(), search: new RecordedSearchFeed(folder.Path)).RunAsync("KEYS", "research-theme-checked");

        // A theme refreshed inside a name's pass runs its check under the name's run, and the
        // name's own check after it is a row of its own rather than a second row under one
        // stage, which the 6.11 production run found stopping MSFT's pass with nothing on its
        // row once its theme had written a cycle.
        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(
            [$"1|{ClaimChecker.Accepted}"],
            Query(store, "SELECT version, status FROM theme_section;"));
        Assert.Equal(
            [$"{ClaimRules.ComputedSection}|{ClaimChecker.Accepted}"],
            Query(store, "SELECT section, status FROM research_section WHERE ticker = 'KEYS';"));

        var stages = Query(store, "SELECT stage FROM run_log WHERE run_id = 'research-theme-checked';");

        Assert.Contains(ClaimChecker.ThemeStage, stages);
        Assert.Contains(ClaimChecker.Stage, stages);
        Assert.Contains(ResearchRunner.Stage, stages);
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
        // row counts its probe and its search a site, and the name's counts its own probe
        // beside its news and its archive requests, and not the theme's, which the 6.9 sweep
        // found no test telling apart.
        Assert.Equal(2, paid.Probes);
        Assert.Equal(
            [(IndustryList().Count + 1).ToString(CultureInfo.InvariantCulture)],
            Query(store, "SELECT network_requests FROM run_log WHERE run_id = 'research-counted' AND stage = 'theme research';"));
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
