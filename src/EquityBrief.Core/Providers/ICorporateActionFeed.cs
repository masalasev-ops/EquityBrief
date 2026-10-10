namespace EquityBrief.Core.Providers;

// What kind of action a row is. Both change a name's adjusted prices, which is
// the only reason this system cares about either.
public enum ActionKind
{
    Split,
    Dividend,
}

// One corporate action: a name, a date, and what happened.
//
// The value is kept as the provider sent it rather than parsed into a number.
// A split arrives as a ratio in a string, "4.000000/1.000000", and a dividend
// arrives as a decimal in a string; neither is arithmetic this system does, and
// the only question asked of an action is whether it happened. Parsing a value
// nothing reads would be an invitation to compute with it.
public sealed record CorporateAction(string Ticker, DateOnly Date, ActionKind Kind, string Value)
{
    // A dividend's whole row where the action is one and its amount reads as a figure, which the night keeps for the
    // name page's dividend history; none for a split.
    // see: Each dividend a member paid is kept from the night's bulk answer and from one history run, and read as the provider restated it on the day it was read
    public DividendPaid? Paid { get; init; }
}

// A dividend as the provider files it: the day the stock trades without it, the amount a share as the provider restated
// it for splits on the day it was read and as it was paid, the days it was declared, recorded and paid, how often the
// company says it pays and the currency, each none where the answer files none. Money, so decimal.
// see: Each dividend a member paid is kept from the night's bulk answer and from one history run, and read as the provider restated it on the day it was read
public sealed record DividendPaid(
    string Ticker,
    DateOnly ExDate,
    decimal Amount,
    decimal? Unadjusted,
    DateOnly? DeclaredOn,
    DateOnly? RecordOn,
    DateOnly? PaidOn,
    string? Period,
    string? Currency);

// The day's splits and dividends for a whole exchange, in one request each.
//
// Nightly and once, which is the catalogue's own Runs cell for the component
// that reads it. The per-name endpoints exist and are not used on this path:
// they would put one call per name on a night for information the bulk route
// returns in one.
// see: The nightly run is arithmetic only
public interface ICorporateActionFeed
{
    Task<IReadOnlyList<CorporateAction>> ActionsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default);

    int Requests { get; }
}
