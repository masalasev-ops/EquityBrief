using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using Microsoft.ML;
using Microsoft.ML.Data;
using Microsoft.ML.Trainers.FastTree;

namespace EquityBrief.Worker.Loop;

// The learned score's challenger fitted: a tree ensemble by ML.NET's FastTree over the rows and readings the ridge score
// learns on, the label each row's clipped edge in typical moves, on one thread with a fixed seed and each tree on a
// seeded share of the rows, every setting fixed before any row is read. The trees are taken out of the fitted ensemble
// as the model the tester walks and the night reads, so nothing the package computes scores a member.
// see: The learned score's challenger is a tree ensemble fitted by FastTree and scored by code from its stored trees, put only where it passes and earns more than the ridge score
public static class TreeFit
{
    public const string VersionDeclaration = "public const string Version =";

    public const string Version = "a1398ec3803b";

    // The trees, the leaves a tree holds at most, the fewest rows a leaf is fitted over, the learning rate, the share
    // of the rows each tree is fitted on and the seed that draws it.
    public const int Trees = 100;

    public const int Leaves = 8;

    public const int LeafRows = 50;

    public const double Rate = 0.1;

    public const double Fraction = 0.8;

    public const int Seed = 17;

    // The settings in the model's text, so a stored model names how it was fitted.
    public static string Settings =>
        FormattableString.Invariant($"fasttree trees {Trees} leaves {Leaves} leaf rows {LeafRows} rate {Rate.ToString("R", CultureInfo.InvariantCulture)} share {Fraction.ToString("R", CultureInfo.InvariantCulture)} seed {Seed} threads 1");

    // The trees fitted over the learning rows, none where the ridge score would fit none over them.
    public static TreeModel? Fit(ScoreRows rows, IReadOnlyList<int> learning, int seed = Seed) => Fitted(rows, learning, seed).Model;

    // The trees fitted with the package's own score of each row they learned on beside them, the rows holding every
    // reading the trees weigh in the learning rows' order, which the core's walk of the stored trees is held to.
    public static (TreeModel? Model, IReadOnlyList<float> Package) FitBeside(ScoreRows rows, IReadOnlyList<int> learning)
    {
        var (model, scored) = Fitted(rows, learning, Seed);

        return (model, scored is { } fitted ? [.. fitted.Trained.Transform(fitted.Data).GetColumn<float>("Score")] : []);
    }

    static (TreeModel? Model, (ITransformer Trained, IDataView Data)? Scored) Fitted(ScoreRows rows, IReadOnlyList<int> learning, int seed)
    {
        if (RidgeScore.Prepare(rows, learning) is not { } learnable)
        {
            return (null, null);
        }

        int[] weighed = [.. learnable.Kept.Select(one => one.Weighable)];
        TreeRow[] data =
        [
            .. learnable.Rows.Select((row, at) => new TreeRow
            {
                Label = (float)learnable.Labels[at],
                Features = [.. weighed.Select(reading => (float)rows.Reading(row, reading))],
            }),
        ];

        var ml = new MLContext(seed);
        var schema = SchemaDefinition.Create(typeof(TreeRow));

        schema[nameof(TreeRow.Features)].ColumnType = new VectorDataViewType(NumberDataViewType.Single, weighed.Length);

        var view = ml.Data.LoadFromEnumerable(data, schema);
        var trained = ml.Regression.Trainers.FastTree(new FastTreeRegressionTrainer.Options
        {
            NumberOfTrees = Trees,
            NumberOfLeaves = Leaves,
            MinimumExampleCountPerLeaf = LeafRows,
            LearningRate = Rate,
            NumberOfThreads = 1,
            Seed = seed,
            BaggingSize = 1,
            BaggingExampleFraction = Fraction,
            FeatureFraction = 1,
        }).Fit(view);

        var ensemble = trained.Model.TrainedTreeEnsemble;
        ScoreTree[] trees =
        [
            .. ensemble.Trees.Select((tree, at) => new ScoreTree(
                [.. tree.NumericalSplitFeatureIndexes],
                [.. tree.NumericalSplitThresholds],
                [.. tree.LeftChild],
                [.. tree.RightChild],
                [.. tree.LeafValues],
                ensemble.TreeWeights[at])),
        ];

        var model = new TreeModel(
            [.. weighed.Select(reading => RidgeScore.Weighable[reading])],
            ensemble.Bias,
            trees,
            learnable.Rows.Count,
            learnable.Low,
            learnable.High,
            Settings,
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal));

        IReadOnlyList<double?> ReadingsOf(int row)
        {
            var readings = new double?[LedgerReadings.Count];

            foreach (var reading in weighed)
            {
                readings[RidgeScore.Weighable[reading]] = rows.Reading(row, reading);
            }

            return readings;
        }

        return (model with { Hundredths = TreeModel.HundredthsOf(model, learnable.Rows.Select(row => (rows.Index(row), ReadingsOf(row)))) }, (trained, view));
    }

    // One learning row as the package reads it.
    public sealed class TreeRow
    {
        public float Label { get; set; }

        public float[] Features { get; set; } = [];
    }
}
