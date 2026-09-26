using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, the 5.8 corrections: a report is regenerated whole on the operator's ask once the day
// it was written has passed, the press carrying the ask on its request and the drain handing it on.
// see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
public partial class ReadSurface
{
    const string RegenerateControl =
        "<form class=\"research-control\" method=\"post\" action=\"/passes/KEYS\" data-kind=\"refresh\" data-refresh=\"true\" data-paid-for-local=\"false\">";

    [Fact]
    public async Task AReportIsOfferedRegenerateReportOnceTheDayItWasWrittenHasPassedAndNotOnThatDay()
    {
        using var store = await FixtureReplay.ResearchedAsync();

        // A week after the pass that wrote it, with the research standing and no trigger fired: the
        // one control is Regenerate Report, with the cost of research so far stated before it.
        var standing = await ResearchedPage(store, "KEYS", AWeekLater);
        var offer = Regex.Match(standing, Regex.Escape(RegenerateControl) + ".*?<button type=\"submit\">([^<]*)</button>", RegexOptions.Singleline);

        Assert.Contains("<p class=\"research-state\" data-state=\"stands\">", standing, StringComparison.Ordinal);
        Assert.True(offer.Success, "A standing report is not offered Regenerate Report.");
        Assert.Equal("Regenerate Report", offer.Groups[1].Value);
        Assert.True(standing.IndexOf("class=\"research-cost\"", StringComparison.Ordinal) < offer.Index, "the cost is not stated before the control");
        Assert.Single(Regex.Matches(standing, "<form class=\"research-control\""));

        // On the day the pass ran it is not offered, so the report written that day is not paid for
        // twice by a second press.
        var sameDay = await ResearchedPage(store, "KEYS", DateTimeOffset.Parse("2026-09-08T23:00:00Z", CultureInfo.InvariantCulture));

        Assert.DoesNotContain("data-kind=\"refresh\"", sameDay, StringComparison.Ordinal);

        // Gone stale, it stands beside the rewrite of the stale sections.
        store.Execute("UPDATE research_section SET as_of = '2026-08-01' WHERE ticker = 'KEYS';");

        var stale = await ResearchedPage(store, "KEYS", AWeekLater);

        Assert.Contains("data-state=\"stale\"", stale, StringComparison.Ordinal);
        Assert.Contains("data-kind=\"rewrite\"", stale, StringComparison.Ordinal);
        Assert.Contains(RegenerateControl, stale, StringComparison.Ordinal);

        // While the cap has paused research it is not offered, since a press would be refused.
        Spend(store, "research-today", "research call: The two cases", "2026-09-15T12:00:00Z", "10.00");

        Assert.DoesNotContain("data-kind=\"refresh\"", await ResearchedPage(store, "KEYS", AWeekLater), StringComparison.Ordinal);

        // And where nothing is written the page offers to write it, and nothing to regenerate.
        using var unwritten = await FixtureReplay.ReplayedAsync();

        var missing = await ResearchedPage(unwritten, "KEYS", AWeekLater);

        Assert.Contains("data-state=\"missing\"", missing, StringComparison.Ordinal);
        Assert.DoesNotContain("data-kind=\"refresh\"", missing, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APressAskingToRegenerateAReportWritesARequestCarryingTheAskAndTheDrainHandsItsPassTheRewrite()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var launcher = new RecordingLauncher();

        using var host = new PassHost(store.Root) { Launcher = launcher };
        using var client = host.CreateClient();

        Assert.Contains("KEYS", await client.GetStringAsync("/screens/name/KEYS"), StringComparison.Ordinal);

        // The control's own fields sent as the page sends them: the rewrite asked for, on the row.
        var pressed = await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "name"), ("refresh", "true"), ("paidForLocal", "false")));

        Assert.Equal(HttpStatusCode.Accepted, pressed.StatusCode);
        Assert.Contains("KEYS is in the queue, to have every section written again.", WebUtility.HtmlDecode(await pressed.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal([["KEYS", "name", "1"]], Rows(store, "SELECT ticker, asked_from, refresh FROM research_request;"));

        // A plain press asks for no rewrite.
        await client.SendAsync(Press(SinglePageApp.PassRoute, "AAPL", SinglePageApp.PassHeaderValue, ("from", "name"), ("refresh", "false")));

        Assert.Equal("0", Rows(store, "SELECT refresh FROM research_request WHERE ticker = 'AAPL';").Single()[0]);

        // Two requests naming the local lane, one asking for a regenerate and one not: the regenerate is
        // the paid model's whole, whatever lane its request names, and the plain one keeps its lane.
        store.Execute(
            "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state, refresh) VALUES " +
            "('MSFT', '2026-09-27T12:00:00Z', 'name', 'local', 'outstanding', 1), " +
            "('NFLX', '2026-09-27T12:00:00Z', 'name', 'local', 'outstanding', 0);");

        // The drain hands each request's pass what its press asked for, read off the verb it runs: the
        // rewrite to the ones that asked and to no other, whichever it takes first, since two presses in
        // one second are taken by ticker. A Sunday, so nothing waits for off-peak.
        var clock = FixedClock.At(DateTimeOffset.Parse("2026-09-27T12:00:05Z", CultureInfo.InvariantCulture), SessionZones.UnitedStates);
        var passes = new List<string>();

        await RequestDrain.DrainAsync(
            store.DatabaseFile,
            clock,
            verb =>
            {
                passes.Add(string.Join(' ', verb));

                return Task.CompletedTask;
            },
            Providers.ResearchModelFeedTests.Shipped().Pricing,
            _ => throw new InvalidOperationException("nothing here is at peak, so nothing waits"));

        Assert.Equal(
            ["research --ticker AAPL --paid-for-local", "research --ticker KEYS --paid-for-local --refresh", "research --ticker MSFT --paid-for-local --refresh", "research --ticker NFLX"],
            passes.Order(StringComparer.Ordinal));
        Assert.Equal(2, launcher.Started);
    }
}
