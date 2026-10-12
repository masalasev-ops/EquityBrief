using EquityBrief.Core;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Loop;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.15: the learned score's challenger, a tree ensemble FastTree fits on the rows the ridge score
// learns on, the same text and hash fitted twice and another seed moving them, its stored trees scored by the core's walk
// as the package scores the rows, read back from its text, and a proposal of it put only where it passes and its total
// edge is above the ridge score's proposal of its kind.
// see: The learned score's challenger is a tree ensemble fitted by FastTree and scored by code from its stored trees, put only where it passes and earns more than the ridge score
public partial class FixtureExpectations
{
    // Twelve hundred constructed setups on two indices: liquidity and the RSI drawn from a fixed sequence, the edge
    // rising with liquidity, falling past an RSI of 70, and a draw of noise on top, so the trees have something to split
    // on and the share of rows each is fitted on changes what they find.
    static ScoreRows TreeRows()
    {
        var rows = new ScoreRows();
        var state = 12345u;

        double Next()
        {
            state = (state * 1103515245u) + 12345u;

            return (state >> 8) / 16777216.0;
        }

        for (var at = 0; at < 1200; at++)
        {
            var liquidity = Math.Round(Next() * 4, 3);
            var rsi = Math.Round(20 + (Next() * 70), 3);
            var edge = (0.5 * liquidity) - (rsi > 70 ? 1 : 0) + (Next() - 0.5);

            rows.Add(at % 3 == 0 ? "MID" : "GSPC", ScoreSession(at), ScoreSession(at + 10), Weighed(1, ("liquidity", liquidity), ("rsi", rsi)), edge, 1);
        }

        return rows;
    }

    // A row's two readings as the catalogue holds them, every other reading at one.
    static double?[] RowReadings(ScoreRows rows, int row)
    {
        var weighable = RidgeScore.Weighable.ToList();

        return Weighed(1, ("liquidity", rows.Reading(row, weighable.IndexOf(LedgerReadings.IndexOf("liquidity")))), ("rsi", rows.Reading(row, weighable.IndexOf(LedgerReadings.IndexOf("rsi")))));
    }

    [Fact]
    public void TheTreesFittedTwiceAreTheSameTextAndHashAndAnotherSeedMovesThem()
    {
        var rows = TreeRows();
        int[] all = [.. Enumerable.Range(0, rows.Count)];
        var first = TreeFit.Fit(rows, all)!;
        var second = TreeFit.Fit(rows, all)!;

        Assert.Equal(first.Canonical(), second.Canonical());
        Assert.Equal(first.Hash, second.Hash);
        Assert.Equal(TreeFit.Trees, first.Trees.Count);
        Assert.Equal(["rsi", "liquidity"], first.Readings.Select(reading => LedgerReadings.All[reading].Column));
        Assert.Equal(1200, first.Rows);

        // The seed draws the share of the rows each tree is fitted on, so another seed fits other trees.
        Assert.NotEqual(first.Hash, TreeFit.Fit(rows, all, TreeFit.Seed + 1)!.Hash);

        // The model read back from its text is the model and scores as it does, and a reading it splits on not held
        // scores nothing.
        var read = TreeModel.Parse(first.Canonical());

        Assert.Equal(first.Canonical(), read.Canonical());
        Assert.Equal(first.Score(Weighed(1, ("liquidity", 3.5), ("rsi", 40))), read.Score(Weighed(1, ("liquidity", 3.5), ("rsi", 40))));
        Assert.Null(read.Score(Weighed(1, ("liquidity", null), ("rsi", 40))));

        // Higher liquidity scores higher and an RSI past 70 lower, as the rows were drawn.
        Assert.True(read.Score(Weighed(1, ("liquidity", 3.5), ("rsi", 40))) > read.Score(Weighed(1, ("liquidity", 0.5), ("rsi", 40))));
        Assert.True(read.Score(Weighed(1, ("liquidity", 2), ("rsi", 40))) > read.Score(Weighed(1, ("liquidity", 2), ("rsi", 85))));

        // A fit one row short of the ridge score's floor is refused, as the ridge's is.
        Assert.Null(TreeFit.Fit(rows, [.. all.Take(RidgeScore.MinimumRows - 1)]));
    }

    [Fact]
    public void TheStoredTreesScoredByTheCoresWalkEqualThePackagesScores()
    {
        var rows = TreeRows();
        var (model, package) = TreeFit.FitBeside(rows, [.. Enumerable.Range(0, rows.Count)]);

        Assert.NotNull(model);
        Assert.Equal(rows.Count, package.Count);

        // Every row, walked from the stored text, within a millionth of the package's own score of it.
        var stored = TreeModel.Parse(model!.Canonical());

        for (var row = 0; row < rows.Count; row++)
        {
            var walked = stored.Score(RowReadings(rows, row))!.Value;

            Assert.True(Math.Abs(walked - package[row]) <= 1e-6 * Math.Max(1, Math.Abs(walked)), FormattableString.Invariant($"row {row}: walked {walked}, the package {package[row]}"));
        }
    }

    [Fact]
    public void ATreeProposalIsPutOnlyWhereItPassesAndItsTotalEdgeIsAboveTheRidgeScoresOfItsKind()
    {
        // Worked by hand over two test years, the rule today at nothing. The ridge score's ordering earns 3 over the span
        // and passes, and the trees' ordering 4 and passes, so it is put; the trees' lowest fifth left off earns 2 against
        // the ridge's 3 of its kind and is held back though it passes; and the trees' lowest two fifths earn 5 against a
        // ridge proposal of its kind that earned 6 and did not pass, which the trees still have to beat.
        var calendar = Weekdays(new DateOnly(2021, 1, 4), 780);
        var folds = LoopFolds.Of(calendar, calendar[^1]);

        LoopEvidence Earning(double total) =>
            new(false, [], [.. folds.Select(fold => new LoopYear(fold.Year, fold.Complete, 1, 1, 0, total / folds.Count))], [0.0], [total]);

        WalkForwardTester.Tested One(string name, double total, bool passes) =>
            new(
                new LoopProposalRead("drift", name, "a change", "the rule", "risks", 20, [], folds.Count, Earning(total)),
                new LoopVerdict(passes ? 0.001 : 0.5, 12, passes, true, 2, 2, 1, 400, true, 0.1));

        var judged = ScoreProcedures.AgainstRidge(
        [
            One(ScoreProcedures.ProposalName(1), 3, true),
            One(ScoreProcedures.TreesName(1), 4, true),
            One(ScoreProcedures.ProposalName(2), 3, true),
            One(ScoreProcedures.TreesName(2), 2, true),
            One(ScoreProcedures.ProposalName(3), 6, false),
            One(ScoreProcedures.TreesName(3), 5, true),
        ]);

        Assert.Equal([true, true, true, false, false, false], judged.Select(one => one.Passed));
        Assert.Equal([null, true, null, false, null, false], judged.Select(one => one.BeatsRidge));
        Assert.Equal("its total edge after costs over the test years, 4, is above the learned score's of its kind, 3", judged[1].Proposal.Finding);
        Assert.Equal("its total edge after costs over the test years, 2, is not above the learned score's of its kind, 3", judged[3].Proposal.Finding);

        // A trees proposal that did not pass is not put whatever it earns, and one with no ridge proposal of its kind
        // beside it is read against none and not put.
        Assert.False(ScoreProcedures.AgainstRidge([One(ScoreProcedures.ProposalName(1), 1, true), One(ScoreProcedures.TreesName(1), 9, false)])[1].Passed);

        var alone = Assert.Single(ScoreProcedures.AgainstRidge([One(ScoreProcedures.TreesName(2), 9, true)]));

        Assert.False(alone.Passed);
        Assert.Equal("the learned score brought no proposal of its kind to be read against", alone.Proposal.Finding);
    }

    // The pin the challenger's rows carry: the source that fits it and the source its trees are read and walked by.
    [Fact]
    public void TheChallengersVersionIsThePinOfTheSourcesThatFitAndWalkIt()
    {
        string[] sources =
        [
            File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "TreeFit.cs")),
            File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Core", "Loop", "TreeScore.cs")),
        ];

        Assert.Equal(TreeFit.Version, SourcePin.Of(sources, TreeFit.VersionDeclaration));
        Assert.NotEqual(TreeFit.Version, SourcePin.Of([sources[0], sources[1] + "\nvar moved = 1;\n"], TreeFit.VersionDeclaration));
        Assert.Equal(TreeFit.Version, SourcePin.Of([sources[0], sources[1] + "\n// a comment\n"], TreeFit.VersionDeclaration));
    }
}
