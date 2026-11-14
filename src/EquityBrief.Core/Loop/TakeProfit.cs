using EquityBrief.Core.Prices;
using EquityBrief.Core.Swings;

namespace EquityBrief.Core.Loop;

// Where a pick's take-profit price came from: the setup's own sale, a price the stock turned down at before, or the risk taken
// a number of times over the buy.
public enum TakeProfitSource
{
    Own,
    TurnedDown,
    Risk,
}

// A price the stock turned down at before: a swing high's price and the session it was made on.
public sealed record TurnedDown(double Price, DateOnly On);

// A pick's take-profit price: the price, where it came from, the session the stock turned down at where it came from one, and
// the multiple of the risk where it came from the risk.
public sealed record TakeProfitPrice(double Price, TakeProfitSource Source, DateOnly? TurnedDownOn = null, double? Multiple = null);

// From the operator's order of 2026-11-14 every pick takes profit at a price, set in this order: the setup's own registered
// sale price where it sits above the buy, its target, the first band above or the first staged level; otherwise the nearest
// price above the buy where the stock turned down before, a swing high of its history before the session; otherwise the buy
// plus a multiple of the distance from the buy to the stop. A setup with no stop of its own stops a number of typical daily
// moves under the buy, so the last step has a risk to multiply. The test chooses the multiple and the moves, never whether
// there is a price.
// see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
public static class TakeProfit
{
    // The multiples of the risk and the typical daily moves of a stop the test walks, stated before its run on the operator's
    // order of 2026-11-14, and the middle of each, which the three setups with no stop of their own trade at this restart.
    public static IReadOnlyList<double> Multiples { get; } = [1.5, 2, 3];

    public static IReadOnlyList<double> StopMoves { get; } = [2, 3, 4];

    public const double MiddleMultiple = 2;

    public const double MiddleStopMoves = 3;

    // The years the history the turned-down price is read over reaches back from the session.
    public const int HistoryYears = 20;

    // The prices a stock turned down at before a session: each swing high confirmed by it and made within the history's years
    // before it, one a price, the most recent of equal prices, in rising order of price.
    public static IReadOnlyList<TurnedDown> Before(IReadOnlyList<Swing> swings, DateOnly session)
    {
        var from = session.AddYears(-HistoryYears);
        var byPrice = new Dictionary<decimal, DateOnly>();

        foreach (var swing in swings)
        {
            if (swing.Direction != SwingSeries.High || swing.ConfirmedOn > session || swing.SessionDate < from)
            {
                continue;
            }

            if (!byPrice.TryGetValue(swing.Price, out var held) || swing.SessionDate > held)
            {
                byPrice[swing.Price] = swing.SessionDate;
            }
        }

        return [.. byPrice.OrderBy(pair => pair.Key).Select(pair => new TurnedDown(Statistic.FromPrice(pair.Key), pair.Value))];
    }

    // The nearest of those above a price, none where none stands above it.
    public static TurnedDown? NearestAbove(double price, IReadOnlyList<TurnedDown> turnedDown)
    {
        foreach (var one in turnedDown)
        {
            if (one.Price > price)
            {
                return one;
            }
        }

        return null;
    }

    // The same read straight off a series' swings, the first of them confirmed by the session, with nothing gathered: the
    // lowest swing high above the price made within the history's years, the most recent of equal prices. The long test reads
    // every member on every session through it.
    public static TurnedDown? NearestAbove(double price, IReadOnlyList<Swing> swings, int confirmed, DateOnly session)
    {
        var from = session.AddYears(-HistoryYears);
        Swing? nearest = null;

        for (var at = 0; at < confirmed && at < swings.Count; at++)
        {
            var swing = swings[at];

            if (swing.Direction != SwingSeries.High || swing.ConfirmedOn > session || swing.SessionDate < from || !(Statistic.FromPrice(swing.Price) > price))
            {
                continue;
            }

            if (nearest is not { } held || swing.Price < held.Price || (swing.Price == held.Price && swing.SessionDate > held.SessionDate))
            {
                nearest = swing;
            }
        }

        return nearest is { } found ? new TurnedDown(Statistic.FromPrice(found.Price), found.SessionDate) : null;
    }

    // The take-profit price of a pick bought at a price: its own sale's where above the buy, else the nearest price above the buy
    // it turned down at before, else the buy plus the multiple of the risk; none where none of them places one, a pick with no
    // stop beneath its buy and no price above it.
    public static TakeProfitPrice? Of(double buy, double? stop, double? own, IReadOnlyList<TurnedDown> turnedDown, double multiple) =>
        Of(buy, stop, own, price => NearestAbove(price, turnedDown), multiple);

    // The same with the nearest price it turned down at above a price read by the caller's own means.
    public static TakeProfitPrice? Of(double buy, double? stop, double? own, Func<double, TurnedDown?> nearestAbove, double multiple)
    {
        if (!(buy > 0))
        {
            return null;
        }

        if (own is { } sale && sale > buy)
        {
            return new TakeProfitPrice(sale, TakeProfitSource.Own);
        }

        if (nearestAbove(buy) is { } turned)
        {
            return new TakeProfitPrice(turned.Price, TakeProfitSource.TurnedDown, turned.On);
        }

        return stop is { } under && buy > under && multiple > 0
            ? new TakeProfitPrice(buy + (multiple * (buy - under)), TakeProfitSource.Risk, Multiple: multiple)
            : null;
    }

    // A plan's sale with its take-profit price beside its other ways out: a target of its own above the buy stands as it is;
    // staged levels keep their levels and what is left sells at the nearest price above the last level where the stock turned
    // down before, or at the buy plus the multiple of the risk where that stands above the last level; a sale naming no price
    // of its own above the buy, a trail, the dip's bounce or the time limit alone, sells at the take-profit price beside them.
    public static PlanSale SaleWith(PlanSale sale, double buy, double stop, IReadOnlyList<TurnedDown> turnedDown, double multiple) =>
        SaleWith(sale, buy, stop, price => NearestAbove(price, turnedDown), multiple);

    public static PlanSale SaleWith(PlanSale sale, double buy, double stop, Func<double, TurnedDown?> nearestAbove, double multiple) =>
        sale.Target is { } own && own > buy ? sale : sale with { Target = Placed(sale, buy, stop, nearestAbove, multiple)?.Price };

    // The price a plan's sale takes profit at from a buy, with where it came from: its own target above the buy; where it stages
    // its sales above the buy, the price what is left after the last level sells at, the nearest it turned down at above that
    // level or the multiple of the risk where that stands above it; otherwise the nearest it turned down at above the buy or
    // the multiple of the risk. One function, so a card draws the price the walk sells at.
    public static TakeProfitPrice? Placed(PlanSale sale, double buy, double stop, IReadOnlyList<TurnedDown> turnedDown, double multiple) =>
        Placed(sale, buy, stop, price => NearestAbove(price, turnedDown), multiple);

    public static TakeProfitPrice? Placed(PlanSale sale, double buy, double stop, Func<double, TurnedDown?> nearestAbove, double multiple)
    {
        if (sale.Target is { } own && own > buy)
        {
            return new TakeProfitPrice(own, TakeProfitSource.Own);
        }

        var last = double.NaN;

        foreach (var level in sale.Staged)
        {
            if (level > buy && !(level <= last))
            {
                last = level;
            }
        }

        if (double.IsNaN(last))
        {
            return Of(buy, stop, null, nearestAbove, multiple);
        }

        if (nearestAbove(last) is { } turned)
        {
            return new TakeProfitPrice(turned.Price, TakeProfitSource.TurnedDown, turned.On);
        }

        return buy > stop && buy + (multiple * (buy - stop)) > last
            ? new TakeProfitPrice(buy + (multiple * (buy - stop)), TakeProfitSource.Risk, Multiple: multiple)
            : null;
    }

    // The stop of a setup with none of its own: the buy less the typical daily moves; none where the typical move is not read.
    public static double? StopUnder(double buy, double typicalDay, double moves) =>
        buy > 0 && typicalDay > 0 && moves > 0 && buy - (moves * typicalDay) > 0 ? buy - (moves * typicalDay) : null;

    // The reasons a book's holding or a Fundamentals trade ends on its own stop or its take-profit, at the night's close.
    public const string UnderItsStop = EquityBrief.Core.Families.RuleWording.TakeProfit.UnderItsStop;

    public const string TookProfit = EquityBrief.Core.Families.RuleWording.TakeProfit.TookProfit;

    // A book's or a Fundamentals trade's stop and take-profit from its buy, on the operator's answer of 2026-11-14: the stop the
    // middle of the moves the test walks under the buy, and the take-profit at the middle multiple; none where the typical move
    // is not read.
    public static (double Stop, TakeProfitPrice? TakeProfit)? ForBook(double buy, double typicalDay, IReadOnlyList<TurnedDown> turnedDown, double moves = MiddleStopMoves, double multiple = MiddleMultiple) =>
        StopUnder(buy, typicalDay, moves) is { } stop ? (stop, Of(buy, stop, null, turnedDown, multiple)) : null;

    // Whether a close ends such a holding: under its stop, or at or above its take-profit; none otherwise.
    public static string? EndOf(double close, double? stop, double? takeProfit) =>
        stop is { } floor && close < floor ? UnderItsStop
        : takeProfit is { } aim && close >= aim ? TookProfit
        : null;

    // Where a take-profit came from as a store keeps it beside the price, and read back.
    public static string Stored(TakeProfitPrice price) => price.Source switch
    {
        TakeProfitSource.TurnedDown => "turned down " + price.TurnedDownOn!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        TakeProfitSource.Risk => FormattableString.Invariant($"risk {price.Multiple:0.##}"),
        _ => "own",
    };

    public static (TakeProfitSource Source, DateOnly? On, double? Multiple) FromStored(string? stored) =>
        stored is null ? (TakeProfitSource.Own, null, null)
        : stored.StartsWith("turned down ", StringComparison.Ordinal) && DateOnly.TryParseExact(stored["turned down ".Length..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var on) ? (TakeProfitSource.TurnedDown, on, null)
        : stored.StartsWith("risk ", StringComparison.Ordinal) && double.TryParse(stored["risk ".Length..], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var times) ? (TakeProfitSource.Risk, null, times)
        : (TakeProfitSource.Own, null, null);

    // The words a plan's sale carries its take-profit multiple in, after its sale's own.
    public static string Words(double multiple) => EquityBrief.Core.Families.RuleWording.TakeProfit.Taking(multiple);

    // The multiple a plan's words name, none where they name none.
    public static double? MultipleOf(string words)
    {
        const string opening = EquityBrief.Core.Families.RuleWording.TakeProfit.TakingOpening;
        const string closing = EquityBrief.Core.Families.RuleWording.TakeProfit.TakingClosing;

        return words.StartsWith(opening, StringComparison.Ordinal) && words.EndsWith(closing, StringComparison.Ordinal)
            && double.TryParse(words[opening.Length..^closing.Length], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var multiple)
            && multiple > 0
                ? multiple
                : null;
    }
}
