using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// A fitted score as the tester proposes it and a rule's hooks read it, whichever way it was fitted.
public interface IScoreModel
{
    string Hash { get; }

    int Rows { get; }

    // The readings it weighs, by their place in the catalogue.
    IReadOnlyList<int> Readings { get; }

    // A member's score, none where a reading it weighs is not held.
    double? Score(IReadOnlyList<double?> readings);

    // Where a score stands among the scores of the setups it learned on on an index, in hundredths.
    int? Rank(string index, double score);

    // The score at a share of the setups it learned on on an index.
    double? FloorAt(string index, double share);

    // The hooks a rule would be registered with to be ordered by the score, a score under the floor left off.
    IReadOnlyDictionary<string, double> HookParameters(double? floor);

    string Canonical();
}

// The words every proposal of the learned score's challenger opens with, by which a run's proposals of it are told from
// the ridge score's.
public static class TreeScore
{
    public const string Proposal = "the learned trees";
}

// One tree of the ensemble: each split's reading by its place among the model's readings, its threshold, and the node
// or the leaf either side of it leads to, a leaf written as the complement of its place; each leaf's value; and the
// weight the ensemble gives the tree.
public sealed record ScoreTree(IReadOnlyList<int> Splits, IReadOnlyList<float> Thresholds, IReadOnlyList<int> Left, IReadOnlyList<int> Right, IReadOnlyList<double> Leaves, double Weight)
{
    // The leaf a member's readings reach: at each split the side its reading lies on, at or under the threshold to the
    // left, read as the single-precision figure the trees were fitted on.
    public double Output(IReadOnlyList<float> features)
    {
        if (Splits.Count == 0)
        {
            return Weight * Leaves[0];
        }

        var node = 0;

        while (node >= 0)
        {
            node = features[Splits[node]] <= Thresholds[node] ? Left[node] : Right[node];
        }

        return Weight * Leaves[~node];
    }
}

// The learned score's challenger: the readings it splits on in the catalogue's order, the ensemble's bias and trees, the
// rows it was fitted over, the label's clip, the settings it was fitted at, and on each index the scores of the rows it
// learned on at each hundredth. A member's score is the bias and each tree's weighted leaf, walked here by code from the
// stored trees and never by the package that fitted them, so a stored model scores the same wherever it is read.
// see: The learned score's challenger is a tree ensemble fitted by FastTree and scored by code from its stored trees, put only where it passes and earns more than the ridge score
public sealed record TreeModel(
    IReadOnlyList<int> Readings,
    double Bias,
    IReadOnlyList<ScoreTree> Trees,
    int Rows,
    double LabelLow,
    double LabelHigh,
    string Fit,
    IReadOnlyDictionary<string, IReadOnlyList<double>> Hundredths) : IScoreModel
{
    public double? Score(IReadOnlyList<double?> readings)
    {
        var features = new float[Readings.Count];

        for (var at = 0; at < Readings.Count; at++)
        {
            if (readings[Readings[at]] is not { } value)
            {
                return null;
            }

            features[at] = (float)value;
        }

        var sum = Bias;

        foreach (var tree in Trees)
        {
            sum += tree.Output(features);
        }

        return sum;
    }

    public int? Rank(string index, double score)
    {
        if (!Hundredths.TryGetValue(index, out var hundredths))
        {
            return null;
        }

        var under = 0;

        for (var at = 0; at < hundredths.Count; at++)
        {
            under += hundredths[at] <= score ? 1 : 0;
        }

        return Math.Max(0, under - 1);
    }

    public double? FloorAt(string index, double share) =>
        Hundredths.TryGetValue(index, out var hundredths) ? hundredths[(int)Math.Round(share * (hundredths.Count - 1))] : null;

    // The number a rule's hooks name the model by: the first twelve hexadecimal characters of its hash, which a
    // registration's parameter holds whole.
    public long Key => KeyOf(Hash);

    public static long KeyOf(string hash) => long.Parse(hash[..12], NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    public IReadOnlyDictionary<string, double> HookParameters(double? floor)
    {
        var parameters = new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.TreesParameter] = Key };

        if (floor is { } least)
        {
            parameters[RuleHooks.ScoreFloorParameter] = least;
        }

        return parameters;
    }

    // How often each reading is split on across the trees, the most first, ties in the catalogue's order.
    public IReadOnlyList<(int Reading, int Splits)> Importances =>
    [
        .. Readings.Select((reading, at) => (Reading: reading, Splits: Trees.Sum(tree => tree.Splits.Count(split => split == at))))
            .Where(one => one.Splits > 0)
            .OrderByDescending(one => one.Splits)
            .ThenBy(one => one.Reading),
    ];

    // The share of the splits the business readings take.
    public double BusinessShare
    {
        get
        {
            var importances = Importances;
            var total = importances.Sum(one => one.Splits);

            var business = importances.Where(one => RidgeScore.Business.Contains(LedgerReadings.All[one.Reading].Column)).Sum(one => one.Splits);

            return total > 0 ? 1.0 * business / total : 0;
        }
    }

    public string Canonical()
    {
        static string Figures(IEnumerable<double> values) => string.Join(",", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

        static string Thresholds(IEnumerable<float> values) => string.Join(",", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

        static string Places(IEnumerable<int> values) => string.Join(",", values.Select(value => value.ToString(CultureInfo.InvariantCulture)));

        return string.Join(
            "\n",
            [
                "readings=" + string.Join(",", Readings.Select(reading => LedgerReadings.All[reading].Column)),
                "bias=" + Bias.ToString("R", CultureInfo.InvariantCulture),
                "rows=" + Rows.ToString(CultureInfo.InvariantCulture),
                "clip=" + Figures([LabelLow, LabelHigh]),
                "fit=" + Fit,
                .. Trees.Select((tree, at) => FormattableString.Invariant($"tree.{at}=")
                    + string.Join("|", tree.Weight.ToString("R", CultureInfo.InvariantCulture), Places(tree.Splits), Thresholds(tree.Thresholds), Places(tree.Left), Places(tree.Right), Figures(tree.Leaves))),
                .. RidgeScore.Indices.Where(Hundredths.ContainsKey).Select(index => "hundredths." + index + "=" + Figures(Hundredths[index])),
            ]);
    }

    public string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical())))[..16].ToLowerInvariant();

    // The model read back from its canonical text.
    public static TreeModel Parse(string canonical)
    {
        var parts = canonical.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        static double[] Figures(string text) => text.Length == 0 ? [] : [.. text.Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture))];

        static float[] Thresholds(string text) => text.Length == 0 ? [] : [.. text.Split(',').Select(value => float.Parse(value, CultureInfo.InvariantCulture))];

        static int[] Places(string text) => text.Length == 0 ? [] : [.. text.Split(',').Select(value => int.Parse(value, CultureInfo.InvariantCulture))];

        int[] readings = parts["readings"].Length == 0 ? [] : [.. parts["readings"].Split(',').Select(LedgerReadings.IndexOf)];
        var clip = Figures(parts["clip"]);
        var trees = new List<ScoreTree>();

        for (var at = 0; parts.TryGetValue(FormattableString.Invariant($"tree.{at}"), out var tree); at++)
        {
            var fields = tree.Split('|');

            trees.Add(new ScoreTree(Places(fields[1]), Thresholds(fields[2]), Places(fields[3]), Places(fields[4]), Figures(fields[5]), double.Parse(fields[0], CultureInfo.InvariantCulture)));
        }

        return new TreeModel(
            readings,
            double.Parse(parts["bias"], CultureInfo.InvariantCulture),
            trees,
            int.Parse(parts["rows"], CultureInfo.InvariantCulture),
            clip[0],
            clip[1],
            parts["fit"],
            RidgeScore.Indices.Where(index => parts.ContainsKey("hundredths." + index)).ToDictionary(index => index, index => (IReadOnlyList<double>)Figures(parts["hundredths." + index]), StringComparer.Ordinal));
    }

    // The scores of the rows a model learned on, on each index, at each hundredth.
    public static IReadOnlyDictionary<string, IReadOnlyList<double>> HundredthsOf(IScoreModel model, IEnumerable<(string Index, IReadOnlyList<double?> Readings)> rows)
    {
        var held = rows.ToArray();
        var hundredths = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);

        foreach (var index in RidgeScore.Indices)
        {
            var ordered = held.Where(row => row.Index == index).Select(row => model.Score(row.Readings)!.Value).Order().ToArray();

            if (ordered.Length > 0)
            {
                hundredths[index] = [.. Enumerable.Range(0, 101).Select(hundredth => RidgeScore.Quantile(ordered, hundredth / 100.0))];
            }
        }

        return hundredths;
    }

    // A model's splits in words: the three readings split on most, the business readings' share, the trees and the rows.
    public static string Words(TreeModel model) =>
        "the trees split most on "
        + string.Join(", ", model.Importances.Take(3).Select(one => FormattableString.Invariant($"{LedgerReadings.All[one.Reading].Column} ({one.Splits})")))
        + FormattableString.Invariant($"; the business readings take {model.BusinessShare * 100:0} per cent of the splits; {model.Trees.Count} trees fitted over {model.Rows} finished setups across the three indices")
        + FormattableString.Invariant($", their edge clipped at {model.LabelLow:+0.00;-0.00} and {model.LabelHigh:+0.00;-0.00} moves");
}
