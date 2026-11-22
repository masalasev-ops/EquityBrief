using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;

namespace EquityBrief.Core.Paper;

// One trade the live list recommended, as the store holds it: the name the swing filter passed on a night it listed, the
// plan that night's trade gate read, its entry, stop and target, the forward return that plan was scored under with
// whether a row is stored at all, the company's name, the close the name's bars hold now for its listing night, and the
// newest close at or before the night asked for.
public sealed record PickRow(
    string Ticker,
    DateOnly Night,
    string Plan,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    bool OutcomeStored,
    string? Outcome,
    DateOnly? ResolvedOn,
    double? ReturnPct,
    double? BreakEven,
    string? Company,
    decimal? ListingClose,
    DateOnly? NowOn,
    decimal? NowClose,
    // The state the member's reported quarters gave it on its listing night, and none on a night that
    // stored no readings.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    string? State = null,
    // The setup family the page listed the trade under, and the other families the stock qualified under
    // that night, which a night the families did not draw holds none of.
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    string Family = SetupFamilies.Pullback,
    IReadOnlyList<string>? Also = null,
    // What the plan put at risk from its fill, as the filler stored it beside the outcome.
    double? PlannedRisk = null,
    // The trade's fill at the next session's open: its result there in the risk its close put up, the open it was bought at,
    // the session of that open, whether that open was past its stop or its target, and why it holds no fill; all none while
    // it waits on its next session.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    double? FillResult = null,
    decimal? Fill = null,
    DateOnly? FilledOn = null,
    string? Past = null,
    string? Unfilled = null,
    // The session the next open sold it at and the price, both none while that sale waits on its open.
    DateOnly? SoldOn = null,
    decimal? Sale = null);

// The live list's trades as of a night, each what became of it, the sessions it was held, its result in multiples of the
// risk it took and where its price stood against its buy as a ratio, which Past picks, a name's own region and the S&P
// 500's paper accounts read alike.
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
// see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
public static class PickCells
{
    // Every trade as of a night, in the order the rows came: newest night first, and within a night the order that night's
    // list was drawn in. A trade a setup on provisional settings listed is marked so, by the setups handed in as provisional.
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    public static IReadOnlyList<PickCell> Of(IReadOnlyList<PickRow> rows, DateOnly asOf, IReadOnlySet<string>? provisional = null)
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

    static PickCell Cell(PickRow row, DateOnly asOf)
    {
        // A trade the store says finished after the night drawn was open on it.
        var finishedBy = row.ResolvedOn is { } resolved && resolved <= asOf;

        // A trade is open only once bought: a listing whose next session's open has not filled it yet is still to buy.
        // see: Every count of trades names its group and one function works the four groups
        var ending = row.Outcome switch
        {
            ForwardReturnSeries.Win => PickStates.Target,
            ForwardReturnSeries.Loss => PickStates.Stopped,
            // A trade sold at a close under its trailing stop left at its stop, at a gain or a loss.
            TrailingExit.Trailed => PickStates.Stopped,
            ForwardReturnSeries.Unresolved => PickStates.Time,
            _ => PickStates.Missing,
        };

        // A trade whose exit fired on a close is held until the next open sells it: open, saying why it sells, and ended on the
        // night after the sale with the sale's price. One no fill bought has no sale to wait on.
        // see: Every count of trades names its group and one function works the four groups
        var sold = row.SoldOn is { } soldOn ? soldOn <= asOf : row.Fill is null || row.FillResult is not null;
        var selling = row.OutcomeStored && finishedBy && !sold && ending != PickStates.Missing;

        var status = !row.OutcomeStored
            ? PickStates.Missing
            : row.Fill is null && row.Unfilled is null && row.Outcome is null
                ? PickStates.ToBuy
                : row.Outcome is null || !finishedBy || selling
                ? PickStates.Open
                : ending;

        var family = SetupFamilies.Named(row.Family);

        var finished = status is PickStates.Target or PickStates.Stopped or PickStates.Time;

        // A trade is bought at its fill at the next session's open: its result is the fill's, in the risk its close put up, its
        // move and the price line are read from the fill, and the share reaching its target and the break-even it is held to
        // stay its plan's, which the fill does not move. A trade waiting on its next session draws its plan's buy and says it
        // buys at the next open; one with no outcome row draws its plan's buy alone.
        // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
        var buy = row.Fill ?? row.Entry;
        double? returned = finished && row.ReturnPct is { } made && row.ListingClose is { } close && close > 0 && row.Fill is { } fill && fill > 0
            ? (Statistic.FromRatio(close / fill) * (1 + (made / 100)) - 1) * 100
            : null;

        double? along = finished
            ? returned is { } moved ? 1 + (moved / 100) : null
            : status == PickStates.Open && row.NowClose is { } now && (row.Fill ?? row.ListingClose) is { } then && then > 0
                ? Statistic.FromRatio(now / then)
                : null;

        return new PickCell(
            row.Ticker,
            row.Company,
            row.Night,
            row.Plan,
            buy,
            row.Stop,
            row.Target,
            status,
            ExchangeClosures.SessionsUntil(row.Night, finished ? row.ResolvedOn!.Value : asOf),
            finished ? row.FillResult : null,
            along,
            finished ? row.ResolvedOn : null,
            returned,
            status == PickStates.Open ? row.NowOn : null,
            status == PickStates.Open ? row.NowClose : null,
            row.BreakEven,
            row.State,
            Family: row.Family,
            Setup: family?.Label ?? row.Family,
            Trailing: family?.Trails ?? false,
            Planned: row.Entry,
            Past: row.Past,
            Unfilled: row.Unfilled,
            Waiting: row.OutcomeStored && row.Fill is null && row.Unfilled is null,
            FilledOn: row.FilledOn,
            Cap: CapOf(row),
            FiredAs: selling ? ending : null,
            FiredOn: selling ? row.ResolvedOn : null,
            SoldOn: finished ? row.SoldOn : null,
            Sale: finished ? row.Sale : null);
    }
}
