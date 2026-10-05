using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// One listing a setup family's rule makes on one night under one setting: the stock and its bar, the session,
// the family's order keys, the trade bought at the close, with a target or a trail and none of the other, and
// the typical move on the night, which says how near the stop sits.
public readonly record struct FamilyListing(
    int Name,
    int Bar,
    int Session,
    double Order,
    double ThenBy,
    double Entry,
    double Stop,
    double Target,
    double Trail,
    int Cap,
    double Move = double.NaN)
{
    public bool Trails => !double.IsNaN(Trail);

    // A stop nearer the buy than one typical move, where a result counted in risks grows with how near it is.
    public bool CloseStop => Move > 0 && Entry - Stop < Move;
}

// One listing the walk kept: what its trade came to in multiples of its risk, none while the history has not
// reached its end, and the same plan entered on every member that night.
public readonly record struct FamilyTrade(FamilyListing Listing, int Year, double? Result, double Benchmark);

// A setup family's grid: each dial with the levels the sweep reads it at, and the provisional setting's place
// on each. A setting is one level of every dial, read by its indexes.
public sealed record FamilyGrid(IReadOnlyList<(string Dial, IReadOnlyList<double> Levels)> Dials, IReadOnlyList<int> Provisional)
{
    public IReadOnlyList<int[]> Settings
    {
        get
        {
            var settings = new List<int[]> { new int[Dials.Count] };

            for (var dial = 0; dial < Dials.Count; dial++)
            {
                settings = [.. settings.SelectMany(held => Enumerable.Range(0, Dials[dial].Levels.Count).Select(level =>
                {
                    var next = (int[])held.Clone();

                    next[dial] = level;

                    return next;
                }))];
            }

            return settings;
        }
    }

    public double Value(int[] setting, int dial) => Dials[dial].Levels[setting[dial]];

    public string Key(int[] setting) => string.Join("|", Dials.Select((dial, at) => dial.Dial + "=" + Number(dial.Levels[setting[at]])));

    // How many dials a setting moves from the provisional one.
    public int Changes(int[] setting) => setting.Where((level, dial) => level != Provisional[dial]).Count();

    // The settings one step along one dial from a setting, either way.
    public IEnumerable<int[]> Neighbours(int[] setting)
    {
        for (var dial = 0; dial < Dials.Count; dial++)
        {
            foreach (var step in new[] { -1, 1 })
            {
                var level = setting[dial] + step;

                if (level >= 0 && level < Dials[dial].Levels.Count)
                {
                    var next = (int[])setting.Clone();

                    next[dial] = level;

                    yield return next;
                }
            }
        }
    }

    public static string Number(double value) =>
        double.IsPositiveInfinity(value) ? "off" : value.ToString("0.##", CultureInfo.InvariantCulture);
}

// What one setting's trades came to over the history: the listings kept, the trades with a result, the nights
// a stock was listed, the edge over the same plan entered on every member and the plain result, both in
// multiples of the risk, each year's trades and edge, the years the edge stood above nothing, the last three
// years together, the edge without its five largest results by size, the edge's standard error, and the share
// of the trades whose stop sat nearer the buy than one typical move, so an edge bought with near stops is seen.
public sealed record FamilyFigures(
    string Key,
    int Listed,
    int Trades,
    int Nights,
    int ScoredNights,
    double? Edge,
    double? Result,
    int[] YearTrades,
    double?[] YearEdge,
    int YearsBeating,
    double? RecentEdge,
    double? EdgeWithoutLargest,
    double? StandardError,
    double? CloseStops = null)
{
    public bool MeetsFloors => Trades >= FamilySweep.TradeFloor && YearsBeating >= FamilySweep.YearsBeating;

    public double NightShare => ScoredNights > 0 ? 1.0 * Nights / ScoredNights : 0;
}

// What a family's sweep proposes: the setting with the best edge among those meeting the floors, ties to the
// one nearest the provisional and then its key, with its neighbours as the variants, or nothing where no setting
// meets them, the family keeping its provisional settings and listing until the operator rules.
// see: No family on any index is set aside or hidden by a test result without the operator's word
public sealed record FamilyProposal(FamilyFigures? Proposed, IReadOnlyList<FamilyFigures> Variants)
{
    public bool NonePassed => Proposed is null;
}

// A setup family's sweep: the family's own rule replayed over the stored history under each setting of its
// grid, night by night, the market check at the live filter's floor closing every list, five a night in the
// family's own order and one open trade a stock, each trade scored against the same plan entered on every
// member that night.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
public static class FamilySweep
{
    public const int PerNight = SetupFamilies.ListedANight;

    // The floors, the pullback sweep's own: trades with a result, and years the edge stood above nothing.
    public const int TradeFloor = SweepMeasures.TradeFloor;

    public const int YearsBeating = SweepMeasures.YearsBeating;

    public const int RecentYears = SweepMeasures.RecentYears;

    public const int LargestLeftOut = SweepStages.LargestLeftOut;

    // The variants a proposal states, the most a family registers beside its rule.
    public const int Variants = 8;

    // The market check every family's list is closed by, at the live filter's breadth floor.
    public static double MarketFloor => SweepGrid.Coarse.MarketFloors[DialSetting.LiveOnCoarse.Market];

    // Walks one setting's listings night by night: on each session the listings of stocks no kept trade still
    // holds, in the family's order, the first five kept, or as many as the night's count says, each holding its
    // stock through the session its trade ended on or, where the history has not reached its end, through its cap.
    public static List<FamilyTrade> Walk(
        IEnumerable<FamilyListing> listings,
        IReadOnlyList<string> tickers,
        Func<int, int> yearOf,
        Func<FamilyListing, (double? Result, int Sessions)> exit,
        Func<FamilyListing, double> benchmark,
        int perNight = PerNight)
    {
        var kept = new List<FamilyTrade>();
        var openUntil = new Dictionary<int, int>();

        foreach (var night in listings.GroupBy(listing => listing.Session).OrderBy(group => group.Key))
        {
            var taken = 0;

            foreach (var listing in night
                .OrderByDescending(one => one.Order)
                .ThenByDescending(one => one.ThenBy)
                .ThenBy(one => tickers[one.Name], StringComparer.Ordinal))
            {
                if (taken == perNight)
                {
                    break;
                }

                if (openUntil.TryGetValue(listing.Name, out var held) && listing.Session <= held)
                {
                    continue;
                }

                var (result, sessions) = exit(listing);

                openUntil[listing.Name] = listing.Session + sessions;
                kept.Add(new FamilyTrade(listing, yearOf(listing.Session), result, result is null ? double.NaN : benchmark(listing)));
                taken++;
            }
        }

        return kept;
    }

    public static FamilyFigures Figures(string key, IReadOnlyList<FamilyTrade> trades, int scoredNights)
    {
        var years = SweepFigures.Years;
        var yearTrades = new int[years];
        var yearEdge = new double?[years];
        var withResult = trades.Where(trade => trade.Result is not null && trade.Year is >= 0 and < SweepFigures.Years).ToArray();
        var edges = withResult.Where(trade => !double.IsNaN(trade.Benchmark)).ToArray();

        for (var year = 0; year < years; year++)
        {
            var inYear = edges.Where(trade => trade.Year == year).ToArray();

            yearTrades[year] = withResult.Count(trade => trade.Year == year);
            yearEdge[year] = inYear.Length > 0 ? inYear.Average(EdgeOf) : null;
        }

        var recent = edges.Where(trade => trade.Year >= years - RecentYears).ToArray();
        var trimmed = edges.OrderByDescending(trade => Math.Abs(trade.Result!.Value)).Skip(LargestLeftOut).ToArray();
        double? edge = edges.Length > 0 ? edges.Average(EdgeOf) : null;
        double? error = null;

        if (edges.Length > 1 && edge is { } mean)
        {
            var variance = edges.Sum(trade => Math.Pow(EdgeOf(trade) - mean, 2)) / (edges.Length - 1);

            error = Math.Sqrt(variance / edges.Length);
        }

        return new FamilyFigures(
            key,
            trades.Count,
            withResult.Length,
            trades.Select(trade => trade.Listing.Session).Distinct().Count(),
            scoredNights,
            edge,
            withResult.Length > 0 ? withResult.Average(trade => trade.Result!.Value) : null,
            yearTrades,
            yearEdge,
            yearEdge.Count(value => value > 0),
            recent.Length > 0 ? recent.Average(EdgeOf) : null,
            trimmed.Length > 0 ? trimmed.Average(EdgeOf) : null,
            error,
            withResult.Length > 0 ? 1.0 * withResult.Count(trade => trade.Listing.CloseStop) / withResult.Length : null);
    }

    static double EdgeOf(FamilyTrade trade) => trade.Result!.Value - trade.Benchmark;

    // The proposal over every setting's figures: the best edge among the settings meeting the floors, ties to
    // the fewest dials moved from the provisional setting and then the key, and up to eight of its neighbours,
    // the higher edge first; none where no setting meets the floors.
    public static FamilyProposal Propose(FamilyGrid grid, IReadOnlyList<(int[] Setting, FamilyFigures Figures)> read)
    {
        var best = read
            .Where(one => one.Figures.MeetsFloors && one.Figures.Edge is not null)
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => grid.Changes(one.Setting))
            .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Figures is null)
        {
            return new FamilyProposal(null, []);
        }

        var keys = grid.Neighbours(best.Setting).Select(grid.Key).ToHashSet(StringComparer.Ordinal);

        return new FamilyProposal(
            best.Figures,
            [
                .. read
                    .Where(one => keys.Contains(one.Figures.Key))
                    .Select(one => one.Figures)
                    .OrderByDescending(figures => figures.Edge ?? double.MinValue)
                    .ThenBy(figures => figures.Key, StringComparer.Ordinal)
                    .Take(Variants),
            ]);
    }
}
