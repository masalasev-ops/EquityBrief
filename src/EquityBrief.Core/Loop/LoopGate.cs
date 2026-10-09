using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Loop;

// One unit a proposal's paired difference is summed from: the session it was entered on, or a book's month opened on,
// the proposal's figure or none where it held nothing there, and the current rule's the same.
public readonly record struct PairedUnit(int Session, double? Proposed, double? Current)
{
    // The difference, a side that held nothing counting as nothing.
    public double Difference => (Proposed ?? 0) - (Current ?? 0);
}

// The gate a proposal passes: the paired difference in total edge after costs between the proposal and the index's
// current rule, summed over blocks of 63 sessions keyed on the session each trade was entered on, a block counted once
// it holds a unit of either side and its whole outcome window has closed, studentised as a candidate's record is and
// read against every arrangement of the blocks' signs. Where several proposals are tested for one rule in a run, Romano
// and Wolf's step-down reads them together, one sign vector applied to every proposal's blocks at once, each adjusted
// p-value the share of arrangements whose largest statistic among the proposals still standing reaches the proposal's
// own, never below the one before it. The bar is 0.05 over 12 a run.
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
// see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
public static class LoopGate
{
    // The level a run is held to, and the runs a year it is divided over.
    public const double Level = 0.05;

    public const int RunsAYear = 12;

    public static double Bar => Level / RunsAYear;

    // Every sign vector is enumerated up to this many blocks; past it a fixed seed's draws of as many vectors as this
    // many blocks hold are read, the observed arrangement counted among them.
    public const int Enumerated = 24;

    public const int Seed = 20261009;

    const double Scale = 1e9;

    // The blocks of one proposal: each block's sum of the paired difference, the blocks counted in the order they fall,
    // from the session the test span opens on, a block counted where it holds a unit of either side and the history
    // holds the cap's sessions after its last session.
    public static IReadOnlyList<(int Block, double Sum)> Blocks(IEnumerable<PairedUnit> units, int opens, int newest, int cap)
    {
        var sums = new SortedDictionary<int, double>();

        foreach (var unit in units)
        {
            if (unit.Session < opens || (unit.Proposed is null && unit.Current is null))
            {
                continue;
            }

            var block = (unit.Session - opens) / Returns.Blocks.Sessions;
            var last = opens + ((block + 1) * Returns.Blocks.Sessions) - 1;

            if (newest - last < cap)
            {
                continue;
            }

            sums[block] = sums.GetValueOrDefault(block) + unit.Difference;
        }

        return [.. sums.Select(pair => (pair.Key, pair.Value))];
    }

    // The adjusted p-value of each proposal tested for one rule in a run, in the order given; each proposal's sums are
    // over the same blocks. A single proposal's is its own sign-flip p-value.
    public static double[] Adjusted(IReadOnlyList<IReadOnlyList<double>> proposals)
    {
        if (proposals.Count == 0)
        {
            return [];
        }

        var q = proposals[0].Count;

        if (q < 2 || proposals.Any(sums => sums.Count != q))
        {
            throw new ArgumentException(
                "The step-down reads every proposal over the same blocks and at least two of them: a statistic over one block has no scatter to divide by.",
                nameof(proposals));
        }

        // Each block's sum held as a whole number of billionths, so a signed sum is the same figure whichever order its
        // flips arrived in and two arrangements the arithmetic makes equal are read as the tie they are.
        var k = proposals.Count;
        var held = proposals.Select(sums => sums.Select(sum => (long)Math.Round(sum * Scale)).ToArray()).ToArray();
        var squares = held.Select(sums => sums.Sum(sum => (sum / Scale) * (sum / Scale))).ToArray();
        var observed = Enumerable.Range(0, k).Select(at => Studentised(held[at].Sum() / Scale, squares[at], q)).ToArray();
        var order = Enumerable.Range(0, k).OrderByDescending(at => observed[at]).ThenBy(at => at).ToArray();
        var reached = new long[k];
        var signed = new long[k];
        var arrangements = 0L;

        void Count()
        {
            var standing = double.NegativeInfinity;

            for (var step = k - 1; step >= 0; step--)
            {
                var at = order[step];

                standing = Math.Max(standing, Studentised(signed[at] / Scale, squares[at], q));

                if (SignFlip.AtLeast(standing, observed[at]))
                {
                    reached[step]++;
                }
            }

            arrangements++;
        }

        for (var at = 0; at < k; at++)
        {
            signed[at] = held[at].Sum();
        }

        Count();

        if (q <= Enumerated)
        {
            for (long code = 1; code < 1L << q; code++)
            {
                var block = System.Numerics.BitOperations.TrailingZeroCount(code);
                var negative = ((code ^ (code >> 1)) & (1L << block)) != 0;

                for (var at = 0; at < k; at++)
                {
                    signed[at] += (negative ? -2 : 2) * held[at][block];
                }

                Count();
            }
        }
        else
        {
            var random = new Random(Seed);

            for (long draw = 1; draw < 1L << Enumerated; draw++)
            {
                Array.Clear(signed);

                for (var block = 0; block < q; block++)
                {
                    var positive = random.Next(2) == 0;

                    for (var at = 0; at < k; at++)
                    {
                        signed[at] += positive ? held[at][block] : -held[at][block];
                    }
                }

                Count();
            }
        }

        var adjusted = new double[k];
        var floor = 0.0;

        for (var step = 0; step < k; step++)
        {
            floor = Math.Max(floor, 1.0 * reached[step] / arrangements);
            adjusted[order[step]] = floor;
        }

        return adjusted;
    }

    // The studentised mean of blocks whose signed sum and sum of squares are given, as the sign-flip statistic reads it:
    // a sign flipped leaves the sum of squares as it was, so the statistic follows the signed sum alone.
    static double Studentised(double sum, double squares, int q)
    {
        var mean = sum / q;
        var variance = (squares - (q * mean * mean)) / (q - 1);

        if (variance <= 0)
        {
            return mean > 0 ? double.PositiveInfinity : mean < 0 ? double.NegativeInfinity : 0;
        }

        return mean * Math.Sqrt(q) / Math.Sqrt(variance);
    }

    // The smallest difference a unit the gate detects four times in five at the bar, from the blocks' own scatter: the
    // statistic's two normal points over the bar and the power times the blocks' standard deviation, spread over the
    // proposal's units.
    public const double Power = 0.8;

    public static double? Detectable(IReadOnlyList<double> sums, int units)
    {
        var q = sums.Count;

        if (q < 2 || units <= 0)
        {
            return null;
        }

        var mean = sums.Sum() / q;
        var deviation = Math.Sqrt(sums.Sum(sum => (sum - mean) * (sum - mean)) / (q - 1));

        return (Normal.Quantile(1 - Bar) + Normal.Quantile(Power)) * deviation * Math.Sqrt(q) / units;
    }
}
