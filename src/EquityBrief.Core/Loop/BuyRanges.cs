using System.Globalization;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Loop;

// The two widths a buy range is set by, in the stock's typical daily moves on its night: the ceiling above the last close
// and the floor under it, none where the range has no floor.
public readonly record struct RangeWidths(double Ceiling, double? Floor)
{
    // The width as a table row names it, "1 / 2" or "1 / none".
    public string Key => string.Create(CultureInfo.InvariantCulture, $"{Ceiling:0.##} / {(Floor is { } floor ? floor.ToString("0.##", CultureInfo.InvariantCulture) : BuyRanges.NoFloor)}");

    public string Words => string.Create(
        CultureInfo.InvariantCulture,
        $"a ceiling {Ceiling:0.##} typical {(Ceiling == 1 ? "day" : "days")} above the last close and {(Floor is { } floor ? string.Create(CultureInfo.InvariantCulture, $"a floor {floor:0.##} typical {(floor == 1 ? "day" : "days")} under it") : "no floor")}");
}

// A buy range in prices: the most it pays, and the open under which it buys nothing, none where it has no floor.
public readonly record struct BuyRange(double Ceiling, double? Floor);

// How a buy in a range came out over the sessions after its night: bought at a session and price, not bought for the side
// of its range it opened on, or waiting where the history ends before either is known.
public sealed record RangeFill(WaitFill? Fill, string? NotBought)
{
    public bool Waiting => Fill is null && NotBought is null;
}

// One width's figures over a group of picks, every pick one the open buys and walks to its end: the picks offered, those
// the range bought, those it did not buy because they opened above or below it, the edge after costs it made summed with a
// pick not bought counting nought, and the edges the open made on the picks it did not buy, those above nought the winners
// it missed and those under the losses it kept out of, each summed as a positive figure.
public sealed record RangeTally(int Offered, int Bought, int Above, int Below, double Edge, double Missed, double Saved)
{
    public static RangeTally Empty { get; } = new(0, 0, 0, 0, 0, 0, 0);

    public double? EdgePerPick => Offered > 0 ? Edge / Offered : null;

    public double? MissedPerPick => Offered > 0 ? Missed / Offered : null;

    public double? SavedPerPick => Offered > 0 ? Saved / Offered : null;

    // A pick the open bought with its edge there, under the range bought with its own edge, or not bought on a side.
    public RangeTally Add(double openEdge, double? rangeEdge, string? notBought) =>
        notBought switch
        {
            BuyRanges.Above => this with { Offered = Offered + 1, Above = Above + 1, Missed = Missed + Math.Max(0, openEdge), Saved = Saved + Math.Max(0, -openEdge) },
            BuyRanges.Below => this with { Offered = Offered + 1, Below = Below + 1, Missed = Missed + Math.Max(0, openEdge), Saved = Saved + Math.Max(0, -openEdge) },
            _ => this with { Offered = Offered + 1, Bought = Bought + (rangeEdge is null ? 0 : 1), Edge = Edge + (rangeEdge ?? 0) },
        };

    public RangeTally Plus(RangeTally other) =>
        new(Offered + other.Offered, Bought + other.Bought, Above + other.Above, Below + other.Below, Edge + other.Edge, Missed + other.Missed, Saved + other.Saved);
}

// One row of the widths table: its index, its setup or every setup of the index together, its width or none for the buy at
// the open, its years, its figures and whether it is the width the index's early years chose.
public sealed record RangeWidthRow(string Index, string Family, RangeWidths? Widths, string Years, RangeTally Tally, bool Chosen)
{
    public string Width => Widths?.Key ?? BuyRanges.TheOpen;
}

// Every plan buying at the next open carries a range set from the last close: a ceiling some typical daily moves above it
// and a floor some under it, or no floor. An open inside the range buys at the open; an open above the ceiling places an
// order at the ceiling good for the session it opened and the one after, filling at the ceiling where the price comes back
// to it and at the second session's open where that opens at or under it, and buys nothing where neither does; an open under
// the floor buys nothing. The stop and the take-profit are placed from the price paid as the plan places them. A pick not
// bought keeps the result the open would have made, so what the range missed stands beside what it kept out of. The widths
// are chosen by test on 2005 to 2018 for each index and checked from 2019 on, and none is used before the operator approves
// it.
// see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
public static class BuyRanges
{
    // The candidate widths: the ceiling's, and the floor's with none for a range with no floor.
    public static IReadOnlyList<double> Ceilings { get; } = [0.5, 1, 1.5, 2];

    public static IReadOnlyList<double?> Floors { get; } = [1, 2, null];

    // The sessions an order at the ceiling stands for: the one the stock opened above it and the one after.
    public const int OrderSessions = 2;

    public const string Above = "above";

    public const string Below = "below";

    public const string NoFloor = "none";

    // The row a widths table names the buy at the open by, with no range.
    public const string TheOpen = "the open";

    // The rows reading every setup of an index together.
    public const string AllSetups = "all";

    public const string Early = "early";

    public const string Late = "late";

    public static IReadOnlyList<RangeWidths> Widths { get; } = [.. from ceiling in Ceilings from floor in Floors select new RangeWidths(ceiling, floor)];

    // The range a night's last close and typical daily move set; none where either is not above nought, and no floor where the
    // floor would stand at or under nought.
    public static BuyRange? Of(double close, double typicalMove, RangeWidths widths)
    {
        if (!(close > 0) || !(typicalMove > 0))
        {
            return null;
        }

        double? floor = widths.Floor is { } moves ? close - (moves * typicalMove) : null;

        return new BuyRange(close + (widths.Ceiling * typicalMove), floor is > 0 ? floor : null);
    }

    // Where a pick bought in its range fills over the bars after `pick`, its night's own bar: waiting while the history holds
    // no open after the night, or the order's sessions have not passed; not bought below where the first open stands under
    // the floor; filled as an order at the ceiling fills, at an open at or under it or at the ceiling where the price comes
    // back to it within the order's sessions, so an open inside the range fills at that open; and not bought above otherwise.
    public static RangeFill FillOf(ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, int pick, BuyRange range)
    {
        var next = pick + 1;

        if (next >= opens.Length || !(opens[next] > 0))
        {
            return new RangeFill(null, null);
        }

        if (range.Floor is { } floor && opens[next] < floor)
        {
            return new RangeFill(null, Below);
        }

        if (PlanPoints.LevelFillOf(opens, lows, pick, range.Ceiling, 0, OrderSessions) is { } filled)
        {
            return new RangeFill(filled, null);
        }

        return pick + OrderSessions < opens.Length && pick + OrderSessions < lows.Length ? new RangeFill(null, Above) : new RangeFill(null, null);
    }

    // The width chosen on an index: the highest edge per pick offered over 2005 to 2018 among those resting on the fewest fills
    // a recommendation rests on, a tie going to the one missing fewer picks, the wider ceiling and then no floor or the wider
    // floor; none where no width rests on enough. Nothing from 2019 on is read.
    public static RangeWidths? Chosen(IReadOnlyList<(RangeWidths Widths, RangeTally Early)> read) =>
        read
            .Where(one => one.Early.Bought >= PlanPoints.FewestFills && one.Early.EdgePerPick is not null)
            .OrderByDescending(one => one.Early.EdgePerPick)
            .ThenByDescending(one => one.Widths.Ceiling)
            .ThenByDescending(one => one.Widths.Floor ?? double.PositiveInfinity)
            .Select(one => one.Widths)
            .Cast<RangeWidths?>()
            .FirstOrDefault();

    // An index's rows from each setup's tallies by width, the buy at the open under no width: each setup's rows, then every
    // setup together, each over the early years and from 2019, the width chosen on the early years of every setup together
    // marked on both of its rows.
    public static IReadOnlyList<RangeWidthRow> Rows(string index, IReadOnlyList<(string Family, RangeWidths? Widths, RangeTally Early, RangeTally Late)> tallies)
    {
        var together = tallies
            .GroupBy(one => one.Widths)
            .Select(group => (Family: AllSetups, Widths: group.Key, Early: group.Aggregate(RangeTally.Empty, (sum, one) => sum.Plus(one.Early)), Late: group.Aggregate(RangeTally.Empty, (sum, one) => sum.Plus(one.Late))))
            .ToArray();
        var chosen = Chosen([.. together.Where(one => one.Widths is not null).Select(one => (one.Widths!.Value, one.Early))]);
        var rows = new List<RangeWidthRow>();

        foreach (var one in tallies.Concat(together))
        {
            var marked = one.Family == AllSetups && one.Widths is not null && one.Widths == chosen;

            rows.Add(new RangeWidthRow(index, one.Family, one.Widths, Early, one.Early, marked));
            rows.Add(new RangeWidthRow(index, one.Family, one.Widths, Late, one.Late, marked));
        }

        return rows;
    }

    // A card's plan: the range it buys in, then what it does on an open above it and, where it has a floor, under it.
    public static string Between(decimal ceiling, decimal? floor) =>
        floor is { } low ? $"Buys between {Dollars(low)} and {Dollars(ceiling)}" : $"Buys at {Dollars(ceiling)} or less";

    public static string AboveWords(decimal ceiling) =>
        string.Create(CultureInfo.InvariantCulture, $"Opens above {Dollars(ceiling)}: waits up to {OrderSessions} days for the price to come back to {Dollars(ceiling)}, then not bought.");

    public static string? BelowWords(decimal? floor) =>
        floor is { } low ? $"Opens below {Dollars(low)}: not bought." : null;

    // The lines a card's plan opens with, in order.
    public static IReadOnlyList<string> PlanLines(BuyRange range)
    {
        var ceiling = Statistic.ToPrice(range.Ceiling);
        decimal? floor = range.Floor is { } low ? Statistic.ToPrice(low) : null;

        return [Between(ceiling, floor), AboveWords(ceiling), .. BelowWords(floor) is { } below ? [below] : Array.Empty<string>()];
    }

    // What a pick not bought says, by the side of its range it opened on.
    public static string NotBoughtWords(string side) =>
        side == Above ? "not bought: opened above its range" : "not bought: opened below its range";

    static string Dollars(decimal price) => "$" + price.ToString("#,##0.00", CultureInfo.InvariantCulture);
}
