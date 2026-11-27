using EquityBrief.Core.Candidates;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: a background variant registers beside its live rule where its family has room, retiring nothing; where
// the family holds nine a variant moving the floor alone is entered to wait, any other refused; each night a waiting variant is
// walked from its live rule's own orders under its floor, worked by hand on both sides of the floor, and registers in the first
// slot that frees; its tables take inserts alone.
// see: A background variant a full family has no room for is walked each night from its live rule's own orders and registers in the first slot that frees
public partial class FixtureExpectations
{
    // The night's step that registers or walks the waiting variants, as section 14 states it.
    internal const string WaitingVariantStep =
        "From the 24.19 correction of 2026-11-27, register each background variant waiting for a slot where its family holds fewer than nine, the " +
        "first entered first and retiring nothing, and walk every other one from its live rule's own orders the families wrote earlier in the night, " +
        "each walked again with its floor under its level and written once it has ended; arithmetic over the night's stored rows, calling no model and " +
        "asking nothing of a network (see: A background variant a full family has no room for is walked each night from its live rule's own orders and " +
        "registers in the first slot that frees).";

    // The correction's rows: the step's catalogue and matrix rows, its two stores, the night's step and the Run page's region.
    internal static string[] WaitingVariantRows =>
        [.. ComponentAccess.WaitingVariantRows, .. SchemaColumns.WaitingVariantStoreRows, CheckReach.Key(NightlyRunSteps.Heading, WaitingVariantStep), .. Reading.ReadSurface.WaitingVariantRunRows];

    static readonly Dictionary<string, double> FloorAtTwo = new(StringComparer.Ordinal) { [PlanHook.RangeFloorParameter] = 2, [PlanHook.RangeCeilingParameter] = 0 };

    // An order at the second zone's price of 100 with a typical day of 2, stopped 2 typical days under the buy and taking the
    // whole at 110.
    static readonly PlanOrder AtAHundred = new("the second zone", PlanPoints.SecondZone, 100, 3, PlanPoints.TypicalStop(2), 90, null, 2, 110, [], null, null, 20);

    static PlanBars Bars(params (string Session, double Open, double Low, double Close)[] bars) =>
        new([.. bars.Select(bar => DateOnly.Parse(bar.Session, System.Globalization.CultureInfo.InvariantCulture))], [.. bars.Select(bar => bar.Open)], [.. bars.Select(bar => bar.Low)], [.. bars.Select(bar => bar.Close)]);

    [Fact]
    public void AWaitingVariantsOrderIsTheLiveOrderWithItsFloorAndBuysNothingOnAnOpenUnderIt()
    {
        // By hand: the floor sits 2 typical days of 2 under the price of 100, at 96.
        Assert.Equal(96, WaitingVariants.Floored(AtAHundred, 2).LevelFloor);
        Assert.Null(WaitingVariants.Floored(AtAHundred with { Level = 3 }, 2).LevelFloor);
        Assert.Null(WaitingVariants.Floored(AtAHundred with { Range = new BuyRange(105, 95) }, 2).LevelFloor);
        Assert.Null(WaitingVariants.Floored(AtAHundred with { Level = null }, 2).LevelFloor);

        // Opening at 95, under the floor: the live rule buys at the open of 95 and the variant buys nothing, its order ended on
        // its window's last session, and the floor alone kept it out.
        var under = WaitingVariants.EndOf(AtAHundred, 2, 102m, 90m, new DateOnly(2026, 11, 11), Bars(("2026-11-11", 102, 101, 102), ("2026-11-12", 95, 94, 97), ("2026-11-13", 97, 96, 111), ("2026-11-16", 112, 111, 112)));

        Assert.NotNull(under);
        Assert.Null(under.Value.End.FilledOn);
        Assert.Equal(PlanEnd.Unfilled, under.Value.End.End);
        Assert.True(under.Value.UnderFloor);

        // Opening at 97, above the floor: both buy at 97 and end alike.
        var over = WaitingVariants.EndOf(AtAHundred, 2, 102m, 90m, new DateOnly(2026, 11, 11), Bars(("2026-11-11", 102, 101, 102), ("2026-11-12", 97, 94, 97), ("2026-11-13", 97, 96, 111), ("2026-11-16", 112, 111, 112)));

        Assert.NotNull(over);
        Assert.Equal(97, over.Value.End.Fill);
        Assert.False(over.Value.UnderFloor);
        Assert.Equal(over.Value.LiveResult, over.Value.End.Result);
        Assert.NotNull(over.Value.End.Result);

        // A move other than the floor alone is no waiting variant.
        Assert.True(WaitingVariants.FloorAlone(FloorAtTwo));
        Assert.False(WaitingVariants.FloorAlone(new Dictionary<string, double> { [RuleHooks.RewardTradedParameter] = 1.5 }));
        Assert.False(WaitingVariants.FloorAlone(new Dictionary<string, double> { [PlanHook.RangeFloorParameter] = 2, [PlanHook.RangeCeilingParameter] = 0.5 }));
    }

    static string FloorVariantName => CandidateRegistrar.VariantOf(PlanLive, CandidateRegistrar.BackgroundWords(FloorAtTwo));

    [Fact]
    public async Task ABackgroundVariantRegistersWhereItsFamilyHasRoomAndWaitsWhereItHoldsNine()
    {
        var (roomy, at) = await PlanRegisterAsync(0);

        using (roomy)
        {
            var registrar = new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), roomy.DatabaseFile);

            Assert.Equal(CandidateRegistrar.Registered, (await registrar.RegisterPlannedAsync(PlanLive, SecondZoneWide, true, "23.10's table", "plan-given")).Outcome);

            var later = new CandidateRegistrar(FixedClock.At(at.AddHours(1), SessionZones.UnitedStates), roomy.DatabaseFile);
            var before = await later.RowsAsync();
            var given = await later.RegisterBackgroundAsync(PlanLive, FloorAtTwo, "the operator's answers of 2026-11-27", "background-room");

            Assert.Equal(CandidateRegistrar.Registered, given.Outcome);

            var after = await later.RowsAsync();
            var variant = after.Single(row => row.Candidate == FloorVariantName);

            Assert.Equal("the breakout rule at a 126-session high, not bought on an open more than 2 typical days under its price", FloorVariantName);
            Assert.Equal(2, CandidateEvaluator.Read(variant.Parameters)[PlanHook.RangeFloorParameter]);
            Assert.Equal(SecondZoneWide with { LevelFloor = 2 }, RuleHooks.Of(CandidateEvaluator.Read(variant.Parameters)).Plan);
            Assert.Equal(before.Count + 1, after.Count);
            Assert.DoesNotContain(after.Skip(before.Count), row => row.Event == CandidateFamily.Retired);
            Assert.Empty(Query(roomy, "SELECT candidate FROM waiting_variant;"));

            // Registered once: the same again, an unknown move and moves leaving it at its own settings are refused.
            Assert.Equal(CandidateRegistrar.Refused, (await later.RegisterBackgroundAsync(PlanLive, FloorAtTwo, "again", "background-again")).Outcome);
            Assert.Equal(CandidateRegistrar.Refused, (await later.RegisterBackgroundAsync(PlanLive, new Dictionary<string, double> { ["volumeMultiple"] = 2 }, "unknown", "background-unknown")).Outcome);
            Assert.Equal(CandidateRegistrar.Refused, (await later.RegisterBackgroundAsync("the breakout rule at 1.6 times the volume", FloorAtTwo, "not live", "background-not-live")).Outcome);
        }

        var (full, fullAt) = await PlanRegisterAsync(CandidateFamily.Maximum - 2);

        using (full)
        {
            Assert.Equal(CandidateRegistrar.Registered, (await new CandidateRegistrar(FixedClock.At(fullAt, SessionZones.UnitedStates), full.DatabaseFile).RegisterPlannedAsync(PlanLive, SecondZoneWide, true, "23.10's table", "plan-given")).Outcome);

            var registrar = new CandidateRegistrar(FixedClock.At(fullAt.AddHours(1), SessionZones.UnitedStates), full.DatabaseFile);

            Assert.Equal(CandidateFamily.Maximum, CandidateFamily.Standing(await registrar.RowsAsync(), fullAt.AddHours(1)).Count);

            // No bar stored: no session stands to walk after, and nothing is entered.
            Assert.Equal(CandidateRegistrar.Refused, (await registrar.RegisterBackgroundAsync(PlanLive, FloorAtTwo, "the answers", "background-no-bar")).Outcome);

            full.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('ZZZ', '2026-11-10', '1', '1', '1', '1', 1, 'eod', '2026-11-10T22:00:00Z');");

            var rows = (await registrar.RowsAsync()).Count;
            var entered = await registrar.RegisterBackgroundAsync(PlanLive, FloorAtTwo, "the answers", "background-full");

            Assert.True(entered.Outcome == CandidateRegistrar.Entered, entered.Detail);
            Assert.Equal(rows, (await registrar.RowsAsync()).Count);
            Assert.Equal([$"{FloorVariantName}|{PlanLive}|2026-11-10"], Query(full, "SELECT candidate || '|' || live || '|' || walked_from FROM waiting_variant;"));

            // A reward variant in a full family is no walk of its live rule's own orders, and entering one twice is refused.
            Assert.Equal(CandidateRegistrar.Refused, (await registrar.RegisterBackgroundAsync(PlanLive, new Dictionary<string, double> { [RuleHooks.RewardTradedParameter] = 2 }, "the answers", "background-reward")).Outcome);
            Assert.Equal(CandidateRegistrar.Refused, (await registrar.RegisterBackgroundAsync(PlanLive, FloorAtTwo, "the answers", "background-twice")).Outcome);
            Assert.Single(Query(full, "SELECT candidate FROM waiting_variant;"));
        }
    }

    [Fact]
    public async Task AWaitingVariantIsWalkedEachNightFromItsLiveRulesOrdersAndRegistersInTheFirstSlotThatFrees()
    {
        var (store, at) = await PlanRegisterAsync(CandidateFamily.Maximum - 2);

        using (store)
        {
            Assert.Equal(CandidateRegistrar.Registered, (await new CandidateRegistrar(FixedClock.At(at, SessionZones.UnitedStates), store.DatabaseFile).RegisterPlannedAsync(PlanLive, SecondZoneWide, true, "23.10's table", "plan-given")).Outcome);
            store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('ZZZ', '2026-11-10', '1', '1', '1', '1', 1, 'eod', '2026-11-10T22:00:00Z');");
            Assert.Equal(CandidateRegistrar.Entered, (await new CandidateRegistrar(FixedClock.At(at.AddHours(1), SessionZones.UnitedStates), store.DatabaseFile).RegisterBackgroundAsync(PlanLive, FloorAtTwo, "the answers", "background-full")).Outcome);

            // The live rule's two orders of 11 Nov at 100, AAA opening under the floor at 95 and BBB above it at 97, and one of
            // 10 Nov, on the session the variant was entered after, which it never walks.
            foreach (var (ticker, open) in new[] { ("AAA", 95), ("BBB", 97) })
            {
                foreach (var (session, o, l, c) in new[] { ("2026-11-11", 102, 101, 102), ("2026-11-12", open, 94, 97), ("2026-11-13", 97, 96, 111), ("2026-11-16", 112, 111, 112) })
                {
                    store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at) VALUES ('{ticker}', '{session}', '{o}', '{c}', '{l}', '{c}', 1000, 'eod', '{session}T22:00:00Z');");
                }

                store.Execute("INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, plan) " +
                    $"VALUES ('{PlanLive}', '{ticker}', '2026-11-11', 'breakout', 1, '102', '90', '110', 2.0, 1.5, 20, '{AtAHundred.Json()}');");
            }

            store.Execute("INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap, plan) " +
                $"VALUES ('{PlanLive}', 'CCC', '2026-11-10', 'breakout', 1, '102', '90', '110', 2.0, 1.5, 20, '{AtAHundred.Json()}');");

            var night = new WaitingVariants(FixedClock.At(at.AddDays(6), SessionZones.UnitedStates), store.DatabaseFile);

            Assert.Equal("1 background variant(s) waiting, 0 registered in a slot that freed, 2 order(s) ended under a waiting variant's floor written", await night.NightAsync("night-1"));
            Assert.Equal(["AAA|1|", "BBB|0|97"], Query(store, "SELECT ticker || '|' || under_floor || '|' || COALESCE(fill, '') FROM waiting_variant_trade ORDER BY ticker;"));
            Assert.Equal(["1"], Query(store, "SELECT COUNT(*) FROM waiting_variant_trade WHERE ticker = 'BBB' AND result = live_result;"));

            // Walked again, it writes nothing twice.
            Assert.Equal("1 background variant(s) waiting, 0 registered in a slot that freed, 0 order(s) ended under a waiting variant's floor written", await night.NightAsync("night-2"));

            // A slot frees: the next night registers it at the live rule's settings with the floor, and walks it no more.
            var freed = new CandidateRegistrar(FixedClock.At(at.AddDays(7), SessionZones.UnitedStates), store.DatabaseFile);
            var retiring = CandidateFamily.Standing(await freed.RowsAsync(), at.AddDays(7)).First(row => row.Candidate != PlanLive);

            Assert.Equal(CandidateRegistrar.Retired, (await freed.RetireAsync(retiring.Candidate, "a slot for the test", "free-a-slot")).Outcome);

            var next = new WaitingVariants(FixedClock.At(at.AddDays(8), SessionZones.UnitedStates), store.DatabaseFile);

            Assert.Equal("1 background variant(s) waiting, 1 registered in a slot that freed, 0 order(s) ended under a waiting variant's floor written", await next.NightAsync("night-3"));
            Assert.Contains(CandidateFamily.Standing(await freed.RowsAsync(), at.AddDays(8)), row => row.Candidate == FloorVariantName);
            Assert.Equal("0 background variant(s) waiting, 0 registered in a slot that freed, 0 order(s) ended under a waiting variant's floor written", await next.NightAsync("night-4"));

            // Both tables take inserts alone.
            foreach (var write in new[] { "UPDATE waiting_variant SET live = 'x';", "DELETE FROM waiting_variant;", "UPDATE waiting_variant_trade SET result = 0;", "DELETE FROM waiting_variant_trade;" })
            {
                Assert.Throws<SqliteException>(() => store.Execute(write));
            }
        }
    }
}
