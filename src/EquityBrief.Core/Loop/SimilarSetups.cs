namespace EquityBrief.Core.Loop;

// One finished setup a rule's own setting passed on an index, as the card's part matches it: its stock, the session it
// was read on by its place among the sessions the ledger read, the readings matched on in the order matched, and its
// edge in risks against the same plan on every member that session, before costs.
public sealed record SimilarSetup(string Ticker, int Session, IReadOnlyList<double> Readings, double Edge);

// The setups like a pick under its rule on its index: the readings matched on, how many were matched and their median
// distance, their mean edge with its interval, and the mean edge of every finished setup the rule passed on the index.
public sealed record SimilarPart(IReadOnlyList<string> Readings, int Count, double MedianDistance, double Mean, double Low, double High, double RuleMean, int RuleSetups)
{
    // Whether the interval leaves out the rule's own mean; where it holds it, the part is not told from the rule's record.
    public bool Distinguishable => RuleMean < Low || RuleMean > High;
}

// The card's part of setups like tonight's pick: among the finished setups the rule's own setting passed on the pick's
// index, each placed on each of the readings carrying the most weight in the index's current score by its share of
// those setups at or under it, the pick placed the same way, the nearest by the straight distance between those places;
// the pick's own stock left out, and a stock matched at most once within any run of sessions so one stock's stretch of
// setups does not fill the part. Its mean edge is drawn with a two-sided ninety per cent interval, the mean plus and
// minus 1.645 of its standard errors, beside the mean of every finished setup the rule passed on the index.
// see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
public static class SimilarSetups
{
    // The most setups matched, the readings matched on and the sessions within which a stock is matched once.
    public const int Nearest = 250;

    public const int Matched = 6;

    public const int TickerSessions = 21;

    // The fewest setups matched for the part to be drawn.
    public const int Fewest = 30;

    // The interval's width in per cent, two-sided, and the normal quantile it reaches.
    public const int IntervalPercent = 90;

    public const double Z = 1.6448536269514722;

    // The readings matched on: the six carrying the most weight in the score, the largest first.
    public static IReadOnlyList<int> Readings(RidgeModel model) => [.. model.Importances.Take(Matched).Select(one => one.Reading)];

    // The part, none where fewer than the fewest are matched; every setup holds every reading matched on.
    public static SimilarPart? Match(IReadOnlyList<SimilarSetup> setups, string ticker, IReadOnlyList<double> pick, IReadOnlyList<string> readings)
    {
        if (setups.Count == 0)
        {
            return null;
        }

        var columns = Enumerable.Range(0, pick.Count).Select(at => setups.Select(setup => setup.Readings[at]).Order().ToArray()).ToArray();
        var places = new double[setups.Count][];

        for (var row = 0; row < setups.Count; row++)
        {
            places[row] = [.. Enumerable.Range(0, pick.Count).Select(at => Place(columns[at], setups[row].Readings[at]))];
        }

        double[] own = [.. Enumerable.Range(0, pick.Count).Select(at => Place(columns[at], pick[at]))];

        double Distance(int row)
        {
            var sum = 0.0;

            for (var at = 0; at < own.Length; at++)
            {
                var gap = places[row][at] - own[at];

                sum += gap * gap;
            }

            return Math.Sqrt(sum);
        }

        var matched = new List<(int Row, double Distance)>();
        var taken = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        foreach (var (row, distance) in Enumerable.Range(0, setups.Count)
            .Where(row => !string.Equals(setups[row].Ticker, ticker, StringComparison.Ordinal))
            .Select(row => (Row: row, Distance: Distance(row)))
            .OrderBy(one => one.Distance)
            .ThenByDescending(one => setups[one.Row].Session)
            .ThenBy(one => setups[one.Row].Ticker, StringComparer.Ordinal))
        {
            if (matched.Count == Nearest)
            {
                break;
            }

            var setup = setups[row];

            if (taken.TryGetValue(setup.Ticker, out var sessions) && sessions.Any(session => Math.Abs(session - setup.Session) < TickerSessions))
            {
                continue;
            }

            (taken.TryGetValue(setup.Ticker, out var held) ? held : taken[setup.Ticker] = []).Add(setup.Session);
            matched.Add((row, distance));
        }

        if (matched.Count < Fewest)
        {
            return null;
        }

        var (sum, all) = (0.0, 0.0);

        foreach (var (row, _) in matched)
        {
            sum += setups[row].Edge;
        }

        foreach (var setup in setups)
        {
            all += setup.Edge;
        }

        var mean = sum / matched.Count;
        var squares = 0.0;

        foreach (var (row, _) in matched)
        {
            var gap = setups[row].Edge - mean;

            squares += gap * gap;
        }

        var error = Math.Sqrt(squares / (matched.Count - 1)) / Math.Sqrt(matched.Count);
        var distances = matched.Select(one => one.Distance).Order().ToArray();
        var median = distances.Length % 2 == 1 ? distances[distances.Length / 2] : (distances[(distances.Length / 2) - 1] + distances[distances.Length / 2]) / 2;

        return new SimilarPart(readings, matched.Count, median, mean, mean - (Z * error), mean + (Z * error), all / setups.Count, setups.Count);
    }

    // A value's place among sorted values: the share under it and half the share equal to it.
    public static double Place(double[] sorted, double value)
    {
        var under = LowerBound(sorted, value);
        var through = UpperBound(sorted, value);

        return (under + ((through - under) / 2.0)) / sorted.Length;
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
}
