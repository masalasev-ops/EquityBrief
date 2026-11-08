namespace EquityBrief.Core.Loop;

// A setting's figures over what a fold learns on, by calendar year: its trades with a result, its mean edge after costs,
// and the years whose mean edge stood above nothing.
public sealed record LongFigures(string Key, int Trades, double? Edge, int YearsBeating);

// One test year of a search over the long history: whether the search ran at it or carried the setting chosen at the last
// search, and the setting chosen or carried, none where no setting met the floors.
public sealed record LongFold<T>(int Year, bool Searched, T? Chosen)
    where T : class;

// How a family's starting settings are searched again over the long history: at its first test year and every third test
// year after, the setting chosen carried through the two between, each fold choosing on what ended before its year began
// with the family's floors read in proportion to the years it learns on against the years the long history scores.
// see: A search over the long history re-runs every third test year with its setting carried between
// see: A search run inside a fold reads the family floors in proportion to the years it learns on
public static class LongSearchRules
{
    public const int SearchEvery = 3;

    public static bool SearchesAt(int year, int firstTestYear) => year >= firstTestYear && (year - firstTestYear) % SearchEvery == 0;

    // Each test year in order, searched where the schedule says and carried otherwise, the first always searched.
    public static IReadOnlyList<LongFold<T>> Folds<T>(IReadOnlyList<int> years, int firstTestYear, Func<int, T?> search)
        where T : class
    {
        var folds = new List<LongFold<T>>();
        T? carried = null;

        foreach (var year in years)
        {
            if (folds.Count == 0 || SearchesAt(year, firstTestYear))
            {
                carried = search(year);
                folds.Add(new LongFold<T>(year, true, carried));
            }
            else
            {
                folds.Add(new LongFold<T>(year, false, carried));
            }
        }

        return folds;
    }

    // The floors a fold reads: the family's trades and years above nothing in proportion to its learning years against the
    // years the history scores, each rounded up.
    public static (int Trades, int Years) Floors(int tradeFloor, int yearsFloor, int learningYears, int historyYears) =>
        historyYears <= 0 ? (tradeFloor, yearsFloor) : (Up(tradeFloor * learningYears, historyYears), Up(yearsFloor * learningYears, historyYears));

    static int Up(int numerator, int denominator) => (numerator + denominator - 1) / denominator;

    // A setting's figures over its trades with a result, each trade's year the calendar year it was entered in.
    public static LongFigures FiguresOf(string key, IReadOnlyList<(int Year, double Edge)> trades)
    {
        var years = trades.GroupBy(trade => trade.Year).Count(year => year.Average(trade => trade.Edge) > 0);

        return new LongFigures(key, trades.Count, trades.Count > 0 ? trades.Average(trade => trade.Edge) : null, years);
    }

    // The setting a fold chooses: the highest edge among those meeting the floors, the fewer changes from the rule today and
    // then the key settling a tie; none where none meets them.
    public static T? Choose<T>(IReadOnlyList<(T Setting, LongFigures Figures, int Changes)> read, (int Trades, int Years) floors)
        where T : class =>
        read
            .Where(one => one.Figures.Edge is not null && one.Figures.Trades >= floors.Trades && one.Figures.YearsBeating >= floors.Years)
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => one.Changes)
            .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
            .Select(one => one.Setting)
            .FirstOrDefault();
}
