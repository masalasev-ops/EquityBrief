namespace EquityBrief.Core.Returns;

// What a setup with no edge would have done: the share of the time a plan's
// target is reached before its stop, and what it would have made and lost doing
// it, each as a share of what it was entered at.
public readonly record struct NullOutcome(double Win, double Gain, double Loss, int Resolved);

// The bar a setup is judged against, calibrated from its own plan rather than
// read off the plan's arithmetic.
//
// A plan's stated break-even assumes the exit lands exactly on the stop or the
// target. Both are judged on closes, so a close lands past either and the exit
// is not the planned one, and a plan with a tight stop is stopped through by more
// than it planned to lose. A condition with tight stops would clear the planned
// bar while making nothing, which is why the bar is simulated from the plan under
// the name's own trailing volatility with the same session cap the scoring uses,
// and the round trip is added on top.
//
// Everything here is a share of the entry rather than a price, so no figure in it
// crosses between the two worlds: the caller hands over where the stop and the
// target sit as multiples of the close the setup was entered at.
// see: A setup's null win probability is calibrated from its own plan, and its planned break-even is shown beside it
// see: The calibrated null carries a round trip of ten basis points, and thirty is shown as a sensitivity
public static class NullWin
{
    // Section 17's calibration: how many paths a setup's bar is simulated over,
    // the sessions of closes the volatility is measured over, and the seed the
    // paths start from.
    //
    // The seed is pinned rather than taken from the clock for the reason every
    // other figure here is computed rather than asked for: a bar that moved
    // between two readings of the same stored setup would be a bar nobody could
    // reproduce, and the register names what a candidate was judged against.
    public const int Paths = 4000;

    public const int VolatilityWindow = 63;

    public const int Seed = 20260922;

    // Section 17's round trip, in basis points, and the sensitivity shown beside it.
    public const double CostBasisPoints = 10;

    public const double SensitivityBasisPoints = 30;

    // The trailing volatility: the scatter of a session's log change over the
    // window, measured on the closes up to and including the listing.
    //
    // Log changes rather than percentage ones, because the walk the paths take is
    // multiplicative and a price cannot go through nought. A name with too few
    // sessions stored has no volatility rather than a volatility of zero, and a
    // window that never moved has none either: a plan under a name that cannot
    // move reaches nothing, which is an absent bar rather than a bar of zero.
    public static double? Volatility(IReadOnlyList<double> sessionChanges)
    {
        if (sessionChanges.Count < 2)
        {
            return null;
        }

        var changes = new List<double>(sessionChanges.Count);

        foreach (var change in sessionChanges)
        {
            if (change <= 0 || double.IsNaN(change) || double.IsInfinity(change))
            {
                return null;
            }

            changes.Add(Math.Log(change));
        }

        var mean = changes.Average();
        var variance = changes.Sum(change => (change - mean) * (change - mean)) / (changes.Count - 1);

        return variance <= 0 ? null : Math.Sqrt(variance);
    }

    // The calibrated bar for one setup, at a round trip in basis points.
    //
    // The paths start where the setup was entered and walk a session at a time
    // with no drift in price, which is what having no edge means, for the sessions
    // the cap leaves. A path that closes through the stop is a loss and one that
    // closes at or above the target is a win, tested in that order and on closes,
    // exactly as the scoring reads a stored series. A path that does neither
    // before the cap resolved nothing and is counted in neither, because an
    // unresolved setup is never a win and the record this bar is compared against
    // holds none.
    //
    // The cost is added to the bar rather than to the paths: a round trip does not
    // change where the price went, it changes how often the plan had to be right
    // to come out even, which is the cost over what a win makes and a loss costs.
    // see: An unresolved setup is never a win
    public static double? For(double floor, double ceiling, double volatility, int sessions, double basisPoints, int seed)
    {
        if (Walk(floor, ceiling, volatility, sessions, seed) is not { Resolved: > 0 } resolved)
        {
            return null;
        }

        var made = resolved.Gain + resolved.Loss;

        if (made <= 0)
        {
            return null;
        }

        // The round trip as a share of the entry, which is the scale the gain and
        // the loss are measured on.
        return Math.Clamp(resolved.Win + (basisPoints / 10_000 / made), 0, 1);
    }

    // The paths themselves: how often a no-edge plan won, and what it made when it
    // won and lost when it lost.
    public static NullOutcome? Walk(double floor, double ceiling, double volatility, int sessions, int seed)
    {
        if (floor <= 0 || floor >= 1 || ceiling <= 1 || volatility <= 0 || sessions <= 0
            || double.IsNaN(floor) || double.IsNaN(ceiling) || double.IsNaN(volatility))
        {
            return null;
        }

        // No drift in the price, which is a downward drift in its logarithm of
        // half the variance. Left out, the paths would rise on average and the bar
        // would be the bar of a plan with an edge.
        var drift = -0.5 * volatility * volatility;
        var random = new PathRandom(seed);
        var (wins, losses, gain, loss) = (0, 0, 0d, 0d);

        for (var path = 0; path < Paths; path++)
        {
            var price = 1d;

            for (var session = 0; session < sessions; session++)
            {
                price *= Math.Exp(drift + (volatility * random.Normal()));

                if (price < floor)
                {
                    losses++;
                    loss += 1 - price;

                    break;
                }

                if (price >= ceiling)
                {
                    wins++;
                    gain += price - 1;

                    break;
                }
            }
        }

        var resolved = wins + losses;

        return new NullOutcome(
            resolved == 0 ? 0 : (double)wins / resolved,
            wins == 0 ? 0 : gain / wins,
            losses == 0 ? 0 : loss / losses,
            resolved);
    }

    // The seed one setup's paths are walked from: the pinned seed and the setup's
    // own name and session, so a setup's bar is the same whichever night computed
    // it and whatever order the night reached its names in.
    public static int SeedFor(string ticker, int sessionNumber)
    {
        unchecked
        {
            var hash = 2166136261u;

            foreach (var letter in ticker)
            {
                hash = (hash ^ letter) * 16777619u;
            }

            hash = (hash ^ (uint)sessionNumber) * 16777619u;

            return (int)(hash ^ (uint)Seed);
        }
    }

    // The paths' own source of numbers, carried here rather than taken from the
    // platform's, so the same setup yields the same bar on both machines and in
    // ten years. A generator whose sequence is the framework's to change is a
    // figure nobody can reproduce.
    sealed class PathRandom
    {
        ulong state;

        double? spare;

        public PathRandom(int seed) => state = (ulong)(uint)seed + 0x9E3779B97F4A7C15UL;

        public double Normal()
        {
            if (spare is { } kept)
            {
                spare = null;

                return kept;
            }

            // The polar form, which takes two uniform numbers from inside the unit
            // circle and yields two normal ones, and keeps the second.
            double first, second, square;

            do
            {
                first = (2 * Uniform()) - 1;
                second = (2 * Uniform()) - 1;
                square = (first * first) + (second * second);
            }
            while (square >= 1 || square == 0);

            var scale = Math.Sqrt(-2 * Math.Log(square) / square);

            spare = second * scale;

            return first * scale;
        }

        double Uniform()
        {
            state += 0x9E3779B97F4A7C15UL;

            var mixed = state;

            mixed = (mixed ^ (mixed >> 30)) * 0xBF58476D1CE4E5B9UL;
            mixed = (mixed ^ (mixed >> 27)) * 0x94D049BB133111EBUL;
            mixed ^= mixed >> 31;

            return (mixed >> 11) * (1.0 / (1UL << 53));
        }
    }
}
