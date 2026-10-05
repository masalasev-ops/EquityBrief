using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// The projection from stored rows to what the Past picks screen and a name's own region draw.
//
// It sits beside `UniverseScreen` and derives for the reason that one does: four figures the screen
// draws are stored nowhere. What became of a trade as of the night drawn, which on a page for an earlier
// night is open where the store says it finished later. The sessions it was held, counted on the exchange
// calendar from the night it was listed. Its result in multiples of the risk it took, the stored return
// over the risk as a share of the buy. And where the price stood against the buy, read as ratios so a
// split since the listing moves neither side. Not in the read API, whose claim is that it computes
// nothing, and not in the page, whose claim is the same.
// see: A screen reads and renders, and computes nothing
// see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
public static class PicksScreen
{
    // Every trade as of a night, in the order the rows came: newest night first, and within a night the
    // order that night's list was drawn in, improving businesses first where it stored its readings and the
    // filter's own order where it stored none.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    // A trade a setup on provisional settings listed is marked so, by the setups handed in as provisional.
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    public static IReadOnlyList<PickCell> Cells(IReadOnlyList<PickRow> rows, DateOnly asOf, IReadOnlySet<string>? provisional = null)
    {
        // Each listing walked against the trades before it: a listing made while the stock's kept trade was
        // still open repeats that trade, and is drawn marked and counted in no total.
        // see: A repeat listing made before the rule reached the filter is marked and counted once
        var walked = OpenTrades.Walk(rows.Select(Listing));

        return [.. rows.Select(row => Cell(row, asOf) with { RepeatOf = walked[(row.Ticker, row.Night)], Provisional = provisional?.Contains(row.Family) ?? false })];
    }

    // A trade as the open trade rule reads it: the stock, the night, and what became of the plan its night
    // traded on its capped horizon, with whether an outcome row is stored at all.
    public static OpenTradeListing Listing(PickRow row) =>
        new(row.Ticker, row.Night, row.OutcomeStored, row.Outcome, row.ResolvedOn, CapOf(row));

    // The sessions a trade is given: its family's own cap where the family is scored on a horizon of its
    // own, and the cap of the plan its night traded where the trade is the pullback's.
    static int CapOf(PickRow row) =>
        SetupFamilies.Named(row.Family) is { OnThePullbacksPlan: false } family ? family.CapSessions : ForwardReturnSeries.CapOf(row.Plan);

    // The trades one setup family's filter keeps. A setup the page does not draw keeps every trade, as all does.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public static IReadOnlyList<PickCell> OfSetup(IReadOnlyList<PickCell> cells, string? setup) =>
        setup is { } asked && SetupFamilies.Named(asked) is not null
            ? [.. cells.Where(cell => cell.Family == asked)]
            : cells;

    // The setups the page has listed a trade under, in the page's order, each with its trades, a listing
    // that repeated a trade still open counted in none.
    public static IReadOnlyList<(string Family, string Label, int Trades)> Setups(IReadOnlyList<PickCell> cells) =>
    [
        .. SetupFamilies.InPageOrder
            .Select(family => (family.Name, family.Label, cells.Count(cell => cell.Family == family.Name && cell.RepeatOf is null)))
            .Where(setup => setup.Item3 > 0),
    ];

    // The sector heavyweights' holdings as of a night, newest buy first and its sectors in order, each ended one with
    // its result, its size cut's return and the edge between them as the rule reads it, an open one with none.
    // see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
    // An S&P 400's or 600's trades newest first, each named by its setup's label with its result before and after its
    // cost, and its sector heavyweights' holdings as the S&P 500's are drawn.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    public static IReadOnlyList<IndexTradeCell> IndexTrades(IReadOnlyList<IndexTradeRow> trades, IReadOnlyDictionary<string, string?> companies) =>
    [
        .. trades
            .OrderByDescending(trade => trade.Listed)
            .ThenBy(trade => trade.Place)
            .ThenBy(trade => trade.Ticker, StringComparer.Ordinal)
            .Select(trade => new IndexTradeCell(
                trade.Ticker,
                companies.GetValueOrDefault(trade.Ticker),
                trade.Family,
                SetupFamilies.Named(trade.Family)?.Label ?? trade.Family,
                trade.Listed,
                trade.Entry,
                trade.Stop,
                trade.Target,
                trade.Trail is not null,
                trade.EndedOn,
                trade.Result,
                trade.Cost,
                trade.Result is { } result && trade.Cost is { } cost ? result - cost : null)),
    ];

    public static IReadOnlyList<HeavyweightPickCell> IndexHeavyweights(IReadOnlyList<IndexHoldingRow> holdings) =>
        Heavyweights([.. holdings.Select(holding => new HeavyweightHoldingRow(
            holding.Ticker,
            holding.EnteredOn,
            holding.Sector,
            string.Empty,
            holding.EntryClose,
            holding.EndedOn,
            holding.ExitClose,
            holding.Reason,
            holding.Result,
            holding.CutReturn))]);

    public static IReadOnlyList<HeavyweightPickCell> Heavyweights(IReadOnlyList<HeavyweightHoldingRow> holdings) =>
    [
        .. holdings
            .OrderByDescending(holding => holding.EnteredOn)
            .ThenBy(holding => holding.Sector, StringComparer.Ordinal)
            .ThenBy(holding => holding.Ticker, StringComparer.Ordinal)
            .Select(holding => new HeavyweightPickCell(
                holding.Ticker,
                holding.Sector,
                holding.EnteredOn,
                holding.EntryClose,
                holding.EndedOn,
                holding.ExitClose,
                holding.Reason,
                holding.Result,
                holding.CutReturn,
                HeavyweightRule.Edge(holding.Result, holding.CutReturn))),
    ];

    // A name's trades listed before the night its page draws, which is what "On the list before" states:
    // the night's own listing is the page's subject rather than one before it.
    public static IReadOnlyList<PickCell> Before(IReadOnlyList<PickRow> rows, DateOnly night) =>
        [.. Cells(rows, night).Where(cell => cell.Night < night)];

    // The trades a filter keeps. A status the screen does not filter by keeps every trade, as all does.
    public static IReadOnlyList<PickCell> Filtered(IReadOnlyList<PickCell> cells, string? status) =>
        status is { } asked && PickStatus.Filters.Contains(asked, StringComparer.Ordinal)
            ? [.. cells.Where(cell => cell.Status == asked)]
            : cells;

    // The trades counted, and the three figures that wait on the minimum computed only once both of its
    // counts are met. The counts are the run page's: the trades decided at their target or their stop
    // that set a bar, and the nights they were listed on, against the two minimums its reason records
    // wait on. A trade that ran out of time is finished and never in the share, as an unresolved setup is
    // never in a reason's, and it counts in the average result, which is what each trade earned. A share
    // over fewer is not drawn because it is not computed.
    // see: An unresolved setup is never a win
    public static PicksSummary Summary(IReadOnlyList<PickCell> cells)
    {
        // A listing that repeated a trade still open is one move counted already, so it is in no count.
        // see: A repeat listing made before the rule reached the filter is marked and counted once
        var counted = cells.Where(cell => cell.RepeatOf is null).ToArray();
        var finished = counted.Where(cell => cell.Status is PickStatus.Target or PickStatus.Stopped or PickStatus.Time).ToArray();

        // A trade a setup on provisional settings listed is in neither the share nor the average: its
        // setup's record starts at its freeze.
        // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
        var decided = finished.Where(cell => !cell.Provisional && ForwardReturnSeries.IsScored(Outcome(cell.Status), cell.BreakEven)).ToArray();
        var nights = decided.Select(cell => cell.Night).Distinct().Count();
        var met = decided.Length >= ReasonVerdict.MinimumResolved && nights >= ReasonVerdict.MinimumSessions;

        var results = finished.Where(cell => !cell.Provisional && cell.Result is not null).Select(cell => cell.Result!.Value).ToArray();

        return new PicksSummary(
            counted.Length,
            counted.Select(cell => cell.Night).Distinct().Count(),
            counted.Count(cell => cell.Status == PickStatus.Open),
            finished.Count(cell => cell.Status == PickStatus.Target),
            finished.Count(cell => cell.Status == PickStatus.Stopped),
            finished.Count(cell => cell.Status == PickStatus.Time),
            counted.Count(cell => cell.Status == PickStatus.Missing),
            decided.Length,
            nights,
            ReasonVerdict.MinimumResolved,
            ReasonVerdict.MinimumSessions,
            met ? 100.0 * decided.Count(cell => cell.Status == PickStatus.Target) / decided.Length : null,
            met ? decided.Average(cell => cell.BreakEven!.Value) : null,
            met && results.Length > 0 ? results.Average() : null,
            cells.Count - counted.Length,
            counted.Count(cell => cell.Provisional));
    }

    // The stored outcome a status stands for, which the run page's own test of a scored setup reads.
    static string? Outcome(string status) => status switch
    {
        PickStatus.Target => ForwardReturnSeries.Win,
        PickStatus.Stopped => ForwardReturnSeries.Loss,
        _ => null,
    };

    static PickCell Cell(PickRow row, DateOnly asOf)
    {
        // A trade the store says finished after the night drawn was open on it.
        var finishedBy = row.ResolvedOn is { } resolved && resolved <= asOf;

        var status = !row.OutcomeStored
            ? PickStatus.Missing
            : row.Outcome is null || !finishedBy
                ? PickStatus.Open
                : row.Outcome switch
                {
                    ForwardReturnSeries.Win => PickStatus.Target,
                    ForwardReturnSeries.Loss => PickStatus.Stopped,
                    // A trade sold at a close under its trailing stop left at its stop, at a gain or a loss.
                    TrailingExit.Trailed => PickStatus.Stopped,
                    ForwardReturnSeries.Unresolved => PickStatus.Time,
                    _ => PickStatus.Missing,
                };

        var family = SetupFamilies.Named(row.Family);

        var finished = status is PickStatus.Target or PickStatus.Stopped or PickStatus.Time;

        // The risk as a percentage of the buy, which a finished trade's return is stated in multiples of.
        double? risk = row.Entry is { } buy && row.Stop is { } stop && buy > 0 && stop < buy
            ? Statistic.FromRatio((buy - stop) / buy) * 100
            : null;

        double? along = finished
            ? row.ReturnPct is { } made ? 1 + (made / 100) : null
            : status == PickStatus.Open && row.NowClose is { } now && row.ListingClose is { } then && then > 0
                ? Statistic.FromRatio(now / then)
                : null;

        return new PickCell(
            row.Ticker,
            row.Company,
            row.Night,
            row.Plan,
            row.Entry,
            row.Stop,
            row.Target,
            status,
            ExchangeClosures.SessionsUntil(row.Night, finished ? row.ResolvedOn!.Value : asOf),
            finished && row.ReturnPct is { } returned && risk is { } risked ? returned / risked : null,
            along,
            finished ? row.ResolvedOn : null,
            finished ? row.ReturnPct : null,
            status == PickStatus.Open ? row.NowOn : null,
            status == PickStatus.Open ? row.NowClose : null,
            row.BreakEven,
            row.State,
            Family: row.Family,
            Setup: family?.Label ?? row.Family,
            Trailing: family?.Trails ?? false);
    }
}
