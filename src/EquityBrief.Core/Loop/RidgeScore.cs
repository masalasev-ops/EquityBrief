using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// The ledger's finished setups of one family the score learns on, held a column a reading: each row's index, the session
// it was read on and the session its path ended on, each weighable reading as the ledger stores it, not a number where
// the ledger holds none, and its label, its edge in typical moves.
public sealed class ScoreRows(int capacity = 0)
{
    readonly List<string> indices = new(capacity);
    readonly List<DateOnly> sessions = new(capacity);
    readonly List<DateOnly?> ended = new(capacity);
    readonly List<double> labels = new(capacity);
    readonly List<double>[] columns = [.. RidgeScore.Weighable.Select(_ => new List<double>(capacity))];

    public int Count => labels.Count;

    // A finished setup: its readings in the catalogue's order, and its edge in risks with the stop's distance in typical
    // moves, the label the edge times that distance floored at one move.
    public void Add(string index, DateOnly session, DateOnly? end, IReadOnlyList<double?> readings, double edge, double riskMoves)
    {
        indices.Add(index);
        sessions.Add(session);
        ended.Add(end);
        labels.Add(edge * Math.Max(riskMoves, RidgeScore.RiskFloorMoves));

        for (var at = 0; at < columns.Length; at++)
        {
            columns[at].Add(readings[RidgeScore.Weighable[at]] ?? double.NaN);
        }
    }

    public string Index(int row) => indices[row];

    public DateOnly Session(int row) => sessions[row];

    public DateOnly? Ended(int row) => ended[row];

    public double Label(int row) => labels[row];

    // A weighable reading of a row by its place among the weighable, not a number where it is not held.
    public double Reading(int row, int weighable) => columns[weighable][row];

    // The rows a fold learns on, those whose path ended before its year's first session.
    public IReadOnlyList<int> Learning(LoopFold fold) => new FoldView<int>(fold, Enumerable.Range(0, Count), Session, Ended).Learning;
}

// A fitted score: the readings it weighs in the catalogue's order, each one's mean and spread over the rows it was fitted
// on, each one's weight on that standard scale, each shifted index's shift from the S&P 500's, the intercept, the rows,
// the label's clip, and on each index the hook's score of the rows it learned on at each hundredth.
public sealed record RidgeModel(
    IReadOnlyList<int> Readings,
    IReadOnlyList<double> Means,
    IReadOnlyList<double> Spreads,
    IReadOnlyList<double> Weights,
    IReadOnlyList<double> IndexShifts,
    double Intercept,
    int Rows,
    double LabelLow,
    double LabelHigh,
    IReadOnlyDictionary<string, IReadOnlyList<double>> Hundredths)
{
    // Each reading's weight on its own scale, the weight a rule's score hook carries, by its place in the catalogue.
    public IReadOnlyList<(int Reading, double Weight)> HookWeights =>
        [.. Readings.Select((reading, at) => (reading, Weights[at] / Spreads[at]))];

    // The hooks a rule would be registered with to be ordered by the score, a score under the floor left off where one
    // is given: each weight as a score hook's parameter and the floor as the score's.
    public IReadOnlyDictionary<string, double> HookParameters(double? floor)
    {
        var parameters = HookWeights.ToDictionary(one => RuleHooks.ScorePrefix + LedgerReadings.All[one.Reading].Column, one => one.Weight, StringComparer.Ordinal);

        if (floor is { } least)
        {
            parameters[RuleHooks.ScoreFloorParameter] = least;
        }

        return parameters;
    }

    // A member's score as a rule's hook reads it, none where a weighed reading is not held.
    public double? Score(IReadOnlyList<double?> readings)
    {
        var sum = 0.0;

        for (var at = 0; at < Readings.Count; at++)
        {
            if (readings[Readings[at]] is not { } value)
            {
                return null;
            }

            sum += Weights[at] / Spreads[at] * value;
        }

        return sum;
    }

    // Where a score stands among the scores of the setups the model learned on on an index, in hundredths: the last
    // hundredth at or under it, nought under the lowest; none on an index it learned on no setup of.
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

    // The score at a share of the setups it learned on on an index, the floor a proposal leaves the share under off at.
    public double? FloorAt(string index, double share) =>
        Hundredths.TryGetValue(index, out var hundredths) ? hundredths[(int)Math.Round(share * (hundredths.Count - 1))] : null;

    // The readings in the order of the weight they carry, the largest first, ties in the catalogue's order.
    public IReadOnlyList<(int Reading, double Weight)> Importances =>
        [.. Readings.Select((reading, at) => (Reading: reading, Weight: Weights[at])).OrderByDescending(one => Math.Abs(one.Weight)).ThenBy(one => one.Reading)];

    // The share of the weight the business readings carry, the quarters' and the facts'.
    public double BusinessShare
    {
        get
        {
            var (total, business) = (0.0, 0.0);

            for (var at = 0; at < Readings.Count; at++)
            {
                total += Math.Abs(Weights[at]);
                business += RidgeScore.Business.Contains(LedgerReadings.All[Readings[at]].Column) ? Math.Abs(Weights[at]) : 0;
            }

            return total > 0 ? business / total : 0;
        }
    }

    // The parameters as one text, every figure at its round trip, which the hash is taken over and the night reads the
    // model back from.
    public string Canonical()
    {
        static string Figures(IEnumerable<double> values) => string.Join(",", values.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

        return string.Join(
            "\n",
            [
                "readings=" + string.Join(",", Readings.Select(reading => LedgerReadings.All[reading].Column)),
                "means=" + Figures(Means),
                "spreads=" + Figures(Spreads),
                "weights=" + Figures(Weights),
                "shifts=" + Figures(IndexShifts),
                "intercept=" + Intercept.ToString("R", CultureInfo.InvariantCulture),
                "rows=" + Rows.ToString(CultureInfo.InvariantCulture),
                "clip=" + Figures([LabelLow, LabelHigh]),
                "lambda=" + RidgeScore.Lambda.ToString("R", CultureInfo.InvariantCulture),
                .. RidgeScore.Indices.Where(Hundredths.ContainsKey).Select(index => "hundredths." + index + "=" + Figures(Hundredths[index])),
            ]);
    }

    // The first sixteen hexadecimal characters of the canonical text's SHA-256.
    public string Hash => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical())))[..16].ToLowerInvariant();

    // The model read back from its canonical text.
    public static RidgeModel Parse(string canonical)
    {
        var parts = canonical.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.Split('=', 2)).ToDictionary(pair => pair[0], pair => pair[1], StringComparer.Ordinal);

        static double[] Figures(string text) => text.Length == 0 ? [] : [.. text.Split(',').Select(value => double.Parse(value, CultureInfo.InvariantCulture))];

        int[] readings = parts["readings"].Length == 0 ? [] : [.. parts["readings"].Split(',').Select(LedgerReadings.IndexOf)];
        var clip = Figures(parts["clip"]);

        return new RidgeModel(
            readings,
            Figures(parts["means"]),
            Figures(parts["spreads"]),
            Figures(parts["weights"]),
            Figures(parts["shifts"]),
            double.Parse(parts["intercept"], CultureInfo.InvariantCulture),
            int.Parse(parts["rows"], CultureInfo.InvariantCulture),
            clip[0],
            clip[1],
            RidgeScore.Indices.Where(index => parts.ContainsKey("hundredths." + index)).ToDictionary(index => index, index => (IReadOnlyList<double>)Figures(parts["hundredths." + index]), StringComparer.Ordinal));
    }
}

// The learned score: ridge regression over the catalogue's readings the night hands a rule's hooks, each standardised
// over the rows it is fitted on, the label each finished setup's edge in typical moves clipped at the rows' first and
// ninety-ninth hundredths, the S&P 400's and 600's shifts from the S&P 500's fitted beside the readings since the rows are
// pooled across the three, and a penalty fixed before any row is read. It is solved in closed form by elimination in a
// fixed order, every sum taken row by row in the rows' own order, so the same rows give the same weights on one machine
// and weights within a stated tolerance on another; it draws nothing at random, so it carries no seed.
// see: A fitted statistical model is a rule
public static class RidgeScore
{
    public const string VersionDeclaration = "public const string Version =";

    public const string Version = "be11d99e21a1";

    // The words every proposal of the score opens with, by which a run's proposals of it are told from the others'.
    public const string Proposal = "the learned score";

    // The penalty on the standardised weights, the rows' squared error weighed against it.
    public const double Lambda = 10;

    // The share of the learning rows a reading must be held by to be weighed.
    public const double Coverage = 0.95;

    // The label's clip, its first and ninety-ninth hundredths.
    public const double Clip = 0.01;

    // The least risk in typical moves a label is read at, so a setup whose stop sat close counts for no more than its move.
    public const double RiskFloorMoves = 1;

    // The fewest rows holding every weighed reading a score is fitted over, ten a weight it could fit.
    public const int RowsAWeight = 10;

    public static int MinimumRows => RowsAWeight * (Weighable.Count + Shifted.Count);

    // The indices in the order their hundredths are written, and those whose shift from the S&P 500's is fitted.
    public static IReadOnlyList<string> Indices { get; } = ["GSPC", "MID", "SML"];

    public static IReadOnlyList<string> Shifted { get; } = ["MID", "SML"];

    // A family's own readings, read only for its own setups and never handed to a rule's hooks.
    public static IReadOnlyList<string> FamilyOwn { get; } = ["reward_to_risk", "freshness", "band_strength", "volume_multiple", "range_ratio", "reaction_moves"];

    // The reading the history reads ahead of its session: the weekdays to the next report, which the history reads from
    // the day each report came and a session weeks before it knew only as the calendar then stood.
    public static IReadOnlyList<string> LookAhead { get; } = ["earnings_sessions"];

    // The readings the score may weigh, by their place in the catalogue: those the night hands every member, less the one
    // that reads ahead.
    public static IReadOnlyList<int> Weighable { get; } =
    [
        .. LedgerReadings.All.Select((reading, at) => (reading, at))
            .Where(pair => !FamilyOwn.Contains(pair.reading.Column) && !LookAhead.Contains(pair.reading.Column))
            .Select(pair => pair.at),
    ];

    // The business readings, the quarters' and the facts', whose share of the weight the Loop page states.
    public static IReadOnlyList<string> Business { get; } = ["profit", "coverage", "revenue_growth", "growth_change", "gross_margin_change", "operating_margin_change", "cash_over_income"];

    // The value a share of a sorted set reaches, by its nearest rank.
    public static double Quantile(IReadOnlyList<double> sorted, double share) =>
        sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Count) - 1, 0, sorted.Count - 1)];

    // The score fitted over the learning rows, none where fewer than the minimum hold every reading it would weigh.
    public static RidgeModel? Fit(ScoreRows rows, IReadOnlyList<int> learning)
    {
        if (learning.Count == 0)
        {
            return null;
        }

        // The readings held by enough of the learning rows, and the rows holding every one of them.
        var covered = new List<int>();

        for (var at = 0; at < Weighable.Count; at++)
        {
            var held = 0;

            foreach (var row in learning)
            {
                held += double.IsNaN(rows.Reading(row, at)) ? 0 : 1;
            }

            if (held >= Coverage * learning.Count)
            {
                covered.Add(at);
            }
        }

        var heldRows = learning.Where(row => covered.All(at => !double.IsNaN(rows.Reading(row, at)))).ToArray();

        if (heldRows.Length < MinimumRows || covered.Count == 0)
        {
            return null;
        }

        var sorted = heldRows.Select(rows.Label).Order().ToArray();
        var (low, high) = (Quantile(sorted, Clip), Quantile(sorted, 1 - Clip));
        var labels = heldRows.Select(row => Math.Clamp(rows.Label(row), low, high)).ToArray();

        // Each reading's mean and spread over the rows; one whose rows hold a single value carries nothing and is left out.
        var kept = new List<(int Weighable, double Mean, double Spread)>();

        foreach (var at in covered)
        {
            var sum = 0.0;

            foreach (var row in heldRows)
            {
                sum += rows.Reading(row, at);
            }

            var mean = sum / heldRows.Length;
            var squares = 0.0;

            foreach (var row in heldRows)
            {
                var gap = rows.Reading(row, at) - mean;

                squares += gap * gap;
            }

            var spread = Math.Sqrt(squares / heldRows.Length);

            if (spread > 0)
            {
                kept.Add((at, mean, spread));
            }
        }

        if (kept.Count == 0)
        {
            return null;
        }

        // The columns: each reading standardised, then each shifted index's indicator less its share of the rows.
        var size = kept.Count + Shifted.Count;
        var centres = new double[size];

        for (var at = 0; at < Shifted.Count; at++)
        {
            var on = 0.0;

            foreach (var row in heldRows)
            {
                on += rows.Index(row) == Shifted[at] ? 1 : 0;
            }

            centres[kept.Count + at] = on / heldRows.Length;
        }

        var labelSum = 0.0;

        foreach (var label in labels)
        {
            labelSum += label;
        }

        var meanLabel = labelSum / labels.Length;

        // The normal equations with the penalty on the readings' diagonal, the indices' shifts unpenalised, each sum
        // taken row by row.
        var a = new double[size, size];
        var b = new double[size];
        var x = new double[size];

        for (var place = 0; place < heldRows.Length; place++)
        {
            var row = heldRows[place];

            for (var at = 0; at < kept.Count; at++)
            {
                x[at] = (rows.Reading(row, kept[at].Weighable) - kept[at].Mean) / kept[at].Spread;
            }

            for (var at = 0; at < Shifted.Count; at++)
            {
                x[kept.Count + at] = (rows.Index(row) == Shifted[at] ? 1 : 0) - centres[kept.Count + at];
            }

            var target = labels[place] - meanLabel;

            for (var i = 0; i < size; i++)
            {
                for (var j = i; j < size; j++)
                {
                    a[i, j] += x[i] * x[j];
                }

                b[i] += x[i] * target;
            }
        }

        for (var i = 0; i < size; i++)
        {
            for (var j = 0; j < i; j++)
            {
                a[i, j] = a[j, i];
            }

            if (i < kept.Count)
            {
                a[i, i] += Lambda;
            }
        }

        var solved = Solve(a, b);
        var intercept = meanLabel;

        for (var at = 0; at < kept.Count; at++)
        {
            intercept -= solved[at] * kept[at].Mean / kept[at].Spread;
        }

        for (var at = 0; at < Shifted.Count; at++)
        {
            intercept -= solved[kept.Count + at] * centres[kept.Count + at];
        }

        var model = new RidgeModel(
            [.. kept.Select(one => Weighable[one.Weighable])],
            [.. kept.Select(one => one.Mean)],
            [.. kept.Select(one => one.Spread)],
            solved[..kept.Count],
            solved[kept.Count..],
            intercept,
            heldRows.Length,
            low,
            high,
            new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal));

        // On each index, the hook's score of the rows it learned on at each hundredth.
        var hundredths = new Dictionary<string, IReadOnlyList<double>>(StringComparer.Ordinal);
        var readings = new double?[LedgerReadings.Count];

        foreach (var index in Indices)
        {
            var scores = new List<double>();

            foreach (var row in heldRows)
            {
                if (rows.Index(row) != index)
                {
                    continue;
                }

                for (var at = 0; at < kept.Count; at++)
                {
                    readings[Weighable[kept[at].Weighable]] = rows.Reading(row, kept[at].Weighable);
                }

                scores.Add(model.Score(readings)!.Value);
            }

            if (scores.Count > 0)
            {
                var ordered = scores.Order().ToArray();

                hundredths[index] = [.. Enumerable.Range(0, 101).Select(hundredth => Quantile(ordered, hundredth / 100.0))];
            }
        }

        return model with { Hundredths = hundredths };
    }

    // Gaussian elimination with partial pivoting, in a fixed order; a column with no pivot left at nothing.
    static double[] Solve(double[,] a, double[] b)
    {
        var size = b.Length;
        var m = (double[,])a.Clone();
        var v = (double[])b.Clone();

        for (var column = 0; column < size; column++)
        {
            var pivot = column;

            for (var row = column + 1; row < size; row++)
            {
                if (Math.Abs(m[row, column]) > Math.Abs(m[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (Math.Abs(m[pivot, column]) < 1e-12)
            {
                continue;
            }

            if (pivot != column)
            {
                for (var at = 0; at < size; at++)
                {
                    (m[column, at], m[pivot, at]) = (m[pivot, at], m[column, at]);
                }

                (v[column], v[pivot]) = (v[pivot], v[column]);
            }

            for (var row = column + 1; row < size; row++)
            {
                var factor = m[row, column] / m[column, column];

                for (var at = column; at < size; at++)
                {
                    m[row, at] -= factor * m[column, at];
                }

                v[row] -= factor * v[column];
            }
        }

        var solved = new double[size];

        for (var row = size - 1; row >= 0; row--)
        {
            if (Math.Abs(m[row, row]) < 1e-12)
            {
                continue;
            }

            var sum = v[row];

            for (var at = row + 1; at < size; at++)
            {
                sum -= m[row, at] * solved[at];
            }

            solved[row] = sum / m[row, row];
        }

        return solved;
    }

    // A model's weight in words: the three readings carrying the most, the business readings' share and the rows.
    public static string Words(RidgeModel model) =>
        "the score weighs most "
        + string.Join(", ", model.Importances.Take(3).Select(one => FormattableString.Invariant($"{LedgerReadings.All[one.Reading].Column} {(one.Weight >= 0 ? "higher" : "lower")} ({one.Weight:+0.000;-0.000})")))
        + FormattableString.Invariant($"; the business readings carry {model.BusinessShare * 100:0} per cent of its weight; fitted over {model.Rows} finished setups across the three indices")
        + FormattableString.Invariant($", their edge clipped at {model.LabelLow:+0.00;-0.00} and {model.LabelHigh:+0.00;-0.00} moves");
}
