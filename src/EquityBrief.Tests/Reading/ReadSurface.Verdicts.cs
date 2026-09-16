using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 8.5: a reason's wins tested against the bar its own setups
// demanded, at a corrected threshold, with the verdict drawn beside the reason
// and withheld below either floor.
//
// The arithmetic is asserted against cases worked by hand rather than against a
// second implementation, because a test that computes the expected value the way
// the code does is a test that agrees with the code by construction.
// see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
public partial class ReadSurface
{
    [Fact]
    public void EveryTailAndDisplayStateMatchesTheOneWorkedByHandInTheExpectation()
    {
        // The derived expectation. Each tail is worked in the file as the sum of
        // its own terms, so what is compared is a derivation against the code
        // rather than a run against a copy of itself.
        var expectation = Expected("reason-verdicts");
        var tails = expectation.GetProperty("tails").EnumerateArray().ToArray();

        Assert.True(tails.Length >= 6, $"The expectation works {tails.Length} tail(s), expected at least 6.");

        foreach (var one in tails)
        {
            var what = one.GetProperty("case").GetString();

            var probabilities = one.GetProperty("breakEvens").EnumerateArray()
                .Select(value => value.GetDouble())
                .ToArray();

            Assert.Equal(
                (what, one.GetProperty("tail").GetDouble()),
                (what, Math.Round(PoissonBinomial.UpperTail(probabilities, one.GetProperty("atLeast").GetInt32()), 10)));
        }

        // The approximation the decision names and nothing uses, worked in the
        // file too, so the number it would have produced is on the record beside
        // the one the code does produce.
        var approximate = expectation.GetProperty("binomialAtTheMean");

        Assert.Equal(
            approximate.GetProperty("tail").GetDouble(),
            Math.Round(
                PoissonBinomial.BinomialUpperTailAtTheMean(
                    [.. approximate.GetProperty("breakEvens").EnumerateArray().Select(value => value.GetDouble())],
                    approximate.GetProperty("atLeast").GetInt32()),
                10));

        // The four display states, each over the constructed counts the file
        // states, judged at the threshold the file states.
        foreach (var one in expectation.GetProperty("display").EnumerateArray())
        {
            var state = one.GetProperty("state").GetString();

            var verdict = ReasonVerdict.For(
                Scored(
                    one.GetProperty("wins").GetInt32(),
                    one.GetProperty("losses").GetInt32(),
                    one.GetProperty("breakEven").GetDouble(),
                    one.GetProperty("sessions").GetInt32()),
                expectation.GetProperty("liveFamily").GetInt32());

            Assert.Equal((state, one.GetProperty("resolved").GetInt32()), (state, verdict.Resolved));
            Assert.Equal((state, one.GetProperty("short").GetString()), (state, verdict.Withheld));

            var expectedVerdict = one.GetProperty("verdict");

            Assert.Equal(
                (state, expectedVerdict.ValueKind == System.Text.Json.JsonValueKind.Null ? (bool?)null : expectedVerdict.GetBoolean()),
                (state, verdict.Cleared));
        }
    }

    [Fact]
    public void TheFiguresTheDocumentStatesAreTheOnesTheCodeCarries()
    {
        // The pin. Every one of these is a convention rather than a measurement,
        // which is exactly why it needs pinning: nothing else would notice the
        // document and the code drifting apart, and each is a figure a reader of
        // the limits table would take as the one the verdict was computed at.
        var expectation = Expected("reason-verdicts");

        Assert.Equal(ReasonVerdict.Significance, expectation.GetProperty("significance").GetDouble(), 10);
        Assert.Equal(ReasonVerdict.LiveFamily, expectation.GetProperty("liveFamily").GetInt32());
        Assert.Equal(ReasonVerdict.MinimumResolved, expectation.GetProperty("minimumResolved").GetInt32());
        Assert.Equal(ReasonVerdict.MinimumSessions, expectation.GetProperty("minimumSessions").GetInt32());

        // And against the document itself, which is the statement a reader
        // actually reads. Each figure is matched where section 17 states it.
        var architecture = Checks.Corpus.Read("docs/ARCHITECTURE.html");

        Assert.Contains(
            FormattableString.Invariant($"{ReasonVerdict.Significance}, one-sided, divided by the family"),
            architecture,
            StringComparison.Ordinal);

        Assert.Contains(
            FormattableString.Invariant($"the six live reasons are one family of {ReasonVerdict.LiveFamily}"),
            architecture,
            StringComparison.Ordinal);

        Assert.Contains(
            FormattableString.Invariant(
                $"{ReasonVerdict.MinimumResolved} resolved setups spread over at least {ReasonVerdict.MinimumSessions} distinct listing sessions"),
            architecture,
            StringComparison.Ordinal);

        // The higher floor a live reason's retirement waits on, where section 17
        // states it and as the constant the register's refusal names.
        Assert.Contains(
            FormattableString.Invariant($"; {ReasonVerdict.MinimumBeforeALiveReasonIsRetired} before a live condition may be retired"),
            architecture,
            StringComparison.Ordinal);

        Assert.Contains(
            FormattableString.Invariant($"once its record holds {ReasonVerdict.MinimumBeforeALiveReasonIsRetired} resolved setups"),
            Checks.Corpus.Read("docs/RUNBOOK.md"),
            StringComparison.Ordinal);

        Assert.True(ReasonVerdict.MinimumBeforeALiveReasonIsRetired > ReasonVerdict.MinimumResolved);

        // The read API's own minimum is the same number, stated in two places
        // because the projection and the test are two components, and asserted
        // equal so the two cannot drift.
        Assert.Equal(ReasonVerdict.MinimumResolved, RunScreen.MinimumResolvedSetups);
    }

    [Fact]
    public void AFamilyThatGrowsCorrectsEveryVerdictReadFromItAgain()
    {
        // 13.3's "adding restarts the clock": a family that gains a member changes
        // the correction, and the verdicts under it are recomputed. They are,
        // because no verdict is stored: each is read from the record and the
        // divisor at the moment it is drawn. What this holds is that the divisor
        // reaching a verdict is the family as it stands.

        // The live family is the count of live reasons the code evaluates, so a
        // reason added to section 11 and the code leaves this red until the family
        // it divides by grows with it.
        Assert.Equal(ReasonVerdict.LiveFamily, ShortlistSeries.Reasons.Length);

        // 120 wins of 251 against a bar of 40 per cent has an exact tail of about
        // 0.00727: under 0.05 over 6, which is 0.00833, and over 0.05 over 7, which
        // is 0.00714. The same record clears at six and does not at seven.
        var record = Scored(120, 131, 40, ReasonVerdict.MinimumSessions);

        var six = ReasonVerdict.For(record, ReasonVerdict.LiveFamily);
        var seven = ReasonVerdict.For(record, ReasonVerdict.LiveFamily + 1);

        Assert.Equal(six.PValue, seven.PValue);
        Assert.True(six.Cleared);
        Assert.False(seven.Cleared);
        Assert.Equal((6, 7), (six.Divisor, seven.Divisor));

        // A candidate's family is read off the register the same way: a second
        // registration before the window opened is a divisor of two where one
        // stood, and the threshold a verdict under it carries halves.
        var opened = new DateTimeOffset(2026, 9, 20, 21, 0, 0, TimeSpan.Zero);
        RegisterRow Row(long id, string name, DateTimeOffset at) =>
            new(id, name, "a rule", "a test", Core.Candidates.MomentumIndexReading.EvaluatorName, "{}", "000000000000", CandidateFamily.Registered, null, at, null);

        RegisterRow[] one = [Row(1, "first", opened.AddDays(-2))];
        RegisterRow[] two = [.. one, Row(2, "second", opened.AddDays(-1))];

        var byOne = ReasonVerdict.For(record, CandidateFamily.Divisor(one, opened));
        var byTwo = ReasonVerdict.For(record, CandidateFamily.Divisor(two, opened));

        Assert.Equal((1, 2), (byOne.Divisor, byTwo.Divisor));
        Assert.Equal(byOne.Threshold / 2, byTwo.Threshold, 12);
    }

    [Fact]
    public void TheExactTailMatchesTheCasesWorkedByHand()
    {
        // Three setups each demanding half. The wins are then Binomial(3, 0.5),
        // whose upper tail at 2 is the three ways to win exactly two plus the one
        // way to win all three, over eight: 4 of 8, which is a half.
        Assert.Equal(0.5, PoissonBinomial.UpperTail([0.5, 0.5, 0.5], 2), 10);

        // Two setups demanding a fifth and three fifths. At least one is one
        // minus both losing: 1 - 0.8 x 0.4 = 0.68. Both is 0.2 x 0.6 = 0.12.
        Assert.Equal(0.68, PoissonBinomial.UpperTail([0.2, 0.6], 1), 10);
        Assert.Equal(0.12, PoissonBinomial.UpperTail([0.2, 0.6], 2), 10);

        // Three setups demanding a tenth, a fifth and three tenths, which is the
        // case that separates a Poisson binomial from a binomial: no two of the
        // three probabilities agree, so a binomial at any single value gets it
        // wrong. All three is 0.1 x 0.2 x 0.3 = 0.006. At least one is
        // 1 - 0.9 x 0.8 x 0.7 = 0.496. Exactly two is the three ways it can
        // happen, 0.1 x 0.2 x 0.7 + 0.1 x 0.8 x 0.3 + 0.9 x 0.2 x 0.3 =
        // 0.014 + 0.024 + 0.054 = 0.092, so at least two is 0.098.
        double[] uneven = [0.1, 0.2, 0.3];

        Assert.Equal(0.006, PoissonBinomial.UpperTail(uneven, 3), 10);
        Assert.Equal(0.496, PoissonBinomial.UpperTail(uneven, 1), 10);
        Assert.Equal(0.098, PoissonBinomial.UpperTail(uneven, 2), 10);

        // And the binomial at the mean of those three, which is a fifth, gives a
        // different answer for the same count: at least two of Binomial(3, 0.2)
        // is 3 x 0.2 x 0.2 x 0.8 + 0.008 = 0.096 + 0.008 = 0.104. The two
        // differing is what makes using the exact one a choice rather than a
        // detail.
        Assert.Equal(0.104, PoissonBinomial.BinomialUpperTailAtTheMean(uneven, 2), 10);

        // The distribution sums to one, which no single tail above would catch if
        // the recurrence dropped weight.
        Assert.Equal(1d, PoissonBinomial.Distribution(uneven).Sum(), 10);

        // The ends. A tail at nothing is certain, a tail past every setup is
        // impossible, and setups nobody can lose or nobody can win are the two
        // degenerate cases the recurrence has to survive.
        Assert.Equal(1d, PoissonBinomial.UpperTail(uneven, 0), 10);
        Assert.Equal(0d, PoissonBinomial.UpperTail(uneven, 4), 10);
        Assert.Equal(0d, PoissonBinomial.UpperTail([0, 0, 0], 1), 10);
        Assert.Equal(1d, PoissonBinomial.UpperTail([1, 1, 1], 3), 10);

        // A break-even outside the unit interval refuses rather than producing a
        // number, because a tail computed over one is meaningless rather than
        // wrong and a page would draw it either way.
        Assert.Throws<ArgumentOutOfRangeException>(() => PoissonBinomial.UpperTail([0.5, 1.5], 1));
    }

    [Fact]
    public void TheBinomialAtTheMeanBoundsTheExactTailWhereTheDecisionSaysItDoes()
    {
        // The conservative approximation, named in the decision and used by
        // nothing. Hoeffding's bound holds for a count at or above the mean plus
        // one, which is where a verdict that clears its bar sits, and it is
        // asserted over that range rather than everywhere: the inequality is
        // stated with its condition, so a test that checked it below the mean
        // would be checking something the decision does not claim.
        // see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
        double[] spread = [0.1, 0.3, 0.5, 0.7, 0.9];

        var mean = spread.Average();

        Assert.Equal(0.5, mean, 10);

        // The mean times the count is 2.5, so the range the bound is claimed over
        // begins at 3.5 and the integer counts in it are 4 and 5.
        for (var wins = 4; wins <= spread.Length; wins++)
        {
            var exact = PoissonBinomial.UpperTail(spread, wins);
            var bound = PoissonBinomial.BinomialUpperTailAtTheMean(spread, wins);

            Assert.True(
                bound >= exact,
                $"at {wins} wins the binomial at the mean gives {bound} and the exact tail {exact}, " +
                "so the bound is not the conservative one the decision names.");
        }

        // The bound is not vacuous: over a spread it is strictly larger, so a
        // run where the two agreed everywhere would mean the spread was not a
        // spread.
        Assert.True(
            PoissonBinomial.BinomialUpperTailAtTheMean(spread, 4) > PoissonBinomial.UpperTail(spread, 4),
            "the bound equals the exact tail over a spread of probabilities, so nothing distinguishes them.");

        // And where every setup demands the same thing, the two are the same
        // distribution and agree exactly, which is the case the bound degenerates to.
        Assert.Equal(
            PoissonBinomial.UpperTail([0.4, 0.4, 0.4, 0.4], 3),
            PoissonBinomial.BinomialUpperTailAtTheMean([0.4, 0.4, 0.4, 0.4], 3),
            10);
    }

    static IReadOnlyList<ReasonVerdict.ScoredSetup> Scored(int wins, int losses, double breakEven, int sessions)
    {
        var night = new DateOnly(2026, 9, 4);
        var at = 0;

        ReasonVerdict.ScoredSetup One(bool won) =>
            new(won, breakEven, night.AddDays(-(++at % Math.Max(sessions, 1))));

        return
        [
            .. Enumerable.Range(0, wins).Select(_ => One(true)),
            .. Enumerable.Range(0, losses).Select(_ => One(false)),
        ];
    }

    [Fact]
    public void NoVerdictAppearsBelowEitherFloorAndTheOneThatIsShortIsNamed()
    {
        // 8.5's done condition, at both boundaries rather than near them. A bound
        // is asserted at the value it names and one either side, because a gate
        // tested only well below its threshold is a gate nobody has shown opens.
        var below = ReasonVerdict.For(Scored(125, 124, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);
        var at = ReasonVerdict.For(Scored(125, 125, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);

        Assert.Equal((249, 250), (below.Resolved, at.Resolved));
        Assert.Null(below.Cleared);
        Assert.Null(below.PValue);
        Assert.Equal(ReasonVerdict.BelowTheResolvedMinimum, below.Withheld);

        Assert.NotNull(at.Cleared);
        Assert.NotNull(at.PValue);
        Assert.Equal(ReasonVerdict.Shown, at.Withheld);

        // The night floor, at its own boundary, over a population that clears the
        // row floor comfortably. This is the case a single floor would have drawn
        // a verdict for: 300 resolved setups arriving on 59 sessions.
        var fewNights = ReasonVerdict.For(Scored(150, 150, 40, ReasonVerdict.MinimumSessions - 1), ReasonVerdict.LiveFamily);
        var enough = ReasonVerdict.For(Scored(150, 150, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);

        Assert.Equal((59, 60), (fewNights.Sessions, enough.Sessions));
        Assert.Equal(300, fewNights.Resolved);
        Assert.Null(fewNights.Cleared);
        Assert.Equal(ReasonVerdict.BelowTheSessionMinimum, fewNights.Withheld);
        Assert.NotNull(enough.Cleared);

        // Every verdict names its divisor and the threshold that divisor set,
        // withheld or not, because a reader has to be able to see how hard the
        // test would be before deciding whether the wait is worth it.
        // see: The significance threshold is divided by the family size, and the divisor is shown
        foreach (var verdict in new[] { below, at, fewNights, enough })
        {
            Assert.Equal(ReasonVerdict.LiveFamily, verdict.Divisor);
            Assert.Equal(ReasonVerdict.Significance / ReasonVerdict.LiveFamily, verdict.Threshold, 10);
        }

        // A family of none divides by nothing, which is the correction not being
        // applied, so it refuses rather than testing at the uncorrected level.
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ReasonVerdict.For(Scored(150, 150, 40, 60), 0));

        // And a setup that set no bar is outside the population entirely, for the
        // reason the share is: a count tested against a bar has to be the count of
        // the rows that bar was averaged over.
        var mixed = Scored(125, 125, 40, ReasonVerdict.MinimumSessions)
            .Concat(Enumerable.Range(0, 20).Select(_ => new ReasonVerdict.ScoredSetup(false, null, new DateOnly(2026, 9, 4))))
            .ToArray();

        Assert.Equal(250, ReasonVerdict.For(mixed, ReasonVerdict.LiveFamily).Resolved);
    }

    [Fact]
    public void AReasonThatBeatsItsOwnBarClearsAndOneThatDoesNotDoesNot()
    {
        // The verdict itself, over two populations that differ only in their
        // wins. Half of 250 against a bar of 40 per cent is well clear; 100 of
        // 250 against the same bar is below it and cannot clear however the
        // arithmetic is arranged.
        var clears = ReasonVerdict.For(Scored(125, 125, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);

        Assert.True(clears.Cleared);
        Assert.True(clears.PValue < clears.Threshold);

        var misses = ReasonVerdict.For(Scored(100, 150, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);

        Assert.False(misses.Cleared);
        Assert.True(misses.PValue > misses.Threshold);

        // A reason exactly on its own bar does not clear: the test asks whether
        // the wins beat what the plans demanded, and matching it is not beating
        // it. 100 wins of 250 against a bar of 40 per cent is exactly the bar.
        var exactly = ReasonVerdict.For(Scored(100, 150, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);

        Assert.False(exactly.Cleared);
        Assert.True(exactly.PValue > 0.3, $"the tail at the bar is {exactly.PValue}, which is not the middle of the distribution.");
    }

    [Fact]
    public void AConditionThatHasFiredAndResolvedNothingSaysSoRatherThanShowingNothing()
    {
        // Section 18's row. A reason that has fired and resolved nothing is not a
        // reason with no record: it is a reason whose record is nought of the
        // minimum, and the run page says that rather than leaving the cell blank.
        var none = ReasonVerdict.For([], ReasonVerdict.LiveFamily);

        Assert.Equal((0, 0), (none.Resolved, none.Sessions));
        Assert.Null(none.Cleared);
        Assert.Equal(ReasonVerdict.BelowTheResolvedMinimum, none.Withheld);

        var record = new ReasonRecord(
            ShortlistSeries.EarningsSoon, 400, 0, 0, 40, RunScreen.MinimumResolvedSetups,
            Sessions: 0, SessionMinimum: ReasonVerdict.MinimumSessions,
            Threshold: none.Threshold, Divisor: none.Divisor);

        var drawn = new MarkRenderer().ReasonRecords([record], RunScreen.Tracks([record]), Rates(1.2, 3.4), 60);

        Assert.Contains("data-outline=\"dashed\"", drawn, StringComparison.Ordinal);
        Assert.Contains("0 of 250 resolved", drawn, StringComparison.Ordinal);
        Assert.Contains("data-fired=\"400\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("per cent of", drawn, StringComparison.Ordinal);
    }
}
