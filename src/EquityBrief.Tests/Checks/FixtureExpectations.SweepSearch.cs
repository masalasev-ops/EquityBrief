using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// Stage 2's search over landscapes worked by hand: the space's points, depth measured one dial at a time with
// the ends the grid does not limit depth at, the leaders' order and the proposal's ties, the recent-years rule,
// the refinement stopping when no move is deeper, the look beyond a grid end, a dial's own end named as a
// limit, the sample covering every value of every dial, the slices holding every other dial, and the
// conditions' steps (b) and (c).
// see: A starting point is proposed from the deepest setting of a plateau on the edge and never its best variation, and nothing is registered before the operator approves it
public partial class FixtureExpectations
{
    // A summary meeting every floor at an edge, and one under the floors where the edge is under nought.
    static SweepSummary SweepAt(double edge) =>
        edge < 0
            ? new SweepSummary(100, 30, 40, 35, (float)edge, (float)edge, 2, 2, false, 10, 0.3f, 0)
            : new SweepSummary(400, 60, 40, 35, (float)edge, (float)edge, 7, 7, true, 25, 0.7f, 5);

    static SweepMeasures SweepMeasuresAt(double edge) => YearMeasures(SweepForty, [.. Enumerable.Repeat<double?>(edge, 8)]);

    static SweepDesignSearch SweepLandscape(SweepSpace space, Func<int[], double> edge, SweepDesign? design = null) =>
        new(design ?? SweepDesign.Live, 300, space, (point, _) => SweepAt(edge(point)), point => SweepMeasuresAt(edge(point)));

    static readonly SweepMeasures SweepLive = SweepMeasuresAt(0.2);

    [Fact]
    public void ASpacesPointsCarryTheDialsAndTheConditionsBothWays()
    {
        var space = SweepSpace.For([5, 1, 7]);

        // The ten grid dials and the conditions in order: the 52-week high, the beat's window and size, the
        // pullback's length and gap.
        Assert.Equal(15, space.Count);
        Assert.Equal([DialKind.High, DialKind.BeatWindow, DialKind.BeatSize, DialKind.Length, DialKind.Gap], space.Dials.Skip(10).Select(dial => dial.Kind));
        Assert.Equal([1, 5, 7], space.ConditionsOn);

        // The live point reads back to the live settings and every condition off.
        var live = space.LivePoint();

        Assert.Equal(DialSetting.LiveOnFine.Describe(SweepGrid.Fine), space.Setting(live).Describe(space.Grid));
        Assert.True(space.Conditions(live).IsOff);
        Assert.Equal("strength 0.5, depth low 1, depth high 5, dry-up 1.5, freshness 3, reward to risk 1.5, nearest stop 0.5, farthest stop 4, market 45%, band strength 0, 52-week high off, beat window off, beat size any, pullback length off, gap inside off", space.Describe(live));

        // A setting with conditions on carries onto a point and back.
        var conditions = SweepConditions.Join(SweepConditions.OnAtTheMiddle(5), SweepConditions.OnAtTheMiddle(7));
        var setting = new DialSetting(3, 2, 4, 6, 1, 5, 3, 0, 2);
        var point = space.Point(setting, conditions);

        Assert.Equal(setting, space.Setting(point));
        Assert.Equal(conditions, space.Conditions(point));
        Assert.Equal("an earnings beat of any size within 40 sessions, the pullback at most 15 sessions long, no gap down inside it larger than 1.5 typical moves", SweepConditions.Describe(space.Conditions(point)));

        // The stop's two dials: option (1, 4) is the nearest stop at 1 and the farthest at 4.
        Assert.Equal((1, 1), (point[(int)DialKind.StopLow], point[(int)DialKind.StopHigh]));

        // A condition the space does not hold cannot be switched on in it.
        Assert.Throws<InvalidOperationException>(() => space.Point(setting, SweepConditions.OnAtTheMiddle(2)));

        // The beat's size is no step while the beat's window is off, and a step once it is on.
        Assert.True(space.IsNoOp(live, space.IndexOf(DialKind.BeatSize)));
        Assert.False(space.IsNoOp(point, space.IndexOf(DialKind.BeatSize)));

        // The tested ranges are the fine grid's and the conditions' tested values, the grid holding more.
        Assert.Equal((2, 8), (space.Low(0), space.High(0)));
        Assert.Equal(11, space.Dials[0].Count);
        Assert.Equal((0, 4), (space.Low(space.IndexOf(DialKind.High)), space.High(space.IndexOf(DialKind.High))));
        Assert.Equal(6, space.Dials[space.IndexOf(DialKind.High)].Count);
        Assert.True(space.Dials[(int)DialKind.StopLow].Exempt);
        Assert.False(space.Dials[0].Exempt);

        // Keys round-trip.
        Assert.Equal(point, SweepSpace.Parse(SweepSpace.Key(point)));
    }

    [Fact]
    public void DepthIsTheFewestSingleStepsOnAnyDialBeforeLeavingThePlateauWithTheEndsTheGridDoesNotLimit()
    {
        var space = SweepSpace.For([5]);
        var live = space.LivePoint();

        // A flat landscape: every setting at an edge of 1. The line at 0.95 leaves every setting on the plateau,
        // so depth is bounded by the grid's ends alone. Beyond off, at either end of a two-value dial and at a
        // dial's own end the grid does not limit, so from the live point the nearest limiting end is the
        // strength's low end: two steps down to 0.30, beyond which 0.20 exists and is not opened.
        var flat = SweepLandscape(space, _ => 1.0);

        flat.SetLine(0.95f);

        var depth = flat.Depth(live);

        Assert.Equal((2, "strength", -1, true), (depth.Depth, space.Dials[depth.Dial].Name, depth.Direction, depth.AtAGridEnd));

        // A cliff: the depth high at its two lowest tested values falls under the line, so from the live point,
        // two steps above them, one step down stays on the plateau and the second leaves it: a depth of 1, not at
        // a grid end.
        var cliff = SweepLandscape(space, point => point[(int)DialKind.DepthHigh] <= 1 ? 0.5 : 1.0);

        cliff.SetLine(0.95f);

        var bounded = cliff.Depth(live);

        Assert.Equal((1, "depth high", -1, false), (bounded.Depth, space.Dials[bounded.Dial].Name, bounded.Direction, bounded.AtAGridEnd));

        // Beyond off the grid does not limit: a point at the market's off with every other dial deep inside is
        // bounded elsewhere, and the beat's size is no step while its window is off.
        var offMarket = (int[])live.Clone();

        offMarket[(int)DialKind.Market] = 0;

        var atOff = flat.Depth(offMarket);

        Assert.NotEqual("market", space.Dials[atOff.Dial].Name);
        Assert.NotEqual("beat size", space.Dials[atOff.Dial].Name);

        // A dial's own end: band strength 0 has nothing below it, so it bounds nothing; the freshness at 1 the
        // same.
        Assert.False(space.LimitsAt((int)DialKind.Band, -1));
        Assert.False(space.LimitsAt((int)DialKind.Freshness, -1));
        Assert.True(space.LimitsAt((int)DialKind.Strength, -1));
        Assert.True(space.AtTheRulesEnd(live, (int)DialKind.Band, -1));
        Assert.False(space.AtTheRulesEnd(live, (int)DialKind.Market, -1));

        // Depth is counted to at most six steps.
        Assert.Equal(6, SweepSearch.MostDepth);
    }

    [Fact]
    public void TheLeadersAreOrderedByEdgeAndTheProposalIsTheDeepestThatDoesNotTrailTheLiveRule()
    {
        var space = SweepSpace.For([]);
        var live = space.LivePoint();

        // A landscape falling by three hundredths a step from a peak P two steps above the live point on the
        // reward to risk, at 2.5, a coarse value, with the market at its off, so P is among the coarse settings
        // evaluated. The line at the best less 0.05 holds every setting one step from P and none two steps
        // away, so P's depth is 1 on every dial, and each of its neighbours, one step from the plateau's edge,
        // has a depth of 0: P is the deepest leader and is proposed.
        var peak = (int[])live.Clone();

        peak[(int)DialKind.RewardToRisk] += 2;
        peak[(int)DialKind.Market] = 0;

        var search = SweepLandscape(space, point => 1.0 - (0.03 * SweepSpace.Distance(point, peak)));

        search.EvaluateCoarse(ConditionSetting.Off, 2);
        search.FixTheLine(SweepSearch.PlateauMargin);

        Assert.Equal(1.0f, search.BestEdge, 3);
        Assert.Equal(0.95f, search.Line, 3);

        var leaders = search.LeadersOf(5);

        Assert.Equal(peak, leaders[0]);
        Assert.True(leaders.Select(leader => search.Evaluate(leader).Edge).SequenceEqual(leaders.Select(leader => search.Evaluate(leader).Edge).OrderDescending()));

        var (proposal, trailing) = search.Propose(leaders, SweepLive);

        Assert.NotNull(proposal);
        Assert.Equal(0, trailing);
        Assert.Equal(peak, proposal.Point);
        Assert.Equal(1, proposal.Depth.Depth);
        Assert.All(leaders.Skip(1), leader => Assert.Equal(0, search.Depth(leader).Depth));

        // Every leader trailing the live rule in a recent year leaves no proposal, and each is counted.
        var (none, allTrailing) = search.Propose(leaders, SweepMeasuresAt(5.0));

        Assert.Null(none);
        Assert.Equal(leaders.Count, allTrailing);

        // The tie on depth and edge goes to the setting nearest the live rule.
        var twin = SweepLandscape(space, point => Math.Abs(point[(int)DialKind.RewardToRisk] - live[(int)DialKind.RewardToRisk]) <= 1 ? 1.0 : 0.5);

        twin.SetLine(0.95f);

        var up = (int[])live.Clone();
        var down = (int[])live.Clone();

        up[(int)DialKind.RewardToRisk]++;
        down[(int)DialKind.RewardToRisk]--;

        var (nearest, _) = twin.Propose([up, down, live], SweepLive);

        Assert.Equal(live, nearest!.Point);

        // The deepest leader is proposed over the highest edge: a spike S at 1.0 whose every neighbour is under
        // the line, a depth of 0, beside a broad region at 0.97 around the live point, one step deep; the live
        // point is proposed though S's edge is higher.
        var spike = (int[])live.Clone();

        spike[(int)DialKind.Strength] += 3;
        spike[(int)DialKind.Market] = 0;

        var spiked = SweepLandscape(space, point => point.SequenceEqual(spike) ? 1.0 : SweepSpace.Distance(point, live) <= 1 ? 0.97 : 0.5);

        spiked.SetLine(0.95f);

        var (deepest, _) = spiked.Propose([spike, live], SweepLive);

        Assert.Equal(0, spiked.Depth(spike).Depth);
        Assert.Equal(1, spiked.Depth(live).Depth);
        Assert.Equal(live, deepest!.Point);
    }

    [Fact]
    public void TheRefinementTakesTheDeepestMoveAndStopsWhenNoMoveIsDeeper()
    {
        var space = SweepSpace.For([]);
        var live = space.LivePoint();

        // Under the line at the reward to risk's four lowest values: at the live point, 1.5, one step down leaves
        // the plateau, a depth of 0. One step up, at 2, is one from the cliff and one from the high end's last
        // tested value's neighbour: a depth of 1. Two up, at 2.5, is one from the tested high end, beyond which
        // 3.5 exists: a depth of 1 too. The first round takes the one-step move, the deeper by no less and nearer
        // the live rule, and the second finds nothing deeper.
        var search = SweepLandscape(space, point => point[(int)DialKind.RewardToRisk] <= 3 ? 0.5 : 1.0);

        search.SetLine(0.95f);

        var start = new SweepProposal(live, search.Evaluate(live), search.Depth(live), search.Measures(live));

        Assert.Equal(0, start.Depth.Depth);

        var rounds = new List<string>();
        var refined = search.Refine(start, SweepLive, rounds);

        Assert.Equal(live[(int)DialKind.RewardToRisk] + 1, refined.Point[(int)DialKind.RewardToRisk]);
        Assert.Equal(1, refined.Depth.Depth);
        Assert.Equal(2, rounds.Count);
        Assert.StartsWith("round 1: the reward to risk from 1.5 to 2 deepens the plateau from 0 to 1", rounds[0], StringComparison.Ordinal);
        Assert.StartsWith("round 2: no one-step or two-step move is deeper than 1", rounds[1], StringComparison.Ordinal);

        // A move that trails the live rule in a recent year is not taken however deep.
        var trailing = search.Refine(start, SweepMeasuresAt(5.0), []);

        Assert.Equal(live, trailing.Point);
    }

    [Fact]
    public void AGridEndTheDepthRunsIntoIsLookedBeyondAndADialsOwnEndIsNamedAsALimit()
    {
        var space = SweepSpace.For([]);
        var live = space.LivePoint();
        var flat = SweepLandscape(space, _ => 1.0);

        flat.SetLine(0.95f);

        var start = new SweepProposal(live, flat.Evaluate(live), flat.Depth(live), flat.Measures(live));

        Assert.True(start.Depth.AtAGridEnd);
        Assert.Equal(2, start.Depth.Depth);

        var rounds = new List<string>();
        var extensions = new List<string>();
        var limits = new List<string>();
        var extended = flat.Extend(start, SweepLive, rounds, extensions, limits);

        // The strength's low end was looked beyond at 0.2 and 0.1, opening its low end whole, and every other
        // end the depth ran into after it in turn, the refinement moving the proposal between them, until nothing
        // the grid limits at bounds the depth: six.
        Assert.Contains("the strength dial looked beyond its low end at 0.2 and 0.1", extensions);
        Assert.Equal(0, space.Low((int)DialKind.Strength));
        Assert.True(extensions.Count > 1, "only one end was looked beyond.");
        Assert.Equal(SweepSearch.MostDepth, extended.Depth.Depth);
        Assert.False(extended.Depth.AtAGridEnd);

        // A proposal at a dial's own end is named as a limit, the market's off never: the live point sits at the
        // band strength's own low end, 0, and a proposal held there says so.
        var atZero = flat.Extend(new SweepProposal(live, flat.Evaluate(live), new SweepDepth(SweepSearch.MostDepth, -1, 0, false), flat.Measures(live)), SweepLive, [], [], limits = []);

        Assert.Equal(live, atZero.Point);
        Assert.Contains(limits, limit => limit.StartsWith("the proposal sits at the band strength dial's own low end, 0", StringComparison.Ordinal));
        Assert.DoesNotContain(limits, limit => limit.Contains("market", StringComparison.Ordinal));

        // A landscape whose plateau lies past the strength's tested high end: under the line at a strength of
        // 0.6 and below, on it above, so a proposal at the tested end, 0.85, is one step from the cliff below and
        // bounded at nought above by the grid end. The extension opens 0.95 and 1, and the proposal moves into
        // it, to 1, three steps from the cliff, which is the dial's own end and is named.
        var rising = SweepSpace.For([]);
        var climb = SweepLandscape(rising, point => point[(int)DialKind.Strength] <= 5 ? 0.5 : 1.0 - (0.001 * SweepSpace.Distance(point, live)));

        climb.SetLine(0.9f);

        var high = (int[])live.Clone();

        high[(int)DialKind.Strength] = 8;

        var atTheEnd = new SweepProposal(high, climb.Evaluate(high), climb.Depth(high), climb.Measures(high));

        Assert.Equal((0, true), (atTheEnd.Depth.Depth, atTheEnd.Depth.AtAGridEnd));

        var climbLimits = new List<string>();
        var moved = climb.Extend(atTheEnd, SweepLive, [], [], climbLimits);

        Assert.Equal(10, rising.High((int)DialKind.Strength));
        Assert.Equal(10, moved.Point[(int)DialKind.Strength]);
        Assert.Equal(4, moved.Depth.Depth);
        Assert.Contains(climbLimits, limit => limit.StartsWith("the proposal sits at the strength dial's own high end, 1,", StringComparison.Ordinal));
    }

    [Fact]
    public void TheSampleCoversEveryValueOfEveryDialEquallyAndTheSlicesHoldEveryOtherDialAtTheStartingPoint()
    {
        var space = SweepSpace.For([2, 6]);
        var search = SweepLandscape(space, _ => 1.0);
        var drawn = search.Draw(840, new Random(SweepSearch.Seed));

        Assert.Equal(840, drawn.Count);

        for (var dial = 0; dial < space.Count; dial++)
        {
            var size = space.High(dial) - space.Low(dial) + 1;
            var counts = drawn.GroupBy(point => point[dial]).ToDictionary(group => group.Key, group => group.Count());

            // 840 is a multiple of every dial's size, 4 to 7 and 2, so each value appears exactly equally often,
            // and every value inside the tested range appears.
            Assert.Equal(size, counts.Count);
            Assert.All(counts, pair => Assert.Equal(840 / size, pair.Value));
            Assert.All(counts.Keys, value => Assert.InRange(value, space.Low(dial), space.High(dial)));
        }

        // The same seed draws the same sample.
        Assert.Equal(drawn.Select(point => SweepSpace.Key(point)), search.Draw(840, new Random(SweepSearch.Seed)).Select(point => SweepSpace.Key(point)));

        // The sample's size is set from the measured time against the budget, never more than the grid.
        var (sampled, gridSize, perPoint) = search.EvaluateSample(TimeSpan.FromMilliseconds(1), SweepSearch.Seed, 2);

        Assert.True(sampled >= 1 && sampled <= gridSize, $"{sampled} sampled of {gridSize}.");
        Assert.True(perPoint >= 0);

        // The slices: every pair of dials, each cell the other dials held at the centre, reaching three either
        // side within the ranges.
        var live = space.LivePoint();
        var slices = search.Slices(live);

        Assert.Equal(space.Count * (space.Count - 1) / 2, slices.Count);

        var strengthByReward = slices.Single(slice => slice.RowDial == "strength" && slice.ColumnDial == "reward to risk");

        Assert.Equal(["0.3", "0.4", "0.5", "0.6", "0.67", "0.75"], strengthByReward.RowLabels);
        Assert.Equal(["1", "1.25", "1.5", "2", "2.5", "3"], strengthByReward.ColumnLabels);
        Assert.Equal((2, 2), (strengthByReward.Row, strengthByReward.Column));
        Assert.All(strengthByReward.Edge, row => Assert.All(row, edge => Assert.Equal(1.0f, edge)));
    }

    [Fact]
    public void ASettingSurvivesStepBOnSixOfTenDesignsWithNoBestSettingChosenAndStepCCarriesEverySurvivorOnAndOff()
    {
        var without = YearMeasures(SweepForty, [0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1]);

        SweepMeasures With(int yearsUp, int recentUp, int scored = 400)
        {
            var edges = new double?[8];

            for (var year = 0; year < 8; year++)
            {
                edges[year] = 0.1;
            }

            for (var up = 0; up < recentUp; up++)
            {
                edges[7 - up] = 0.2;
            }

            for (var year = 0; year < 8 && yearsUp - recentUp > 0; year++)
            {
                if (edges[year] == 0.1 && year < 5)
                {
                    edges[year] = 0.2;
                    yearsUp--;
                }
            }

            return YearMeasures([.. Enumerable.Repeat(scored / 8, 8)], edges);
        }

        // Kept on a design where the edge rises in 6 of 8 years, 2 of them recent, with 300 trades; not with 5
        // years, not with 1 recent, not with 299 trades.
        Assert.True(SweepSearch.Kept(With(6, 2), 6, 2));
        Assert.False(SweepSearch.Kept(With(5, 2), 5, 2));
        Assert.False(SweepSearch.Kept(With(6, 1), 6, 1));
        Assert.False(SweepSearch.Kept(With(6, 2, 299), 6, 2));
        Assert.Equal((6, 2), SweepSearch.YearsUp(without, With(6, 2)));

        // Ten designs: a setting kept on six survives its condition, one kept on five does not, and no best
        // setting is chosen: both settings of a condition go through as its dial.
        ConditionTrial Trial(int condition, string setting, int design, bool kept) =>
            new(condition, setting, setting, FormattableString.Invariant($"design {design}"), kept ? 6 : 3, kept ? 2 : 1, 400, 400, 0.2, 0.1, kept);

        var trials = new List<ConditionTrial>();

        for (var design = 0; design < 10; design++)
        {
            trials.Add(Trial(1, "0,-1,-1,-1,-1,0,-1,-1,-1", design, design < 6));
            trials.Add(Trial(1, "1,-1,-1,-1,-1,0,-1,-1,-1", design, design < 2));
            trials.Add(Trial(3, "-1,-1,0,-1,-1,0,-1,-1,-1", design, design < 5));
        }

        var verdicts = SweepSearch.Verdicts(trials);

        Assert.True(verdicts.Single(verdict => verdict.Condition == 1).Survives);
        Assert.Equal([6, 2], verdicts.Single(verdict => verdict.Condition == 1).Settings.Select(setting => setting.DesignsKept));
        Assert.False(verdicts.Single(verdict => verdict.Condition == 3).Survives);
        Assert.Equal(7, verdicts.Count);

        // Step (c) crosses every survivor on and off at the middle of its tested settings: four survivors give
        // sixteen combinations, every survivor carried and not the strongest three, the beat at a 40-session
        // window of any size and the shape at 15 sessions with a 1.5-move gap.
        var combinations = SweepSearch.Combinations([1, 3, 5, 7]);

        Assert.Equal(16, combinations.Count);
        Assert.Contains(ConditionSetting.Off, combinations);
        Assert.Contains(SweepConditions.Join(SweepConditions.Join(SweepConditions.Join(SweepConditions.OnAtTheMiddle(1), SweepConditions.OnAtTheMiddle(3)), SweepConditions.OnAtTheMiddle(5)), SweepConditions.OnAtTheMiddle(7)), combinations);
        Assert.Equal(16, combinations.Distinct().Count());
        Assert.Equal("an earnings beat of any size within 40 sessions", SweepConditions.Describe(SweepConditions.OnAtTheMiddle(5)));
        Assert.Equal("the pullback at most 15 sessions long, no gap down inside it larger than 1.5 typical moves", SweepConditions.Describe(SweepConditions.OnAtTheMiddle(7)));
        Assert.Equal(1, SweepConditions.Middle(4));
        Assert.Equal(1, SweepConditions.Middle(3));

        // Every survivor enters the space as a dial, off at one end.
        var space = SweepSpace.For([1, 5, 7]);

        Assert.Equal([DialKind.High, DialKind.BeatWindow, DialKind.BeatSize, DialKind.Length, DialKind.Gap], space.Dials.Skip(10).Select(dial => dial.Kind));
        Assert.All(space.Dials.Skip(10).Where(dial => dial.Kind != DialKind.BeatSize), dial => Assert.Equal(0, dial.Off));
    }
}
