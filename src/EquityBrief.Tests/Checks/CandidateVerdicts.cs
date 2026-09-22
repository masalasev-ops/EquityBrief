using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Checks;

// The arithmetic a registered candidate is judged by, over blocks worked by hand.
//
// Every figure here is enumerated rather than sampled, so every assertion is against a count a
// person can repeat: a sign-flip p-value is a share of two to the q arrangements, and the
// arrangements are listed in the comments that state them.
public sealed class CandidateVerdicts
{
    internal static CheckReach Reach => new(
        "candidate-verdicts",
        ["docs/ARCHITECTURE.html"],
        [
            CheckReach.Key(Scope.LimitsTable, "Looks a candidate's verdict is read at"),
            CheckReach.Key(Scope.LimitsTable, "The calibrated bar"),
        ]);

    // Eight blocks whose sums are all the same size and all positive: the statistic is infinite,
    // and the only arrangement that reaches it is the one with no sign flipped.
    static readonly double[] AllOneWay = [0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5, 0.5];

    // One block carrying twenty setups that all won against a bar of a half, and seven blocks of
    // one setup each that lost against the same bar.
    static readonly double[] OneHeavyBlock = [10, -0.5, -0.5, -0.5, -0.5, -0.5, -0.5, -0.5];

    [Fact]
    public void ASignFlipPValueIsTheShareOfArrangementsAtLeastAsLargeAsTheObservedOne()
    {
        // Worked by hand. Flipping any sign of a set of equal positive sums leaves a mean below the
        // observed one, and the observed arrangement's own scatter is nought, so its statistic is
        // infinite and every other arrangement sits below it. One arrangement of 256.
        Assert.Equal(1d / 256, SignFlip.PValue(AllOneWay), 12);
        Assert.Equal(double.PositiveInfinity, SignFlip.Statistic(AllOneWay));

        // And the same set read the other way round: with every block negative the observed
        // arrangement is the smallest of the 256, so every one of them is at least as large.
        Assert.Equal(1, SignFlip.PValue([.. AllOneWay.Select(sum => -sum)]), 12);
    }

    [Fact]
    public void TheSignFlipAndThePoissonBinomialDisagreeWhereTheWinsSitInOneBlock()
    {
        // The statistic is a monotone function of the signed sum, because flipping a sign leaves
        // the sum of squares as it was. So with the heavy block positive, every one of the 128
        // arrangements of the seven small blocks is at least as large as the observed one, which is
        // the arrangement with all seven negative; with the heavy block flipped the mean is below
        // nought and no arrangement reaches it. 128 of 256.
        Assert.Equal(0.5, SignFlip.PValue(OneHeavyBlock), 12);

        // The Poisson binomial over the same setups reads 20 wins of 27 at a bar of a half, which
        // is the tail of a binomial and is small. It assumes the setups are independent, and the
        // blocks exist because they are not, which is why it is drawn beside the verdict and
        // decides nothing.
        var independent = PoissonBinomial.UpperTail([.. Enumerable.Repeat(0.5, 27)], 20);

        Assert.True(independent < 0.01, $"The Poisson-binomial tail read {independent}, expected it under a hundredth.");
        Assert.True(SignFlip.PValue(OneHeavyBlock) > independent * 10, "The two tests did not disagree, so the case proves nothing.");
    }

    [Fact]
    public void APermutationOfTheOutcomesAcrossSetupsReturnsOne()
    {
        // The test this corpus does not use, computed over every permutation of five setups'
        // outcomes. Shuffling which setup won leaves the wins and the bars where they were, so the
        // excess is the same number every time and every arrangement is at least as large as the
        // observed one. A p-value of 1 whatever the setups did is a test that cannot answer.
        var setups = new[]
        {
            Setup(2026, 9, 1, ForwardReturnSeries.Win, 0.4),
            Setup(2026, 9, 2, ForwardReturnSeries.Loss, 0.5),
            Setup(2026, 9, 3, ForwardReturnSeries.Win, 0.3),
            Setup(2026, 9, 4, ForwardReturnSeries.Loss, 0.6),
            Setup(2026, 9, 7, ForwardReturnSeries.Win, 0.45),
        };

        var observed = CandidateRecord.Excess(setups);
        var permutations = Permutations([.. setups.Select(setup => setup.Outcome)]).ToArray();

        Assert.Equal(120, permutations.Length);

        var atLeast = permutations.Count(order => CandidateRecord.Excess(
            [.. setups.Select((setup, at) => setup with { Outcome = order[at] })]) >= observed - 1e-12);

        Assert.Equal(permutations.Length, atLeast);
        Assert.Equal(1d, (double)atLeast / permutations.Length);
    }

    [Fact]
    public void AFirstLookAtEightBlocksCannotCrossAtTheLevelTheFamilyIsTestedAt()
    {
        // The strongest a first look can be: eight blocks all one way, whose p-value is one in 256,
        // which is 0.0039. The level the spending function releases at half the information, over a
        // third of 0.05, is 0.00071, so nothing crosses however the blocks fell.
        var level = ReasonVerdict.Significance / 3;
        var released = Looks.Spent(level, Looks.Fraction(0));

        Assert.Equal(0.00071, released, 5);
        Assert.True(released < 1d / 256, $"The first look released {released}, which a sign-flip test over 8 blocks could reach.");
        Assert.Null(Looks.CrossedAt(AllOneWay, level));

        // And the level a candidate holds after both others have been promoted does release enough,
        // which is the one arrangement of the graph where a first look can promote.
        Assert.True(Looks.Spent(ReasonVerdict.Significance, Looks.Fraction(0)) > 1d / 256);
        Assert.Equal(0, Looks.CrossedAt(AllOneWay, ReasonVerdict.Significance));
    }

    [Fact]
    public void TheSpendingFunctionReleasesTheLevelsTheRegisterWasWrittenWith()
    {
        var level = ReasonVerdict.Significance / 3;

        Assert.Equal(0.00071, Looks.Spent(level, 0.5), 5);
        Assert.Equal(0.0057, Looks.Spent(level, 0.75), 4);
        Assert.Equal(level, Looks.Spent(level, 1), 12);
        Assert.Equal([8, 12, 16], Looks.At);
        Assert.Equal(0.5, Looks.Fraction(0), 12);
        Assert.Equal(0.75, Looks.Fraction(1), 12);
        Assert.Equal(1, Looks.Fraction(2), 12);
    }

    [Fact]
    public void ARecordBelowTheBlockFloorIsWithheldWithItsCountAgainstTheFloor()
    {
        // Seven whole blocks of one won setup each, which is a record every way as strong as the
        // test can be and one block short of a verdict.
        var night = Night(Blocks.Sessions * 9);
        var measured = CandidateRecord.For(BlockPerSession(7), First, night, ReasonVerdict.Significance / 3);

        Assert.Equal(7, measured.Blocks);
        Assert.Equal(CandidateRecord.BelowTheBlockFloor, measured.Withheld);
        Assert.Equal(CandidateRecord.NoLookYet, measured.Verdict);
        Assert.Empty(measured.Looks);
        Assert.Equal(Blocks.Floor, measured.Floor);
        Assert.Equal(Looks.At[0], measured.NextLookAt);

        // The eighth block turns the withholding off and reads the first look, and the record says
        // so rather than the count quietly appearing.
        var eight = CandidateRecord.For(BlockPerSession(8), First, night, ReasonVerdict.Significance / 3);

        Assert.Equal(8, eight.Blocks);
        Assert.Equal(CandidateRecord.Shown, eight.Withheld);
        Assert.Single(eight.Looks);
        Assert.Equal(Looks.At[0], eight.Looks[0].Blocks);
        Assert.False(eight.Looks[0].Crossed);
    }

    [Fact]
    public void ALookReadsOnlyTheSetupsWhoseWholeWindowHasClosedAndANightlyReadingNeverMovesTheVerdictField()
    {
        var level = ReasonVerdict.Significance / 3;
        var setups = BlockPerSession(8).ToList();

        // A ninth setup listed after the eighth block, whose own window has not closed and whose
        // block is not whole. It moves the running figure and nothing else.
        setups.Add(Setup(Sessions(Blocks.Sessions * 8 + 2), ForwardReturnSeries.Loss, 0.5));

        var night = Night(Blocks.Sessions * 9);
        var before = CandidateRecord.For(BlockPerSession(8), First, night, level);
        var after = CandidateRecord.For(setups, First, night, level);

        Assert.Equal(before.Verdict, after.Verdict);
        Assert.Equal(before.Blocks, after.Blocks);
        Assert.Equal(before.Setups, after.Setups);
        Assert.Equal(before.Looks[0].Setups, after.Looks[0].Setups);
        Assert.Equal(1, after.NotYetInABlock);
        Assert.Equal(0, before.NotYetInABlock);
    }

    [Fact]
    public void TheGraphPassesAPromotedLevelInEqualSharesAndARetiredLevelToNoOne()
    {
        var significance = ReasonVerdict.Significance;

        // Three candidates, none decided: each holds a third of the level.
        var standing = new[] { Member("a", false, false, false), Member("b", false, false, false), Member("c", false, false, false) };

        Assert.All(HolmGraph.Levels(standing, significance), level => Assert.Equal(significance / 3, level.Level, 12));

        // The first crosses: its share passes in equal parts to the two still standing.
        var promoted = new[] { Member("a", true, false, false), Member("b", false, false, false), Member("c", false, false, false) };
        var passed = HolmGraph.Levels(promoted, significance).ToDictionary(level => level.Candidate, StringComparer.Ordinal);

        Assert.Equal(significance / 3 + (significance / 3 / 2), passed["b"].Level, 12);
        Assert.Equal(passed["b"].Level, passed["c"].Level, 12);
        Assert.True(passed["a"].Crossed);
        Assert.Equal(1, passed["a"].Step);
        Assert.Equal(2, passed["b"].Step);

        // And with one retired rather than promoted, its share reaches nobody: the other two hold
        // what they opened with, and the one that crossed passes its share to the one left.
        var retired = new[] { Member("a", true, false, false), Member("b", false, false, false), Member("c", false, true, false) };
        var kept = HolmGraph.Levels(retired, significance).ToDictionary(level => level.Candidate, StringComparer.Ordinal);

        Assert.Equal(significance / 3 * 2, kept["b"].Level, 12);
        Assert.Equal(significance / 3, kept["c"].Level, 12);
        Assert.False(kept["c"].Crossed);
    }

    [Fact]
    public void ASetupEnteredAndStoppedOnOneSessionIsCountedAsALossAgainstTheBarTheWorstFillInTheZoneSets()
    {
        // A listing whose close sits above its entry zone, and a session that closes through the
        // zone and the stop at once. The scoring calls it a loss with no figure, because the store
        // does not say where in the zone the fill sat, and it hands back the top of the zone, which
        // is the worst price the plan offered to buy at, as what a bar is simulated from.
        var outcome = ForwardReturnSeries.OverSetup(
            [new ReturnBar(new DateOnly(2026, 9, 2), 80m)],
            stop: 90m,
            target: 130m,
            entryHigh: 100m,
            closeAtListing: 110m,
            rawCloseAtListing: 110m);

        Assert.Equal(ForwardReturnSeries.Loss, outcome.Outcome);
        Assert.Null(outcome.ReturnPct);
        Assert.Null(outcome.BreakEven);
        Assert.Equal(100m, outcome.EnteredAt);
        Assert.Equal(ForwardReturnSeries.SetupSessionCap - 1, outcome.SessionsLeft);

        // The record counts it as the loss it is, and against a bar rather than against nothing.
        var setups = new[] { Setup(2026, 9, 1, ForwardReturnSeries.Loss, 0.4, breakEven: null) };

        Assert.Single(setups, CandidateRecord.IsSameSession);
        Assert.Equal(-0.4, CandidateRecord.Excess(setups), 12);
    }

    [Fact]
    public void TheCalibratedBarFallsWhereThePlanAsksForMoreAndRisesWithTheRoundTrip()
    {
        // A plan whose target is as far above the fill as its stop is below it wins about half the
        // time under a walk with no edge, and one whose target is three times as far wins far less.
        // Neither figure is hand-worked: what is asserted is the ordering the calibration exists to
        // catch, which the planned break-even cannot see, and that the cost only ever raises the bar.
        const double volatility = 0.02;
        var even = NullWin.For(0.95, 1.05, volatility, ForwardReturnSeries.SetupSessionCap, 0, seed: 1);
        var far = NullWin.For(0.95, 1.15, volatility, ForwardReturnSeries.SetupSessionCap, 0, seed: 1);

        Assert.NotNull(even);
        Assert.NotNull(far);
        Assert.InRange(even!.Value, 0.45, 0.55);
        Assert.True(far!.Value < even.Value - 0.1, $"A target three times as far read {far}, against {even} for an even one.");

        // The round trip is added to the bar rather than to the paths, so it raises it and the
        // sensitivity raises it further.
        var carried = NullWin.For(0.95, 1.05, volatility, ForwardReturnSeries.SetupSessionCap, NullWin.CostBasisPoints, seed: 1);
        var sensitivity = NullWin.For(0.95, 1.05, volatility, ForwardReturnSeries.SetupSessionCap, NullWin.SensitivityBasisPoints, seed: 1);

        Assert.True(carried > even.Value, "The round trip did not raise the bar.");
        Assert.True(sensitivity > carried, "The sensitivity did not raise the bar further than the round trip it is shown beside.");

        // The same setup yields the same bar however often it is read, which is what lets a record
        // be recomputed from the store rather than remembered.
        Assert.Equal(carried, NullWin.For(0.95, 1.05, volatility, ForwardReturnSeries.SetupSessionCap, NullWin.CostBasisPoints, seed: 1));
        Assert.Equal(NullWin.SeedFor("AAPL", 20_000), NullWin.SeedFor("AAPL", 20_000));
        Assert.NotEqual(NullWin.SeedFor("AAPL", 20_000), NullWin.SeedFor("MSFT", 20_000));
    }

    // The first session of the record, early enough in the closure table that nine blocks of it
    // still fall inside what the table covers.
    static readonly DateOnly First = new(2025, 1, 2);

    static GraphMember Member(string candidate, bool crosses, bool retired, bool promoted) =>
        new(candidate, promoted, retired, _ => crosses);

    // One won setup in each of the first blocks, each listed on the block's own first session.
    static IReadOnlyList<CandidateSetup> BlockPerSession(int blocks) =>
    [
        .. Enumerable.Range(0, blocks).Select(block => Setup(Sessions(block * Blocks.Sessions), ForwardReturnSeries.Win, 0.5)),
    ];

    // The session that many exchange sessions after the record's first, which is what a block is
    // counted in.
    static DateOnly Sessions(int sessions)
    {
        var at = First;

        for (var counted = 0; counted < sessions;)
        {
            at = at.AddDays(1);

            if (ExchangeClosures.IsSession(at))
            {
                counted++;
            }
        }

        return at;
    }

    static DateOnly Night(int sessions) => Sessions(sessions);

    static CandidateSetup Setup(int year, int month, int day, string outcome, double bar, double? breakEven = 40) =>
        Setup(new DateOnly(year, month, day), outcome, bar, breakEven);

    static CandidateSetup Setup(DateOnly session, string outcome, double bar, double? breakEven = 40) =>
        new(session, outcome, bar, bar + 0.01, breakEven, null, null, false);

    static IEnumerable<string[]> Permutations(IReadOnlyList<string> values) =>
        values.Count <= 1
            ? [[.. values]]
            : values.SelectMany((value, at) => Permutations([.. values.Where((_, other) => other != at)])
                .Select(rest => (string[])[value, .. rest]));
}
