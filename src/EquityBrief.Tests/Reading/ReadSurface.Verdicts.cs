using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.App;
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

            Assert.Equal((state, one.GetProperty("resolved").GetInt32()), (state, verdict.Scored));
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
                $"{ReasonVerdict.MinimumResolved} resolved setups that set a bar, spread over at least {ReasonVerdict.MinimumSessions} distinct listing sessions"),
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

        // And the figures the projection hands every record it draws are these
        // constants rather than copies of them.
        Assert.All(
            RunScreen.Records([], []),
            record => Assert.Equal(
                (ReasonVerdict.MinimumResolved, ReasonVerdict.MinimumSessions, ReasonVerdict.Significance),
                (record.Minimum, record.SessionMinimum, record.Significance)));
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
            new(won ? ForwardReturnSeries.Win : ForwardReturnSeries.Loss, breakEven, night.AddDays(-(++at % Math.Max(sessions, 1))));

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

        var above = ReasonVerdict.For(Scored(126, 125, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);

        Assert.Equal((249, 250, 251), (below.Scored, at.Scored, above.Scored));
        Assert.Null(below.Cleared);
        Assert.Null(below.PValue);
        Assert.Equal(ReasonVerdict.BelowTheResolvedMinimum, below.Withheld);

        Assert.NotNull(at.Cleared);
        Assert.NotNull(at.PValue);
        Assert.Equal(ReasonVerdict.Shown, at.Withheld);
        Assert.Equal(ReasonVerdict.Shown, above.Withheld);

        // The night floor, at its own boundary, over a population that clears the
        // row floor comfortably. This is the case a single floor would have drawn
        // a verdict for: 300 resolved setups arriving on 59 sessions.
        var fewNights = ReasonVerdict.For(Scored(150, 150, 40, ReasonVerdict.MinimumSessions - 1), ReasonVerdict.LiveFamily);
        var enough = ReasonVerdict.For(Scored(150, 150, 40, ReasonVerdict.MinimumSessions), ReasonVerdict.LiveFamily);
        var more = ReasonVerdict.For(Scored(150, 150, 40, ReasonVerdict.MinimumSessions + 1), ReasonVerdict.LiveFamily);

        Assert.Equal((59, 60, 61), (fewNights.Sessions, enough.Sessions, more.Sessions));
        Assert.Equal(300, fewNights.Scored);
        Assert.Null(fewNights.Cleared);
        Assert.Equal(ReasonVerdict.BelowTheSessionMinimum, fewNights.Withheld);
        Assert.NotNull(enough.Cleared);
        Assert.NotNull(more.Cleared);

        // Every verdict names its divisor and the threshold that divisor set,
        // withheld or not, because a reader has to be able to see how hard the
        // test would be before deciding whether the wait is worth it.
        // see: The significance threshold is divided by the family size, and the divisor is shown
        foreach (var verdict in new[] { below, at, above, fewNights, enough, more })
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
            .Concat(Enumerable.Range(0, 20).Select(_ => new ReasonVerdict.ScoredSetup(ForwardReturnSeries.Loss, null, new DateOnly(2026, 9, 4))))
            .ToArray();

        Assert.Equal(250, ReasonVerdict.For(mixed, ReasonVerdict.LiveFamily).Scored);
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

        Assert.Equal((0, 0), (none.Scored, none.Sessions));
        Assert.Null(none.Cleared);
        Assert.Equal(ReasonVerdict.BelowTheResolvedMinimum, none.Withheld);

        var record = new ReasonRecord(
            ShortlistSeries.EarningsSoon, 400, 0, 0, 40, ReasonVerdict.MinimumResolved,
            Sessions: 0, SessionMinimum: ReasonVerdict.MinimumSessions,
            Threshold: none.Threshold, Divisor: none.Divisor);

        var drawn = new MarkRenderer().ReasonRecords([record], RunScreen.Tracks([record]), Rates(1.2, 3.4), 60);

        Assert.Contains("data-outline=\"dashed\"", drawn, StringComparison.Ordinal);
        Assert.Contains("0 of 250 resolved", drawn, StringComparison.Ordinal);
        Assert.Contains("data-fired=\"400\"", drawn, StringComparison.Ordinal);
        Assert.DoesNotContain("per cent of", drawn, StringComparison.Ordinal);
    }

    // The projection's record for one reason over constructed setups, every one
    // listed under that reason on the session it carries.
    static ReasonRecord RecordOf(IReadOnlyList<ResolvedSetup> setups, string reason = ShortlistSeries.AtEntryZone) =>
        RunScreen.Records(Listings(setups, reason), setups).Single(one => one.Reason == reason);

    // Unresolved setups at a bar, on the given sessions' own days or one each on
    // days of their own past them.
    static IReadOnlyList<ResolvedSetup> Unresolved(DateOnly night, int count, double breakEven, int sessions, bool elsewhere) =>
    [
        .. Enumerable.Range(0, count).Select(at => new ResolvedSetup(
            $"U{at:0000}",
            night.AddDays(-(elsewhere ? sessions + at : at % sessions)),
            ForwardReturnSeries.Unresolved,
            breakEven)),
    ];

    [Fact]
    public void EveryPopulationCaseInTheExpectationIsCountedOverTheResolvedSetupsThatSetABarAndNoOthers()
    {
        // The derived expectation for the verdict's set, through the projection and onto the run page,
        // so a count, a floor, a share or a tail taken over any other set is red here.
        // see: A reason's share, verdict and both floors are counted over the resolved setups that set a bar
        var expectation = Expected("reason-verdicts");
        var cases = expectation.GetProperty("population").GetProperty("cases").EnumerateArray().ToArray();

        Assert.True(cases.Length >= 4, $"The expectation states {cases.Length} population case(s), expected at least 4.");

        var display = expectation.GetProperty("display").EnumerateArray()
            .ToDictionary(one => one.GetProperty("state").GetString()!, StringComparer.Ordinal);

        var night = new DateOnly(2026, 9, 4);
        var marks = new MarkRenderer();

        foreach (var one in cases)
        {
            var what = one.GetProperty("case").GetString();
            var sessions = one.GetProperty("sessions").GetInt32();
            var breakEven = one.GetProperty("breakEven").GetDouble();

            IReadOnlyList<ResolvedSetup> setups =
            [
                .. Setups(night, one.GetProperty("wins").GetInt32(), one.GetProperty("losses").GetInt32(), breakEven, one.GetProperty("withoutABar").GetInt32(), sessions),
                .. Unresolved(night, one.GetProperty("unresolved").GetInt32(), breakEven, sessions, one.GetProperty("unresolvedOnOtherSessions").GetBoolean()),
            ];

            var record = RecordOf(setups);

            Assert.Equal((what, one.GetProperty("resolved").GetInt32()), (what, record.Resolved));
            Assert.Equal((what, one.GetProperty("scored").GetInt32()), (what, record.Scored));
            Assert.Equal((what, one.GetProperty("scoredSessions").GetInt32()), (what, record.Sessions));
            Assert.Equal((what, one.GetProperty("short").GetString()), (what, record.Withheld));
            Assert.Equal((what, one.GetProperty("unresolved").GetInt32()), (what, record.Unresolved));

            var verdict = one.GetProperty("verdict");
            var share = one.GetProperty("share");

            Assert.Equal((what, verdict.ValueKind == JsonValueKind.Null ? (bool?)null : verdict.GetBoolean()), (what, record.Cleared));
            Assert.Equal((what, share.ValueKind == JsonValueKind.Null ? (double?)null : share.GetDouble()), (what, record.Share));

            // Where the case is a display case with setups added that are in no
            // figure, its tail is that case's tail to the last bit.
            if (one.TryGetProperty("sameTailAs", out var same))
            {
                var shown = display[same.GetString()!];
                var alone = ReasonVerdict.For(
                    Scored(shown.GetProperty("wins").GetInt32(), shown.GetProperty("losses").GetInt32(), shown.GetProperty("breakEven").GetDouble(), shown.GetProperty("sessions").GetInt32()),
                    ReasonVerdict.LiveFamily);

                Assert.Equal((what, alone.PValue), (what, record.PValue));
            }

            var drawn = marks.ReasonRecords([record], RunScreen.Tracks([record]), Rates(1.2, 3.4), 60);

            Assert.Contains(one.GetProperty("drawn").GetString()!, drawn, StringComparison.Ordinal);

            if (record.Cleared is null)
            {
                Assert.Contains("data-verdict=\"none\"", drawn, StringComparison.Ordinal);
                Assert.DoesNotContain("per cent of", drawn, StringComparison.Ordinal);
                Assert.DoesNotContain("Does not clear", drawn, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void AnUnresolvedSetupIsCountedInItsOwnColumnAndInNoFloorNoShareAndNoTail()
    {
        // 13.3's "reported in its own column and excluded from the rate", through
        // the projection the page reads rather than at the arithmetic alone.
        // see: An unresolved setup is never a win
        var night = new DateOnly(2026, 9, 4);

        // The set, named once: a win or a loss that set a bar, and nothing else.
        Assert.Equal(
            (true, true, false, false, false),
            (ForwardReturnSeries.IsScored(ForwardReturnSeries.Win, 40),
                ForwardReturnSeries.IsScored(ForwardReturnSeries.Loss, 40),
                ForwardReturnSeries.IsScored(ForwardReturnSeries.Loss, null),
                ForwardReturnSeries.IsScored(ForwardReturnSeries.Unresolved, 40),
                ForwardReturnSeries.IsScored(ForwardReturnSeries.NeverEntered, null)));

        var clearing = Setups(night, wins: 125, losses: 125, breakEven: 40d);
        var alone = RecordOf(clearing);
        var withThirty = RecordOf([.. clearing, .. Unresolved(night, 30, 40d, ReasonVerdict.MinimumSessions, elsewhere: false)]);

        // Its own column, and its own segment of the track.
        Assert.Equal(30, withThirty.Unresolved);
        Assert.Equal((125, 125, 30), (RunScreen.Tracks([withThirty])[0].Won, RunScreen.Tracks([withThirty])[0].Lost, RunScreen.Tracks([withThirty])[0].Unresolved));

        // And in no figure: the counts, the sessions, the share and the tail are
        // the ones the population without them has.
        Assert.Equal((250, 250, 60), (withThirty.Resolved, withThirty.Scored, withThirty.Sessions));
        Assert.Equal(alone.Share, withThirty.Share);
        Assert.Equal(alone.PValue, withThirty.PValue);
        Assert.True(withThirty.Cleared);

        // Not a case that cannot fail: the same thirty read as losing draws put
        // the tail over the threshold, so a verdict counting them would not clear.
        var counted = PoissonBinomial.UpperTail([.. Enumerable.Repeat(0.4, 280)], 125);

        Assert.True(counted > withThirty.Threshold, $"125 wins in 280 at a bar of 40 per cent has a tail of {counted}, which clears {withThirty.Threshold} and proves nothing.");

        // A session only unresolved setups arrived on is not one the record stands on.
        var fewNights = Setups(night, wins: 150, losses: 100, breakEven: 40d, sessions: 30);
        var shortOfNights = RecordOf([.. fewNights, .. Unresolved(night, 30, 40d, sessions: 30, elsewhere: true)]);

        Assert.Equal((30, ReasonVerdict.BelowTheSessionMinimum), (shortOfNights.Sessions, shortOfNights.Withheld));
        Assert.False(shortOfNights.HasEarnedAVerdict);
        Assert.Null(shortOfNights.Cleared);
        Assert.Null(shortOfNights.Share);

        // And at the verdict itself, whoever hands it the setups.
        var direct = ReasonVerdict.For(
            [
                .. Scored(125, 125, 40, ReasonVerdict.MinimumSessions),
                .. Enumerable.Range(0, 30).Select(at => new ReasonVerdict.ScoredSetup(ForwardReturnSeries.Unresolved, 40, night.AddDays(-(100 + at)))),
            ],
            ReasonVerdict.LiveFamily);

        Assert.Equal((250, 60, (bool?)true), (direct.Scored, direct.Sessions, direct.Cleared));
    }

    [Fact]
    public void ARecordEarnsAVerdictExactlyWhereTheTestRanAndAWithheldOneIsNeverDrawnAsFailing()
    {
        // Every population either side of each floor, holding setups that would fill a floor if counted,
        // asserted against the rule and against the verdict in both directions.
        // see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
        var night = new DateOnly(2026, 9, 4);
        var marks = new MarkRenderer();
        var (populations, earned, withheld) = (0, 0, 0);

        foreach (var wins in new[] { 124, 125, 126 })
        {
            foreach (var withoutABar in new[] { 0, 1, 6 })
            {
                foreach (var unresolved in new[] { 0, 30 })
                {
                    foreach (var sessions in new[] { ReasonVerdict.MinimumSessions - 1, ReasonVerdict.MinimumSessions, ReasonVerdict.MinimumSessions + 1 })
                    {
                        var record = RecordOf(
                        [
                            .. Setups(night, wins, 125, 40d, withoutABar, sessions),
                            .. Unresolved(night, unresolved, 40d, sessions, elsewhere: true),
                        ]);

                        var which = (wins, withoutABar, unresolved, sessions);

                        // The wins and the 125 losses carrying a bar, spread over
                        // every one of the sessions.
                        var scored = wins + 125;
                        var expected = scored >= ReasonVerdict.MinimumResolved && sessions >= ReasonVerdict.MinimumSessions;

                        Assert.Equal((which, scored, sessions), (which, record.Scored, record.Sessions));
                        Assert.Equal((which, expected), (which, record.HasEarnedAVerdict));
                        Assert.Equal((which, record.HasEarnedAVerdict), (which, record.Cleared is not null));
                        Assert.Equal((which, record.HasEarnedAVerdict), (which, record.PValue is not null));

                        var drawn = marks.ReasonRecords([record], RunScreen.Tracks([record]), Rates(1.2, 3.4), 60);

                        if (record.HasEarnedAVerdict)
                        {
                            earned++;
                            Assert.Contains(record.Cleared is true ? "data-verdict=\"cleared\"" : "data-verdict=\"not cleared\"", drawn, StringComparison.Ordinal);
                        }
                        else
                        {
                            withheld++;
                            Assert.Contains("data-verdict=\"none\"", drawn, StringComparison.Ordinal);
                            Assert.DoesNotContain("not cleared", drawn, StringComparison.Ordinal);
                            Assert.DoesNotContain("Does not clear", drawn, StringComparison.Ordinal);
                        }

                        populations++;
                    }
                }
            }
        }

        // Stated before the loop: 3 x 3 x 2 x 3 populations, and a verdict needs 125 or 126 wins on 60 or
        // 61 sessions, which is 2 x 3 x 2 x 2 of them.
        Assert.Equal((54, 24, 30), (populations, earned, withheld));
    }

    [Fact]
    public void APValueIsDrawnToThePagesFivePlacesAndOneBelowThemAsTheBoundItLiesUnder()
    {
        // A tail too small for five places is drawn as the bound it lies under,
        // and the attribute carries the tail as computed, to the last bit.
        var expectation = Expected("reason-verdicts").GetProperty("drawnProbability");
        var cases = expectation.GetProperty("cases").EnumerateArray().ToArray();

        Assert.True(cases.Length >= 2, $"The expectation states {cases.Length} drawn probability case(s), expected at least 2.");

        var night = new DateOnly(2026, 9, 4);

        foreach (var one in cases)
        {
            var what = one.GetProperty("case").GetString();
            var record = RecordOf(Setups(
                night,
                one.GetProperty("wins").GetInt32(),
                one.GetProperty("losses").GetInt32(),
                one.GetProperty("breakEven").GetDouble(),
                sessions: one.GetProperty("sessions").GetInt32()));

            Assert.True(record.HasEarnedAVerdict, what);

            if (one.TryGetProperty("powerOfTwo", out var power))
            {
                Assert.Equal((what, Math.ScaleB(1d, power.GetInt32())), (what, record.PValue!.Value));
            }

            if (one.TryGetProperty("tail", out var tail))
            {
                Assert.Equal((what, tail.GetDouble()), (what, Math.Round(record.PValue!.Value, 10)));
            }

            var drawn = new MarkRenderer().ReasonRecords([record], RunScreen.Tracks([record]), Rates(1.2, 3.4), 60);

            Assert.Contains($"on an exact one-sided p {one.GetProperty("drawn").GetString()}</td>", drawn, StringComparison.Ordinal);
            Assert.DoesNotContain("p of 0<", drawn, StringComparison.Ordinal);

            var attribute = Regex.Match(drawn, "data-p-value=\"([^\"]+)\"").Groups[1].Value;

            Assert.Equal((what, record.PValue!.Value), (what, double.Parse(attribute, NumberStyles.Float, CultureInfo.InvariantCulture)));
        }
    }

    [Fact]
    public void TheLevelDrawnBesideTheDivisorIsTheOneTheRecordCarries()
    {
        // A record at a level the build does not use, so a renderer drawing a
        // level of its own beside the divisor is red here.
        var record = new ReasonRecord(
            ShortlistSeries.CrossedALevel, 900, 150, 150, 0, ReasonVerdict.MinimumResolved,
            Scored: 300, Share: 50d, BreakEven: 40d, Sessions: 60, SessionMinimum: ReasonVerdict.MinimumSessions,
            Cleared: false, PValue: 0.002d, Threshold: 0.01 / 6, Divisor: 6,
            Withheld: ReasonVerdict.Shown, Significance: 0.01);

        var drawn = new MarkRenderer().ReasonRecords([record], RunScreen.Tracks([record]), Rates(1.2, 3.4), 60);

        // 0.01 over 6 is 0.001666..., which is 0.00167 to five places.
        Assert.Contains("Does not clear at 0.00167, which is 0.01 divided by a family of 6, on an exact one-sided p of 0.002", drawn, StringComparison.Ordinal);
    }

    [Fact]
    public void TonightsListDrawsTheThreeTogetherBesideAReasonThatEarnedAVerdictAndNamesTheFloorThatIsShort()
    {
        // 15.11 says wherever the reason appears: one reason clears both floors beside six losses that
        // set no bar, and the other has the rows and not the nights.
        // see: A reason's record is displayed, beside the reason and never beside the name
        var night = new DateOnly(2026, 9, 4);

        var atEntry = Setups(night, wins: 150, losses: 100, breakEven: 40d, withoutABar: 6);
        var crossed = Setups(night.AddDays(-400), wins: 150, losses: 150, breakEven: 40d, sessions: ReasonVerdict.MinimumSessions - 1);

        var records = RunScreen.Records(
            [.. Listings(atEntry, ShortlistSeries.AtEntryZone), .. Listings(crossed, ShortlistSeries.CrossedALevel)],
            [.. atEntry, .. crossed]);

        var list = new MarkRenderer().TonightList(
            [new ListingCell("ZZZZ", night, 2, 0, 10m, [ShortlistSeries.AtEntryZone, ShortlistSeries.CrossedALevel])],
            SinglePageApp.TonightDrawn,
            records);

        // Worked from the construction: 150 of the 250 that set a bar is 60 per
        // cent, and the six that set none are in no figure.
        var earned = Assert.Single(Regex.Matches(list, "<span class=\"record\" [^>]*>[^<]*</span>")).Value;

        Assert.Contains("data-verdict=\"cleared\"", earned, StringComparison.Ordinal);
        Assert.Contains("data-share=\"60\" data-scored=\"250\" data-break-even=\"40\"", earned, StringComparison.Ordinal);
        Assert.Contains("60 per cent of 250 resolved setups that set a bar reached target before stop, against the 40 per cent those setups demanded", earned, StringComparison.Ordinal);
        Assert.DoesNotContain("of 256", list, StringComparison.Ordinal);
        Assert.DoesNotContain("data-verdict=\"due\"", list, StringComparison.Ordinal);

        var dashed = Assert.Single(Regex.Matches(list, "<span class=\"record not-measured\" [^>]*>[^<]*</span>")).Value;

        Assert.Contains("data-short=\"sessions\"", dashed, StringComparison.Ordinal);
        Assert.Contains("300 of 250 resolved setups that set a bar, over 59 of 60 listing session(s)", dashed, StringComparison.Ordinal);
        Assert.DoesNotContain("per cent", dashed, StringComparison.Ordinal);

        // Each inside its own reason's disclosure, and neither beside the name.
        Assert.Matches("<details class=\"reason\" data-reason=\"at entry zone\"><summary>entry</summary>(?:(?!</details>).)*<span class=\"record\" ", list);
        Assert.Matches("<details class=\"reason\" data-reason=\"crossed a level\"><summary>crossed</summary>(?:(?!</details>).)*<span class=\"record not-measured\" ", list);
    }
}
