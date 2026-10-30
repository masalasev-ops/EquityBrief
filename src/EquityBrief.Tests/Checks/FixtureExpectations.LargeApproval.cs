using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Returns;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 17.13: a change the operator approves to the S&P 500's breakout or drift written as its stored setting and registered
// as the family's live rule by the night's apply step alone, on a night whose session the registration counts from; the
// rule it replaced kept as a variant and retired on the night that reaches the block after the change and not the night
// before, and kept where a restore came first; from 21.5 a family at nine taking the change with the rule it replaces in
// the background, counted outside the nine, by the family's luck correction and on no row of the Run page; the page's
// list kept and ordered by the stored hooks; and a pick walked under the stored exit. Each is worked by hand over
// constructed stores, results and closes.
// see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
public partial class FixtureExpectations
{
    const string LargeRun = "loop-test-GSPC-20261009T120000Z";

    // The S&P 500's breakout family registered at its freeze, the live rule and its seven variants, before any night read.
    static async Task LargeBreakoutsAsync(TemporaryStore store) =>
        Assert.Equal(
            CandidateRegistrar.Registered,
            (await new CandidateRegistrar(At(2026, 9, 1, 12, 0), store.DatabaseFile).RegisterTogetherAsync(TheSetupFamilies.Breakouts, "register-breakouts")).Outcome);

    static FixedClock At(int year, int month, int day, int hour, int minute, int second = 0) =>
        new(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero), TimeZoneInfo.FindSystemTimeZoneById(SessionZones.UnitedStates));

    // A session the store holds, which is the night's for the apply step.
    static void LargeSession(TemporaryStore store, string session) =>
        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) " +
            $"VALUES ('AAA', '{session}', '10', '11', '9', '10.5', 100, 'test', '{session}T21:00:00Z', '10.5');");

    static async Task<IReadOnlyList<RegisterRow>> LargeFamilyAsync(TemporaryStore store, string family, FixedClock clock) =>
        CandidateFamily.In(CandidateFamily.Standing(await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync(), clock.UtcNow), family);

    [Fact]
    public async Task AnApprovalOfTheSAndP500sBreakoutIsRegisteredAsItsLiveRuleByTheNightOnASessionItCountsFromAndWaitsOtherwise()
    {
        using var store = new TemporaryStore().Migrated();

        await LargeBreakoutsAsync(store);
        Assert.Equal(8, TheSetupFamilies.Breakouts.Count);
        LargeSession(store, "2026-10-09");

        // The S&P 500's newest run holds a passing breakout exit, a target at 1.5 risks, and the S&P 600's a breakout exit;
        // the operator approves both.
        LoopRun(store, LargeRun, "GSPC", "2026-10");
        LoopProposed(store, LargeRun, "GSPC", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(14));
        LoopDecided(store, LargeRun, "GSPC", "breakout", "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        LoopRun(store, "loop-test-SML-20261009T120000Z", "SML", "2026-10");
        LoopProposed(store, "loop-test-SML-20261009T120000Z", "SML", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(7));
        LoopDecided(store, "loop-test-SML-20261009T120000Z", "SML", "breakout", "the autopsy's exit, ranked 1", LoopDecisions.Approved);

        // By hand the S&P 600's is applied and the S&P 500's waits for the night, which alone registers one.
        var byHand = await new LoopApply(At(2026, 10, 9, 23, 35), store.DatabaseFile, "on approval").RunAsync("by-hand");

        Assert.Equal(["SML|breakout|applied"], [.. byHand.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}")]);
        Assert.Equal(1, byHand.Waiting);
        Assert.Null(byHand.WroteFrom);

        // A night run after midnight UTC, whose registration would count from Monday's session and not the night's
        // Friday, leaves it waiting again and writes nothing for it.
        var late = await new LoopApply(At(2026, 10, 10, 0, 10), store.DatabaseFile, "on approval").NightAsync("night-late");

        Assert.Empty(late.Applications);
        Assert.Equal(1, late.Waiting);
        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM provisional_setting WHERE index_code = 'GSPC';"));
        Assert.Equal(8, (await LargeFamilyAsync(store, BreakoutRule.Name, At(2026, 10, 10, 0, 10))).Count);

        // The night of Friday 2026-10-09 applies it: its setting stored on the S&P 500 alone, the S&P 600's as it was, and
        // the change registered as the family's live rule, the family's own setting with the exit, the rule it replaced
        // standing beside it as a variant.
        var night = await new LoopApply(At(2026, 10, 9, 23, 35), store.DatabaseFile, "on approval").NightAsync("night-applied");

        Assert.Equal(["GSPC|breakout|applied"], [.. night.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}")]);
        Assert.Equal(0, night.Waiting);
        Assert.Equal(
            [$"GSPC|breakout|{ExitChange(14)}", $"SML|breakout|{ExitChange(7)}"],
            FamilyRows(store, "SELECT index_code, family, change FROM provisional_setting ORDER BY index_code;"));
        Assert.Equal(["applied|the family stands from tonight at exit: a target at 1.5 risks"], FamilyRows(store, "SELECT outcome, words FROM loop_applied WHERE index_code = 'GSPC';"));

        var family = await LargeFamilyAsync(store, BreakoutRule.Name, At(2026, 10, 9, 23, 35));
        var live = FamilyRecords.LiveOf(family)!;
        var replaced = TheSetupFamilies.Breakouts[0].Candidate;

        Assert.Equal(9, family.Count);
        Assert.Equal(replaced + "; exit: a target at 1.5 risks", live.Candidate);
        Assert.True(FamilyRecords.IsReplaced(replaced, family));
        Assert.False(FamilyRecords.IsReplaced(live.Candidate, family));
        Assert.Equal(14, RuleHooks.Of(CandidateEvaluator.Read(live.Parameters)).Exit);
        Assert.Equal(
            BreakoutCandidate.ParametersOf(BreakoutRule.Live).OrderBy(pair => pair.Key, StringComparer.Ordinal),
            RuleHooks.Without(CandidateEvaluator.Read(live.Parameters)).OrderBy(pair => pair.Key, StringComparer.Ordinal));
        Assert.Equal(new DateOnly(2026, 10, 9), FamilyRecords.FirstSession(live.RegisteredAt));

        // The night's swing step reads the register a second after the registration, so the change is evaluated from the
        // night it was applied; read at the night's start it would not be.
        var register = await new CandidateRegistrar(At(2026, 10, 9, 23, 35), store.DatabaseFile).RowsAsync();
        var nightStarted = new DateTimeOffset(2026, 10, 9, 23, 0, 0, TimeSpan.Zero);
        var readFrom = LoopApply.ReadFrom(register, nightStarted, night.WroteFrom);

        Assert.Equal(new DateTimeOffset(2026, 10, 9, 23, 35, 1, TimeSpan.Zero), readFrom);
        Assert.Contains(live.Candidate, FamilyRuleShadow.For(register, readFrom).Standing.Select(row => row.Candidate));
        Assert.Contains(replaced, FamilyRuleShadow.For(register, readFrom).Standing.Select(row => row.Candidate));
        Assert.DoesNotContain(live.Candidate, FamilyRuleShadow.For(register, nightStarted).Standing.Select(row => row.Candidate));
        Assert.Equal(nightStarted, LoopApply.ReadFrom(register, nightStarted, null));

        // A night run again answers nothing again.
        Assert.Empty((await new LoopApply(At(2026, 10, 9, 23, 50), store.DatabaseFile, "on approval").NightAsync("night-again")).Applications);
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM loop_applied WHERE index_code = 'GSPC';"));
    }

    [Fact]
    public async Task AnApprovalOfAnSAndP500FamilyHoldingNineRulesRegistersTheChangeWithTheRuleItReplacesInTheBackground()
    {
        using var store = new TemporaryStore().Migrated();

        await LargeBreakoutsAsync(store);
        LargeSession(store, "2026-10-09");
        LoopRun(store, LargeRun, "GSPC", "2026-10");
        LoopProposed(store, LargeRun, "GSPC", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(14));
        LoopDecided(store, LargeRun, "GSPC", "breakout", "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        Assert.Equal(1, (await new LoopApply(At(2026, 10, 9, 23, 35), store.DatabaseFile, null).NightAsync("night-first")).Applied);

        var first = FamilyRecords.LiveOf(await LargeFamilyAsync(store, BreakoutRule.Name, At(2026, 10, 9, 23, 40)))!;

        // A later run's second change to the breakout, approved while the family holds nine, the first change and the rule
        // it replaced among them: the change is registered as the family's live rule, and the first change, the rule it
        // replaces, is retired and registered again unchanged under the background's name at the same instant, so nine
        // rules stand drawn and the tenth runs in the background.
        const string later = "loop-test-GSPC-20261012T120000Z";

        LargeSession(store, "2026-10-12");
        LoopRun(store, later, "GSPC", "2026-10");
        LoopProposed(store, later, "GSPC", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(2));
        LoopDecided(store, later, "GSPC", "breakout", "the autopsy's exit, ranked 1", LoopDecisions.Approved);

        var applied = await new LoopApply(At(2026, 10, 12, 23, 35), store.DatabaseFile, null).NightAsync("night-second");

        Assert.Equal(["GSPC|breakout|applied"], [.. applied.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}")]);

        var family = await LargeFamilyAsync(store, BreakoutRule.Name, At(2026, 10, 12, 23, 40));
        var background = CandidateRegistrar.BackgroundOf(first.Candidate);
        var live = FamilyRecords.LiveOf(family)!;
        var kept = family.Single(row => FamilyRecords.IsBackground(row.Candidate));

        Assert.Equal(FamilyRecords.BackgroundPrefix + first.Candidate[FamilyRecords.LivePrefix.Length..], background);
        Assert.Equal((10, 9), (family.Count, family.Count(row => !FamilyRecords.IsBackground(row.Candidate))));
        Assert.Equal((background, first.Parameters, first.Evaluator), (kept.Candidate, kept.Parameters, kept.Evaluator));
        Assert.Equal(2, RuleHooks.Of(CandidateEvaluator.Read(live.Parameters)).Exit);
        Assert.Equal(TheSetupFamilies.Breakouts[0].Candidate + "; exit: a target at 1.5 risks", first.Candidate);
        Assert.Equal(TheSetupFamilies.Breakouts[0].Candidate + "; exit: the stop raised to the buy once a close is 1 risks up", live.Candidate);
        Assert.Contains(background, applied.Applications.Single().Words, StringComparison.Ordinal);

        // The luck correction counts the background rule while it runs: a window opened after the change divides the
        // family's level across the ten standing.
        var register = await new CandidateRegistrar(At(2026, 10, 13, 12, 0), store.DatabaseFile).RowsAsync();

        Assert.Equal(10, CandidateFamily.Divisor(CandidateFamily.In(register, BreakoutRule.Name), new DateTimeOffset(2026, 10, 13, 12, 0, 0, TimeSpan.Zero)));

        // Drawn on no page: the Run page's records hold no row of it, while the nine drawn are each read at five per cent
        // over the ten standing. It is told from the rule before a freeze by the live rule of its name the register retired
        // at the instant it was registered.
        var records = EquityBrief.Api.Reading.TonightScreen.FamilyRecordRows(await new EquityBrief.Api.Reading.ReadApi(store.DatabaseFile, At(2026, 10, 13, 12, 0)).RegisteredCandidatesAsync(), [], new DateOnly(2026, 10, 13));

        Assert.True(EquityBrief.Api.Reading.TonightScreen.MovedByAnApproval(background, register));
        Assert.False(EquityBrief.Api.Reading.TonightScreen.MovedByAnApproval(live.Candidate, register));
        Assert.DoesNotContain(background, records.Select(row => row.Rule));
        Assert.Equal(9, records.Count(row => row.Family == BreakoutRule.Name));
        Assert.All(records.Where(row => row.Family == BreakoutRule.Name), row => Assert.Equal(ReasonVerdict.Significance / 10, row.Level, 12));

        // A third change at nine moves the second to the background beside the first, the drawn rules still nine: the
        // background is counted outside the nine.
        const string third = "loop-test-GSPC-20261013T120000Z";

        LargeSession(store, "2026-10-13");
        LoopRun(store, third, "GSPC", "2026-10");
        LoopProposed(store, third, "GSPC", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(15));
        LoopDecided(store, third, "GSPC", "breakout", "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        Assert.Equal(1, (await new LoopApply(At(2026, 10, 13, 23, 35), store.DatabaseFile, null).NightAsync("night-third")).Applied);

        var three = await LargeFamilyAsync(store, BreakoutRule.Name, At(2026, 10, 13, 23, 40));

        Assert.Equal((11, 9), (three.Count, three.Count(row => !FamilyRecords.IsBackground(row.Candidate))));
        Assert.Equal(
            [background, CandidateRegistrar.BackgroundOf(live.Candidate)],
            three.Where(row => FamilyRecords.IsBackground(row.Candidate)).Select(row => row.Candidate).Order(StringComparer.Ordinal));

        // The background rule retires on the night whose session is the 63rd after the second change's session, counted
        // over the exchange's closures, which is one session after the first change's 63rd, and not on the night before;
        // its evidence names the change that moved it there.
        Assert.Equal((0, 1), (Blocks.Of(new DateOnly(2026, 10, 12), new DateOnly(2027, 1, 11)), Blocks.Of(new DateOnly(2026, 10, 12), new DateOnly(2027, 1, 12))));

        LargeSession(store, "2027-01-11");
        Assert.DoesNotContain(background, (await new LoopApply(At(2027, 1, 11, 23, 35), store.DatabaseFile, null).NightAsync("night-before")).Retired!);

        LargeSession(store, "2027-01-12");

        var reached = await new LoopApply(At(2027, 1, 12, 23, 35), store.DatabaseFile, null).NightAsync("night-reached");

        Assert.Contains(background, reached.Retired!);
        Assert.Contains(
            $"the live rule '{first.Candidate}' replaced by '",
            FamilyRows(store, $"SELECT evidence FROM candidate_register WHERE event = 'retired' AND retires = '{background.Replace("'", "''", StringComparison.Ordinal)}';").Single(),
            StringComparison.Ordinal);
        Assert.DoesNotContain(background, (await LargeFamilyAsync(store, BreakoutRule.Name, At(2027, 1, 12, 23, 40))).Select(row => row.Candidate));
    }

    [Fact]
    public async Task TheRuleAChangeReplacedIsRetiredOnTheNightReachingTheBlockAfterTheChangeAndKeptWhereARestoreCameFirst()
    {
        // From the change's first session, Friday 2026-10-09 as session 0, the 63rd session after it is Monday 2027-01-11,
        // counted by hand over the exchange's closures of Thanksgiving, Christmas and New Year's Day: October holds 16 from
        // the 9th, November 20, December 22 and January 4 through the 8th, 62, and the 11th is the 63rd.
        var before = new DateOnly(2027, 1, 8);
        var reached = new DateOnly(2027, 1, 11);
        var first = new DateOnly(2026, 10, 9);

        Assert.Equal(0, Blocks.Of(first, before));
        Assert.Equal(1, Blocks.Of(first, reached));

        // Without a restore: the night of the 8th retires nothing, and the night of the 11th retires the replaced rule
        // with its evidence, the change standing as the family's live rule.
        using (var store = new TemporaryStore().Migrated())
        {
            await AppliedLargeChangeAsync(store);
            LargeSession(store, "2027-01-08");

            var waiting = await new LoopApply(At(2027, 1, 8, 23, 35), store.DatabaseFile, null).NightAsync("night-before");

            Assert.Empty(waiting.Retired!);
            Assert.True(FamilyRecords.IsReplaced(TheSetupFamilies.Breakouts[0].Candidate, await LargeFamilyAsync(store, BreakoutRule.Name, At(2027, 1, 8, 23, 40))));

            LargeSession(store, "2027-01-11");

            var retiring = await new LoopApply(At(2027, 1, 11, 23, 35), store.DatabaseFile, null).NightAsync("night-reached");

            Assert.Equal([TheSetupFamilies.Breakouts[0].Candidate], retiring.Retired);
            Assert.NotNull(retiring.WroteFrom);

            var family = await LargeFamilyAsync(store, BreakoutRule.Name, At(2027, 1, 11, 23, 40));

            Assert.Equal(8, family.Count);
            Assert.DoesNotContain(TheSetupFamilies.Breakouts[0].Candidate, family.Select(row => row.Candidate));
            Assert.Equal(TheSetupFamilies.Breakouts[0].Candidate + "; exit: a target at 1.5 risks", FamilyRecords.LiveOf(family)!.Candidate);
            Assert.Equal(
                [$"replaced by '{TheSetupFamilies.Breakouts[0].Candidate}; exit: a target at 1.5 risks', whose record counts from 2026-10-09, and the 63 sessions it ran beside it have passed by 2027-01-11"],
                FamilyRows(store, "SELECT evidence FROM candidate_register WHERE event = 'retired';"));
        }

        // With a restore approved on Monday 2026-10-12: the rule the change replaced is registered again unchanged as the
        // family's live rule, so on the 11th it stands and the change, now the replaced rule, waits for the block after
        // the restore's first session.
        using (var store = new TemporaryStore().Migrated())
        {
            await AppliedLargeChangeAsync(store);
            LargeSession(store, "2026-10-12");
            LoopDecided(store, LoopDecisions.RestoreRun, "GSPC", "breakout", LoopDecisions.RestoreProposal(1), LoopDecisions.Approved);

            var restoring = await new LoopApply(At(2026, 10, 12, 23, 35), store.DatabaseFile, null).NightAsync("night-restore");

            Assert.Equal(["GSPC|breakout|applied"], [.. restoring.Applications.Select(one => $"{one.Index}|{one.Family}|{(one.Applied ? "applied" : "refused")}")]);
            Assert.Contains($"registered '{TheSetupFamilies.Breakouts[0].Candidate}' again unchanged as the family's live rule", restoring.Applications.Single().Words, StringComparison.Ordinal);

            var restored = await LargeFamilyAsync(store, BreakoutRule.Name, At(2026, 10, 12, 23, 40));

            Assert.Equal(9, restored.Count);
            Assert.Equal(TheSetupFamilies.Breakouts[0].Candidate, FamilyRecords.LiveOf(restored)!.Candidate);
            Assert.True(FamilyRecords.IsReplaced(TheSetupFamilies.Breakouts[0].Candidate + "; exit: a target at 1.5 risks", restored));
            Assert.Equal(["GSPC|breakout|{\"places\":null,\"key\":null,\"floor\":null,\"hooks\":{}}"], FamilyRows(store, "SELECT index_code, family, change FROM provisional_setting WHERE id = 2;"));

            LargeSession(store, "2027-01-11");
            Assert.Empty((await new LoopApply(At(2027, 1, 11, 23, 35), store.DatabaseFile, null).NightAsync("night-kept")).Retired!);
            Assert.Equal(9, (await LargeFamilyAsync(store, BreakoutRule.Name, At(2027, 1, 11, 23, 40))).Count);

            LargeSession(store, "2027-01-12");
            Assert.Equal(
                [TheSetupFamilies.Breakouts[0].Candidate + "; exit: a target at 1.5 risks"],
                (await new LoopApply(At(2027, 1, 12, 23, 35), store.DatabaseFile, null).NightAsync("night-change-retired")).Retired);
        }
    }

    // The breakout's change of a target at 1.5 risks approved and applied on the night of 2026-10-09.
    static async Task AppliedLargeChangeAsync(TemporaryStore store)
    {
        await LargeBreakoutsAsync(store);
        LargeSession(store, "2026-10-09");
        LoopRun(store, LargeRun, "GSPC", "2026-10");
        LoopProposed(store, LargeRun, "GSPC", "breakout", "the autopsy's exit, ranked 1", true, ExitChange(14));
        LoopDecided(store, LargeRun, "GSPC", "breakout", "the autopsy's exit, ranked 1", LoopDecisions.Approved);
        Assert.Equal(1, (await new LoopApply(At(2026, 10, 9, 23, 35), store.DatabaseFile, null).NightAsync("night-applied")).Applied);
    }

    [Fact]
    public void TheSAndP500sListIsKeptAndOrderedByTheHooksItsStoredSettingStatesAMemberAConditionRefusesNamed()
    {
        var rsi = LedgerReadings.All.Select((reading, at) => (reading, at)).First(one => one.reading.Column == "rsi").at;
        var volume = LedgerReadings.All.Select((reading, at) => (reading, at)).First(one => one.reading.Column == "volume_ratio").at;

        static FamilyResult Passing(string ticker, string family, double order) =>
            new(ticker, family, [new Gate(FamilyRule.Market, true, "open", FamilyRule.Values())], 100m, 95m, null, order, []);

        // Three breakouts passing in their own order by their volume, AAA first, and one drift.
        IReadOnlyList<FamilyResult> results =
        [
            Passing("AAA", BreakoutRule.Name, 3),
            Passing("BBB", BreakoutRule.Name, 2),
            Passing("CCC", BreakoutRule.Name, 1),
            Passing("DDD", DriftRule.Name, 1),
        ];
        var readings = new Dictionary<string, double?[]>(StringComparer.Ordinal)
        {
            ["AAA"] = Readings(rsi, 40, volume, 1.0),
            ["BBB"] = Readings(rsi, 55, volume, 1.5),
            ["CCC"] = Readings(rsi, 70, volume, 2.0),
            ["DDD"] = Readings(rsi, 10, volume, 1.0),
        };

        double?[] Readings(int one, double first, int two, double second)
        {
            var held = new double?[LedgerReadings.Count];

            held[one] = first;
            held[two] = second;

            return held;
        }

        IReadOnlyList<double?>? Of(string ticker) => readings.GetValueOrDefault(ticker);

        // No stored setting: every family answered and placed as its own rule answers it.
        var (own, ownPlaces) = FamilyEvaluator.Hooked(results, new Dictionary<string, RuleHooks>(StringComparer.Ordinal), Of);

        Assert.Equal(results, own);
        Assert.Equal([1, 2, 3, 1], [ownPlaces[("breakout", "AAA")], ownPlaces[("breakout", "BBB")], ownPlaces[("breakout", "CCC")], ownPlaces[("drift", "DDD")]]);

        // The breakout requiring an RSI of at least 50: AAA, at 40, fails on the hooks and is named so; BBB and CCC keep
        // their own order; the drift, stating no hook, stands as it was though its RSI is 10.
        var condition = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["also_rsi_above"] = 50 });
        var (kept, keptPlaces) = FamilyEvaluator.Hooked(results, new Dictionary<string, RuleHooks>(StringComparer.Ordinal) { [BreakoutRule.Name] = condition }, Of);

        Assert.False(kept[0].Passed);
        Assert.Equal([IndexNightRead.NotByItsHooks], kept[0].Exclusions);
        Assert.Equal([false, true, true, true], [.. kept.Select(result => result.Passed)]);
        Assert.Equal([1, 2, 1], [keptPlaces[("breakout", "BBB")], keptPlaces[("breakout", "CCC")], keptPlaces[("drift", "DDD")]]);
        Assert.False(keptPlaces.ContainsKey(("breakout", "AAA")));

        // A score of the volume ratio orders the breakouts by it, CCC first; a floor of 1.2 leaves AAA off.
        var score = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { ["score_volume_ratio"] = 1, [RuleHooks.ScoreFloorParameter] = 1.2 });
        var (scored, scoredPlaces) = FamilyEvaluator.Hooked(results, new Dictionary<string, RuleHooks>(StringComparer.Ordinal) { [BreakoutRule.Name] = score }, Of);

        Assert.Equal([2, 1], [scoredPlaces[("breakout", "BBB")], scoredPlaces[("breakout", "CCC")]]);
        Assert.Equal([IndexNightRead.NotByItsHooks], scored[0].Exclusions);

        // A night handed no readings leaves every breakout off where the hooks read them.
        var (none, nonePlaces) = FamilyEvaluator.Hooked(results, new Dictionary<string, RuleHooks>(StringComparer.Ordinal) { [BreakoutRule.Name] = condition }, null);

        Assert.Equal([false, false, false, true], [.. none.Select(result => result.Passed)]);
        Assert.Equal([("drift", "DDD")], [.. nonePlaces.Keys]);

        // An exit alone keeps the list as it stands.
        var exit = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExitParameter] = 14 });

        Assert.Equal(results, FamilyEvaluator.Hooked(results, new Dictionary<string, RuleHooks>(StringComparer.Ordinal) { [BreakoutRule.Name] = exit }, null).Results);

        // From 21.4 a setting stating the exposure reads the family with the market check off: three breakouts the check
        // closed, BBB failing its new high as well, and a drift stating no exposure. With the breadth at 0.5 over the
        // floor of 0.45 and the VIX at 1.3 of its median, one switch holds and two picks are allowed: AAA and CCC pass
        // with the exposure's answer in the check's place, BBB stays off its own rule and the drift closed as it was.
        static FamilyResult ClosedOn(string ticker, string family, double order, bool newHigh = true) =>
            new(ticker, family, [new Gate(FamilyRule.Market, false, "closed", FamilyRule.Values()), new Gate(BreakoutRule.NewHigh, newHigh, "the high", FamilyRule.Values())], 100m, 95m, null, order, []);

        IReadOnlyList<FamilyResult> closed = [ClosedOn("AAA", BreakoutRule.Name, 3), ClosedOn("BBB", BreakoutRule.Name, 2, newHigh: false), ClosedOn("CCC", BreakoutRule.Name, 1), ClosedOn("DDD", DriftRule.Name, 1)];
        var exposure = new Dictionary<string, RuleHooks>(StringComparer.Ordinal) { [BreakoutRule.Name] = RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExposureParameter] = 0.45 }) };
        var (exposed, exposedPlaces) = FamilyEvaluator.Hooked(closed, exposure, _ => Readings(RuleHooks.BreadthColumn, 0.5, RuleHooks.VixOverMedianColumn, 1.3));

        Assert.True(FamilyRule.FailsOnTheMarketAlone(closed[0]));
        Assert.False(FamilyRule.FailsOnTheMarketAlone(closed[1]));
        Assert.Equal([true, false, true, false], [.. exposed.Select(result => result.Passed)]);
        Assert.Equal([1, 2], [exposedPlaces[("breakout", "AAA")], exposedPlaces[("breakout", "CCC")]]);
        Assert.Equal(FamilyRule.Market, exposed[0].Gates[0].Name);
        Assert.Contains("the exposure allows 2 pick(s) tonight", exposed[0].Gates[0].Reason, StringComparison.Ordinal);
        Assert.Empty(exposed[1].Exclusions);
        Assert.False(exposedPlaces.ContainsKey(("breakout", "BBB")));

        // With both switches holding five are allowed and both still pass; with neither none is, and the two the rule
        // would have listed are left off by their hooks, the check standing closed on them.
        Assert.Equal(2, FamilyEvaluator.Hooked(closed, exposure, _ => Readings(RuleHooks.BreadthColumn, 0.5, RuleHooks.VixOverMedianColumn, 0.9)).Places.Count(pair => pair.Key.Family == "breakout"));

        var (shut, shutPlaces) = FamilyEvaluator.Hooked(closed, exposure, _ => Readings(RuleHooks.BreadthColumn, 0.3, RuleHooks.VixOverMedianColumn, 1.3));

        Assert.DoesNotContain(shutPlaces.Keys, key => key.Family == "breakout");
        Assert.Equal([IndexNightRead.NotByItsHooks], shut[0].Exclusions);
        Assert.False(shut[0].Passed);
    }

    [Fact]
    public void AnSAndP500PickIsWalkedUnderTheExitItsStoredSettingNames()
    {
        static IReadOnlyList<ReturnBar> After(params decimal[] closes) =>
            [.. closes.Select((close, at) => new ReturnBar(new DateOnly(2026, 10, 12).AddDays(at), close))];

        // A drift's plan bought at 100 with its stop at 95 and its target at 120, the risk 5. Under a target at 1.5 risks,
        // 107.50, the close of 108 on the third session after sells it: 1.6 risks, 8 per cent, a win on 2026-10-14.
        var target = ForwardReturnFiller.Menu(After(101m, 104m, 108m, 90m), 100m, 95m, 120m, 2, DriftRule.Horizon, 20, ExitMenu.Of(14)!)!;

        Assert.Equal((ForwardReturnSeries.Win, new DateOnly(2026, 10, 14)), (target.Outcome, target.ResolvedOn));
        Assert.Equal(8.0, target.ReturnPct!.Value, 9);

        // Its own target of 120 is not reached before the close of 90 under its stop: under the plan's own exit it would be a
        // loss of 2 risks on the fourth session; under the stop raised to the buy once a close is a risk up, 105, the
        // close of 106 raises it, and the close of 99 sells it at -0.2 risks, -1 per cent, under its raised stop.
        var breakEven = ForwardReturnFiller.Menu(After(106m, 99m, 90m), 100m, 95m, 120m, 2, DriftRule.Horizon, 20, ExitMenu.Of(2)!)!;

        Assert.Equal((ForwardReturnSeries.Loss, new DateOnly(2026, 10, 13)), (breakEven.Outcome, breakEven.ResolvedOn));
        Assert.Equal(-1.0, breakEven.ReturnPct!.Value, 9);

        // A breakout's trailing plan, its trail the buy less its stop, sold at the close ten sessions in that is not above
        // its buy: unresolved, as a trade its cap ended is.
        var time = ForwardReturnFiller.Menu(After(101m, 102m, 101m, 100m, 99m, 100m, 101m, 100m, 99m, 99.5m, 130m), 100m, 95m, null, 2, BreakoutRule.Horizon, 63, ExitMenu.Of(19)!)!;

        Assert.Equal((ForwardReturnSeries.Unresolved, new DateOnly(2026, 10, 21)), (time.Outcome, time.ResolvedOn));
        Assert.Equal(-0.5, time.ReturnPct!.Value, 9);

        // Fewer sessions than it needs leave it open; a trail of the menu with no typical move cannot be walked.
        Assert.Null(ForwardReturnFiller.Menu(After(101m), 100m, 95m, 120m, 2, DriftRule.Horizon, 20, ExitMenu.Of(14)!)!.Outcome);
        Assert.Null(ForwardReturnFiller.Menu(After(101m, 102m), 100m, 95m, null, null, BreakoutRule.Horizon, 63, ExitMenu.Of(6)!));
    }
}
