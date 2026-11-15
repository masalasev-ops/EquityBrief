using EquityBrief.Core.Bars;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// From the 23.10 correction of 2026-11-13 a live rule's trade under a plan that buys at a price as every page draws it: the
// plan's own trade, filled only once its price traded, its stop placed from that fill, waiting with its price and its last
// session while it waits, and "not filled, no trade" once its window passed unfilled, which no group, rate, account or record
// counts. Only picks listed since the restart under such a rule; a trade an earlier version bought at the next open, and a
// plan buying at the next open, are drawn as they are.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
public static class PlanScreen
{
    // Whether a plan buys at a price: every entry but the next open, which fills as the list's own trade does.
    public static bool BuysAtAPrice(PlannedTradeRow row) => !string.Equals(row.Order.Entry, PlanPoints.NextOpen, StringComparison.Ordinal);

    // The live rules' trades under a plan buying at a price, a stock a family listed on a night once.
    static IEnumerable<PlannedTradeRow> LiveBuying(IReadOnlyList<PlannedTradeRow> planned) =>
        planned.Where(row => row.Live && BuysAtAPrice(row)).GroupBy(row => (row.Family, row.Ticker, row.Listed)).Select(group => group.First());

    // Each rule's account with the stocks it kept that no fill bought worded as its plan stands: an order still waiting with
    // its price and last session, and one its window passed unfilled as not filled, never as waiting on the next open.
    public static IReadOnlyList<PaperAccountSource> Accounts(IReadOnlyList<PaperAccountSource> sources, IReadOnlyList<PlannedTradeRow> planned)
    {
        var byKey = planned.Where(BuysAtAPrice).GroupBy(row => (row.Owner, row.Ticker, row.Listed)).ToDictionary(group => group.Key, group => group.First());

        return
        [
            .. sources.Select(account => account.Listed is { } owner && account.Unfilled is { Count: > 0 } unfilled
                ? account with
                {
                    Unfilled =
                    [
                        .. unfilled.Select(one => byKey.TryGetValue((owner, one.Ticker, one.Listed), out var row) && row.Fill is null
                            ? (one.Ticker, one.Listed, row.NotFilled ? PlanOrderWords.NotFilledOf(row.Order) : WaitingWords(row))
                            : one),
                    ],
                }
                : account),
        ];
    }

    // The last session an order waits: the window's sessions on the exchange calendar after the night that listed it.
    public static DateOnly LastDay(DateOnly listed, int window)
    {
        var day = listed;

        for (var counted = 0; counted < window;)
        {
            day = day.AddDays(1);

            if (ExchangeClosures.IsSession(day))
            {
                counted++;
            }
        }

        return day;
    }

    public static bool IsWaiting(PlannedTradeRow row) => row.Fill is null && !row.NotFilled;

    public static decimal? Price(PlannedTradeRow row) => row.Order.Level is { } level ? Statistic.ToPrice(level) : null;

    public static string WaitingWords(PlannedTradeRow row) => PlanOrderWords.Waiting(Price(row), LastDay(row.Listed, row.Order.Window));

    // The plan's stop placed from its fill, or from its price while it waits, the rule's own where the plan places none.
    public static decimal? StopOf(PlannedTradeRow row) =>
        (row.Fill is { } fill ? Statistic.FromPrice(fill) : row.Order.Level) is { } from
        && PlanPoints.StopOf(row.Order.Stop, from, row.Order.RuleStop, row.Order.ZoneStop, row.Order.TypicalDay) is { } placed
            ? Statistic.ToPrice(placed)
            : row.Stop;

    // How a filled plan trade ended, read off its sale against its target and its stop, time otherwise; from the operator's order
    // of 2026-11-14 its target the price its walk took profit at from its fill.
    static string Ending(PlannedTradeRow row, decimal? stop) =>
        row.Sale is { } sale && TargetOf(row, stop) is { } target && sale >= target ? PickStatus.Target
        : row.Sale is { } sold && stop is { } floor && sold <= floor ? PickStatus.Stopped
        : PickStatus.Time;

    static decimal? TargetOf(PlannedTradeRow row, decimal? stop) =>
        (row.Fill is { } fill && stop is { } floor
            ? row.Order.SaleFrom(Statistic.FromPrice(fill), Statistic.FromPrice(floor)).Target
            : row.Order.Target) is { } target
            ? Statistic.ToPrice(target)
            : null;

    // The S&P 500's page list's cells with each a planned live rule listed drawn as its plan's trade.
    public static IReadOnlyList<PickCell> Cells(IReadOnlyList<PickCell> cells, IReadOnlyList<PlannedTradeRow> planned, DateOnly asOf)
    {
        var byKey = LiveBuying(planned).ToDictionary(row => (row.Family, row.Ticker, row.Listed));

        return [.. cells.Select(cell => cell.RepeatOf is null && byKey.TryGetValue((cell.Family, cell.Ticker, cell.Night), out var row) ? Cell(cell, row, asOf) : cell)];
    }

    static PickCell Cell(PickCell cell, PlannedTradeRow row, DateOnly asOf)
    {
        var stop = StopOf(row);

        if (row.Fill is not { } fill)
        {
            return cell with
            {
                Buy = Price(row) ?? cell.Buy,
                Stop = stop,
                Status = row.NotFilled ? PickStatus.NotFilled : PickStatus.Waiting,
                Result = null,
                Along = null,
                EndedOn = null,
                ReturnPct = null,
                NowOn = null,
                NowClose = null,
                Past = null,
                Unfilled = row.NotFilled ? PlanOrderWords.NotFilledOf(row.Order) : WaitingWords(row),
                Waiting = false,
                FilledOn = null,
                FiredAs = null,
                FiredOn = null,
                SoldOn = null,
                Sale = null,
                UnderPlan = true,
            };
        }

        var ended = row.EndedOn is { } end && end <= asOf;

        return cell with
        {
            Buy = fill,
            Stop = stop,
            Status = ended ? Ending(row, stop) : PickStatus.Open,
            Result = ended ? row.Result : null,
            EndedOn = ended ? row.EndedOn : null,
            ReturnPct = ended && row.Sale is { } sale && fill > 0 ? Statistic.FromRatio((sale / fill) - 1m) * 100 : null,
            Along = !ended && cell.NowClose is { } now && fill > 0 ? Statistic.FromRatio(now / fill) : null,
            Unfilled = null,
            Waiting = false,
            FilledOn = row.FilledOn,
            Past = null,
            FiredAs = null,
            FiredOn = null,
            SoldOn = ended ? row.EndedOn : null,
            Sale = ended ? row.Sale : null,
            UnderPlan = true,
        };
    }

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
            ? held with { Planned = true, PlanStop = StopOf(row), PlanTarget = row.Order.Target is { } target ? Statistic.ToPrice(target) : row.Target }
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
            row.Order.Target is { } target ? Statistic.ToPrice(target) : row.Target,
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

    // The orders from nights before the one drawn still waiting for their price, each with its price and its last session,
    // in the order they were listed, which Home, Tonight and Past picks draw as a group of their own.
    public static IReadOnlyList<WaitingOrderView> StillWaiting(IReadOnlyList<PlannedTradeRow> planned, DateOnly night) =>
    [
        .. LiveBuying(planned)
            .Where(row => IsWaiting(row) && row.Listed < night)
            .OrderBy(row => row.Listed)
            .ThenBy(row => row.Place)
            .Select(row => new WaitingOrderView(row.Family, row.Ticker, row.Listed, Price(row), LastDay(row.Listed, row.Order.Window))),
    ];
}
