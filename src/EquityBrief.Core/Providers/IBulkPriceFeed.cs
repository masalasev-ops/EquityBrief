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
    // Every row the exchange returned for the session asked for, unfiltered.
    // Filtering by membership is the fetcher's work and not the feed's, because
    // a feed that filtered would be deciding which names are in the index from
    // something other than the membership store.
    //
    // The session is named rather than left as "the last day".
    //
    // A feed that asked for the last day has nothing to compare the answer
    // against, and a night answered with yesterday's file logs one request and
    // no error. Naming the session is what turns that into a question with a
    // right answer, and the feed can ask it because it knows what it asked for
    // and the payload declares what it is. How many names the answer should
    // hold is a different question and only the fetcher can ask it.
    // see: A feed is unavailable when it does not answer, and wrong when it answers with something else
    Task<IReadOnlyList<BulkBar>> RowsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default);

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

    // The rows the exchange sent that this reader could not read, by ticker,
    // with the reason each gave.
    //
    // Kept apart from the rows that did not trade, because they mean different
    // things: those are sessions that did not happen and these are sessions the
    // reader refused. A row nobody asked for does not stop the night, since the
    // file is the whole exchange and the store holds the index; the fetcher
    // refuses by name if a current member is among them. Before the phase 5
    // sign-off one fund's fractional volume anywhere in the file refused the
    // file for all five hundred names, with a message that named nothing, and
    // two of the first four days fetched at index size carried one.
    //
    // A default of none, so a double that reads nothing it cannot read need not
    // say so.
    IReadOnlyList<UnreadableRow> Unreadable => [];
}

// A bulk row the reader refused, and why.
public sealed record UnreadableRow(string Ticker, string Reason);
