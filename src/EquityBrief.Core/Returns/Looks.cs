namespace EquityBrief.Core.Returns;

// The looks a candidate's verdict is read at, the level each spends, and the
// boundary each one's arrangements allow.
//
// A test repeated as evidence arrives promotes a worthless condition far more
// often than its level says, so the reads are fixed when the candidate is
// registered: three of them, at eight, twelve and sixteen non-empty blocks,
// triggered by the block count alone and never by where the figure stands.
// see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow
public static class Looks
{
    // Section 17's looks, in non-empty blocks.
    static readonly int[] LooksAt = [8, 12, 16];

    public static IReadOnlyList<int> At => LooksAt;

    // The last look, which is the maximum and is never extended.
    public static int Maximum => LooksAt[^1];

    // The share of power a look is measured against when the smallest excess it
    // could detect is stated.
    public const double PowerStatedAt = 0.80;

    // The looks a block count has reached.
    public static int Taken(int blocks) => LooksAt.Count(look => look <= blocks);

    // The information a look reads, as a share of what the last look reads.
    // Blocks and not setups: the block is the unit the test is over, and a look
    // triggered by a count of setups would be triggered by how busy the market
    // had been.
    public static double Fraction(int look) => (double)At[look] / Maximum;

    // The level released by an information fraction, spent by a Lan-DeMets
    // function of the O'Brien-Fleming type.
    //
    // It releases almost nothing early and nearly the whole level at the end,
    // which is what makes an early look able to retire a candidate and unable to
    // promote one: at half the information it releases 0.00071 of a level of a
    // third of 0.05, and eight blocks of a sign-flip test cannot produce a
    // p-value below 1 in 256, which is 0.0039.
    public static double Spent(double level, double fraction)
    {
        if (fraction <= 0)
        {
            return 0;
        }

        return fraction >= 1 ? level : 2 - (2 * Normal.Cdf(Normal.Quantile(1 - (level / 2)) / Math.Sqrt(fraction)));
    }

    // The look a record first crosses its boundary at, or none.
    //
    // The boundary at each look is found rather than assumed: every arrangement
    // of the blocks' signs is enumerated, the arrangements that already crossed
    // at an earlier look are kept marked, and the boundary is the lowest
    // statistic at which the share of arrangements crossing by this look stays
    // within the level spent by it. Nothing is drawn and no seed is taken,
    // because at sixteen blocks the arrangements are enumerable whole, and a
    // boundary read off the arrangements the record's own blocks allow holds its
    // size whatever the setups inside a block did to each other.
    public static int? CrossedAt(IReadOnlyList<double> sums, double level)
    {
        var through = Array.IndexOf(LooksAt, sums.Count);

        if (through < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sums),
                sums.Count,
                "A look is read over the blocks the look holds, and this is not one of the counts a look " +
                "is taken at. A boundary found over any other count would be a look nobody registered.");
        }

        var arrangements = 1 << sums.Count;
        var crossed = new bool[arrangements];
        var crossedSoFar = 0;

        for (var look = 0; look <= through; look++)
        {
            var statistics = Statistics(sums, At[look], sums.Count);
            var order = Enumerable.Range(0, arrangements)
                .Where(arrangement => !crossed[arrangement])
                .OrderByDescending(arrangement => statistics[arrangement])
                .ToArray();

            var spends = Spent(level, Fraction(look));
            var (accepted, at) = (0, 0);

            while (at < order.Length)
            {
                // A tie is admitted whole or not at all. Half of a tied group
                // would be a boundary that separated two arrangements the
                // arithmetic cannot tell apart, which is a boundary decided by
                // the order the loop happened to read them in.
                var group = at;

                while (group < order.Length
                    && SignFlip.AtLeast(statistics[order[group]], statistics[order[at]]))
                {
                    group++;
                }

                if ((double)(crossedSoFar + accepted + (group - at)) / arrangements > spends)
                {
                    break;
                }

                accepted += group - at;
                at = group;
            }

            for (var taken = 0; taken < accepted; taken++)
            {
                crossed[order[taken]] = true;
            }

            crossedSoFar += accepted;

            // The observed arrangement is the one with no sign flipped.
            if (crossed[0])
            {
                return look;
            }
        }

        return null;
    }

    // Each arrangement's statistic over the first blocks of it, laid out over
    // the whole arrangement space so the looks can be read against each other.
    static double[] Statistics(IReadOnlyList<double> sums, int blocks, int over)
    {
        var prefix = new double[1 << blocks];
        var flipped = new double[blocks];

        for (var arrangement = 0; arrangement < prefix.Length; arrangement++)
        {
            for (var block = 0; block < blocks; block++)
            {
                flipped[block] = (arrangement & (1 << block)) == 0 ? sums[block] : -sums[block];
            }

            prefix[arrangement] = SignFlip.Statistic(flipped);
        }

        if (blocks == over)
        {
            return prefix;
        }

        var statistics = new double[1 << over];

        for (var arrangement = 0; arrangement < statistics.Length; arrangement++)
        {
            statistics[arrangement] = prefix[arrangement & (prefix.Length - 1)];
        }

        return statistics;
    }

    // The smallest excess a look could detect, in points of win share.
    //
    // Stated beside the excess observed, so a look that found nothing says how
    // much there would have had to be. It is a normal approximation over the
    // setups the blocks hold, widened by the design effect the record measures,
    // and it is the one figure here that is an approximation rather than an
    // enumeration, which is why the surface says so.
    public static double? SmallestExcess(double spends, int blocks, double setupsPerBlock, double meanNull, double designEffect)
    {
        if (blocks <= 0 || setupsPerBlock <= 0 || spends is <= 0 or >= 1 || designEffect <= 0
            || meanNull is <= 0 or >= 1)
        {
            return null;
        }

        var scatter = Math.Sqrt(designEffect * meanNull * (1 - meanNull) / (blocks * setupsPerBlock));

        return (Normal.Quantile(1 - spends) + Normal.Quantile(PowerStatedAt)) * scatter * 100;
    }
}
