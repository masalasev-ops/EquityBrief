namespace EquityBrief.Core.Returns;

// The distribution a reason's wins follow under the null this corpus tests
// against, and the tail read off it.
//
// Each resolved setup wins with the probability its own plan demanded, which is
// its stored break-even. Those probabilities differ from setup to setup, so the
// number of wins is a sum of independent draws with different probabilities,
// which is a Poisson binomial rather than a binomial. Using a binomial at the
// mean would be using a different distribution and calling it the same one.
//
// The exact tail is computed rather than approximated, because the break-evens
// are stored per row and the arithmetic over a few thousand rows is a dynamic
// program that runs in the time a page takes to draw. Code owns every number the
// page shows, and an approximation used where the exact answer is available is a
// number nobody can reproduce from the store.
// see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
// see: Code owns every number
public static class PoissonBinomial
{
    // The probability of exactly k successes, for every k from 0 to n.
    //
    // The recurrence adds one draw at a time: with the first i draws distributed,
    // adding the next moves weight from k to k + 1 with probability p and leaves
    // it where it is with probability 1 - p. Walking k downward is what lets the
    // row be updated in place without a second array.
    public static IReadOnlyList<double> Distribution(IReadOnlyList<double> probabilities)
    {
        var mass = new double[probabilities.Count + 1];

        mass[0] = 1;

        for (var draw = 0; draw < probabilities.Count; draw++)
        {
            var p = probabilities[draw];

            if (p is < 0 or > 1 || double.IsNaN(p))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(probabilities),
                    p,
                    "A setup's break-even is a probability and this one is not in [0, 1]. A tail computed " +
                    "over it would be a number with no meaning rather than a wrong one.");
            }

            for (var wins = draw + 1; wins > 0; wins--)
            {
                mass[wins] = (mass[wins] * (1 - p)) + (mass[wins - 1] * p);
            }

            mass[0] *= 1 - p;
        }

        return mass;
    }

    // The probability of at least this many successes.
    //
    // Summed from the top down rather than as one minus the lower tail, because
    // the upper tail is the small number here and subtracting two near-equal
    // doubles is where the precision would go. A verdict turns on whether this
    // sits below a threshold of the order of a hundredth, so the arithmetic is
    // arranged to keep the small number exact rather than the large one.
    public static double UpperTail(IReadOnlyList<double> probabilities, int atLeast)
    {
        if (atLeast <= 0)
        {
            return 1;
        }

        if (atLeast > probabilities.Count)
        {
            return 0;
        }

        var mass = Distribution(probabilities);
        var tail = 0d;

        for (var wins = mass.Count - 1; wins >= atLeast; wins--)
        {
            tail += mass[wins];
        }

        // Clamped, because a sum of doubles can land a hair outside [0, 1] and a
        // probability the page draws as 1.0000000000000002 is a number a reader
        // would rightly distrust.
        return Math.Clamp(tail, 0, 1);
    }

    // The binomial at the mean probability: the conservative approximation, named
    // here and used by nothing.
    //
    // Hoeffding (1956) shows this is at least as large as the Poisson binomial's
    // upper tail for a count at or above the mean plus one, which is where a
    // verdict that clears its bar sits. It is kept because the bound is the
    // reason the exact computation can be trusted to be the tighter of the two,
    // and a bound nobody can evaluate is a bound nobody can check. A test asserts
    // the inequality over constructed setups in that range.
    // see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
    public static double BinomialUpperTailAtTheMean(IReadOnlyList<double> probabilities, int atLeast)
    {
        var mean = probabilities.Count == 0 ? 0 : probabilities.Average();

        return UpperTail([.. Enumerable.Repeat(mean, probabilities.Count)], atLeast);
    }
}
