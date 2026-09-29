using System.Text.Json;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// The starting point and the variants: a plateau's centre and never the single best setting, the year-by-year
// floor refusing a record one year carries, each of the four variant tests refusing on its own, and the report
// saying what the ruling asks it to.
public partial class FixtureExpectations
{
    // Every dial at three levels and the stop at three options, 19,683 settings.
    static readonly SweepGrid SweepThrees = SweepGrid.Coarse;

    static int SweepDistance(DialSetting a, DialSetting b) =>
        Math.Abs(a.Strength - b.Strength) + Math.Abs(a.DepthLow - b.DepthLow) + Math.Abs(a.DepthHigh - b.DepthHigh)
        + Math.Abs(a.DryUp - b.DryUp) + Math.Abs(a.Freshness - b.Freshness) + Math.Abs(a.RewardToRisk - b.RewardToRisk)
        + Math.Abs(a.Stop - b.Stop) + Math.Abs(a.Market - b.Market) + Math.Abs(a.Band - b.Band);

    static SweepVariation SweepGood(double multiple) =>
        new(0, 0, 60, 40, 35, (float)multiple, 400, 7, true, 25, 0.7f);

    static SweepVariation SweepPoor() =>
        new(0, 0, 30, 40, 35, -0.2f, 400, 2, false, 25, 0.7f);

    [Fact]
    public void TheStartingPointIsThePlateausCentreAndNeverTheSingleBestSetting()
    {
        // The plateau is every setting with the strength bar at its first two levels: each beats its break-even
        // and no skill and meets the four floors, its average result falling by a tenth of a risk for each step
        // it stands from the centre C, the strength bar at its first level and every other dial at its middle.
        // At C every neighbour is one step away, a median of 0.9; anywhere else some neighbours are two steps
        // away, so C's is the highest. Every setting with the strength bar at its last level beats neither,
        // except B, the best single setting, at 9 risks, whose neighbours beat neither but one. The plateau
        // holds all 2 x 3^7 x 3 = 13,122 settings with the bar at its first two levels, B not among them.
        var centre = new DialSetting(0, 1, 1, 1, 1, 1, 1, 1, 1);
        var best = new DialSetting(2, 0, 0, 0, 0, 0, 0, 0, 0);
        var variations = new SweepVariation[SweepThrees.Variations];

        for (var index = 0; index < variations.Length; index++)
        {
            var setting = DialSetting.Of(SweepThrees, index / SweepThrees.CellsPerStop, index % SweepThrees.CellsPerStop);

            variations[index] = setting == best ? SweepGood(9) : setting.Strength < 2 ? SweepGood(1 - (0.1 * SweepDistance(setting, centre))) : SweepPoor();
        }

        var found = SweepPlateau.Centre(SweepThrees, variations);

        Assert.NotNull(found);
        Assert.Equal(centre, found.Value.Setting);
        Assert.Equal(0.9, found.Value.NeighbourMedian, 6);
        Assert.Equal(13_122, found.Value.PlateauSize);

        // B is the best single setting, off the plateau.
        Assert.Equal(SweepPlateau.Index(SweepThrees, best), Enumerable.Range(0, variations.Length).MaxBy(index => variations[index].AverageMultiple));
        Assert.False(SweepPlateau.OnThePlateau(SweepThrees, variations, best));

        // A plateau whose settings miss a floor proposes nothing: the same map with every setting one block
        // short of the 22.
        var short1 = variations.Select(one => one with { Blocks = 21 }).ToArray();

        Assert.Null(SweepPlateau.Centre(SweepThrees, short1));

        // Three quarters of a setting's neighbours are enough and fewer are not: the grid's first corner has
        // nine neighbours, one step up on each dial, all on the plateau. Two failing leaves seven of nine, over
        // three quarters; a third leaves six, under.
        var corner = new DialSetting(0, 0, 0, 0, 0, 0, 0, 0, 0);
        var neighbours = SweepPlateau.Neighbours(SweepThrees, corner).ToArray();

        Assert.Equal(9, neighbours.Length);
        Assert.All(neighbours, neighbour => Assert.Equal(1, SweepDistance(neighbour, corner)));

        var sparse = variations.ToArray();

        sparse[SweepPlateau.Index(SweepThrees, neighbours[0])] = SweepPoor();
        sparse[SweepPlateau.Index(SweepThrees, neighbours[1])] = SweepPoor();

        Assert.True(SweepPlateau.OnThePlateau(SweepThrees, sparse, corner));

        sparse[SweepPlateau.Index(SweepThrees, neighbours[2])] = SweepPoor();

        Assert.False(SweepPlateau.OnThePlateau(SweepThrees, sparse, corner));
    }

    static SweepMeasures YearMeasures(int[] scored, double?[] multiples, double?[]? shares = null, double?[]? breakEvens = null) =>
        new(
            scored.Sum(),
            scored.Sum(),
            scored.Sum(),
            scored.Sum() / 2,
            50,
            40,
            35,
            multiples.Average(),
            scored,
            shares ?? [.. scored.Select(_ => (double?)50)],
            breakEvens ?? [.. scored.Select(_ => (double?)40)],
            [.. scored.Select(_ => (double?)35)],
            multiples,
            30,
            200,
            300);

    static readonly int[] SweepForty = [40, 40, 40, 40, 40, 40, 40, 40];

    [Fact]
    public void EachOfTheFourVariantTestsRefusesOnItsOwn()
    {
        var start = YearMeasures(SweepForty, [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5]);
        var startPicks = Enumerable.Range(0, 10).Select(at => (at, 100)).ToHashSet();

        // Six of ten picks shared, four the starting point does not pick: a share of 0.4.
        var picks = Enumerable.Range(4, 10).Select(at => (at, 100)).ToHashSet();
        var mixed = YearMeasures(SweepForty, [0.6, 0.4, 0.6, 0.4, 0.6, 0.4, 0.6, 0.4]);

        SweepVariant Judge(SweepMeasures measures, IReadOnlySet<(int, int)> own, bool onThePlateau = true) =>
            SweepPlateau.Judge("strength", "the strength bar", SweepDesign.Live, DialSetting.LiveOnCoarse, measures, own, start, startPicks, onThePlateau);

        var passing = Judge(mixed, picks);

        Assert.True(passing.Passes);
        Assert.Equal((4, 4), (passing.YearsItWins, passing.YearsTheStartWins));
        Assert.Equal(0.4, passing.OutsideShare, 9);
        Assert.Equal(40, passing.FewestTradesInAYear);

        // One change reaching a setting off the plateau.
        var off = Judge(mixed, picks, onThePlateau: false);

        Assert.Equal((false, true, true, true), (off.OnThePlateau, off.Open, off.DifferentEnough, off.EnoughTrades));

        // Settled by history: six of the eight years one way, and five open.
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
    }

    static SweepHistoryInputs SweepInputs(long indexNights, long withoutBar) =>
        new(new DateOnly(2026, 9, 25), [new DateOnly(2019, 1, 2)], [], indexNights, withoutBar, 3, 535, "constructed");

    static string SweepReportOf(SweepHistoryInputs inputs, IReadOnlyList<RankRow> rows, IReadOnlyList<SweepDesign> carried) =>
        SweepReport.Build(inputs, [], rows, carried, new Dictionary<SweepDesign, SweepVariation[]>(), new SweepRunner.State(), 1, [new DateOnly(2019, 1, 2)], 0);

    static RankRow SweepRankRow(SweepDesign design, int viable, double? median) =>
        new(design.Key, SweepGrid.Coarse.Variations, viable, (double)viable / SweepGrid.Coarse.Variations, median, null, null, null, null, null, null);

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
    }

    [Fact]
    public void TheRankingMarksTheLiveRulesDesignWithItsRankAndTheReportNamesAStopNotAtABand()
    {
        var average = SweepDesign.Live with { Support = SupportKind.Average };
        var any = SweepDesign.Live with { Support = SupportKind.AnyBand };
        var rows = new[] { SweepRankRow(any, 900, 0.3), SweepRankRow(average, 800, 0.2), SweepRankRow(SweepDesign.Live, 700, 0.1), SweepRankRow(SweepDesign.Live with { Hold = 10 }, 10, 0.0) };

        var withAverage = SweepReportOf(SweepInputs(1_000, 0), rows, [any, average]);
        var liveRow = System.Text.RegularExpressions.Regex.Match(withAverage, "<tr class=\"live\">.*?</tr>").Value;

        Assert.Contains("ranks 3 of 4", withAverage, StringComparison.Ordinal);
        Assert.Contains(System.Net.WebUtility.HtmlEncode("the live rule's design"), liveRow, StringComparison.Ordinal);
        Assert.Contains("<td class=\"num\">3</td>", liveRow, StringComparison.Ordinal);
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(withAverage, "carried to stage 2").Count);
        Assert.Contains("section 10 states as a principle", withAverage, StringComparison.Ordinal);
        Assert.Contains("every figure on this page is history", withAverage, StringComparison.OrdinalIgnoreCase);

        var withoutAverage = SweepReportOf(SweepInputs(1_000, 0), rows, [any, SweepDesign.Live]);

        Assert.DoesNotContain("section 10 states as a principle", withoutAverage, StringComparison.Ordinal);
        Assert.Contains("so every one of them stops at a band", withoutAverage, StringComparison.Ordinal);

        // With no starting point there is no registration to state, and none is run.
        Assert.Contains("No starting point is proposed", withoutAverage, StringComparison.Ordinal);
        Assert.DoesNotContain("register --freeze", withoutAverage, StringComparison.Ordinal);
    }

    [Fact]
    public void StageTwoTakesTheFiveStrongestWhoseFiguresDifferEachTieReadThroughTheDesignNearestTheLiveRule()
    {
        // Seven designs ranked by their share: the first three tie on every figure, the live classifier's own
        // among them standing for the other two, then three that differ, then two tying again below them. The
        // five carried are the tie's live classifier, the three that differ and the first of the lower tie.
        var below = SweepDesign.Live with { Uptrend = UptrendRule.ClassifierBelowBoth };
        var cross = SweepDesign.Live with { Uptrend = UptrendRule.ClassifierBelowBothUnderACross, Strength = StrengthMeasure.SixMonths };
        var any = SweepDesign.Live with { Support = SupportKind.AnyBand };
        var six = SweepDesign.Live with { Strength = StrengthMeasure.SixMonths };
        var fifty = SweepDesign.Live with { ReferenceHigh = 50 };
        var ten = SweepDesign.Live with { EarningsWindow = 10 };
        var five = SweepDesign.Live with { EarningsWindow = 5, Trigger = TriggerKind.TopQuarterOfRange };
        var rows = new[]
        {
            SweepRankRow(cross, 900, 0.3), SweepRankRow(below, 900, 0.3), SweepRankRow(SweepDesign.Live, 900, 0.3),
            SweepRankRow(any, 800, 0.25), SweepRankRow(six, 700, 0.25), SweepRankRow(fifty, 700, 0.24),
            SweepRankRow(five, 600, 0.2), SweepRankRow(ten, 600, 0.2),
        };

        Assert.Equal([SweepDesign.Live, any, six, fifty, ten], SweepRunner.Carried(rows).Select(row => row.Design));
        Assert.Equal([SweepDesign.Live, below, cross, any, six, fifty, ten, five], SweepRunner.Ranked(rows).Select(row => row.Design));

        // The report marks the tie.
        var page = SweepReportOf(SweepInputs(1_000, 0), rows, [.. SweepRunner.Carried(rows).Select(row => row.Design)]);

        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(page, "the same figures as rank [0-9]+").Count);
        Assert.Contains("the same figures as rank 1", page, StringComparison.Ordinal);
        Assert.Contains("the same figures as rank 7", page, StringComparison.Ordinal);
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
        }
    }
}
