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
        Regex.Match(page, "<div class=\"report-standing\"[^>]*><p class=\"rs-line\">.*?</p>(?:<p class=\"research-cost\"[^>]*>[^<]*</p><div class=\"rs-controls\">.*?</div>)?</div>", RegexOptions.Singleline).Value;

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
