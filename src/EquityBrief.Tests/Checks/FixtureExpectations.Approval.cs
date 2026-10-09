using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 17.9: the operator's word applied before the next night on the index it was given on alone, the automatic setting
// shipped off, the tester walking each rule at the setting it stands at, and the live alarm. Each is worked over
// constructed stores and constructed units, the alarm's low by a reference whose every unit holds one edge so every
// draw's mean is that edge.
// see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
// see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
// see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
public partial class FixtureExpectations
{
    static readonly FixedClock ApprovalClock = FixedClock.At(new DateTimeOffset(2026, 10, 9, 23, 35, 0, TimeSpan.Zero), SessionZones.UnitedStates);

    static string ExitChange(int exit) => LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExitParameter] = exit }).Json;

    static void LoopRun(TemporaryStore store, string run, string index, string month) =>
        store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{run}', '{month}', '{index}', '2026-10-08', '2026-10-09T12:00:00Z', '2026-10-09T12:01:00Z', 5);");

    static void LoopProposed(TemporaryStore store, string run, string index, string family, string proposal, bool passed, string? change, int blocks = 19, double adjusted = 0.001) =>
        store.Execute(
            "INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change) "
            + FormattableString.Invariant($"VALUES ('{run}', '{index}', '{family}', '{proposal.Replace("'", "''", StringComparison.Ordinal)}', 'the change', 'the rule today', 'risks', 400, {blocks}, {adjusted}, 1, 1, 4, 3, 1.5, 1, 0.1, 3, {(passed ? 1 : 0)}, NULL, {(change is null ? "NULL" : $"'{change}'")});"));

    static void LoopDecided(TemporaryStore store, string run, string index, string family, string proposal, string decision, string? reason = null) =>
        store.Execute($"INSERT INTO loop_decision (run_id, index_code, family, proposal, decision, reason, decided_at) VALUES ('{run}', '{index}', '{family}', '{proposal.Replace("'", "''", StringComparison.Ordinal)}', '{decision}', {(reason is null ? "NULL" : $"'{reason}'")}, '2026-10-09T15:00:00Z');");

    [Fact]
    public async Task TheAdoptSettingShipsAsOnApprovalAndTheApplyStepRefusesAProposalHoldingNoDecisionUnlessItReadsAutomatic()
    {
        // The shipped settings read on approval, and the setting reads it where none is stated and refuses any other word.
        var shipped = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "appsettings.json"), optional: false)
            .Build();

        Assert.Equal("on approval", shipped[LoopDecisions.AdoptSetting]);
        Assert.Equal(LoopDecisions.OnApproval, LoopDecisions.AdoptOf(shipped[LoopDecisions.AdoptSetting]));
        Assert.Equal(LoopDecisions.OnApproval, LoopDecisions.AdoptOf(null));
        Assert.Equal(LoopDecisions.Automatic, LoopDecisions.AdoptOf("automatic"));
        Assert.Throws<ArgumentException>(() => LoopDecisions.AdoptOf("sometimes"));

        // The S&P 600's newest run holds two breakout exits that passed, the stronger the second by its adjusted p-value,
        // and no decision.
        using var store = new TemporaryStore().Migrated();

        LoopRun(store, "loop-test-SML-20261009T120000Z", "SML", "2026-10");
        LoopProposed(store, "loop-test-SML-20261009T120000Z", "SML", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(7), adjusted: 0.003);
        LoopProposed(store, "loop-test-SML-20261009T120000Z", "SML", "breakout", "the autopsy's exit, ranked 2", true, ExitChange(8), adjusted: 0.001);

        // On approval, a proposal holding no decision is not applied, and nothing is written but the run log's row.
        var waiting = await new LoopApply(ApprovalClock, store.DatabaseFile, "on approval").RunAsync("night-on-approval");

        Assert.Empty(waiting.Applications);
        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM provisional_setting;"));
        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM loop_applied;"));

        // On automatic, the stronger of the family's two is applied and the other is not, one change a family a run.
        var applied = await new LoopApply(ApprovalClock, store.DatabaseFile, "automatic").RunAsync("night-automatic");

        Assert.Equal(["the autopsy's exit, ranked 2|applied"], [.. applied.Applications.Select(one => $"{one.Proposal}|{(one.Applied ? "applied" : "refused")}")]);
        Assert.Equal([$"SML|breakout|{ExitChange(8)}"], FamilyRows(store, "SELECT index_code, family, change FROM provisional_setting;"));

        // A second run answers nothing again.
        Assert.Empty((await new LoopApply(ApprovalClock, store.DatabaseFile, "automatic").RunAsync("night-again")).Applications);
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM loop_applied;"));
    }

    [Fact]
    public async Task AnApprovalOnTheSAndP600IsAppliedThereAloneAndADeclineChangesNothing()
    {
        using var store = new TemporaryStore().Migrated();

        // Each index's newest run holds a passing breakout exit and drift exit. The operator approves the S&P 600's
        // breakout, declines the S&P 400's drift and approves the S&P 500's drift.
        foreach (var index in new[] { "GSPC", "MID", "SML" })
        {
            var run = $"loop-test-{index}-20261009T120000Z";

            LoopRun(store, run, index, "2026-10");
            LoopProposed(store, run, index, "breakout", "the autopsy's exit, ranked 1", true, ExitChange(7));
            LoopProposed(store, run, index, "drift", "the autopsy's exit, ranked 1", true, ExitChange(8));
        }

        LoopDecided(store, "loop-test-SML-20261009T120000Z", "SML", "breakout", "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        LoopDecided(store, "loop-test-MID-20261009T120000Z", "MID", "drift", "the autopsy's exit, ranked 1", LoopDecisions.Declined, "too few years");
        LoopDecided(store, "loop-test-GSPC-20261009T120000Z", "GSPC", "drift", "the autopsy's exit, ranked 1", LoopDecisions.Approved);

        var outcome = await new LoopApply(ApprovalClock, store.DatabaseFile, "on approval").RunAsync("night-applied");

        // The S&P 600's breakout is applied; the S&P 500's drift is refused with why; the decline is answered by nothing.
        Assert.Equal(
            ["GSPC|drift|refused", "SML|breakout|applied"],
            [.. outcome.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}").Order(StringComparer.Ordinal)]);
        Assert.Equal([LoopDecisions.LargeIndexRefused], FamilyRows(store, "SELECT words FROM loop_applied WHERE index_code = 'GSPC';"));
        Assert.Equal(["SML|breakout"], FamilyRows(store, "SELECT index_code, family FROM provisional_setting;"));

        // The S&P 600's night reads its breakout at the change and states it among its settings; the S&P 400's and the
        // S&P 500's read none.
        await using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={store.DatabaseFile}");
        await connection.OpenAsync();

        var small = await IndexFamilies.StoredSettingsAsync(connection, "SML", CancellationToken.None);
        var mid = await IndexFamilies.StoredSettingsAsync(connection, "MID", CancellationToken.None);

        Assert.Equal(["breakout"], [.. small.Keys]);
        Assert.Equal(7, RuleHooks.Of(small["breakout"].Hooks).Exit);
        Assert.Empty(mid);
        Assert.Contains("\"approved\":{\"breakout\":\"exit: ", IndexFamilies.Settings("SML", null, small), StringComparison.Ordinal);
        Assert.Contains("\"approved\":{}", IndexFamilies.Settings("MID", null, mid), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheSAndP400sNightReadsTheBreakoutAtTheSettingAnApprovalStoredForItAndNoOtherIndexsSetting()
    {
        using var store = IndexStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        decimal Stop() => decimal.Parse(FamilyRows(store, "SELECT stop FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND ticker = 'IA';").Single(), CultureInfo.InvariantCulture);

        // At its provisional setting the breakout's stop sits 1.5 typical moves under IA's buy of 102.
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-own");

        var own = Stop();

        // The same setting with the stop at 3 moves, stored for the S&P 600, leaves the S&P 400's night as it was.
        int[] wider = IndexNightRead.Places(EquityBrief.Worker.Sweep.BreakoutSweep.Grid, [126, 1.5, 0.85, 3]);
        var change = LoopChange.OfSetting(wider, EquityBrief.Worker.Sweep.BreakoutSweep.Grid.Key(wider), 0).Json;

        store.Execute($"INSERT INTO provisional_setting (index_code, family, change, words, set_at, run_id, proposal) VALUES ('SML', 'breakout', '{change}', 'the wider stop', '2026-10-02T15:00:00Z', 'run', 'the grid');");
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-small");

        Assert.Equal(own, Stop());

        // Stored for the S&P 400, it moves IA's stop twice as far under its buy, and the night's settings state it.
        store.Execute($"INSERT INTO provisional_setting (index_code, family, change, words, set_at, run_id, proposal) VALUES ('MID', 'breakout', '{change}', 'the wider stop', '2026-10-02T15:00:00Z', 'run', 'the grid');");
        await new IndexFamilies(clock, store.DatabaseFile).RunAsync("night-mid");

        Assert.InRange((102m - Stop()) / (102m - own), 1.99m, 2.01m);
        Assert.Contains(
            "\"approved\":{\"breakout\":\"the setting high 126, volume 1.5, ceiling 0.85, stop 3\"}",
            FamilyRows(store, "SELECT settings FROM index_family_night WHERE index_code = 'MID';").Single(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheApplyStepAnswersEachChangeAsItsIndexAndFamilyAllowAndARestoreWritesTheSettingBeforeTheChange()
    {
        var exit = ExitChange(7);
        var rsi = LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_above"] = 50 }).Json;

        // Refused: an S&P 500 rule, a book, a family no approval reaches, a proposal stating no change and the pullback's
        // grid setting.
        Assert.Equal((false, LoopDecisions.LargeIndexRefused), Words(LoopApply.Answer("run", "GSPC", "drift", "p", exit, [])));
        Assert.Equal((false, LoopDecisions.BookRefused), Words(LoopApply.Answer("run", "SML", "heavyweight", "p", exit, [])));
        Assert.Equal((false, LoopDecisions.FamilyRefused), Words(LoopApply.Answer("run", "SML", "fundamentals", "p", exit, [])));
        Assert.Equal((false, LoopApply.NoChangeRefused), Words(LoopApply.Answer("run", "SML", "breakout", "p", null, [])));
        Assert.False(LoopApply.Answer("run", "SML", "pullback", "p", LoopChange.OfSetting([1, 1], "a=1|b=2", 0).Json, []).Applied);

        // A change written on top of the setting the family stands at: its exit beside the condition standing.
        var (applied, _, written) = LoopApply.Answer("run", "SML", "breakout", "p", exit, [(1, rsi)]);

        Assert.True(applied);
        Assert.Equal(7, RuleHooks.Of(written!.Hooks).Exit);
        Assert.Equal(["also_rsi_above", "exit"], written.Hooks.Keys.Order(StringComparer.Ordinal));

        // A restore of change 2 writes change 1's setting again; of change 1, the family's own setting.
        IReadOnlyList<(long, string)> standing = [(1, rsi), (2, LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_above"] = 50, ["exit"] = 7 }).Json)];
        var (_, _, before) = LoopApply.Answer(LoopDecisions.RestoreRun, "SML", "breakout", LoopDecisions.RestoreProposal(2), null, standing);
        var (_, _, own) = LoopApply.Answer(LoopDecisions.RestoreRun, "SML", "breakout", LoopDecisions.RestoreProposal(1), null, standing);

        Assert.Equal(["also_rsi_above"], before!.Hooks.Keys);
        Assert.Empty(own!.Hooks);
        Assert.False(LoopApply.Answer(LoopDecisions.RestoreRun, "SML", "breakout", LoopDecisions.RestoreProposal(9), null, standing).Applied);

        static (bool, string) Words((bool Applied, string Words, LoopChange? Written) answer) => (answer.Applied, answer.Words);
    }

    [Fact]
    public void ADeclinedProposalIsPutAgainOnlyOverMoreBlocksAndAChangeOnTopOfAnotherReplacesItsOwnPartsAlone()
    {
        Assert.False(LoopDecisions.PutAgain(19, 19, passedNow: true));
        Assert.True(LoopDecisions.PutAgain(19, 20, passedNow: true));
        Assert.False(LoopDecisions.PutAgain(19, 20, passedNow: false));

        Assert.True(LoopDecisions.Applies(LoopDecisions.OnApproval, LoopDecisions.Approved, passed: false));
        Assert.False(LoopDecisions.Applies(LoopDecisions.OnApproval, null, passed: true));
        Assert.True(LoopDecisions.Applies(LoopDecisions.Automatic, null, passed: true));
        Assert.False(LoopDecisions.Applies(LoopDecisions.Automatic, LoopDecisions.Declined, passed: true));

        // A condition on the same reading and side replaces the standing one, one on the other side stands beside it, an
        // exit replaces the exit, and a score replaces the whole score with its floor.
        var standing = new LoopChange(null, null, null, new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_above"] = 50, ["exit"] = 7, ["score_rsi"] = 1, ["score_floor"] = 0.5 });

        static IReadOnlyDictionary<string, double> Hooks(params (string Name, double Value)[] hooks) => hooks.ToDictionary(one => one.Name, one => one.Value, StringComparer.Ordinal);

        Assert.Equal(60, LoopChange.OfHooks(Hooks(("also_rsi_above", 60))).OnTopOf(standing).Hooks["also_rsi_above"]);
        Assert.Equal(["also_rsi_above", "also_rsi_below", "exit", "score_floor", "score_rsi"], LoopChange.OfHooks(Hooks(("also_rsi_below", 70))).OnTopOf(standing).Hooks.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(9, LoopChange.OfHooks(Hooks(("exit", 9))).OnTopOf(standing).Hooks["exit"]);
        Assert.Equal(["also_rsi_above", "exit", "score_depth"], LoopChange.OfHooks(Hooks(("score_depth", 2))).OnTopOf(standing).Hooks.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheAlarmsRuleOfTwoPeriodsRunningIsWorkedByHandOnBothSidesAtEachPeriodsOwnCount()
    {
        // A reference whose forty units each hold an edge of a half, so every draw's mean is a half and the low is a half at
        // every count.
        IReadOnlyList<AlarmUnit> level = [.. Enumerable.Range(0, 40).Select(at => new AlarmUnit(new DateOnly(2023, 1, 2).AddDays(at * 7), 0.5))];

        Assert.Equal(0.5, LiveAlarm.Low(level, 5));
        Assert.Equal(0.5, LiveAlarm.Low(level, 37));

        static (DateOnly, IReadOnlyList<double>) Month(int month, params double[] edges) => (new DateOnly(2026, month, 1), edges);

        var read = LiveAlarm.Read(
            [
                Month(1, 0.4, 0.4, 0.4, 0.4, 0.4, 0.4),
                Month(2, 0, 0, 0, 0),
                Month(3, 0.45, 0.45, 0.45, 0.45, 0.45),
                Month(4, 0.5, 0.5, 0.5, 0.5, 0.5),
                Month(5, 0.4, 0.4, 0.4, 0.4, 0.4),
            ],
            level);

        // January six units under the low, the run one; February four, neither counting nor breaking the run; March five
        // under it, the run two and the rule flagged; April at the low, not under it, the run broken; May under it again,
        // the run one.
        Assert.Equal(
            [
                "2026-01-01|6|True|True|1|False",
                "2026-02-01|4|False|False|1|False",
                "2026-03-01|5|True|True|2|True",
                "2026-04-01|5|True|False|0|False",
                "2026-05-01|5|True|True|1|False",
            ],
            [.. read.Select(period => FormattableString.Invariant($"{period.Start:yyyy-MM-dd}|{period.Units}|{period.Counted}|{period.Under}|{period.Streak}|{period.Flagged}"))]);

        // A run carried on from the period read before: one under the low after a run of one flags the rule.
        Assert.True(LiveAlarm.Read([Month(6, 0.1, 0.1, 0.1, 0.1, 0.1)], level, streak: 1)[0].Flagged);

        // Over a reference whose units spread, the low at five units sits further under the mean than at fifty, and a
        // period is read against the low at its own count.
        IReadOnlyList<AlarmUnit> spread = [.. Enumerable.Range(0, 60).Select(at => new AlarmUnit(new DateOnly(2023, 1, 2).AddDays(at * 7), at % 2 == 0 ? 1.0 : -1.0))];

        Assert.True(LiveAlarm.Low(spread, 5) < LiveAlarm.Low(spread, 50));
        Assert.Equal(LiveAlarm.Low(spread, 5), LiveAlarm.Read([Month(1, 0, 0, 0, 0, 0)], spread)[0].Low);
        Assert.Equal(LiveAlarm.Low(spread, 5), LiveAlarm.Low(spread, 5));

        // A swing family reads by month and a book by quarter.
        Assert.Equal(new DateOnly(2026, 5, 1), LiveAlarm.PeriodOf(new DateOnly(2026, 5, 17), quarterly: false));
        Assert.Equal(new DateOnly(2026, 4, 1), LiveAlarm.PeriodOf(new DateOnly(2026, 5, 17), quarterly: true));
    }

    [Fact]
    public async Task TheLiveAlarmWritesEachSettledPeriodOnceAndFlagsARuleTwoCountedMonthsUnderItsLow()
    {
        using var store = new TemporaryStore().Migrated();

        // The newest session 2026-10-02; the S&P 400's newest run's breakout reference, thirty units each a half.
        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('IA', '2026-10-02', '10', '10', '10', '10', 1, 'test', '2026-10-02T21:00:00Z', '10');");
        LoopRun(store, "loop-test-MID-20261009T120000Z", "MID", "2026-10");
        store.Execute(
            "INSERT INTO loop_reference (run_id, index_code, family, place, entered, edge) VALUES "
            + string.Join(", ", Enumerable.Range(1, 30).Select(at => FormattableString.Invariant($"('loop-test-MID-20261009T120000Z', 'MID', 'breakout', {at}, '{new DateOnly(2023, 1, 2).AddDays(at * 7):yyyy-MM-dd}', 0.5)"))) + ";");

        // Five page trades ended in July at 0.3 less a cost of 0.1, five in August at 0.2 with none, each against a
        // benchmark of nothing; in September five settled and one whose benchmark is not yet written; and one in October.
        void Trade(string ticker, string listed, string ended, double result, double cost, bool settled) =>
            store.Execute(
                "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, ended_on, result, cost, benchmark, members) "
                + FormattableString.Invariant($"VALUES ('MID', 'breakout', '{ticker}', '{listed}', 1, '100', '97', NULL, '3', 63, '{ended}', {result}, {cost}, {(settled ? "0" : "NULL")}, {(settled ? "7" : "NULL")});"));

        for (var at = 0; at < 5; at++)
        {
            Trade($"J{at}", "2026-06-01", FormattableString.Invariant($"2026-07-{10 + at}"), 0.3, 0.1, true);
            Trade($"A{at}", "2026-07-01", FormattableString.Invariant($"2026-08-{10 + at}"), 0.2, 0, true);
            Trade($"S{at}", "2026-08-01", FormattableString.Invariant($"2026-09-{10 + at}"), 0.9, 0, true);
        }

        Trade("SX", "2026-08-02", "2026-09-20", 0.9, 0, false);
        Trade("OX", "2026-09-01", "2026-10-01", 0.9, 0, true);

        var first = await new LiveAlarmReader(ApprovalClock, store.DatabaseFile).RunAsync("night-alarm");

        // July and August each at 0.2 under the low of a half, the rule flagged on August; September waits on its unsettled
        // trade and October has not closed; the S&P 500 and the 600 hold no run, so no reference.
        Assert.Equal(
            ["2026-07-01|5|0.2|0.5|1|1|1|0", "2026-08-01|5|0.2|0.5|1|1|2|1"],
            FamilyRows(store, "SELECT period, trades, round(edge, 9), edge_floor, counted, under, streak, flagged FROM loop_alarm WHERE index_code = 'MID' AND family = 'breakout' ORDER BY period;"));
        Assert.Equal(1, first.Flagged);
        Assert.Equal(2, first.Reads.Count(read => read.Why == LiveAlarmReader.NoRun));

        // Once the September trade's benchmark is written, September is read at the low and over it, and the run breaks; a
        // later night writes nothing twice.
        store.Execute("UPDATE index_family_trade SET benchmark = 0, members = 7 WHERE ticker = 'SX';");
        await new LiveAlarmReader(ApprovalClock, store.DatabaseFile).RunAsync("night-alarm-2");
        await new LiveAlarmReader(ApprovalClock, store.DatabaseFile).RunAsync("night-alarm-3");

        Assert.Equal(
            ["2026-07-01|1|0", "2026-08-01|2|1", "2026-09-01|0|0"],
            FamilyRows(store, "SELECT period, streak, flagged FROM loop_alarm WHERE index_code = 'MID' AND family = 'breakout' ORDER BY period;"));
        Assert.Equal(["6"], FamilyRows(store, "SELECT trades FROM loop_alarm WHERE period = '2026-09-01';"));
    }

    [Fact]
    public void ARuleStandingAtApprovedHooksIsWalkedAtThemAndAProposalsHooksAreSetOnTopAsAnApprovalSetsThem()
    {
        var calls = new List<(ExitChoice? Exit, Func<int, bool>? Keep, Func<IReadOnlyList<int>, IReadOnlyList<int>>? Arrange)>();

        IReadOnlyList<ExitProcedures.Walked> Walk(ExitChoice? exit, Func<int, bool>? keep, Func<IReadOnlyList<int>, IReadOnlyList<int>>? arrange)
        {
            calls.Add((exit, keep, arrange));

            return [];
        }

        // Three listings whose relative strength index reads 40, 55 and 70.
        var rsi = Enumerable.Range(0, LedgerReadings.All.Count).First(at => LedgerReadings.All[at].Column == "rsi");

        IReadOnlyList<double?> Readings(int at)
        {
            var held = new double?[LedgerReadings.All.Count];

            held[rsi] = at switch { 0 => 40, 1 => 55, _ => 70 };

            return held;
        }

        IReadOnlyList<int> Night(int call) => calls[call].Arrange!([0, 1, 2]);

        // A rule standing at exit 7 and a relative strength index of at least 50.
        var standing = new Dictionary<string, double>(StringComparer.Ordinal) { ["exit"] = 7, ["also_rsi_above"] = 50 };
        var rule = new RuleWalk("breakout", "the breakout as approved", [], 63, [], Walk, standing, Readings);

        _ = rule.Own;
        Assert.Equal(7, calls[^1].Exit!.Number);
        Assert.Equal([1, 2], Night(calls.Count - 1));

        // The autopsy's exit 3 in place of 7, the condition kept.
        _ = rule.Under(ExitMenu.Of(3)!);
        Assert.Equal(3, calls[^1].Exit!.Number);
        Assert.Equal([1, 2], Night(calls.Count - 1));

        // A condition on the same reading and side replaces the standing one; on the other side it stands beside it.
        _ = rule.Hooked(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_above"] = 60 }, Readings);
        Assert.Equal(7, calls[^1].Exit!.Number);
        Assert.Equal([2], Night(calls.Count - 1));
        _ = rule.Hooked(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_below"] = 60 }, Readings);
        Assert.Equal([1], Night(calls.Count - 1));

        // A score orders what the standing condition keeps, highest first.
        _ = rule.Hooked(new Dictionary<string, double>(StringComparer.Ordinal) { ["score_rsi"] = 1 }, Readings);
        Assert.Equal([2, 1], Night(calls.Count - 1));

        // A filter keeps beside the standing hooks.
        _ = rule.Keeping(at => at != 2);
        Assert.False(calls[^1].Keep!(2));
        Assert.Equal(7, calls[^1].Exit!.Number);

        // A rule at its family's own setting is walked as it always was, under its own exit with no arrangement; and one
        // standing at hooks that read the readings refuses a walk handed none.
        _ = new RuleWalk("breakout", "the breakout", [], 63, [], Walk).Own;
        Assert.Null(calls[^1].Exit);
        Assert.Null(calls[^1].Arrange);
        Assert.Throws<InvalidOperationException>(() => new RuleWalk("breakout", "the breakout as approved", [], 63, [], Walk, standing).Own);
    }
}
