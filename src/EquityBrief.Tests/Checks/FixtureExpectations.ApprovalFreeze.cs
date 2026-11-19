using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Quality;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 24.2: inside the rule freeze, from 15 Nov 2026 until the monthly run of 6 Feb 2027, the apply step
// answers every approval waiting refused, over a register shaped as the operator's: the S&P 400's frozen breakout, the S&P
// 500's dip and its quality book. Nothing is registered and no setting is stored, on a night whose day falls inside the
// freeze and on one whose session alone does; on the first night after the freeze's last session the same changes register
// as before it.
// see: An approval inside the rule freeze is refused and replaces no live rule
public partial class FixtureExpectations
{
    // The words a refusal inside the freeze writes, as the operator's ruling of 2026-11-14 dates the freeze's end.
    const string FreezeRefusedWords = "the rules are frozen until the monthly run of 6 Feb 2027, so an approval changes no rule before it";

    [Fact]
    public async Task InsideTheRuleFreezeTheApplyStepRefusesEveryApprovalAndRegistersNothingAndAfterItsLastSessionItApplies()
    {
        using var store = new TemporaryStore().Migrated();
        var frozenAt = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);

        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt, [RegisterVerb.IndexFamily, BreakoutRule.Name, "--index", "MID", .. MidBreakoutFrozen])).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(1), RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", FamilyApprovalDip)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(2), RegisterVerb.IndexFamily, QualityRule.Name, "--index", "GSPC", "--parameters", QualityFrozen)).Code);

        var before = await StandingRowsAsync(store, frozenAt.AddMinutes(3));
        var dip = FamilyRecords.LiveOf(CandidateFamily.In(before, OwnStepCandidate.FamilyOn(DipRule.Name, "GSPC")))!;
        var quality = FamilyRecords.LiveOf(CandidateFamily.In(before, OwnStepCandidate.FamilyOn(QualityRule.Name, "GSPC")))!;
        var dipPlaces = LoopRegistrations.DipPlaces("GSPC", CandidateEvaluator.Read(dip.Parameters)).ToArray();
        var limitPlace = DipRules.Dials("GSPC").Select(dial => dial.Parameter).ToList().IndexOf(DipCandidate.LimitParameter);

        dipPlaces[limitPlace] = dipPlaces[limitPlace] == 0 ? 1 : dipPlaces[limitPlace] - 1;

        var dipChange = LoopChange.OfSetting(dipPlaces, LoopRegistrations.KeyOf("GSPC", dipPlaces), 0).Json;
        var qualityChange = new LoopChange(null, (QualityCandidate.SettingsOf(CandidateEvaluator.Read(quality.Parameters))! with { SellOnFiling = true }).Key, null, LoopChange.NoHooks).Json;

        // The three changes approved on a run of the S&P 400 and one of the S&P 500, each run's ids its own.
        void Approve(string stamp)
        {
            var mid = $"loop-test-MID-{stamp}";
            var large = $"loop-test-GSPC-{stamp}";

            LoopRun(store, mid, "MID", stamp[..4] + "-" + stamp[4..6]);
            LoopProposed(store, mid, "MID", BreakoutRule.Name, "the autopsy's exit, ranked 1", true, ExitChange(7));
            LoopDecided(store, mid, "MID", BreakoutRule.Name, "the autopsy's exit, ranked 1", LoopDecisions.Approved);
            LoopRun(store, large, "GSPC", stamp[..4] + "-" + stamp[4..6]);
            LoopProposed(store, large, "GSPC", DipRule.Name, OwnProcedures.SwingGrid, true, dipChange);
            LoopDecided(store, large, "GSPC", DipRule.Name, OwnProcedures.SwingGrid, LoopDecisions.Approved);
            LoopProposed(store, large, "GSPC", QualityRule.Name, ExitProcedures.ProposalName(1), true, qualityChange);
            LoopDecided(store, large, "GSPC", QualityRule.Name, ExitProcedures.ProposalName(1), LoopDecisions.Approved);
        }

        long Count(string sql) => Scalar(store, sql);

        // A registration is a row of the register's own event; a rule kept in the background before the freeze may still
        // retire on its 63rd session, which registers nothing.
        var registrations = $"SELECT COUNT(*) FROM candidate_register WHERE event = '{CandidateFamily.Registered}';";
        var registerRows = Count(registrations);

        // Inside the freeze by the day the step runs and by the night's session: Friday 20 Nov 2026's night.
        Approve("20261107T120000Z");
        LargeSession(store, "2026-11-20");

        var inside = await new LoopApply(At(2026, 11, 20, 23, 35), store.DatabaseFile, null).NightAsync("night-inside");

        Assert.Equal(
            [$"GSPC|{DipRule.Name}|refused|{FreezeRefusedWords}", $"GSPC|{QualityRule.Name}|refused|{FreezeRefusedWords}", $"MID|{BreakoutRule.Name}|refused|{FreezeRefusedWords}"],
            inside.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}|{one.Words}").Order(StringComparer.Ordinal));
        Assert.Equal(0, inside.Waiting);
        Assert.Null(inside.WroteFrom);
        Assert.Equal(registerRows, Count(registrations));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM provisional_setting;"));
        Assert.Equal(3, Count($"SELECT COUNT(*) FROM loop_applied WHERE outcome = '{LoopDecisions.Refused}' AND words = '{FreezeRefusedWords}';"));

        // Inside it by the night's session alone: the night of Friday 5 Feb 2027, the freeze's last session, run after
        // midnight UTC on 6 Feb, whose registration would count from 8 Feb.
        Approve("20270102T120000Z");
        LargeSession(store, "2027-02-05");

        var lastSession = await new LoopApply(At(2027, 2, 6, 0, 35), store.DatabaseFile, null).NightAsync("night-last-session");

        Assert.Equal(3, lastSession.Refused);
        Assert.All(lastSession.Applications, one => Assert.Equal(FreezeRefusedWords, one.Words));
        Assert.Equal(registerRows, Count(registrations));
        Assert.Equal(0, Count("SELECT COUNT(*) FROM provisional_setting;"));

        // After it: the night of Monday 8 Feb 2027 applies the same three changes approved at the run of 6 Feb.
        Approve("20270206T120000Z");
        LargeSession(store, "2027-02-08");

        var after = await new LoopApply(At(2027, 2, 8, 23, 35), store.DatabaseFile, null).NightAsync("night-after");

        Assert.Equal(
            [$"GSPC|{DipRule.Name}|applied", $"GSPC|{QualityRule.Name}|applied", $"MID|{BreakoutRule.Name}|applied"],
            after.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}").Order(StringComparer.Ordinal));
        Assert.True(Count(registrations) > registerRows);
    }

    // The shipped sources 24.2 writes outside their comments.
    // see: A comment or a blank line moves no pin, and every other change to a pinned source does
    static readonly string[] ApprovalFreezeSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Approval.cs",
        "src/EquityBrief.Core/Candidates/RuleFreezeDates.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Loop.cs",
        "src/EquityBrief.Worker/Loop/LoopApply.cs",
    ];

    // No source a rule's version pins is one 24.2 writes outside its comments, so no rule's version moves.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheRefusalOfAnApprovalInsideTheFreezeWrites()
    {
        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(ApprovalFreezeSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(ApprovalFreezeSources.Intersect(pinned, StringComparer.Ordinal));
    }
}
