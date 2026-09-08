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
}
