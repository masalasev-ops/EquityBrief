using EquityBrief.Core;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.7: the learned score worked by hand over constructed setups. A single reading's weight on its
// own scale is twice the rows over the rows and the penalty where the edge is twice the reading, the intercept the
// labels' mean less that, and an S&P 400 one more is a shift of one, fitted unpenalised beside it; a reading held by
// too few rows is not weighed and a row missing a reading that is weighed is left out, and the reading that reads ahead
// never is; the score is the same fitted twice and read back from its stored text, and its hash moves with a row; its
// rank and its floors are read off its hundredths; the hooks a registration would set order a night by it and leave the
// floor's share off; and the setups like a pick are matched by hand on both sides of the sentence that the part is not
// told from the rule's record.
// see: A fitted statistical model is a rule
// see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
public partial class FixtureExpectations
{
    // The rows 17.7 adds that this check reaches: section 17's fit, its proposals and the part's match, and section 18's
    // model that cannot load.
    internal static readonly string[] ScoreClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The learned score's fit"),
        CheckReach.Key(Scope.LimitsTable, "The learned score's proposals"),
        CheckReach.Key(Scope.LimitsTable, "Setups like a pick"),
    ];

    // Every row 17.7's score adds, named after phase 16's report until phase 17's own pair is checked: the models' store,
    // the three above, section 18's two, the card's part and the Loop page's score.
    internal static string[] ScoreRows =>
    [
        CheckReach.Key(Scope.StoresTable, "Loop models"),
        .. ScoreClaims,
        CheckReach.Key(Scope.FailureTable, "A model that cannot load"),
        CheckReach.Key(Scope.FailureTable, "A score the tester has not passed"),
        .. Reading.ReadSurface.ScorePageClaims,
    ];

    static DateOnly ScoreSession(int at) => new DateOnly(2020, 1, 1).AddDays(at);

    // Every reading the score may weigh at one value, with those named set apart.
    static double?[] Weighed(double value, params (string Column, double? Value)[] set)
    {
        var readings = new double?[LedgerReadings.Count];

        foreach (var at in RidgeScore.Weighable)
        {
            readings[at] = value;
        }

        foreach (var (column, held) in set)
        {
            readings[LedgerReadings.IndexOf(column)] = held;
        }

        return readings;
    }

    // Four hundred setups on an index, a hundred at each liquidity of 0, 1, 2 and 3, every other reading the same for all,
    // each one's edge twice its liquidity and one more on the S&P 400, at a risk of one move.
    static void FourHundred(ScoreRows rows, string index)
    {
        for (var at = 0; at < 400; at++)
        {
            var liquidity = at % 4;

            rows.Add(index, ScoreSession(at), ScoreSession(at + 10), Weighed(1, ("liquidity", liquidity)), (2.0 * liquidity) + (index == "MID" ? 1 : 0), 1);
        }
    }

    static RidgeModel Fitted(ScoreRows rows) => RidgeScore.Fit(rows, [.. Enumerable.Range(0, rows.Count)]) ?? throw new InvalidOperationException("No score was fitted.");

    [Fact]
    public void TheScoreIsWorkedByHandOverConstructedSetups()
    {
        // The label is the edge in typical moves, the risk floored at one move: half a move reads as one and two double it.
        var labels = new ScoreRows();

        labels.Add("GSPC", ScoreSession(0), ScoreSession(1), Weighed(1), 1.5, 0.5);
        labels.Add("GSPC", ScoreSession(0), ScoreSession(1), Weighed(1), 1.5, 2);

        Assert.Equal((1.5, 3.0), (labels.Label(0), labels.Label(1)));

        // Only liquidity moves, so it alone is weighed: its mean 1.5 and its spread the square root of 1.25 over the four
        // hundred; in standard units its weight is twice the rows times the spread over the rows and the penalty, so on its
        // own scale 2 x 400 / (400 + 10) = 80/41, and the intercept the labels' mean less that times the mean, 3 - 120/41.
        var rows = new ScoreRows();

        FourHundred(rows, "GSPC");

        var model = Fitted(rows);

        Assert.Equal(["liquidity"], model.Readings.Select(reading => LedgerReadings.All[reading].Column));
        Assert.Equal(1.5, model.Means.Single(), 12);
        Assert.Equal(Math.Sqrt(1.25), model.Spreads.Single(), 12);
        Assert.Equal(80.0 / 41.0, model.HookWeights.Single().Weight, 12);
        Assert.Equal(3.0 / 41.0, model.Intercept, 12);
        Assert.Equal([0.0, 0.0], model.IndexShifts);
        Assert.Equal((400, 0.0, 6.0), (model.Rows, model.LabelLow, model.LabelHigh));

        // The same four hundred again on the S&P 400, each one's edge one more: the shift is unpenalised and, the liquidity
        // spread the same on both indices, apart from the weight, so it is one exactly, and the weight over eight hundred
        // rows is 2 x 800 / 810 = 160/81, the intercept 3.5 - 1.5 x 160/81 - 0.5 = 1/27.
        FourHundred(rows, "MID");

        var pooled = Fitted(rows);

        Assert.Equal(160.0 / 81.0, pooled.HookWeights.Single().Weight, 12);
        Assert.Equal(1.0, pooled.IndexShifts[0], 12);
        Assert.Equal(0.0, pooled.IndexShifts[1]);
        Assert.Equal(1.0 / 27.0, pooled.Intercept, 12);
        Assert.Equal(["GSPC", "MID"], pooled.Hundredths.Keys.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void AReadingTooFewHoldIsNotWeighedARowMissingAWeighedOneIsLeftOutAndTheReadingThatReadsAheadNever()
    {
        // Five hundred setups: liquidity and the VIX each move; the relative strength index is held by 474 of them, one
        // short of 95 per cent; the VIX is missing on ten, so they are left out; and the weekdays to the next report equal
        // the edge, which the score never weighs since the history reads it ahead of its session.
        var rows = new ScoreRows();

        for (var at = 0; at < 500; at++)
        {
            var edge = (2.0 * (at % 4)) + (at % 5);

            rows.Add(
                "GSPC",
                ScoreSession(at),
                ScoreSession(at + 10),
                Weighed(1, ("liquidity", at % 4), ("vix", at < 10 ? null : at % 5), ("rsi", at < 26 ? null : at % 3), ("earnings_sessions", edge)),
                edge,
                1);
        }

        var model = Fitted(rows);

        Assert.Equal(["liquidity", "vix"], model.Readings.Select(reading => LedgerReadings.All[reading].Column));
        Assert.Equal(490, model.Rows);
        Assert.DoesNotContain(LedgerReadings.IndexOf("earnings_sessions"), RidgeScore.Weighable);
        Assert.Equal(LedgerReadings.Count - RidgeScore.FamilyOwn.Count - RidgeScore.LookAhead.Count, RidgeScore.Weighable.Count);

        // Fewer than ten rows a weight it could fit, and none is fitted.
        Assert.Null(RidgeScore.Fit(rows, [.. Enumerable.Range(0, RidgeScore.MinimumRows - 1)]));
    }

    [Fact]
    public void TheScoreIsTheSameFittedTwiceAndReadBackFromItsStoredTextAndItsHashMovesWithARow()
    {
        var rows = new ScoreRows();

        FourHundred(rows, "GSPC");
        FourHundred(rows, "MID");

        var model = Fitted(rows);
        var again = Fitted(rows);
        var stored = model.Canonical();
        var read = RidgeModel.Parse(stored);
        var member = Weighed(1, ("liquidity", 2.5));

        Assert.Equal(stored, again.Canonical());
        Assert.Equal(model.Hash, again.Hash);
        Assert.Equal(16, model.Hash.Length);
        Assert.Equal(stored, read.Canonical());
        Assert.Equal(model.Score(member), read.Score(member));
        Assert.Equal(model.Rank("MID", 3.0), read.Rank("MID", 3.0));

        // One row's edge moved moves the hash.
        rows.Add("GSPC", ScoreSession(900), ScoreSession(910), Weighed(1, ("liquidity", 3)), 7, 1);

        Assert.NotEqual(model.Hash, Fitted(rows).Hash);
    }

    [Fact]
    public void TheRankAndTheFloorsAreReadOffTheHundredthsOfTheSetupsTheScoreLearnedOnOnTheIndex()
    {
        var rows = new ScoreRows();

        FourHundred(rows, "GSPC");

        var model = Fitted(rows);
        double Score(double liquidity) => model.Score(Weighed(1, ("liquidity", liquidity)))!.Value;

        // The four hundred scores are a hundred each of 0, 80/41, 160/41 and 240/41: the nearest rank puts hundredths 0 to
        // 25 at the first, 26 to 50 at the second, 51 to 75 at the third and 76 to 100 at the last.
        Assert.Equal(25, model.Rank("GSPC", Score(0)));
        Assert.Equal(50, model.Rank("GSPC", Score(1)));
        Assert.Equal(75, model.Rank("GSPC", Score(2)));
        Assert.Equal(100, model.Rank("GSPC", Score(3)));
        Assert.Equal(0, model.Rank("GSPC", Score(-1)));
        Assert.Null(model.Rank("MID", Score(2)));
        Assert.Equal(Score(0), model.FloorAt("GSPC", 0.2));
        Assert.Equal(Score(1), model.FloorAt("GSPC", 0.4));

        // The hooks a registration would set: tonight's four in the rule's own order, liquidity 0 to 3, are ordered by
        // the score, the highest first, and the lowest two fifths' floor leaves the one under it off.
        var hooks = RuleHooks.Of(model.HookParameters(model.FloorAt("GSPC", 0.4)));

        Assert.Equal([3, 2, 1], hooks.Order([0, 1, 2, 3], at => Weighed(1, ("liquidity", at))));
        Assert.Equal([3, 2, 1, 0], RuleHooks.Of(model.HookParameters(null)).Order([0, 1, 2, 3], at => Weighed(1, ("liquidity", at))));
        Assert.Equal(
            ["the learned score, ordering the list", "the learned score, its lowest fifth left off", "the learned score, its lowest two fifths left off"],
            Enumerable.Range(1, 3).Select(ScoreProcedures.ProposalName));
        Assert.True(ScoreProcedures.Reaches("MID", "pullback") && !ScoreProcedures.Reaches("GSPC", "pullback") && ScoreProcedures.Reaches("GSPC", "drift") && !ScoreProcedures.Reaches("SML", "heavyweight"));
    }

    [Fact]
    public void TheScoresVersionIsThePinOfTheSourceThatFitsIt()
    {
        string[] sources = [File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Core", "Loop", "RidgeScore.cs"))];

        Assert.Equal(RidgeScore.Version, SourcePin.Of(sources, RidgeScore.VersionDeclaration));
        Assert.NotEqual(RidgeScore.Version, SourcePin.Of([sources[0] + "\nvar moved = 1;\n"], RidgeScore.VersionDeclaration));
        Assert.Equal(RidgeScore.Version, SourcePin.Of([sources[0] + "\n// a comment\n"], RidgeScore.VersionDeclaration));
    }

    // A thousand finished setups of a thousand stocks, the k-th reading k on a session of its own.
    static SimilarSetup[] Thousand(Func<int, double> edge) =>
        [.. Enumerable.Range(0, 1000).Select(at => new SimilarSetup(FormattableString.Invariant($"T{at:0000}"), at * 30, [at], edge(at)))];

    [Fact]
    public void TheSetupsLikeAPickAreMatchedByHandOnBothSidesOfTheSentence()
    {
        // The pick reads 999.5, over every setup, so its place is 1; the k-th setup's is (k + 0.5) / 1000, and the nearest
        // 250 are the 750th to the 999th, their distances (j + 0.5) / 1000 for j from 0 to 249, the median 0.125.
        var above = SimilarSetups.Match(Thousand(at => at >= 750 ? 1 : 0), "PICK", [999.5], ["liquidity"])!;

        Assert.Equal((250, 1000), (above.Count, above.RuleSetups));
        Assert.Equal(0.125, above.MedianDistance, 12);

        // Each of them won one risk and the rule's mean is a quarter: the interval is one exactly and leaves the rule's
        // mean out, so the part is told from the rule's record.
        Assert.Equal((1.0, 1.0, 1.0, 0.25), (above.Mean, above.Low, above.High, above.RuleMean));
        Assert.True(above.Distinguishable);

        // The same setups winning and losing a risk in turn: the 250 hold 125 of each, their mean is nought, the rule's
        // mean nought, and the interval 1.645 standard errors either side, the errors the square root of 250/249 over the
        // square root of 250, holds it.
        var even = SimilarSetups.Match(Thousand(at => at % 2 == 0 ? 1 : -1), "PICK", [999.5], ["liquidity"])!;
        var error = Math.Sqrt(250.0 / 249.0) / Math.Sqrt(250);

        Assert.Equal(0.0, even.Mean, 12);
        Assert.Equal(0.0, even.RuleMean, 12);
        Assert.Equal(-SimilarSetups.Z * error, even.Low, 12);
        Assert.Equal(SimilarSetups.Z * error, even.High, 12);
        Assert.False(even.Distinguishable);
    }

    [Fact]
    public void ThePicksOwnStockIsNeverMatchedAndAStockIsMatchedOnceWithinItsRunOfSessions()
    {
        // Thirty stocks reading 0 to 29, edges nought; the pick's own stock reading its own value on ten sessions; and
        // SAME reading one under it on sessions 0, 5 and 21. The pick's stock is left out; SAME is matched at 21, its
        // newest, and at 0, twenty-one sessions before, but not at 5.
        SimilarSetup[] setups =
        [
            .. Enumerable.Range(0, 30).Select(at => new SimilarSetup(FormattableString.Invariant($"O{at:00}"), 100 + at, [at], 0)),
            .. Enumerable.Range(0, 10).Select(at => new SimilarSetup("PICK", at, [100], 5)),
            new SimilarSetup("SAME", 0, [99], 1),
            new SimilarSetup("SAME", 5, [99], 1),
            new SimilarSetup("SAME", 21, [99], 1),
        ];

        var part = SimilarSetups.Match(setups, "PICK", [100], ["liquidity"])!;

        Assert.Equal(32, part.Count);
        Assert.Equal(2.0 / 32.0, part.Mean, 12);

        // Twenty-nine stocks and nothing else are under the thirty a part is drawn from, and no part is drawn.
        Assert.Null(SimilarSetups.Match([.. setups.Take(29)], "PICK", [100], ["liquidity"]));
    }
}
