namespace EquityBrief.Core.Providers;

// One session's bar for one name, as the provider reports it.
//
// Prices are decimal, which is the code half of the money rule
// (see: Bars are never interpolated). Volume is a count and is long.
//
// Open, High, Low and Close are one price set and it is the adjusted one
// (see: The stored series is adjusted). The provider adjusts only the close, so
// the other three are scaled by the same factor rather than passed through: a
// bar holding three raw prices and one adjusted price is a mixed set, and 1.2
// stored 96 of 756 fixture bars whose close fell outside their own low and high
// because of it.
//
// RawClose is the provider's unadjusted close, kept because it is the input to
// that factor. A store holding only the adjusted set cannot recompute or audit
// its own factor after a later restatement moves it, and the corporate action
// checker's refetch is the thing that moves it.
public sealed record ProviderBar(
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    decimal RawClose,
    long Volume);

// The per-ticker historical endpoint, which the backfill uses and the nightly
// path does not.
//
// One request per name, once, and never again for a name that already holds its
// year. That is the opposite shape from the nightly fetch on purpose: the bulk
// endpoint is priced per request and the historical endpoint per name, so a year
// of past sessions costs 500 calls this way and 25,000 through the bulk one
// (see: Bars come from EODHD, bulk nightly and per ticker for the backfill).
public interface IHistoricalBarFeed
{
    // How many network requests this feed has made, on the interface so the live
    // feed answers the same question the double does and the backfill's cost is
    // measured rather than stated.
    int Requests { get; }

    Task<IReadOnlyList<ProviderBar>> BarsAsync(
        string ticker,
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default);
}
