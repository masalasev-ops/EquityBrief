using EquityBrief.Core.Loop;

namespace EquityBrief.Core.Ledger;

// One settled setup as the table of which readings go with winners reads it: its session, its result in risks and its
// readings in the catalogue's order.
public sealed record LinkedSetup(DateOnly Session, double Result, IReadOnlyList<double?> Readings);

// One reading's row of the table: the settled setups holding it in the test years, the test years counted, the mean of
// those years' rank correlations, the share of them pointing its way, the 95th percentile of the same mean's size with
// the results shuffled within each night, and its words.
public sealed record ReadingLink(string Reading, int Setups, int Years, double? Correlation, double? SameWay, double? Shuffled, string Words);

// The table one family's rows on an index were stored in: the tester's run, the month it was for and the rows in the
// catalogue's order.
public sealed record LinkTable(string RunId, string Month, IReadOnlyList<ReadingLink> Rows);

// Which readings go with winners. For each reading, its rank correlation at entry with the setup's result in risks over
// the settled setups holding it, worked within each test year from the walk-forward's first: a Spearman correlation,
// each value ranked among the year's setups holding the reading and a tie taking the mean of the places it shares. The
// figure is the mean over the years holding at least the floor of such setups and a reading and a result that vary, with
// the share of those years whose correlation points its way; and beside it the 95th percentile of the same mean's size
// after shuffling the results within each night among the setups holding the reading, over every arrangement where
// they number no more than the draws and over the draws at the seed where they number more. Its words say it goes with
// winners or with losers where its size is above that percentile and that it has no link beyond the shuffle where it is
// not. The words inform only: no reading is removed and no rule or proposal moves because of them.
// see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
public static class ReadingLinks
{
    public const int YearFloor = 30;

    public const int Draws = 199;

    public const int Seed = 20261016;

    public const double Percentile = 0.95;

    public const string Winners = "goes with winners";

    public const string Losers = "goes with losers";

    public const string NoLink = "no link beyond the shuffle";

    public const string TooFew = "too few settled setups in a test year to read";

    public static IReadOnlyList<ReadingLink> Of(IReadOnlyList<LinkedSetup> setups) =>
        [.. Enumerable.Range(0, LedgerReadings.Count).Select(reading => Of(setups, reading))];

    // One test year's setups holding a reading as the shuffle reads them: each one's reading's rank and its result's
    // rank, and the setups of each night by their places among the year's.
    sealed record Year(double[] Readings, double[] Results, int[][] Nights);

    public static ReadingLink Of(IReadOnlyList<LinkedSetup> setups, int reading)
    {
        var column = LedgerReadings.All[reading].Column;
        var held = setups
            .Where(setup => setup.Session.Year >= LoopFolds.FirstTestYear && setup.Readings.Count > reading && setup.Readings[reading] is not null)
            .ToArray();
        var years = held
            .GroupBy(setup => setup.Session.Year)
            .OrderBy(year => year.Key)
            .Where(year => year.Count() >= YearFloor)
            .Select(year => Prepared([.. year.OrderBy(setup => setup.Session)], reading))
            .Where(year => Varies(year.Readings) && Varies(year.Results))
            .ToArray();

        if (years.Length == 0)
        {
            return new ReadingLink(column, held.Length, 0, null, null, null, TooFew);
        }

        var correlations = years.Select(year => Pearson(year.Readings, year.Results)).ToArray();
        var mean = correlations.Average();
        var sameWay = 1.0 * correlations.Count(correlation => Math.Sign(correlation) == Math.Sign(mean)) / correlations.Length;
        var shuffled = Shuffled(years);
        var bar = shuffled[Math.Clamp((int)Math.Ceiling(Percentile * shuffled.Length) - 1, 0, shuffled.Length - 1)];
        var words = Math.Abs(mean) > bar ? mean > 0 ? Winners : Losers : NoLink;

        return new ReadingLink(column, held.Length, years.Length, mean, sameWay, bar, words);
    }

    static Year Prepared(LinkedSetup[] year, int reading)
    {
        var nights = year
            .Select((setup, at) => (setup.Session, at))
            .GroupBy(pair => pair.Session)
            .Select(night => night.Select(pair => pair.at).ToArray())
            .ToArray();

        return new Year(Ranks([.. year.Select(setup => setup.Readings[reading]!.Value)]), Ranks([.. year.Select(setup => setup.Result)]), nights);
    }

    // The sizes of the shuffled means, sorted: every arrangement of the results within the nights where they number no
    // more than the draws, and the draws at the seed where they number more.
    static double[] Shuffled(IReadOnlyList<Year> years)
    {
        var arrangements = 1.0;

        foreach (var night in years.SelectMany(year => year.Nights))
        {
            for (var held = 2; held <= night.Length && arrangements <= Draws; held++)
            {
                arrangements *= held;
            }
        }

        var sizes = arrangements <= Draws ? Every(years) : Drawn(years);

        Array.Sort(sizes);

        return sizes;
    }

    static double[] Drawn(IReadOnlyList<Year> years)
    {
        var random = new Random(Seed);
        var sizes = new double[Draws];

        for (var draw = 0; draw < Draws; draw++)
        {
            var sum = 0.0;

            foreach (var year in years)
            {
                var results = year.Results.ToArray();

                foreach (var night in year.Nights)
                {
                    for (var at = night.Length - 1; at > 0; at--)
                    {
                        var other = random.Next(at + 1);

                        (results[night[at]], results[night[other]]) = (results[night[other]], results[night[at]]);
                    }
                }

                sum += Pearson(year.Readings, results);
            }

            sizes[draw] = Math.Abs(sum / years.Count);
        }

        return sizes;
    }

    // Every arrangement of the results within every night of every year, each night's orders taken in turn.
    static double[] Every(IReadOnlyList<Year> years)
    {
        var nights = years.SelectMany((year, at) => year.Nights.Select(night => (Year: at, Places: night))).ToArray();
        var results = years.Select(year => year.Results.ToArray()).ToArray();
        var sizes = new List<double>();

        void Arrange(int night)
        {
            if (night == nights.Length)
            {
                sizes.Add(Math.Abs(years.Select((year, at) => Pearson(year.Readings, results[at])).Average()));

                return;
            }

            var (year, places) = nights[night];
            var values = places.Select(place => years[year].Results[place]).ToArray();

            foreach (var order in Orders(values.Length))
            {
                for (var at = 0; at < places.Length; at++)
                {
                    results[year][places[at]] = values[order[at]];
                }

                Arrange(night + 1);
            }

            for (var at = 0; at < places.Length; at++)
            {
                results[year][places[at]] = values[at];
            }
        }

        Arrange(0);

        return [.. sizes];
    }

    // Every order of a count of places, each as the places it takes from.
    static IEnumerable<int[]> Orders(int count)
    {
        if (count <= 1)
        {
            yield return [.. Enumerable.Range(0, count)];

            yield break;
        }

        foreach (var shorter in Orders(count - 1))
        {
            for (var at = 0; at < count; at++)
            {
                var order = new int[count];

                for (int from = 0, to = 0; to < count; to++)
                {
                    order[to] = to == at ? count - 1 : shorter[from++];
                }

                yield return order;
            }
        }
    }

    static bool Varies(IReadOnlyList<double> values) => values.Count > 1 && values.Any(value => value != values[0]);

    // The places of values from 1 for the lowest, a tie taking the mean of the places it shares.
    public static double[] Ranks(IReadOnlyList<double> values)
    {
        var order = Enumerable.Range(0, values.Count).OrderBy(at => values[at]).ToArray();
        var ranks = new double[values.Count];

        for (var at = 0; at < order.Length;)
        {
            var end = at;

            while (end + 1 < order.Length && values[order[end + 1]] == values[order[at]])
            {
                end++;
            }

            for (var tie = at; tie <= end; tie++)
            {
                ranks[order[tie]] = ((at + end) / 2.0) + 1;
            }

            at = end + 1;
        }

        return ranks;
    }

    static double Pearson(IReadOnlyList<double> one, IReadOnlyList<double> other) =>
        FactorReadings.Correlation(one, other) ?? 0;
}
