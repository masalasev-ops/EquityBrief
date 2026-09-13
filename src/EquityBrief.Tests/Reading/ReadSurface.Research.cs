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

// read-surface, 6.6: the name page's first regions a written section reaches, being
// the cause of each move and the provenance footer, and the page's half of section
// 18's two rows about the local lane failing. Each read back off the markup against
// the store by a query of the test's own, so the page and the read surface cannot
// agree with each other by sharing a mistake.
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
            prosePass: await api.NewestProsePassAsync(ticker));
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

    [Fact]
    public async Task TheProvenanceFooterStatesWhereEveryPartOfThePageCameFrom()
    {
        using var store = await FixtureExpectations.WithWrittenRelease();

        var region = await NamePageWithResearch(store, "KEYS");
        var footer = Regex.Match(region, "<footer class=\"provenance\"[^>]*>(.*?)</footer>", RegexOptions.Singleline);

        Assert.True(footer.Success);

        // The computed parts, as of the newest session stored for the name.
        var through = Rows(store, "SELECT MAX(session_date) FROM bar WHERE ticker = 'KEYS';").Single()[0];

        Assert.Contains($"<p data-part=\"computed\" data-as-of=\"{through}\">", footer.Value, StringComparison.Ordinal);

        // The numbers, as of the newest filing stored for the name.
        var filed = Rows(store, "SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'KEYS';").Single()[0];

        Assert.Contains($"<p data-part=\"fundamentals\" data-filed-on=\"{filed}\">", footer.Value, StringComparison.Ordinal);

        // Each written section, as of its date and naming its model: the newest
        // accepted version of each, which is what a reader is shown.
        var written = Rows(
            store,
            "SELECT section, as_of, model FROM research_section r WHERE ticker = 'KEYS' AND status = 'accepted' " +
            "AND version = (SELECT MAX(version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section AND s.status = 'accepted') " +
            "ORDER BY section;");

        // Four written over the two passes, stated in advance: three on the first
        // draft and the key under each figure on its retry.
        Assert.Equal(4, written.Count);

        var drawn = Regex.Matches(footer.Value, "<p data-part=\"research\" data-section=\"([^\"]*)\" data-as-of=\"([^\"]*)\" data-model=\"([^\"]*)\">")
            .Select(match => new[] { WebUtility.HtmlDecode(match.Groups[1].Value), match.Groups[2].Value, WebUtility.HtmlDecode(match.Groups[3].Value) })
            .ToArray();

        Assert.Equal(written.Select(row => string.Join("|", row)), drawn.Select(row => string.Join("|", row)));
        Assert.Contains($"data-written=\"{written.Count}\"", region, StringComparison.Ordinal);

        // A name with nothing written, and a part with nothing behind it, each say so.
        using var replayed = await FixtureReplay.ReplayedAsync();

        Assert.Contains("<p data-part=\"research\" data-section=\"none\">", await NamePageWithResearch(replayed, "KEYS"), StringComparison.Ordinal);

        var marks = new MarkRenderer();

        Assert.Contains("data-filed-on=\"none\"", marks.ProvenanceFooter("KEYS", new DateOnly(2026, 9, 8), null, []), StringComparison.Ordinal);
        Assert.Contains("data-as-of=\"none\"", marks.ProvenanceFooter("KEYS", null, null, []), StringComparison.Ordinal);
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
                    ProseWriter.DefaultLane,
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
                    ProseWriter.DefaultLane,
                    FixtureExpectations.ProseClock,
                    store.DatabaseFile)
                .WriteAsync("KEYS", FixtureExpectations.Handed(document), "prose-unavailable-page");

            var region = await NamePageWithResearch(store, "KEYS");
            var stored = NotWrittenOnTheRunLog(store, "prose-unavailable-page");

            Assert.Equal(ProseWriter.DefaultLane, stored.Select(line => line.Section).ToArray());

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
                [.. ProseWriter.DefaultLane.Select(section => new WrittenSectionRow(section, 1, new DateOnly(2026, 9, 8), LocalModelSettings.DefaultModel, "prose", "[]"))],
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
