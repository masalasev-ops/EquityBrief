namespace EquityBrief.Core.Providers;

// One row of the day's bulk file: which name it belongs to, and the bar.
public sealed record BulkBar(string Ticker, ProviderBar Bar);

// The day's bars for a whole exchange, in one request.
//
// One call for the night, whatever the universe is. That is the rule the whole
// nightly path is shaped around: bars arrive in one bulk file and news in one
// feed request, so a night costs the same for fifty names as for five hundred.
// see: The nightly run is arithmetic only
public interface IBulkPriceFeed
{
    // Every row the exchange returned, unfiltered. Filtering by membership is
    // the fetcher's work and not the feed's, because a feed that filtered would
    // be deciding which names are in the index from something other than the
    // membership store.
    Task<IReadOnlyList<BulkBar>> RowsAsync(string exchange, CancellationToken cancellation = default);

    // How many requests this feed has made. The nightly claim is a number, so
    // the feed counts rather than the caller asserting it did not loop.
    int Requests { get; }

    // The rows the exchange sent that are not sessions, by ticker.
    //
    // The file is the whole exchange rather than the index, so it carries
    // symbols that are listed and did not trade: 62 of 44,362 rows on the first
    // live night. Refusing the file for one of those would let a penny stock
    // stop the night for five hundred names, and 1.4's capture could not have
    // shown it because all seven hand-picked rows traded.
    //
    // Counted rather than skipped quietly, and on the interface rather than on
    // the live feed alone, for the reason `Requests` is: the same skip at scale
    // is a night that stores almost nothing and reports success, and a figure
    // only one implementation answers is one the fixture never exercises.
    IReadOnlyList<string> NotSessions { get; }
}
