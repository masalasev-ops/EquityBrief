using EquityBrief.Core.Candidates;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 23.10's second part: an approval the operator withdrew undone at one instant. The live rule its change registered and the
// rule it replaced, kept in the background, are retired on the evidence, and the earlier rule is registered live again under
// its own name at its own parameters; a name that is no live rule, or a family keeping no rule in the background, is refused
// and nothing is written. Worked by hand over a migrated store.
// see: An approval the operator withdraws is drawn as withdrawn on their word and undone by the next restart's remedy
public partial class FixtureExpectations
{
    [Fact]
    public async Task AWithdrawnApprovalRetiresTheChangedLiveRuleAndItsBackgroundAndRegistersTheEarlierRuleLiveAgain()
    {
        using var store = new TemporaryStore().Migrated();
        var at = new DateTimeOffset(2026, 11, 10, 21, 43, 50, TimeSpan.Zero);
        var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile);
        var plain = new Dictionary<string, double>(StringComparer.Ordinal) { ["book"] = 10, ["floor"] = 7, ["sale"] = 0, ["trend"] = 0, ["valuation"] = 1 };
        var scored = new Dictionary<string, double>(plain, StringComparer.Ordinal) { ["score_close_over_long"] = 0.34 };
        const string Words = "S&P 500 quality rule at a score of 7 in 9, at or below its own median, the trend off, 10 held, sold at reshuffles";
        const string Changed = "the live " + Words + "; ordered by a score over close_over_long";

        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync("the background " + Words, "Quality", "the test", "quality-500", plain, "register-background")).Outcome);
        Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterAsync(Changed, "Quality", "the test", "quality-500", scored, "register-changed")).Outcome);

        // A name that is no live rule is refused and nothing is written.
        var later = new CandidateRegistrar(FixedClock.At(at.AddDays(4), SessionZones.UnitedStates), store.DatabaseFile);
        var before = Scalar(store, "SELECT COUNT(*) FROM candidate_register;");

        Assert.Equal(CandidateRegistrar.Refused, (await later.WithdrawAsync("the background " + Words, "the operator's word", "withdraw-refused")).Outcome);
        Assert.Equal(before, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // The withdrawal: both retired on the evidence and the earlier rule live again at its own parameters, three rows at one
        // instant.
        var outcome = await later.WithdrawAsync(Changed, "23.10's second part: the approval of 2026-11-09 withdrawn on the operator's word of 2026-11-13", "withdraw");

        Assert.Equal(CandidateRegistrar.Registered, outcome.Outcome);
        Assert.Equal(before + 3, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        var rows = await later.RowsAsync();
        var standing = CandidateFamily.Standing(rows, at.AddDays(5));

        Assert.Equal(["the live " + Words], standing.Select(row => row.Candidate));
        Assert.Equal(plain.OrderBy(pair => pair.Key), CandidateEvaluator.Read(standing.Single().Parameters).OrderBy(pair => pair.Key));
        Assert.Equal(2, rows.Count(row => row.Event == CandidateFamily.Retired && row.Evidence!.Contains("withdrawn on the operator's word", StringComparison.Ordinal)));

        // Run again, the changed rule no longer standing, it is refused and writes nothing.
        Assert.Equal(CandidateRegistrar.Refused, (await later.WithdrawAsync(Changed, "again", "withdraw-again")).Outcome);
        Assert.Equal(before + 3, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));
    }
}
