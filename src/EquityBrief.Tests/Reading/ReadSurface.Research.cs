using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Reading;

// read-surface, 6.6: the name page's first regions a written section reaches, being the
// cause of each move, what refused a section the checker left out, and where each part of
// the page states the date it is as of; and the page's half of section 18's two rows about
// the local lane failing. Each read back off the markup against the store by a query of
// the test's own, so the page and the read surface cannot agree with each other by sharing
// a mistake.
public partial class ReadSurface
{
    static async Task<string> NamePageWithResearch(TemporaryStore store, string ticker)
    {
        var api = Api(store);

        return NameScreen.Region(
            new SinglePageApp(),
            new MarkRenderer(),
            ticker,
            await api.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue),
            await api.LevelsAsync(ticker),
            await api.ProfileAsync(ticker),
            await api.LadderAsync(ticker),
            await api.NextEventAsync(ticker, DateOnly.MinValue),
            await api.MovesAsync(ticker),
            await api.FundamentalsAsync(ticker),
            sections: await api.SectionStatesAsync(ticker, DateOnly.MaxValue),
            staleness: await api.StalenessAsync(ticker),
            written: await api.WrittenSectionsAsync(ticker),
            pass: await api.NewestPassAsync(ticker));
    }

    static IReadOnlyList<string[]> Rows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string[]>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(field => reader.IsDBNull(field) ? "null" : reader.GetValue(field).ToString()!)]);
        }

        return rows;
    }

    [Fact]
    public void TheReadSurfaceNamesTheStageTheProseWriterRecordsItselfUnder()
    {
        // The read surface cannot reference the worker, so the stage is stated twice
        // and this is what keeps the two statements one.
        Assert.Equal(ProseWriter.Stage, ReadApi.ProseStage);
    }

    [Fact]
    public async Task TheCauseOfEachMoveIsDrawnInTheRowOfTheMoveItNamesAndNoOther()
    {
        using var store = await FixtureExpectations.WithWrittenRelease();

        var region = await NamePageWithResearch(store, "KEYS");

        // The accepted cause section, read by a query of the test's own.
        var cause = Rows(
            store,
            $"SELECT as_of, model, prose FROM research_section WHERE ticker = 'KEYS' AND section = '{ClaimRules.CauseSection}' " +
            "AND status = 'accepted' ORDER BY version DESC LIMIT 1;").Single();

        var table = Regex.Match(region, "<table class=\"moves-table\"[^>]*>").Value;

        Assert.Contains("data-cause-column=\"written\"", table, StringComparison.Ordinal);
        Assert.Contains($"data-cause-as-of=\"{cause[0]}\"", table, StringComparison.Ordinal);
        Assert.Contains($"data-cause-model=\"{WebUtility.HtmlEncode(cause[1])}\"", table, StringComparison.Ordinal);

        // The session each stored move ended on, and the ones the prose names, which
        // the prose fixture's expectation worked out by hand.
        var sessions = Rows(store, "SELECT session_date FROM move WHERE ticker = 'KEYS' ORDER BY rank;").Select(row => row[0]).ToArray();
        var named = FixtureExpectation.Of("prose").GetProperty("release").GetProperty("causeNames").EnumerateArray().Select(item => item.GetString()!).ToArray();

        Assert.Equal(8, sessions.Length);
        Assert.NotEmpty(named);

        foreach (var session in sessions)
        {
            var row = Regex.Match(region, $"<tr data-session-date=\"{session}\"[^>]*>(.*?)</tr>", RegexOptions.Singleline);

            Assert.True(row.Success, $"no row for {session}");

            var cell = Regex.Match(row.Groups[1].Value, "<td class=\"cause\" data-cause=\"([a-z]+)\">([^<]*)</td>");

            Assert.True(cell.Success, $"the row for {session} carries no cause cell");

            if (named.Contains(session))
            {
                Assert.Equal("written", cell.Groups[1].Value);
                Assert.Equal(cause[2], WebUtility.HtmlDecode(cell.Groups[2].Value));
            }
            else
            {
                Assert.Equal("none", cell.Groups[1].Value);
            }
        }

        Assert.Equal(named.Length, Regex.Matches(region, "data-cause=\"written\"").Count);
        Assert.DoesNotContain("data-cause=\"absent\"", region, StringComparison.Ordinal);

        // A name no cause section was accepted for draws the column as absent, said
        // once rather than as an empty cell in every row.
        using var replayed = await FixtureReplay.ReplayedAsync();

        var none = await NamePageWithResearch(replayed, "KEYS");

        Assert.Contains("data-cause-column=\"absent\"", none, StringComparison.Ordinal);
        Assert.DoesNotContain("<td class=\"cause\"", none, StringComparison.Ordinal);
    }

    // What refused a section, as the name page states it. Which rules record the draft's
    // own sentence rather than something the checker extracted is read off the shipped
    // checker here rather than listed a second time beside this test, so a rule that
    // starts recording a sentence is found by this and not by a reader.
    // see: A refused draft's own words are kept on the row and drawn on the evidence page, and never on the name page
    [Fact]
    public async Task ThePageNamesEveryRuleThatRefusedASectionAndNeverTheWordsOfTheRefusedDraft()
    {
        // The page states the second refusal in the checker's own word.
        Assert.Equal(ClaimChecker.RejectedTwice, NameScreen.RejectedTwice);

        var admitted = new StoredDocument(
            "d1", "https://a.test/a", "a release", new DateOnly(2026, 9, 1),
            DateTimeOffset.UnixEpoch, "text", Admissibility.Accepted);

        // Prose written to break four rules at once, two of which the checker records the
        // sentence itself for and two of which it records what it pulled out of one. The
        // figure rule is broken twice, with a different figure each time.
        var researched = ClaimRules.Check(
            "What the company sells",
            "It earned 12.34 last year [D1]. It earned 56.78 the year before [D1]. " +
            "It sells test gear and nobody said so. It cites too far [D4].",
            [],
            [admitted]);

        var cause = ClaimRules.Check(
            ClaimRules.CauseSection,
            "The move ending 2026-08-24 fell after a release [D1].",
            [],
            [admitted]);

        var findings = (IReadOnlyList<ClaimFinding>)[.. researched.Findings, .. cause.Findings];

        // Stated in advance: four rules over five findings, the figure rule twice.
        Assert.Equal(6, findings.Count);
        Assert.Equal(
            [
                ClaimRules.UnmatchedFigure, ClaimRules.Uncited, ClaimRules.CitationOutOfRange,
                ClaimRules.CauseNamingNoMove, ClaimRules.UnknownDate,
            ],
            [.. findings.Select(finding => finding.Reason).Distinct()]);

        // The two the page drops its text for are exactly the two the checker records the
        // whole sentence for, read from the findings rather than named here.
        var sentences = findings
            .Where(finding => string.Equals(finding.Offending, finding.Sentence, StringComparison.Ordinal))
            .Select(finding => finding.Reason)
            .Distinct()
            .Order(StringComparer.Ordinal);

        Assert.Equal([ClaimRules.CauseNamingNoMove, ClaimRules.Uncited], sentences);

        // The reason as the checker composes and stores it, and as the page states it:
        // every rule, the extracted text beside the two rules that carry one, the two
        // figures collected under their one rule, and no sentence of the draft anywhere.
        var stored = ClaimChecker.RejectedTwice + ": " + string.Join(
            "; ",
            findings.Select(finding => $"{finding.Reason}: {finding.Offending}"));

        Assert.Equal(
            ClaimChecker.RejectedTwice + ": " +
            ClaimRules.UnmatchedFigure + ": 12.34, 56.78; " +
            ClaimRules.Uncited + "; " +
            ClaimRules.CitationOutOfRange + ": D4; " +
            ClaimRules.CauseNamingNoMove + "; " +
            ClaimRules.UnknownDate + ": 2026-08-24",
            NameScreen.Refused(stored));

        foreach (var finding in findings.Where(finding => finding.Sentence.Length > 0))
        {
            Assert.DoesNotContain(finding.Sentence, NameScreen.Refused(stored), StringComparison.Ordinal);
        }

        // A reason carrying no second refusal is drawn as it was stored, which leaves the
        // shorter reasons the local lane records alone.
        Assert.Equal(NameScreen.LocalUnavailable, NameScreen.Refused(NameScreen.LocalUnavailable));
        Assert.Equal(ClaimRules.NoAdmissibleSource, NameScreen.Refused(ClaimRules.NoAdmissibleSource));

        // And on the page, off a row the checker's own words were stored on.
        using var store = await FixtureExpectations.WithWrittenRelease();

        Insert(
            store,
            "INSERT INTO research_section VALUES ('KEYS', 'The two cases', 1, '2026-09-08', 'a/model', 'fallback', '', '[]', " +
            "'" + stored.Replace("'", "''") + "');");

        var region = await NamePageWithResearch(store, "KEYS");
        var line = Regex.Match(region, "<p class=\"left-out\" data-section=\"The two cases\">(.*?)</p>", RegexOptions.Singleline);

        Assert.True(line.Success);
        Assert.Equal(
            "The two cases is left out: " + NameScreen.Refused(stored),
            WebUtility.HtmlDecode(line.Groups[1].Value));
    }

    // Every part of the page states as of when on the card that holds it, which is where
    // a reader meets it, and no region restates the same dates in one list of its own.
    // see: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
    [Fact]
    public async Task EveryPartOfThePageStatesAsOfWhenOnTheCardThatHoldsIt()
    {
        using var store = await FixtureExpectations.WithWrittenRelease();

        // A newer draft of a written section still waiting on the checker, by another
        // model on a later day. What a reader is shown is what passed, so the card
        // keeps naming the accepted version's date.
        Insert(
            store,
            "INSERT INTO research_section VALUES ('KEYS', 'What the company sells', 2, '2026-09-09', 'another/model', 'pending', 'a draft', '[]', NULL);");

        var region = await NamePageWithResearch(store, "KEYS");

        Assert.DoesNotContain("another/model", region, StringComparison.Ordinal);

        // Nothing restates the dates in a region of its own.
        Assert.DoesNotContain("class=\"provenance\"", region, StringComparison.Ordinal);
        Assert.DoesNotContain("data-part=\"", region, StringComparison.Ordinal);

        // The computed cards, stamped with the newest session stored for the name. Every
        // one of them, so a card added without its stamp fails rather than going unread.
        var through = Rows(store, "SELECT MAX(session_date) FROM bar WHERE ticker = 'KEYS';").Single()[0];
        var stamps = Regex.Matches(region, "<span class=\"stamp computed\">([^<]*)</span>").Select(match => match.Groups[1].Value).ToArray();

        Assert.NotEmpty(stamps);
        Assert.All(stamps, stamp => Assert.Equal($"Computed for {through}", stamp));

        // The numbers, on the spine of their own card, as of the newest filing stored.
        var filed = Rows(store, "SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'KEYS';").Single()[0];

        Assert.Contains($"<span class=\"dl-k\">Filed</span><b>{filed}</b>", region, StringComparison.Ordinal);

        // Each written section, on its own card and again beneath its prose: the newest
        // accepted version of each, which is what a reader is shown.
        var written = Rows(
            store,
            "SELECT section, as_of FROM research_section r WHERE ticker = 'KEYS' AND status = 'accepted' " +
            "AND version = (SELECT MAX(version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted') " +
            "ORDER BY section;");

        // Four written over the two passes, stated in advance: three on the first
        // draft and the key under each figure on its retry.
        Assert.Equal(4, written.Count);

        foreach (var row in written)
        {
            var key = row[0] == ClaimRules.ComputedSection;
            var spine = key ? SinglePageApp.KeyDated : "Written";

            Assert.Contains($"<span class=\"dl-k\">{spine}</span><b>{row[1]}</b>", region, StringComparison.Ordinal);
            Assert.Contains(
                $"<p class=\"written-by\">{(key ? "written for the close of" : "written on")} {row[1]}</p>",
                region,
                StringComparison.Ordinal);
        }

        // A name with nothing written draws no written card and still stamps its computed
        // ones, so the page never falls silent about a date it does hold.
        using var replayed = await FixtureReplay.ReplayedAsync();
        var bare = await NamePageWithResearch(replayed, "KEYS");

        Assert.DoesNotContain("<p class=\"written-by\">", bare, StringComparison.Ordinal);
        Assert.Contains("<span class=\"stamp computed\">Computed for ", bare, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASectionTheMachineCannotHoldIsAbsentAsUsualWithTheReasonTheRunLogStored()
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
                .WriteAsync("KEYS", FixtureExpectations.Handed(document), "prose-cannot-hold-page");

            await new ClaimChecker(FixtureExpectations.ProseClock, store.DatabaseFile).RunAsync("claims-cannot-hold-page");

            var region = await NamePageWithResearch(store, "KEYS");
            var stored = NotWrittenOnTheRunLog(store, "prose-cannot-hold-page");

            Assert.Equal(3, stored.Count);

            foreach (var (section, reason) in stored)
            {
                Assert.StartsWith(ProseWriter.CannotHold, reason, StringComparison.Ordinal);

                // Absent as usual: no stored row, and no written part in the footer.
                Assert.Empty(Rows(store, $"SELECT version FROM research_section WHERE ticker = 'KEYS' AND section = '{section}';"));
                Assert.DoesNotContain($"data-part=\"research\" data-section=\"{WebUtility.HtmlEncode(section)}\"", region, StringComparison.Ordinal);

                AssertNotWrittenLine(region, section, reason);
            }

            Assert.Contains($"data-not-written=\"{stored.Count}\"", region, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TheLocalLaneSectionsAnUnavailableModelLeftAreDrawnAbsentWithTheirReason()
    {
        var (store, document) = await FixtureExpectations.WithRelease();

        using (store)
        {
            using var client = new HttpClient(new NothingAnswers()) { BaseAddress = new Uri(LocalModelSettings.DefaultBaseAddress) };

            await new ProseWriter(
                    new OpenAiCompatibleModelFeed(client, FixtureExpectations.LocalSettings()),
                    FixtureExpectations.LocalSettings(),
                    FixtureExpectations.ReleaseLane,
                    FixtureExpectations.ProseClock,
                    store.DatabaseFile)
                .WriteAsync("KEYS", FixtureExpectations.Handed(document), "prose-unavailable-page");

            var region = await NamePageWithResearch(store, "KEYS");
            var stored = NotWrittenOnTheRunLog(store, "prose-unavailable-page");

            Assert.Equal(FixtureExpectations.ReleaseLane, stored.Select(line => line.Section).ToArray());

            foreach (var (section, reason) in stored)
            {
                Assert.StartsWith(ProseWriter.Unavailable, reason, StringComparison.Ordinal);

                AssertNotWrittenLine(region, section, reason);
            }

            Assert.Contains($"data-not-written=\"{stored.Count}\"", region, StringComparison.Ordinal);

            // The line is about the newest pass: a later pass that writes the section
            // takes it off the page, because a written section is drawn as written.
            Assert.Empty(NameScreen.NotWritten(
                Rows(store, "SELECT detail FROM run_log WHERE run_id = 'prose-unavailable-page';").Single()[0],
                [.. FixtureExpectations.ReleaseLane.Select(section => new WrittenSectionRow(section, 1, new DateOnly(2026, 9, 8), LocalModelSettings.DefaultModel, "prose", "[]"))],
                []));
        }
    }

    static IReadOnlyList<(string Section, string Reason)> NotWrittenOnTheRunLog(TemporaryStore store, string runId)
    {
        using var detail = JsonDocument.Parse(Rows(store, $"SELECT detail FROM run_log WHERE run_id = '{runId}';").Single()[0]);

        return
        [
            .. detail.RootElement.GetProperty("notWritten").EnumerateArray()
                .Select(line => (line.GetProperty("section").GetString()!, line.GetProperty("reason").GetString()!)),
        ];
    }

    static void AssertNotWrittenLine(string region, string section, string reason)
    {
        var line = Regex.Match(region, $"<p class=\"not-written\" data-section=\"{Regex.Escape(WebUtility.HtmlEncode(section))}\">([^<]*)</p>");

        Assert.True(line.Success, $"no line for {section}");
        Assert.Equal($"{section} is not written: {reason}", WebUtility.HtmlDecode(line.Groups[1].Value));
    }

    sealed class NothingAnswers : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No connection could be made because the target machine actively refused it.");
    }
}
