using System.Net;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Research;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, the 18.4 correction of the report page: beneath each of a name's own sections left out or not written, a
// control that has the local model write it again alone; its press writes a request naming the section on the local lane,
// and the drain takes it at once, inside a peak window too, handing its pass the section and nothing that would move it to
// the paid model.
// see: A section the checker left out is written again alone on the local model at a press, and the day's one regenerate does not hold it
public partial class ReadSurface
{
    [Fact]
    public void EachOwnSectionLeftOutCarriesTheControlThatWritesItAgainAloneAndTheCycleDoesNot()
    {
        var marks = new MarkRenderer();
        var cost = new ResearchCost(1, 0.05m, 0.05m, "within the caps");
        var drawn = marks.LeftOut(
            "ROP",
            [new LeftOutSection("ROP", "What management said", "the claim check refused it on its third retry")],
            notWritten: [new LeftOutSection("ROP", ClaimRules.CycleSection, "the theme could not be refreshed: no source")],
            cost: cost);

        Assert.Contains(
            "<form class=\"research-control section-again\" method=\"post\" action=\"/passes/ROP\" data-kind=\"section\" data-section=\"What management said\"><input type=\"hidden\" name=\"section\" value=\"What management said\"><button type=\"submit\">" + MarkRenderer.AgainAloneLabel + "</button></form>",
            drawn,
            StringComparison.Ordinal);
        Assert.DoesNotContain("data-section=\"" + ClaimRules.CycleSection + "\"><input", drawn, StringComparison.Ordinal);

        // A page stating no cost, an export's, draws none.
        Assert.DoesNotContain("section-again", marks.LeftOut("ROP", [new LeftOutSection("ROP", "What management said", "refused")]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task APressForOneSectionWritesALocalRequestNamingItAndTheDrainTakesItAtOnceWithTheSectionAlone()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var launcher = new RecordingLauncher();

        using var host = new PassHost(store.Root) { Launcher = launcher };
        using var client = host.CreateClient();

        // The control's own field sent as the page sends it: the section, on a local request asking for no rewrite.
        var pressed = await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "name"), ("section", "What management said")));

        Assert.Equal(HttpStatusCode.Accepted, pressed.StatusCode);
        Assert.Contains("KEYS is in the queue, to have What management said written again alone on the local model.", WebUtility.HtmlDecode(await pressed.Content.ReadAsStringAsync()), StringComparison.Ordinal);
        Assert.Equal([["KEYS", "local", "0", "What management said"]], Rows(store, "SELECT ticker, lane, refresh, section FROM research_request;"));
        Assert.Equal(1, launcher.Started);

        // A section the name does not write alone is refused and writes nothing.
        var cycle = await client.SendAsync(Press(SinglePageApp.PassRoute, "AAPL", SinglePageApp.PassHeaderValue, ("from", "name"), ("section", ClaimRules.CycleSection)));

        Assert.Equal(HttpStatusCode.BadRequest, cycle.StatusCode);
        Assert.Single(Rows(store, "SELECT ticker FROM research_request;"));

        // The store refuses a section on a paid request or one asking for a rewrite.
        Assert.ThrowsAny<Exception>(() => store.Execute(
            "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state, refresh, section) VALUES ('MSFT', '2026-10-27T13:00:00Z', 'name', 'paid', 'outstanding', 0, 'What management said');"));

        // Beside it a paid request asked earlier: inside a peak window on a weekday the drain takes the section at once and
        // waits for the off-peak hours before the paid one, handing the section's pass the section alone.
        store.Execute("INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES ('AAPL', '2000-01-01T00:00:00Z', 'name', 'paid', 'outstanding');");

        var clock = new WaitedClock(UtcAt("2026-10-27T07:00:00Z"));
        var passes = new List<string>();
        var waited = new List<DateTimeOffset>();

        await RequestDrain.DrainAsync(
            store.DatabaseFile,
            clock,
            verb =>
            {
                passes.Add(string.Join(' ', verb));

                return Task.CompletedTask;
            },
            [Providers.ResearchModelFeedTests.Pinned().Pricing],
            until =>
            {
                waited.Add(until);
                clock.UtcNow = until;

                return Task.CompletedTask;
            });

        Assert.Equal(["research --ticker KEYS --section What management said", "research --ticker AAPL --paid-for-local"], passes);
        Assert.Single(waited);
    }

    [Fact]
    public void TheResearchRegionSaysWhatTheNewestSectionAskedForAloneCameTo()
    {
        // Written again; not written with the local model's reason; and the local model not reached, each in one line
        // beside the report's own pass, read off the pass's row as the runner writes it.
        const string Written = "{\"ticker\":\"ROP\",\"asOf\":\"2026-10-28\",\"outcome\":\"ok\",\"reason\":null,\"written\":[{\"section\":\"What management said\",\"version\":3,\"model\":\"google/gemma-4-26b-a4b-qat\",\"retry\":false}],\"notWritten\":[],\"alone\":\"What management said\"}";
        const string Held = "{\"ticker\":\"ROP\",\"asOf\":\"2026-10-28\",\"outcome\":\"ok\",\"reason\":null,\"written\":[],\"notWritten\":[{\"section\":\"The risks\",\"reason\":\"the machine cannot hold it\"}],\"alone\":\"The risks\"}";
        const string Unreached = "{\"ticker\":\"ROP\",\"asOf\":\"2026-10-28\",\"outcome\":\"unavailable\",\"reason\":\"the local model is unavailable: refused\",\"written\":[],\"notWritten\":[{\"section\":\"The risks\",\"reason\":\"the local model is unavailable: refused\"}],\"alone\":\"The risks\"}";

        Assert.Equal("What management said was written again alone on the local model on 2026-10-28.", NameScreen.Alone(Written)!.Line);
        Assert.Equal("The risks was asked for again alone on the local model on 2026-10-28, and it was not written: the machine cannot hold it.", NameScreen.Alone(Held)!.Line);
        Assert.Equal("The risks was asked for again alone on the local model on 2026-10-28, and the local model was not reached: the local model is unavailable: refused.", NameScreen.Alone(Unreached)!.Line);
        Assert.Null(NameScreen.Alone("{\"ticker\":\"ROP\",\"asOf\":\"2026-10-28\",\"outcome\":\"ok\"}"));
        Assert.Null(NameScreen.Alone(null));

        var drawn = new MarkRenderer().LeftOut("ROP", [], alone: NameScreen.Alone(Written));

        Assert.Contains("<p class=\"research-alone\" data-section=\"What management said\" data-outcome=\"ok\" data-as-of=\"2026-10-28\">", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void TheQueuePageStatesASectionAskedForAloneAsStartingNowAheadOfAReportWaitingForThePeakToEnd()
    {
        // A Tuesday at 07:00 UTC, inside the fixture's peak window from 06:00 to 10:00: the report asked first waits for the
        // window's end, and the section asked after it starts now and is taken first, its words naming it.
        var now = UtcAt("2026-10-27T07:00:00Z");
        RequestRow report = new("AAPL", UtcAt("2026-10-27T06:30:00Z"), ResearchRequests.FromName, ResearchRequests.Paid, ResearchRequests.Outstanding, null, null, null);
        RequestRow alone = new("KEYS", UtcAt("2026-10-27T06:45:00Z"), ResearchRequests.FromName, ResearchRequests.Local, ResearchRequests.Outstanding, null, null, null, "What management said");

        var times = QueueTimes.For([report, alone], new Dictionary<RequestRow, DateTimeOffset?>(), QueueTimes.Estimate([TimeSpan.FromMinutes(10)]), [Peaked], now);

        Assert.Equal((TimeBasis.Now, now, 0), (times[1].Basis, times[1].Starts!.Value, times[1].Ahead));
        Assert.Equal((TimeBasis.Estimated, 1), (times[0].Basis, times[0].Ahead));
        Assert.True(times[0].Starts >= UtcAt("2026-10-27T10:00:00Z"), "the report starts before the peak window ends");
        Assert.StartsWith("What management said alone on the local model: starts now", QueueTimes.Words(times[1], alone, TimeZoneInfo.Utc), StringComparison.Ordinal);
    }
}
