using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Quality;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.5: an approval of a change to a family whose live rule stands registered on its index, over a
// register shaped as the operator's: the S&P 400's frozen breakout, the S&P 500's dip and its quality book. Each change is
// registered by the night's apply step as the family's live rule again at the change, its words the family's own setting's
// and its hooks', the rule it replaces kept beside it and retired on the night reaching the block after; a frozen swing
// rule takes its hooks alone, and a frozen heavyweights book its menu's sale under the 200-day average alone.
// see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
// see: The engines test a frozen rule at the levels it states, and a frozen rule's setting moves only by a freeze
public partial class FixtureExpectations
{
    const string FamilyApprovalRun = "loop-test-MID-20261022T120000Z";

    const string FamilyApprovalLargeRun = "loop-test-GSPC-20261022T120000Z";

    const string FamilyApprovalDip = "drop=-3,trend=50,sell=65,limit=10,stop=3,market=6,volume=0";

    static string QualityFrozen => "floor=7,valuation=1,trend=0,book=10,sale=0";

    static async Task<IReadOnlyList<RegisterRow>> StandingRowsAsync(TemporaryStore store, DateTimeOffset at) =>
        CandidateFamily.Standing(await new CandidateRegistrar(Core.Time.FixedClock.At(at, Core.Time.SessionZones.UnitedStates), store.DatabaseFile).RowsAsync(), at);

    [Fact]
    public async Task AnApprovalRegistersTheFamilysLiveRuleAgainAtTheChangeAndKeepsTheRuleItReplacesForABlock()
    {
        using var store = new TemporaryStore().Migrated();
        var frozenAt = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);

        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt, [RegisterVerb.IndexFamily, BreakoutRule.Name, "--index", "MID", .. MidBreakoutFrozen])).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(1), RegisterVerb.IndexFamily, DipRule.Name, "--index", "GSPC", "--parameters", FamilyApprovalDip)).Code);
        Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(2), RegisterVerb.IndexFamily, QualityRule.Name, "--index", "GSPC", "--parameters", QualityFrozen)).Code);

        var before = await StandingRowsAsync(store, frozenAt.AddMinutes(3));
        var breakout = FamilyRecords.LiveOf(CandidateFamily.In(before, IndexRuleCandidate.FamilyOn(BreakoutRule.Name, "MID")))!;
        var dip = FamilyRecords.LiveOf(CandidateFamily.In(before, OwnStepCandidate.FamilyOn(DipRule.Name, "GSPC")))!;
        var quality = FamilyRecords.LiveOf(CandidateFamily.In(before, OwnStepCandidate.FamilyOn(QualityRule.Name, "GSPC")))!;

        // The S&P 400's breakout: an exit of the menu approved, and a setting on its grid approved in a later run, which
        // its freeze alone moves. The S&P 500's dip: its setting one place along on its grid, its time limit the next
        // level. Its quality book: sold at the close of a night a filing drops its score below the floor.
        var dipPlaces = LoopRegistrations.DipPlaces("GSPC", CandidateEvaluator.Read(dip.Parameters)).ToArray();
        var limitPlace = DipRules.Dials("GSPC").Select(dial => dial.Parameter).ToList().IndexOf(DipCandidate.LimitParameter);

        dipPlaces[limitPlace] = dipPlaces[limitPlace] == 0 ? 1 : dipPlaces[limitPlace] - 1;

        var dipChange = LoopChange.OfSetting(dipPlaces, LoopRegistrations.KeyOf("GSPC", dipPlaces), 0).Json;
        var onFiling = QualityCandidate.SettingsOf(CandidateEvaluator.Read(quality.Parameters))! with { SellOnFiling = true };
        var qualityChange = new LoopChange(null, onFiling.Key, null, LoopChange.NoHooks).Json;

        LargeSession(store, "2026-10-22");
        LoopRun(store, FamilyApprovalRun, "MID", "2026-10");
        LoopProposed(store, FamilyApprovalRun, "MID", BreakoutRule.Name, "the autopsy's exit, ranked 1", true, ExitChange(7));
        LoopDecided(store, FamilyApprovalRun, "MID", BreakoutRule.Name, "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        LoopRun(store, FamilyApprovalLargeRun, "GSPC", "2026-10");
        LoopProposed(store, FamilyApprovalLargeRun, "GSPC", DipRule.Name, OwnProcedures.SwingGrid, true, dipChange);
        LoopDecided(store, FamilyApprovalLargeRun, "GSPC", DipRule.Name, OwnProcedures.SwingGrid, LoopDecisions.Approved);
        LoopProposed(store, FamilyApprovalLargeRun, "GSPC", QualityRule.Name, ExitProcedures.ProposalName(1), true, qualityChange);
        LoopDecided(store, FamilyApprovalLargeRun, "GSPC", QualityRule.Name, ExitProcedures.ProposalName(1), LoopDecisions.Approved);

        var night = await new LoopApply(At(2026, 10, 22, 23, 35), store.DatabaseFile, null).NightAsync("night-applied");

        Assert.Equal(
            [$"GSPC|{DipRule.Name}|applied", $"GSPC|{QualityRule.Name}|applied", $"MID|{BreakoutRule.Name}|applied"],
            night.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}").Order(StringComparer.Ordinal));
        Assert.NotNull(night.WroteFrom);

        var after = await StandingRowsAsync(store, new DateTimeOffset(2026, 10, 22, 23, 40, 0, TimeSpan.Zero));

        RegisterRow LiveOn(string family) => FamilyRecords.LiveOf(CandidateFamily.In(after, family))!;

        // The breakout's live rule registered again with the exit on top of its own setting, its words the freeze's with
        // the exit's; its own parameters as they stood, the rule it replaces standing beside it.
        var newBreakout = LiveOn(IndexRuleCandidate.FamilyOn(BreakoutRule.Name, "MID"));

        Assert.Equal(breakout.Candidate + "; exit: " + ExitMenu.Of(7)!.Words, newBreakout.Candidate);
        Assert.Equal(7, RuleHooks.Of(CandidateEvaluator.Read(newBreakout.Parameters)).Exit);
        Assert.Equal(
            CandidateEvaluator.Read(breakout.Parameters).OrderBy(pair => pair.Key, StringComparer.Ordinal),
            RuleHooks.Without(CandidateEvaluator.Read(newBreakout.Parameters)).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.True(FamilyRecords.IsReplaced(breakout.Candidate, CandidateFamily.In(after, IndexRuleCandidate.FamilyOn(BreakoutRule.Name, "MID"))));

        // The dip at the next level of its time limit, read back by its own evaluator, and the quality book selling on a
        // filing, each named by its setting's own words.
        var newDip = LiveOn(OwnStepCandidate.FamilyOn(DipRule.Name, "GSPC"));
        var dipAt = LoopRegistrations.DipParameters("GSPC", dipPlaces)!;

        Assert.Equal(dipAt.OrderBy(pair => pair.Key, StringComparer.Ordinal), CandidateEvaluator.Read(newDip.Parameters).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.NotEqual(CandidateEvaluator.Read(dip.Parameters)[DipCandidate.LimitParameter], dipAt[DipCandidate.LimitParameter]);
        Assert.Equal(FamilyRecords.LivePrefix + OwnStepCandidate.NameOf("GSPC") + " dip rule at " + DipRules.Short(((DipCandidate)CandidateEvaluators.Find(newDip.Evaluator)!).SettingOf(dipAt), "GSPC"), newDip.Candidate);

        var newQuality = LiveOn(OwnStepCandidate.FamilyOn(QualityRule.Name, "GSPC"));

        Assert.Equal(onFiling, QualityCandidate.SettingsOf(CandidateEvaluator.Read(newQuality.Parameters)));
        Assert.Equal(FamilyRecords.LivePrefix + OwnStepCandidate.NameOf("GSPC") + " quality rule at " + QualityRules.Short(onFiling), newQuality.Candidate);

        // A later run's setting on the breakout's grid is refused: a frozen rule's setting moves only by a freeze.
        const string later = "loop-test-MID-20261023T120000Z";

        LargeSession(store, "2026-10-23");
        LoopRun(store, later, "MID", "2026-10");
        LoopProposed(store, later, "MID", BreakoutRule.Name, LoopProcedures.Grid, true, LoopChange.OfSetting([1, 1, 1, 1], "high=126|volume=1.5|ceiling=0.85|stop=3", 0).Json);
        LoopDecided(store, later, "MID", BreakoutRule.Name, LoopProcedures.Grid, LoopDecisions.Approved);

        var refused = await new LoopApply(At(2026, 10, 23, 23, 35), store.DatabaseFile, null).NightAsync("night-refused");

        Assert.Equal([$"MID|{BreakoutRule.Name}|{LoopDecisions.FrozenSettingRefused}"], refused.Applications.Select(one => $"{one.Index}|{one.Family}|{one.Words}"));

        // The rule each change replaced retires on the night whose session is the 63rd after the change's first session,
        // counted over the exchange's closures, and not the night before; the change came one session after the freeze,
        // so its 63rd session is the one after the freeze's.
        Assert.Equal((0, 1), (Blocks.Of(new DateOnly(2026, 10, 22), new DateOnly(2027, 1, 22)), Blocks.Of(new DateOnly(2026, 10, 22), new DateOnly(2027, 1, 25))));

        LargeSession(store, "2027-01-22");

        var waiting = await new LoopApply(At(2027, 1, 22, 23, 35), store.DatabaseFile, null).NightAsync("night-before");

        Assert.DoesNotContain(breakout.Candidate, waiting.Retired!);
        Assert.DoesNotContain(dip.Candidate, waiting.Retired!);

        LargeSession(store, "2027-01-25");

        var reached = await new LoopApply(At(2027, 1, 25, 23, 35), store.DatabaseFile, null).NightAsync("night-reached");

        Assert.Contains(breakout.Candidate, reached.Retired!);
        Assert.Contains(dip.Candidate, reached.Retired!);
        Assert.Contains(quality.Candidate, reached.Retired!);

        // The three retired on one night each hold a row of their own under the night's run, named by the rule.
        Assert.Equal(
            new[] { breakout.Candidate, dip.Candidate, quality.Candidate }.Select(one => CandidateRegistrar.Stage + ": " + one).Order(StringComparer.Ordinal),
            FamilyRows(store, "SELECT stage FROM run_log WHERE run_id = 'night-reached' AND outcome = 'retired' ORDER BY stage;"));
    }
}
