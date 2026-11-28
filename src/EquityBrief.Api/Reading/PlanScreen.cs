using EquityBrief.Core.Bars;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;
using static EquityBrief.Core.Paper.PlannedTrades;

namespace EquityBrief.Api.Reading;

// A live rule's trade under a plan that buys at a price as the S&P 400's and 600's trade tables, the chart page's brief and
// the waiting orders draw it, over the core's one reading of the plan.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
public static class PlanScreen
{
    // An S&P 400's or 600's trades with each family's whose live rule trades a plan drawn from that rule's own trades from the
    // night of its first planned trade, the page list's next-open trades of the family from then left out, and the dip's
    // alike.
    public static IReadOnlyList<IndexTradeRow> IndexRows(IReadOnlyList<IndexTradeRow> rows, IReadOnlyList<PlannedTradeRow> planned)
    {
        PlannedTradeRow[] buying = [.. LiveBuying(planned)];
        var from = buying.GroupBy(row => row.Family, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Min(row => row.Listed), StringComparer.Ordinal);

        return
        [
            .. rows.Where(row => !(from.TryGetValue(row.Family, out var start) && row.Listed >= start)),
            .. buying.Select(IndexRow),
        ];
    }

    // A held trade on the chart page's brief as its plan made it, where a live rule's plan buying at a price bought it: its
    // fill, the stop the plan placed from that fill and the plan's target.
    public static BriefHeld Held(BriefHeld held, string ticker, IReadOnlyList<PlannedTradeRow> planned) =>
        LiveBuying(planned).FirstOrDefault(row => row.Family == held.Family && row.Ticker == ticker && row.Listed == held.Listed) is { Fill: not null } row
            ? held with { Planned = true, PlanStop = StopOf(row), PlanTarget = TakeProfitOf(row) }
            : held;

    // The short-term dip's trades alone with its plan's, and the swing families' alone with theirs, for a page reading one
    // list and not the other.
    public static IReadOnlyList<IndexTradeRow> DipRows(IReadOnlyList<IndexTradeRow> dips, IReadOnlyList<PlannedTradeRow> planned) =>
        IndexRows(dips, [.. planned.Where(row => row.Family == Core.Families.DipRule.Name)]);

    public static IReadOnlyList<IndexTradeRow> SwingRows(IReadOnlyList<IndexTradeRow> trades, IReadOnlyList<PlannedTradeRow> planned) =>
        IndexRows(trades, [.. planned.Where(row => row.Family != Core.Families.DipRule.Name)]);

    static IndexTradeRow IndexRow(PlannedTradeRow row)
    {
        var stop = StopOf(row) ?? row.Stop ?? row.Entry;

        return new IndexTradeRow(
            row.Index,
            row.Family,
            row.Ticker,
            row.Listed,
            row.Place,
            row.Fill ?? Price(row) ?? row.Entry,
            stop,
            TakeProfitOf(row),
            row.Trail,
            row.Cap,
            row.Fill is not null ? row.EndedOn : null,
            row.Fill is not null ? row.Result : null,
            null,
            row.Fill,
            row.Fill is not null ? null : row.NotFilled ? PlanOrderWords.NotFilledOf(row.Order) : WaitingWords(row),
            null,
            row.Fill is not null ? row.Sale : null)
        {
            UnderPlan = true,
            FilledOn = row.FilledOn,
        };
    }

    // On the operator's answers of 2026-11-16 the live rules' orders placed on the night drawn that buy at a price, by family
    // and stock, which the pages count among tonight's picks bought only on a dip.
    public static IReadOnlyDictionary<(string Family, string Ticker), PlannedTradeRow> PlacedTonight(IReadOnlyList<PlannedTradeRow> planned, DateOnly night) =>
        LiveBuying(planned).Where(row => row.Listed == night).GroupBy(row => (row.Family, row.Ticker)).ToDictionary(group => group.Key, group => group.First());

    // The orders from nights before the one drawn still waiting for their price, each with its price and its last session,
    // in the order they were listed, which Home, Tonight and Past picks draw as a group of their own.
    // From the operator's order of 2026-11-28 an order that filled on the night stays on the list that night, saying it was bought
    // and at what price, and leaves it the night after.
    // see: A waiting order's fill odds are how often the ledger's past orders of its rule this far away with as long left filled before their last day
    public static IReadOnlyList<WaitingOrderView> StillWaiting(IReadOnlyList<PlannedTradeRow> planned, DateOnly night) =>
    [
        .. LiveBuying(planned)
            .Where(row => ((IsWaiting(row) && !row.Order.PlacesNoPrice()) || (row.FilledOn == night && row.Fill is not null)) && row.Listed < night)
            .OrderBy(row => row.Listed)
            .ThenBy(row => row.Place)
            .Select(row => new WaitingOrderView(row.Family, row.Ticker, row.Listed, Price(row), LastDay(row.Listed, row.Order.Window))
            {
                // From the operator's answers of 2026-11-16 the stop and the sale its card draws, as a pick's card works them.
                Stop = CardScreen.PlanOf(row.Order).Stop,
                Sale = CardScreen.PlanOf(row.Order).Sale,
                Typical = row.Order.TypicalDay,
                Bought = row.FilledOn == night && row.Fill is { } paid ? FillOdds.BoughtLine(night, paid) : null,
            }),
    ];

    // A waiting card's fill odds as of a night: its distance above its buy price in the typical daily move its plan was placed
    // with, the trading days it has left after the night, and the cell of its rule's past orders on the index matching both.
    public static WaitingOrderView WithOdds(WaitingOrderView order, DateOnly night, IReadOnlyDictionary<string, IReadOnlyList<FillOddsCell>> odds)
    {
        if (order.Bought is not null || order.Price is not { } price || order.LastClose is not { } close)
        {
            return order;
        }

        var left = DaysLeft(night, order.LastDay);
        var cell = order.Typical is { } typical && typical > 0 && odds.TryGetValue(order.Family, out var cells)
            ? FillOdds.CellOf(cells, OrderChanges.MovesAbove(close, price, typical), left)
            : null;

        return order with { Odds = FillOdds.Line(close, price, left, cell) };
    }

    // The trading sessions after a night up to an order's last day.
    public static int DaysLeft(DateOnly night, DateOnly last)
    {
        var left = 0;

        for (var day = night.AddDays(1); day <= last; day = day.AddDays(1))
        {
            left += ExchangeClosures.IsSession(day) ? 1 : 0;
        }

        return left;
    }
}
