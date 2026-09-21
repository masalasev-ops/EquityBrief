using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Passes;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Reading;

// read-surface, 9.3 and 9.4: the queue screen and the lane the head of the page states.
//
// The screen is read back off its own markup against the store in both directions, so
// neither a request the store lacks nor one it holds and the page omits passes. The lane
// is read off the surface a person reads rather than off the constant that draws it,
// because what the done condition is about is what the operator sees.
public partial class ReadSurface
{
    // Every request the page draws, by the state its region puts it in, read off the
    // markup rather than off what the page was handed.
    static IReadOnlyList<(string Ticker, string AskedAt, string Region)> DrawnRequests(string markup)
    {
        // Split on the region opening rather than matched with one pattern over the whole
        // page: an empty region carries no table, so a pattern closing on one runs past it
        // into the next and reports that region's rows under this one's name.
        const string Opens = "<div class=\"queue-part\" data-region=\"";

        return
        [
            .. markup
                .Split(Opens, StringSplitOptions.RemoveEmptyEntries)
                .Skip(1)
                .Select(part => (Region: part[..part.IndexOf('"', StringComparison.Ordinal)], Markup: part))
                .SelectMany(part => Regex
                    .Matches(part.Markup, "<tr data-ticker=\"(?<ticker>[^\"]+)\" data-asked-at=\"(?<asked>[^\"]+)\"")
                    .Select(row => (row.Groups["ticker"].Value, row.Groups["asked"].Value, part.Region))),
        ];
    }

    static QueuedCell Queued(string ticker, string askedAt, string state, string? reason = null) =>
        new(ticker, askedAt, ResearchRequests.FromList, ResearchRequests.Paid, state, null, null, reason);

    [Fact]
    public void TheQueueScreenPutsEveryRequestInTheRegionItsStatePutsItIn()
    {
        // One request in each state the store admits, so every region is drawn over a
        // population rather than three of the five falling in one.
        QueuedCell[] held =
        [
            Queued("AAPL", "2026-09-20T10:00:00Z", ResearchRequests.Outstanding),
            Queued("MSFT", "2026-09-20T11:00:00Z", ResearchRequests.Outstanding),
            Queued("KEYS", "2026-09-20T12:00:00Z", ResearchRequests.Writing),
            Queued("INCY", "2026-09-20T13:00:00Z", ResearchRequests.Written),
            Queued("NVDA", "2026-09-20T14:00:00Z", ResearchRequests.Refused, "the pass came to unavailable"),
            Queued("TSLA", "2026-09-20T15:00:00Z", ResearchRequests.Withdrawn, "taken out of the queue before it was written"),
        ];

        var markup = new SinglePageApp().QueueRegion(held);
        var drawn = DrawnRequests(markup);

        // Forwards: every request the store holds is on the page, in the region its state
        // puts it in. A request drawn nowhere is the omission this reads for.
        Assert.Equal(held.Length, drawn.Count);

        foreach (var row in held)
        {
            var found = Assert.Single(drawn, entry => entry.Ticker == row.Ticker && entry.AskedAt == row.AskedAt);

            var expected = row.State switch
            {
                ResearchRequests.Outstanding => "outstanding",
                ResearchRequests.Writing => "writing",
                _ => "settled",
            };

            Assert.Equal(expected, found.Region);
        }

        // Backwards: nothing is drawn that the store does not hold.
        Assert.All(drawn, entry => Assert.Contains(held, row => row.Ticker == entry.Ticker && row.AskedAt == entry.AskedAt));

        // The counts the head states are the counts the regions drew, so a header that
        // said one thing over regions holding another would fail rather than read well.
        Assert.Contains("data-outstanding=\"2\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-writing=\"1\"", markup, StringComparison.Ordinal);
        Assert.Contains("data-settled=\"3\"", markup, StringComparison.Ordinal);

        // A settled request keeps what came of it and why, because what is asked of this
        // screen is what was asked for and what came of it.
        Assert.Contains("the pass came to unavailable", markup, StringComparison.Ordinal);
        Assert.Contains("taken out of the queue before it was written", markup, StringComparison.Ordinal);
    }

    [Fact]
    public void TheControlToTakeOneOutIsDrawnOnAnOutstandingRequestAndOnNoOther()
    {
        // What the operator asked for, bounded by what they asked for: a report that has
        // not been generated is one nobody has started, so the control is on those rows
        // and on no others. Read off the markup, because a control the code would draw
        // and the page does not is the fault this is about.
        QueuedCell[] held =
        [
            Queued("AAPL", "2026-09-20T10:00:00Z", ResearchRequests.Outstanding),
            Queued("KEYS", "2026-09-20T12:00:00Z", ResearchRequests.Writing),
            Queued("INCY", "2026-09-20T13:00:00Z", ResearchRequests.Written),
        ];

        var markup = new SinglePageApp().QueueRegion(held);

        var controls = Regex
            .Matches(markup, "<form class=\"withdraw-control\"[^>]*data-takes=\"(?<ticker>[^\"]+)\"[^>]*data-asked-at=\"(?<asked>[^\"]+)\"")
            .Select(control => (Ticker: control.Groups["ticker"].Value, Asked: control.Groups["asked"].Value))
            .ToArray();

        var only = Assert.Single(controls);

        Assert.Equal("AAPL", only.Ticker);

        // It names the request by its instant and not by the name, because a name may
        // have been asked for before and settled since.
        Assert.Equal("2026-09-20T10:00:00Z", only.Asked);
        Assert.Contains("name=\"askedAt\" value=\"2026-09-20T10:00:00Z\"", markup, StringComparison.Ordinal);

        // And it posts to the withdraw route rather than to the route that asks for one.
        Assert.Contains($"action=\"{SinglePageApp.WithdrawRoute}AAPL\"", markup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestIsTakenOutFromTheScreenAndOneTheWorkerHoldsIsRefusedThere()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        Assert.Contains("KEYS", await client.GetStringAsync("/screens/name/KEYS"), StringComparison.Ordinal);

        await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "list")));

        // The screen, before anything is taken out, holding the request as outstanding
        // with its control.
        var before = await client.GetStringAsync("/screens/queue");
        var askedAt = Assert.Single(Regex.Matches(before, "data-takes=\"KEYS\" data-asked-at=\"(?<asked>[^\"]+)\"")
            .Select(found => found.Groups["asked"].Value));

        var taken = await client.SendAsync(Press(SinglePageApp.WithdrawRoute, "KEYS", SinglePageApp.PassHeaderValue, ("askedAt", askedAt)));

        Assert.Equal(HttpStatusCode.OK, taken.StatusCode);

        // Read back off the screen rather than out of the store: the request has left the
        // outstanding region, is drawn as settled, and carries no control.
        var after = await client.GetStringAsync("/screens/queue");

        Assert.Contains("data-outstanding=\"0\"", after, StringComparison.Ordinal);
        Assert.DoesNotContain("data-takes=\"KEYS\"", after, StringComparison.Ordinal);

        // Nothing is deleted, so it still reads as having been asked for.
        Assert.Contains(DrawnRequests(after), row => row is { Ticker: "KEYS", Region: "settled" });
        Assert.Contains(ResearchRequests.Withdrawn, after, StringComparison.Ordinal);

        // A request the worker holds is not one that has not been generated, so the same
        // press is refused on the surface and the refusal names the state that refused it.
        // The claim is made in the store rather than by asking again, because a second ask
        // in the same second is the same request by its key and would be testing that.
        Rows(store, "UPDATE research_request SET state = 'writing', settled_at = NULL, reason = NULL WHERE ticker = 'KEYS';");

        var held = await client.SendAsync(Press(SinglePageApp.WithdrawRoute, "KEYS", SinglePageApp.PassHeaderValue, ("askedAt", askedAt)));

        Assert.Equal(HttpStatusCode.Conflict, held.StatusCode);
        Assert.Contains(ResearchRequests.Writing, await held.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // And the screen draws it as being written, with no control on it: a report
        // somebody is writing is not one that has not been generated.
        var writing = await client.GetStringAsync("/screens/queue");

        Assert.Contains(DrawnRequests(writing), row => row is { Ticker: "KEYS", Region: "writing" });
        Assert.DoesNotContain("data-takes=\"KEYS\"", writing, StringComparison.Ordinal);
        Assert.Contains("data-writing=\"1\"", writing, StringComparison.Ordinal);
    }

    [Fact]
    public void TheHeadOfThePageStatesTheLaneInTheOperatorsWordsAndOffersOneOfTheTwo()
    {
        var shell = new SinglePageApp().Shell("EquityBrief");

        // The operator's two words, and no model's name anywhere near them. The names are
        // read for rather than the absence assumed, because what this refuses is a page
        // that states a lane by naming what would write it.
        // see: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
        var lane = Assert.Single(Regex.Matches(shell, "<div class=\"lane\".*?</div>", RegexOptions.Singleline).Select(found => found.Value));

        Assert.Contains("Report generation", lane, StringComparison.Ordinal);
        Assert.Contains(">Local</span>", lane, StringComparison.Ordinal);
        Assert.Contains(">Paid</span>", lane, StringComparison.Ordinal);

        Assert.All(
            new[] { "deepseek", "qwen", "gpt", "claude", "llama", "gemma", "mistral", "27b", "9b" },
            model => Assert.DoesNotContain(model, lane, StringComparison.OrdinalIgnoreCase));

        // The local choice is drawn and is not selectable, which is a claim about the
        // markup a person reads: it carries no control at all, and says so on itself.
        Assert.Contains("data-choice=\"local\" data-offered=\"false\" aria-disabled=\"true\"", lane, StringComparison.Ordinal);
        Assert.Contains("data-choice=\"paid\" data-offered=\"true\"", lane, StringComparison.Ordinal);
        Assert.DoesNotContain("<button", lane, StringComparison.Ordinal);
        Assert.DoesNotContain("<input", lane, StringComparison.Ordinal);
        Assert.DoesNotContain("<a ", lane, StringComparison.Ordinal);

        // And the lane the head states is the one a request would carry.
        Assert.Contains($"data-lane=\"{SinglePageApp.PaidLane}\"", lane, StringComparison.Ordinal);
    }

    [Fact]
    public void TheScreenSaysWhatTheChoiceNobodyCanMakeIsWaitingOn()
    {
        // Stated on the surface a person reads and not only on the element, because a
        // choice drawn as refused with no reason beside it is a page refusing without
        // saying why. Read off the queue screen, which is where a reader is deciding
        // whether to ask for a report.
        var markup = new SinglePageApp().QueueRegion([]);

        var line = Assert.Single(Regex.Matches(markup, "<p class=\"lane-waits\"[^>]*>(?<said>[^<]+)</p>").Select(found => found.Groups["said"].Value));

        Assert.Contains("paid", line, StringComparison.Ordinal);
        Assert.Contains(SinglePageApp.LaneWaitsOn, line, StringComparison.Ordinal);

        // It says what it waits on rather than that it is unavailable, which is the
        // difference between a reason and a refusal.
        Assert.Contains("compared", line, StringComparison.Ordinal);

        // And it is not hidden, which is what would make it stated in the code and not on
        // the surface. The one rule naming it sets no display, so nothing draws it away.
        var styles = Stylesheet.Css;

        Assert.DoesNotContain(".lane-waits{display:none", styles, StringComparison.Ordinal);
    }

    [Fact]
    public void ARequestIsSettledByWhatThePassCameToAndNeverByWhetherTheVerbRan()
    {
        // The defect the first drain had, kept as a case: it settled on the verb's exit
        // code, so two passes that reported unavailable were recorded as written. A verb
        // that exits without failing has run, and a pass that ran is not a pass that
        // wrote.
        var wrote = RequestDrain.SettlementFor(RequestDrain.Ok);

        Assert.Equal(ResearchRequests.Written, wrote.State);
        Assert.Null(wrote.Reason);

        // Every other outcome the runner writes settles the request as refused and carries
        // the run's own word for why, so the queue screen says what came of it.
        foreach (var outcome in new[] { "unavailable", "failed", "paused", "nothing to write" })
        {
            var refused = RequestDrain.SettlementFor(outcome);

            Assert.Equal(ResearchRequests.Refused, refused.State);
            Assert.Contains(outcome, refused.Reason!, StringComparison.Ordinal);
        }

        // A request whose pass left no run at all is refused and says that, rather than
        // being read as written because nothing contradicted it.
        var absent = RequestDrain.SettlementFor(null);

        Assert.Equal(ResearchRequests.Refused, absent.State);
        Assert.Contains("no run at all", absent.Reason!, StringComparison.Ordinal);

        // And the word the runner writes for a pass that ran to its end is the word this
        // reads, rather than a second spelling of it kept beside the check.
        Assert.Equal("ok", RequestDrain.Ok);
    }

    // Four requests whose instants and whose names sort against each other, so an order
    // keyed on the name is a different order rather than the same one by coincidence.
    // Two rows cannot tell the two apart often enough to be evidence: any two agree half
    // the time, and the queue is drained until it is empty rather than two at a time.
    const string FourAskedInAnOrderTheirNamesDoNotShare = @"
        INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES
            ('ZS',   '2026-09-20T10:00:00Z', 'list', 'paid', 'outstanding'),
            ('MMM',  '2026-09-20T11:00:00Z', 'list', 'paid', 'outstanding'),
            ('AAPL', '2026-09-20T12:00:00Z', 'name', 'paid', 'outstanding'),
            ('NVDA', '2026-09-20T13:00:00Z', 'list', 'paid', 'outstanding');
    ";

    [Fact]
    public async Task TheDrainTakesTheOldestRequestFirstAndNotTheOneWhoseNameSortsFirst()
    {
        // What the queue is for: a report asked for before another is written before it.
        // Read by draining to the end rather than by claiming twice, because the property
        // is the order of the whole and not of its first pair.
        using var store = new TemporaryStore().Migrated();

        store.Execute(FourAskedInAnOrderTheirNamesDoNotShare);

        await using var connection = store.Open();

        var claimed = DateTimeOffset.Parse("2026-09-20T14:00:00Z", CultureInfo.InvariantCulture);
        var taken = new List<string>();

        while (await RequestDrain.ClaimAsync(connection, claimed) is { } request)
        {
            taken.Add(request.Ticker);
        }

        // The instants, not the names, which sort AAPL, MMM, NVDA, ZS and share no
        // position with this.
        Assert.Equal(["ZS", "MMM", "AAPL", "NVDA"], taken);
    }

    [Fact]
    public async Task TheOutstandingRegionDrawsTheOldestRequestFirst()
    {
        // The region states what is waiting and in what order it will be written, so the
        // order is part of what it says. Read off the page's own markup in the order the
        // markup carries it, rather than off the read that fed it.
        using var store = new TemporaryStore().Migrated();

        store.Execute(FourAskedInAnOrderTheirNamesDoNotShare);

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        var drawn = DrawnRequests(await client.GetStringAsync("/screens/queue"))
            .Where(row => row.Region == "outstanding")
            .Select(row => row.Ticker);

        Assert.Equal(["ZS", "MMM", "AAPL", "NVDA"], drawn);
    }

    [Fact]
    public async Task ASecondAskIsRefusedWhileAnOlderRequestForTheNameIsStillOutstanding()
    {
        // The rule is one outstanding request per name, and the case that matters is a
        // press while a request asked for earlier is still waiting, which is what a queue
        // nobody has drained holds. The request already in the store is dated well before
        // this press, so the key of ticker and instant cannot refuse it and only the index
        // over the outstanding state can. Pressing twice in one second tests the key
        // instead, because both presses carry the same instant.
        using var store = await FixtureReplay.ReplayedAsync();

        using var host = new PassHost(store.Root);
        using var client = host.CreateClient();

        Assert.Contains("KEYS", await client.GetStringAsync("/screens/name/KEYS"), StringComparison.Ordinal);

        Rows(store, "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) "
            + "VALUES ('KEYS', '2026-09-01T09:00:00Z', 'list', 'paid', 'outstanding');");

        var again = await client.SendAsync(Press(SinglePageApp.PassRoute, "KEYS", SinglePageApp.PassHeaderValue, ("from", "name")));

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Contains("already in the queue", await again.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // Nothing was added, and what stands is the request asked for first.
        Assert.Equal(
            [["KEYS", "2026-09-01T09:00:00Z", ResearchRequests.Outstanding]],
            Rows(store, "SELECT ticker, asked_at, state FROM research_request;"));
    }

    // A store holding one name's earlier pass that ran to its end, and one request for
    // that name nobody has started. The earlier pass is what a read bounded by the name
    // alone would return for the request below it.
    static TemporaryStore WithAnEarlierPass(string ticker)
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome) VALUES "
            + $"('research-20260901T100000Z-{ticker}', 'research', '2026-09-01T10:00:00Z', '2026-09-01T10:05:00Z', 'ok');"
            + "INSERT INTO research_request (ticker, asked_at, asked_from, lane, state) VALUES "
            + $"('{ticker}', '2026-09-20T12:00:00Z', 'list', 'paid', 'outstanding');");

        return store;
    }

    static (string State, string RunId, string Reason) SettledRow(TemporaryStore store, string ticker) =>
        Assert.Single(Rows(store, $"SELECT state, run_id, reason FROM research_request WHERE ticker = '{ticker}';")
            .Select(row => (row[0], row[1], row[2])));

    [Fact]
    public async Task ARequestWhosePassWroteNoRunIsRefusedAndNeverSettledUnderAnEarlierOne()
    {
        // A pass refused before the runner starts writes no run at all, and the request is
        // settled on that rather than on whatever the name last did. Read for the name
        // alone, the newest run here is a pass that ran to its end three weeks earlier, so
        // a request that wrote nothing would be recorded as written and carry that run.
        using var store = WithAnEarlierPass("KEYS");
        await using var connection = store.Open();

        var claimed = DateTimeOffset.Parse("2026-09-20T12:00:05Z", CultureInfo.InvariantCulture);
        var request = await RequestDrain.ClaimAsync(connection, claimed);

        Assert.NotNull(request);

        // No run started at or after the claim, because the pass wrote none.
        var (runId, outcome) = await RequestDrain.PassAsync(connection, request);

        Assert.Null(runId);
        Assert.Null(outcome);

        var (state, reason) = RequestDrain.SettlementFor(outcome);

        await RequestDrain.SettleAsync(connection, request, state, reason, runId, claimed);

        var settled = SettledRow(store, "KEYS");

        Assert.Equal(ResearchRequests.Refused, settled.State);
        Assert.Equal("null", settled.RunId);
        Assert.Contains("no run at all", settled.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARequestIsSettledUnderItsOwnRunAndNotTheNewestTheNameHolds()
    {
        // The other direction: a pass that did write a run settles under that run, and the
        // earlier one standing beside it is not the one recorded.
        using var store = WithAnEarlierPass("KEYS");
        await using var connection = store.Open();

        var claimed = DateTimeOffset.Parse("2026-09-20T12:00:05Z", CultureInfo.InvariantCulture);
        var request = await RequestDrain.ClaimAsync(connection, claimed);

        Assert.NotNull(request);

        // The run this request's own pass wrote, which starts after the claim.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome) VALUES "
            + "('research-20260920T120010Z-KEYS', 'research', '2026-09-20T12:00:10Z', '2026-09-20T12:04:00Z', 'ok');");

        var (runId, outcome) = await RequestDrain.PassAsync(connection, request);

        Assert.Equal("research-20260920T120010Z-KEYS", runId);
        Assert.Equal(RequestDrain.Ok, outcome);

        var (state, reason) = RequestDrain.SettlementFor(outcome);

        await RequestDrain.SettleAsync(connection, request, state, reason, runId, claimed);

        var settled = SettledRow(store, "KEYS");

        Assert.Equal(ResearchRequests.Written, settled.State);
        Assert.Equal("research-20260920T120010Z-KEYS", settled.RunId);
    }

    [Fact]
    public async Task ARunOfAnotherNameOrAnotherStageIsNotReadAsThisRequestsPass()
    {
        // The run is matched by the pattern a pass names its runs by, so a row ending in
        // this name that no pass named is not read as this request's. All three sit after
        // the claim, which is what leaves the pattern alone deciding: another name's pass,
        // another stage's row for this name, and a research row under a name no pass
        // writes, which is the one a pattern keyed on the ending alone would take.
        using var store = WithAnEarlierPass("KEYS");
        await using var connection = store.Open();

        var claimed = DateTimeOffset.Parse("2026-09-20T12:00:05Z", CultureInfo.InvariantCulture);
        var request = await RequestDrain.ClaimAsync(connection, claimed);

        Assert.NotNull(request);

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome) VALUES "
            + "('research-20260920T120010Z-INCY', 'research', '2026-09-20T12:00:10Z', '2026-09-20T12:04:00Z', 'ok'),"
            + "('night-20260920T120010Z-queue-KEYS', 'overnight queue', '2026-09-20T12:00:10Z', '2026-09-20T12:04:00Z', 'ok'),"
            + "('night-20260920T120010Z-KEYS', 'research', '2026-09-20T12:00:10Z', '2026-09-20T12:04:00Z', 'ok');");

        var (runId, outcome) = await RequestDrain.PassAsync(connection, request);

        Assert.Null(runId);
        Assert.Null(outcome);
    }
}
