using System.Text.RegularExpressions;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface: a written report's standing and the controls that write it again, drawn under the name page's masthead
// and on the chart page's report line as the research region at the name page's foot offers them, each after what
// research has cost.
// see: Every link that opens a stock opens its chart page first, and the chart page links to its report with the controls that write it again
public partial class ReadSurface
{
    // A page's report standing, from its opening tag to its close.
    static string StandingOn(string page) =>
        Regex.Match(page, "<div class=\"report-standing\"[^>]*><p class=\"rs-line\">.*?</p>(?:<p class=\"rs-today\">[^<]*</p>)?(?:<p class=\"research-cost\"[^>]*>[^<]*</p><div class=\"rs-controls\">.*?</div>)?(?:<p class=\"rs-withheld\">[^<]*</p>)?</div>", RegexOptions.Singleline).Value;

    // The kinds of the controls a piece of a page draws, in order.
    static IReadOnlyList<string> KindsIn(string markup) =>
        [.. Regex.Matches(markup, "<form class=\"research-control\"[^>]*data-kind=\"([a-z-]+)\"").Select(form => form.Groups[1].Value)];

    [Fact]
    public async Task AWrittenReportsStandingAndTheControlsThatWriteItAgainStandUnderTheNamePagesMasthead()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        // Standing a week after its pass: the slate pill beside the day its newest section was written, the cost, and the
        // one control a standing report is offered, under the masthead and above the lead.
        var page = await ResearchedPage(store, "KEYS", AWeekLater);
        var standing = StandingOn(page);
        var at = page.IndexOf("<div class=\"report-standing\"", StringComparison.Ordinal);

        Assert.StartsWith(
            "<div class=\"report-standing\" data-ticker=\"KEYS\" data-state=\"stands\" data-written=\"2026-09-08\" data-controls=\"1\"><p class=\"rs-line\"><span class=\"rs-written\">The report was written 2026-09-08</span> <span class=\"status-pill live\">stands</span></p><p class=\"research-cost\"",
            standing,
            StringComparison.Ordinal);
        Assert.Equal([NameScreenKinds.Refresh], KindsIn(standing));
        Assert.Contains(RegenerateControl, standing, StringComparison.Ordinal);
        Assert.True(at > page.IndexOf("class=\"m-asof\"", StringComparison.Ordinal) && at < page.IndexOf("class=\"lead", StringComparison.Ordinal), "the standing is not drawn between the masthead and the lead");

        // Gone stale: the red pill with what made it so, in the words the research region's line names it in, and both
        // controls, the rewrite of the stale sections first.
        store.Execute("UPDATE research_section SET as_of = '2026-08-01' WHERE ticker = 'KEYS';");

        var stalePage = await ResearchedPage(store, "KEYS", AWeekLater);
        var stale = StandingOn(stalePage);
        var why = Regex.Match(stalePage, "<p class=\"research-state\" data-state=\"stale\">the research is stale: ([^<]*)</p>").Groups[1].Value;

        Assert.Contains("data-state=\"stale\" data-written=\"2026-08-01\" data-controls=\"2\"", stale, StringComparison.Ordinal);
        Assert.NotEmpty(why);
        Assert.Contains($"<span class=\"status-pill failed\">stale</span> <span class=\"rs-why\">{why}</span></p>", stale, StringComparison.Ordinal);
        Assert.Equal([NameScreenKinds.Rewrite, NameScreenKinds.Refresh], KindsIn(stale));
        Assert.True(stale.IndexOf("class=\"research-cost\"", StringComparison.Ordinal) < stale.IndexOf("<form", StringComparison.Ordinal), "the cost is not stated before the controls");

        // On the day its pass ran, the standing with no control and no cost.
        var sameDay = StandingOn(await ResearchedPage(store, "KEYS", DateTimeOffset.Parse("2026-09-08T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture)));

        Assert.Contains("data-state=\"stale\" data-written=\"2026-08-01\" data-controls=\"0\">", sameDay, StringComparison.Ordinal);
        Assert.DoesNotContain("research-cost", sameDay, StringComparison.Ordinal);
        Assert.Empty(KindsIn(sameDay));

        // While the cap has paused research, no control either.
        Spend(store, "research-today", "research call: The two cases", "2026-09-15T12:00:00Z", "10.00");

        Assert.Empty(KindsIn(StandingOn(await ResearchedPage(store, "KEYS", AWeekLater))));

        // And where nothing is written, no standing at all.
        using var unwritten = await FixtureReplay.ReplayedAsync();

        Assert.DoesNotContain("report-standing", await ResearchedPage(unwritten, "KEYS", AWeekLater), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheChartPagesReportLineDrawsAWrittenReportsStandingWithTheControlsThatWriteItAgain()
    {
        using var store = await FixtureReplay.ResearchedAsync();
        using var host = new PassHost(store.Root) { Clock = FixedClock.At(AWeekLater, SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        // Right after the line opening the report, the standing with the control a standing report is offered after the
        // cost, the day named by the line it follows.
        var chart = await client.GetStringAsync("/screens/chart/KEYS");
        var line = Regex.Match(chart, "<p class=\"report-line\" data-report=\"written\" data-written=\"2026-09-08\">.*?</p>", RegexOptions.Singleline).Value;
        var standing = StandingOn(chart);

        Assert.Contains("<a class=\"btn open-report\" href=\"#/name/KEYS\">Open the report</a>", line, StringComparison.Ordinal);
        Assert.Contains(line + standing, chart, StringComparison.Ordinal);
        Assert.StartsWith(
            "<div class=\"report-standing\" data-ticker=\"KEYS\" data-state=\"stands\" data-written=\"2026-09-08\" data-controls=\"1\"><p class=\"rs-line\"><span class=\"status-pill live\">stands</span></p><p class=\"research-cost\"",
            standing,
            StringComparison.Ordinal);
        Assert.Contains(RegenerateControl, standing, StringComparison.Ordinal);

        // Gone stale, both controls and the triggers that fired.
        store.Execute("UPDATE research_section SET as_of = '2026-08-01' WHERE ticker = 'KEYS';");

        var stale = StandingOn(await client.GetStringAsync("/screens/chart/KEYS"));

        Assert.Contains("<span class=\"status-pill failed\">stale</span> <span class=\"rs-why\">", stale, StringComparison.Ordinal);
        Assert.Equal([NameScreenKinds.Rewrite, NameScreenKinds.Refresh], KindsIn(stale));

        // A stock no report is written for keeps the list's own request and draws no standing.
        var none = await client.GetStringAsync("/screens/chart/AAPL");

        Assert.Contains(MarkRenderer.AskForAReport("AAPL"), none, StringComparison.Ordinal);
        Assert.DoesNotContain("report-standing", none, StringComparison.Ordinal);
    }

    // A page about an earlier night carries no staleness verdict, since staleness is the application asking about the
    // company now, so it draws no standing and no control, where tonight's page over the same store draws both.
    // see: A name's page for an earlier night draws what the store held that night and nothing it learned after
    [Fact]
    public async Task ANamePageForAnEarlierNightDrawsNoReportStandingWhereTonightsDrawsOne()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        var earlier = SessionWithoutAnEvening(store, "KEYS", NightIn(store));

        store.Execute($"INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons) VALUES ('KEYS', '{earlier}', '[]', 0, '[]', '[]');");

        using var host = new PassHost(store.Root) { Clock = FixedClock.At(AWeekLater, SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        var tonight = await client.GetStringAsync("/screens/name/KEYS");
        var then = await client.GetStringAsync($"/screens/name/KEYS/{earlier}");

        Assert.Contains("<div class=\"report-standing\" data-ticker=\"KEYS\" data-state=\"stands\"", tonight, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"notice earlier-night\" data-night=\"{earlier}\"", then, StringComparison.Ordinal);
        Assert.DoesNotContain("report-standing", then, StringComparison.Ordinal);
        Assert.Empty(KindsIn(then));
    }

    // From the 19.11 correction of 2026-10-23: a pass that ran today and wrote none of the report's own sections, as a
    // night's pass refreshing only the industry does, leaves Regenerate offered and says it checked; one that wrote them
    // withholds Regenerate and says why; and the report's own day is named apart from a later industry section's, on the
    // name page and on the chart page alike.
    // see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, and not on a day a pass wrote the name's own sections
    [Fact]
    public async Task APassTodayWritingNoneOfTheReportsOwnSectionsLeavesRegenerateOfferedAndTheLineNamesEachDayAndWhy()
    {
        using var store = await FixtureReplay.ResearchedAsync();
        var passDay = DateTimeOffset.Parse("2026-09-08T23:00:00Z", System.Globalization.CultureInfo.InvariantCulture);
        async Task<string> NameOn(DateTimeOffset at) => StandingOn(await ResearchedPage(store, "KEYS", at));

        // On the day its pass wrote the report's own sections: no Regenerate, and the line saying why.
        var wrote = await NameOn(passDay);

        Assert.Empty(KindsIn(wrote));
        Assert.Contains($"<p class=\"rs-withheld\">{EquityBrief.Api.Reading.NameScreen.RegenerateTomorrow}</p></div>", wrote, StringComparison.Ordinal);
        Assert.DoesNotContain("rs-today", wrote, StringComparison.Ordinal);

        // The same pass as one that wrote none of the report's own sections: Regenerate offered after the cost, the line
        // saying a pass checked it today, and nothing withheld.
        store.Execute("UPDATE run_log SET detail = json_set(detail, '$.written', json('[]')) WHERE stage = 'research' AND json_valid(detail) AND json_extract(detail, '$.ticker') = 'KEYS';");

        var checkedOnly = await NameOn(passDay);

        Assert.Equal([NameScreenKinds.Refresh], KindsIn(checkedOnly));
        Assert.Contains($"</p><p class=\"rs-today\">{EquityBrief.Api.Reading.NameScreen.CheckedToday(new DateOnly(2026, 9, 8))}</p><p class=\"research-cost\"", checkedOnly, StringComparison.Ordinal);
        Assert.Equal("A pass checked it today, 2026-09-08, and wrote none of its own sections.", EquityBrief.Api.Reading.NameScreen.CheckedToday(new DateOnly(2026, 9, 8)));
        Assert.DoesNotContain("rs-withheld", checkedOnly, StringComparison.Ordinal);

        // A week later its industry's cycle written by a theme pass on the fourteenth: the report's own day stays the
        // eighth, and the industry's is named apart, on the name page and on the chart page.
        store.Execute(
            "INSERT INTO theme_section (theme, section, version, as_of, model, status, prose, source_ids, reject_reason, industries) "
            + $"SELECT m.industry, '{EquityBrief.Core.Research.ClaimRules.CycleSection}', 99, '2026-09-14', 'deepseek-flash', 'accepted', 'The cycle turned.', '[]', NULL, '[]' "
            + "FROM membership m WHERE m.ticker = 'KEYS' AND m.industry IS NOT NULL ORDER BY m.observed_at DESC LIMIT 1;");

        var name = await NameOn(AWeekLater);

        using var host = new PassHost(store.Root) { Clock = FixedClock.At(AWeekLater, SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        var chart = StandingOn(await client.GetStringAsync("/screens/chart/KEYS"));

        Assert.Contains("data-written=\"2026-09-08\"", name, StringComparison.Ordinal);
        Assert.Contains("<span class=\"rs-written\">The report was written 2026-09-08</span>", name, StringComparison.Ordinal);
        Assert.Contains(" <span class=\"rs-industry\">Its industry section was written 2026-09-14.</span></p>", name, StringComparison.Ordinal);
        Assert.Contains("data-written=\"2026-09-08\"", chart, StringComparison.Ordinal);
        Assert.Contains(" <span class=\"rs-industry\">Its industry section was written 2026-09-14.</span></p>", chart, StringComparison.Ordinal);
        Assert.DoesNotContain("rs-today", name, StringComparison.Ordinal);

        // An industry section no later than the report's own is not named.
        store.Execute($"UPDATE theme_section SET as_of = '2026-09-08' WHERE version = 99 AND section = '{EquityBrief.Core.Research.ClaimRules.CycleSection}';");

        Assert.DoesNotContain("rs-industry", await NameOn(AWeekLater), StringComparison.Ordinal);
    }

    // A press from a report's standing is confirmed with the cost its own block states, and the pass it starts is watched
    // for the stock its form names, which on the chart page is not the one the name page's route would read.
    [Fact]
    public void APressFromAReportsStandingIsConfirmedWithItsOwnCostAndWatchedForTheStockItsFormNames()
    {
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains("const cost = (form.closest('.report-standing, section.research') || screen).querySelector('.research-cost');", shell, StringComparison.Ordinal);
        Assert.Contains($"watch(said.getAttribute('data-watch-from'), decodeURIComponent(form.getAttribute('action').slice('{SinglePageApp.PassRoute}'.length)));", shell, StringComparison.Ordinal);
        Assert.Contains("async function watch(from, ticker) {", shell, StringComparison.Ordinal);
    }

    // The two controls' kinds as the read surface names them.
    static class NameScreenKinds
    {
        public const string Rewrite = EquityBrief.Api.Reading.NameScreen.RewriteKind;
        public const string Refresh = EquityBrief.Api.Reading.NameScreen.RefreshKind;
    }
}
