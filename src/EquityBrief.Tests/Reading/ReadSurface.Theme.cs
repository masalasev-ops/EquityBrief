using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, 6.9: the industry cycle on the name page, which is the theme's. Every member
// of an industry draws the one record its theme holds, and a name whose theme could not be
// refreshed draws every section but the cycle and one line saying why, read back off the
// markup against rows of the test's own.
public partial class ReadSurface
{
    static readonly DateTimeOffset ThemePassAt = DateTimeOffset.Parse("2026-09-08T21:10:00Z", CultureInfo.InvariantCulture);

    const string Instruments = "Scientific & Technical Instruments";

    // The cycle cites two pages, each naming a word of its industry's own name, the fewest it is written over.
    static void ThemeCycle(TemporaryStore store, int version, string asOf, string status, string prose)
    {
        store.Execute(
            "INSERT INTO theme_section VALUES " +
            $"('{Instruments}', '{ClaimRules.CycleSection}', {version}, '{asOf}', 'deepseek-flash', '{status}', '{prose}', '[\"theme-page-1\",\"theme-page-2\"]', NULL, '[\"{Instruments}\"]');");
    }

    static void ThemeDocument(TemporaryStore store) =>
        store.Execute(
            "INSERT INTO source_document (id, url, title, published_on, fetched_at, body, admissibility) VALUES " +
            "('theme-page-1', 'https://www.semiconductors.org/a-page', 'A page on the industry', '2026-09-01', '2026-09-08T21:10:00Z', 'The scientific instruments industry text.', 'accepted'), " +
            "('theme-page-2', 'https://www.example.org/another-page', 'Another page on the industry', '2026-09-02', '2026-09-08T21:10:00Z', 'Orders for technical instruments.', 'accepted');");

    static IReadOnlyList<Match> NotWrittenLines(string page, string section) =>
        [.. Regex.Matches(page, $"<p class=\"not-written\" data-section=\"{Regex.Escape(WebUtility.HtmlEncode(section))}\">([^<]*)</p>").Cast<Match>()];

    [Fact]
    public async Task EveryMemberOfAnIndustryDrawsTheOneCycleItsThemeWrote()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // Two fixture names put in one industry, one theme cycle accepted for it citing a
        // stored page, and a newer draft still waiting on the checker, which no page draws.
        store.Execute($"UPDATE membership SET industry = '{Instruments}' WHERE ticker = 'AAPL';");
        ThemeDocument(store);
        ThemeCycle(store, 1, "2026-09-08", "accepted", "Orders across the industry are turning up from a low [D1][D2].");
        ThemeCycle(store, 2, "2026-09-09", "pending", "A draft nobody has checked [D1].");

        foreach (var ticker in new[] { "KEYS", "AAPL" })
        {
            var page = await ResearchedPage(store, ticker, AWeekLater);
            var drawn = WrittenOnThePage(page, ClaimRules.CycleSection);

            Assert.True(drawn.Success, $"{ticker}'s page draws no industry cycle.");
            Assert.Equal("2026-09-08", drawn.Groups[1].Value);
            Assert.Equal("deepseek-flash", drawn.Groups[2].Value);
            Assert.Contains("Orders across the industry are turning up from a low.", WebUtility.HtmlDecode(drawn.Groups[3].Value), StringComparison.Ordinal);
            Assert.DoesNotContain("A draft nobody has checked", page, StringComparison.Ordinal);

            // The page it cites is among the name's sources, with its date and link.
            Assert.Contains("data-document=\"theme-page-1\"", page, StringComparison.Ordinal);
        }

        // A member of another industry draws none.
        Assert.False(WrittenOnThePage(await ResearchedPage(store, "MSFT", AWeekLater), ClaimRules.CycleSection).Success);

        // Where a page it cites names neither the company nor a word of its industry, that page leaves the cycle's sources,
        // and with fewer than two left the cycle says so in place of its paragraph.
        // see: A stock's industry group is the provider's until an override is set and is checked against its own account, and the sources list and the cycle read only documents naming the company or its industry in their opening
        store.Execute("UPDATE source_document SET title = 'Bond markets this week', body = 'Yields moved.', url = 'https://www.example.org/bonds' WHERE id = 'theme-page-2';");
        store.Execute($"INSERT INTO company (ticker, fetched_at, industry) VALUES ('KEYS', '2026-09-30T00:00:00Z', '{Instruments}');");

        // Read through the page's own route, which reads the industry the page names the stock by.
        using var host = new Host(store.Root, FixedClock.At(AWeekLater, SessionZones.UnitedStates));
        using var client = host.CreateClient();

        var thin = WrittenOnThePage(await client.GetStringAsync("/screens/name/KEYS"), ClaimRules.CycleSection);

        Assert.True(thin.Success);
        Assert.Contains("Not enough relevant sources: 1 of the 2 documents it rests on name the company or its industry", WebUtility.HtmlDecode(thin.Groups[3].Value), StringComparison.Ordinal);
        Assert.DoesNotContain("data-document=\"theme-page-2\"", thin.Groups[3].Value, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameWhoseThemeCouldNotBeRefreshedDrawsEverySectionButTheCycleAndOneLine()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        var refused = new FixtureExpectations.RefusingSearch("The search tool refused the search with status 401: Unauthorized: missing or invalid API key.");

        await FixtureReplay.Researcher(store, FixedClock.At(ThemePassAt, SessionZones.UnitedStates), search: refused).RunAsync("KEYS", "research-theme-fails-page");

        var page = await ResearchedPage(store, "KEYS", AWeekLater);

        // Every section the name wrote of its own, under the date the store holds, and no cycle.
        AssertEveryAcceptedSectionIsDrawnWithItsOwnDate(store, page, 6);
        Assert.False(WrittenOnThePage(page, ClaimRules.CycleSection).Success);

        // And one line saying the theme could not be refreshed.
        var lines = NotWrittenLines(page, ClaimRules.CycleSection);

        Assert.Single(lines);
        Assert.Equal(
            $"{ClaimRules.CycleSection} is not written: {ResearchRunner.ThemeNotRefreshed}{refused.Line}",
            WebUtility.HtmlDecode(lines[0].Groups[1].Value));
    }

    [Fact]
    public async Task ACycleWrittenBeforeARefreshThatFailedIsDrawnUnderItsOwnDateBesideTheLine()
    {
        using var store = await FixtureReplay.ReplayedForResearchAsync();

        // A cycle accepted in June, and a pass on the night that could not refresh it.
        ThemeDocument(store);
        ThemeCycle(store, 1, "2026-06-01", "accepted", "Orders were flat across the industry [D1].");

        var detail = """{"ticker":"KEYS","asOf":"2026-09-08","outcome":"ok","notWritten":[{"section":"The industry cycle","reason":"the theme could not be refreshed: the search tool did not answer"}]}""";

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES " +
            $"('research-stale-theme', 'research', '2026-09-08T21:10:00Z', '2026-09-08T21:12:00Z', 'ok', 0, 0, 0, '0', '{detail}');");

        var page = await ResearchedPage(store, "KEYS", AWeekLater);

        // The stored cycle under June's date, and the one line saying why it is not newer.
        var drawn = WrittenOnThePage(page, ClaimRules.CycleSection);

        Assert.True(drawn.Success);
        Assert.Equal("2026-06-01", drawn.Groups[1].Value);
        Assert.Equal(
            $"{ClaimRules.CycleSection} is not written: the theme could not be refreshed: the search tool did not answer",
            WebUtility.HtmlDecode(Assert.Single(NotWrittenLines(page, ClaimRules.CycleSection)).Groups[1].Value));

        // A cycle another member's pass accepted after that night stands, and the line goes.
        ThemeCycle(store, 2, "2026-09-09", "accepted", "Orders across the industry turned up in September [D1].");

        var later = await ResearchedPage(store, "KEYS", AWeekLater);

        Assert.Equal("2026-09-09", WrittenOnThePage(later, ClaimRules.CycleSection).Groups[1].Value);
        Assert.Empty(NotWrittenLines(later, ClaimRules.CycleSection));
    }
}
