using EquityBrief.Core.Candidates;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: a Promote the night answers whose variant no longer stands, or whose setup holds no running revision,
// is answered without a swap, its words saying which, and the register stands as it stood; and a Keep is answered with the
// operator's word recorded and nothing changed.
// see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
public partial class FixtureExpectations
{
    internal static readonly string[] SwapClaims =
    [
        CheckReach.Key(Scope.FailureTable, "A Promote the night answers whose variant or running revision no longer stands"),
    ];

    [Fact]
    public async Task APromoteWhoseVariantNoLongerStandsIsAnsweredWithoutASwapAndAKeepChangesNothing()
    {
        using var store = await Reading.ReadSurface.PaperVariantsStore();
        var clock = FixedClock.At(new DateTimeOffset(2027, 2, 8, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var registrar = new CandidateRegistrar(clock, store.DatabaseFile);
        var variant = Core.Paper.RuleRegister.Slug(TheSetupFamilies.Breakouts[^2].Candidate);

        store.Execute($"INSERT INTO promotion_press (index_code, family, variant, press, month, pressed_at) VALUES ('GSPC', 'breakout', '{variant}', 'promote', '2027-02', '2027-02-06T14:00:00Z');");
        store.Execute($"INSERT INTO promotion_press (index_code, family, variant, press, month, pressed_at) VALUES ('GSPC', 'breakout', 'no-such-variant', 'promote', '2027-02', '2027-02-06T14:01:00Z');");
        store.Execute($"INSERT INTO promotion_press (index_code, family, variant, press, month, pressed_at) VALUES ('GSPC', 'breakout', '{variant}', 'keep', '2027-02', '2027-02-06T14:02:00Z');");

        // The variant retired the day before the night that would swap it.
        await new CandidateRegistrar(FixedClock.At(new DateTimeOffset(2027, 2, 7, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RetireAsync(TheSetupFamilies.Breakouts[^2].Candidate, "retired before the night, for the test", "test-retire");

        var before = (await registrar.RowsAsync()).Count;
        var answered = await new PromotionSwap(clock, store.DatabaseFile).NightAsync("night-test", new DateOnly(2027, 2, 8));

        Assert.Equal([PromotionSwap.NoVariant, PromotionSwap.NoVariant, Core.Paper.PromotionSwaps.Kept(1)], answered.Answers.Select(answer => answer.Words));
        Assert.All(answered.Answers, answer => Assert.False(answer.Applied));
        Assert.Null(answered.WroteFrom);
        Assert.Equal(before, (await registrar.RowsAsync()).Count);
        Assert.Equal(["promotion-swap|ok|3"], Query(store, "SELECT stage || '|' || outcome || '|' || rows_written FROM run_log WHERE run_id = 'night-test' AND stage = 'promotion-swap';"));

        // A night run again answers nothing twice, and a night that answered no press writes no row.
        Assert.Empty((await new PromotionSwap(clock, store.DatabaseFile).NightAsync("night-again", new DateOnly(2027, 2, 8))).Answers);
        Assert.Empty(Query(store, "SELECT stage FROM run_log WHERE run_id = 'night-again' AND stage = 'promotion-swap';"));
    }
}
