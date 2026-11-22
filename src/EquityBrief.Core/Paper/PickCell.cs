using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;

namespace EquityBrief.Core.Paper;

// One trade the live list recommended, as Past picks, a name's own page and the S&P 500's paper accounts read it: the
// night it was listed, the name, the plan's buy, stop and target, what became of it as of the night read, the sessions it
// was held, its result in multiples of the risk it took once it finished, where the price stood against the buy as a ratio
// of it, the session it finished on or the close it stood at, and the bar its plan set.
public sealed record PickCell(
    string Ticker,
    string? Company,
    DateOnly Night,
    string Plan,
    decimal? Buy,
    decimal? Stop,
    decimal? Target,
    string Status,
    int? Sessions,
    double? Result,
    double? Along,
    DateOnly? EndedOn,
    double? ReturnPct,
    DateOnly? NowOn,
    decimal? NowClose,
    double? BreakEven,
    // The state the member's reported quarters gave it on its listing night, and null on a night that
    // stored no readings, which the row says it was not read on.
    string? State = null,
    // Where the listing repeated a trade still open, the night that trade was listed on, and null where
    // the listing is itself the kept trade. A repeat is drawn with its mark and counted in no total.
    // see: A repeat listing made before the rule reached the filter is marked and counted once
    DateOnly? RepeatOf = null,
    // The setup family the page listed the trade under, by the word the store keeps and the label a row
    // draws, and whether the family trails its stop and names no target.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    string Family = SetupFamilies.Pullback,
    string Setup = "Pullback",
    bool Trailing = false,
    // Whether the setup that listed the trade runs on provisional settings, no freeze having registered it:
    // the trade is drawn and counted as listed, and is in no share and no average until the freeze.
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    bool Provisional = false,
    // The buy above is the trade's fill at the next session's open: the plan's own buy at its close, whether that open was
    // past its stop or its target, why it holds no fill, and whether it still waits on its next session, which the row says
    // it buys at.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    decimal? Planned = null,
    string? Past = null,
    string? Unfilled = null,
    bool Waiting = false,
    DateOnly? FilledOn = null,
    // The sessions the trade is given, which paper trading draws an open trade's sessions held against.
    int? Cap = null,
    // A trade whose exit fired and whose sale the next open has not made is open: the status it fired as and when; and once
    // sold, the session and the price of its sale.
    // see: Every count of trades names its group and one function works the four groups
    string? FiredAs = null,
    DateOnly? FiredOn = null,
    DateOnly? SoldOn = null,
    decimal? Sale = null)
{
    // A live rule's trade under a plan buying at a price, its buy and its stop the plan's.
    // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
    public bool UnderPlan { get; init; }

    public string? FiredWords => FiredAs switch
    {
        null => null,
        PickStates.Target => PaperAccount.TargetReached,
        PickStates.Time => PaperAccount.TimeRanOut,
        _ => Trailing ? PaperAccount.UnderTrail : PaperAccount.UnderStop,
    };
}

// What became of a trade, by the one value each is stored and filtered by; the words each is drawn in are the page's.
public static class PickStates
{
    public const string Open = "open";
    public const string Target = "target";
    public const string Stopped = "stopped";
    public const string Time = "time";
    public const string Missing = "missing";

    // A stock listed and not yet bought, which no group of trades counts until its next session's open fills it.
    // see: Every count of trades names its group and one function works the four groups
    public const string ToBuy = "to-buy";

    // A live rule's order under a plan buying at a price: waiting for its price, or ended at its window's last session
    // unfilled, which no group, rate, account or record counts.
    // see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are cards of their own nearest their buy price first
    public const string Waiting = "waiting";
    public const string NotFilled = "not-filled";
}
