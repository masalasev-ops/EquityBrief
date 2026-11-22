using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Paper;

// A live rule's trade under a plan that buys at a price as every page and the paper accounts read it: the plan's own trade,
// filled only once its price traded, its stop placed from that fill, waiting with its price and its last session while it
// waits, and "not filled, no trade" once its window passed unfilled, which no group, rate, account or record counts. Only
// picks listed since the restart under such a rule; a trade an earlier version bought at the next open, and a plan buying
// at the next open, are read as they are.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
public static class PlannedTrades
{
    // Whether a plan buys at a price: every entry but the next open, which fills as the list's own trade does.
    public static bool BuysAtAPrice(PlannedTradeRow row) => !string.Equals(row.Order.Entry, PlanPoints.NextOpen, StringComparison.Ordinal);

    // The live rules' trades under a plan buying at a price, a stock a family listed on a night once.
    public static IEnumerable<PlannedTradeRow> LiveBuying(IReadOnlyList<PlannedTradeRow> planned) =>
        planned.Where(row => row.Live && BuysAtAPrice(row)).GroupBy(row => (row.Family, row.Ticker, row.Listed)).Select(group => group.First());

    // Each rule's account with the stocks it kept that no fill bought worded as its plan stands: an order still waiting with
    // its price and last session, and one its window passed unfilled as not filled, never as waiting on the next open.
    public static IReadOnlyList<PaperAccountSource> Accounts(IReadOnlyList<PaperAccountSource> sources, IReadOnlyList<PlannedTradeRow> planned)
    {
        var byKey = planned.Where(BuysAtAPrice).GroupBy(row => (row.Owner, row.Ticker, row.Listed)).ToDictionary(group => group.Key, group => group.First());

        return
        [
            .. Exited(sources, planned).Select(account => account.Listed is { } owner && account.Unfilled is { Count: > 0 } unfilled
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

    // Each rule's trades kept under a plan with the stop the plan placed from the fill and the one exit its walk sells by, every
    // rule's, a variant's as its rule's, in place of the stop and target its listing stored.
    // see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
    static IEnumerable<PaperAccountSource> Exited(IReadOnlyList<PaperAccountSource> sources, IReadOnlyList<PlannedTradeRow> planned)
    {
        var filled = planned.Where(row => row.Fill is not null).GroupBy(row => (row.Owner, row.Ticker, row.Listed)).ToDictionary(group => group.Key, group => group.First());

        foreach (var account in sources)
        {
            yield return account.Listed is { } owner && account.Trades.Any(one => filled.ContainsKey((owner, one.Trade.Ticker, one.Trade.ListedOn)))
                ? account with
                {
                    Trades =
                    [
                        .. account.Trades.Select(one => filled.TryGetValue((owner, one.Trade.Ticker, one.Trade.ListedOn), out var row)
                            ? (one.Trade with { Stop = StopOf(row), Target = TakeProfitOf(row), Trail = row.Order.Trail is { } trail ? Statistic.ToPrice(trail) : one.Trade.Trail }, one.Result)
                            : one),
                    ],
                }
                : account;
        }
    }

    // The last session an order waits: the window's sessions on the exchange calendar after the night that listed it.
    public static DateOnly LastDay(DateOnly listed, int window) => PlanOrderWords.LastDay(listed, window);

    public static bool IsWaiting(PlannedTradeRow row) => row.Fill is null && !row.NotFilled;

    public static decimal? Price(PlannedTradeRow row) => row.Order.Level is { } level ? Statistic.ToPrice(level) : null;

    public static string WaitingWords(PlannedTradeRow row) => PlanOrderWords.Waiting(Price(row), LastDay(row.Listed, row.Order.Window));

    // The one exit the plan's walk sells the trade by, from its fill, or while it waits from the price it waits for; none for an
    // order buying at the next open that has not filled, whose price is not known.
    // see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
    public static TradeExit? ExitOf(PlannedTradeRow row) =>
        row.Fill is { } fill ? TradeExit.Filled(row.Order, Statistic.FromPrice(fill)) : TradeExit.Waiting(row.Order);

    // The price the plan first takes profit at, which every page draws as its take-profit.
    public static decimal? TakeProfitOf(PlannedTradeRow row) => ExitOf(row)?.First is { } first ? Statistic.ToPrice(first) : null;

    // Which part of its plan placed a trade's take-profit, in the words its row draws beneath the price: the band above read
    // on its night, its setup's own sale, the price it turned down at, or the multiple of the risk, saying where the band its
    // plan sells at first stood none on its night; none where it names no price.
    // see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
    public static string? TakeProfitFromWords(PlannedTradeRow row)
    {
        var from = row.Fill is { } fill ? Statistic.FromPrice(fill) : row.Order.Level;

        if (from is not { } buy || PlanPoints.StopOf(row.Order.Stop, buy, row.Order.RuleStop, row.Order.ZoneStop, row.Order.TypicalDay) is not { } stop || !(buy > stop))
        {
            return null;
        }

        var exit = TradeExit.Of(row.Order, buy, stop);
        var sale = PlanPrices.Named(row.Order.Words)?.Exit;
        var multiple = row.Order.TakeProfit is { } times ? FormattableString.Invariant($"{times:0.##} times the risk") : null;

        if (exit.FirstIsPart)
        {
            return multiple is null ? "on part of it" : "on half, at " + multiple;
        }

        if (exit.Whole is null)
        {
            return null;
        }

        if (row.Order.Sale.Target is { } own && own > buy)
        {
            return sale == PlanPoints.FirstBand ? "the band above, read on its night" : "its setup's own sale";
        }

        return row.Order.TakeProfitFrom(buy, stop)?.Source switch
        {
            TakeProfitSource.TurnedDown => "where it turned down before",
            TakeProfitSource.Risk => sale == PlanPoints.FirstBand ? multiple + ": no band above on its night" : multiple,
            _ => null,
        };
    }

    // The plan's stop placed from its fill, or from its price while it waits, the rule's own where the plan places none.
    public static decimal? StopOf(PlannedTradeRow row) =>
        (row.Fill is { } fill ? Statistic.FromPrice(fill) : row.Order.Level) is { } from
        && PlanPoints.StopOf(row.Order.Stop, from, row.Order.RuleStop, row.Order.ZoneStop, row.Order.TypicalDay) is { } placed
            ? Statistic.ToPrice(placed)
            : row.Stop;

    // How a filled plan trade ended, read off its sale against its target and its stop, time otherwise; its target the price
    // its walk took profit at from its fill.
    static string Ending(PlannedTradeRow row, decimal? stop) =>
        row.Sale is { } sale && TargetOf(row, stop) is { } target && sale >= target ? PickStates.Target
        : row.Sale is { } sold && stop is { } floor && sold <= floor ? PickStates.Stopped
        : PickStates.Time;

    static decimal? TargetOf(PlannedTradeRow row, decimal? stop) =>
        (row.Fill is { } fill && stop is { } floor
            ? row.Order.SaleFrom(Statistic.FromPrice(fill), Statistic.FromPrice(floor)).Target
            : row.Order.Target) is { } target
            ? Statistic.ToPrice(target)
            : null;

    // The S&P 500's page list's cells with each a planned live rule listed read as its plan's trade.
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
                Target = TakeProfitOf(row),
                Status = row.NotFilled ? PickStates.NotFilled : PickStates.Waiting,
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
            Target = TakeProfitOf(row),
            Status = ended ? Ending(row, stop) : PickStates.Open,
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
}
