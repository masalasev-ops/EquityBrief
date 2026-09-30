using System.Text.Json;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The starting point across the designs and the variants: edge first and then depth across the five, the
// year-by-year floor refusing a record one year carries, each of the four variant tests refusing on its own on
// the edge, the strongest distinct designs ranked on the edge with too few viable settings ranking below, and
// the report saying what the ruling asks it to.
public partial class FixtureExpectations
{
    static SweepMeasures YearMeasures(int[] scored, double?[] edges, double?[]? shares = null, double?[]? breakEvens = null, int blocked = 0) =>
        new(
            scored.Sum(),
            scored.Sum(),
            scored.Sum(),
            scored.Sum() / 2,
            50,
            40,
            35,
            edges.Average(),
            edges.Average(),
            scored,
            shares ?? [.. scored.Select(_ => (double?)50)],
            breakEvens ?? [.. scored.Select(_ => (double?)40)],
            [.. scored.Select(_ => (double?)35)],
            edges,
            edges,
            30,
            200,
            300,
            blocked);

    static readonly int[] SweepForty = [40, 40, 40, 40, 40, 40, 40, 40];

    [Fact]
    public void EachOfTheFourVariantTestsRefusesOnItsOwnOnTheEdge()
    {
        var start = YearMeasures(SweepForty, [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]);
        var startPicks = Enumerable.Range(0, 10).Select(at => (at, 100)).ToHashSet();
        var point = SweepSpace.For([]).LivePoint();

        // Six of ten picks shared, four the starting point does not pick: a share of 0.4.
        var picks = Enumerable.Range(4, 10).Select(at => (at, 100)).ToHashSet();
        var mixed = YearMeasures(SweepForty, [0.6, 0.4, 0.6, 0.4, 0.6, 0.4, 0.6, 0.4]);

        SweepVariant Judge(SweepMeasures measures, IReadOnlySet<(int, int)> own, float line = 0.4f) =>
            SweepPlateau.Judge("strength", "the strength bar", SweepDesign.Live, point, measures, own, start, startPicks, line);

        var passing = Judge(mixed, picks);

        Assert.True(passing.Passes);
        Assert.Equal((4, 4), (passing.YearsItWins, passing.YearsTheStartWins));
        Assert.Equal(0.4, passing.OutsideShare, 9);
        Assert.Equal(40, passing.FewestTradesInAYear);
        Assert.Empty(passing.Failing(0.4f));

        // One change reaching a setting under the line, off the plateau, and one exactly on the line passes.
        var off = Judge(mixed, picks, line: 0.51f);

        Assert.Equal((false, true, true, true), (off.OnThePlateau, off.Open, off.DifferentEnough, off.EnoughTrades));
        Assert.Contains(off.Failing(0.51f), reason => reason.StartsWith("off the plateau", StringComparison.Ordinal));
        Assert.True(Judge(mixed, picks, line: 0.5f).OnThePlateau);

        // Settled by history: six of the eight years one way on the edge, and five open.
        var settled = Judge(YearMeasures(SweepForty, [0.6, 0.6, 0.6, 0.6, 0.6, 0.6, 0.4, 0.4]), picks);

        Assert.Equal((true, false, true, true), (settled.OnThePlateau, settled.Open, settled.DifferentEnough, settled.EnoughTrades));
        Assert.True(Judge(YearMeasures(SweepForty, [0.6, 0.6, 0.6, 0.6, 0.6, 0.4, 0.4, 0.4]), picks).Open);

        // Too like the starting point: two of ten picks its own, under a quarter; one of four, a quarter exactly,
        // passes.
        var alike = Judge(mixed, Enumerable.Range(2, 10).Select(at => (at, 100)).ToHashSet());

        Assert.Equal((true, true, false, true), (alike.OnThePlateau, alike.Open, alike.DifferentEnough, alike.EnoughTrades));
        Assert.True(Judge(mixed, Enumerable.Range(7, 4).Select(at => (at, 100)).ToHashSet()).DifferentEnough);

        // Too few trades in a year: 29 in one, and 30 passes.
        var thin = Judge(YearMeasures([40, 40, 40, 29, 40, 40, 40, 40], [0.6, 0.4, 0.6, 0.4, 0.6, 0.4, 0.6, 0.4]), picks);

        Assert.Equal((true, true, true, false), (thin.OnThePlateau, thin.Open, thin.DifferentEnough, thin.EnoughTrades));
        Assert.True(Judge(YearMeasures([40, 40, 40, 30, 40, 40, 40, 40], [0.6, 0.4, 0.6, 0.4, 0.6, 0.4, 0.6, 0.4]), picks).EnoughTrades);
        Assert.Contains("too few trades, 29 in its thinnest year", string.Join("; ", thin.Failing(0.4f)), StringComparison.Ordinal);

        // The structural neighbours of a design: 23 for the live design, the exit's hold and its move among
        // them, each one change.
        var neighbours = SweepPlateau.StructuralNeighbours(SweepDesign.Live);

        Assert.Equal(23, neighbours.Count);
        Assert.All(neighbours, neighbour => Assert.NotNull(SweepPlateau.StructuralDifference(SweepDesign.Live, neighbour)));
        Assert.Contains(SweepDesign.Live with { BreakEven = true }, neighbours);
        Assert.Contains(SweepDesign.Live with { Hold = 10 }, neighbours);
        Assert.Equal(19, SweepSearch.SelectionNeighbours(SweepDesign.Live.Selection).Count);
    }

    [Fact]
    public void TheVariantsMoveEachDialOneAndTwoStepsEitherWayAndChangeEachStructuralChoiceOnce()
    {
        // Over constructed candidates, from the live rule's point with the plateau's line far under every
        // edge so the plateau test refuses nothing: the strength, four steps inside its tested range either
        // way, gives four variants, one and two steps each way; the band strength at its own low end gives two,
        // up alone, as the beat's window at off does; the beat's size is no move while its window is off; and
        // each of the 23 structural neighbours is one variant.
        var candidates = SweepConstructed(SweepCount, 20260929);
        var space = SweepSpace.For([5]);
        var live = space.LivePoint();
        var start = new SweepStart(SweepDesign.Live, space, live, new SweepDepth(2, 0, -1, true), SweepStages.Direct(candidates, SweepDesign.Live, space.Setting(live), ConditionSetting.Off, SweepNights), -10f);
        var variants = SweepPlateau.Variants(candidates, start, SweepNights);

        Assert.Equal(4, variants.Count(variant => variant.Change.StartsWith("the strength from", StringComparison.Ordinal)));
        Assert.Contains(variants, variant => variant.Change == "the strength from 0.5 to 0.67");
        Assert.Contains(variants, variant => variant.Change == "the strength from 0.5 to 0.3");
        Assert.Equal(2, variants.Count(variant => variant.Change.StartsWith("the band strength from", StringComparison.Ordinal)));
        Assert.DoesNotContain(variants, variant => variant.Change.StartsWith("the beat size from", StringComparison.Ordinal));
        Assert.Equal(2, variants.Count(variant => variant.Change.StartsWith("the beat window from", StringComparison.Ordinal)));
        Assert.Contains(variants, variant => variant.Change == "the beat window from off to 40");
        Assert.Equal(23, variants.Count(variant => variant.Design != SweepDesign.Live));
        Assert.All(variants, variant => Assert.True(variant.Measures.Edge is { } edge && edge >= -10));

        // The passing variants come first, at most six, and every tested one follows with what it fails.
        var passing = variants.TakeWhile(variant => variant.Passes).Count();

        Assert.True(passing <= SweepPlateau.MostVariants);
        Assert.Equal(passing, variants.Count(variant => variant.Passes));
    }

    [Fact]
    public void ARecordOneYearCarriesIsRefusedAndOneSpreadOverTheYearsIsNot()
    {
        // Eight years of 40 trades against a break-even of 40%: one year won at 90% and the others at 38%
        // clears the break-even over the whole, 44.5%, and not once the best year is removed, 38%. Spread at 45%
        // a year, removing any one leaves 45%.
        var carried = YearMeasures(SweepForty, [0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1], [90, 38, 38, 38, 38, 38, 38, 38]);
        var spread = YearMeasures(SweepForty, [0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1, 0.1], [45, 45, 45, 45, 45, 45, 45, 45]);

        Assert.Equal(1, carried.YearsBeatingBreakEven);
        Assert.False(carried.BeatsBreakEvenWithoutItsBestYear);
        Assert.False(carried.MeetsTheFloors);
        Assert.True(spread.BeatsBreakEvenWithoutItsBestYear);
        Assert.Equal(8, spread.YearsBeatingBreakEven);
        Assert.True(spread.MeetsTheFloors);

        // Six of the eight years is the floor and five is not.
        Assert.True(YearMeasures(SweepForty, [.. Enumerable.Repeat<double?>(0.1, 8)], [45, 45, 45, 45, 45, 45, 38, 38]).YearsBeatingBreakEven >= SweepMeasures.YearsBeating);
        Assert.False(YearMeasures(SweepForty, [.. Enumerable.Repeat<double?>(0.1, 8)], [45, 45, 45, 45, 45, 38, 38, 38]).MeetsTheFloors);

        // The recent-years rule: a record trails the live rule where its edge is under the live rule's in any of
        // 2024 to 2026, and a year either has no trade in is no trailing.
        var live = YearMeasures(SweepForty, [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]);

        Assert.False(YearMeasures(SweepForty, [0.1, 0.1, 0.1, 0.1, 0.1, 0.5, 0.6, 0.7]).TrailsInARecentYear(live));
        Assert.True(YearMeasures(SweepForty, [0.9, 0.9, 0.9, 0.9, 0.9, 0.9, 0.49, 0.9]).TrailsInARecentYear(live));
        Assert.False(YearMeasures(SweepForty, [0.9, 0.9, 0.9, 0.9, 0.9, 0.9, null, 0.9]).TrailsInARecentYear(live));
    }

    static SweepHistoryInputs SweepInputs(long indexNights, long withoutBar) =>
        new(new DateOnly(2026, 9, 25), [new DateOnly(2019, 1, 2)], [], indexNights, withoutBar, 3, 535, "constructed");

    static readonly PointInTimeResult SweepClean = new(200, 7, 200, [], 12.5);

    static string SweepReportOf(SweepHistoryInputs inputs, IReadOnlyList<RankRow> rows, IReadOnlyList<CombinationRow> carried, IReadOnlyList<SweepDesignResult>? results = null) =>
        SweepReport.Build(inputs, [], rows, [], [], [], carried, results ?? [], SweepClean, new SweepRunner.State(), 1, [new DateOnly(2019, 1, 2)], 0);

    static RankRow SweepRankRow(SweepDesign design, int viable, double? edge) =>
        new(design.Key, SweepGrid.Coarse.Variations, viable, (double)viable / SweepGrid.Coarse.Variations, edge, edge, null, null, null, null, null, null, null);

    static CombinationRow SweepCarried(SweepDesign design, int viable, double? edge) => new(design.Key, ConditionSetting.Off.Key, viable, edge, edge);

    [Fact]
    public void TheReportSaysPlainlyWhatTheMissingDeparturesMeanAboveThreePerCentAndNotAtIt()
    {
        const string Better = "the results read better than the market was";

        Assert.DoesNotContain(Better, SweepReportOf(SweepInputs(1_000, 30), [], []), StringComparison.Ordinal);
        Assert.Contains("3.00%", SweepReportOf(SweepInputs(1_000, 30), [], []), StringComparison.Ordinal);
        Assert.Contains(Better, SweepReportOf(SweepInputs(1_000, 31), [], []), StringComparison.Ordinal);
        Assert.Contains("3.10%", SweepReportOf(SweepInputs(1_000, 31), [], []), StringComparison.Ordinal);

        // The operator's store's own count, 2,580 of 1,107,914, is stated as it is and under the threshold.
        var measured = SweepReportOf(SweepInputs(1_107_914, 2_580), [], []);

        Assert.Contains("0.23%", measured, StringComparison.Ordinal);
        Assert.DoesNotContain(Better, measured, StringComparison.Ordinal);

        // The point-in-time result is stated with its counts, and the run's stages are named.
        Assert.Contains("200 of 200 name-sessions rebuilt", measured, StringComparison.Ordinal);
        Assert.Contains("No difference.", measured, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRankingMarksTheLiveRulesDesignWithItsRankAndTheReportNamesAStopNotAtABand()
    {
        var average = SweepDesign.Live with { Support = SupportKind.Average };
        var any = SweepDesign.Live with { Support = SupportKind.AnyBand };
        var rows = new[] { SweepRankRow(any, 900, 0.3), SweepRankRow(average, 800, 0.2), SweepRankRow(SweepDesign.Live, 700, 0.1), SweepRankRow(SweepDesign.Live with { Hold = 10 }, 10, 0.9) };

        // A design carried with a proposal on the average, and one without.
        var space = SweepSpace.For([]);
        var point = space.LivePoint();
        var measures = YearMeasures(SweepForty, [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]);
        var proposal = new SweepProposal(point, new SweepSummary(320, 50, 40, 35, 0.5f, 0.5f, 7, 7, true, 25, 0.7f, 3), new SweepDepth(2, 0, -1, true), measures);

        SweepDesignResult Result(SweepDesign design) => new(design.Key, ConditionSetting.Off.Key, [], 2_016_000, 1_000, 0.001, 1_500, 0.55f, 0.5f, 100, 0, proposal, [], [], [], [], [], []);

        var withAverage = SweepReportOf(SweepInputs(1_000, 0), rows, [SweepCarried(any, 900, 0.3), SweepCarried(average, 800, 0.2)], [Result(any), Result(average)]);
        var liveRow = System.Text.RegularExpressions.Regex.Match(withAverage, "<tr class=\"live\"><td class=\"num\">.*?</tr>").Value;

        // The design with 10 viable settings ranks last however high its edge, so the live design ranks 3 of 4.
        Assert.Contains("ranks 3 of 4", withAverage, StringComparison.Ordinal);
        Assert.Contains(System.Net.WebUtility.HtmlEncode("the live rule's design"), liveRow, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">3</td>", liveRow, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(withAverage, "carried to stage 2").Count);
        Assert.Contains("section 10 states as a principle", withAverage, StringComparison.Ordinal);
        Assert.Contains("every figure on this page is history", withAverage, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("register --freeze", withAverage, StringComparison.Ordinal);

        var withoutAverage = SweepReportOf(SweepInputs(1_000, 0), rows, [SweepCarried(any, 900, 0.3), SweepCarried(SweepDesign.Live, 700, 0.1)]);

        Assert.DoesNotContain("section 10 states as a principle", withoutAverage, StringComparison.Ordinal);
        Assert.Contains("so every one of them stops at a band", withoutAverage, StringComparison.Ordinal);

        // With no starting point there is no registration to state, and none is run.
        Assert.Contains("No starting point is proposed", withoutAverage, StringComparison.Ordinal);
        Assert.DoesNotContain("register --freeze", withoutAverage, StringComparison.Ordinal);
    }

    [Fact]
    public void TheStrongestDesignsAreDistinctOnTheEdgeAndOneWithTooFewViableSettingsRanksBelowEveryOther()
    {
        // Eight designs: the first three tie on every figure, the live classifier's own among them standing for
        // the other two, then three that differ, then two tying again below them, and one with the highest
        // median edge of all but only 50 viable settings, which ranks last. The five strongest are the tie's
        // live classifier, the three that differ and the first of the lower tie.
        var below = SweepDesign.Live with { Uptrend = UptrendRule.ClassifierBelowBoth };
        var cross = SweepDesign.Live with { Uptrend = UptrendRule.ClassifierBelowBothUnderACross, Strength = StrengthMeasure.SixMonths };
        var any = SweepDesign.Live with { Support = SupportKind.AnyBand };
        var six = SweepDesign.Live with { Strength = StrengthMeasure.SixMonths };
        var fifty = SweepDesign.Live with { ReferenceHigh = 50 };
        var ten = SweepDesign.Live with { EarningsWindow = 10 };
        var five = SweepDesign.Live with { EarningsWindow = 5, Trigger = TriggerKind.TopQuarterOfRange };
        var thin = SweepDesign.Live with { Hold = 10 };
        var rows = new[]
        {
            SweepRankRow(cross, 900, 0.3), SweepRankRow(below, 900, 0.3), SweepRankRow(SweepDesign.Live, 900, 0.3),
            SweepRankRow(any, 800, 0.25), SweepRankRow(six, 700, 0.25), SweepRankRow(fifty, 700, 0.24),
            SweepRankRow(five, 600, 0.2), SweepRankRow(ten, 600, 0.2), SweepRankRow(thin, 50, 0.9),
        };

        Assert.Equal([SweepDesign.Live, any, six, fifty, ten], SweepStages.Strongest(rows, 5).Select(row => row.Design));
        Assert.Equal([SweepDesign.Live, below, cross, any, six, fifty, ten, five, thin], SweepStages.Ranked(rows).Select(row => row.Design));

        // The report marks the tie.
        var page = SweepReportOf(SweepInputs(1_000, 0), rows, [.. SweepStages.Strongest(rows, 5).Select(row => SweepCarried(row.Design, row.Viable, row.MedianEdge))]);

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(page, "the same figures as rank [0-9]+").Count);
        Assert.Contains("the same figures as rank 1", page, StringComparison.Ordinal);
        Assert.Contains("the same figures as rank 7", page, StringComparison.Ordinal);

        // Step (c)'s rows are ranked the same way, one row a design.
        var crossRows = new[] { SweepCarried(any, 800, 0.4), new CombinationRow(any.Key, "0,-1,-1,-1,-1,0,-1,-1,-1", 800, 0.35, 0.35), SweepCarried(six, 700, 0.3), SweepCarried(thin, 50, 0.9) };

        Assert.Equal([any.Key, six.Key, thin.Key], SweepSearch.StrongestRows(crossRows, 5).Select(row => row.DesignKey));
    }

    [Fact]
    public void AcrossTheDesignsTheEdgeComesFirstAndThenTheDepth()
    {
        // Three proposals: A at an edge of 0.50 and a depth of 1, B at 0.47 and a depth of 4, C at 0.44 and a
        // depth of 6. Within the margin of 0.05 of A's edge are A and B, and B is the deeper, so B is chosen; C,
        // the deepest, is outside the margin and never chosen however deep.
        var space = SweepSpace.For([]);
        var live = space.LivePoint();
        var measures = YearMeasures(SweepForty, [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]);

        SweepProposal Proposal(float edge, int depth) => new(live, new SweepSummary(320, 50, 40, 35, edge, edge, 7, 7, true, 25, 0.7f, 0), new SweepDepth(depth, 0, 1, false), measures);

        var a = Proposal(0.50f, 1);
        var b = Proposal(0.47f, 4);
        var c = Proposal(0.44f, 6);

        Assert.Equal(1, SweepSearch.AcrossDesigns([(a, live), (b, live), (c, live)]));

        // A tie on depth goes to the higher edge, and a design with no proposal is passed over.
        Assert.Equal(2, SweepSearch.AcrossDesigns([(null, live), (Proposal(0.46f, 4), live), (Proposal(0.48f, 4), live)]));
        Assert.Null(SweepSearch.AcrossDesigns([(null, live), (null, live)]));
    }

    [Fact]
    public void AStageOneRowIsSavedByItsKeyAndReadBackToTheSameDesign()
    {
        foreach (var design in SweepAxes.Designs(selectionOnly: false).Where((_, at) => at % 997 == 0))
        {
            var row = SweepRankRow(design, 12, 0.25);
            var read = JsonSerializer.Deserialize<RankRow[]>(JsonSerializer.Serialize(new[] { row }))!.Single();

            Assert.Equal(row, read);
            Assert.Equal(design, read.Design);

            // A design and a row print as their keys and end, a design's selection being a design again.
            Assert.Equal(design.Key, design.ToString());
            Assert.Equal(design.Key, FormattableString.Invariant($"{design}"));
            Assert.Contains(design.Key, row.ToString(), StringComparison.Ordinal);
        }

        // A stage 2 result round-trips with its proposal, its depth and its slices.
        var space = SweepSpace.For([1, 5]);
        var point = space.LivePoint();
        var result = new SweepDesignResult(
            SweepDesign.Live.Key,
            ConditionSetting.Off.Key,
            [1, 5],
            1e9,
            250_000,
            0.0015,
            300_000,
            0.6f,
            0.55f,
            100,
            3,
            new SweepProposal(point, new SweepSummary(320, 50, 40, 35, 0.5f, 0.58f, 7, 7, true, 25, 0.7f, 4), new SweepDepth(3, 2, 1, true), YearMeasures(SweepForty, [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5])),
            ["round 1: no move is deeper"],
            ["the strength dial looked beyond its high end at 0.95 and 1"],
            [],
            [new SweepSlice("strength", "reward to risk", ["0.4", "0.5"], ["1", "1.25"], [[0.1f, 0.2f], [0.3f, float.NaN]], [[true, false], [true, true]], [[300, 200], [400, 100]], 1, 0)],
            [new SweepMarginProposal(0.03, "a setting", 0.59f, 2)],
            ["a note"]);
        var back = JsonSerializer.Deserialize<SweepDesignResult>(JsonSerializer.Serialize(result, SweepRunner.Json), SweepRunner.Json)!;

        Assert.Equal(result.Proposal!.Point, back.Proposal!.Point);
        Assert.Equal(result.Proposal.Depth, back.Proposal.Depth);
        Assert.Equal(result.Proposal.Summary, back.Proposal.Summary);
        Assert.Equal(result.Slices[0].Edge[0], back.Slices[0].Edge[0]);
        Assert.Equal(result.OtherMargins, back.OtherMargins);
        Assert.Equal(result.ConditionsOn, back.ConditionsOn);
    }
}
