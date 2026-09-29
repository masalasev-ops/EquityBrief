using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The sweep's arithmetic: every setting's figures read off a design's cumulative table are the figures reading
// that setting directly gives, over constructed candidates; the trigger's arrival is its event's alone whatever
// the pullback's depth was on the sessions before; the grid holds the designs the ruling counts; and the exit
// that moves the stop to break-even is the scorer's own walk until the move fires.
//
// Every candidate figure is a multiple of a quarter, so a sum over a few thousand of them is exact in single
// precision whatever order the table and the direct reading add them in, and the two are compared exactly.
// see: A starting point is proposed from the centre of a plateau of the stored history and never its best variation, and nothing is registered before the operator approves it
public partial class FixtureExpectations
{
    // Three hundred sessions, eight scored years of them and thirty-one blocks, and the constructed candidates
    // over them, enough that the loosest coarse settings reach stage 1's floor of 300 scored trades and the
    // tightest do not.
    const int SweepNights = 300;

    const int SweepCount = 3_000;

    // A small grid of every dial, each at levels the constructed readings fall on both sides of: 3 levels on
    // every ordered dial and 2 stop options, 13,122 settings.
    static readonly SweepGrid SweepSmall = new(
        [0.30, 0.50, 0.70],
        [0.5, 1, 2],
        [3, 5, 8],
        [1.0, 1.5, SweepGrid.Off],
        [1, 3, 8],
        [1.0, 1.5, 2.5],
        [(0.5, 4), (1, 2.5)],
        [SweepGrid.MarketOff, 0.45, 0.55],
        [0, 2, 4]);

    // The name whose history the fifth review asked for: its pullback is 0.8 and 0.9 typical moves deep on the
    // two sessions before its trigger's night and 1.2 on the night, and the trigger's event fired on the first of
    // the three alone, so it arrives 0, 1 and 2 sessions back on them.
    const int SweepPulled = 999;

    static readonly int[] SweepPulledSessions = [100, 101, 102];

    static float SweepQuarter(Random random, double low, double high) =>
        (float)(Math.Round((low + (random.NextDouble() * (high - low))) * 4) / 4);

    static List<SweepCandidate> SweepConstructed(int count, int seed)
    {
        var random = new Random(seed);
        var candidates = new List<SweepCandidate>();

        for (var at = 0; at < count; at++)
        {
            var session = random.Next(SweepNights);
            var candidate = new SweepCandidate
            {
                Name = random.Next(40),
                Session = session,
                Year = session * SweepFigures.Years / SweepNights,
                Block = session / 10,
                Breadth = random.NextDouble() < 0.1 ? double.NaN : 0.35 + (random.NextDouble() * 0.3),
                Uptrend = (byte)(random.NextDouble() < 0.8 ? 127 : random.Next(128)),
                Earnings = (short)(random.NextDouble() < 0.7 ? -1 : random.Next(25)),
            };

            for (var measure = 0; measure < 3; measure++)
            {
                candidate.Strength[measure] = random.NextDouble() < 0.05 ? double.NaN : 0.2 + (random.NextDouble() * 0.8);
                candidate.Depth[measure] = 0.3 + (random.NextDouble() * 9);
                candidate.DryUp[measure] = random.NextDouble() < 0.1 ? double.NaN : 0.5 + (random.NextDouble() * 1.5);
                candidate.Band[measure] = (sbyte)(random.Next(10) - 1);
            }

            for (var age = 0; age < candidate.Age.Length; age++)
            {
                candidate.Age[age] = (sbyte)(random.Next(10) - 1);
            }

            for (var plan = 0; plan < candidate.Plans.Length; plan++)
            {
                if (random.NextDouble() < 0.2)
                {
                    continue;
                }

                var outcomes = new SweepPlanOutcomes { RewardToRisk = 0.8 + (random.NextDouble() * 2.7), StopMoves = 0.3 + (random.NextDouble() * 4.2) };

                for (var exit = 0; exit < SweepAxes.Exits; exit++)
                {
                    // Wins more often than not, so the loosest settings are viable and the tightest are not.
                    var roll = random.NextDouble();
                    var code = roll < 0.5 ? SweepPlanOutcomes.Win : roll < 0.8 ? SweepPlanOutcomes.Loss : roll < 0.9 ? SweepPlanOutcomes.Unresolved : roll < 0.95 ? SweepPlanOutcomes.NeverEntered : SweepPlanOutcomes.Immature;
                    var entered = code is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved;

                    outcomes.Code[exit] = code;
                    // The calibrated bar is a share of one, here a sixty-fourth from 0.20 to 0.41, and the
                    // break-even a per cent.
                    outcomes.Null[exit] = code is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss && random.NextDouble() < 0.95 ? random.Next(13, 27) / 64f : float.NaN;
                    outcomes.BreakEven[exit] = entered ? SweepQuarter(random, 25, 45) : float.NaN;
                    outcomes.Multiple[exit] = entered ? SweepQuarter(random, -1.5, 3) : float.NaN;
                }

                candidate.Plans[plan] = outcomes;
            }

            candidates.Add(candidate);
        }

        // The pulled name's three sessions: every other reading passes the loosest setting of every dial, and
        // each plan wins, so what lists it is its depth and its trigger's arrival alone.
        for (var at = 0; at < SweepPulledSessions.Length; at++)
        {
            var candidate = new SweepCandidate
            {
                Name = SweepPulled,
                Session = SweepPulledSessions[at],
                Year = SweepPulledSessions[at] * SweepFigures.Years / SweepNights,
                Block = SweepPulledSessions[at] / 10,
                Breadth = 0.6,
                Uptrend = 127,
                Earnings = -1,
            };

            for (var measure = 0; measure < 3; measure++)
            {
                candidate.Strength[measure] = 0.9;
                candidate.Depth[measure] = new[] { 0.8, 0.9, 1.2 }[at];
                candidate.DryUp[measure] = 0.9;
                candidate.Band[measure] = 5;
            }

            for (var trigger = 0; trigger < 3; trigger++)
            {
                for (var support = 0; support < 3; support++)
                {
                    candidate.Age[SweepCandidate.AgeAt((TriggerKind)trigger, (SupportKind)support)] = (sbyte)at;
                }
            }

            for (var plan = 0; plan < candidate.Plans.Length; plan++)
            {
                var outcomes = new SweepPlanOutcomes { RewardToRisk = 2.0, StopMoves = 1.5 };

                for (var exit = 0; exit < SweepAxes.Exits; exit++)
                {
                    outcomes.Code[exit] = SweepPlanOutcomes.Win;
                    outcomes.Null[exit] = 0.25f;
                    outcomes.BreakEven[exit] = 40;
                    outcomes.Multiple[exit] = 1.5f;
                }

                candidate.Plans[plan] = outcomes;
            }

            candidates.Add(candidate);
        }

        return [.. candidates.OrderBy(one => one.Session).ThenBy(one => one.Name)];
    }

    // One setting read directly: every pick it passes, its figures summed and the nights it lists counted.
    static SweepMeasures SweepDirectly(IReadOnlyList<SweepPick> picks, DialSetting setting, int exit, int nights)
    {
        var sum = new float[SweepFigures.Width];
        var figures = new float[SweepFigures.Width];
        var listed = new HashSet<int>();

        foreach (var pick in picks)
        {
            if (!pick.Corner.Passes(setting))
            {
                continue;
            }

            SweepFigures.Fill(figures, pick.Year, pick.Block, pick.Plan.Code[exit], pick.Plan.Null[exit], pick.Plan.BreakEven[exit], pick.Plan.Multiple[exit]);

            for (var at = 0; at < sum.Length; at++)
            {
                sum[at] += figures[at];
            }

            listed.Add(pick.Session);
        }

        return SweepMeasures.Of(sum, listed.Count, nights);
    }

    static readonly SweepDesign[] SweepDesigns =
    [
        SweepDesign.Live,
        SweepDesign.Live with { Strength = StrengthMeasure.TwelveLessOne, Uptrend = UptrendRule.RisingTwoHundred, Support = SupportKind.Average, Plan = PlanRule.NearestBands, Hold = 10, BreakEven = true },
        SweepDesign.Live with { ReferenceHigh = 50, Trigger = TriggerKind.TopQuarterOfRange, Support = SupportKind.AnyBand, Plan = PlanRule.Ladder, EarningsWindow = 0, Hold = 40 },
    ];

    [Fact]
    public void EverySettingsFiguresOffTheTableAreTheFiguresReadingItDirectlyGives()
    {
        var candidates = SweepConstructed(SweepCount, 20260929);

        foreach (var design in SweepDesigns)
        {
            var picks = SweepStages.Picks(candidates, design, SweepSmall);
            var table = SweepStages.Fine(candidates, design, SweepNights,SweepSmall);

            Assert.Equal(SweepSmall.Variations, table.Length);
            Assert.True(picks.Count >= 300, $"{design.Key} picked {picks.Count} of {candidates.Count} candidates, expected at least 300.");

            var listing = 0;

            for (var index = 0; index < table.Length; index++)
            {
                var setting = DialSetting.Of(SweepSmall, index / SweepSmall.CellsPerStop, index % SweepSmall.CellsPerStop);
                var direct = SweepDirectly(picks, setting, design.ExitIndex, SweepNights);
                var read = table[index];

                Assert.Equal(direct.Scored, read.Scored);
                Assert.Equal((float)(direct.Share ?? double.NaN), read.Share);
                Assert.Equal((float)(direct.BreakEven ?? double.NaN), read.BreakEven);
                Assert.Equal((float)(direct.NoSkill ?? double.NaN), read.NoSkill);
                Assert.Equal((float)(direct.AverageMultiple ?? double.NaN), read.AverageMultiple);
                Assert.Equal(direct.YearsBeatingBreakEven, read.YearsBeatingBreakEven);
                Assert.Equal(direct.BeatsBreakEvenWithoutItsBestYear, read.WithoutBestYear);
                Assert.Equal(direct.BlocksWithTrades, read.Blocks);
                Assert.Equal((float)direct.ListingShareOfNights, read.Listing);
                Assert.Equal(direct.MeetsTheFloors, read.MeetsTheFloors);

                listing += direct.NightsListing > 0 ? 1 : 0;
            }

            // Not a table of nothing: most settings list a name on some night.
            Assert.True(listing > table.Length / 2, $"{design.Key}: {listing} of {table.Length} settings list a name.");

            // And the sweep's own direct reading, the one the report states, agrees with this one.
            foreach (var index in new[] { 0, 1, 4_000, 6_560, 6_561, 13_121 })
            {
                var setting = DialSetting.Of(SweepSmall, index / SweepSmall.CellsPerStop, index % SweepSmall.CellsPerStop);

                Assert.Equal(SweepDirectly(picks, setting, design.ExitIndex, SweepNights), SweepStages.Direct(candidates, design, SweepSmall, setting, SweepNights), SweepMeasuresComparer.Instance);
            }
        }
    }

    [Fact]
    public void StageOnesViableCountAndMedianAreTheOnesReadingEachCoarseSettingDirectlyGives()
    {
        var candidates = SweepConstructed(SweepCount, 20260929);
        var selection = SweepDesign.Live.Selection;
        var ranks = SweepStages.Rank(candidates, selection, SweepNights);
        var picks = SweepStages.Picks(candidates, selection, SweepGrid.Coarse);

        Assert.Equal(SweepAxes.Exits, ranks.Count);

        foreach (var exit in new[] { 0, SweepAxes.ExitIndex(63, false), SweepAxes.Exits - 1 })
        {
            var viable = 0;
            var multiples = new List<double>();
            SweepMeasures? live = null;

            for (var index = 0; index < SweepGrid.Coarse.Variations; index++)
            {
                var setting = DialSetting.Of(SweepGrid.Coarse, index / SweepGrid.Coarse.CellsPerStop, index % SweepGrid.Coarse.CellsPerStop);
                var direct = SweepDirectly(picks, setting, exit, SweepNights);

                if (direct.Viable)
                {
                    viable++;

                    if (direct.AverageMultiple is { } multiple)
                    {
                        multiples.Add(multiple);
                    }
                }

                if (setting == DialSetting.LiveOnCoarse)
                {
                    live = direct;
                }
            }

            var rank = ranks[exit];
            var (hold, breakEven) = SweepAxes.ExitOf(exit);

            Assert.Equal(selection with { Hold = hold, BreakEven = breakEven }, rank.Design);
            Assert.Equal(viable, rank.Viable);
            Assert.InRange(viable, 1, SweepGrid.Coarse.Variations - 1);
            Assert.Equal((double)viable / SweepGrid.Coarse.Variations, rank.ViableShare);

            multiples.Sort();

            var median = multiples.Count % 2 == 1 ? multiples[multiples.Count / 2] : (multiples[(multiples.Count / 2) - 1] + multiples[multiples.Count / 2]) / 2;

            Assert.Equal(median, rank.MedianMultiple);

            // The live rule's own settings are read where the design is the live rule's own, and nowhere else.
            if (rank.Design == SweepDesign.Live)
            {
                Assert.Equal(live, rank.Live, SweepMeasuresComparer.Instance);
            }
            else
            {
                Assert.Null(rank.Live);
            }
        }
    }

    [Fact]
    public void ATriggerFiredOnASessionWhosePullbackFailsADepthStillArrivesOnTheNightThatPasses()
    {
        // The pulled name: 0.8, 0.9 and 1.2 typical moves deep on sessions 100, 101 and 102, its trigger first
        // fired on 100. Worked by hand on the small grid, the depth's low edge at 1.0 or 0.5 and the window at 3
        // or 1 sessions, every other dial at its loosest:
        //   low edge 1.0, window 3: 102 alone, the trigger two sessions back inside the window of three;
        //   low edge 1.0, window 1: none, 102's arrival being two sessions back;
        //   low edge 0.5, window 1: 100 alone, the night it fired;
        //   low edge 0.5, window 3: all three.
        var candidates = SweepConstructed(SweepCount, 20260929);
        var loosest = new DialSetting(0, 0, 2, 2, 0, 0, 0, 0, 0);
        var cases = new (DialSetting Setting, int[] Listed)[]
        {
            (loosest with { DepthLow = 1, Freshness = 1 }, [102]),
            (loosest with { DepthLow = 1, Freshness = 0 }, []),
            (loosest with { DepthLow = 0, Freshness = 0 }, [100]),
            (loosest with { DepthLow = 0, Freshness = 1 }, [100, 101, 102]),
        };

        foreach (var design in SweepDesigns)
        {
            var table = SweepStages.Fine(candidates, design, SweepNights,SweepSmall);
            var picks = SweepStages.Picks(candidates, design, SweepSmall);

            foreach (var (setting, listed) in cases)
            {
                var picked = new HashSet<(int Name, int Session)>();
                var direct = SweepStages.Direct(candidates, design, SweepSmall, setting, SweepNights,picked);

                Assert.Equal(listed, picked.Where(pick => pick.Name == SweepPulled).Select(pick => pick.Session).Order());

                // The table agrees with the direct reading on the setting, the pulled name among its picks.
                var read = table[SweepPlateau.Index(SweepSmall, setting)];

                Assert.Equal(direct.Scored, read.Scored);
                Assert.Equal((float)direct.ListingShareOfNights, read.Listing);
                Assert.Equal(direct, SweepDirectly(picks, setting, design.ExitIndex, SweepNights), SweepMeasuresComparer.Instance);
            }
        }
    }

    [Fact]
    public void TheGridHoldsEveryExpressibleDesignAndLeavesOutTheLadderUnderARuleThatIsNotTheClassifier()
    {
        // 3 strength measures, 7 uptrend rules, 3 supports, 3 reference highs, 3 triggers, 3 plans, 8 exits and
        // 4 earnings windows: 54,432. The ladder under the three rules that are not the classifier cannot be
        // built, 3 x 3 x 3 x 3 x 1 x 8 x 4 x 3 = 7,776, leaving 46,656, of which 5,832 are selections.
        Assert.Equal(54_432, SweepAxes.AllCombinations);
        Assert.Equal(46_656, SweepAxes.Designs(selectionOnly: false).Count);
        Assert.Equal(5_832, SweepAxes.Designs(selectionOnly: true).Count);

        var left = SweepAxes.Designs(selectionOnly: false).ToHashSet();

        Assert.DoesNotContain(left, design => design.Plan == PlanRule.Ladder && design.Uptrend is UptrendRule.CloseAboveTwoHundred or UptrendRule.FiftyAboveTwoHundred or UptrendRule.RisingTwoHundred);
        Assert.Contains(SweepDesign.Live, left);
        Assert.False(SweepAxes.Expressible(SweepDesign.Live with { Plan = PlanRule.Ladder, Uptrend = UptrendRule.RisingTwoHundred }));
        Assert.True(SweepAxes.Expressible(SweepDesign.Live with { Plan = PlanRule.Ladder, Uptrend = UptrendRule.ClassifierHoldsTwoNights }));

        // Stage 1 reads 19,683 coarse settings a design and stage 2 2,016,000 fine ones.
        Assert.Equal(19_683, SweepGrid.Coarse.Variations);
        Assert.Equal(2_016_000, SweepGrid.Fine.Variations);

        // The live rule's settings on both grids, read as the live filter's own values.
        Assert.Equal("strength 0.5, depth 1 to 5, dry-up under 1.5, fresh within 3, reward to risk 1.5, stop 0.5 to 4, market 45%, band strength 0", DialSetting.LiveOnCoarse.Describe(SweepGrid.Coarse));
        Assert.Equal(DialSetting.LiveOnCoarse.Describe(SweepGrid.Coarse), SweepReport.LiveOnFine().Describe(SweepGrid.Fine));
    }

    [Fact]
    public void MovingTheStopToBreakEvenIsTheScorersWalkUntilTheMoveFires()
    {
        static IReadOnlyList<ReturnBar> After(params decimal[] closes) =>
            [.. closes.Select((close, at) => new ReturnBar(new DateOnly(2026, 1, 2).AddDays(at), close))];

        // Entered at 100, stopped below 95 and won at 104: the move needs a close at 105, which the target
        // comes before, so every path is the scorer's own.
        foreach (var path in new[] { After(101, 99, 104), After(99, 96, 94), After(100, 101, 102) })
        {
            Assert.Equal(
                ForwardReturnSeries.OverSetup(path, 95m, 104m, null, 100m, null, ForwardReturnSeries.Setup, 3),
                SweepWalk.OverSetupMovingTheStop(path, 95m, 104m, null, 100m, 3));
        }

        // Won at 112: a close at 105 moves the stop to 100, the next close at 99 stops the trade there, where
        // the scorer's fixed stop holds it to an unresolved end at 99 after three sessions.
        var moved = SweepWalk.OverSetupMovingTheStop(After(105, 99, 99), 95m, 112m, null, 100m, 3);
        var fixedStop = ForwardReturnSeries.OverSetup(After(105, 99, 99), 95m, 112m, null, 100m, null, ForwardReturnSeries.Setup, 3);

        Assert.Equal(ForwardReturnSeries.Loss, moved.Outcome);
        Assert.Equal(new DateOnly(2026, 1, 3), moved.ResolvedOn);
        Assert.Equal(-1.0, moved.ReturnPct!.Value, 9);
        Assert.Equal(ForwardReturnSeries.Unresolved, fixedStop.Outcome);

        // The break-even is the plan's own, from the stop it set, 5 over 17, not the moved stop's.
        Assert.Equal(5.0 / 17 * 100, moved.BreakEven!.Value, 9);

        // A close at 104.99 does not move it, so the same path runs to the scorer's answer.
        Assert.Equal(
            ForwardReturnSeries.OverSetup(After(104.99m, 99, 99), 95m, 112m, null, 100m, null, ForwardReturnSeries.Setup, 3),
            SweepWalk.OverSetupMovingTheStop(After(104.99m, 99, 99), 95m, 112m, null, 100m, 3));
    }

    // Two sets of figures compared field by field, the arrays by their elements.
    sealed class SweepMeasuresComparer : IEqualityComparer<SweepMeasures?>
    {
        public static readonly SweepMeasuresComparer Instance = new();

        public bool Equals(SweepMeasures? x, SweepMeasures? y) =>
            x is null || y is null
                ? x is null && y is null
                : x.Listed == y.Listed && x.Entered == y.Entered && x.Scored == y.Scored && x.Wins == y.Wins
                    && x.Share == y.Share && x.BreakEven == y.BreakEven && x.NoSkill == y.NoSkill && x.AverageMultiple == y.AverageMultiple
                    && x.YearScored.SequenceEqual(y.YearScored) && x.YearShare.SequenceEqual(y.YearShare)
                    && x.YearBreakEven.SequenceEqual(y.YearBreakEven) && x.YearNoSkill.SequenceEqual(y.YearNoSkill)
                    && x.YearAverageMultiple.SequenceEqual(y.YearAverageMultiple)
                    && x.BlocksWithTrades == y.BlocksWithTrades && x.NightsListing == y.NightsListing && x.Nights == y.Nights;

        public int GetHashCode(SweepMeasures? obj) => obj?.Scored ?? 0;
    }
}
