using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// nightly-run, 15.1's third pull request: the overnight queue drafts the S&P 500's list, then the S&P 400's and then the
// S&P 600's, each in its page's order, before every other member, the S&P 500's first among those; and a pass over an
// S&P 400 member's facts file as the night stores it writes its sections.
// see: The overnight queue drafts the three indices' lists before every other member, the S&P 500's first
public partial class NightlyRun
{
    const string QueueOrderRun = "night-20260908T211000Z-queue-order";

    // The queue of a night whose funds' files name members the fixture recorded no draft for: a local model answering
    // nothing, so the queue stops at its first pass and says so, as it does when the runtime is not running.
    static NightQueue SilentQueue() => NightQueue.FromFixture(FixtureFolder()) with { LocalModel = new FixtureExpectations.NothingAnsweringLocal() };

    // A local model answering every section with a sentence holding no figure, which the checker accepts.
    sealed class PlainLocal : ILocalModelFeed
    {
        public int Requests { get; private set; }

        public Task<ModelAnswer> CompleteAsync(ModelRequest request, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(new ModelAnswer(request.Model, "The shares closed above their averages on the night.", 0, 0, "stop"));
        }
    }

    static OvernightQueue QueueOver(TemporaryStore store, ILocalModelFeed local)
    {
        var clock = FixedClock.At(Night, SessionZones.UnitedStates);

        return new OvernightQueue(
            new StalenessJudge(clock, store.DatabaseFile),
            sections => new ProseWriter(local, FixtureExpectations.LocalSettings(), sections, clock, store.DatabaseFile),
            new ClaimChecker(clock, store.DatabaseFile),
            ProseWriter.DefaultLane,
            TimeSpan.FromHours(OvernightQueue.DefaultHours),
            new RecordingAwake(),
            clock,
            store.DatabaseFile);
    }

    [Fact]
    public async Task TheQueueDraftsEachIndexsListInTurnBeforeEveryOtherMemberAndAPassOverAnSAndP400MembersFactsWritesItsSections()
    {
        // The fixture's night with the S&P 400's fund holding A, AA and XRAY and the S&P 600's holding AAL, four names
        // the night's bulk file carries, each stored with its facts file. Its queue answers nothing, so no draft is
        // written and every member stays queued.
        using var store = new TemporaryStore().Migrated();

        var feeds = WithFunds(Funds(Holdings("Mid-Cap", "Sep 04, 2026", "A", "AA", "XRAY"), Holdings("Small-Cap", "Sep 04, 2026", "AAL")));
        var (code, _, error) = await NightAsync(store, feeds, QueueOrderRun, FixedClock.At(Night, SessionZones.UnitedStates), SilentQueue());

        Assert.True(code == 0, error);
        Assert.Equal(["MID A", "MID AA", "MID XRAY", "SML AAL"], Texts(store, "SELECT index_code || ' ' || ticker FROM membership WHERE index_code <> 'GSPC' ORDER BY 1;"));

        // The night hands its queue the indices it reads: its own row queues the four wider members beside the S&P 500's
        // four.
        var nightsQueue = System.Text.Json.JsonDocument.Parse(Texts(store, $"SELECT detail FROM run_log WHERE run_id = '{QueueOrderRun}' AND stage = '{OvernightQueue.Stage}';").Single());

        Assert.Equal(
            ["A", "AA", "AAL", "AAPL", "KEYS", "MSFT", "NFLX", "XRAY"],
            nightsQueue.RootElement.GetProperty("queued").EnumerateArray().Select(one => one.GetString()!).Order(StringComparer.Ordinal));

        // Each page's list drawn over the night as its own step draws it: the S&P 500's families list NFLX, which the
        // filter is made to pass; the S&P 400's lists XRAY first and AA second, which their tickers would put the other
        // way, leaving A off; the S&P 600's lists AAL.
        store.Execute("UPDATE gate_result SET passed = 1, rank = 1 WHERE ticker = 'NFLX' AND session_date = '2026-09-08';");
        await new EquityBrief.Worker.Families.FamilyLister(FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile).RunAsync("queue-order-list");
        store.Execute("DELETE FROM index_family_pick WHERE session_date = '2026-09-08';");
        store.Execute(
            "INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night) VALUES " +
            "('MID', '2026-09-08', 'XRAY', 'pullback', 'listed', 1, '[]', NULL, NULL, NULL), " +
            "('MID', '2026-09-08', 'AA', 'breakout', 'listed', 2, '[]', NULL, NULL, NULL), " +
            "('SML', '2026-09-08', 'AAL', 'drift', 'listed', 1, '[]', NULL, NULL, NULL), " +
            "('GSPC', '2026-09-08', 'KEYS', 'fundamentals', 'listed', 1, '[]', NULL, NULL, NULL);");

        // The S&P 500's own order, as the queue reads it handed no wider index: its list and then its other members.
        var fiveHundred = await QueueOver(store, new FixtureExpectations.NothingAnsweringLocal()).RunAsync("queue-order-500", new DateOnly(2026, 9, 8));

        Assert.Equal(["NFLX"], fiveHundred.Listed);
        Assert.Equal("NFLX", fiveHundred.Queued[0]);
        Assert.Equal(4, fiveHundred.Queued.Count);

        // Handed the S&P 400 and 600: the S&P 500's list, from 17.8 the S&P 500's fundamentals-first pick KEYS after it,
        // the 400's in its page's order and the 600's, then every other member, the S&P 500's first in their own order
        // and then the 400's A.
        var three = await QueueOver(store, new FixtureExpectations.NothingAnsweringLocal()).RunAsync("queue-order-three", new DateOnly(2026, 9, 8), wider: ["MID", "SML"]);

        Assert.Equal(["NFLX", "KEYS", "XRAY", "AA", "AAL", .. fiveHundred.Queued.Skip(1).Where(ticker => ticker != "KEYS"), "A"], three.Queued);

        // And a pass over the S&P 400's AA reads its facts file as the night stored it and writes its sections, each
        // accepted by the checker, written by the local model and handed no document.
        var written = await QueueOver(store, new PlainLocal()).RunAsync("queue-order-written", new DateOnly(2026, 9, 8), wider: ["MID", "SML"]);
        var pass = Assert.Single(written.Completed, one => one.Ticker == "AA");
        var asked = ProseWriter.DefaultLane.Where(section => !ClaimRules.IsResearched(section)).ToArray();

        Assert.Equal(asked, pass.Asked);
        Assert.Equal(asked, pass.Written.Select(section => section.Section).Distinct(StringComparer.Ordinal));
        Assert.Empty(pass.NotWritten);
        Assert.Equal([ClaimChecker.Accepted], Texts(store, "SELECT DISTINCT status FROM research_section WHERE ticker = 'AA';"));
        Assert.Equal(["[]"], Texts(store, "SELECT DISTINCT source_ids FROM research_section WHERE ticker = 'AA';"));
        Assert.Equal([FixtureExpectations.LocalSettings().Model], Texts(store, "SELECT DISTINCT model FROM research_section WHERE ticker = 'AA';"));
    }
}
