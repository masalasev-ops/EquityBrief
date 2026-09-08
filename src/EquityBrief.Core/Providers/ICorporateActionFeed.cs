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
public sealed record CorporateAction(string Ticker, DateOnly Date, ActionKind Kind, string Value);

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
