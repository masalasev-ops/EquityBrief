namespace EquityBrief.Core.Loop;

// One finished setup a rule's own setting passed on an index, as the card's part matches it: its stock, the session it
// was read on by its place among the sessions the ledger read, the readings matched on in the order matched, and its
// edge in risks against the same plan on every member that session, before costs; from 21.7 what it came to after its
// round trip, in risks or for a book in points, and whether it won as its form reads a win, each none where the ledger
// holds neither.
public sealed record SimilarSetup(string Ticker, int Session, IReadOnlyList<double> Readings, double Edge, double? Result = null, bool? Won = null);

// The setups like a pick under its rule on its index: the readings matched on, how many were matched and their median
// distance, their mean edge with its interval, and the mean edge of every finished setup the rule passed on the index;
// from 21.7 their chance, none where none matched holds a result.
public sealed record SimilarPart(IReadOnlyList<string> Readings, int Count, double MedianDistance, double Mean, double Low, double High, double RuleMean, int RuleSetups)
{
    // Whether the interval leaves out the rule's own mean; where it holds it, the part is not told from the rule's record.
    public bool Distinguishable => RuleMean < Low || RuleMean > High;

    public SetupChance? Chance { get; init; }
}

// From 21.7 how a rule's setups count a win and what their result is read in: a rule with a target wins where the target
// was reached before the stop, a trailing rule or one with no target where it ended above its buy, each in risks; and a
// book where its holding ended above the equal-weighted hold it is judged against, in points of its buy.
// see: Each pick's card says how often the setups like it won and what they made after costs, under the rule's heading
public enum ChanceForm
{
    Target,
    Trailing,
    Book,
}

// From 21.7 what the setups like a pick came to: how many hold a result, how many of them won and their share, and their
// mean result after costs with its two-sided ninety per cent interval, the mean plus and minus 1.645 of its standard
// errors, in risks or for a book in points; the expected value an order by it reads is the mean.
// see: Each pick's card says how often the setups like it won and what they made after costs, under the rule's heading
public sealed record SetupChance(ChanceForm Form, int Count, int Won, double Mean, double Low, double High)
{
    public double Share => Count == 0 ? 0 : 1.0 * Won / Count;

    // The form a family's setup is read in: a book family's, and otherwise by whether its plan holds a target.
    public static ChanceForm FormOf(bool book, bool hasTarget) => book ? ChanceForm.Book : hasTarget ? ChanceForm.Target : ChanceForm.Trailing;

    // Whether one setup won as its form reads it: the target reached for a rule with one, a result above nothing for a
    // trailing rule, and an edge above nothing for a book; none where the ledger holds what the form reads none of.
    public static bool? WonOf(ChanceForm form, string? end, double? result, double? edge) => form switch
    {
        ChanceForm.Target => end is null ? null : string.Equals(end, "target", StringComparison.Ordinal),
        ChanceForm.Trailing => result is { } made ? made > 0 : null,
        _ => edge is { } beat ? beat > 0 : null,
    };

    // The chance over the setups matched: none where fewer than the fewest hold a result.
    public static SetupChance? Of(ChanceForm form, IReadOnlyList<SimilarSetup> matched)
    {
        var held = matched.Where(setup => setup.Result is not null && setup.Won is not null).ToArray();

        if (held.Length < SimilarSetups.Fewest)
        {
            return null;
        }

        var results = held.Select(setup => setup.Result!.Value).ToArray();
        var mean = results.Average();
        var squares = results.Sum(result => (result - mean) * (result - mean));
        var error = Math.Sqrt(squares / (results.Length - 1)) / Math.Sqrt(results.Length);

        return new SetupChance(form, held.Length, held.Count(setup => setup.Won!.Value), mean, mean - (SimilarSetups.Z * error), mean + (SimilarSetups.Z * error));
    }
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

    // The part, none where fewer than the fewest are matched; every setup holds every reading matched on. From 21.7 a form
    // given reads the matched setups' chance in it.
    public static SimilarPart? Match(IReadOnlyList<SimilarSetup> setups, string ticker, IReadOnlyList<double> pick, IReadOnlyList<string> readings, ChanceForm? form = null) =>
        setups.Count == 0 ? null : Prepare(setups, pick.Count).Match(ticker, pick, readings, form);

    // From 21.7 the setups picks are matched among, each placed once on each of the readings matched on, so the tester
    // matches every listing of a fold's year among one pool without placing it again.
    public static Prepared Prepare(IReadOnlyList<SimilarSetup> setups, int readings) => new(setups, readings);

    public sealed class Prepared
    {
        readonly IReadOnlyList<SimilarSetup> setups;
        readonly double[][] columns;
        readonly double[][] places;

        internal Prepared(IReadOnlyList<SimilarSetup> setups, int readings)
        {
            this.setups = setups;
            columns = [.. Enumerable.Range(0, readings).Select(at => setups.Select(setup => setup.Readings[at]).Order().ToArray())];
            places = new double[setups.Count][];

            for (var row = 0; row < setups.Count; row++)
            {
                places[row] = [.. Enumerable.Range(0, readings).Select(at => Place(columns[at], setups[row].Readings[at]))];
            }
        }

        public int Count => setups.Count;

        public SimilarPart? Match(string ticker, IReadOnlyList<double> pick, IReadOnlyList<string> readings, ChanceForm? form = null) =>
            setups.Count == 0 ? null : MatchAmong(setups, columns, places, ticker, pick, readings, form);
    }

    static SimilarPart? MatchAmong(IReadOnlyList<SimilarSetup> setups, double[][] columns, double[][] places, string ticker, IReadOnlyList<double> pick, IReadOnlyList<string> readings, ChanceForm? form)
    {
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

        foreach (var (row, distance) in ByDistance(setups, ticker, Distance))
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

        return new SimilarPart(readings, matched.Count, median, mean, mean - (Z * error), mean + (Z * error), all / setups.Count, setups.Count)
        {
            Chance = form is { } read ? SetupChance.Of(read, [.. matched.Select(one => setups[one.Row])]) : null,
        };
    }

    // Every setup but the pick's own stock's, nearest first, a tie the newer session first and then by ticker. Only those
    // within the distance of the nearest few times the count matched are sorted at first, which holds the order's start
    // whole since every setup nearer stands among them; the rest are sorted only where those run out before the count.
    static IEnumerable<(int Row, double Distance)> ByDistance(IReadOnlyList<SimilarSetup> setups, string ticker, Func<int, double> distance)
    {
        var rows = new List<(int Row, double Distance)>(setups.Count);

        for (var row = 0; row < setups.Count; row++)
        {
            if (!string.Equals(setups[row].Ticker, ticker, StringComparison.Ordinal))
            {
                rows.Add((row, distance(row)));
            }
        }

        int Compare((int Row, double Distance) one, (int Row, double Distance) other)
        {
            var by = one.Distance.CompareTo(other.Distance);

            if (by == 0)
            {
                by = setups[other.Row].Session.CompareTo(setups[one.Row].Session);
            }

            return by != 0 ? by : string.CompareOrdinal(setups[one.Row].Ticker, setups[other.Row].Ticker);
        }

        var first = SortedFirst * Nearest;

        if (rows.Count > first)
        {
            var distances = rows.Select(one => one.Distance).ToArray();

            Array.Sort(distances);

            var cut = distances[first - 1];
            var near = rows.Where(one => one.Distance <= cut).ToList();

            near.Sort(Compare);

            foreach (var one in near)
            {
                yield return one;
            }

            var far = rows.Where(one => one.Distance > cut).ToList();

            far.Sort(Compare);

            foreach (var one in far)
            {
                yield return one;
            }

            yield break;
        }

        rows.Sort(Compare);

        foreach (var one in rows)
        {
            yield return one;
        }
    }

    // How many times the count matched are sorted first.
    const int SortedFirst = 4;

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
