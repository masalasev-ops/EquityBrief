using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The sweep's arithmetic: the walk over a design's candidates keeps one open trade a stock exactly as the pages'
// open trade rule does over constructed listings; every setting's summary is the record reading it in full
// gives, with the rule inside; stage 1's viable count and median edge are the ones every coarse setting read
// directly gives; the trigger's arrival is its event's alone whatever the pullback's depth was on the sessions
// before; the benchmark is the same plan on every member, worked by hand; the grid holds the designs the ruling
// counts; and the exit that moves the stop to break-even is the scorer's own walk until the move fires.
//
// Every candidate figure is a multiple of a quarter, so a sum over a few thousand of them is exact in single
// precision whatever order the walk adds them in, and the two are compared exactly.
// see: A starting point is proposed from the deepest setting of a plateau on the edge and never its best variation, and nothing is registered before the operator approves it
public partial class FixtureExpectations
{
    // Three hundred sessions, eight scored years of them and thirty-one blocks, and the constructed candidates
    // over them, enough that the loosest coarse settings reach stage 1's floor of 300 scored trades and the
    // tightest do not.
    const int SweepNights = 300;

    const int SweepCount = 3_000;

    // The name whose history the fifth review asked for: its pullback is 0.8 and 0.9 typical moves deep on the
    // two sessions before its trigger's night and 1.2 on the night, and the trigger's event fired on the first of
    // the three alone, so it arrives 0, 1 and 2 sessions back on them.
    const int SweepPulled = 999;

    static readonly int[] SweepPulledSessions = [100, 101, 102];

    static float SweepQuarter(Random random, double low, double high) =>
        (float)(Math.Round((low + (random.NextDouble() * (high - low))) * 4) / 4);

    static SweepPlanOutcomes SweepOutcomes(Random random, double rewardToRisk, double stopMoves, bool everyExitWins = false)
    {
        var outcomes = new SweepPlanOutcomes { RewardToRisk = rewardToRisk, StopMoves = stopMoves };

        for (var exit = 0; exit < SweepAxes.Exits; exit++)
        {
            // Wins more often than not, so the loosest settings are viable and the tightest are not.
            var roll = random.NextDouble();
            var code = everyExitWins ? SweepPlanOutcomes.Win : roll < 0.5 ? SweepPlanOutcomes.Win : roll < 0.8 ? SweepPlanOutcomes.Loss : roll < 0.9 ? SweepPlanOutcomes.Unresolved : roll < 0.95 ? SweepPlanOutcomes.NeverEntered : SweepPlanOutcomes.Immature;
            var entered = code is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved;

            outcomes.Code[exit] = code;
            // The calibrated bar is a share of one, here a sixty-fourth from 0.20 to 0.41, and the break-even a
            // per cent.
            outcomes.Null[exit] = code is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss && (everyExitWins || random.NextDouble() < 0.95) ? random.Next(13, 27) / 64f : float.NaN;
            outcomes.BreakEven[exit] = entered ? SweepQuarter(random, 25, 45) : float.NaN;
            outcomes.Multiple[exit] = entered ? SweepQuarter(random, -1.5, 3) : float.NaN;
            outcomes.Benchmark[exit] = SweepQuarter(random, -0.5, 0.5);
            outcomes.Ends[exit] = (short)random.Next(1, 12);
        }

        return outcomes;
    }

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
                HighRatio = (float)(0.7 + (random.NextDouble() * 0.3)),
                SectorRank = (sbyte)random.Next(1, 12),
                RsiUpMask = (short)random.Next(0, 1 << SweepCandidate.Backs),
                SurpriseSessions = (short)(random.NextDouble() < 0.5 ? -1 : random.Next(0, 80)),
                SurprisePercent = (float)((random.NextDouble() * 20) - 5),
                Tightness = (float)(0.4 + (random.NextDouble() * 0.8)),
            };

            for (var measure = 0; measure < 3; measure++)
            {
                candidate.Strength[measure] = random.NextDouble() < 0.05 ? double.NaN : 0.2 + (random.NextDouble() * 0.8);
                candidate.Depth[measure] = 0.3 + (random.NextDouble() * 9);
                candidate.DryUp[measure] = random.NextDouble() < 0.1 ? double.NaN : 0.5 + (random.NextDouble() * 1.5);
                candidate.Band[measure] = (sbyte)(random.Next(10) - 1);
                candidate.RsiLow[measure] = (float)(20 + (random.NextDouble() * 40));
                candidate.PullbackSessions[measure] = (short)random.Next(0, 30);
                candidate.GapMoves[measure] = (float)(random.NextDouble() * 2.5);
            }

            for (var back = 0; back < SweepCandidate.Backs; back++)
            {
                candidate.TurnVolume[back] = (float)(0.5 + (random.NextDouble() * 2));
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

                candidate.Plans[plan] = SweepOutcomes(random, 0.8 + (random.NextDouble() * 2.7), 0.3 + (random.NextDouble() * 4.2));
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
                var outcomes = SweepOutcomes(random, 2.0, 1.5, everyExitWins: true);

                // Each trade ends on its own session, so no session of the three blocks the next.
                Array.Fill(outcomes.Ends, (short)0);
                candidate.Plans[plan] = outcomes;
            }

            candidates.Add(candidate);
        }

        return [.. candidates.OrderBy(one => one.Session).ThenBy(one => one.Name)];
    }

    // One setting read in full, the rule inside: every pick it passes in name and session order, kept unless the
    // name's kept trade under the exit is still open, its figures summed and the nights it lists counted.
    static SweepMeasures SweepDirectly(IReadOnlyList<SweepPick> picks, DialSetting setting, ConditionSetting conditions, int exit, int nights, HashSet<(int Name, int Session)>? kept = null)
    {
        var sum = new float[SweepFigures.Width];
        var listed = new HashSet<int>();
        var openUntil = new Dictionary<int, int>();

        foreach (var pick in picks.OrderBy(pick => pick.Name).ThenBy(pick => pick.Session))
        {
            if (!pick.Corner.Passes(setting) || !conditions.Passes(pick.Readings))
            {
                continue;
            }

            if (openUntil.TryGetValue(pick.Name, out var until) && pick.Session <= until)
            {
                SweepFigures.TallyBlocked(sum, pick.Year);

                continue;
            }

            SweepFigures.Tally(sum, pick.Year, pick.Block, pick.Plan.Code[exit], pick.Plan.Null[exit], pick.Plan.BreakEven[exit], pick.Plan.Multiple[exit], pick.Plan.EdgeAt(exit));
            listed.Add(pick.Session);
            kept?.Add((pick.Name, pick.Session));
            openUntil[pick.Name] = pick.Session + pick.Plan.Ends[exit];
        }

        return SweepMeasures.Of(sum, listed.Count, nights);
    }

    static readonly SweepDesign[] SweepDesigns =
    [
        SweepDesign.Live,
        SweepDesign.Live with { Strength = StrengthMeasure.TwelveLessOne, Uptrend = UptrendRule.RisingTwoHundred, Support = SupportKind.Average, Plan = PlanRule.NearestBands, Hold = 10, BreakEven = true },
        SweepDesign.Live with { ReferenceHigh = 50, Trigger = TriggerKind.TopQuarterOfRange, Support = SupportKind.AnyBand, Plan = PlanRule.Ladder, EarningsWindow = 0, Hold = 40 },
    ];

    // A few settings across the extended grid, the loosest, the live rule's and some tight ones, with the
    // conditions off and on.
    static IEnumerable<(DialSetting Setting, ConditionSetting Conditions)> SweepSettings()
    {
        var grid = SweepGrid.Extended;

        yield return (new DialSetting(0, 0, grid.DepthHighs.Count - 1, grid.DryUpCeilings.Count - 1, grid.Freshness.Count - 1, 0, 0, 0, 0), ConditionSetting.Off);
        yield return (grid.Carry(SweepGrid.Fine, DialSetting.LiveOnFine), ConditionSetting.Off);
        yield return (grid.Carry(SweepGrid.Coarse, DialSetting.LiveOnCoarse), SweepConditions.OnAtTheMiddle(1));
        yield return (new DialSetting(4, 1, 3, 3, 2, 3, 1, 2, 1), SweepConditions.Join(SweepConditions.OnAtTheMiddle(3), SweepConditions.OnAtTheMiddle(7)));
        yield return (new DialSetting(2, 2, 4, 4, 3, 2, 3, 0, 2), SweepConditions.Join(SweepConditions.OnAtTheMiddle(4), SweepConditions.OnAtTheMiddle(5)));
    }

    [Fact]
    public void TheWalkKeepsOneOpenTradeAStockExactlyAsThePagesRuleDoesOverTheSameListings()
    {
        // Forty names over 120 weekday sessions of 2025, each listed on a third of them, every trade's end drawn
        // from one to eleven sessions on. The sweep's walk keeps a listing unless the name's kept trade is still
        // open on its session; the pages' rule reads the same listings as trades with their outcomes decided the
        // session the walk ends them on. The kept sets are one set.
        var random = new Random(20260930);
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2025, 3, 3); sessions.Count < 120; day = day.AddDays(1))
        {
            if (Core.Bars.ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        var picks = new List<SweepPick>();
        var listings = new List<OpenTradeListing>();
        var corner = SweepCorner.Of(SweepGrid.Extended, 0.9, 1.5, 0.9, 0, 2.0, 1.5, 0.6, 5)!.Value;
        var exit = SweepDesign.Live.ExitIndex;

        for (var name = 0; name < 40; name++)
        {
            for (var session = 0; session < sessions.Count; session++)
            {
                if (random.NextDouble() > 0.33)
                {
                    continue;
                }

                var plan = SweepOutcomes(random, 2.0, 1.5, everyExitWins: true);

                picks.Add(new SweepPick(picks.Count, name, session, corner, plan, 0, session / 63, default));

                var ends = Math.Min(sessions.Count - 1, session + plan.Ends[exit]);

                listings.Add(new OpenTradeListing(FormattableString.Invariant($"N{name:00}"), sessions[session], true, "win", sessions[ends], ForwardReturnSeries.SetupSessionCap));
            }
        }

        Assert.True(picks.Count > 1000, $"{picks.Count} listings were constructed, expected over a thousand.");

        var kept = new HashSet<(int Name, int Session)>();
        var measures = SweepStages.Measures([.. picks.OrderBy(pick => pick.Name).ThenBy(pick => pick.Session)], SweepDesign.Live, SweepGrid.Extended.Carry(SweepGrid.Fine, DialSetting.LiveOnFine), ConditionSetting.Off, sessions.Count, kept);
        var verdicts = OpenTrades.Walk(listings);
        var theirs = verdicts.Where(pair => pair.Value is null).Select(pair => (int.Parse(pair.Key.Ticker[1..], System.Globalization.CultureInfo.InvariantCulture), sessions.IndexOf(pair.Key.Night))).ToHashSet();

        Assert.Equal(theirs.OrderBy(pair => pair).ToArray(), kept.OrderBy(pair => pair).ToArray());
        Assert.Equal(kept.Count, measures.Listed);
        Assert.Equal(picks.Count - kept.Count, measures.Blocked);
        Assert.True(measures.Blocked > 100, $"{measures.Blocked} listings were kept off, expected over a hundred.");

        // The boundary, by hand: a trade listed on session 10 that ends on session 15 keeps a listing on 15 off
        // and lets one on 16 through, and a listing on 15 kept off never blocks 16 itself.
        var plan15 = SweepOutcomes(random, 2.0, 1.5, everyExitWins: true);
        var plan16 = SweepOutcomes(random, 2.0, 1.5, everyExitWins: true);
        var plan10 = SweepOutcomes(random, 2.0, 1.5, everyExitWins: true);

        Array.Fill(plan10.Ends, (short)5);
        Array.Fill(plan15.Ends, (short)40);
        Array.Fill(plan16.Ends, (short)1);

        var three = new HashSet<(int Name, int Session)>();
        var boundary = SweepStages.Measures(
            [new SweepPick(0, 1, 10, corner, plan10, 0, 0, default), new SweepPick(1, 1, 15, corner, plan15, 0, 0, default), new SweepPick(2, 1, 16, corner, plan16, 0, 0, default)],
            SweepDesign.Live,
            SweepGrid.Extended.Carry(SweepGrid.Fine, DialSetting.LiveOnFine),
            ConditionSetting.Off,
            sessions.Count,
            three);

        Assert.Equal([(1, 10), (1, 16)], three.OrderBy(pair => pair));
        Assert.Equal(1, boundary.Blocked);
    }

    [Fact]
    public void EverySettingsSummaryIsTheRecordReadingItInFullGivesWithTheRuleInside()
    {
        var candidates = SweepConstructed(SweepCount, 20260929);

        foreach (var design in SweepDesigns)
        {
            var picks = SweepStages.Picks(candidates, design);
            var tally = SweepStages.Tally.OneExit(design.ExitIndex, SweepNights);

            Assert.True(picks.Count >= 300, $"{design.Key} picked {picks.Count} of {candidates.Count} candidates, expected at least 300.");
            Assert.Equal(picks, picks.OrderBy(pick => pick.Name).ThenBy(pick => pick.Session));

            foreach (var (setting, conditions) in SweepSettings())
            {
                var direct = SweepDirectly(picks, setting, conditions, design.ExitIndex, SweepNights);
                var summary = SweepStages.Summary(picks, setting, conditions, SweepNights, tally);
                var kept = new HashSet<(int, int)>();
                var measures = SweepStages.Measures(picks, design, setting, conditions, SweepNights, kept);

                Assert.Equal(direct.Scored, summary.Scored);
                Assert.Equal((float)(direct.Share ?? double.NaN), summary.Share);
                Assert.Equal((float)(direct.BreakEven ?? double.NaN), summary.BreakEven);
                Assert.Equal((float)(direct.NoSkill ?? double.NaN), summary.NoSkill);
                Assert.Equal((float)(direct.AverageMultiple ?? double.NaN), summary.AverageMultiple);
                Assert.Equal((float)(direct.Edge ?? double.NaN), summary.Edge);
                Assert.Equal(direct.YearsBeatingBoth, summary.YearsBeatingBoth);
                Assert.Equal(direct.YearsBeatingBreakEven, summary.YearsBeatingBreakEven);
                Assert.Equal(direct.BeatsBreakEvenWithoutItsBestYear, summary.WithoutBestYear);
                Assert.Equal(direct.BlocksWithTrades, summary.Blocks);
                Assert.Equal((float)direct.ListingShareOfNights, summary.Listing);
                Assert.Equal(direct.MeetsTheFloors, summary.MeetsTheFloors);
                Assert.Equal(direct.Viable, summary.Viable);
                Assert.Equal(direct.Blocked, summary.Blocked);
                Assert.Equal(direct, measures, SweepMeasuresComparer.Instance);
                Assert.Equal(direct, SweepStages.Direct(candidates, design, setting, conditions, SweepNights), SweepMeasuresComparer.Instance);
                Assert.Equal(kept.Count, measures.Listed);
            }

            // Not a walk of nothing: the loosest setting lists a name on most nights and keeps some off.
            var loosest = SweepDirectly(picks, SweepSettings().First().Setting, ConditionSetting.Off, design.ExitIndex, SweepNights);

            Assert.True(loosest.NightsListing > SweepNights / 2, $"{design.Key}: the loosest setting lists a name on {loosest.NightsListing} of {SweepNights} nights.");
            Assert.True(loosest.Blocked > 0, $"{design.Key}: the loosest setting kept no listing off.");
            Assert.NotNull(loosest.Edge);
        }
    }

    [Fact]
    public void StageOnesViableCountAndMedianEdgeAreTheOnesReadingEachCoarseSettingDirectlyGives()
    {
        var candidates = SweepConstructed(SweepCount, 20260929);
        var selection = SweepDesign.Live.Selection;
        var ranks = SweepStages.Rank(candidates, selection, SweepNights);
        var picks = SweepStages.Picks(candidates, selection);

        Assert.Equal(SweepAxes.Exits, ranks.Count);

        foreach (var exit in new[] { 0, SweepAxes.ExitIndex(63, false), SweepAxes.Exits - 1 })
        {
            var viable = 0;
            var edges = new List<double>();
            SweepMeasures? live = null;

            for (var index = 0; index < SweepGrid.Coarse.Variations; index++)
            {
                var coarse = DialSetting.Of(SweepGrid.Coarse, index / SweepGrid.Coarse.CellsPerStop, index % SweepGrid.Coarse.CellsPerStop);
                var direct = SweepDirectly(picks, SweepGrid.Extended.Carry(SweepGrid.Coarse, coarse), ConditionSetting.Off, exit, SweepNights);

                if (direct.Viable)
                {
                    viable++;

                    // The rank reads each setting's edge as the single it summarises it to.
                    if (direct.Edge is { } edge)
                    {
                        edges.Add((float)edge);
                    }
                }

                if (coarse == DialSetting.LiveOnCoarse)
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
            Assert.Equal(SweepStages.Median(edges), rank.MedianEdge);

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

        // The coarse settings carried onto the extended grid read the same values.
        Assert.Equal(SweepGrid.Coarse.Variations, SweepStages.CoarseOnExtended.Count);
        Assert.Equal(DialSetting.LiveOnCoarse.Describe(SweepGrid.Coarse), SweepGrid.Extended.Carry(SweepGrid.Coarse, DialSetting.LiveOnCoarse).Describe(SweepGrid.Extended));
    }

    [Fact]
    public void ATriggerFiredOnASessionWhosePullbackFailsADepthStillArrivesOnTheNightThatPasses()
    {
        // The pulled name: 0.8, 0.9 and 1.2 typical moves deep on sessions 100, 101 and 102, its trigger first
        // fired on 100. Worked by hand on the extended grid, the depth's low edge at 1.0 or 0.5 and the window at
        // 3 or 1 sessions, every other dial at its loosest:
        //   low edge 1.0, window 3: 102 alone, the trigger two sessions back inside the window of three;
        //   low edge 1.0, window 1: none, 102's arrival being two sessions back;
        //   low edge 0.5, window 1: 100 alone, the night it fired;
        //   low edge 0.5, window 3: all three, each trade ending on its own session so none blocks the next.
        var candidates = SweepConstructed(SweepCount, 20260929);
        var grid = SweepGrid.Extended;
        var loosest = new DialSetting(0, 0, grid.DepthHighs.Count - 1, grid.DryUpCeilings.Count - 1, 0, 0, 0, 0, 0);
        var one = SweepGrid.IndexOf(grid.DepthLows, 1.0);
        var half = SweepGrid.IndexOf(grid.DepthLows, 0.5);
        var three = SweepGrid.IndexOf(grid.Freshness, 3);
        var tonight = SweepGrid.IndexOf(grid.Freshness, 1);
        var cases = new (DialSetting Setting, int[] Listed)[]
        {
            (loosest with { DepthLow = one, Freshness = three }, [102]),
            (loosest with { DepthLow = one, Freshness = tonight }, []),
            (loosest with { DepthLow = half, Freshness = tonight }, [100]),
            (loosest with { DepthLow = half, Freshness = three }, [100, 101, 102]),
        };

        foreach (var design in SweepDesigns)
        {
            foreach (var (setting, listed) in cases)
            {
                var picked = new HashSet<(int Name, int Session)>();

                SweepStages.Direct(candidates, design, setting, ConditionSetting.Off, SweepNights, picked);

                Assert.Equal(listed, picked.Where(pick => pick.Name == SweepPulled).Select(pick => pick.Session).Order());
            }
        }
    }

    [Fact]
    public void TheBenchmarkIsTheSamePlanEnteredAtTheCloseOnEveryMemberWorkedByHand()
    {
        // Three members on one session, each with a typical move of 2 and a close of 100, the plan stopping 1.5
        // moves below and rewarding 2 to 1: entry 100, stop 97, target 106. Member A closes 103, 106 and wins at
        // 6 over 3, a result of 2; member B closes 98, 96 and loses at 96, minus 4 over 3; member C drifts 101,
        // 102, 103 to a hold of 10 and beyond, unresolved at 103 after 10 sessions, a result of 1 at every hold
        // its series reaches, and none where it does not. The benchmark at a hold of 10 is (2 - 4/3 + 1) / 3.
        var closes = new[]
        {
            new[] { 100d, 103, 106, 107, 108, 109, 110, 111, 112, 113, 114 },
            [100d, 98, 96, 95, 94, 93, 92, 91, 90, 89, 88],
            [100d, 101, 102, 103, 103, 103, 103, 103, 103, 103, 103],
        };
        Span<double> outcome = stackalloc double[SweepAxes.Holds.Count];
        Span<bool> has = stackalloc bool[SweepAxes.Holds.Count];

        SweepBenchmark.Walk(closes[0], 0, 100, 97, 106, 3, false, outcome, has);
        Assert.Equal([true, true, true, true], has.ToArray());
        Assert.Equal(2.0, outcome[0], 9);

        SweepBenchmark.Walk(closes[1], 0, 100, 97, 106, 3, false, outcome, has);
        Assert.Equal(-4.0 / 3, outcome[0], 9);

        SweepBenchmark.Walk(closes[2], 0, 100, 97, 106, 3, false, outcome, has);
        Assert.Equal([true, false, false, false], has.ToArray());
        Assert.Equal(1.0, outcome[0], 9);

        // The move to break-even: member B's stop moves to 100 once a close stands 3 above it, which it never
        // does; member C's would, at 103, but the close before the move never falls back to 100, so the two
        // exits agree on every member here.
        SweepBenchmark.Walk(closes[2], 0, 100, 97, 106, 3, true, outcome, has);
        Assert.Equal(1.0, outcome[0], 9);

        // A member whose close falls back through the moved stop: 104 moves it to 100, 99 stops it there, a
        // result of minus a third, where the fixed stop rides to the hold at 99.
        var back = new[] { 100d, 104, 99, 99, 99, 99, 99, 99, 99, 99, 99 };

        SweepBenchmark.Walk(back, 0, 100, 97, 106, 3, true, outcome, has);
        Assert.Equal(-1.0 / 3, outcome[0], 9);
        SweepBenchmark.Walk(back, 0, 100, 97, 106, 3, false, outcome, has);
        Assert.Equal(-1.0 / 3, outcome[0], 9);
        Assert.Equal([true, false, false, false], has.ToArray());

        // Over the members, through the candidate: the plan's benchmark under the hold of 10 is the mean.
        var series = closes.Select((member, at) => new SweepSeries
        {
            Name = new SweepName(FormattableString.Invariant($"M{at}"), [], [(null, null)], []),
            Bars = [.. member.Select((close, session) => new SweepBar(new DateOnly(2025, 1, 2).AddDays(session), (decimal)close + 1, (decimal)close - 1, (decimal)close, 1000))],
            SessionAt = [.. Enumerable.Range(0, member.Length)],
            WindowStart = new int[member.Length],
            Sma20 = new double[member.Length],
            Sma50 = new double[member.Length],
            Sma200 = new double[member.Length],
            Atr = [.. Enumerable.Repeat(2d, member.Length)],
            Rsi = new double[member.Length],
            Volume50 = new double[member.Length],
            Swings = [],
            Confirmed = new int[member.Length],
            Member = [.. Enumerable.Repeat(true, member.Length)],
            Gap = new bool[member.Length],
            Label = new string[member.Length],
            Uptrend = new byte[member.Length],
            Return63 = new double[member.Length],
            Return126 = new double[member.Length],
            ReturnTwelveLessOne = new double[member.Length],
            Depth = new double[member.Length * 3],
            DryUp = new double[member.Length * 3],
            High252 = new double[member.Length],
            Tightness = new double[member.Length],
            VolumeRatio = new double[member.Length],
            RsiUp = new bool[member.Length],
            SinceHigh = new int[member.Length * 3],
            GapDown = new double[member.Length * 3],
            RsiLow = new double[member.Length * 3],
            SurpriseBar = [],
            NewestSurprise = new int[member.Length],
        }).ToArray();
        var candidate = new SweepCandidate { Name = 0, Session = 0 };

        candidate.Plans[4] = new SweepPlanOutcomes { RewardToRisk = 2.0, StopMoves = 1.5 };

        SweepBenchmark.Fill([candidate], series, SweepBenchmark.On(series, 11), 1);

        var ten = SweepAxes.ExitIndex(10, false);
        var sixtyThree = SweepAxes.ExitIndex(63, false);

        Assert.Equal((2.0 - (4.0 / 3) + 1.0) / 3, candidate.Plans[4]!.Benchmark[ten], 5);
        // At 63 sessions member C's series runs out, so the mean is over A and B alone.
        Assert.Equal((2.0 - (4.0 / 3)) / 2, candidate.Plans[4]!.Benchmark[sixtyThree], 5);

        // The edge is the trade's result less the benchmark, and none where either is missing.
        candidate.Plans[4]!.Multiple[ten] = 1.5f;
        Assert.Equal(1.5 - ((2.0 - (4.0 / 3) + 1.0) / 3), candidate.Plans[4]!.EdgeAt(ten), 5);
        Assert.True(float.IsNaN(candidate.Plans[4]!.EdgeAt(sixtyThree)));
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

        // Stage 1 reads 19,683 coarse settings a design, the fine grid holds 2,016,000, and the extended grid
        // holds every fine value with the values beyond each end the rule allows.
        Assert.Equal(19_683, SweepGrid.Coarse.Variations);
        Assert.Equal(2_016_000, SweepGrid.Fine.Variations);

        foreach (var value in SweepGrid.Fine.StrengthBars) Assert.Contains(value, SweepGrid.Extended.StrengthBars);
        foreach (var value in SweepGrid.Fine.RewardToRiskFloors) Assert.Contains(value, SweepGrid.Extended.RewardToRiskFloors);
        foreach (var value in SweepGrid.Fine.Freshness) Assert.Contains(value, SweepGrid.Extended.Freshness);
        Assert.Equal([0.95, 1.0], SweepGrid.Extended.StrengthBars.TakeLast(2));
        Assert.Equal(14, SweepGrid.Extended.Freshness[^1]);
        Assert.Equal(SweepColumns.LongestWindow, SweepGrid.Extended.Freshness[^1]);

        // The live rule's settings on every grid, read as the live filter's own values.
        Assert.Equal("strength 0.5, depth 1 to 5, dry-up under 1.5, fresh within 3, reward to risk 1.5, stop 0.5 to 4, market 45%, band strength 0", DialSetting.LiveOnCoarse.Describe(SweepGrid.Coarse));
        Assert.Equal(DialSetting.LiveOnCoarse.Describe(SweepGrid.Coarse), DialSetting.LiveOnFine.Describe(SweepGrid.Fine));
        Assert.Equal(DialSetting.LiveOnCoarse.Describe(SweepGrid.Coarse), SweepGrid.Extended.Carry(SweepGrid.Fine, DialSetting.LiveOnFine).Describe(SweepGrid.Extended));

        // The 31 condition settings, in condition order.
        Assert.Equal(31, SweepConditions.Settings.Count);
        Assert.Equal([4, 4, 4, 4, 6, 3, 6], Enumerable.Range(1, 7).Select(condition => SweepConditions.Settings.Count(pair => pair.Condition == condition)));
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
                    && x.Share == y.Share && x.BreakEven == y.BreakEven && x.NoSkill == y.NoSkill && x.AverageMultiple == y.AverageMultiple && x.Edge == y.Edge
                    && x.YearScored.SequenceEqual(y.YearScored) && x.YearShare.SequenceEqual(y.YearShare)
                    && x.YearBreakEven.SequenceEqual(y.YearBreakEven) && x.YearNoSkill.SequenceEqual(y.YearNoSkill)
                    && x.YearAverageMultiple.SequenceEqual(y.YearAverageMultiple) && x.YearEdge.SequenceEqual(y.YearEdge)
                    && x.BlocksWithTrades == y.BlocksWithTrades && x.NightsListing == y.NightsListing && x.Nights == y.Nights && x.Blocked == y.Blocked;

        public int GetHashCode(SweepMeasures? obj) => obj?.Scored ?? 0;
    }
}
