using System.Globalization;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Quarters;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Shortlist;

namespace EquityBrief.Tests.Checks;

// nightly-run: the night reads every member's reported quarters after its swing readings and before its
// listings, and asks for the members due after its close and before the overnight queue.
// see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
// see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted
public partial class NightlyRun
{
    [Fact]
    public async Task TheNightReadsTheReportedQuartersBeforeTheListingsAndAsksForThemAfterTheClose()
    {
        // Section 14's order, read off the document.
        var steps = NightlyRunSteps.In(File.ReadAllText(Repository.Architecture)).ToList();
        var readings = steps.IndexOf(ArchitectureConformance.ReadingsStep);
        var quarters = steps.IndexOf(ArchitectureConformance.QuartersStep);

        Assert.True(readings > 0 && quarters > 0, "Section 14 holds neither step as the claims key them.");
        Assert.StartsWith("Compute the swing readings", steps[readings - 1], StringComparison.Ordinal);
        Assert.StartsWith("Evaluate the list reasons", steps[readings + 1], StringComparison.Ordinal);
        Assert.StartsWith("Close the arithmetic", steps[quarters - 1], StringComparison.Ordinal);
        Assert.StartsWith("Run the overnight queue", steps[quarters + 1], StringComparison.Ordinal);

        // And the night running it, over the fixture.
        using var store = new TemporaryStore();

        var (code, output, error) = await NightAsync(store, runId: "night-with-quarters");

        Assert.True(code == 0, error);

        var stages = RunLog(store, "night-with-quarters").Select(row => row.Stage).ToList();
        var read = stages.IndexOf(FundamentalReader.Stage);
        var asked = stages.IndexOf(QuarterFetcher.Stage);

        Assert.True(read > stages.IndexOf(SwingReader.Stage) && read < stages.IndexOf(ShortlistBuilder.Stage), string.Join(", ", stages));
        Assert.True(asked > stages.IndexOf(EquityBrief.Worker.Nights.NightClose.Stage) && asked < stages.IndexOf(EquityBrief.Worker.Research.OvernightQueue.Stage), string.Join(", ", stages));

        // The readings wrote a row for every member, and each reads no fundamentals yet: the night holds
        // no quarter fetched before it.
        Assert.Equal(
            [["AAPL", "no fundamentals yet"], ["KEYS", "no fundamentals yet"], ["MSFT", "no fundamentals yet"], ["NFLX", "no fundamentals yet"]],
            StoreRows(store, "SELECT ticker, state FROM fundamental_reading WHERE session_date = '2026-09-08' ORDER BY ticker;"));

        // And in the same step, after them and before the listings, the member readings: a row for every member under
        // its index, each carrying the state the readings wrote, and the switches' row beside them.
        var members = stages.IndexOf(EquityBrief.Worker.Members.MemberReader.Stage);

        Assert.True(members > read && members < stages.IndexOf(ShortlistBuilder.Stage), string.Join(", ", stages));
        Assert.Equal(
            [["GSPC", "AAPL", "no fundamentals yet"], ["GSPC", "KEYS", "no fundamentals yet"], ["GSPC", "MSFT", "no fundamentals yet"], ["GSPC", "NFLX", "no fundamentals yet"]],
            StoreRows(store, "SELECT index_code, ticker, state FROM member_reading WHERE session_date = '2026-09-08' ORDER BY ticker;"));
        Assert.Equal([["2026-09-08"]], StoreRows(store, "SELECT session_date FROM switch_reading;"));

        // The quarters step asked for each member once, the fill's, and stored what each answer carried.
        Assert.Equal(
            [["AAPL", "fill", "stored"], ["KEYS", "fill", "stored"], ["MSFT", "fill", "stored"], ["NFLX", "fill", "stored"]],
            StoreRows(store, "SELECT ticker, reason, outcome FROM quarter_ask ORDER BY ticker;"));
        Assert.Contains("  quarters: 4 of 4 member(s) due asked: 0 reporting, 0 waiting, 0 joining, 4 filled", output, StringComparison.Ordinal);

        // Handed no token from the night's deadline, which bounds the arithmetic alone: the step's call in
        // the night's own list passes none, so its own limit is what bounds it.
        var night = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Nightly.cs"));
        var step = night[night.IndexOf($"new(\"{QuarterFetcher.Stage}\"", StringComparison.Ordinal)..];

        step = step[..step.IndexOf("}),", StringComparison.Ordinal)];

        Assert.DoesNotContain("night.Token", step, StringComparison.Ordinal);
        Assert.Contains(".RunAsync(indexCode, runId, wider: wider)", step, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheQueueTakesImprovingBusinessesFirstWhereTheNightStoredItsReadingsAndTheFiltersOrderWithinAState()
    {
        // Over a copy of the fixture's night the filter is made to pass all four, NFLX ranked first, MSFT
        // second, AAPL third and KEYS fourth, and three of the night's readings are replaced by ones a later
        // night stores: NFLX holding one quarter, too few, MSFT improving and KEYS deteriorating, AAPL keeping
        // the night's own, no fundamentals yet. Worked by hand: MSFT first, then the two reading no state in
        // the filter's order, NFLX before AAPL, and KEYS last, whatever its rank.
        var night = await FixtureReplay.NightAsync(NightQueue.FromFixture(FixtureFolder(), new RecordingAwake()) with { LocalModel = new FixtureExpectations.NothingAnsweringLocal() });

        using var store = night.Store;

        Assert.True(night.Code == 0, night.Error);

        foreach (var (ticker, rank) in new[] { ("NFLX", 1), ("MSFT", 2), ("AAPL", 3), ("KEYS", 4) })
        {
            store.Execute($"UPDATE gate_result SET passed = 1, rank = {rank} WHERE ticker = '{ticker}' AND session_date = '2026-09-08';");
        }

        store.Execute("DELETE FROM fundamental_reading WHERE session_date = '2026-09-08' AND ticker IN ('NFLX', 'MSFT', 'KEYS');");
        FixtureExpectations.StoreReadings(store, "2026-09-08", "NFLX", QuarterReadings.Of([FixtureExpectations.Quarter(0)], null, null));
        FixtureExpectations.StoreReadings(store, "2026-09-08", "MSFT", QuarterReadings.Of(FixtureExpectations.Quarters(4), null, null));
        FixtureExpectations.StoreReadings(store, "2026-09-08", "KEYS", QuarterReadings.Of(FixtureExpectations.Falling(), null, null));

        Assert.Equal(
            [["AAPL", FundamentalState.NoFundamentalsYet], ["KEYS", FundamentalState.Deteriorating], ["MSFT", FundamentalState.Improving], ["NFLX", FundamentalState.NotEnoughQuarters]],
            StoreRows(store, "SELECT ticker, state FROM fundamental_reading WHERE session_date = '2026-09-08' ORDER BY ticker;"));

        var clock = FixedClock.At(Night, SessionZones.UnitedStates);

        // The page's list is drawn again over the rows as changed, as the night's own step draws it, and
        // it lists the four under the pullback family in that order, which the queue then follows.
        await new EquityBrief.Worker.Families.FamilyLister(clock, store.DatabaseFile).RunAsync("state-first-list");

        Assert.Equal(
            [["MSFT", "1"], ["NFLX", "2"], ["AAPL", "3"], ["KEYS", "4"]],
            StoreRows(store, "SELECT ticker, place FROM family_pick WHERE session_date = '2026-09-08' AND state = 'listed' ORDER BY place;"));

        var queue = await new OvernightQueue(
            new StalenessJudge(clock, store.DatabaseFile),
            sections => new ProseWriter(new FixtureExpectations.NothingAnsweringLocal(), new LocalModelSettings(null, null, null, null, null), sections, clock, store.DatabaseFile),
            new ClaimChecker(clock, store.DatabaseFile),
            ProseWriter.DefaultLane,
            TimeSpan.FromHours(OvernightQueue.DefaultHours),
            new RecordingAwake(),
            clock,
            store.DatabaseFile).RunAsync("state-first", new DateOnly(2026, 9, 8));

        Assert.Equal(["MSFT", "NFLX", "AAPL", "KEYS"], queue.Listed);
        Assert.Equal(["MSFT", "NFLX", "AAPL", "KEYS"], queue.Queued);
    }

    [Fact]
    public async Task TheNightAsksForTheFirstImprovingBusinessWhereItStoredItsReadingsAndTheFiltersFirstOnANightItStoredNone()
    {
        // The replay's night with MSFT's trade passing first and AAPL's second, as the filter's order gives
        // them. Where the night stored AAPL improving and MSFT deteriorating the night asks for AAPL first,
        // the first its list draws, and MSFT after it; the same night holding no reading, as a night before
        // the readings existed holds none, asks for MSFT first, the filter's first, and AAPL after it.
        foreach (var (stored, first, second) in new[] { (true, "AAPL", "MSFT"), (false, "MSFT", "AAPL") })
        {
            using var store = await FixtureReplay.ReplayedAsync();

            var night = Expected("night-request").GetProperty("replayNight").GetString()!;

            Passing(store, night);
            store.Execute($"DELETE FROM fundamental_reading WHERE session_date = '{night}';");

            if (stored)
            {
                FixtureExpectations.StoreReadings(store, night, "AAPL", QuarterReadings.Of(FixtureExpectations.Quarters(4), null, null));
                FixtureExpectations.StoreReadings(store, night, "MSFT", QuarterReadings.Of(FixtureExpectations.Falling(), null, null));
            }

            Assert.Equal("MSFT", FirstPassed(store, night));

            var ask = await RequestDrain.AskForTheNightAsync(
                store.DatabaseFile,
                DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates));

            Assert.Equal([first, second], ask.Asked);
            Assert.Equal($"{first} is first on the list, and a report on it was asked for; {second} is number 2 on the list, and a report on it was asked for", ask.Line);
        }
    }
}
