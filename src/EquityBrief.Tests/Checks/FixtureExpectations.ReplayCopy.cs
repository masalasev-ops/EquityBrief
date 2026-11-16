using EquityBrief.Core.Candidates;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Replay;

namespace EquityBrief.Tests.Checks;

// On the operator's rulings of 2026-11-16 the new build's copy has each moved rule registered again at its own instant
// under the version its code carries, so the replay reads it as the old build's copy reads the rule it stood as; a store
// carrying no copy's mark is refused and keeps its register as it was.
// see: The replay compares the old build and the new over two copies of the store capped night by night, and a moved live rule keeps its record only where every trade is the same
public partial class FixtureExpectations
{
    [Fact]
    public async Task OnTheNewBuildsCopyEachMovedRuleStandsAtItsOwnInstantUnderTheVersionTheCodeCarries()
    {
        var threeAt = new DateTimeOffset(2026, 11, 16, 22, 0, 0, TimeSpan.Zero);
        var later = ReplayClock.At(threeAt.AddDays(2), SessionZones.UnitedStates);

        using var store = await FamilyStore(threeAt);

        store.Execute(
            "INSERT INTO candidate_register (id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence) " +
            "SELECT 4, 'a rule on a moved evaluator', rule, test, evaluator, parameters, 'moved000000', 'registered', NULL, '2026-11-16T23:00:00Z', NULL FROM candidate_register WHERE id = 1;");

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CandidateRegistrar(later, store.DatabaseFile).RegisterAsTheCodeIsOnACopyAsync());
        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        File.WriteAllText(Path.Combine(store.Root, ReplayCopy.CopyMark), "a replay's copy");

        Assert.Equal(1, await new CandidateRegistrar(later, store.DatabaseFile).RegisterAsTheCodeIsOnACopyAsync());

        var rows = await new CandidateRegistrar(later, store.DatabaseFile).RowsAsync();
        var standing = Assert.Single(CandidateFamily.Standing(rows, later.UtcNow), row => row.Candidate == "a rule on a moved evaluator");

        Assert.Equal(new DateTimeOffset(2026, 11, 16, 23, 0, 0, TimeSpan.Zero), standing.RegisteredAt);
        Assert.Equal(CandidateEvaluators.Find(standing.Evaluator)!.Version, standing.EvaluatorVersion);
        Assert.DoesNotContain("a rule on a moved evaluator", RegisterVerb.LiveRestarts(rows, [], later.UtcNow));
    }
}
