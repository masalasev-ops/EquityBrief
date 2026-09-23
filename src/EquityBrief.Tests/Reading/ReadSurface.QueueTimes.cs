using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface: when each request on the queue will be written, stated in New York's time with
// UTC beside it.
public partial class ReadSurface
{
    static RequestRow Request(string ticker, string askedAt, string state, string? settledAt = null) =>
        new(ticker, UtcAt(askedAt), "list", "paid", state, settledAt is null ? null : UtcAt(settledAt), null, null);

    static readonly TimeZoneInfo NewYork = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);

    // Two passes that ran to their end, four minutes and twenty-six, so the median is their
    // mean, fifteen minutes.
    static readonly TimeSpan[] TwoPasses = [TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(26)];

    static IReadOnlyList<RequestTime> TimesAt(string now, IReadOnlyList<RequestRow> rows, IReadOnlyList<TimeSpan> passes, params (RequestRow Row, string? Started)[] writing) =>
        QueueTimes.For(
            rows,
            writing.ToDictionary(pair => pair.Row, pair => pair.Started is null ? (DateTimeOffset?)null : UtcAt(pair.Started)),
            QueueTimes.Estimate(passes),
            Providers.ResearchModelFeedTests.Shipped().Pricing,
            UtcAt(now));

    [Fact]
    public void TheMedianIsTakenOverThePassesThatRanToTheirEndAndSaysHowManyAndNoneSaysItCannotEstimate()
    {
        Assert.Equal(new PassEstimate(TimeSpan.FromMinutes(15), 2), QueueTimes.Estimate(TwoPasses));
        Assert.Equal(new PassEstimate(TimeSpan.FromMinutes(26), 3), QueueTimes.Estimate([TimeSpan.FromMinutes(29), TimeSpan.FromMinutes(4), TimeSpan.FromMinutes(26)]));
        Assert.Equal(new PassEstimate(null, 0), QueueTimes.Estimate([]));
    }

    [Fact]
    public void ARequestWithNothingAheadOfItStartsNowOffPeakAndAtTheWindowsEndInsideOne()
    {
        var alone = Request("KEYS", "2026-09-21T11:59:00Z", ResearchRequests.Outstanding);

        // Noon on a Monday is off-peak: it starts now and ends a median later.
        var noon = Assert.Single(TimesAt("2026-09-21T12:00:00Z", [alone], TwoPasses));

        Assert.Equal(new RequestTime(TimeBasis.Now, UtcAt("2026-09-21T12:00:00Z"), UtcAt("2026-09-21T12:15:00Z"), 0), noon);

        // Half past two on a Monday is inside the window from 01:00 to 04:00, so it starts when
        // the window ends.
        var peak = Assert.Single(TimesAt("2026-09-21T02:30:00Z", [alone], TwoPasses));

        Assert.Equal(new RequestTime(TimeBasis.PeakEnds, UtcAt("2026-09-21T04:00:00Z"), UtcAt("2026-09-21T04:15:00Z"), 0), peak);
    }

    [Fact]
    public void ARequestBehindOthersStartsWhenTheyEndMovedPastAPeakWindowItFallsIn()
    {
        // At 00:40 on a Monday, a pass started at 00:35 is being written, with two requests
        // outstanding behind it. Worked by hand: the pass ends at 00:50, the first request runs
        // from 00:50 to 01:05, and the second would start at 01:05, inside the window, so it
        // starts at 04:00 and ends at 04:15.
        var writing = Request("DGX", "2026-09-21T00:30:00Z", ResearchRequests.Writing);
        var first = Request("KEYS", "2026-09-21T00:31:00Z", ResearchRequests.Outstanding);
        var second = Request("AAPL", "2026-09-21T00:32:00Z", ResearchRequests.Outstanding);

        var times = TimesAt("2026-09-21T00:40:00Z", [writing, first, second], TwoPasses, (writing, "2026-09-21T00:35:00Z"));

        Assert.Equal(
            [
                new RequestTime(TimeBasis.Started, UtcAt("2026-09-21T00:35:00Z"), UtcAt("2026-09-21T00:50:00Z"), 0),
                new RequestTime(TimeBasis.Estimated, UtcAt("2026-09-21T00:50:00Z"), UtcAt("2026-09-21T01:05:00Z"), 1),
                new RequestTime(TimeBasis.Estimated, UtcAt("2026-09-21T04:00:00Z"), UtcAt("2026-09-21T04:15:00Z"), 2),
            ],
            times);

        // A pass being written longer than the median is expected to end now rather than in the
        // past, and the request behind it starts then.
        var overdue = TimesAt("2026-09-21T12:00:00Z", [writing, first], TwoPasses, (writing, "2026-09-21T11:00:00Z"));

        Assert.Equal(UtcAt("2026-09-21T12:00:00Z"), overdue[0].Ends);
        Assert.Equal(new RequestTime(TimeBasis.Estimated, UtcAt("2026-09-21T12:00:00Z"), UtcAt("2026-09-21T12:15:00Z"), 1), overdue[1]);
    }

    [Fact]
    public void AStoreHoldingNoFinishedPassGivesNoTimeBehindAnotherRequest()
    {
        var writing = Request("DGX", "2026-09-21T11:40:00Z", ResearchRequests.Writing);
        var behind = Request("KEYS", "2026-09-21T11:41:00Z", ResearchRequests.Outstanding);

        var times = TimesAt("2026-09-21T12:00:00Z", [writing, behind], [], (writing, "2026-09-21T11:50:00Z"));

        Assert.Equal(new RequestTime(TimeBasis.Started, UtcAt("2026-09-21T11:50:00Z"), null, 0), times[0]);
        Assert.Equal(new RequestTime(TimeBasis.NotEstimated, null, null, 1), times[1]);

        // With nothing ahead, the first request still starts now, and the one behind it has no time.
        var queued = TimesAt("2026-09-21T12:00:00Z", [behind, Request("AAPL", "2026-09-21T11:42:00Z", ResearchRequests.Outstanding)], []);

        Assert.Equal(new RequestTime(TimeBasis.Now, UtcAt("2026-09-21T12:00:00Z"), null, 0), queued[0]);
        Assert.Equal(new RequestTime(TimeBasis.NotEstimated, null, null, 1), queued[1]);
    }

    [Fact]
    public void AnInstantIsStatedInNewYorksTimeWithItsOffsetOnBothSidesOfTheChangeAndUtcBesideIt()
    {
        // The same UTC hour on the Monday before 2026-11-01 and the Monday after it: four in the
        // morning UTC is midnight in New York under daylight time and eleven the evening before
        // under standard time.
        Assert.Equal("2026-10-26 00:00 New York (UTC-04:00), 04:00 UTC", QueueTimes.Stated(UtcAt("2026-10-26T04:00:00Z"), NewYork));
        Assert.Equal("2026-11-01 23:00 New York (UTC-05:00), 04:00 UTC", QueueTimes.Stated(UtcAt("2026-11-02T04:00:00Z"), NewYork));

        // And a request asked at peak on each side starts when the window ends, stated in the
        // offset that applies on its date.
        var before = Assert.Single(TimesAt("2026-10-26T02:00:00Z", [Request("KEYS", "2026-10-26T01:59:00Z", ResearchRequests.Outstanding)], TwoPasses));
        var after = Assert.Single(TimesAt("2026-11-02T02:00:00Z", [Request("KEYS", "2026-11-02T01:59:00Z", ResearchRequests.Outstanding)], TwoPasses));

        Assert.StartsWith("starts 2026-10-26 00:00 New York (UTC-04:00), 04:00 UTC, when the peak window ends", QueueTimes.Words(before, Request("KEYS", "2026-10-26T01:59:00Z", ResearchRequests.Outstanding), NewYork), StringComparison.Ordinal);
        Assert.StartsWith("starts 2026-11-01 23:00 New York (UTC-05:00), 04:00 UTC, when the peak window ends", QueueTimes.Words(after, Request("KEYS", "2026-11-02T01:59:00Z", ResearchRequests.Outstanding), NewYork), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheQueuePageStatesWhenEachRequestWillBeWrittenReadBackAgainstTheStore()
    {
        using var store = new TemporaryStore().Migrated();

        // Two passes that ran to their end, DGX over four minutes and NVDA over twenty-six, a
        // third that stopped before its last stage and is not one of them, and one request in
        // each state: KEYS being written since 00:35, AAPL and MSFT outstanding behind it, and
        // DGX written. The page is drawn at 00:40 on a Monday.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome) VALUES "
            + "('research-20260919T030000Z-NVDA', 'research', '2026-09-19T03:25:00Z', '2026-09-19T03:26:00Z', 'ok'),"
            + "('research-20260920T100000Z-DGX', 'research', '2026-09-20T10:03:00Z', '2026-09-20T10:04:00Z', 'ok'),"
            + "('research-20260918T150000Z-NVDA', 'fundamentals', '2026-09-18T15:00:00Z', '2026-09-18T15:29:00Z', 'ok'),"
            + "('research-20260921T003500Z-KEYS', 'fundamentals', '2026-09-21T00:35:00Z', '2026-09-21T00:36:00Z', 'ok');"
            + "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state, settled_at, run_id) VALUES "
            + "('DGX', '2026-09-20T09:59:00Z', 'list', 'paid', 'written', '2026-09-20T10:04:00Z', 'research-20260920T100000Z-DGX'),"
            + "('KEYS', '2026-09-21T00:30:00Z', 'list', 'paid', 'writing', NULL, NULL),"
            + "('AAPL', '2026-09-21T00:31:00Z', 'name', 'paid', 'outstanding', NULL, NULL),"
            + "('MSFT', '2026-09-21T00:32:00Z', 'list', 'paid', 'outstanding', NULL, NULL);");

        using var host = new PassHost(store.Root) { Clock = FixedClock.At(UtcAt("2026-09-21T00:40:00Z"), SessionZones.UnitedStates) };
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/queue"));

        // What every time rests on, stated once: two passes, fifteen minutes.
        Assert.Matches("<p class=\"queue-estimate\" data-passes=\"2\" data-median-minutes=\"15\">[^<]*15 minutes, the median of the 2 passes the store holds that ran to their end", page);

        // Each row's time, read back off its own cell against the instants worked by hand: KEYS
        // started at 00:35 and ends at 00:50; AAPL runs from 00:50 to 01:05; MSFT would start at
        // 01:05, inside the window, so it starts at 04:00 and ends at 04:15; DGX was written at
        // 10:04 the day before.
        (string Basis, string Starts, string Ends) Cell(string ticker)
        {
            var row = Regex.Match(page, $"<tr data-ticker=\"{ticker}\"[^>]*>.*?<td class=\"q-when\" data-basis=\"([^\"]*)\" data-starts=\"([^\"]*)\" data-ends=\"([^\"]*)\">([^<]*)</td>", RegexOptions.Singleline);

            Assert.True(row.Success, $"no time is drawn for {ticker}");

            return (row.Groups[1].Value, row.Groups[2].Value, row.Groups[3].Value);
        }

        Assert.Equal(("Started", "2026-09-21T00:35:00Z", "2026-09-21T00:50:00Z"), Cell("KEYS"));
        Assert.Equal(("Estimated", "2026-09-21T00:50:00Z", "2026-09-21T01:05:00Z"), Cell("AAPL"));
        Assert.Equal(("Estimated", "2026-09-21T04:00:00Z", "2026-09-21T04:15:00Z"), Cell("MSFT"));
        Assert.Equal(("Settled", string.Empty, "2026-09-20T10:04:00Z"), Cell("DGX"));

        // And the words a reader reads, in New York's time with UTC beside it.
        Assert.Contains("started 2026-09-20 20:35 New York (UTC-04:00), 00:35 UTC, and is expected to end about 2026-09-20 20:50 New York (UTC-04:00), 00:50 UTC", page, StringComparison.Ordinal);
        Assert.Contains("starts about 2026-09-21 00:00 New York (UTC-04:00), 04:00 UTC, after the 2 requests ahead of it", page, StringComparison.Ordinal);
        Assert.Contains("written 2026-09-20 06:04 New York (UTC-04:00), 10:04 UTC", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ADrainStartedWhileAnotherHoldsTheQueueWaitsForItToEnd()
    {
        using var root = new TemporaryDirectory();

        var first = await DrainLock.AcquireAsync(root.Path, () => throw new InvalidOperationException("the first drain waits for nobody"));
        var pauses = 0;

        // The second waits, and the first ends while it does.
        using var second = await DrainLock.AcquireAsync(root.Path, () =>
        {
            pauses++;
            first.Dispose();

            return Task.CompletedTask;
        });

        Assert.Equal(1, pauses);
    }
}
