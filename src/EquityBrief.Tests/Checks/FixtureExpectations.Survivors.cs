using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: a survivor registers as its setup's variant where it can run there and has room, the variant with
// the lowest result per $1 risked past the minimum making room and never the running revision, worked by hand on both sides
// of the minimum; queues where its setup cannot yet run a part of its line, or where none can go; is walked each night it
// queues, its picks stored among no buys, its record carried across its registration as one record, worked by hand.
// see: A passed idea registers by itself as its setup's variant where it has room or a variant past its minimum makes room, and queues where none can go
// see: A survivor that has to queue is walked each night as a record drawn among no buys and carries it when a slot opens
public partial class FixtureExpectations
{
    // The night's step that walks the queued survivors, as section 14 states it.
    internal const string SurvivorStep =
        "From 24.27, walk each queued survivor to the night's setups its line keeps, storing its picks with the night's close and typical move and " +
        "drawing them among no buys, and admit it again, so a slot that has opened registers it as its setup's variant; arithmetic over the night's " +
        "stored rows, calling no model and asking nothing of a network (see: A survivor that has to queue is walked each night as a record drawn " +
        "among no buys and carries it when a slot opens).";

    internal static readonly string[] SurvivorClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Making room for a survivor"),
        CheckReach.Key(Scope.FailureTable, "A survivor that cannot register yet"),
    ];

    internal static string[] SurvivorRows =>
        [.. ComponentAccess.SurvivorRows, .. SchemaColumns.SurvivorStoreRows, CheckReach.Key(NightlyRunSteps.Heading, SurvivorStep), .. SurvivorClaims, .. Reading.ReadSurface.SurvivorRows];

    static IdeaLine Survivor(string text) => IdeaGrammar.Parse(text).Line!;

    const string Runs = "event breakout(500) · if volume_ratio above tenth 8 · buy next open · stop 2 typical days · take profit whole at 2 risks · sell after 63 sessions · cap 5 a night by strength highest";

    [Fact]
    public void ASurvivorRegistersWhereItCanRunAndHasRoomTheWorstPastTheMinimumMakingRoomAndQueuesOtherwise()
    {
        // From the January pin move its setup runs a time limit and a cap a night of its own; it cannot run every setup it keeps
        // or a loose event, and each of those queues and says why.
        Assert.Null(Survivors.CannotRunYet(Survivor(Runs)));
        Assert.Null(Survivors.CannotRunYet(Survivor(Runs.Replace("sell after 63", "sell after 20", StringComparison.Ordinal))));
        Assert.Null(Survivors.CannotRunYet(Survivor(Runs.Replace("cap 5", "cap 3", StringComparison.Ordinal))));
        Assert.Contains("buys every setup it keeps", Survivors.CannotRunYet(Survivor(Runs.Replace(" · cap 5 a night by strength highest", string.Empty, StringComparison.Ordinal))), StringComparison.Ordinal);
        Assert.Contains("loose gates", Survivors.CannotRunYet(Survivor(Runs.Replace("breakout(500)", "breakout(500) loose", StringComparison.Ordinal))), StringComparison.Ordinal);
        Assert.Equal(60, IdeaGrammar.FamilyTimeLimit("drift"));
        Assert.Contains(60, IdeaGrammar.SessionLimits);

        // Room: a setup holding fewer than nine registers it in a free slot.
        SurvivorSlot Slot(string name, int nights, double? made) => new(name, false, nights, made);
        var running = new SurvivorSlot("the live breakout", true, int.MaxValue, -9.0);

        Assert.Equal((Survivors.Registered, (string?)null), Admitted([running, Slot("a", 70, 0.1)]));

        // A setup holding nine: of those past 63 nights with a closed trade, the lowest per $1 risked makes room, never the
        // running revision however low its own, and never one under the minimum however low. By hand: d at 63 nights and
        // -0.20 goes, before e at 62 nights and -0.50.
        SurvivorSlot[] full = [running, Slot("a", 100, 0.30), Slot("b", 90, 0.10), Slot("c", 80, null), Slot("d", 63, -0.20), Slot("e", 62, -0.50), Slot("f", 70, 0.00), Slot("g", 64, 0.05), Slot("h", 200, 0.40)];

        Assert.Equal((Survivors.Registered, (string?)"d"), Admitted(full));

        // One night younger, d is under the minimum too, and f at 0.00 goes.
        full[4] = Slot("d", 62, -0.20);
        Assert.Equal((Survivors.Registered, (string?)"f"), Admitted(full));

        // None past the minimum: it queues, naming the nearest at 62 of the 63.
        SurvivorSlot[] young = [running, .. Enumerable.Range(0, 8).Select(at => Slot(((char)('a' + at)).ToString(), 62 - at, -1.0))];
        var queued = Survivors.Admit(Survivor(Runs), young);

        Assert.Equal((Survivors.Queued, (string?)null), (queued.Outcome, queued.Retires));
        Assert.Equal("All 9 slots held; none can make room yet, the nearest having run 62 of the 63 nights.", queued.Why);

        // A name that is the running revision's is never written or retired.
        Assert.True(Survivors.TouchesRunning("the live breakout", "the live breakout"));
        Assert.True(Survivors.TouchesRunning("the live pullback", "the live breakout"));
        Assert.False(Survivors.TouchesRunning(SurvivorDesk.NameOf("breakout", 4), "the live breakout"));
        Assert.Equal(4L, SurvivorDesk.SurvivorOf(SurvivorDesk.NameOf("breakout", 4), "breakout"));
        Assert.Null(SurvivorDesk.SurvivorOf("the live breakout", "breakout"));

        (string, string?) Admitted(IReadOnlyList<SurvivorSlot> slots) => Survivors.Admit(Survivor(Runs), slots) is var one ? (one.Outcome, one.Retires) : default;
    }

    [Fact]
    public void ASurvivorsVariantStatesItsConditionsPlanAndOrderAndItsRecordIsOneRecordAcrossItsRegistration()
    {
        // The variant's settings: the running revision's own, a band's edges as levels, the plan and the order. By hand, a
        // band from the 9th tenth reads above the 8th cut point, the least figure over 2.0, and the 3rd tenth from below reads
        // at or under the 3rd cut point.
        var cuts = new Dictionary<string, double[]> { ["volume_ratio"] = [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.2, 2.0, 3.0], ["rsi"] = [20, 30, 40, 50, 55, 60, 65, 70, 80] };
        var line = Survivor("event breakout(500) · if volume_ratio above tenth 8 · if rsi below tenth 4 · buy next open · stop 2 typical days · take profit whole at 2 risks · sell after 63 sessions · cap 5 a night by strength highest");
        var settings = Survivors.Parameters(line, new Dictionary<string, double> { ["window"] = 3 }, cuts);

        Assert.Equal(3, settings["window"]);
        Assert.Equal(Math.BitIncrement(2.0), settings["also_volume_ratio_above"]);
        Assert.True(settings["also_volume_ratio_above"] > 2.0);
        Assert.Equal(40, settings["also_rsi_below"]);
        Assert.False(settings.ContainsKey("also_rsi_above"));
        Assert.Equal(1, settings["score_strength"]);
        Assert.Equal(PlanHook.ParametersOf(Survivors.PlanOf(line)).Count, settings.Keys.Count(key => PlanHook.ParametersOf(Survivors.PlanOf(line)).ContainsKey(key)));
        Assert.Equal(new PlanCombination(PlanPoints.NextOpen, 0, PlanPoints.TypicalStop(2), PlanPoints.TimeLimit).TakingAtRisk(2), PlanHook.Of(settings));

        // One record: three picks walked while it queued, +0.5, -1.0 and one still open, and two closed trades of its variant
        // at +0.25 each after it registered: four closed, (0.5 - 1.0 + 0.5) / 4 = 0.
        Assert.Equal((4, 0.0), Survivors.Record([0.5, -1.0, null], 2, 0.25));
        Assert.Equal((2, -0.25), Survivors.Record([0.5, -1.0, null], 0, null));
        Assert.Equal((0, (double?)null), Survivors.Record([], 0, null));

        // A queued pick walked by hand: the night's close 100, its typical move 1; bought at the next open of 101, a stop 2
        // under at 99, the whole sold at the next open after a close reaches 2 times the risk, 105: the close of 106 on the
        // third session sells at the fourth's open of 106, (106 - 101) / 2 = 2.5 risks. One still open reads none.
        Assert.Equal(2.5, Survivors.PickResult(line, 1, [(100, 100), (101, 102), (103, 104), (104, 106), (106, 106)]));
        Assert.Null(Survivors.PickResult(line, 1, [(100, 100), (101, 102)]));
        Assert.Null(Survivors.PickResult(Survivor("event breakout(500) · buy at the first zone within 5 sessions · stop 2 typical days · take profit none · sell after 63 sessions · cap 5 a night by strength highest"), 1, [(100, 100), (101, 102)]));
    }

    [Fact]
    public async Task AQueuedSurvivorIsWalkedEachNightItsPicksStoredAndItsStateWrittenOnceForAReason()
    {
        using var store = new TemporaryStore().Migrated();
        var line = Survivor(Runs.Replace(" · cap 5 a night by strength highest", string.Empty, StringComparison.Ordinal));
        var cuts = new Dictionary<string, double[]> { ["volume_ratio"] = [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.2, 2.0, 3.0] };

        store.Execute("INSERT INTO notebook (entered_at, source, index_code, family, words, line, verdict, why, tested_on, run_id) VALUES ('2026-11-20T00:00:00Z', 'the operator', 'GSPC', 'breakout', 'w', 'l', 'passed', 'y', '2026-11-20', 'r');");

        foreach (var (session, rows) in new[] { ("2026-11-23", new[] { ("AAA", 2.5, 1.0), ("BBB", 1.0, 2.0), ("CCC", 4.0, 3.0) }), ("2026-11-24", new[] { ("AAA", 2.6, 1.0) }) })
        {
            store.Execute($"INSERT INTO setup_night (index_code, family, session_date, members, setups, live_passes, source) VALUES ('GSPC', 'breakout', '{session}', 500, {rows.Length}, {rows.Length}, 'night');");

            foreach (var (ticker, volume, strength) in rows)
            {
                store.Execute($"INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin, volume_ratio, strength) VALUES ('GSPC', 'breakout', '{ticker}', '{session}', 'r', 1, '100', '95', 63, 'open', 0, 'night', 'p', {volume}, {strength});");
                store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('{ticker}', '{session}', '100', '100', '100', '100', 1000, 'eod', '{session}T22:00:00Z');");
                store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('{ticker}', '{session}', 'atr14', 1.0, 14);");
            }
        }

        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 23, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var desk = new SurvivorDesk(clock, store.DatabaseFile, store.Root);
        var admitted = await desk.AdmitAsync(1, line, cuts, new DateOnly(2026, 11, 20), "test-admit");

        Assert.Equal(Survivors.Queued, admitted.Outcome);
        Assert.StartsWith("Its setup lists at most a number a night in an order", admitted.Why, StringComparison.Ordinal);

        // The night of 24 Nov, the newest the ledger wrote: AAA's volume, 2.6 times, stands above the 8th cut point of 2.0.
        Assert.Equal("1 survivor(s) queued walked, 1 pick(s) stored among no buys, 0 registered", await desk.NightAsync("night-1"));
        Assert.Equal(["2026-11-24|AAA|100"], Query(store, "SELECT session_date || '|' || ticker || '|' || last_close FROM survivor_pick;"));

        // Run again, it stores nothing twice and writes no second state for the same reason; each walk writes the step's row.
        Assert.Equal("1 survivor(s) queued walked, 0 pick(s) stored among no buys, 0 registered", await desk.NightAsync("night-2"));
        Assert.Single(Query(store, "SELECT ticker FROM survivor_pick;"));
        Assert.Equal(["queued"], Query(store, "SELECT state FROM survivor_state;"));
        Assert.Equal(["night-1|1", "night-2|0"], Query(store, $"SELECT run_id || '|' || rows_written FROM run_log WHERE stage = '{SurvivorDesk.Stage}' ORDER BY rowid;"));

        // The survivor's tables take inserts alone.
        foreach (var write in new[] { "UPDATE survivor SET family = 'x';", "DELETE FROM survivor;", "UPDATE survivor_state SET state = 'registered';", "DELETE FROM survivor_state;", "UPDATE survivor_pick SET ticker = 'X';", "DELETE FROM survivor_pick;" })
        {
            Assert.Throws<SqliteException>(() => store.Execute(write));
        }
    }

    // No page, step or account draws a queued survivor's picks among any buys: the picks' table is read by the survivors' desk
    // and the Loop page's survivors alone.
    [Fact]
    public void NoQueuedSurvivorsPickIsDrawnAmongAnyBuys()
    {
        var readers = Repository.SourceFiles()
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}EquityBrief.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(file => File.ReadAllText(file).Contains("survivor_pick", StringComparison.Ordinal))
            .Select(file => Path.GetFileName(file))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["ReadApi.Survivors.cs", "SchemaMigrations.cs", "SurvivorDesk.cs"], readers);
    }
}
