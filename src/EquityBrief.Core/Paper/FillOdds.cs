using System.Globalization;

namespace EquityBrief.Core.Paper;

// One night a past order waited through: how far the close stood above its buy price, in the typical daily moves its plan was
// placed with, the trading days it had left, and whether it filled on one of them.
public readonly record struct OrderState(double Moves, int DaysLeft, bool Filled);

// One cell of a rule's fill odds on an index: the band of distances, its trading days left, the past orders' nights that fell
// in it and those that filled before their last day.
public sealed record FillOddsCell(int Band, int DaysLeft, int Orders, int Filled)
{
    public double Share => Orders > 0 ? Filled * 1.0 / Orders : 0;
}

// From the operator's order of 2026-11-28, item 2, how often the ledger's past orders of a rule on an index this far from
// their buy price, in typical daily moves, with as many trading days left were filled before their last day: each past order
// read on every night it waited after the one that placed it, its distance put in a band half a typical daily move wide, and
// the cell matching a waiting order's band and days left read beside it; under 30 orders in the cell it says too few. These
// are the rule's past orders, never the stock's own chance.
// see: A waiting order's fill odds are how often the ledger's past orders of its rule this far away with as long left filled before their last day
public static class FillOdds
{
    // The width of a band of distances, in typical daily moves.
    public const double BandWidth = 0.5;

    // The fewest past orders a cell must hold for its share to be said.
    public const int FewestOrders = 30;

    public static int BandOf(double moves) => (int)Math.Floor(Math.Max(0, moves) / BandWidth);

    // The nights one past order waited: each from the night that placed it, at `pick`, to the session before its window's last,
    // that its fill had not yet come by; its days left the window's sessions after that night, and filled where its fill came
    // on one of them. None for an order whose level or typical move is not above nought.
    public static IReadOnlyList<OrderState> States(ReadOnlySpan<double> closes, int pick, double level, double typical, int window, int? fillBar)
    {
        var states = new List<OrderState>();

        if (!(level > 0) || !(typical > 0) || window <= 0)
        {
            return states;
        }

        var last = pick + window;
        var filled = fillBar is { } bar && bar > pick && bar <= last;

        for (var night = pick; night < last && night < closes.Length; night++)
        {
            if (fillBar is { } fill && fill <= night)
            {
                break;
            }

            if (!(closes[night] > 0))
            {
                continue;
            }

            states.Add(new OrderState((closes[night] - level) / typical, last - night, filled));
        }

        return states;
    }

    // The states tallied into cells by band and days left.
    public static IReadOnlyList<FillOddsCell> Tally(IEnumerable<OrderState> states) =>
    [
        .. states
            .GroupBy(state => (Band: BandOf(state.Moves), state.DaysLeft))
            .OrderBy(group => group.Key.Band)
            .ThenBy(group => group.Key.DaysLeft)
            .Select(group => new FillOddsCell(group.Key.Band, group.Key.DaysLeft, group.Count(), group.Count(state => state.Filled))),
    ];

    public static FillOddsCell? CellOf(IReadOnlyList<FillOddsCell> cells, double moves, int daysLeft) =>
        cells.FirstOrDefault(cell => cell.Band == BandOf(moves) && cell.DaysLeft == daysLeft);

    public const string TooFew = "Too few past orders to say how often they filled.";

    public const string Caveat = "Its rule's past orders, not this stock's chance.";

    // A waiting card's line: how far the price must fall from the last close to its buy price and the trading days it has left,
    // in one sentence, then how often past orders this far away with this long left filled, or that too few say.
    // see: A waiting order's distance is said in one measure everywhere, the fall from its last close to its buy price
    public static string Line(decimal close, decimal price, int daysLeft, FillOddsCell? cell)
    {
        var head = FormattableString.Invariant($"{PlanOrderWords.ToFallClause(price, close)}, {daysLeft} trading day{(daysLeft == 1 ? string.Empty : "s")} left.");

        return cell is { Orders: >= FewestOrders } held
            ? FormattableString.Invariant($"{head} Past orders this far away with this long left were filled {held.Share * 100:0}% of the time ({held.Orders:#,##0} orders). {Caveat}")
            : $"{head} {TooFew}";
    }

    // The card of an order filled on the night, until it leaves the waiting list.
    public static string BoughtLine(DateOnly on, decimal paid) =>
        "Bought on " + on.ToString("d MMM", CultureInfo.InvariantCulture) + " at $" + paid.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
