using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 6.8: the research runner over the fixture's recordings. One
// pass for KEYS, what it fetched, what it handed each section, which lane wrote each
// and what the checker made of it, reproduced from the recorded endpoints; the same
// evidence asked both ways, which is the lane comparison; and the ways a pass does not
// run, each read off the run log.
public partial class FixtureExpectations
{
    static readonly DateTimeOffset ResearchNight = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);

    static IClock ResearchClock => FixedClock.At(ResearchNight, SessionZones.UnitedStates);

    // ---- the pass, from the recordings ----

    [Fact]
    public async Task ThePassWritesTheResearchRecordFromTheRecordingsByteForByte()
    {
        var expected = Expected("research-record");
        var local = new RecordedLocalModelFeed(Folder());
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());

        using var store = await FixtureReplay.ResearchedAsync(local: local, paid: paid);

        var ticker = expected.GetProperty("ticker").GetString()!;

        // Every version the pass stored, with the lane that wrote it and the checker's
        // verdict, against the file. The lane is read off the model a row records.
        Assert.Equal(
            [.. expected.GetProperty("versions").EnumerateArray().Select(version =>
                $"{version.GetProperty("section").GetString()}|{version.GetProperty("version").GetInt32()}|{version.GetProperty("lane").GetString()}|{version.GetProperty("status").GetString()}")],
            Query(store, $"SELECT section, version, CASE model WHEN '{LocalModelSettings.DefaultModel}' THEN 'local' ELSE 'paid' END, status FROM research_section WHERE ticker = '{ticker}' ORDER BY section, version;"));

        // Byte for byte: each stored draft is the text of the recording its request is
        // keyed on, in the order each lane asked, and nothing reached a network.
        var drafts = Query(store, $"SELECT section, version, prose FROM research_section WHERE ticker = '{ticker}' ORDER BY section, version;")
            .Select(row => row.Split('|', 3))
            .ToLookup(row => row[0], row => row[2]);

        // The theme's call is the theme's, made inside the pass for the name's industry, and is
        // read apart from the name's own below.
        var own = paid.Asked.Where(request => request.Section != ClaimRules.CycleSection).ToArray();
        var theme = expected.GetProperty("theme");

        var answered = local.Asked
            .Select(request => (request.Section, Text: OpenAiCompatibleModelFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedLocalModelFeed.FileFor(request))), request.Section).Text))
            .Concat(own.Select(request => (request.Section, Text: OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(request))), request.Section).Text)))
            .ToLookup(answer => answer.Section, answer => answer.Text);

        foreach (var section in drafts)
        {
            Assert.Equal([.. answered[section.Key]], [.. section]);
        }

        Assert.Equal(expected.GetProperty("modelCalls").GetProperty("local").GetInt32(), local.Requests);
        Assert.Equal(expected.GetProperty("modelCalls").GetProperty("paid").GetInt32(), own.Length);

        // The theme's one call, over the ten pages it was handed, answered with nothing: the
        // recording carries no answer, and the pass stored no cycle.
        var cycle = Assert.Single(paid.Asked, request => request.Section == ClaimRules.CycleSection);

        Assert.Equal(theme.GetProperty("paid").GetInt32(), paid.Asked.Count(request => request.Section == ClaimRules.CycleSection));
        Assert.Equal(theme.GetProperty("handed").GetInt32(), cycle.DocumentIds.Count);
        Assert.Throws<UnusableResearchAnswer>(() => OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(cycle))), cycle.Section));
        Assert.Empty(Query(store, "SELECT theme FROM theme_section;"));

        // Every request the paid lane made was asked in the paid lane, of the configured
        // model, and every one the local lane made in the local lane: the lanes, derived.
        var lanes = expected.GetProperty("lanes");

        Assert.All(paid.Asked, request => Assert.Equal(SectionPrompt.PaidLane, request.Lane));
        Assert.All(local.Asked, request => Assert.Equal(SectionPrompt.Lane, request.Lane));
        Assert.Equal(Listed(lanes.GetProperty("paid")).Order(StringComparer.Ordinal), own.Select(request => request.Section).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(Listed(lanes.GetProperty("local")).Order(StringComparer.Ordinal), local.Asked.Select(request => request.Section).Distinct().Order(StringComparer.Ordinal));
        Assert.Equal(Listed(lanes.GetProperty("local")), ProseWriter.DefaultLane);

        // What was fetched, admitted and refused, and the company's own filing among it, beside
        // the pages the theme's searches kept, which are stored in the same store.
        var documents = expected.GetProperty("documents");
        var themeRow = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'replay-research' AND stage = 'theme research';").Single()).RootElement;

        Assert.Equal(theme.GetProperty("theme").GetString(), themeRow.GetProperty("theme").GetString());
        Assert.Equal(theme.GetProperty("stored").GetInt32(), themeRow.GetProperty("fetched").GetInt32());
        Assert.Equal(theme.GetProperty("admitted").GetInt32(), themeRow.GetProperty("admitted").GetInt32());
        Assert.Equal([(theme.GetProperty("searches").GetInt32() + 1).ToString(CultureInfo.InvariantCulture)], Query(store, "SELECT network_requests FROM run_log WHERE run_id = 'replay-research' AND stage = 'theme research';"));

        Assert.Equal((documents.GetProperty("fetched").GetInt32() + theme.GetProperty("stored").GetInt32()).ToString(CultureInfo.InvariantCulture), Query(store, "SELECT COUNT(*) FROM source_document;").Single());
        Assert.Equal((documents.GetProperty("admitted").GetInt32() + theme.GetProperty("admitted").GetInt32()).ToString(CultureInfo.InvariantCulture), Query(store, "SELECT COUNT(*) FROM source_document WHERE admissibility = 'accepted';").Single());
        Assert.Equal(
            [.. documents.GetProperty("refused").EnumerateObject().Select(refused => $"{refused.Name}|{refused.Value.GetInt32()}")],
            Query(store, "SELECT admissibility, COUNT(*) FROM source_document WHERE admissibility != 'accepted' GROUP BY admissibility ORDER BY admissibility;"));
        // A refused document keeps no body. The pass reads no refusal from the capture since 6.11: the
        // one the capture holds, an AI-written summary dated 2026-07-09, is inside no move and before
        // the release, and a pass reads only those windows.
        Assert.All(Query(store, "SELECT IFNULL(body, 'null') FROM source_document WHERE admissibility != 'accepted';"), body => Assert.Equal("null", body));
        Assert.Single(Query(store, $"SELECT id FROM source_document WHERE title = '{documents.GetProperty("ownFiling").GetString()}' AND url LIKE 'https://www.sec.gov/%' AND published_on = '2026-08-18';"));

        // What each section was handed, in order, read off the source list each version stored.
        var handed = expected.GetProperty("handed");

        string[] Titles(string section) =>
        [
            .. JsonDocument.Parse(Query(store, $"SELECT source_ids FROM research_section WHERE ticker = '{ticker}' AND section = '{section}' AND version = 1;").Single()).RootElement
                .EnumerateArray()
                .Select(id => Query(store, $"SELECT title FROM source_document WHERE id = '{id.GetString()}';").Single()),
        ];

        Assert.Equal(Listed(handed.GetProperty("The cause of each large move")), Titles("The cause of each large move"));
        Assert.Equal(Listed(handed.GetProperty("What the company sells")), Titles("What the company sells"));
        Assert.Equal(Listed(handed.GetProperty("The segment commentary")), Titles("The segment commentary"));

        foreach (var section in Evidence.AcrossTheEvidence)
        {
            Assert.Equal(Listed(handed.GetProperty("acrossTheEvidence")), Titles(section));
        }

        // The section with nothing to rest on, named, and the pass's stages in the order
        // its rows landed, one run.
        var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'replay-research' AND stage = 'research';").Single()).RootElement;

        Assert.Equal(
            [.. expected.GetProperty("notWritten").EnumerateObject().Select(line => $"{line.Name}|{line.Value.GetString()}")],
            detail.GetProperty("notWritten").EnumerateArray().Select(line => $"{line.GetProperty("section").GetString()}|{line.GetProperty("reason").GetString()}").ToArray());

        Assert.Equal(Listed(expected.GetProperty("stages")), Query(store, "SELECT stage FROM run_log WHERE run_id = 'replay-research' ORDER BY rowid;"));

        // What the pass spent on the name's own sections and on its theme, off the rows the
        // spend cap wrote. The theme's refused call was billed, and its row carries the price.
        Assert.Equal(
            decimal.Parse(expected.GetProperty("spend").GetString()!, CultureInfo.InvariantCulture),
            Query(store, $"SELECT spend FROM run_log WHERE run_id = 'replay-research' AND stage NOT LIKE 'research call: {ClaimRules.CycleSection}%';").Sum(spend => decimal.Parse(spend, CultureInfo.InvariantCulture)));
        Assert.Equal(
            decimal.Parse(theme.GetProperty("spend").GetString()!, CultureInfo.InvariantCulture),
            Query(store, $"SELECT spend FROM run_log WHERE run_id = 'replay-research' AND stage LIKE 'research call: {ClaimRules.CycleSection}%';").Sum(spend => decimal.Parse(spend, CultureInfo.InvariantCulture)));

        // The window it read, which is the stored year to the night.
        var window = expected.GetProperty("window");

        Assert.Equal(expected.GetProperty("night").GetString(), ResearchClock.SessionDateAt(ResearchNight).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(window.GetProperty("from").GetString(), ResearchClock.SessionDateAt(ResearchNight).AddYears(-ResearchRunner.WindowYears).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Assert.Equal(window.GetProperty("to").GetString(), expected.GetProperty("night").GetString());
    }

    [Fact]
    public async Task OpeningANameTwiceRunsOnePassReadOffTheRunLog()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var local = new RecordedLocalModelFeed(Folder());

        var sectionsBefore = Query(store, "SELECT COUNT(*) FROM research_section;").Single();
        var documentsBefore = Query(store, "SELECT COUNT(*) FROM source_document;").Single();

        var second = await FixtureReplay.Researcher(store, ResearchClock, local: local, paid: paid).RunAsync("KEYS", "replay-research-again");

        // The second open wrote nothing, asked nothing and fetched nothing, and the run log
        // says so: one pass row saying it was not warranted, and no paid call row.
        Assert.Equal(ResearchRunner.NotWarranted, second.Outcome);
        Assert.Equal(
            [$"{ResearchRunner.Stage}|{ResearchRunner.NotWarranted}", "staleness|ok"],
            Query(store, "SELECT stage, outcome FROM run_log WHERE run_id = 'replay-research-again' ORDER BY stage;"));
        Assert.Equal("0", Query(store, "SELECT COUNT(*) FROM run_log WHERE run_id = 'replay-research-again' AND stage LIKE 'research call:%';").Single());
        Assert.Equal(sectionsBefore, Query(store, "SELECT COUNT(*) FROM research_section;").Single());
        Assert.Equal(documentsBefore, Query(store, "SELECT COUNT(*) FROM source_document;").Single());
        Assert.Equal(0, paid.Requests + paid.Probes + local.Requests);

        // Over the whole store, one research pass row that wrote anything for the name.
        Assert.Equal(["ok"], Query(store, "SELECT outcome FROM run_log WHERE stage = 'research' AND outcome = 'ok';"));
    }

    [Fact]
    public async Task ANameWhosePassFoundNothingToWriteFromIsNotFetchedForAgainOnTheSameDay()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A year with no article and no release: the pass writes the key under each
        // figure, which rests on the facts file alone, and hands every other section
        // nothing, so none of them gets a row a second open could read as written.
        var first = await FixtureReplay.Researcher(store, ResearchClock, archive: new NoRelease(), news: new NoArticles()).RunAsync("KEYS", "research-nothing");

        Assert.Equal(ResearchRunner.Written, first.Outcome);
        Assert.Equal(["The key under each figure"], first.Written.Select(section => section.Section).Distinct().ToArray());
        Assert.Equal(
            ["What the company sells", "The segment commentary", "The cause of each large move", "The dated calendar items", "The two cases", "The risks, each with what would confirm it", "The short version"],
            first.NotWritten.Where(line => line.Reason == ProseWriter.NothingHanded).Select(line => line.Section).ToArray());

        // Opened again the same day: nothing fetched, nothing asked, and the pass's row
        // says why and counts no request.
        var news = new NoArticles();
        var archive = new NoRelease();
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());

        var again = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, archive: archive, news: news).RunAsync("KEYS", "research-nothing-again");

        Assert.Equal(ResearchRunner.NotWarranted, again.Outcome);
        Assert.Equal(ResearchRunner.RanToday, again.Reason);
        Assert.Equal(0, news.Requests + archive.Requests + paid.Probes + paid.Requests);
        Assert.Equal([$"{ResearchRunner.NotWarranted}|0"], Query(store, "SELECT outcome, network_requests FROM run_log WHERE run_id = 'research-nothing-again' AND stage = 'research';"));

        // The page's explicit ask still starts a pass, which fetches again, and what it
        // writes is still decided section by section: the key accepted today is not paid for.
        var asked = new NoArticles();
        var paidForLocal = await FixtureReplay.Researcher(store, ResearchClock, archive: new NoRelease(), news: asked)
            .RunAsync("KEYS", "research-nothing-paid-for-local", new ResearchPassRequest(PaidForLocal: true));

        Assert.Equal(ResearchRunner.Written, paidForLocal.Outcome);

        // One request a window: with no release, the moves' spans and the quarter back from the
        // night, overlapping spans once.
        var facts = FactsFile.Read(Query(store, "SELECT payload FROM facts WHERE ticker = 'KEYS' AND session_date = '2026-09-08';").Single());

        Assert.Equal(NewsWindows.For(MoveWindows.In(facts), null, new DateOnly(2026, 9, 8)).Count, asked.Requests);
        Assert.DoesNotContain("The key under each figure", paidForLocal.Warranted);
        Assert.Empty(paidForLocal.Written);

        // And a day later a plain open starts one, since the day's pass is what closed it.
        var nextDay = new NoArticles();
        var tomorrow = await FixtureReplay.Researcher(store, FixedClock.At(ResearchNight.AddDays(1), SessionZones.UnitedStates), archive: new NoRelease(), news: nextDay, search: new NoResults())
            .RunAsync("KEYS", "research-nothing-next-day");

        Assert.NotEqual(ResearchRunner.RanToday, tomorrow.Reason);
        Assert.Equal(NewsWindows.For(MoveWindows.In(facts), null, new DateOnly(2026, 9, 9)).Count, nextDay.Requests);
    }

    [Fact]
    public async Task APassIsWarrantedForTheSectionsItsNewestVersionsLeaveUnwrittenOrStaleAndForNoOther()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // One section in each state the rule reads, for a pass on 2026-09-08. The newest
        // filing the store holds for the name is dated after 2026-08-01, so a section
        // accepted on that day has gone stale, and one accepted on the pass's own day has not.
        store.Execute(
            "INSERT INTO research_section VALUES " +
            "('KEYS', 'What the company sells', 1, '2026-09-08', 'a writer', 'accepted', 'prose', '[]', NULL), " +
            "('KEYS', 'The segment commentary', 1, '2026-09-01', 'a writer', 'pending', 'prose', '[]', NULL), " +
            "('KEYS', 'The key under each figure', 1, '2026-09-08', 'a writer', 'fallback', 'prose', '[]', 'left out'), " +
            "('KEYS', 'The dated calendar items', 1, '2026-09-01', 'a writer', 'fallback', 'prose', '[]', 'left out'), " +
            "('KEYS', 'The two cases', 1, '2026-09-08', 'a writer', 'rejected', 'prose', '[]', 'refused'), " +
            "('KEYS', 'The risks, each with what would confirm it', 1, '2026-08-01', 'a writer', 'accepted', 'prose', '[]', NULL), " +
            "('KEYS', 'The short version', 1, '2026-09-08', 'a writer', 'accepted', 'prose', '[]', NULL);");

        // A research model that does not answer, so the pass records what it warranted
        // and stops before it fetches or writes anything.
        var unreachable = "The research model could not be reached: nothing is listening.";
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped(), unreachable);

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, paid: paid).RunAsync("KEYS", "research-warranted");

        // Warranted: never written, left out on an earlier day, refused, and accepted and
        // gone stale. Not: accepted today, waiting on the checker, left out today. The
        // industry cycle is the theme's, and no pass has written the theme the name's
        // industry is, so it is warranted by the same rule and the pass would refresh the
        // theme before anything of the name's own.
        string[] warranted = ["The cause of each large move", "The industry cycle", "The dated calendar items", "The two cases", "The risks, each with what would confirm it"];

        Assert.Equal(ResearchRunner.Unavailable, outcome.Outcome);
        Assert.Equal(warranted, outcome.Warranted);

        var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'research-warranted' AND stage = 'research';").Single()).RootElement;

        Assert.Equal(warranted, detail.GetProperty("warranted").EnumerateArray().Select(section => section.GetString()!).ToArray());
        // The pass did not start, so the theme was not refreshed either and nothing is named
        // as not written: a pass that does not start writes nothing of the theme's.
        Assert.Empty(detail.GetProperty("notWritten").EnumerateArray());
        Assert.Empty(Query(store, "SELECT theme FROM theme_section;"));

        // The stale one is stale for the reason the rule gives, read off the judge's own row
        // against the newest filing the store holds, which is after 2026-08-01 and on or
        // before the night.
        var filed = Query(store, "SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'KEYS';").Single();

        Assert.InRange(string.CompareOrdinal(filed, "2026-08-01"), 1, int.MaxValue);
        Assert.InRange(string.CompareOrdinal(filed, "2026-09-08"), int.MinValue, 0);
        Assert.Contains(
            $"a filing dated {filed} arrived after it was written",
            Query(store, "SELECT detail FROM run_log WHERE run_id = 'research-warranted' AND stage = 'staleness';").Single(),
            StringComparison.Ordinal);

        // A pass the research model did not answer did not run to the end, so it does not
        // close the day: an open the same day once the model answers starts one and fetches,
        // which the 6.8 sweep found no test showing.
        var news = new NoArticles();
        var later = await FixtureReplay.Researcher(store, ResearchClock, archive: new NoRelease(), news: news).RunAsync("KEYS", "research-warranted-answered");

        Assert.NotEqual(ResearchRunner.NotWarranted, later.Outcome);
        Assert.NotEqual(0, news.Requests);

        // A rewrite asked the same day writes what went stale and nothing accepted that day,
        // so the rewrite control pays for no section twice in a day. The judge makes every
        // accepted section stale for a rewrite, which is what leaves this to the rule.
        var rewrite = await FixtureReplay.Researcher(store, ResearchClock, paid: new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped(), unreachable))
            .RunAsync("KEYS", "research-warranted-rewrite", new ResearchPassRequest(Refresh: true));

        Assert.Equal(warranted, rewrite.Warranted);
    }

    [Fact]
    public async Task ASectionHandedOnlyRefusedDocumentsIsLeftToTheCheckerWithoutACall()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // One article inside the largest move whose text could not be retrieved, and no
        // release, so every section resting on a document is handed only a refusal.
        var session = Query(store, "SELECT session_date FROM move WHERE ticker = 'KEYS' ORDER BY rank LIMIT 1;").Single();
        var article = new NewsArticle(
            DateTimeOffset.Parse(session + "T15:00:00Z", CultureInfo.InvariantCulture),
            "Keysight shares move",
            "https://example.test/keysight-move",
            "example.test",
            ["KEYS.US"],
            string.Empty);

        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());

        // The theme's searches find nothing, so its call is not what the count below reads.
        var outcome = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, archive: new NoRelease(), news: new Articles([article]), search: new NoResults())
            .RunAsync("KEYS", "research-only-refused");

        // No paid call: a section with nothing admitted is stored citing what it was handed,
        // and the checker leaves it out, which code knew before a call could be paid for.
        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(0, paid.Requests);

        var refused = Query(store, "SELECT id, admissibility FROM source_document;").Single().Split('|');

        Assert.Equal(Admissibility.NoText, refused[1]);

        foreach (var section in new[] { "The cause of each large move", "The dated calendar items", "The two cases", "The risks, each with what would confirm it", "The short version" })
        {
            var row = Query(store, $"SELECT prose, source_ids, status, reject_reason FROM research_section WHERE ticker = 'KEYS' AND section = '{section}';").Single().Split('|');

            Assert.Equal(string.Empty, row[0]);
            Assert.Equal($"[\"{refused[0]}\"]", row[1]);
            Assert.Equal(ClaimChecker.Fallback, row[2]);
            Assert.Contains(ClaimRules.NoAdmissibleSource, row[3], StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AResearchModelThatDoesNotAnswerStopsThePassBeforeItFetchesAnything()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var unreachable = "The research model could not be reached: No connection could be made because the target machine actively refused it.";
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped(), unreachable);
        var local = new RecordedLocalModelFeed(Folder());

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, local: local, paid: paid).RunAsync("KEYS", "research-unreachable");

        Assert.Equal(ResearchRunner.Unavailable, outcome.Outcome);
        Assert.Equal(unreachable, outcome.Reason);
        Assert.Equal(1, paid.Probes);
        Assert.Equal(0, paid.Requests + local.Requests);

        // Nothing fetched, nothing stored, and the one row that says why.
        Assert.Equal("0", Query(store, "SELECT COUNT(*) FROM source_document;").Single());
        Assert.Equal("0", Query(store, "SELECT COUNT(*) FROM research_section WHERE ticker = 'KEYS';").Single());
        Assert.Equal(["research|unavailable|1", "staleness|ok|0"], Query(store, "SELECT stage, outcome, network_requests FROM run_log WHERE run_id = 'research-unreachable' ORDER BY stage;"));
        Assert.Contains(unreachable.Replace("'", "\\u0027", StringComparison.Ordinal)[..40], Query(store, "SELECT detail FROM run_log WHERE run_id = 'research-unreachable' AND stage = 'research';").Single(), StringComparison.Ordinal);

        // A pass with every section local asks nothing of the research model, and runs.
        using var localOnly = await FixtureReplay.ReplayedAsync();

        var asked = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped(), unreachable);

        var ran = await FixtureReplay.Researcher(localOnly, ResearchClock, lane: ClaimRules.Sections, paid: asked, localModel: new NothingAnsweringLocal()).RunAsync("KEYS", "research-local-only");

        // The name's own sections ask it nothing. The industry cycle is the theme's, and the
        // theme's pass has the spend cap make its call, so that pass asks once, finds the
        // model not answering, and leaves the cycle named as not written while the name's
        // own sections are written.
        Assert.Equal(1, asked.Probes);
        Assert.Equal(0, asked.Requests);
        Assert.Equal(ResearchRunner.Written, ran.Outcome);
        Assert.Contains(ran.NotWritten, line => line.Section == ClaimRules.CycleSection && line.Reason == ResearchRunner.ThemeNotRefreshed + unreachable);
    }

    [Fact]
    public async Task ASecondPressWhileAPassRunsIsRefusedByName()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A first pass held inside its first model call, so the lock in force is the one
        // the runner itself took rather than one this test took for it. The 6.8 sweep
        // found the difference: a lock taken here more strictly than the runner takes its
        // own refuses a second pass whatever the runner does.
        var held = new HeldLocal();
        var first = FixtureReplay.Researcher(store, ResearchClock, lane: ClaimRules.Sections, localModel: held, archive: new NoRelease(), news: new NoArticles())
            .RunAsync("KEYS", "research-running");

        await held.Entered.WaitAsync(TimeSpan.FromMinutes(1));

        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var news = new NoArticles();
        var outcome = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, archive: new NoRelease(), news: news).RunAsync("KEYS", "research-while-running");

        Assert.Equal(ResearchRunner.AlreadyRunning, outcome.Outcome);
        Assert.Equal(0, paid.Probes + paid.Requests + news.Requests);
        Assert.Equal([$"research|{ResearchRunner.AlreadyRunning}"], Query(store, "SELECT stage, outcome FROM run_log WHERE run_id = 'research-while-running';"));

        // Released with the pass: the first runs to its end, and the lock goes with it.
        held.Release();

        Assert.Equal(ResearchRunner.Written, (await first).Outcome);

        using (new FileStream(store.DatabaseFile + ".research-KEYS.lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
        }
    }

    [Fact]
    public async Task APassAtTheCapWritesNoPaidSectionAndNamesEachWithTheCapsLine()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A day cap below what the pass's first paid call could cost, so the cap refuses
        // before the first paid call and every paid section is named with its line.
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var outcome = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, caps: new SpendCaps(0.01m, 50m)).RunAsync("KEYS", "research-at-cap");

        Assert.Equal(ResearchRunner.Paused, outcome.Outcome);
        Assert.Equal(0, paid.Requests);

        var paused = outcome.NotWritten.Where(line => line.Reason.StartsWith("research is paused:", StringComparison.Ordinal)).Select(line => line.Section).ToArray();

        Assert.Equal(["The cause of each large move", "The dated calendar items", "The two cases", "The risks, each with what would confirm it", "The short version"], paused);
        Assert.Equal("0", Query(store, $"SELECT COUNT(*) FROM research_section WHERE ticker = 'KEYS' AND model = '{paid.Identity}';").Single());
        Assert.Equal(["paused"], Query(store, "SELECT DISTINCT outcome FROM run_log WHERE run_id = 'research-at-cap' AND stage LIKE 'research call:%';"));
        Assert.Equal(["paused"], Query(store, "SELECT outcome FROM run_log WHERE run_id = 'research-at-cap' AND stage = 'research';"));
    }

    // A name's news answered from the recording, noting each window it is asked for, and refusing
    // the windows a test names as the live feed refuses one the provider has more of.
    sealed class WindowedNews(INameNewsFeed inner, Func<(DateOnly From, DateOnly To), bool>? refuses = null) : INameNewsFeed
    {
        public List<(DateOnly From, DateOnly To)> Asked { get; } = [];

        public int Requests { get; private set; }

        public Task<IReadOnlyList<NewsArticle>> ArticlesAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellation = default)
        {
            Requests++;
            Asked.Add((from, to));

            return refuses?.Invoke((from, to)) == true
                ? throw new ProviderRefusal($"The news query for {ticker} over {Iso(from)} to {Iso(to)} reached 20 pages of 1000 and the provider still had more.", transient: false)
                : inner.ArticlesAsync(ticker, from, to, cancellation);
        }
    }

    [Fact]
    public async Task APassReadsTheNewsInsideEachMoveAndSinceTheFilingAndNeverTheStoredYear()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        var news = new WindowedNews(new RecordedNameNewsFeed(Folder()));

        await FixtureReplay.Researcher(store, ResearchClock, news: news, search: new NoResults()).RunAsync("KEYS", "research-windows");

        // The windows the rule reads, worked out by the test from the night's facts and the
        // release's filing date rather than taken from the pass: KEYS's eight moves, five of
        // which overlap, and the days from 2026-08-18 to the night.
        var facts = FactsFile.Read(Query(store, "SELECT payload FROM facts WHERE ticker = 'KEYS' AND session_date = '2026-09-08';").Single());
        var moves = MoveWindows.In(facts);

        Assert.Equal(8, moves.Count);

        var release = Query(store, "SELECT published_on FROM source_document WHERE url LIKE 'https://www.sec.gov/%'").Single();
        var filed = DateOnly.ParseExact(release, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        Assert.Equal(new DateOnly(2026, 8, 18), filed);

        var expected = NewsWindows.For(moves, filed, new DateOnly(2026, 9, 8));

        Assert.Equal(expected, news.Asked);
        Assert.True(news.Asked.Count < moves.Count + 1, $"The pass asked for {news.Asked.Count} windows over {moves.Count} moves and the filing, so no overlap was read once.");
        Assert.DoesNotContain(news.Asked, window => window.From <= new DateOnly(2026, 9, 8).AddYears(-ResearchRunner.WindowYears) && window.To >= new DateOnly(2026, 9, 8));

        // And what each section was handed is what the stored year handed it: the research record
        // expectation's titles, read by the test above, rest on documents inside these windows.
        Assert.All(
            Query(store, "SELECT published_on FROM source_document WHERE url NOT LIKE 'https://www.sec.gov/%';"),
            published => Assert.Contains(news.Asked, window => string.CompareOrdinal(Iso(window.From), published) <= 0 && string.CompareOrdinal(published, Iso(window.To)) <= 0));
    }

    [Fact]
    public async Task AWindowTheProviderHasMoreOfIsNamedUnreadAndThePassWritesFromTheRest()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // The window since the filing refused as the live feed refused MSFT's year, and the moves'
        // windows answered. The pass does not stop on it: the refusal is on its row and the sections
        // resting on the moves are written.
        // The sections are handed less than the recordings were asked over, so a research model that
        // answers whatever it is asked stands in for the recorded one.
        var news = new WindowedNews(new RecordedNameNewsFeed(Folder()), window => window.To == new DateOnly(2026, 9, 8));
        var paid = new ScriptedModel([.. Enumerable.Repeat("The section, in words and citing its document [D1].", 20)]);

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, news: news, search: new NoResults()).RunAsync("KEYS", "research-window-refused");

        Assert.NotEqual(ResearchRunner.Unavailable, outcome.Outcome);
        Assert.Contains(news.Asked, window => window.To == new DateOnly(2026, 9, 8));
        Assert.Contains("news: The news query for KEYS", outcome.Reason, StringComparison.Ordinal);
        Assert.Contains("still had more", Query(store, "SELECT detail FROM run_log WHERE run_id = 'research-window-refused' AND stage = 'research';").Single(), StringComparison.Ordinal);
        Assert.Single(Query(store, "SELECT stage FROM run_log WHERE run_id = 'research-window-refused' AND stage = 'research';"));
    }

    [Fact]
    public void TheWindowsAreEachMovesSpanAndTheDaysSinceTheFilingReadOnceWhereTheyOverlap()
    {
        var night = new DateOnly(2026, 9, 8);

        MoveWindow[] moves =
        [
            new("largest move", new DateOnly(2026, 2, 17), new DateOnly(2026, 2, 24)),
            new("move 2", new DateOnly(2026, 2, 20), new DateOnly(2026, 3, 2)),
            new("move 3", new DateOnly(2026, 3, 3), new DateOnly(2026, 3, 6)),
            new("move 4", new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 8)),
            new("move 5", null, new DateOnly(2025, 9, 9)),
            new("move 6", new DateOnly(2026, 8, 20), new DateOnly(2026, 8, 25)),
        ];

        // Overlapping spans are one, and a span starting the day after another ends is one with it;
        // a move whose start has left the stored year is no window; and the filing's span to the
        // night takes in a move inside it.
        Assert.Equal(
            [(new DateOnly(2026, 2, 17), new DateOnly(2026, 3, 6)), (new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 8)), (new DateOnly(2026, 8, 18), night)],
            NewsWindows.For(moves, new DateOnly(2026, 8, 18), night));

        // With no filing, the quarter back from the night; and a filing dated after the night is read
        // as none.
        Assert.Equal((night.AddMonths(-NewsWindows.WithoutAFilingMonths), night), NewsWindows.For([], null, night).Single());
        Assert.Equal((night.AddMonths(-NewsWindows.WithoutAFilingMonths), night), NewsWindows.For([], night.AddDays(1), night).Single());
    }

    [Fact]
    public async Task ACapReachedInsideAPassStopsItsRemainingPaidCallsRatherThanWarning()
    {
        // The pass as the recordings have it, once with the caps that let it run, to read what its
        // first paid call cost and what its second could cost.
        var asked = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());

        using (var free = await FixtureReplay.ReplayedForResearchAsync())
        {
            await FixtureReplay.Researcher(free, ResearchClock, paid: asked, search: new NoResults()).RunAsync("KEYS", "research-priced");
        }

        var first = asked.Asked[0];
        var second = asked.Asked[1];
        var cost = asked.Price(OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(first))), first.Section));

        // A day cap the first call fits inside and the first call's cost with the second's ceiling
        // does not: the cap is reached inside the pass rather than before it.
        var cap = cost + asked.Ceiling(second) - 0.000001m;

        Assert.True(asked.Ceiling(first) <= cap, "The first call's ceiling does not fit the cap, so the pass would stop before it rather than inside it.");

        using var store = await FixtureReplay.ReplayedForResearchAsync();

        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var outcome = await FixtureReplay.Researcher(store, ResearchClock, paid: paid, caps: new SpendCaps(cap, 50m), search: new NoResults()).RunAsync("KEYS", "research-cap-reached");

        // One call made and written, and no call after the refusal: the pass is paused, and every
        // paid section it had not reached is named with the cap's line rather than written.
        Assert.Equal(ResearchRunner.Paused, outcome.Outcome);
        Assert.Equal([first.Section], paid.Asked.Select(request => request.Section).ToArray());
        Assert.Equal([$"{first.Section}|1"], Query(store, $"SELECT section, version FROM research_section WHERE ticker = 'KEYS' AND model = '{paid.Identity}';"));

        var stopped = outcome.NotWritten.Where(line => line.Reason.StartsWith("research is paused:", StringComparison.Ordinal)).Select(line => line.Section).ToArray();

        Assert.Contains(second.Section, stopped);
        Assert.DoesNotContain(first.Section, stopped);
        Assert.Equal([cost.ToString(CultureInfo.InvariantCulture)], Query(store, "SELECT spend FROM run_log WHERE run_id = 'research-cap-reached' AND stage LIKE 'research call:%' AND outcome = 'ok';"));
        Assert.Equal(["paused"], Query(store, "SELECT outcome FROM run_log WHERE run_id = 'research-cap-reached' AND stage = 'research';"));
    }

    [Fact]
    public async Task TheLocalModelUnavailableLeavesTheLocalLaneAbsentAndThePassWritesThePaidLane()
    {
        // The paid lane's sections are written while the local model is not answering, and
        // the local lane's are left absent with their reason, which the page reads. The short
        // version is in the local lane here, so every paid call is one the default pass made.
        using var fresh = await FixtureReplay.ReplayedForResearchAsync();

        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var outcome = await FixtureReplay.Researcher(fresh, ResearchClock, lane: [.. ProseWriter.DefaultLane, "The short version"], paid: paid, localModel: new NothingAnsweringLocal())
            .RunAsync("KEYS", "research-local-gone");

        Assert.Equal(ResearchRunner.Written, outcome.Outcome);
        Assert.Equal(
            [.. ProseWriter.DefaultLane, "The short version"],
            outcome.NotWritten.Where(line => line.Reason.StartsWith(ProseWriter.Unavailable, StringComparison.Ordinal)).Select(line => line.Section).ToArray());
        Assert.Equal(
            ["The cause of each large move", "The dated calendar items", "The two cases", "The risks, each with what would confirm it"],
            outcome.Written.Select(section => section.Section).ToArray());
        Assert.All(outcome.Written, section => Assert.Equal(paid.Identity, section.Model));
        Assert.Equal("0", Query(fresh, $"SELECT COUNT(*) FROM research_section WHERE ticker = 'KEYS' AND model = '{LocalModelSettings.DefaultModel}';").Single());
    }

    [Fact]
    public async Task ASectionTheMachineCannotHoldIsWrittenByThePaidLaneInTheSamePass()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // The short version already written today, so the pass writes the sections it
        // summarises and not a summary of a set no recording was made over.
        store.Execute("INSERT INTO research_section VALUES ('KEYS', 'The short version', 1, '2026-09-08', 'a writer', 'accepted', 'prose', '[]', NULL);");

        // A context too small for the two sections handed the release and large enough for
        // the key: the two are refused before any local call and left for the paid path,
        // which writes them in this pass from the recordings the comparison made.
        var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());
        var local = new RecordedLocalModelFeed(Folder());

        var outcome = await FixtureReplay.Researcher(store, ResearchClock, local: local, paid: paid, localSettings: new LocalModelSettings(null, null, null, 8000, null))
            .RunAsync("KEYS", "research-cannot-hold");

        Assert.Equal(["What the company sells", "The segment commentary"], outcome.LeftForThePaidPath);
        Assert.DoesNotContain(local.Asked, request => request.Section is "What the company sells" or "The segment commentary");
        Assert.Contains(paid.Asked, request => request.Section == "What the company sells");
        Assert.Contains(paid.Asked, request => request.Section == "The segment commentary");

        // The writer's own row names each section and the reason, as section 18 says.
        var prose = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'research-cannot-hold' AND stage = 'prose';").Single()).RootElement;

        Assert.All(
            prose.GetProperty("notWritten").EnumerateArray(),
            line => Assert.StartsWith(ProseWriter.CannotHold, line.GetProperty("reason").GetString()!, StringComparison.Ordinal));
    }

    // ---- the lane boundary, both ways ----

    [Fact]
    public async Task TheLaneBoundaryIsMeasuredBothWaysOverOneEvidenceSet()
    {
        var comparison = Expected("research-record").GetProperty("comparison");

        foreach (var (asked, lane, paidForLocal) in new[]
        {
            (comparison.GetProperty("paid"), (IReadOnlyList<string>)[], true),
            (comparison.GetProperty("local"), (IReadOnlyList<string>)ClaimRules.Sections, false),
        })
        {
            var local = new RecordedLocalModelFeed(Folder());
            var paid = new RecordedResearchModelFeed(Folder(), Providers.ResearchModelFeedTests.Shipped());

            using var store = await FixtureReplay.ResearchedAsync(lane: lane, paidForLocal: paidForLocal, local: local, paid: paid);

            // Which version of each section the checker accepted, and which it left out.
            Assert.Equal(
                [.. asked.GetProperty("accepted").EnumerateObject().Select(section => $"{section.Name}|{section.Value.GetInt32()}").Order(StringComparer.Ordinal)],
                Query(store, "SELECT section, version FROM research_section WHERE ticker = 'KEYS' AND status = 'accepted' ORDER BY section;"));
            Assert.Equal(
                Listed(asked.GetProperty("fallback")).Order(StringComparer.Ordinal),
                Query(store, "SELECT section FROM research_section WHERE ticker = 'KEYS' AND status = 'fallback' ORDER BY section;"));

            // Over the name's own sections: the theme's one call is made the same in both runs,
            // whichever lane the name's sections are in, and is the theme's rather than a lane's.
            Assert.Equal(asked.GetProperty("calls").GetInt32(), local.Requests + paid.Asked.Count(request => request.Section != ClaimRules.CycleSection));
            Assert.Equal(1, paid.Asked.Count(request => request.Section == ClaimRules.CycleSection));
            Assert.Equal(
                decimal.Parse(asked.GetProperty("spend").GetString()!, CultureInfo.InvariantCulture),
                Query(store, $"SELECT spend FROM run_log WHERE run_id = 'replay-research' AND stage NOT LIKE 'research call: {ClaimRules.CycleSection}%';").Sum(spend => decimal.Parse(spend, CultureInfo.InvariantCulture)));

            // One evidence set: every section in both runs was handed what the default pass
            // hands it, whichever model asked.
            Assert.Equal(
                Listed(Expected("research-record").GetProperty("handed").GetProperty("acrossTheEvidence")).Length,
                JsonDocument.Parse(Query(store, "SELECT source_ids FROM research_section WHERE ticker = 'KEYS' AND section = 'The two cases' AND version = 1;").Single()).RootElement.GetArrayLength());
        }
    }

    // ---- the rule that picks the documents, over constructed documents ----

    [Fact]
    public void EachSectionIsHandedTheDocumentsTheRulePicksInTheOrderItPicksThem()
    {
        Fact[] facts =
        [
            new("largest move session", "2026-02-24", "move"),
            new("largest move measured from", "2026-02-17", "move"),
            new("move 2 session", "2026-08-24", "move"),
            new("move 2 measured from", "2026-08-17", "move"),
        ];

        static EvidenceDocument Document(string id, string published, int symbols, bool admitted = true) =>
            new(new StoredDocument(id, "https://example.test/" + id, id, DateOnly.Parse(published, CultureInfo.InvariantCulture), DateTimeOffset.UnixEpoch, admitted ? "text" : null, admitted ? Admissibility.Accepted : Admissibility.AiWrittenSummary), symbols);

        EvidenceDocument[] documents =
        [
            Document("roundup-inside-february", "2026-02-18", 6),
            Document("company-alone-late-february", "2026-02-23", 1),
            Document("company-alone-early-february", "2026-02-19", 1),
            Document("two-listings-february", "2026-02-18", 2),
            Document("own-filing", "2026-08-18", Evidence.OwnFiling),
            Document("company-alone-august", "2026-08-19", 1),
            Document("refused-august", "2026-08-20", 1, admitted: false),
            Document("since-filing-newest-two", "2026-09-03", 2),
            Document("since-filing-older-two", "2026-08-27", 2),
            Document("since-filing-roundup", "2026-09-05", 9),
            Document("before-filing", "2026-08-01", 1),
            Document("since-a", "2026-08-25", 2),
            Document("since-b", "2026-08-26", 2),
            Document("since-c", "2026-08-28", 2),
        ];

        var handed = Evidence.ForSections(facts, documents, "own-filing");

        // Each move: fewest companies, then earliest, at most two, listed once in the
        // order they were published. February's roundup and its two-listing article lose
        // to the two naming the company alone; August's refusal is not handed while an
        // admitted document is.
        Assert.Equal(
            ["company-alone-early-february", "company-alone-late-february", "own-filing", "company-alone-august"],
            handed[Evidence.CauseSection].Select(document => document.Id).ToArray());

        // The invariant that makes the runner's refusal of a cause with no document inside a
        // move a guard rather than a path: every document the rule hands the cause is inside
        // a move the prompt pairs it with, so a cause handed an admitted document always has
        // a move to be about. The 6.8 sweep's mutation of that refusal survived for this
        // reason, and this is where the reason is asserted.
        var causeDocuments = handed[Evidence.CauseSection]
            .Select(document => new PromptDocument(document.Id, document.Title, document.PublishedOn, document.Body ?? string.Empty))
            .ToArray();

        Assert.Equal(
            causeDocuments.Length,
            SectionPrompt.MovesWithDocuments(facts, causeDocuments).SelectMany(move => move.Markers).Distinct().Count());

        Assert.Equal(["own-filing"], handed[Evidence.Sells].Select(document => document.Id));
        Assert.Equal(["own-filing"], handed[Evidence.Segments].Select(document => document.Id));

        // Across the evidence: the filing first, then since the day it was filed, fewest
        // companies and then the newest, at most six, the roundup and the earlier document
        // left out.
        Assert.Equal(
            ["own-filing", "company-alone-august", "since-filing-newest-two", "since-c", "since-filing-older-two", "since-b", "since-a"],
            handed["The two cases"].Select(document => document.Id).ToArray());
        Assert.Equal(1 + Evidence.DocumentsSinceTheFiling, handed["The two cases"].Count);
        Assert.All(Evidence.AcrossTheEvidence, section => Assert.Equal(handed["The two cases"], handed[section]));

        // A move with only refusals inside it hands those, so the section cites what was
        // fetched and the checker leaves it out saying no admissible source was found.
        var refusedOnly = Evidence.Moves(facts, [Document("refused-february", "2026-02-20", 1, admitted: false)]);

        Assert.Equal(["refused-february"], refusedOnly.Select(document => document.Id));

        // No filing of its own: across the whole window, chosen the same way.
        Assert.DoesNotContain("own-filing", Evidence.ForSections(facts, [.. documents.Where(document => document.Stored.Id != "own-filing")], null)["The two cases"].Select(document => document.Id));
        Assert.False(Evidence.ForSections(facts, documents, null).ContainsKey(Evidence.Sells));
    }

    [Fact]
    public void TheCountsTheSpecsStateForWhatASectionIsHandedAreTheOnesTheRulePicksBy()
    {
        // Stated in section 12.2's lane table and in RUNBOOK.md, and set in the rule, so a
        // count moved in one place and not the others fails here rather than leaving a
        // document describing a pass nothing runs.
        string[] words = ["none", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten"];

        var architecture = Corpus.Read("docs/ARCHITECTURE.html");
        var cause = System.Text.RegularExpressions.Regex.Match(architecture, "<tr><td>The cause of each large move</td>.*?</tr>").Value;
        var twoCases = System.Text.RegularExpressions.Regex.Match(architecture, "<tr><td>The two cases</td>.*?</tr>").Value;

        Assert.Contains($"at most {words[Evidence.DocumentsPerMove]} a move", cause, StringComparison.Ordinal);
        Assert.Contains($"at most {words[Evidence.DocumentsSinceTheFiling]} documents published since it", twoCases, StringComparison.Ordinal);

        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Contains($"{words[Evidence.DocumentsPerMove]} a move for the cause of each move, and {words[Evidence.DocumentsSinceTheFiling]} since the release", runbook, StringComparison.Ordinal);

        // What a pass over the fixture cost, as RUNBOOK states it, to the hundredth of a cent
        // of what the research record's paid calls came to.
        var spend = decimal.Parse(Expected("research-record").GetProperty("spend").GetString()!, CultureInfo.InvariantCulture);

        Assert.Contains($"through the spend cap for ${decimal.Round(spend, 4).ToString(CultureInfo.InvariantCulture)}", runbook, StringComparison.Ordinal);

        // And the windows a pass reads the news over, which the runbook states as the rule does:
        // inside each stored move and from the release's filing date to the night.
        Assert.Equal(1, ResearchRunner.WindowYears);
        Assert.Contains("the name's news inside each stored move and from the release's filing date to the night, overlapping spans once", runbook, StringComparison.Ordinal);
        Assert.DoesNotContain("news for the stored year", runbook, StringComparison.Ordinal);
    }

    // ---- a dated calendar item's date ----

    [Fact]
    public void ACalendarItemsDateIsOneItsCitedDocumentStatesAndFallsAfterTheNight()
    {
        var conferences = new StoredDocument(
            "conferences", "https://example.test/conferences", "Upcoming investor conferences", new DateOnly(2026, 8, 28), DateTimeOffset.UnixEpoch,
            "Keysight will present at the Goldman Sachs Communacopia + Technology Conference on Friday, September 11, 2026, and at the Truist Technology Symposium on Tuesday, September 15, 2026.",
            Admissibility.Accepted);

        var collaboration = new StoredDocument(
            "collaboration", "https://example.test/collaboration", "A research collaboration", new DateOnly(2026, 9, 3), DateTimeOffset.UnixEpoch,
            "The collaboration was announced on September 3, 2026.",
            Admissibility.Accepted);

        var meeting = new StoredDocument(
            "meeting", "https://example.test/meeting", "A board meeting", new DateOnly(2026, 9, 8), DateTimeOffset.UnixEpoch,
            "The board met on September 8, 2026.",
            Admissibility.Accepted);

        StoredDocument?[] sources = [conferences, collaboration, meeting];
        var night = new DateOnly(2026, 9, 8);

        string Reasons(string prose) =>
            string.Join("; ", ClaimRules.Check(ClaimRules.CalendarSection, prose, [], sources, night).Findings.Select(finding => finding.Reason));

        // A date the cited document states, after the night: nothing to find.
        Assert.Equal(string.Empty, Reasons("Keysight presents at the Goldman Sachs conference on September 11, 2026 [D1]."));
        Assert.Equal(string.Empty, Reasons("Keysight presents at the Truist symposium on Sept. 15 [D1]."));

        // The same date cited to a document that does not state it.
        Assert.Equal(ClaimRules.DateNoCitedDocumentCarries, Reasons("Keysight presents on September 11, 2026 [D2]."));

        // A date the cited document states that is on or before the night, the night itself
        // included, which the 6.8 sweep found no sentence reaching.
        Assert.Equal(ClaimRules.DateNotAfterTheNight, Reasons("The collaboration was announced on September 3, 2026 [D2]."));
        Assert.Equal(ClaimRules.DateNotAfterTheNight, Reasons("The board met on September 8, 2026 [D3]."));

        // A date nobody stated.
        Assert.Equal(ClaimRules.DateNoCitedDocumentCarries, Reasons("Keysight reports on December 1, 2026 [D1]."));

        // Every other section still holds a date to the facts file.
        Assert.Equal(
            ClaimRules.UnknownDate,
            string.Join("; ", ClaimRules.Check("The two cases", "Keysight presents on September 11, 2026 [D1].", [], sources, night).Findings.Select(finding => finding.Reason)));
    }

    // ---- an answer the provider billed and that cannot be stored ----

    [Fact]
    public async Task AnAnswerTheProviderBilledAndThatCannotBeStoredIsPricedOnItsRow()
    {
        using var store = new TemporaryStore().Migrated();

        var settings = Providers.ResearchModelFeedTests.Shipped();
        var request = Providers.ResearchModelFeedTests.Recorded(settings);

        // The default-mode recording with its answer cut off at the budget, as the provider
        // sends one: the counts are there and the provider billed them.
        var cut = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(request))))!;
        cut["choices"]![0]!["finish_reason"] = "length";

        using var client = new HttpClient(new Answering(cut.ToJsonString())) { BaseAddress = new Uri(settings.BaseAddress) };

        var call = await new SpendCap(new OpenAiCompatibleResearchFeed(client, settings), SpendCaps.Default, SpendClock("2026-09-15T20:00:00Z"), store.DatabaseFile)
            .AskAsync(request, "pass-billed-unusable");

        var billed = settings.Pricing.Price(OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(request))), request.Section));

        Assert.False(call.Answered);
        Assert.Equal(billed, call.Price);
        Assert.Equal(0.0001731m, billed);
        Assert.Equal([$"research call: The two cases|refused|1|1|{billed.ToString(CultureInfo.InvariantCulture)}"], CallRows(store, "pass-billed-unusable"));

        // And the ledger the next call is judged by holds it.
        await using var connection = store.Open();

        Assert.Equal(billed, (await SpendCap.LedgerAsync(connection, DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture))).SpentOn(new DateOnly(2026, 9, 15)));
    }

    sealed class Answering(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
    }

    // A local runtime that takes the first call and answers none until it is released,
    // and then reports that it is not answering, which ends a pass's local lane.
    sealed class HeldLocal : ILocalModelFeed
    {
        readonly TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Requests { get; private set; }

        public Task Entered => entered.Task;

        public void Release() => released.TrySetResult();

        public async Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Requests++;
            entered.TrySetResult();

            await released.Task.WaitAsync(TimeSpan.FromMinutes(1), cancellation);

            throw new LocalModelUnavailable("released");
        }
    }

    // A name's year holding the given articles and no others.
    sealed class Articles(IReadOnlyList<NewsArticle> articles) : INameNewsFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<NewsArticle>> ArticlesAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(articles);
        }
    }

    // A name's year with no article in it.
    internal sealed class NoArticles : INameNewsFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<NewsArticle>> ArticlesAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<NewsArticle>>([]);
        }
    }

    // A search tool that answers and finds nothing, for a pass on a day the recordings were
    // not taken for: a search on another day is another request, which the recording refuses.
    internal sealed class NoResults : ISearchFeed
    {
        public int Requests { get; private set; }

        public Task<SearchAnswer> SearchAsync(SearchQuery query, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(new SearchAnswer([]));
        }
    }

    // An archive that answers for the company and files no results release.
    internal sealed class NoRelease : IFilingsArchiveFeed
    {
        public int Requests { get; private set; }

        public Task<ArchiveFilings> FilingsAsync(string ticker, string cik, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(new ArchiveFilings(ticker, cik, [], [], null, null, [], null, [], 0));
        }
    }

    // A local runtime with nothing listening, as the transport reports it.
    internal sealed class NothingAnsweringLocal : ILocalModelFeed
    {
        public int Requests { get; private set; }

        public Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Requests++;

            throw new LocalModelUnavailable("No connection could be made because the target machine actively refused it.");
        }
    }
}
