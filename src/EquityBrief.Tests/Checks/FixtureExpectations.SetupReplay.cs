using EquityBrief.Core.Ledger;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3: a setup's path replayed from its anchor through the one function, worked by hand over
// constructed closes under each live exit: the stop, the target, the trail and the cap, a series that runs out leaving
// the setup open, and an anchor that places no trade.
// see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
public partial class FixtureExpectations
{
    [Fact]
    public void ASetupIsReplayedFromItsAnchorUnderEachLiveExitAndWorkedByHand()
    {
        // Bought at 100 on the first close with the stop at 95 and the target at 110: a risk of 5.
        var fixedPlan = new SetupAnchor(new DateOnly(2026, 10, 1), 100, 95, 110, null, 5, 1.0);

        // The stop: 101, 97, 94 sells at 94 on the third session, 1.2 risks lost.
        Assert.Equal(new SetupOutcome(-1.2, 3, SetupEnds.Stop), SetupReplay.Replay([100, 101, 97, 94, 99, 99, 99], 0, fixedPlan));

        // The target: 103, 108, 112 sells at 112 on the third, 2.4 risks won; a close at the target sells there too.
        Assert.Equal(new SetupOutcome(2.4, 3, SetupEnds.Target), SetupReplay.Replay([100, 103, 108, 112, 90], 0, fixedPlan));
        Assert.Equal(new SetupOutcome(2.0, 1, SetupEnds.Target), SetupReplay.Replay([100, 110], 0, fixedPlan));

        // The cap: five sessions reaching neither sell at the fifth close, 102, 0.4 risks won; a close exactly at the
        // stop does not sell.
        Assert.Equal(new SetupOutcome(0.4, 5, SetupEnds.Cap), SetupReplay.Replay([100, 101, 95, 99, 101, 102, 150], 0, fixedPlan));

        // The series runs out after two closes: open, two sessions held, no result.
        Assert.Equal(new SetupOutcome(null, 2, SetupEnds.Open), SetupReplay.Replay([100, 101, 102], 0, fixedPlan));

        // A trailing plan: the stop at 95 follows the highest close by 3, never lowered. Closes 104 and 106 raise it to
        // 101 and 103; 102 is under 103, so it sells there on the third session at 0.4 risks won by the trail.
        var trailing = new SetupAnchor(new DateOnly(2026, 10, 1), 100, 95, null, 3, 10, 1.0);

        Assert.Equal(new SetupOutcome(0.4, 3, SetupEnds.Trail), SetupReplay.Replay([100, 104, 106, 102], 0, trailing));

        // The same plan sold under its own stop before the trail rose: 94 on the first session is the stop.
        Assert.Equal(new SetupOutcome(-1.2, 1, SetupEnds.Stop), SetupReplay.Replay([100, 94], 0, trailing));

        // The anchor's session need not be the first close: from the third close the same figures.
        Assert.Equal(new SetupOutcome(-1.2, 3, SetupEnds.Stop), SetupReplay.Replay([50, 60, 100, 101, 97, 94], 2, fixedPlan));

        // An anchor placing no trade: a stop at the buy, a target under it, a trail of nothing.
        Assert.Equal(SetupEnds.None, SetupReplay.Replay([100, 101], 0, fixedPlan with { Stop = 100 }).End);
        Assert.Equal(SetupEnds.None, SetupReplay.Replay([100, 101], 0, fixedPlan with { Target = 99 }).End);
        Assert.Equal(SetupEnds.None, SetupReplay.Replay([100, 101], 0, trailing with { Trail = 0 }).End);
    }
}
