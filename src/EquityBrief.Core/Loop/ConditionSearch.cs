using System.Globalization;
using EquityBrief.Core.Ledger;

namespace EquityBrief.Core.Loop;

// One listing a rule made as the conditions engine reads it: the session it was listed on, which the null shuffles
// within, its edge after its round trip had it been taken, and the catalogue's readings of it on that session.
public sealed record ConditionUnit(int Night, double Edge, IReadOnlyList<double?> Readings);

// A condition or a pair of them the search found, its mean edge over the units it keeps and how many it keeps.
public sealed record ConditionFound(IReadOnlyList<AlsoRequires> Conditions, double Score, int Kept)
{
    public string Words() => string.Join(
        " and ",
        Conditions.Select(condition => FormattableString.Invariant($"{condition.Column} {(condition.Above ? "at or above" : "at or under")} {condition.Level.ToString("0.####", CultureInfo.InvariantCulture)}")));

    // The hooks a registration states for the conditions, which the rule's walk reads.
    public IReadOnlyDictionary<string, double> Parameters() =>
        Conditions.ToDictionary(
            condition => RuleHooks.AlsoPrefix + condition.Column + (condition.Above ? RuleHooks.AboveSuffix : RuleHooks.BelowSuffix),
            condition => condition.Level,
            StringComparer.Ordinal);
}

// What the search found on a population and the null it was held to: the conditions above the null's mark and the
// rule's own mean edge, strongest first, the mark, the best the search found and how many arrangements the null read.
public sealed record ConditionVerdict(IReadOnlyList<ConditionFound> Passing, double? Mark, double? Best, int Arrangements);

// One reading's distribution over a population: how many units hold it, the winners and the losers among them, each
// side's median, and the mean edge of each tenth of the units in the reading's order, lowest first.
public sealed record ReadingSpread(int Reading, int Units, int Winners, int Losers, double? WinnersMedian, double? LosersMedian, IReadOnlyList<double?> Deciles)
{
    public string Column => LedgerReadings.All[Reading].Column;
}

// Winners against losers: each reading of the ledger's catalogue cut at its deciles on the units a search is handed,
// either side, a condition keeping at least the floor's units and fewer than every unit holding the reading, scored
// by the mean edge of the units it keeps; and beneath the best of them, its pair, the best second condition on another
// reading over the units the first keeps. The null shuffles which unit carries which edge within each night, which
// keeps what every member did that night and breaks only which of them a reading points at, and reads the search's
// best again on each arrangement: every arrangement where the nights hold no more than the shuffles' count of them, and
// otherwise the shuffles' count drawn at a fixed seed. A condition goes forward only where its score stands above the
// null's 95th percentile and above the rule's own mean.
// see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
public static class ConditionSearch
{
    public const int Shuffles = 199;

    public const int Seed = 20261010;

    public const double NullQuantile = 0.95;

    // The cuts a reading is read at, its deciles from the tenth to the ninth.
    public static IReadOnlyList<double> Cuts { get; } = [0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9];

    // The value a share of a sorted set reaches, by its nearest rank.
    public static double Quantile(IReadOnlyList<double> sorted, double share) =>
        sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Count) - 1, 0, sorted.Count - 1)];

    // The search and its null over a population.
    public static ConditionVerdict Judge(IReadOnlyList<ConditionUnit> units, int floor)
    {
        if (units.Count == 0)
        {
            return new ConditionVerdict([], null, null, 0);
        }

        var layout = Layout.Of(units);
        var edges = units.Select(unit => unit.Edge).ToArray();
        var found = Search(layout, edges, floor);

        if (found.Count == 0)
        {
            return new ConditionVerdict([], null, null, 0);
        }

        var nulls = new List<double>();

        foreach (var arranged in Arrangements(units))
        {
            var shuffled = arranged.Select(at => edges[at]).ToArray();
            var best = Search(layout, shuffled, floor);

            nulls.Add(best.Count == 0 ? double.NegativeInfinity : best[0].Score);
        }

        nulls.Sort();

        var mark = Quantile(nulls, NullQuantile);
        var own = edges.Average();

        return new ConditionVerdict([.. found.Where(one => one.Score > mark && one.Score > own)], mark, found[0].Score, nulls.Count);
    }

    // Every condition the search reads on a population, the strongest first: each reading's cuts either side, then the
    // pair beneath the strongest single; a tie to fewer conditions, the lower reading, above before under and the lower
    // level.
    public static IReadOnlyList<ConditionFound> Search(IReadOnlyList<ConditionUnit> units, int floor) =>
        Search(Layout.Of(units), [.. units.Select(unit => unit.Edge)], floor);

    static List<ConditionFound> Search(Layout layout, double[] edges, int floor)
    {
        var singles = Singles(layout, edges, floor, null, -1);

        if (singles.Count == 0)
        {
            return [];
        }

        var best = singles[0];
        var first = best.Conditions[0];
        var kept = new bool[edges.Length];

        for (var at = 0; at < edges.Length; at++)
        {
            kept[at] = layout.Units[at].Readings[first.Reading] is { } value && (first.Above ? value >= first.Level : value <= first.Level);
        }

        var seconds = Singles(layout, edges, floor, kept, first.Reading);
        var found = new List<ConditionFound>(singles);

        if (seconds.Count > 0)
        {
            found.Add(new ConditionFound([first, seconds[0].Conditions[0]], seconds[0].Score, seconds[0].Kept));
        }

        return [.. found
            .OrderByDescending(one => one.Score)
            .ThenBy(one => one.Conditions.Count)
            .ThenBy(one => one.Conditions[0].Reading)
            .ThenBy(one => one.Conditions[0].Above ? 0 : 1)
            .ThenBy(one => one.Conditions[0].Level)];
    }

    // Each reading's cuts either side over the units a set keeps, every unit where none is given, a reading set aside.
    static List<ConditionFound> Singles(Layout layout, double[] edges, int floor, bool[]? within, int besides)
    {
        var found = new List<ConditionFound>();

        for (var reading = 0; reading < layout.Orders.Length; reading++)
        {
            if (reading == besides)
            {
                continue;
            }

            // The units holding the reading in its order, those the set keeps, and the running total of their edges.
            var order = layout.Orders[reading];
            var held = within is null ? order : [.. order.Where(at => within[at])];

            if (held.Length < floor)
            {
                continue;
            }

            var values = held.Select(at => layout.Units[at].Readings[reading]!.Value).ToArray();
            var sums = new double[held.Length + 1];

            for (var at = 0; at < held.Length; at++)
            {
                sums[at + 1] = sums[at] + edges[held[at]];
            }

            foreach (var level in Cuts.Select(cut => Quantile(values, cut)).Distinct())
            {
                // Under the level: the units up to the last holding it; above it: those from the first holding it.
                var under = UpperBound(values, level);
                var over = held.Length - LowerBound(values, level);

                if (under >= floor && under < held.Length)
                {
                    found.Add(new ConditionFound([new AlsoRequires(reading, false, level)], sums[under] / under, under));
                }

                if (over >= floor && over < held.Length)
                {
                    found.Add(new ConditionFound([new AlsoRequires(reading, true, level)], (sums[held.Length] - sums[held.Length - over]) / over, over));
                }
            }
        }

        return [.. found
            .OrderByDescending(one => one.Score)
            .ThenBy(one => one.Conditions[0].Reading)
            .ThenBy(one => one.Conditions[0].Above ? 0 : 1)
            .ThenBy(one => one.Conditions[0].Level)];
    }

    static int LowerBound(double[] sorted, double value)
    {
        var (low, high) = (0, sorted.Length);

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (sorted[middle] < value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    static int UpperBound(double[] sorted, double value)
    {
        var (low, high) = (0, sorted.Length);

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (sorted[middle] <= value)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // The arrangements the null reads: for each, the unit whose edge each unit carries, moved only within its night.
    // Every arrangement where the nights hold no more than the shuffles' count of them, in a fixed order; otherwise the
    // shuffles' count drawn at the seed.
    public static IEnumerable<int[]> Arrangements(IReadOnlyList<ConditionUnit> units)
    {
        var nights = units
            .Select((unit, at) => (unit.Night, At: at))
            .GroupBy(one => one.Night)
            .OrderBy(group => group.Key)
            .Select(group => group.Select(one => one.At).ToArray())
            .Where(night => night.Length > 1)
            .ToArray();
        var count = 1.0;

        foreach (var night in nights)
        {
            for (var size = 2; size <= night.Length && count <= Shuffles; size++)
            {
                count *= size;
            }
        }

        if (count <= Shuffles)
        {
            return Every(units.Count, nights);
        }

        return Drawn(units.Count, nights);
    }

    static IEnumerable<int[]> Every(int count, int[][] nights)
    {
        var orders = nights.Select(night => Permutations(night).ToArray()).ToArray();
        var place = new int[nights.Length];

        while (true)
        {
            var arranged = Enumerable.Range(0, count).ToArray();

            for (var at = 0; at < nights.Length; at++)
            {
                var order = orders[at][place[at]];

                for (var unit = 0; unit < nights[at].Length; unit++)
                {
                    arranged[nights[at][unit]] = order[unit];
                }
            }

            yield return arranged;

            var digit = 0;

            while (digit < nights.Length && ++place[digit] == orders[digit].Length)
            {
                place[digit] = 0;
                digit++;
            }

            if (digit == nights.Length)
            {
                yield break;
            }
        }
    }

    static IEnumerable<int[]> Permutations(int[] items)
    {
        if (items.Length <= 1)
        {
            yield return items;

            yield break;
        }

        for (var at = 0; at < items.Length; at++)
        {
            var rest = items.Where((_, other) => other != at).ToArray();

            foreach (var tail in Permutations(rest))
            {
                yield return [items[at], .. tail];
            }
        }
    }

    static IEnumerable<int[]> Drawn(int count, int[][] nights)
    {
        var random = new Random(Seed);

        for (var shuffle = 0; shuffle < Shuffles; shuffle++)
        {
            var arranged = Enumerable.Range(0, count).ToArray();

            foreach (var night in nights)
            {
                var order = night.ToArray();

                for (var at = order.Length - 1; at > 0; at--)
                {
                    var swap = random.Next(at + 1);

                    (order[at], order[swap]) = (order[swap], order[at]);
                }

                for (var unit = 0; unit < night.Length; unit++)
                {
                    arranged[night[unit]] = order[unit];
                }
            }

            yield return arranged;
        }
    }

    // Each reading's spread over a population: the winners' and the losers' median and each tenth's mean edge.
    public static IReadOnlyList<ReadingSpread> Spreads(IReadOnlyList<ConditionUnit> units)
    {
        var spreads = new List<ReadingSpread>();

        for (var reading = 0; reading < LedgerReadings.Count; reading++)
        {
            var held = units.Where(unit => unit.Readings[reading] is not null).OrderBy(unit => unit.Readings[reading]!.Value).ToArray();
            var winners = held.Where(unit => unit.Edge > 0).Select(unit => unit.Readings[reading]!.Value).ToArray();
            var losers = held.Where(unit => unit.Edge <= 0).Select(unit => unit.Readings[reading]!.Value).ToArray();
            var deciles = new double?[10];

            for (var tenth = 0; tenth < 10; tenth++)
            {
                var (from, to) = (tenth * held.Length / 10, (tenth + 1) * held.Length / 10);

                deciles[tenth] = to > from ? held[from..to].Average(unit => unit.Edge) : null;
            }

            spreads.Add(new ReadingSpread(reading, held.Length, winners.Length, losers.Length, PathAutopsy.Median(winners), PathAutopsy.Median(losers), deciles));
        }

        return spreads;
    }

    // The units in each reading's order, those holding it alone, and the population they index.
    sealed record Layout(IReadOnlyList<ConditionUnit> Units, int[][] Orders)
    {
        public static Layout Of(IReadOnlyList<ConditionUnit> units) =>
            new(
                units,
                [
                    .. Enumerable.Range(0, LedgerReadings.Count).Select(reading => Enumerable.Range(0, units.Count)
                        .Where(at => units[at].Readings[reading] is not null)
                        .OrderBy(at => units[at].Readings[reading]!.Value)
                        .ThenBy(at => at)
                        .ToArray()),
                ]);
    }
}
