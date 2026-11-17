using EquityBrief.Core.Candidates;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;

namespace EquityBrief.Tests.Checks;

// On the operator's rulings of 2026-11-15 `register` refuses to restart a live rule's record during the freeze, so a remedy's
// `--moved` passes the freeze check: inside the freeze a moved live rule no replay reproduces refuses the command whole, and
// outside it, or where the replay reproduces every trade, the command registers as it did.
// see: A live rule's record never restarts inside the rule freeze, so register refuses a moved live rule its replay does not reproduce unless the operator accepted its change in writing
public partial class FixtureExpectations
{
    [Fact]
    public async Task InsideTheFreezeAMovedLiveRuleNoReplayReproducesRefusesTheMovedCommandWhole()
    {
        var threeAt = new DateTimeOffset(2026, 11, 16, 22, 0, 0, TimeSpan.Zero);

        using var store = await FamilyStore(threeAt);

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 4, 'the live rule on a moved evaluator', rule, test, evaluator, parameters, 'moved000000', 'registered', NULL, '2026-11-16T23:00:00Z', NULL FROM candidate_register WHERE id = 1;");

        var (refused, refusedSaid) = await RegisterVerbAt(store, threeAt.AddDays(2), RegisterVerb.Moved, "--evidence", "a pinned source moved");

        Assert.Equal(1, refused);
        Assert.Contains("the rules are frozen until 6 Feb 2027", refusedSaid, StringComparison.Ordinal);
        Assert.Contains("the replay did not reproduce every trade of the live rule on a moved evaluator, so nothing was registered", refusedSaid, StringComparison.Ordinal);
        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // The day the freeze ends the same command registers it again, its record restarting as before the freeze.
        var (after, afterSaid) = await RegisterVerbAt(store, new DateTimeOffset(2027, 2, 6, 15, 0, 0, TimeSpan.Zero), RegisterVerb.Moved, "--evidence", "a pinned source moved");

        Assert.True(after == 0, afterSaid);
        Assert.Equal(6, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }

    // From the replay fix `--moved` reads the record the replay over two copies kept: one the store has moved past is refused,
    // one naming the live rule among those whose rows differ is refused inside the freeze, and one reproducing it registers,
    // each rule's replay row written from the record.
    // see: The replay compares the old build and the new over two copies of the store capped night by night, and a moved live rule keeps its record only where every trade is the same
    [Fact]
    public async Task MovedReadsTheReplaysRecordAndRegistersOnlyWhereItReproducesEveryLiveRule()
    {
        var threeAt = new DateTimeOffset(2026, 11, 16, 22, 0, 0, TimeSpan.Zero);

        using var store = await FamilyStore(threeAt);

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 4, 'the live rule on a moved evaluator', rule, test, evaluator, parameters, 'moved000000', 'registered', NULL, '2026-11-16T23:00:00Z', NULL FROM candidate_register WHERE id = 1;");

        var newest = EquityBrief.Worker.Nights.NightSession.NewestStored(store.DatabaseFile)!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var file = Path.Combine(store.Root, "replay-record.txt");

        async Task<(int Code, string Said)> MovedWith(string record, int days)
        {
            await File.WriteAllTextAsync(file, record);

            return await RegisterVerbAt(store, threeAt.AddDays(days), RegisterVerb.Moved, "--evidence", "a pinned source moved", RegisterVerb.Replayed, file);
        }

        var stale = await MovedWith(EquityBrief.Worker.Replay.ReplayCompare.Header("2020-01-02", 4) + "\n", 2);

        Assert.Equal(1, stale.Code);
        Assert.Contains("run the replay again", stale.Said, StringComparison.Ordinal);

        var differs = await MovedWith(
            EquityBrief.Worker.Replay.ReplayCompare.Header(newest, 4) + "\n"
            + EquityBrief.Worker.Replay.ReplayCompare.DiffersOpening + "family_trade\tthe live rule on a moved evaluator\t1 row(s) old, 0 new; only the old build's: ticker=AAA\n", 3);

        Assert.Equal(1, differs.Code);
        Assert.Contains("the replay did not reproduce every trade of the live rule on a moved evaluator", differs.Said, StringComparison.Ordinal);
        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        var same = await MovedWith(EquityBrief.Worker.Replay.ReplayCompare.Header(newest, 4) + "\nreplay-compare: 12 of 12 rule and book table(s) the same\n", 4);

        Assert.True(same.Code == 0, same.Said);
        Assert.Equal(6, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.True(Scalar(store, $"SELECT COUNT(*) FROM run_log WHERE outcome = '{EquityBrief.Core.Candidates.FamilyRecords.ReplayReproduced}' AND detail LIKE '%{EquityBrief.Worker.Families.FamilyReplay.ReproducedOnTheCopies}%';") >= 1);
    }

    // On the operator's answer of 2026-11-17 an acceptance they gave in writing lets the live S&P 400 and 600 heavyweights rules
    // keep their records over a replay that differs in their own books and fills alone, every other moved rule reproducing
    // exactly; a difference anywhere else, another moved rule differing, an acceptance no one gave, or none named refuses.
    // see: A live rule's record never restarts inside the rule freeze, so register refuses a moved live rule its replay does not reproduce unless the operator accepted its change in writing
    [Fact]
    public async Task AnAcceptanceKeepsTheNamedLiveRulesRecordsOnlyWhereTheirDifferenceLiesInTheirOwnBooksAndFills()
    {
        var threeAt = new DateTimeOffset(2026, 11, 16, 22, 0, 0, TimeSpan.Zero);
        const string Mid = "the live S&P 400 heavyweights rule under test";
        const string Small = "the live S&P 600 heavyweights rule under test";

        using var store = await FamilyStore(threeAt);

        string Provisional(string evaluator) => CandidateEvaluator.Write(EquityBrief.Worker.Indices.IndexRules.Provisional(HeavyweightIndexEvaluator(evaluator)));

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) VALUES "
            + $"(4, '{Mid}', 'a rule', 'a test', 'heavyweight-400', '{Provisional("heavyweight-400")}', 'moved000000', 'registered', NULL, '2026-11-16T23:00:00Z', NULL), "
            + $"(5, '{Small}', 'a rule', 'a test', 'heavyweight-600', '{Provisional("heavyweight-600")}', 'moved000000', 'registered', NULL, '2026-11-16T23:00:00Z', NULL);");

        var newest = EquityBrief.Worker.Nights.NightSession.NewestStored(store.DatabaseFile)!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        var file = Path.Combine(store.Root, "replay-record.txt");
        var opening = EquityBrief.Worker.Replay.ReplayCompare.DiffersOpening;
        var ownBooks = opening + $"index_heavyweight_rule_holding\t{Mid}\t9 row(s) old, 9 new\n"
            + opening + $"index_heavyweight_rule_holding\t{Small}\t6 row(s) old, 6 new\n"
            + opening + $"trade_fill\t{Small} | SML\t6 row(s) old, 6 new\n";

        async Task<(int Code, string Said)> MovedWith(string differences, long register, int days, params string[] accepting)
        {
            await File.WriteAllTextAsync(file, EquityBrief.Worker.Replay.ReplayCompare.Header(newest, register) + "\n" + differences);

            return await RegisterVerbAt(store, threeAt.AddDays(days), [RegisterVerb.Moved, "--evidence", "a pinned source moved", RegisterVerb.Replayed, file, .. accepting]);
        }

        // With no acceptance named the freeze refuses the two as before.
        var none = await MovedWith(ownBooks, 5, 2);

        Assert.Equal(1, none.Code);
        Assert.Contains("the rules are frozen until 6 Feb 2027", none.Said, StringComparison.Ordinal);

        // An acceptance no one gave is refused.
        var unknown = await MovedWith(ownBooks, 5, 3, ReplayAcceptances.Option, "an acceptance never given");

        Assert.Equal(1, unknown.Code);
        Assert.Contains("no acceptance is named 'an acceptance never given'", unknown.Said, StringComparison.Ordinal);

        // A difference beyond a rule's own book and fills is refused, naming the rule and the table.
        var beyond = await MovedWith(ownBooks + opening + $"index_rule_trade\t{Mid}\t3 row(s) old, 3 new\n", 5, 4, ReplayAcceptances.Option, "heavyweights-rule-exits");

        Assert.Equal(1, beyond.Code);
        Assert.Contains($"{Mid} differing in index_rule_trade, so nothing was registered", beyond.Said, StringComparison.Ordinal);
        Assert.Equal(5, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // The two differing in their own books and fills alone are registered again, the ruling on the register row and on
        // each accepted rule's run log row.
        var accepted = await MovedWith(ownBooks, 5, 5, ReplayAcceptances.Option, "heavyweights-rule-exits");

        Assert.True(accepted.Code == 0, accepted.Said);
        Assert.Equal(9, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
        Assert.Equal(2, Scalar(store, $"SELECT COUNT(*) FROM candidate_register WHERE id > 5 AND event = '{CandidateFamily.Retired}' AND evidence LIKE '%accepted under ''heavyweights-rule-exits''%This is my written acceptance%';"));
        Assert.Equal(2, Scalar(store, $"SELECT COUNT(*) FROM run_log WHERE detail LIKE '%{RegisterVerb.AcceptedWords.Replace("'", "''", StringComparison.Ordinal)}%';"));

        // Another moved rule the replay does not reproduce refuses the whole command, accepted or not.
        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 10, 'the live rule on a moved evaluator', rule, test, evaluator, parameters, 'moved000000', 'registered', NULL, '2026-11-21T23:00:00Z', NULL FROM candidate_register WHERE id = 1;");

        var other = await MovedWith(opening + "family_trade\tthe live rule on a moved evaluator\t1 row(s) old, 0 new\n", 10, 6, ReplayAcceptances.Option, "heavyweights-rule-exits");

        Assert.Equal(1, other.Code);
        Assert.Contains("every other moved rule must reproduce exactly, and the replay did not reproduce the live rule on a moved evaluator", other.Said, StringComparison.Ordinal);
        Assert.Equal(10, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }

    [Fact]
    public void InsideTheFreezeOnlyAMovedLiveRuleNoReplayReproducesIsNamed()
    {
        var at = new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero);
        var registered = new DateTimeOffset(2026, 11, 16, 0, 0, 0, TimeSpan.Zero);
        var evaluator = CandidateEvaluators.All.OfType<FamilyRuleEvaluator>().First();

        RegisterRow Row(long id, string candidate, string version) =>
            new(id, candidate, "a rule", "a test", evaluator.Name, "{}", version, CandidateFamily.Registered, null, registered, null);

        RegisterRow[] rows =
        [
            Row(1, "the live rule moved", "moved000000"),
            Row(2, "a version moved", "moved000000"),
            Row(3, "the live rule unmoved", evaluator.Version),
        ];

        Assert.Equal(["the live rule moved"], RegisterVerb.LiveRestarts(rows[..2], [], at));
        Assert.Empty(RegisterVerb.LiveRestarts(rows[..2], [new FamilyReplayed("the live rule moved", evaluator.Version, new DateOnly(2026, 11, 16), 3, 2, 2, true, "reproduced")], at));
        Assert.Equal(["the live rule moved"], RegisterVerb.LiveRestarts(rows[..2], [new FamilyReplayed("the live rule moved", evaluator.Version, new DateOnly(2026, 11, 16), 3, 2, 1, false, "differs")], at));
        Assert.Empty(RegisterVerb.LiveRestarts([rows[1], rows[2]], [], at));
    }
}
