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
