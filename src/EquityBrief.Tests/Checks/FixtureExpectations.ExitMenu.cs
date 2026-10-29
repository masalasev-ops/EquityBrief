using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.5: every exit of the menu replayed by hand over one constructed path, a plan with a fixed
// target and the same plan trailing, each result worked out from the closes in multiples of the risk.
// see: The engines' hooks are eight forms a registration states and all default off, and every source a rule pins moves only in a checkpoint that registers every rule reading it again
public partial class FixtureExpectations
{
    // A buy at 100 with its stop at 95, a risk of 5, a target at 110, two risks, twenty sessions and a typical move of
    // 2, and the closes from the session after the buy.
    static readonly double[] MenuPath = [100, 102, 104, 106, 108, 105, 103, 107.5, 109, 104, 101, 99.5, 104, 111, 113, 112.5, 116, 112, 121, 118, 117];

    static readonly SetupAnchor MenuAnchor = new(new DateOnly(2026, 1, 2), 100, 95, 110, null, 20, 2.5);

    [Fact]
    public void EveryExitOfTheMenuIsReplayedByHandOverAConstructedPath()
    {
        // Thirty exits, numbered from one, a rule's own exit being nought: the twenty-six of 17.5 and from 21.4 the
        // time stop at three lengths and half sold at a risk.
        Assert.Equal(30, ExitMenu.Swing.Count);
        Assert.Equal(Enumerable.Range(1, 30), ExitMenu.Swing.Select(choice => choice.Number));
        Assert.Null(ExitMenu.Of(0));

        // The plan's own exit: nothing under 95 and nothing at 110 until the thirteenth session's 111, 2.2 risks.
        AssertMenu(null, 2.2, 13, SetupEnds.Target);

        // The stop raised to the buy at half a risk, one and one and a half: the second, third and fourth sessions
        // reach those, and the eleventh's 99.5 sells under the buy, -0.1; at two risks the target comes first.
        AssertMenu(1, -0.1, 11, SetupEnds.Stop);
        AssertMenu(2, -0.1, 11, SetupEnds.Stop);
        AssertMenu(3, -0.1, 11, SetupEnds.Stop);
        AssertMenu(4, 2.2, 13, SetupEnds.Target);

        // A trail from the buy with no target, at 3, 4 and 6 under the highest close: the floor reaches 105 and 104 by
        // the fourth session and the sixth's 103 sells, 0.6; at 6 it reaches 103 by the eighth and the tenth's 101
        // sells, 0.2.
        AssertMenu(5, 0.6, 6, SetupEnds.Trail);
        AssertMenu(6, 0.6, 6, SetupEnds.Trail);
        AssertMenu(7, 0.2, 10, SetupEnds.Trail);

        // The same once a close is a risk up, the third session's 106: the floor read from every close since the buy
        // then, so the same sales.
        AssertMenu(8, 0.6, 6, SetupEnds.Trail);
        AssertMenu(9, 0.6, 6, SetupEnds.Trail);
        AssertMenu(10, 0.2, 10, SetupEnds.Trail);

        // Once a close is two risks up, the thirteenth's 111: at 3 the floor follows 113 and 116 to 113 and the
        // seventeenth's 112 sells, 2.4; at 4 and at 6 the floor stays under every close and the cap's 117 ends it, 3.4.
        AssertMenu(11, 2.4, 17, SetupEnds.Trail);
        AssertMenu(12, 3.4, 20, SetupEnds.Cap);
        AssertMenu(13, 3.4, 20, SetupEnds.Cap);

        // A target at 1.5, 2, 2.5, 3 and 4 risks: 107.5 reached by the fourth session's 108, 110 by the thirteenth's
        // 111, 112.5 by the fourteenth's 113, 115 by the sixteenth's 116 and 120 by the eighteenth's 121.
        AssertMenu(14, 1.6, 4, SetupEnds.Target);
        AssertMenu(15, 2.2, 13, SetupEnds.Target);
        AssertMenu(16, 2.6, 14, SetupEnds.Target);
        AssertMenu(17, 3.2, 16, SetupEnds.Target);
        AssertMenu(18, 4.2, 18, SetupEnds.Target);

        // A time exit at ten sessions: the tenth's 101 is above the buy, so held to the target; under half a risk,
        // 102.5, so sold there, 0.2. At twenty and forty sessions the target comes first.
        AssertMenu(19, 2.2, 13, SetupEnds.Target);
        AssertMenu(20, 0.2, 10, SetupEnds.Time);
        AssertMenu(21, 2.2, 13, SetupEnds.Target);
        AssertMenu(22, 2.2, 13, SetupEnds.Target);
        AssertMenu(23, 2.2, 13, SetupEnds.Target);
        AssertMenu(24, 2.2, 13, SetupEnds.Target);

        // Half at the target, the thirteenth's 111 at 2.2, and the rest trailed at 4 and at 6 under the highest close,
        // which stays under every later close, so the cap's 117 at 3.4 ends the rest: 2.8 for the whole.
        AssertMenu(25, 2.8, 20, SetupEnds.Cap);
        AssertMenu(26, 2.8, 20, SetupEnds.Cap);

        // From 21.4 the time stop, whatever the trade stands at: the fifth session's 105 sells at 1.0 and the tenth's 101
        // at 0.2; at twenty the target's thirteenth session comes first.
        AssertMenu(27, 1.0, 5, SetupEnds.Time);
        AssertMenu(28, 0.2, 10, SetupEnds.Time);
        AssertMenu(29, 2.2, 13, SetupEnds.Target);
        Assert.Equal("sold at the close 5 sessions in, whatever the trade stands at", ExitMenu.Of(27)!.Words);

        // Half sold once a close is a risk up, the third session's 106 at 1.2, and the stop raised to the buy on the
        // rest: the eleventh's 99.5 sells the rest under it at -0.1, so 0.55 for the whole.
        AssertMenu(30, 0.55, 11, SetupEnds.Stop);
        Assert.Equal("half sold once a close is 1 risks up, the stop raised to the buy on the rest", ExitMenu.Of(30)!.Words);
    }

    [Fact]
    public void TheTimeStopAndTheHalfAtARiskAreReplayedByHandOnBothSidesOfTheirSessionAndTheirRisk()
    {
        // A buy at 100 with its stop at 95 and its target at 110, five sessions at most, over closes reaching exactly a
        // risk up on the second session and the target on the fourth.
        var anchor = new SetupAnchor(new DateOnly(2026, 1, 2), 100, 95, 110, null, 5, 2.5);
        double[] path = [100, 103, 105, 107, 111, 112];
        var halfAtRisk = ExitMenu.Of(30)!;
        var stopAtFive = ExitMenu.Of(27)!;

        // The half is sold at the second session's 105, exactly a risk up, 1.0, and the rest at the fourth's 111, 2.2:
        // 1.6 for the whole at the target.
        var reached = ExitMenu.Replay(path, 0, anchor, 2, halfAtRisk);

        Assert.Equal((1.6, 4, SetupEnds.Target), (Math.Round(reached.Result!.Value, 9), reached.Sessions, reached.End));

        // A hundredth under the level sells no half, and a close at the level and the target on one session is the
        // target's sale of the whole: 2.2 at the fourth session's 111.
        double[] under = [100, 103, 104.99, 104, 111, 112];
        var none = ExitMenu.Replay(under, 0, anchor, 2, halfAtRisk);

        Assert.Equal((2.2, 4, SetupEnds.Target), (Math.Round(none.Result!.Value, 9), none.Sessions, none.End));

        // The level reached a session later sells the half there, the third session's 107 at 1.4: 1.8 for the whole.
        var later = ExitMenu.Replay([100, 103, 104.99, 107, 111, 112], 0, anchor, 2, halfAtRisk);

        Assert.Equal((1.8, 4, SetupEnds.Target), (Math.Round(later.Result!.Value, 9), later.Sessions, later.End));

        // Before the level a close under the stop sells the whole, as the plan would: -1.2 at the second session.
        double[] stopped = [100, 103, 94, 107, 111, 112];
        var whole = ExitMenu.Replay(stopped, 0, anchor, 2, halfAtRisk);

        Assert.Equal((-1.2, 2, SetupEnds.Stop), (Math.Round(whole.Result!.Value, 9), whole.Sessions, whole.End));

        // After the half at the first session's 106, 1.2, the stop stands at the buy: a close at 99.5, which the plan's
        // own stop would have held through, sells the rest at -0.1, so 0.55 for the whole; a close at 100 does not.
        double[] raised = [100, 106, 99.5, 107, 111, 112];
        var rest = ExitMenu.Replay(raised, 0, anchor, 2, halfAtRisk);

        Assert.Equal((0.55, 2, SetupEnds.Stop), (Math.Round(rest.Result!.Value, 9), rest.Sessions, rest.End));
        Assert.Equal(SetupEnds.Target, ExitMenu.Replay([100, 106, 100, 107, 111, 112], 0, anchor, 2, halfAtRisk).End);

        // The cap ends what is left: half at the first session's 106, 1.2, and the fifth's 108, 1.6, so 1.4.
        double[] capped = [100, 106, 104, 105, 107, 108];
        var atCap = ExitMenu.Replay(capped, 0, anchor, 2, halfAtRisk);

        Assert.Equal((1.4, 5, SetupEnds.Cap), (Math.Round(atCap.Result!.Value, 9), atCap.Sessions, atCap.End));

        // On a trailing plan the rest keeps the plan's trail as well as the buy under it: 6 under the highest close, the
        // menu's path sells the rest at the tenth session's 101 after the half at the third's 106, 0.7 for the whole.
        var trailing = MenuAnchor with { Target = null, Trail = 6 };
        var trailed = ExitMenu.Replay(MenuPath, 0, trailing, 2, halfAtRisk);

        Assert.Equal((0.7, 10, SetupEnds.Trail), (Math.Round(trailed.Result!.Value, 9), trailed.Sessions, trailed.End));

        // The time stop at five sells the fifth session's close whatever it stands at, 108 at 1.6 here, and not the
        // fourth's 104; a close reaching the target on that session is the target's sale and not the time stop's; under
        // a cap of four the cap's close ends the trade first, 0.8; a path running out leaves it open; and a close under
        // the buy on the fifth session is sold there as well, -0.9.
        var fifth = ExitMenu.Replay([100, 101, 102, 103, 104, 108], 0, anchor, 2, stopAtFive);
        var atTarget = ExitMenu.Replay([100, 101, 102, 103, 104, 112], 0, anchor, 2, stopAtFive);
        var atFour = ExitMenu.Replay([100, 101, 102, 103, 104, 108], 0, anchor with { Cap = 4 }, 2, stopAtFive);
        var down = ExitMenu.Replay([100, 99, 98, 97, 96, 95.5], 0, anchor, 2, stopAtFive);

        Assert.Equal((1.6, 5, SetupEnds.Time), (Math.Round(fifth.Result!.Value, 9), fifth.Sessions, fifth.End));
        Assert.Equal((2.4, 5, SetupEnds.Target), (Math.Round(atTarget.Result!.Value, 9), atTarget.Sessions, atTarget.End));
        Assert.Equal((0.8, 4, SetupEnds.Cap), (Math.Round(atFour.Result!.Value, 9), atFour.Sessions, atFour.End));
        Assert.Null(ExitMenu.Replay([100, 101, 102, 103], 0, anchor, 2, stopAtFive).Result);
        Assert.Equal((-0.9, 5, SetupEnds.Time), (Math.Round(down.Result!.Value, 9), down.Sessions, down.End));
    }

    [Fact]
    public void AMenuExitOnATrailingPlanKeepsTheTrailUntilWhatItNamesChanges()
    {
        // The same buy and stop trailing 6 under the highest close with no target: its own walk sells at the tenth
        // session's 101, 0.2, as the families' trailing walk does.
        var trailing = MenuAnchor with { Target = null, Trail = 6 };
        var own = ExitMenu.Replay(MenuPath, 0, trailing, 2, null);

        Assert.Equal(0.2, own.Result!.Value, 9);
        Assert.Equal(10, own.Sessions);

        // A target on a trailing plan is a fixed target and no trail: 1.5 risks at the fourth session's 108.
        var target = ExitMenu.Replay(MenuPath, 0, trailing, 2, ExitMenu.Of(14));

        Assert.Equal(1.6, target.Result!.Value, 9);
        Assert.Equal(4, target.Sessions);

        // The stop raised to the buy at a risk keeps the trail, which is already above the buy when it matters: 0.2.
        Assert.Equal(0.2, ExitMenu.Replay(MenuPath, 0, trailing, 2, ExitMenu.Of(2)).Result!.Value, 9);

        // Half at two risks where the plan trails: the trail sells the whole at the tenth session before any close
        // reaches 110.
        var half = ExitMenu.Replay(MenuPath, 0, trailing, 2, ExitMenu.Of(25));

        Assert.Equal(0.2, half.Result!.Value, 9);
        Assert.Equal(SetupEnds.Stop, half.End);

        // A series running out before the trade ends leaves it open; a stop at the buy places none.
        Assert.Null(ExitMenu.Replay(MenuPath.AsSpan(0, 8), 0, MenuAnchor, 2, ExitMenu.Of(12)).Result);
        Assert.Equal(SetupEnds.None, ExitMenu.Replay(MenuPath, 0, MenuAnchor with { Stop = 100 }, 2, ExitMenu.Of(1)).End);

        // The benchmark under an exit averages the members whose closes reach its end: the menu's path, which the second
        // exit sells at -0.1, beside a member whose closes run out first, so the average is the path's over one member;
        // a session holding only the short member reads none.
        double[][] closes = [MenuPath, MenuPath[..8]];
        var both = ExitMenu.Benchmark(closes, [0, 1], [0, 0], (_, _) => 2, 2.5, 2, null, 20, ExitMenu.Of(2));

        Assert.Equal((-0.1, 1), (Math.Round(both.Average, 9), both.Members));
        Assert.True(double.IsNaN(ExitMenu.Benchmark(closes, [1], [0], (_, _) => 2, 2.5, 2, null, 20, ExitMenu.Of(2)).Average));
    }

    static void AssertMenu(int? number, double result, int sessions, string end)
    {
        var outcome = ExitMenu.Replay(MenuPath, 0, MenuAnchor, 2, number is { } one ? ExitMenu.Of(one) : null);

        Assert.True(outcome.Result is not null, $"exit {number} left the trade open");
        Assert.Equal(result, outcome.Result!.Value, 9);
        Assert.Equal(sessions, outcome.Sessions);
        Assert.Equal(end, outcome.End);
    }
}
