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
// is shown. The written sections in section 4's order with their dates, the
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
    // The key under each figure is dated by the night whose figures it explains, so it is
    // drawn beside that night's figures alone, and a page of another night's says which
    // night it was written for in its place.
    static void AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(TemporaryStore store, string page, int stated)
    {
        var accepted = Accepted(store, "KEYS").Where(row => row[0] != ClaimRules.CauseSection).ToArray();
        var session = Rows(store, "SELECT MAX(session_date) FROM bar WHERE ticker = 'KEYS';").Single()[0];

        Assert.Equal(stated, accepted.Length);

        foreach (var row in accepted)
        {
            var drawn = WrittenOnThePage(page, row[0]);
            var key = row[0] == ClaimRules.ComputedSection;

            if (key && row[1] != session)
            {
                Assert.False(drawn.Success, $"{row[0]}, written for {row[1]}, is drawn beside the figures of {session}");
                Assert.Contains(
                    $"<p class=\"key-elsewhere\" data-ticker=\"KEYS\" data-section=\"{WebUtility.HtmlEncode(row[0])}\" data-written-for=\"{row[1]}\">"
                    + $"Not drawn: the newest key explains the figures of {row[1]}, and the figures on this page are {session}'s.</p>",
                    page,
                    StringComparison.Ordinal);

                continue;
            }

            Assert.True(drawn.Success, $"{row[0]} is not drawn");
            Assert.Equal(row[1], drawn.Groups[1].Value);
            Assert.Equal(row[2], WebUtility.HtmlDecode(drawn.Groups[2].Value));

            // Every paragraph the section drew, whatever it was drawn inside: a section broken
            // into parts is the prose cut, so the parts joined back up are what was stored.
            var paragraphs = Regex.Matches(drawn.Groups[3].Value, "<p class=\"prose[^\"]*\">([^<]*)</p>").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value));

            Assert.Equal(Regex.Replace(row[3], @"\s+", " ").Trim(), Regex.Replace(string.Join(" ", paragraphs), @"\s+", " ").Trim());
            Assert.Contains($"<p class=\"written-by\">{(key ? "written for the close of" : "written on")} {row[1]}</p>", drawn.Groups[3].Value, StringComparison.Ordinal);
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
    public async Task TheShortVersionIsDrawnAtTheTopWithItsDateBeneathIt()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var stored = Accepted(store, "KEYS").Single(row => row[0] == "The short version");
        var drawn = WrittenOnThePage(page, "The short version");

        Assert.True(drawn.Success);
        Assert.Equal(stored[1], drawn.Groups[1].Value);
        Assert.Equal(stored[2], WebUtility.HtmlDecode(drawn.Groups[2].Value));

        // Beneath it: the date after the last paragraph of the prose.
        var body = drawn.Groups[3].Value;

        Assert.True(
            body.LastIndexOf("<p class=\"prose\">", StringComparison.Ordinal) < body.IndexOf("<p class=\"written-by\">", StringComparison.Ordinal),
            "the date is not beneath the short version");

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

    // The case for a name and the case against it are drawn as two labelled halves where the
    // writer answered in the shape it was asked for, and as the prose was written where it did
    // not, because a section is what the checker accepted and never a shape this page hoped for.
    [Fact]
    public async Task TheTwoCasesAreDrawnAsTwoLabelledHalvesAndAsWrittenWhereTheyAreNot()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var written = WrittenOnThePage(page, MarkRenderer.TheTwoCases);

        Assert.True(written.Success, "KEYS draws no two cases, and this is what reads them.");

        var halves = Regex.Matches(written.Value, "<div class=\"case\" data-case=\"([^\"]+)\"><h4>[^<]+</h4><p class=\"prose\">([^<]*)</p></div>")
            .Select(match => (Label: match.Groups[1].Value, Prose: WebUtility.HtmlDecode(match.Groups[2].Value)))
            .ToArray();

        Assert.Equal(["The case for", "The case against"], [.. halves.Select(half => half.Label)]);

        // Each half is one of the section's own paragraphs, unchanged and in the order it was
        // written, so the labels are put beside the prose rather than over it.
        var stored = Rows(store, "SELECT prose FROM research_section r WHERE ticker = 'KEYS' AND section = 'The two cases' AND status = 'accepted' " +
            "AND version = (SELECT MAX(version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted');")
            .Single()[0];

        var paragraphs = stored.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        Assert.Equal(2, paragraphs.Length);
        Assert.Equal([.. paragraphs], [.. halves.Select(half => half.Prose)]);

        // A section of the same name written in another shape is drawn as it was written, with
        // no half and no label, which is the case every name that has never had a pass is in.
        var marks = new MarkRenderer();
        var other = marks.WrittenSection(
            "KEYS",
            new WrittenCell(MarkRenderer.TheTwoCases, "One paragraph holding both cases at once.", new DateOnly(2026, 9, 8), "a/model", []),
            []);

        Assert.DoesNotContain("class=\"case\"", other, StringComparison.Ordinal);
        Assert.Contains("<p class=\"prose\">One paragraph holding both cases at once.</p>", other, StringComparison.Ordinal);

        // And a section whose first two paragraphs open the right way and which carries a third
        // is drawn whole rather than as two halves, because two halves have nowhere to put the
        // rest of it: a shape recognised on its opening alone loses every paragraph after the
        // second without saying so.
        var third = marks.WrittenSection(
            "KEYS",
            new WrittenCell(
                MarkRenderer.TheTwoCases,
                "The bull case is the first paragraph.\n\nThe bear case is the second.\n\nAnd a third paragraph the writer added.",
                new DateOnly(2026, 9, 8),
                "a/model",
                []),
            []);

        Assert.DoesNotContain("class=\"case\"", third, StringComparison.Ordinal);
        Assert.Equal(3, Regex.Matches(third, "<p class=\"prose\">").Count);
        Assert.Contains("<p class=\"prose\">And a third paragraph the writer added.</p>", third, StringComparison.Ordinal);
    }

    // The risks the page drew, each as the risk and what would confirm it, read off the markup
    // rather than off the renderer, so a part drawn in another shape is read as no part at all.
    static IReadOnlyList<(string Risk, string? Confirmation)> RisksDrawn(string section) =>
        [.. Regex.Matches(section, "<li class=\"risk\"><p class=\"prose\">([^<]*)</p>(?:<p class=\"prose confirms\">([^<]*)</p>)?</li>")
            .Select(one => (
                WebUtility.HtmlDecode(one.Groups[1].Value),
                one.Groups[2].Success ? WebUtility.HtmlDecode(one.Groups[2].Value) : null))];

    // A part's words, the risk and what would confirm it back in one run.
    static string RiskWhole((string Risk, string? Confirmation) part) =>
        part.Confirmation is null ? part.Risk : part.Risk + " " + part.Confirmation;

    static WrittenCell RisksCell(string prose) =>
        new(MarkRenderer.TheRisks, prose, new DateOnly(2026, 9, 8), "a/model", []);

    [Fact]
    public async Task EachRiskIsDrawnAsItsOwnPartWithWhatWouldConfirmItAndAsWrittenWhereThePartsAreNotStated()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var written = WrittenOnThePage(page, MarkRenderer.TheRisks);

        Assert.True(written.Success, "KEYS draws no risks, and this is what reads them.");

        var stored = Rows(
            store,
            $"SELECT prose FROM research_section r WHERE ticker = 'KEYS' AND section = '{MarkRenderer.TheRisks}' AND status = 'accepted' " +
            "AND version = (SELECT MAX(version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted');")
            .Single()[0];

        // The fixture's risks are one paragraph of seven, each opening on an ordinal from the first,
        // so each is a part, cut where the prose says it starts. A sentence opening "A related risk"
        // names no ordinal and stays inside the part before it. The writer opened no confirmation in
        // the words it is asked for, so none is set apart and each part is drawn as written. The
        // parts joined back up are the prose as it was stored.
        var drawn = RisksDrawn(written.Value);

        Assert.DoesNotContain("\n\n", stored.Trim(), StringComparison.Ordinal);
        Assert.Equal(7, drawn.Count);
        Assert.Equal(stored.Trim(), string.Join(" ", drawn.Select(RiskWhole)));
        Assert.Equal(
            ["The first risk", "The second risk", "The third risk", "The fourth risk", "The fifth risk", "The sixth risk", "The seventh risk"],
            [.. drawn.Select(part => string.Join(' ', part.Risk.Split(' ').Take(3)))]);
        Assert.Contains(". A related risk", drawn[2].Risk, StringComparison.Ordinal);
        Assert.All(drawn, part => Assert.Null(part.Confirmation));

        var marks = new MarkRenderer();

        // Prose the writer broke into paragraphs is a part to a paragraph, unchanged and in the
        // order it was written.
        const string AParagraphEach =
            "The first risk is that supply is short [D1]. That risk would be confirmed by a fall in units [D1].\n\n" +
            "The second risk is that the price is high [D2].";

        var each = RisksDrawn(marks.WrittenSection("KEYS", RisksCell(AParagraphEach), []));

        Assert.Equal([.. AParagraphEach.Split("\n\n")], [.. each.Select(RiskWhole)]);
        Assert.Equal("That risk would be confirmed by a fall in units [D1].", each[0].Confirmation);

        // A section run together as one paragraph is cut where its own prose says a risk starts,
        // and what would confirm each is set apart from it. Nothing is dropped or reworded by
        // either: the parts joined back up are the prose as it was written, the run before the
        // first cut opening the first part rather than being lost.
        const string RunTogether =
            "These are the risks. The first risk is that supply is short [D1]. That risk would be confirmed by a fall in units [D1]. " +
            "The second risk is that the price is high [D2]. That risk would be confirmed by a lower multiple [D2]. " +
            "The third risk is concentration [D1].";

        var together = RisksDrawn(marks.WrittenSection("KEYS", RisksCell(RunTogether), []));

        Assert.Equal(3, together.Count);
        Assert.Equal(RunTogether, string.Join(" ", together.Select(RiskWhole)));
        Assert.Equal("These are the risks. The first risk is that supply is short [D1].", together[0].Risk);
        Assert.Equal("That risk would be confirmed by a fall in units [D1].", together[0].Confirmation);
        Assert.Null(together[2].Confirmation);

        // A section that says nowhere a risk starts is drawn as it was written, because a part
        // boundary this page guessed at would put one risk's words under another's.
        const string OneRun = "The company faces the risk that supply is short, which would be confirmed by a fall in units [D1].";

        var run = marks.WrittenSection("KEYS", RisksCell(OneRun), []);

        Assert.Empty(RisksDrawn(run));
        Assert.DoesNotContain("class=\"risks\"", run, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"prose\">{OneRun}</p>", run, StringComparison.Ordinal);

        // And a section of another name written in the same shape is drawn as prose: the parts
        // are this section's and not every section's.
        var other = marks.WrittenSection(
            "KEYS",
            new WrittenCell("The short version", RunTogether, new DateOnly(2026, 9, 8), "a/model", []),
            []);

        Assert.Empty(RisksDrawn(other));
        Assert.DoesNotContain("class=\"risks\"", other, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFirstOrdinalRiskSaysWhetherWhatStandsBeforeItIsItsOwnPart()
    {
        var marks = new MarkRenderer();

        // Numbered from the second: what stands before it is the first risk, a part of its own
        // with what would confirm it beneath it, so the second is never set beneath the first
        // one's confirmation.
        const string FromTheSecond =
            "The clearest risk is that supply is short [D1]. That risk would be confirmed by a fall in units [D1]. " +
            "A second risk is that the price is high [D2]. That risk would be confirmed by a lower multiple [D2]. " +
            "A third risk is concentration [D1].";

        var second = RisksDrawn(marks.WrittenSection("KEYS", RisksCell(FromTheSecond), []));

        Assert.Equal(3, second.Count);
        Assert.Equal(FromTheSecond, string.Join(" ", second.Select(RiskWhole)));
        Assert.Equal("The clearest risk is that supply is short [D1].", second[0].Risk);
        Assert.Equal("That risk would be confirmed by a fall in units [D1].", second[0].Confirmation);
        Assert.Equal("A second risk is that the price is high [D2].", second[1].Risk);
        Assert.Equal("That risk would be confirmed by a lower multiple [D2].", second[1].Confirmation);
        Assert.Equal("A third risk is concentration [D1].", second[2].Risk);

        // One numbered risk is two parts where it is the second, since the prose says where the
        // second starts.
        const string OneNumbered = "The clearest risk is that supply is short [D1]. A second risk is that the price is high [D2].";

        Assert.Equal(
            ["The clearest risk is that supply is short [D1].", "A second risk is that the price is high [D2]."],
            [.. RisksDrawn(marks.WrittenSection("KEYS", RisksCell(OneNumbered), [])).Select(RiskWhole)]);

        // Numbered from the third: two risks stand before it and the prose says nowhere where the
        // first ends, so the section is drawn as it was written.
        const string FromTheThird =
            "Supply is short [D1]. The price is high [D2]. A third risk is concentration [D1]. A fourth risk is the cycle [D2].";

        var third = marks.WrittenSection("KEYS", RisksCell(FromTheThird), []);

        Assert.Empty(RisksDrawn(third));
        Assert.DoesNotContain("class=\"risks\"", third, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"prose\">{FromTheThird}</p>", third, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheResearchedSectionsAreDrawnInSectionFoursOrderEachWithItsOwnDate()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);

        // Seven written over one pass, stated in advance, and all seven drawn as sections: the
        // cause of each move, which is drawn in the moves table where it is written, was answered
        // with nothing twice, and the page says so.
        Assert.Equal(7, Accepted(store, "KEYS").Count);
        AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(store, page, 7);
        Assert.Equal(
            $"The cause of each large move is not written: {ProseWriter.NoUsableAnswer}",
            WebUtility.HtmlDecode(Regex.Match(page, "<p class=\"not-written\" data-section=\"The cause of each large move\">([^<]*)</p>").Groups[1].Value));

        int At(string marker) => page.IndexOf(marker, StringComparison.Ordinal);
        int Section(string section) => At($"<section class=\"written-section\" data-ticker=\"KEYS\" data-section=\"{WebUtility.HtmlEncode(section)}\"");

        // Section 4's order: how it got here, the chart with the key beneath its figures and
        // the plan, then what the company sells and its segments, the numbers, the two cases,
        // and what would make it wrong. Read as the order the page draws them in, so a page
        // drawing them otherwise says which came where.
        (string Part, int At)[] drawn =
        [
            ("how it got here", At("<section class=\"how-it-got-here\"")),
            ("the chart", At("class=\"level-summary\"")),
            ("the key", Section("The key under each figure")),
            ("the plan", At("<section class=\"plan-arithmetic\"")),
            ("what it sells", Section("What the company sells")),
            ("the segments", Section("The segment commentary")),
            ("the numbers", At("<section class=\"numbers\"")),
            ("the two cases", Section("The two cases")),
            ("the risks", Section("The risks, each with what would confirm it")),
        ];

        Assert.All(drawn, part => Assert.True(part.At >= 0, $"{part.Part} is not drawn"));
        Assert.Equal(drawn.Select(part => part.Part), drawn.OrderBy(part => part.At).Select(part => part.Part));

        // The cycle is the theme's, and the research model answered the theme's call over the
        // pages its searches kept for the name's industry with nothing, so it is not drawn and
        // the page says why in the words the pass stored on its row.
        var reason = JsonDocument.Parse(Rows(store, "SELECT detail FROM run_log WHERE run_id = 'replay-research' AND stage = 'research';").Single()[0]).RootElement
            .GetProperty("notWritten").EnumerateArray()
            .Single(line => line.GetProperty("section").GetString() == "The industry cycle")
            .GetProperty("reason").GetString()!;

        Assert.StartsWith(ResearchRunner.ThemeNotRefreshed, reason, StringComparison.Ordinal);
        Assert.Equal(-1, Section("The industry cycle"));
        Assert.Equal(
            $"The industry cycle is not written: {reason}",
            WebUtility.HtmlDecode(Regex.Match(page, "<p class=\"not-written\" data-section=\"The industry cycle\">([^<]*)</p>").Groups[1].Value));
    }

    [Fact]
    public async Task NoModelThatWroteASectionIsNamedInThePagesWords()
    {
        // The store keeps the model that wrote each section and the element each is drawn in
        // carries it, and the words a reader reads name none of them.
        using var store = await FixtureReplay.ResearchedAsync();

        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var models = Rows(store, "SELECT DISTINCT model FROM research_section WHERE ticker = 'KEYS' ORDER BY model;").Select(row => row[0]).ToArray();

        Assert.Equal(2, models.Length);
        Assert.All(models, model => Assert.Contains($"data-model=\"{WebUtility.HtmlEncode(model)}\"", page, StringComparison.Ordinal));

        var words = WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(page, @"<(script|style)[^>]*>[\s\S]*?</\1>", " "), "<[^>]+>", " "));

        Assert.DoesNotContain(models, model => words.Contains(model, StringComparison.Ordinal));
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

        // Four priced calls over two passes before this page is opened, one of them an answer
        // the provider billed and that could not be stored, and a paused call that is not a
        // price. The most one pass cost is not the most one call cost, which the 6.8 sweep
        // found the data this test first held could not tell apart.
        Spend(store, "research-a", "research call: The two cases", "2026-09-14T19:00:00Z", "0.012");
        Spend(store, "research-a", "research call: The short version, round 2", "2026-09-14T19:02:00Z", "0.004");
        Spend(store, "research-b", "research call: The two cases", "2026-09-15T18:00:00Z", "0.011");
        Spend(store, "research-b", "research call: The two cases, round 2", "2026-09-15T18:05:00Z", "0.010", outcome: "refused");
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
    public async Task APressThatFoundNothingToDoLeavesThePageDrawingThePassThatDid()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        IClock night = FixedClock.At(DateTimeOffset.Parse("2026-09-08T21:10:00Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates);

        // A pass with no article and no release, which writes what rests on the facts file
        // and names every other section as not written, and an open the same day after it,
        // which starts nothing and says so on its own row.
        await FixtureReplay.Researcher(store, night, archive: new FixtureExpectations.NoRelease(), news: new FixtureExpectations.NoArticles()).RunAsync("KEYS", "research-nothing-page");

        var again = await FixtureReplay.Researcher(store, night, archive: new FixtureExpectations.NoRelease(), news: new FixtureExpectations.NoArticles()).RunAsync("KEYS", "research-nothing-again-page");

        Assert.Equal(ResearchRunner.NotWarranted, again.Outcome);

        // The page still draws what the pass that ran came to and the sections it could not
        // write, rather than the press that did nothing, which the 6.8 sweep found no test
        // telling apart.
        var page = await ResearchedPage(store, "KEYS", AWeekLater);

        Assert.Contains("<p class=\"research-pass\" data-outcome=\"ok\" data-as-of=\"2026-09-08\">the newest research pass for this name ran on 2026-09-08</p>", page, StringComparison.Ordinal);
        Assert.Equal(
            $"The two cases is not written: {ProseWriter.NothingHanded}",
            WebUtility.HtmlDecode(Regex.Match(page, "<p class=\"not-written\" data-section=\"The two cases\">([^<]*)</p>").Groups[1].Value));
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

        // The theme's searches find nothing, so the first call the cap refuses is one of the
        // name's own.
        var outcome = await FixtureReplay.Researcher(store, FixedClock.At(night, SessionZones.UnitedStates), caps: caps, search: new FixtureExpectations.NoResults()).RunAsync("KEYS", "research-short-of-the-cap");

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

    // The settings are how a test reaches configuration the surface reads at a press,
    // which is where the lane is decided. The clock is how it reaches the instant a press
    // is written at, which is half of a request's key.
    sealed class PassHost(string root, params (string Key, string Value)[] settings) : WebApplicationFactory<ReadApi>
    {
        public IClock? Clock { get; init; }

        // What a press starts, held by the test so a route is hosted without a drain reaching
        // a model. A host given none starts nothing either, since the surface the suite hosts
        // runs from the suite's own build and finds no worker beside it.
        public IDrainLauncher? Launcher { get; init; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(StoreLocation.DataRootKey, root);

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            if (Clock is { } clock)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(clock));
            }

            if (Launcher is { } launcher)
            {
                builder.ConfigureTestServices(services => services.AddSingleton(launcher));
            }
        }
    }

    static HttpRequestMessage Press(string route, string ticker, string? header, params (string Name, string Value)[] form)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, route + ticker)
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
    public async Task TheControlsRouteWritesOneRequestStartsOneDrainAndWritesNoResearch()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var launcher = new RecordingLauncher();

        using var host = new PassHost(store.Root) { Launcher = launcher };
        using var client = host.CreateClient();

        // The host is up before anything is counted, since starting it writes its own row.
        Assert.Contains("KEYS", await client.GetStringAsync("/screens/name/KEYS"), StringComparison.Ordinal);

        string Held() => string.Join(
            ";",
            Rows(store, "SELECT COUNT(*) FROM research_section;").Single()[0],
            Rows(store, "SELECT COUNT(*) FROM source_document;").Single()[0]);

        var before = Held();

        var pressed = await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "list")));

        Assert.Equal(HttpStatusCode.Accepted, pressed.StatusCode);
        Assert.Contains("data-started=\"true\"", await pressed.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal([["KEYS", "list", "paid", "outstanding"]], Rows(store, "SELECT ticker, asked_from, lane, state FROM research_request;"));

        // A second press for a name already in the queue adds nothing and says so, which is
        // the index refusing rather than the surface reading first and racing itself.
        var again = await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "name")));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already in the queue", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(Rows(store, "SELECT ticker FROM research_request;"));

        // A request not carrying the page's header, and one carrying another value, queue nothing.
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Press(SinglePageApp.PassRoute, "AAPL", null))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Press(SinglePageApp.PassRoute, "AAPL", "another-page"))).StatusCode);

        // A name the index does not hold queues nothing either, refused before the store is reached.
        var outside = await client.SendAsync(Press(SinglePageApp.PassRoute, "ZZZZ", SinglePageApp.PassHeaderValue));

        Assert.Equal(HttpStatusCode.NotFound, outside.StatusCode);
        Assert.Contains("data-refused=\"membership\"", await outside.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Single(Rows(store, "SELECT ticker FROM research_request;"));

        // The surface wrote no research across all of it: a request is an ask, and what it
        // leads to is the worker's, whose drain the one press that wrote a request started.
        Assert.Equal(before, Held());
        Assert.Equal(1, launcher.Started);
    }

    [Fact]
    public async Task ARequestNobodyHasStartedIsWithdrawnAndOneTheWorkerHoldsIsNot()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        Assert.Contains("KEYS", await client.GetStringAsync("/screens/name/KEYS"), StringComparison.Ordinal);

        await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "list")));

        var askedAt = Rows(store, "SELECT asked_at FROM research_request WHERE ticker = 'KEYS';").Single()[0];

        // Refused without the header, as the ask is, and refused naming no request.
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await client.SendAsync(Press(SinglePageApp.WithdrawRoute, "KEYS", null, ("askedAt", askedAt)))).StatusCode);

        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.SendAsync(Press(SinglePageApp.WithdrawRoute, "KEYS", SinglePageApp.PassHeaderValue))).StatusCode);

        var taken = await client.SendAsync(Press(SinglePageApp.WithdrawRoute, "KEYS", SinglePageApp.PassHeaderValue, ("askedAt", askedAt)));

        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);
        Assert.Contains("data-withdrawn=\"true\"", await taken.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Nothing is deleted: what was asked for and what came of it are both still read.
        Assert.Equal([["KEYS", "withdrawn"]], Rows(store, "SELECT ticker, state FROM research_request;"));

        // A request the worker holds is not one that has not been generated, so the same
        // press is refused and says which state refused it.
        Rows(store, "UPDATE research_request SET state = 'writing', settled_at = NULL WHERE ticker = 'KEYS';");

        var held = await client.SendAsync(Press(SinglePageApp.WithdrawRoute, "KEYS", SinglePageApp.PassHeaderValue, ("askedAt", askedAt)));

        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        Assert.Contains("is writing", await held.Content.ReadAsStringAsync(), StringComparison.Ordinal);
        Assert.Equal([["KEYS", "writing"]], Rows(store, "SELECT ticker, state FROM research_request;"));
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
        // two sections, one on the night and one before; FFFF wrote two sections on the
        // night, which is one report; and GGGG's section was accepted before the night and
        // again after it, so as of the night its prose is the older version. The last two
        // are what the 6.8 sweep found the rows could not tell apart.
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
            "('EEEE', 'The short version', 1, '2026-09-03', 'm', 'accepted', 'p', '[]', NULL), " +
            "('FFFF', 'The two cases', 1, '2026-09-08', 'm', 'accepted', 'p', '[]', NULL), " +
            "('FFFF', 'The short version', 1, '2026-09-08', 'm', 'accepted', 'p', '[]', NULL), " +
            "('GGGG', 'The two cases', 1, '2026-09-01', 'm', 'accepted', 'p', '[]', NULL), " +
            "('GGGG', 'The two cases', 2, '2026-09-10', 'm', 'accepted', 'p', '[]', NULL), " +
            // The key under each figure, which the overnight queue writes for every name
            // each night whatever was researched. HHHH holds it and nothing else and is a
            // name with no report; AAAA holds it beside research and is still one name.
            // Counting it would make this figure a count of the index: on the operator's
            // store it stood at 502 where one name held research.
            "('HHHH', 'The key under each figure', 1, '2026-09-08', 'm', 'accepted', 'p', '[]', NULL), " +
            "('AAAA', 'The key under each figure', 1, '2026-09-08', 'm', 'accepted', 'p', '[]', NULL);");

        var prose = TonightScreen.Prose(night, await Api(store).WrittenOnOrBeforeAsync(night));
        var header = new MarkRenderer().NightHeader(night, 503, 4, "00:03:10", null, null, prose);
        var line = Regex.Match(header, "<p class=\"night-prose\" data-fresh=\"(\\d+)\" data-reused=\"(\\d+)\" data-names=\"(\\d+)\">([^<]*)</p>");

        Assert.True(line.Success);

        // Counted by a query of the test's own: a name is fresh where an accepted section is
        // dated the night, and reused where it has accepted sections and none is.
        const string NotTheKey = "AND section <> 'The key under each figure' ";

        var fresh = Rows(store, "SELECT COUNT(DISTINCT ticker) FROM research_section WHERE status = 'accepted' " + NotTheKey + "AND as_of = '2026-09-08';").Single()[0];
        var names = Rows(store, "SELECT COUNT(DISTINCT ticker) FROM research_section WHERE status = 'accepted' " + NotTheKey + "AND as_of <= '2026-09-08';").Single()[0];

        // The same counts without the filter, which is what this figure used to be. They
        // differ over this store, so the filter is shown to do something rather than being
        // asserted over data where it could not.
        var everySection = Rows(store, "SELECT COUNT(DISTINCT ticker) FROM research_section WHERE status = 'accepted' AND as_of <= '2026-09-08';").Single()[0];

        Assert.Equal("7", everySection);
        Assert.NotEqual(everySection, names);

        Assert.Equal(fresh, line.Groups[1].Value);
        Assert.Equal(names, line.Groups[3].Value);

        // Stated in advance, so a count that read every row would not agree with itself.
        Assert.Equal("3", line.Groups[1].Value);
        Assert.Equal("3", line.Groups[2].Value);
        Assert.Equal("6", line.Groups[3].Value);
        Assert.Equal(
            "research prose: 3 report(s) carry prose written on 2026-09-08 and 3 carry only prose written before it, of the 6 name(s) with a written section",
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
