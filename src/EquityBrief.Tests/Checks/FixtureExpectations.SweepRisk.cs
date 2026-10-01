using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The risk a sweep result is counted in. The stepped plan is bought where a close first sits in its zone, which
// can be a hair above its stop, so its result is counted on the risk its plan stated and a fill nearer the stop
// than the stop setting's floor is no trade; a plan bought at the close is counted on its own fill, which is its
// entry; a trade that runs out of sessions is counted at its last close, as its benchmark counts it; and every
// figure is also stated without its largest results by size. Each worked by hand over constructed closes.
// see: The stepped plan's result is counted on the risk its plan stated, and a fill nearer its stop than the stop setting's floor is no trade
public partial class FixtureExpectations
{
    // The stepped plan every test here scores: an entry named at 100, a stop at 95, a target at 110, twice the
    // risk, a zone whose top edge is 102, and a typical move of 2, so the stop sits 2.5 moves under the entry.
    static readonly SweepPlan SteppedPlan = new(100m, 95m, 110m, 2m, 2.5, 102m);

    const double SteppedTypicalMove = 2.0;

    // The listing's close and the closes after it as the bars a candidate's outcomes are scored over, each on
    // its own session in order.
    static (SweepBar[] Bars, int[] SessionAt, IReadOnlyList<ReturnBar> After) SteppedPath(decimal listing, params decimal[] after)
    {
        var first = new DateOnly(2026, 1, 5);
        decimal[] closes = [listing, .. after];
        var bars = closes.Select((close, at) => new SweepBar(first.AddDays(at), close, close, close, 1_000)).ToArray();

        return (bars, [.. Enumerable.Range(0, bars.Length)], [.. bars.Skip(1).Select(bar => new ReturnBar(bar.Session, bar.Close))]);
    }

    static SweepPlanOutcomes SteppedOutcomes(SweepPlan plan, bool stepped, decimal listing, params decimal[] after)
    {
        var (bars, sessionAt, closesAfter) = SteppedPath(listing, after);

        return SweepCandidates.Outcomes(plan, stepped, closesAfter, listing, null, 0, bars, sessionAt, 0, SteppedTypicalMove);
    }

    // A candidate every dial's loosest setting passes, on a name and session of its own, so what a walk makes of
    // it is its plan's outcomes alone.
    static SweepCandidate SweepPassing(int name, int session)
    {
        var candidate = new SweepCandidate
        {
            Name = name,
            Session = session,
            Year = session * SweepFigures.Years / SweepNights,
            Block = session / 10,
            Breadth = 0.6,
            Uptrend = 127,
            Earnings = -1,
        };

        for (var measure = 0; measure < 3; measure++)
        {
            candidate.Strength[measure] = 0.9;
            candidate.Depth[measure] = 2.5;
            candidate.DryUp[measure] = 0.9;
            candidate.Band[measure] = 5;
        }

        Array.Fill(candidate.Age, (sbyte)0);

        return candidate;
    }

    static DialSetting SweepLoosest()
    {
        var grid = SweepGrid.Extended;

        return new DialSetting(0, 0, grid.DepthHighs.Count - 1, grid.DryUpCeilings.Count - 1, grid.Freshness.Count - 1, 0, 0, 0, 0);
    }

    [Fact]
    public void ASteppedPlanFilledAHairAboveItsStopIsNoTradeAtAStopFloorOfHalfAMove()
    {
        var ten = SweepAxes.ExitIndex(10, false);
        var design = SweepDesign.Live with { Plan = PlanRule.Ladder, Hold = 10 };
        var setting = SweepLoosest();

        // The loosest setting's stop option runs from half a typical move, which is the floor the fill is held to.
        Assert.Equal(0.5f, SweepStages.StopFloor(setting));

        // Listed at 104, above the zone. The first close inside it is 95.10, ten cents over the stop, a
        // twentieth of a typical move; the trade then closes at 110. Counted on the fill, its risk would be 0.10
        // in 95.10 and the win 149 times it.
        var hair = SteppedOutcomes(SteppedPlan, stepped: true, 104m, 95.10m, 96m, 110m);

        Assert.Equal(SweepPlanOutcomes.Win, hair.Code[ten]);
        Assert.Equal(0.05f, hair.FillMoves[ten], 5);
        Assert.Equal((110.0 - 95.10) / 95.10 * 100 / 5.0, hair.Multiple[ten], 4);
        Assert.False(hair.IsATradeAt(ten, SweepStages.StopFloor(setting)));

        // A fill exactly half a move over the stop, at 96, is a trade: the floor is held at, not above.
        var atTheFloor = SteppedOutcomes(SteppedPlan, stepped: true, 104m, 96m, 97m, 110m);

        Assert.Equal(0.5f, atTheFloor.FillMoves[ten], 5);
        Assert.True(atTheFloor.IsATradeAt(ten, SweepStages.StopFloor(setting)));

        SweepMeasures Walked(SweepPlanOutcomes outcomes)
        {
            var candidate = SweepPassing(1, 10);

            candidate.Plans[SweepCandidate.PlanAt(PlanRule.Ladder, default)] = outcomes;

            return SweepStages.Direct([candidate], design, setting, ConditionSetting.Off, SweepNights);
        }

        // The walk lists the hair's fill and counts no trade for it: nothing entered and no result.
        var refused = Walked(hair);

        Assert.Equal(1, refused.Listed);
        Assert.Equal(0, refused.Entered);
        Assert.Null(refused.AverageMultiple);
        Assert.Equal(1, refused.NightsListing);

        // The fill at the floor is entered and carries its result, 14 on 96 over the stated risk of 5 per cent.
        var taken = Walked(atTheFloor);

        Assert.Equal(1, taken.Listed);
        Assert.Equal(1, taken.Entered);
        Assert.Equal((110.0 - 96.0) / 96.0 * 100 / 5.0, taken.AverageMultiple!.Value, 4);
    }

    [Fact]
    public void ASteppedPlanFilledInsideItsZoneCountsItsReturnOverTheRiskItsPlanStated()
    {
        var ten = SweepAxes.ExitIndex(10, false);

        // The risk the plan stated: 5 on an entry of 100, whatever the fill. Read from a fill at 101 it would be
        // 6 on 101.
        Assert.Equal(5.0, SweepCandidates.RiskPercent(SteppedPlan, stepped: true, 101m), 9);
        Assert.Equal(6.0 / 101 * 100, SweepCandidates.RiskPercent(SteppedPlan, stepped: false, 101m), 9);

        // Listed at 104, filled at 101, three typical moves over the stop, and stopped by a close at 94: a loss
        // of 7 on 101, which is 1.386 times the stated risk and would be 1.167 times the fill's.
        var stopped = SteppedOutcomes(SteppedPlan, stepped: true, 104m, 101m, 94m);

        Assert.Equal(SweepPlanOutcomes.Loss, stopped.Code[ten]);
        Assert.Equal(3.0f, stopped.FillMoves[ten], 5);
        Assert.Equal((94.0 - 101.0) / 101.0 * 100 / 5.0, stopped.Multiple[ten], 4);

        // Filled and stopped on one session, the first close already under the stop: the fill the scorer carries
        // is the zone's top edge, 102, and the loss is the close against it over the stated risk.
        var atOnce = SteppedOutcomes(SteppedPlan, stepped: true, 104m, 94m);

        Assert.Equal(SweepPlanOutcomes.Loss, atOnce.Code[ten]);
        Assert.Equal(3.5f, atOnce.FillMoves[ten], 5);
        Assert.Equal((94.0 - 102.0) / 102.0 * 100 / 5.0, atOnce.Multiple[ten], 4);

        // Never filled, the target closed above before any close sat in the zone: no fill, no result, and a
        // listing the floor has nothing to refuse.
        var never = SteppedOutcomes(SteppedPlan, stepped: true, 104m, 106m, 111m);

        Assert.Equal(SweepPlanOutcomes.NeverEntered, never.Code[ten]);
        Assert.True(float.IsNaN(never.FillMoves[ten]));
        Assert.True(float.IsNaN(never.Multiple[ten]));
        Assert.True(never.IsATradeAt(ten, 1f));
    }

    [Fact]
    public void ATradeThatRunsOutOfSessionsIsCountedAtItsLastCloseAsItsBenchmarkCountsIt()
    {
        var ten = SweepAxes.ExitIndex(10, false);
        var twenty = SweepAxes.ExitIndex(20, false);
        var bought = new SweepPlan(100m, 97m, 106m, 2m, 1.5);
        decimal[] drifting = [100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 101.5m];

        // Bought at 100 with 3 at risk, never stopped and never at its target, and 101.5 at the tenth close: the
        // ten-session exit ends it there, half a risk up, and it is no win. Twenty sessions are not yet there.
        var open = SteppedOutcomes(bought, stepped: false, 100m, drifting);

        Assert.Equal(SweepPlanOutcomes.Unresolved, open.Code[ten]);
        Assert.Equal(0.5f, open.Multiple[ten], 5);
        Assert.Equal(SweepPlanOutcomes.Immature, open.Code[twenty]);
        Assert.True(float.IsNaN(open.Multiple[twenty]));

        // The benchmark's walk over the same closes ends the same member at the same close: half a risk.
        Span<double> outcome = stackalloc double[SweepAxes.Holds.Count];
        Span<bool> has = stackalloc bool[SweepAxes.Holds.Count];

        SweepBenchmark.Walk([100, .. drifting.Select(close => (double)close)], 0, 100, 97, 106, 3, false, outcome, has);

        Assert.True(has[0]);
        Assert.Equal(0.5, outcome[0], 9);
        Assert.False(has[1]);

        // The walk counts it entered and with its result, and not among the trades scored as won or lost.
        var candidate = SweepPassing(1, 10);

        candidate.Plans[SweepCandidate.PlanAt(PlanRule.Clear, SupportKind.AnchoredBand)] = open;

        var measures = SweepStages.Direct([candidate], SweepDesign.Live with { Hold = 10 }, SweepLoosest(), ConditionSetting.Off, SweepNights);

        Assert.Equal(1, measures.Entered);
        Assert.Equal(0, measures.Scored);
        Assert.Equal(0.5, measures.AverageMultiple!.Value, 6);

        // The stepped plan, listed at 104 and filled at 101, three moves over its stop, then 103 at the tenth
        // close: 2 on 101 over the stated risk of 5 per cent.
        var stepped = SteppedOutcomes(SteppedPlan, stepped: true, 104m, 101m, 102m, 102m, 102m, 102m, 102m, 102m, 102m, 102m, 103m);

        Assert.Equal(SweepPlanOutcomes.Unresolved, stepped.Code[ten]);
        Assert.Equal(3.0f, stepped.FillMoves[ten], 5);
        Assert.Equal((103.0 - 101.0) / 101.0 * 100 / 5.0, stepped.Multiple[ten], 4);

        // A setup no close ever entered through the ten sessions has no fill and no result.
        var above = SteppedOutcomes(SteppedPlan, stepped: true, 104m, 104m, 104m, 104m, 104m, 104m, 104m, 104m, 104m, 104m, 104m);

        Assert.Equal(SweepPlanOutcomes.NeverEntered, above.Code[ten]);
        Assert.True(float.IsNaN(above.Multiple[ten]));
        Assert.Null(SweepWalk.FillOf([.. Enumerable.Range(1, 10).Select(day => new ReturnBar(new DateOnly(2026, 1, 5).AddDays(day), 104m))], 95m, 110m, 102m, 104m, 10));
    }

    [Fact]
    public void APlanBoughtAtTheCloseIsCountedOnItsOwnFillWhicheverWayItsRiskIsRead()
    {
        // A plan bought at the close names that close as its entry and no zone, so the scorer fills it at the
        // listing's close and the fill is the entry: 100, a stop at 97 and a target at 106.
        var plan = new SweepPlan(100m, 97m, 106m, 2m, 1.5);
        // A win, a loss, a path with too few sessions to end, a path the moved stop ends and the fixed stop does
        // not, and ten sessions going nowhere, which the ten-session exits end unresolved.
        decimal[][] paths =
        [
            [101m, 103m, 106.5m],
            [99m, 98m, 96m],
            [100.5m, 101m, 100m],
            [103.5m, 99.5m, 99m],
            [100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m, 100.5m],
        ];
        var counted = 0;

        foreach (var path in paths)
        {
            var bought = SteppedOutcomes(plan, stepped: false, 100m, path);
            var (_, _, after) = SteppedPath(100m, path);

            for (var exit = 0; exit < SweepAxes.Exits; exit++)
            {
                var (hold, breakEven) = SweepAxes.ExitOf(exit);
                var scored = breakEven
                    ? SweepWalk.OverSetupMovingTheStop(after, plan.Stop, plan.Target, null, 100m, hold)
                    : ForwardReturnSeries.OverSetup(after, plan.Stop, plan.Target, null, 100m, null, ForwardReturnSeries.Setup, hold);

                if (scored.ReturnPct is { } made)
                {
                    // The fill is the plan's own entry, so its result is the scorer's return over 3 per cent,
                    // the close to the stop, as it was before the stepped plan's risk was read from its plan.
                    // The scorer states the fill on a win and a loss; the sweep's reading of it is the same
                    // close on every path.
                    Assert.True(scored.EnteredAt is null or 100m);
                    Assert.Equal(100m, SweepWalk.FillOf(after, plan.Stop, plan.Target, null, 100m, hold));
                    Assert.Equal(made / 3.0, bought.Multiple[exit], 4);
                    counted++;
                }
                else
                {
                    Assert.True(float.IsNaN(bought.Multiple[exit]));
                }

                // It carries no fill reading, so no stop floor refuses it.
                Assert.True(float.IsNaN(bought.FillMoves[exit]));
                Assert.True(bought.IsATradeAt(exit, 1f));
            }

            // Read the stepped plan's way it comes to the same results, the entry being the fill.
            var asStepped = SteppedOutcomes(plan, stepped: true, 100m, path);

            Assert.Equal(bought.Multiple, asStepped.Multiple);
            Assert.Equal(bought.Code, asStepped.Code);
        }

        // The win and the loss end under all eight exits, the moved stop ends the fourth path under its four,
        // and the ten sessions end unresolved under the two ten-session exits: 8, 8, 0, 4 and 2.
        Assert.Equal(22, counted);
    }

    [Fact]
    public void AFigureIsAlsoStatedWithoutItsFiveLargestResultsBySizeALargeLossAmongThem()
    {
        var design = SweepDesign.Live;
        var exit = design.ExitIndex;
        var setting = SweepLoosest();
        float[] results = [10f, -8f, 3f, 2f, 1.5f, -1f, 0.5f, -0.25f];
        var candidates = new List<SweepCandidate>();

        // Eight trades, each a name and a session of its own so none blocks another, each a quarter of a risk
        // over a benchmark of a quarter.
        for (var at = 0; at < results.Length; at++)
        {
            var candidate = SweepPassing(at, 10 + at);
            var outcomes = new SweepPlanOutcomes { RewardToRisk = 2.0, StopMoves = 1.5 };

            outcomes.Code[exit] = results[at] > 0 ? SweepPlanOutcomes.Win : SweepPlanOutcomes.Loss;
            outcomes.Multiple[exit] = results[at];
            outcomes.Benchmark[exit] = 0.25f;
            outcomes.Ends[exit] = 1;
            candidate.Plans[SweepCandidate.PlanAt(PlanRule.Clear, SupportKind.AnchoredBand)] = outcomes;
            candidates.Add(candidate);
        }

        var picks = SweepStages.Picks(candidates, design);
        var whole = SweepStages.Measures(picks, design, setting, ConditionSetting.Off, SweepNights);
        var trimmed = SweepStages.WithoutTheLargest(picks, design, setting, ConditionSetting.Off);

        // All eight: 7.75 over 8, and the edge a quarter under it.
        Assert.Equal(7.75 / 8, whole.AverageMultiple!.Value, 6);
        Assert.Equal((7.75 / 8) - 0.25, whole.Edge!.Value, 6);

        // The five largest by size are 10, -8, 3, 2 and 1.5, the loss of 8 leaving as the win of 10 does, and
        // -1, 0.5 and -0.25 are left: -0.25 a trade, and an edge of -0.5. Taken by value the loss of 8 would
        // stay and the three left would average -3.083.
        Assert.Equal(SweepStages.LargestLeftOut, trimmed.LeftOut);
        Assert.Equal(5, SweepStages.LargestLeftOut);
        Assert.Equal(3, trimmed.Left);
        Assert.Equal(-0.25, trimmed.AverageMultiple!.Value, 6);
        Assert.Equal(-0.5, trimmed.Edge!.Value, 6);

        // Fewer trades than the five leave none, and say so.
        var few = SweepStages.WithoutTheLargest(SweepStages.Picks([.. candidates.Take(3)], design), design, setting, ConditionSetting.Off);

        Assert.Equal(3, few.LeftOut);
        Assert.Equal(0, few.Left);
        Assert.Null(few.Edge);
        Assert.Null(few.AverageMultiple);

        // The page states the line under the record it trims.
        var line = SweepReport.Trimmed(trimmed);

        Assert.Contains("data-left-out=\"5\" data-left=\"3\"", line, StringComparison.Ordinal);
        Assert.Contains("an edge of -0.500 and a raw average of -0.250 times the risk over the 3 trade(s) left", line, StringComparison.Ordinal);

        // And each kind of plan's results by size: section 10's plan holds the eight, its largest 10 and its
        // smallest -8, two of them beyond 5 times the risk either way, and the stepped plan holds none.
        var sizes = SweepReport.ResultSizes(candidates, bound: 5);

        Assert.Equal(["the stepped plan", "the plan at the nearest bands", "section 10's plan"], sizes.Select(size => size.Plan));
        Assert.Equal((8, 10f, -8f, 2), (sizes[2].Results, sizes[2].Largest, sizes[2].Smallest, sizes[2].Beyond));
        Assert.Equal(0, sizes[0].Results);
        Assert.Equal(20, SweepReport.ResultBound);
    }
}
