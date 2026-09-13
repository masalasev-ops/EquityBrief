using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EquityBrief.Tests.Reading;

// read-surface, 6.8: the name page once a research pass has written something a reader
// is shown. The written sections in section 4's order with their dates and models, the
// dates and sources, the three research states with what each offers, the control and
// the cost stated before it is pressed, the route that control reaches, and tonight's
// count of reports carrying fresh prose against reused. Each is read back off the markup
// against the store by a query of the test's own.
public partial class ReadSurface
{
    static readonly DateTimeOffset AWeekLater = DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture);

    // The name page as the route composes it, at an instant, over caps.
    static async Task<string> ResearchedPage(TemporaryStore store, string ticker, DateTimeOffset now, SpendCaps? caps = null)
    {
        var api = Api(store);
        var bars = await api.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue);
        var written = await api.WrittenSectionsAsync(ticker);
        IClock clock = FixedClock.At(now, SessionZones.UnitedStates);

        return NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            ticker,
            bars,
            await api.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(ticker),
            await api.ProfileAsync(ticker),
            await api.LadderAsync(ticker),
            await api.NextEventAsync(ticker, bars[^1].SessionDate),
            await api.MovesAsync(ticker),
            await api.FundamentalsAsync(ticker),
            sections: await api.SectionStatesAsync(ticker, DateOnly.MaxValue),
            staleness: await api.StalenessAsync(ticker),
            written: written,
            pass: await api.NewestPassAsync(ticker),
            spend: NameScreen.Spend(await api.SpentRowsAsync(SpendLedger.MonthStart(now), now.AddSeconds(1)), caps ?? SpendCaps.Default, now),
            cited: await api.CitedDocumentsAsync(NameScreen.Cited(written)),
            events: await api.EventsAsync(ticker, bars[^1].SessionDate),
            priced: await api.PaidCallSpendsAsync(),
            today: clock.SessionDateAt(now));
    }

    // The newest accepted version of each of a name's sections, by a query of the test's own.
    static IReadOnlyList<string[]> Accepted(TemporaryStore store, string ticker) =>
        Rows(
            store,
            "SELECT section, as_of, model, prose, source_ids FROM research_section r " +
            $"WHERE ticker = '{ticker}' AND status = 'accepted' " +
            "AND version = (SELECT MAX(version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted') " +
            "ORDER BY section;");

    // A written section as the page drew it, or none.
    static Match WrittenOnThePage(string page, string section) =>
        Regex.Match(
            page,
            $"<section class=\"written-section\" data-ticker=\"[^\"]*\" data-section=\"{Regex.Escape(WebUtility.HtmlEncode(section))}\" data-as-of=\"([^\"]*)\" data-model=\"([^\"]*)\">(.*?)</section>",
            RegexOptions.Singleline);

    // Every accepted section but the cause, which the moves table draws, is drawn once
    // with the date and model the store holds, its paragraphs making up the stored prose.
    static void AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(TemporaryStore store, string page, int stated)
    {
        var accepted = Accepted(store, "KEYS").Where(row => row[0] != ClaimRules.CauseSection).ToArray();

        Assert.Equal(stated, accepted.Length);

        foreach (var row in accepted)
        {
            var drawn = WrittenOnThePage(page, row[0]);

            Assert.True(drawn.Success, $"{row[0]} is not drawn");
            Assert.Equal(row[1], drawn.Groups[1].Value);
            Assert.Equal(row[2], WebUtility.HtmlDecode(drawn.Groups[2].Value));

            var paragraphs = Regex.Matches(drawn.Groups[3].Value, "<p class=\"prose\">([^<]*)</p>").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value));

            Assert.Equal(Regex.Replace(row[3], @"\s+", " ").Trim(), Regex.Replace(string.Join(" ", paragraphs), @"\s+", " ").Trim());
            Assert.Contains($"<p class=\"written-by\">written on {row[1]} by {WebUtility.HtmlEncode(row[2])}</p>", drawn.Groups[3].Value, StringComparison.Ordinal);
            Assert.Single(Regex.Matches(page, $"<section class=\"written-section\" data-ticker=\"KEYS\" data-section=\"{Regex.Escape(WebUtility.HtmlEncode(row[0]))}\""));
        }
    }

    [Fact]
    public void TheReadSurfaceNamesThePassStagesOutcomesAndReasonsTheWorkerWrites()
    {
        // The read surface cannot reference the worker, so each word is stated twice and
        // this is what keeps the two statements one.
        Assert.Equal(ResearchRunner.Stage, ReadApi.ResearchStage);
        Assert.Equal(ResearchRunner.Written, ReadApi.PassWritten);
        Assert.Equal(ResearchRunner.NotWarranted, ReadApi.PassNotWarranted);
        Assert.Equal(ResearchRunner.Unavailable, ReadApi.PassUnavailable);
        Assert.Equal(ResearchRunner.Paused, ReadApi.PassPaused);
        Assert.Equal(ResearchRunner.AlreadyRunning, ReadApi.PassAlreadyRunning);
        Assert.Equal(ResearchRunner.NoFactsFile, ReadApi.PassNoFactsFile);
        Assert.Equal(ProseWriter.Unavailable, NameScreen.LocalUnavailable);
        Assert.Equal(ProseWriter.CannotHold, NameScreen.CannotHold);

        var paused = SpendRule.Judge(new SpendLedger([new SpentRow(AWeekLater, 10m)]), SpendCaps.Default, AWeekLater);

        Assert.StartsWith(NameScreen.PausedLine, paused.Line, StringComparison.Ordinal);
    }

    [Fact]
    public void EverySectionFigureTwelveTwoNamesIsPlacedOnThePageExactlyOnce()
    {
        // Both directions: a section with no place would be written and never drawn, and a
        // section placed twice would be drawn twice.
        string[] placed =
        [
            .. SinglePageApp.AtTheTop,
            .. SinglePageApp.BeforeTheNumbers,
            .. SinglePageApp.AfterTheNumbers,
            .. SinglePageApp.UnderTheFigures,
            .. SinglePageApp.AfterThePlan,
            SinglePageApp.InTheDates,
            SinglePageApp.InTheMovesTable,
        ];

        Assert.Equal(ClaimRules.Sections.Order(StringComparer.Ordinal), placed.Order(StringComparer.Ordinal));
        Assert.Equal(ClaimRules.CauseSection, SinglePageApp.InTheMovesTable);
        Assert.Equal(ClaimRules.CalendarSection, SinglePageApp.InTheDates);
    }

    [Fact]
    public async Task TheShortVersionIsDrawnAtTheTopWithItsDateAndTheModelThatWroteItBeneathIt()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var stored = Accepted(store, "KEYS").Single(row => row[0] == "The short version");
        var drawn = WrittenOnThePage(page, "The short version");

        Assert.True(drawn.Success);
        Assert.Equal(stored[1], drawn.Groups[1].Value);
        Assert.Equal(stored[2], WebUtility.HtmlDecode(drawn.Groups[2].Value));

        // Beneath it: the date and model after the last paragraph of the prose.
        var body = drawn.Groups[3].Value;

        Assert.True(
            body.LastIndexOf("<p class=\"prose\">", StringComparison.Ordinal) < body.IndexOf("<p class=\"written-by\">", StringComparison.Ordinal),
            "the date and model are not beneath the short version");

        // At the top: above the chart, the table of moves and every other written section.
        var at = page.IndexOf("<section class=\"written-section\" data-ticker=\"KEYS\" data-section=\"The short version\"", StringComparison.Ordinal);

        Assert.True(at < page.IndexOf("class=\"level-chart\"", StringComparison.Ordinal));
        Assert.True(at < page.IndexOf("<section class=\"how-it-got-here\"", StringComparison.Ordinal));
        Assert.All(
            Accepted(store, "KEYS").Select(row => row[0]).Where(section => section is not "The short version" and not ClaimRules.CauseSection),
            section => Assert.True(at < page.IndexOf($"<section class=\"written-section\" data-ticker=\"KEYS\" data-section=\"{WebUtility.HtmlEncode(section)}\"", StringComparison.Ordinal), section));

        // Three or four paragraphs as the model wrote them, and none of it rendered as markup.
        Assert.InRange(Regex.Matches(body, "<p class=\"prose\">").Count, 1, 6);
        Assert.DoesNotContain("<script", body, StringComparison.OrdinalIgnoreCase);

        // A name with no research draws no short version at all, and the page says why above it.
        using var replayed = await FixtureReplay.ReplayedAsync();

        var none = await ResearchedPage(replayed, "KEYS", AWeekLater);

        Assert.False(WrittenOnThePage(none, "The short version").Success);
        Assert.Contains("data-state=\"missing\"", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheResearchedSectionsAreDrawnInSectionFoursOrderEachWithItsOwnDateAndModel()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);

        // Eight written over one pass, stated in advance, and seven drawn as sections: the
        // cause of each move is drawn in the moves table.
        Assert.Equal(8, Accepted(store, "KEYS").Count);
        AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(store, page, 7);

        int At(string marker) => page.IndexOf(marker, StringComparison.Ordinal);
        int Section(string section) => At($"<section class=\"written-section\" data-ticker=\"KEYS\" data-section=\"{WebUtility.HtmlEncode(section)}\"");

        // Section 4's order: what the company sells and its segments, the numbers, the two
        // cases, then the chart and the plan, then what would make it wrong.
        Assert.True(Section("What the company sells") < Section("The segment commentary"));
        Assert.True(Section("The segment commentary") < At("<section class=\"numbers\""));
        Assert.True(At("<section class=\"numbers\"") < Section("The two cases"));
        Assert.True(Section("The two cases") < At("class=\"level-summary\""));
        Assert.True(At("class=\"level-summary\"") < Section("The key under each figure"));
        Assert.True(Section("The key under each figure") < At("<section class=\"plan-arithmetic\""));
        Assert.True(At("<section class=\"plan-arithmetic\"") < Section("The risks, each with what would confirm it"));

        // The cycle rests on a theme record, which 6.9 writes, so it is not drawn and the
        // page says why in the words the pass stored.
        Assert.Equal(-1, Section("The industry cycle"));
        Assert.Equal(
            $"The industry cycle is not written: {ResearchRunner.NoThemeRecord}",
            WebUtility.HtmlDecode(Regex.Match(page, "<p class=\"not-written\" data-section=\"The industry cycle\">([^<]*)</p>").Groups[1].Value));
    }

    [Fact]
    public async Task DatesAndSourcesDrawTheCalendarTheDatedItemsAndEveryDocumentTheWrittenSectionsCite()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var region = Regex.Match(page, "<section class=\"dates-and-sources\"[^>]*>(.*)</section>", RegexOptions.Singleline).Value;

        Assert.NotEmpty(region);

        // The calendar: every event the store holds for the name from its newest session,
        // by a query of the test's own.
        var newest = Rows(store, "SELECT MAX(session_date) FROM bar WHERE ticker = 'KEYS';").Single()[0];
        var events = Rows(store, $"SELECT event_date, kind FROM calendar WHERE ticker = 'KEYS' AND event_date >= '{newest}' ORDER BY event_date, kind;");

        Assert.Equal(
            events.Select(row => $"{row[0]}|{row[1]}"),
            Regex.Matches(region, "<tr data-date=\"([^\"]*)\" data-kind=\"([^\"]*)\">").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}"));

        // The dated items the pass read out of the documents, inside the region, as written.
        var items = Accepted(store, "KEYS").Single(row => row[0] == ClaimRules.CalendarSection);
        var drawnItems = WrittenOnThePage(region, ClaimRules.CalendarSection);

        Assert.True(drawnItems.Success);
        Assert.Equal(items[1], drawnItems.Groups[1].Value);

        // Every document a written section cites, once each, with its date and link.
        var cited = Accepted(store, "KEYS")
            .SelectMany(row => JsonDocument.Parse(row[4]).RootElement.EnumerateArray().Select(id => id.GetString()!))
            .Distinct()
            .Order(StringComparer.Ordinal)
            .ToArray();

        var sources = Regex.Match(region, "<ol class=\"sources\">(.*?)</ol>", RegexOptions.Singleline).Groups[1].Value;
        var listed = Regex.Matches(sources, "<li data-document=\"([^\"]*)\" data-published-on=\"([^\"]*)\" data-url=\"([^\"]*)\">(.*?)</li>", RegexOptions.Singleline).ToArray();

        Assert.NotEmpty(cited);
        Assert.Equal(cited, listed.Select(match => match.Groups[1].Value).Order(StringComparer.Ordinal));

        foreach (var document in listed)
        {
            var row = Rows(store, $"SELECT published_on, url, title FROM source_document WHERE id = '{document.Groups[1].Value}';").Single();

            var link = Regex.Match(document.Groups[4].Value, "^<a href=\"([^\"]*)\">([^<]*)</a>, published on ([0-9-]+)$");

            Assert.Equal(row[0], document.Groups[2].Value);
            Assert.Equal(row[1], WebUtility.HtmlDecode(document.Groups[3].Value));
            Assert.True(link.Success, document.Groups[4].Value);
            Assert.Equal(row[1], WebUtility.HtmlDecode(link.Groups[1].Value));
            Assert.Equal(row[2], WebUtility.HtmlDecode(link.Groups[2].Value));
            Assert.Equal(row[0], link.Groups[3].Value);
        }

        // And each section's markers resolve in the order its source list holds them, so
        // [D2] beside a sentence is the second document that section was handed.
        foreach (var row in Accepted(store, "KEYS").Where(row => row[0] != ClaimRules.CauseSection && row[4] != "[]"))
        {
            var drawn = WrittenOnThePage(page, row[0]).Groups[3].Value;

            Assert.Equal(
                JsonDocument.Parse(row[4]).RootElement.EnumerateArray().Select((id, at) => $"D{at + 1}|{id.GetString()}"),
                Regex.Matches(drawn, "<li data-marker=\"(D\\d+)\" data-document=\"([^\"]*)\">").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}"));
        }
    }

    [Fact]
    public async Task ANameWithNoResearchOffersAControlThatWritesItWithTheCostStatedBeforeItIsPressed()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        // Three priced calls over two passes before this page is opened, and a paused one
        // that is not a price.
        Spend(store, "research-a", "research call: The two cases", "2026-09-14T19:00:00Z", "0.012");
        Spend(store, "research-a", "research call: The short version, round 2", "2026-09-14T19:02:00Z", "0.004");
        Spend(store, "research-b", "research call: The two cases", "2026-09-15T18:00:00Z", "0.021");
        Spend(store, "research-c", "research call: The two cases", "2026-09-15T18:30:00Z", "0", outcome: "paused");

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var region = Regex.Match(page, "<section class=\"research\"[^>]*>(.*?)</section>", RegexOptions.Singleline).Groups[1].Value;

        Assert.Contains($"<p class=\"research-state\" data-state=\"missing\">{Staleness.NotYetWritten}</p>", region, StringComparison.Ordinal);

        // The cost, before the control, read against sums of the store's own rows.
        var cost = Regex.Match(region, "<p class=\"research-cost\" data-passes=\"(\\d+)\" data-spend=\"([^\"]+)\" data-most=\"([^\"]+)\">([^<]*)</p>");
        var control = Regex.Match(region, "<form class=\"research-control\" method=\"post\" action=\"([^\"]*)\" data-kind=\"([a-z-]+)\" data-refresh=\"([a-z]+)\" data-paid-for-local=\"([a-z]+)\">");

        Assert.True(cost.Success);
        Assert.True(control.Success);
        Assert.True(cost.Index < control.Index, "the cost is not stated before the control");

        Assert.Equal("2", cost.Groups[1].Value);
        Assert.Equal(Summed(store, "stage LIKE 'research call:%' AND spend != '0'"), decimal.Parse(cost.Groups[2].Value, CultureInfo.InvariantCulture));
        Assert.Equal(0.037m, decimal.Parse(cost.Groups[2].Value, CultureInfo.InvariantCulture));
        Assert.Equal(0.021m, decimal.Parse(cost.Groups[3].Value, CultureInfo.InvariantCulture));
        Assert.Contains("The 2 research pass(es) the run log has priced cost $0.04 in all, and the most one cost was $0.02.", WebUtility.HtmlDecode(cost.Groups[4].Value), StringComparison.Ordinal);
        Assert.Contains("Research may spend: $0.02 of the $10.00 day cap today", WebUtility.HtmlDecode(cost.Groups[4].Value), StringComparison.Ordinal);

        // What a press sends: a plain pass for this name.
        Assert.Equal(SinglePageApp.PassRoute + "KEYS", control.Groups[1].Value);
        Assert.Equal("write", control.Groups[2].Value);
        Assert.Equal("false", control.Groups[3].Value);
        Assert.Equal("false", control.Groups[4].Value);
        Assert.Single(Regex.Matches(region, "<form class=\"research-control\""));

        // With nothing priced the line says so rather than stating a zero as a price.
        using var unpriced = await FixtureReplay.ReplayedAsync();

        Assert.Contains("No research pass has a recorded cost yet.", WebUtility.HtmlDecode(await ResearchedPage(unpriced, "KEYS", AWeekLater)), StringComparison.Ordinal);

        // And a page holding no cost to state draws no control at all.
        var api = Api(unpriced);
        var costless = NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            "KEYS",
            await api.BarsAsync("KEYS", DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync("KEYS", DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync("KEYS"),
            await api.ProfileAsync("KEYS"),
            await api.LadderAsync("KEYS"),
            null,
            await api.MovesAsync("KEYS"),
            staleness: await api.StalenessAsync("KEYS"));

        Assert.DoesNotContain("research-control", costless, StringComparison.Ordinal);
        Assert.Contains("data-state=\"missing\"", costless, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStaleNameDrawsItsStoredSectionsUnderTheirOwnDatesAndOffersToHaveThemRewritten()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        // The research written before the newest filing the store holds, which is one of
        // the things stale is.
        store.Execute("UPDATE research_section SET as_of = '2026-08-01' WHERE ticker = 'KEYS';");

        var filed = Rows(store, "SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'KEYS';").Single()[0];
        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var state = Regex.Match(page, "<p class=\"research-state\" data-state=\"stale\">([^<]*)</p>");

        Assert.True(state.Success);
        Assert.StartsWith("the research is stale: ", state.Groups[1].Value, StringComparison.Ordinal);
        Assert.Contains($"a filing dated {filed} arrived after it was written", state.Groups[1].Value, StringComparison.Ordinal);
        AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(store, page, 7);
        Assert.All(Regex.Matches(page, "<section class=\"written-section\" data-ticker=\"KEYS\" data-section=\"[^\"]*\" data-as-of=\"([^\"]*)\"").Select(match => match.Groups[1].Value), date => Assert.Equal("2026-08-01", date));

        // The option to have them rewritten, with its cost before it.
        var rewrite = Regex.Match(page, "<form class=\"research-control\" method=\"post\" action=\"/passes/KEYS\" data-kind=\"rewrite\" data-refresh=\"false\" data-paid-for-local=\"false\">");

        Assert.True(rewrite.Success);
        Assert.True(page.IndexOf("class=\"research-cost\"", StringComparison.Ordinal) < rewrite.Index);

        // On the day the pass ran, a plain pass writes nothing, so the page offers none and
        // says a pass ran today.
        var sameDay = await ResearchedPage(store, "KEYS", DateTimeOffset.Parse("2026-09-08T23:00:00Z", CultureInfo.InvariantCulture));

        Assert.DoesNotContain("data-kind=\"rewrite\"", sameDay, StringComparison.Ordinal);
        Assert.Contains("<p class=\"research-pass\" data-outcome=\"ok\" data-as-of=\"2026-09-08\">a research pass ran for this name on 2026-09-08, and opening it again today writes nothing</p>", sameDay, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APausedNameDrawsItsStoredSectionsUnderTheirOwnDatesAndOffersNoControl()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        store.Execute("UPDATE research_section SET as_of = '2026-08-01' WHERE ticker = 'KEYS';");

        // The day cap reached on the day the page is opened.
        Spend(store, "research-today", "research call: The two cases", "2026-09-15T12:00:00Z", "10.00");

        var page = await ResearchedPage(store, "KEYS", AWeekLater);

        Assert.Contains("<p class=\"research-paused\" data-cap=\"day\" data-resumes-at=\"2026-09-16T00:00:00Z\">", page, StringComparison.Ordinal);
        AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(store, page, 7);

        // Stale as well, and still no control, because a press would be refused at the cap.
        Assert.Contains("data-state=\"stale\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<form class=\"research-control\"", page, StringComparison.Ordinal);
        Assert.Contains("data-controls=\"0\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ACloudModelThatDoesNotAnswerLeavesTheStoredResearchRenderingWithItsDatesOrAnOfferToWriteItLater()
    {
        var unreachable = "The research model could not be reached: No connection could be made because the target machine actively refused it.";
        var nextDay = DateTimeOffset.Parse("2026-09-09T21:10:00Z", CultureInfo.InvariantCulture);

        // Stored research, and a rewrite asked for the next day with the model not answering.
        using var store = await FixtureReplay.ResearchedAsync();

        var sectionsBefore = Rows(store, "SELECT COUNT(*) FROM research_section;").Single()[0];
        var documentsBefore = Rows(store, "SELECT COUNT(*) FROM source_document;").Single()[0];

        var refused = await FixtureReplay.Researcher(
                store,
                FixedClock.At(nextDay, SessionZones.UnitedStates),
                paid: new RecordedResearchModelFeed(Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder), Providers.ResearchModelFeedTests.Shipped(), unreachable))
            .RunAsync("KEYS", "research-cloud-gone", new ResearchPassRequest(Refresh: true));

        // The pass did not start: nothing stored, nothing fetched.
        Assert.Equal(ResearchRunner.Unavailable, refused.Outcome);
        Assert.Equal(sectionsBefore, Rows(store, "SELECT COUNT(*) FROM research_section;").Single()[0]);
        Assert.Equal(documentsBefore, Rows(store, "SELECT COUNT(*) FROM source_document;").Single()[0]);

        // The stored research still renders, every section under its own date, and the
        // page says what the pass came to.
        var page = await ResearchedPage(store, "KEYS", nextDay);

        AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(store, page, 7);
        Assert.Matches(
            "<p class=\"research-pass\" data-outcome=\"unavailable\" data-as-of=\"2026-09-09\">the research model did not answer when a pass was asked for on 2026-09-09, so the pass did not start and the stored research is shown as written",
            page);

        // A name with nothing stored: the sections are offered to be written later.
        using var none = await FixtureReplay.ReplayedAsync();

        await FixtureReplay.Researcher(
                none,
                FixedClock.At(nextDay, SessionZones.UnitedStates),
                paid: new RecordedResearchModelFeed(Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder), Providers.ResearchModelFeedTests.Shipped(), unreachable))
            .RunAsync("KEYS", "research-cloud-gone-nothing-stored");

        var offered = await ResearchedPage(none, "KEYS", nextDay);

        Assert.Contains("data-state=\"missing\"", offered, StringComparison.Ordinal);
        Assert.Contains("data-outcome=\"unavailable\"", offered, StringComparison.Ordinal);
        Assert.Matches("<form class=\"research-control\" method=\"post\" action=\"/passes/KEYS\" data-kind=\"write\"", offered);
        Assert.DoesNotContain("<section class=\"written-section\"", offered, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASectionTheMachineCannotHoldIsOfferedToThePaidModelToWrite()
    {
        var (store, document) = await FixtureExpectations.WithRelease();

        using (store)
        {
            await new ProseWriter(
                    new RecordedLocalModelFeed(Path.Combine(Repository.Root, "fixtures", FixtureExpectation.Folder)),
                    FixtureExpectations.LocalSettings(FixtureExpectation.Of("prose").GetProperty("cannotHold").GetProperty("contextTokens").GetInt32()),
                    FixtureExpectations.ReleaseLane,
                    FixtureExpectations.ProseClock,
                    store.DatabaseFile)
                .WriteAsync("KEYS", FixtureExpectations.Handed(document), "prose-cannot-hold-offer");

            var page = await ResearchedPage(store, "KEYS", AWeekLater);

            Assert.Contains($"is not written: {ProseWriter.CannotHold}", WebUtility.HtmlDecode(page), StringComparison.Ordinal);

            var offer = Regex.Match(page, "<form class=\"research-control\" method=\"post\" action=\"/passes/KEYS\" data-kind=\"paid-for-local\" data-refresh=\"false\" data-paid-for-local=\"true\">.*?<button type=\"submit\">([^<]*)</button>", RegexOptions.Singleline);

            Assert.True(offer.Success);
            Assert.Equal("Have the paid model write them", offer.Groups[1].Value);
            Assert.True(page.IndexOf("class=\"research-cost\"", StringComparison.Ordinal) < offer.Index);
        }
    }

    [Fact]
    public async Task TheLocalModelUnavailableIsOfferedThePaidModelToWriteItsSections()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A pass on demand with the local model not answering, which writes the paid lane
        // and leaves the local lane's absent with their reason.
        var outcome = await FixtureReplay.Researcher(
                store,
                FixedClock.At(DateTimeOffset.Parse("2026-09-08T21:10:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates),
                lane: [.. ProseWriter.DefaultLane, "The short version"],
                localModel: new FixtureExpectations.NothingAnsweringLocal())
            .RunAsync("KEYS", "research-local-gone-page");

        Assert.Equal(ResearchRunner.Written, outcome.Outcome);

        var page = await ResearchedPage(store, "KEYS", AWeekLater);

        foreach (var section in ProseWriter.DefaultLane)
        {
            Assert.Matches($"<p class=\"not-written\" data-section=\"{Regex.Escape(WebUtility.HtmlEncode(section))}\">[^<]* is not written: {Regex.Escape(ProseWriter.Unavailable)}", page);
            Assert.False(WrittenOnThePage(page, section).Success);
        }

        Assert.Matches("<form class=\"research-control\" method=\"post\" action=\"/passes/KEYS\" data-kind=\"paid-for-local\" data-refresh=\"false\" data-paid-for-local=\"true\">", page);

        // The paid lane's sections the pass wrote are drawn as written.
        Assert.True(WrittenOnThePage(page, "The two cases").Success);
    }

    [Fact]
    public async Task APassTheCapStoppedShortOfAReachedCapIsStatedOnTheNamePageWithTheCapsOwnLine()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A day cap below what the first paid call could cost and far above what was spent,
        // so the cap refuses the call and no cap is reached.
        var caps = new SpendCaps(0.01m, 50m);
        var night = DateTimeOffset.Parse("2026-09-08T21:10:00Z", CultureInfo.InvariantCulture);

        var outcome = await FixtureReplay.Researcher(store, FixedClock.At(night, SessionZones.UnitedStates), caps: caps).RunAsync("KEYS", "research-short-of-the-cap");

        Assert.Equal(ResearchRunner.Paused, outcome.Outcome);

        // The line the cap refused the first call with, read off that call's own row.
        var capLine = JsonDocument.Parse(Rows(store, "SELECT detail FROM run_log WHERE run_id = 'research-short-of-the-cap' AND stage LIKE 'research call:%' ORDER BY rowid LIMIT 1;").Single()[0])
            .RootElement.GetProperty("paused").GetString()!;

        var page = await ResearchedPage(store, "KEYS", night.AddMinutes(30), caps);

        // The page's own verdict, with no call in hand, is that research may spend, so it
        // draws no pause; what it draws is the pass's refusal in the cap's words.
        Assert.DoesNotContain("class=\"research-paused\"", page, StringComparison.Ordinal);

        var line = Regex.Match(page, "<p class=\"research-pass\" data-outcome=\"paused\" data-as-of=\"2026-09-08\">([^<]*)</p>");

        Assert.True(line.Success);
        Assert.EndsWith(capLine, WebUtility.HtmlDecode(line.Groups[1].Value), StringComparison.Ordinal);
        Assert.StartsWith("the pass on 2026-09-08 was stopped by the spend cap before a cap was reached", WebUtility.HtmlDecode(line.Groups[1].Value), StringComparison.Ordinal);

        foreach (var section in new[] { "The cause of each large move", "The dated calendar items", "The two cases", "The risks, each with what would confirm it", "The short version" })
        {
            Assert.Equal(
                $"{section} is not written: {capLine}",
                WebUtility.HtmlDecode(Regex.Match(page, $"<p class=\"not-written\" data-section=\"{Regex.Escape(section)}\">([^<]*)</p>").Groups[1].Value));
        }
    }

    // ---- the control's route ----

    // A starter that records what it was asked and starts nothing.
    sealed class RecordingStarter : IPassStarter
    {
        public List<PassRequest> Asked { get; } = [];

        public PassStart Start(PassRequest request)
        {
            Asked.Add(request);

            return new PassStart(true, $"a research pass for {request.Ticker} has started");
        }
    }

    sealed class PassHost(string root, IPassStarter starter) : WebApplicationFactory<ReadApi>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(StoreLocation.DataRootKey, root);
            builder.ConfigureTestServices(services => services.AddSingleton(starter));
        }
    }

    static HttpRequestMessage Press(string ticker, string? header, params (string Name, string Value)[] form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, SinglePageApp.PassRoute + ticker)
        {
            Content = new FormUrlEncodedContent(form.Select(field => new KeyValuePair<string, string>(field.Name, field.Value))),
        };

        if (header is not null)
        {
            request.Headers.Add(SinglePageApp.PassHeader, header);
        }

        return request;
    }

    [Fact]
    public async Task TheControlsRouteStartsThePassTheFormAsksForAndWritesNothingItself()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var starter = new RecordingStarter();

        using var host = new PassHost(store.Root, starter);
        using var client = host.CreateClient();

        // The host is up before anything is counted, since starting it writes its own row.
        Assert.Contains("KEYS", await client.GetStringAsync("/screens/name/KEYS"), StringComparison.Ordinal);

        string Held() => string.Join(
            ";",
            Rows(store, "SELECT COUNT(*) FROM research_section;").Single()[0],
            Rows(store, "SELECT COUNT(*) FROM source_document;").Single()[0],
            Rows(store, "SELECT COUNT(*) FROM run_log;").Single()[0]);

        var before = Held();

        // A press from the page: started with what the form asked, at once.
        var pressed = await client.SendAsync(Press("KEYS", SinglePageApp.PassHeaderValue, ("refresh", "false"), ("paidForLocal", "true")));

        Assert.Equal(HttpStatusCode.Accepted, pressed.StatusCode);
        Assert.Equal([new PassRequest("KEYS", false, true)], starter.Asked);
        Assert.Contains("data-started=\"true\"", await pressed.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A request not carrying the page's header, and one carrying another value, start
        // nothing.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Press("KEYS", null, ("refresh", "true")))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Press("KEYS", "another-page"))).StatusCode);

        // A name the index does not hold starts nothing either, refused before a starter is asked.
        var outside = await client.SendAsync(Press("ZZZZ", SinglePageApp.PassHeaderValue));

        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.Contains("data-refused=\"membership\"", await outside.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(starter.Asked);

        // And the surface wrote nothing across the four: the pass is the worker's to write.
        Assert.Equal(before, Held());
    }

    [Fact]
    public void TheStarterRunsTheCommandTheRunbookGivesWithItsArgumentsAsAListAndTheStoreThisSurfaceReads()
    {
        var root = Path.Combine(Path.GetTempPath(), "a data root");
        var starter = new WorkerPassStarter(Repository.Root, root);

        var start = starter.StartInfo(new PassRequest("KEYS", Refresh: true, PaidForLocal: true));

        Assert.Equal(WorkerPassStarter.Executable, start.FileName);
        Assert.False(start.UseShellExecute);
        Assert.Equal(string.Empty, start.Arguments);
        Assert.Equal(["run", "--project", Path.Combine("src", "EquityBrief.Worker"), "--", "research", "--ticker", "KEYS", "--refresh", "--paid-for-local"], start.ArgumentList);
        Assert.Equal(Repository.Root, start.WorkingDirectory);
        Assert.Equal(root, start.Environment["EquityBrief__DataRoot"]);

        // The checkout is found from the surface's own build output.
        Assert.Equal(Path.GetFullPath(Repository.Root).TrimEnd(Path.DirectorySeparatorChar), WorkerPassStarter.Checkout(AppContext.BaseDirectory)!.TrimEnd(Path.DirectorySeparatorChar));

        // The plain press is the command RUNBOOK.md gives for writing one name's research
        // by hand, read off the document rather than restated here.
        var runbook = File.ReadAllText(Path.Combine(Repository.Root, "docs", "RUNBOOK.md"));
        var command = Regex.Match(runbook, "### Writing one name's research.*?```\\s*\\n(dotnet [^\\n]+)\\n```", RegexOptions.Singleline).Groups[1].Value.Trim();

        Assert.Equal(
            command,
            WorkerPassStarter.Executable + " " + string.Join(" ", WorkerPassStarter.Arguments(new PassRequest("KEYS", false, false))).Replace('\\', '/'));

        // A name carrying shell syntax is one argument, not a second command.
        Assert.Contains("KEYS; echo started", starter.StartInfo(new PassRequest("KEYS; echo started", false, false)).ArgumentList);

        // Outside a checkout nothing is started and the line says why.
        var outside = new WorkerPassStarter(null, root).Start(new PassRequest("KEYS", false, false));

        Assert.False(outside.Started);
        Assert.Contains("not running inside a checkout", outside.Line, StringComparison.Ordinal);

        // And the page's own script sends the header the route requires.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains($"'{SinglePageApp.PassHeader}': '{SinglePageApp.PassHeaderValue}'", shell, StringComparison.Ordinal);
        Assert.Contains("research-control", shell, StringComparison.Ordinal);
    }

    // ---- tonight's header ----

    [Fact]
    public async Task TonightsHeaderCountsReportsCarryingFreshProseAgainstReused()
    {
        using var store = new TemporaryStore().Migrated();

        var night = new DateOnly(2026, 9, 8);

        // AAAA rewrote a section on the night; BBBB's research is all from before it; CCCC's
        // only accepted section is from after the night and its section on the night fell
        // back; DDDD has a pending draft on the night over an older accepted one; EEEE has
        // two sections, one on the night and one before.
        store.Execute(
            "INSERT INTO research_section VALUES " +
            "('AAAA', 'The two cases', 1, '2026-09-01', 'm', 'accepted', 'p', '[]', NULL), " +
            "('AAAA', 'The two cases', 2, '2026-09-08', 'm', 'accepted', 'p', '[]', NULL), " +
            "('BBBB', 'The two cases', 1, '2026-09-02', 'm', 'accepted', 'p', '[]', NULL), " +
            "('CCCC', 'The two cases', 1, '2026-09-08', 'm', 'fallback', 'p', '[]', 'x'), " +
            "('CCCC', 'The risks, each with what would confirm it', 1, '2026-09-09', 'm', 'accepted', 'p', '[]', NULL), " +
            "('DDDD', 'The two cases', 1, '2026-08-20', 'm', 'accepted', 'p', '[]', NULL), " +
            "('DDDD', 'The two cases', 2, '2026-09-08', 'm', 'pending', 'p', '[]', NULL), " +
            "('EEEE', 'The two cases', 1, '2026-09-08', 'm', 'accepted', 'p', '[]', NULL), " +
            "('EEEE', 'The short version', 1, '2026-09-03', 'm', 'accepted', 'p', '[]', NULL);");

        var prose = TonightScreen.Prose(night, await Api(store).WrittenOnOrBeforeAsync(night));
        var header = new MarkRenderer().NightHeader(night, 503, 4, "00:03:10", null, null, prose);
        var line = Regex.Match(header, "<p class=\"night-prose\" data-fresh=\"(\\d+)\" data-reused=\"(\\d+)\" data-names=\"(\\d+)\">([^<]*)</p>");

        Assert.True(line.Success);

        // Counted by a query of the test's own: a name is fresh where an accepted section is
        // dated the night, and reused where it has accepted sections and none is.
        var fresh = Rows(store, "SELECT COUNT(DISTINCT ticker) FROM research_section WHERE status = 'accepted' AND as_of = '2026-09-08';").Single()[0];
        var names = Rows(store, "SELECT COUNT(DISTINCT ticker) FROM research_section WHERE status = 'accepted' AND as_of <= '2026-09-08';").Single()[0];

        Assert.Equal(fresh, line.Groups[1].Value);
        Assert.Equal(names, line.Groups[3].Value);

        // Stated in advance, so a count that read every row would not agree with itself.
        Assert.Equal("2", line.Groups[1].Value);
        Assert.Equal("2", line.Groups[2].Value);
        Assert.Equal("4", line.Groups[3].Value);
        Assert.Equal(
            "research prose: 2 report(s) carry prose written on 2026-09-08 and 2 carry only prose written before it, of the 4 name(s) with a written section",
            WebUtility.HtmlDecode(line.Groups[4].Value));
        Assert.DoesNotContain("data-prose=\"absent\"", header, StringComparison.Ordinal);

        // The route states it too, over a store with no research at all.
        using var listed = await FixtureExpectations.WithListings();
        using var host = new Host(listed.Root);
        using var client = host.CreateClient();

        Assert.Contains(
            "<p class=\"night-prose\" data-fresh=\"0\" data-reused=\"0\" data-names=\"0\">",
            await client.GetStringAsync($"/screens/tonight/{NightIn(listed)}"),
            StringComparison.Ordinal);
    }
}
