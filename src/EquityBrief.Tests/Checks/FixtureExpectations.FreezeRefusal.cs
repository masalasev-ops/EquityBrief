using EquityBrief.Core.Candidates;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;

namespace EquityBrief.Tests.Checks;

// On the operator's rulings of 2026-11-15 `register` refuses to restart a live rule's record during the freeze, so a remedy's
// `--moved` passes the freeze check: inside the freeze a moved live rule no replay reproduces refuses the command whole, and
// outside it, or where the replay reproduces every trade, the command registers as it did.
// see: A live rule's record never restarts inside the rule freeze, so register refuses a moved live rule its replay does not reproduce
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
