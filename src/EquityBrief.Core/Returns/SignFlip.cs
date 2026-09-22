namespace EquityBrief.Core.Returns;

// The block sign-flip randomization test a candidate's record is judged by.
//
// One block's sum is the excess of its setups' wins over the null win
// probabilities their own plans set, and the question the test puts is whether
// the blocks sum to more than their own scatter allows. Under the null a block's
// excess is as likely to fall either way, so flipping the signs of the blocks
// generates the distribution the observed arrangement is read against, and every
// arrangement is enumerated rather than sampled because there are at most sixteen
// blocks and so at most sixty-five thousand of them.
//
// A permutation of outcomes across setups is not this test and would answer
// nothing: it leaves the sum of wins and the sum of null probabilities as they
// were, so every permuted arrangement equals the observed one.
// see: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
public static class SignFlip
{
    // Two statistics within this of each other are read as the tie they are.
    //
    // The statistic is a monotone function of the signed sum of the blocks,
    // because flipping a sign leaves the sum of squares as it was, so two
    // arrangements whose sums are equal have equal statistics. Those sums are
    // added in a different order for each arrangement, and a tie the arithmetic
    // would make exactly can come back a bit apart. A tie broken by the last bit
    // of a double would be a p-value that moved with the order of a loop.
    public const double Tie = 1e-9;

    // The studentized mean of the block sums: the mean over its own standard
    // error.
    //
    // A single block has no scatter to divide by and no statistic; blocks that
    // are all the same size have none either, and the statistic is infinite in
    // the direction of their mean, which is what the enumeration then compares.
    // That is not a defect in the arithmetic: it says every arrangement but one
    // sits below the observed, and the p-value it yields is one in two to the q.
    public static double Statistic(IReadOnlyList<double> sums)
    {
        if (sums.Count < 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sums),
                sums.Count,
                "A sign-flip statistic over fewer than two blocks has no scatter to divide by. The floor " +
                "of eight blocks is what keeps this from being reached by a record rather than by a caller.");
        }

        var q = sums.Count;
        var mean = sums.Sum() / q;
        var variance = (sums.Sum(sum => sum * sum) - (q * mean * mean)) / (q - 1);

        if (variance <= 0)
        {
            return mean > 0 ? double.PositiveInfinity : mean < 0 ? double.NegativeInfinity : 0;
        }

        return mean * Math.Sqrt(q) / Math.Sqrt(variance);
    }

    // The share of the arrangements whose statistic is at least the observed
    // one, the observed arrangement itself included.
    //
    // Included by rule rather than by oversight: the identity is one of the
    // arrangements the null admits, and a p-value that left it out could reach
    // zero, which is a statement no finite enumeration can make.
    public static double PValue(IReadOnlyList<double> sums)
    {
        var observed = Statistic(sums);
        var atLeast = 0;

        foreach (var arrangement in Arrangements(sums))
        {
            if (AtLeast(arrangement, observed))
            {
                atLeast++;
            }
        }

        return (double)atLeast / (1 << sums.Count);
    }

    // Every arrangement's statistic, the observed one first, which is the
    // arrangement with no sign flipped.
    public static IEnumerable<double> Arrangements(IReadOnlyList<double> sums)
    {
        var flipped = new double[sums.Count];

        for (var arrangement = 0; arrangement < 1 << sums.Count; arrangement++)
        {
            for (var block = 0; block < sums.Count; block++)
            {
                flipped[block] = (arrangement & (1 << block)) == 0 ? sums[block] : -sums[block];
            }

            yield return Statistic(flipped);
        }
    }

    // Whether one statistic is at or above another, with a tie read as a tie.
    public static bool AtLeast(double statistic, double than) =>
        double.IsPositiveInfinity(than)
            ? double.IsPositiveInfinity(statistic)
            : double.IsNegativeInfinity(than) || statistic >= than - (Tie * Math.Max(1, Math.Abs(than)));
}
