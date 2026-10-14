using System.Net;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, the 19.6 correction of 2026-10-14: the off-peak run five minutes after each peak window ends, the queue
// page stating a queue of the night's requests alone as starting at it, and the Write now press on each outstanding
// request, which inside a peak window goes only once the operator confirms the peak price.
// see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both five minutes after each peak window ends
// see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
public partial class ReadSurface
{
    // The row the correction adds that this check reaches.
    internal static readonly string[] WriteNowRows =
    [
        CheckReach.Key("15.15 Queue", "Write now"),
    ];

    static ResearchPricing Peaked => Providers.ResearchModelFeedTests.Pinned().Pricing;

    [Fact]
    public void TheOffPeakRunStartsFiveMinutesAfterEachPeakWindowEndsOnEveryDay()
    {
        // The fixture's profile: peak from 01:00 to 04:00 and 06:00 to 10:00 UTC on weekdays.
        Assert.Equal([TimeSpan.FromHours(4) + TimeSpan.FromMinutes(5), TimeSpan.FromHours(10) + TimeSpan.FromMinutes(5)], OffPeakRun.Times([Peaked]));

        // A Wednesday's night ending in the small hours runs at 04:05; at the run itself it runs then; a minute after it,
        // at 10:05; after the second, on the next day's first.
        Assert.Equal(UtcAt("2026-10-14T04:05:00Z"), OffPeakRun.NextAt([Peaked], UtcAt("2026-10-14T00:20:00Z")));
        Assert.Equal(UtcAt("2026-10-14T04:05:00Z"), OffPeakRun.NextAt([Peaked], UtcAt("2026-10-14T04:05:00Z")));
        Assert.Equal(UtcAt("2026-10-14T10:05:00Z"), OffPeakRun.NextAt([Peaked], UtcAt("2026-10-14T04:05:01Z")));
        Assert.Equal(UtcAt("2026-10-15T04:05:00Z"), OffPeakRun.NextAt([Peaked], UtcAt("2026-10-14T10:06:00Z")));

        // A Friday night's reports on the Saturday, a day naming no window, and not on the Monday.
        Assert.Equal(UtcAt("2026-10-17T04:05:00Z"), OffPeakRun.NextAt([Peaked], UtcAt("2026-10-16T23:40:00Z")));

        // A profile naming no window has no run to wait for.
        Assert.Null(OffPeakRun.NextAt([new ResearchPricing(0.003m, 0.15m, 0.60m, [], [], 2m)], UtcAt("2026-10-14T00:20:00Z")));
    }

    static RequestRow FromTheNight(string ticker, string askedAt, string state = ResearchRequests.Outstanding) =>
        new(ticker, UtcAt(askedAt), ResearchRequests.FromNight, "paid", state, null, null, null);

    [Fact]
    public void AQueueOfTheNightsRequestsAloneStartsAtTheNextOffPeakRunAndAPressStartsItNow()
    {
        // Two passes that ran to their end, six minutes and twenty-six.
        var estimate = QueueTimes.Estimate([TimeSpan.FromMinutes(6), TimeSpan.FromMinutes(26)]);
        var now = UtcAt("2026-10-14T00:18:00Z");

        // The night of 2026-10-13's two requests, read at 00:18 on the Wednesday: the first at the 04:05 run, ending a
        // median of sixteen minutes after it, and the second after it at 04:21, which twenty-six minutes takes to 04:47,
        // before the 06:00 window.
        var night = QueueTimes.For(
            [FromTheNight("JBL", "2026-10-13T23:32:54Z"), FromTheNight("NTCT", "2026-10-13T23:32:54Z")],
            new Dictionary<RequestRow, DateTimeOffset?>(),
            estimate,
            [Peaked],
            now);

        Assert.Equal((TimeBasis.OffPeakRun, UtcAt("2026-10-14T04:05:00Z"), UtcAt("2026-10-14T04:21:00Z")), (night[0].Basis, night[0].Starts!.Value, night[0].Ends!.Value));
        Assert.Equal((TimeBasis.Estimated, UtcAt("2026-10-14T04:21:00Z")), (night[1].Basis, night[1].Starts!.Value));
        Assert.Contains("at the off-peak run the night's requests wait for", QueueTimes.Words(night[0], FromTheNight("JBL", "2026-10-13T23:32:54Z"), NewYork), StringComparison.Ordinal);

        // A press's request among them started the drain, which takes the night's first: now, since twenty-six minutes
        // from 00:18 end before the 01:00 window.
        var pressed = QueueTimes.For(
            [FromTheNight("JBL", "2026-10-13T23:32:54Z"), Request("KEYS", "2026-10-14T00:10:00Z", ResearchRequests.Outstanding)],
            new Dictionary<RequestRow, DateTimeOffset?>(),
            estimate,
            [Peaked],
            now);

        Assert.Equal((TimeBasis.Now, now), (pressed[0].Basis, pressed[0].Starts!.Value));

        // And a night's request being written is a drain running, so the one behind it is not sent to the run.
        var writing = FromTheNight("JBL", "2026-10-13T23:32:54Z", ResearchRequests.Writing);
        var running = QueueTimes.For(
            [writing, FromTheNight("NTCT", "2026-10-13T23:32:54Z")],
            new Dictionary<RequestRow, DateTimeOffset?> { [writing] = UtcAt("2026-10-14T00:15:00Z") },
            estimate,
            [Peaked],
            now);

        Assert.Equal((TimeBasis.Estimated, UtcAt("2026-10-14T00:31:00Z")), (running[1].Basis, running[1].Starts!.Value));
    }

    [Fact]
    public async Task WriteNowWritesTheNamedRequestAloneAheadOfAnOlderOneAndAtPeakOnlyOnceConfirmed()
    {
        // 02:00 on a Tuesday, inside the 01:00 to 04:00 window.
        foreach (var confirmed in new[] { true, false })
        {
            using var store = RequestsFor("OLD", "NEW");

            var clock = new WaitedClock(UtcAt("2026-09-22T02:00:00Z"));
            var waits = new List<DateTimeOffset>();
            var passes = new List<(DateTimeOffset At, string[] Verb)>();

            var (taken, _) = await RequestDrain.WriteNowAsync(
                store.DatabaseFile,
                clock,
                verb =>
                {
                    passes.Add((clock.UtcNow, verb));

                    return Task.CompletedTask;
                },
                "NEW",
                confirmed,
                [Peaked],
                until =>
                {
                    waits.Add(until);
                    clock.UtcNow = until;

                    return Task.CompletedTask;
                });

            Assert.Equal(1, taken);

            // Confirmed, at once at the peak price and the industry's pass told so; not, at the window's end.
            var pass = Assert.Single(passes);

            Assert.Equal(confirmed ? UtcAt("2026-09-22T02:00:00Z") : UtcAt("2026-09-22T04:00:00Z"), pass.At);
            Assert.Equal(confirmed ? [] : [UtcAt("2026-09-22T04:00:00Z")], waits);
            Assert.Equal(["research", "--ticker", "NEW", "--paid-for-local", .. confirmed ? [RequestDrain.AtPeakFlag] : Array.Empty<string>()], pass.Verb);

            // The older request is left in the queue, outstanding, and the named one is settled.
            Assert.Equal(
                [["NEW", ResearchRequests.Refused], ["OLD", ResearchRequests.Outstanding]],
                Rows(store, "SELECT ticker, state FROM research_request ORDER BY ticker;"));
        }

        // A name holding no request nobody has started writes nothing.
        using (var none = RequestsFor("OLD"))
        {
            var (taken, written) = await RequestDrain.WriteNowAsync(
                none.DatabaseFile,
                new WaitedClock(UtcAt("2026-09-22T12:00:00Z")),
                _ => throw new InvalidOperationException("no pass is started for a name with no request"),
                "NEW",
                atPeak: false,
                [Peaked],
                _ => Task.CompletedTask);

            Assert.Equal((0, 0), (taken, written));
        }
    }

    [Fact]
    public async Task TheWriteNowPressIsRefusedWithoutItsHeaderAndAtPeakWithoutTheConfirmation()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        var launcher = new RecordingLauncher();

        // Noon UTC on a Wednesday, off-peak.
        using (var host = new PassHost(store.Root) { Launcher = launcher, Clock = FixedClock.At(UtcAt("2026-10-14T12:00:00Z"), SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();

            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "list")))).StatusCode);

            // Drawn on the outstanding request, saying no peak, and on nothing else.
            var page = await client.GetStringAsync("/screens/queue");

            Assert.Contains($"action=\"{SinglePageApp.WriteNowRoute}KEYS\" data-writes=\"KEYS\" data-peak=\"false\">", page, StringComparison.Ordinal);
            Assert.Single(System.Text.RegularExpressions.Regex.Matches(page, "class=\"now-control\""));

            Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(Press(SinglePageApp.WriteNowRoute, "KEYS", null))).StatusCode);

            var none = await client.SendAsync(Press(SinglePageApp.WriteNowRoute, "AAPL", SinglePageApp.PassHeaderValue));

            Assert.Equal(HttpStatusCode.Conflict, none.StatusCode);
            Assert.Contains("data-now=\"state\"", await none.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.Empty(launcher.Now);

            // Off-peak it starts at once, at the off-peak price.
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Press(SinglePageApp.WriteNowRoute, "KEYS", SinglePageApp.PassHeaderValue))).StatusCode);
            Assert.Equal([("KEYS", false)], launcher.Now);
        }

        // 02:00 UTC on the Thursday, inside the window that ends at 04:00 UTC, midnight in New York.
        using (var host = new PassHost(store.Root) { Launcher = launcher, Clock = FixedClock.At(UtcAt("2026-10-15T02:00:00Z"), SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();

            var page = await client.GetStringAsync("/screens/queue");

            Assert.Contains("data-writes=\"KEYS\" data-peak=\"true\" data-multiple=\"twice\">", page, StringComparison.Ordinal);
            Assert.Contains("title=\"2026-10-15 04:00 UTC\">2026-10-15 00:00 ET</time></span>", page, StringComparison.Ordinal);

            // Refused without the confirmation, saying until when and at what multiple, and starting nothing.
            var unconfirmed = await client.SendAsync(Press(SinglePageApp.WriteNowRoute, "KEYS", SinglePageApp.PassHeaderValue));
            var said = WebUtility.HtmlDecode(await unconfirmed.Content.ReadAsStringAsync());

            Assert.Equal(HttpStatusCode.Conflict, unconfirmed.StatusCode);
            Assert.Contains("data-now=\"peak\"", said, StringComparison.Ordinal);
            Assert.Contains("twice its off-peak price, until 2026-10-15 00:00 New York", said, StringComparison.Ordinal);
            Assert.Single(launcher.Now);

            // Confirmed, it starts at the peak price.
            Assert.Equal(HttpStatusCode.Accepted, (await client.SendAsync(Press(SinglePageApp.WriteNowRoute, "KEYS", SinglePageApp.PassHeaderValue, ("confirm", SinglePageApp.WriteNowAtPeak)))).StatusCode);
            Assert.Equal([("KEYS", false), ("KEYS", true)], launcher.Now);
        }
    }
}
