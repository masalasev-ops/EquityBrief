using EquityBrief.Core.Providers;

namespace EquityBrief.Worker;

// The feeds a name's own request runs against, resolved before it starts.
//
// The sibling of `NightFeeds`, and a separate record rather than six more members
// on that one, because these are not nightly and the distinction is the rule: the
// night's feeds are the ones whose count the limits table puts a bound on, and a
// record holding both would make a per-name endpoint reachable from the night by
// nothing worse than a typo.
// see: The nightly run is arithmetic only
// see: Everything expensive happens when a name is opened
//
// It resolves the way the night's does, through `FeedSource`, so a mistyped
// fixture path refuses here for the same reason and with the same words.
//
// One member today. The filings archive joins it at 6.2 and the search tool at
// 6.9, and each is a feed with its own `Requests` member for the reason this one
// has: the cost of an open is read off the feeds rather than stated by the caller.
public sealed record OnDemandFeeds(IFundamentalsFeed Fundamentals)
{
    // What the open cost, read off the feeds. A caller that wrote the figure
    // would be recording its own intention.
    public int Requests => Fundamentals.Requests;

    // The same open in the units the provider bills in. The fundamentals endpoint
    // weighs 10, measured at 6.1 against the account's own request counter rather
    // than read from documentation: one request moved it from 1,771 to 1,781.
    // see: The night's cost is counted in weighted calls against the stated daily allowance
    public int WeightedCalls => Fundamentals.Requests * ProviderWeights.Fundamentals;

    public static OnDemandFeeds FromFixture(string folder) =>
        new(RecordedFundamentalsFeed.FromFolder(folder));

    public static OnDemandFeeds Live(string? baseAddress, string? apiKey) =>
        new(EodhdFundamentalsFeed.Live(
            string.IsNullOrWhiteSpace(baseAddress) ? EodhdBulkPriceFeed.DefaultBaseAddress : baseAddress,
            new ProviderCredentials(apiKey ?? string.Empty)));

    public static OnDemandFeeds Resolve(
        string? source,
        string? fixtureFolder,
        string? baseAddress,
        string? apiKey) =>
        FeedSource.Resolve(
            source,
            fixtureFolder,
            FromFixture,
            () => Live(baseAddress, apiKey),
            "an open");

    // Whether this set can reach the network at all, asked of the objects rather
    // than of the setting that produced them. Every recorded double is named, so a
    // feed added live and forgotten here reads as one that cannot, which is the
    // fault 6.1 found in the night's own reader.
    public bool ReachesTheNetwork => Fundamentals is not RecordedFundamentalsFeed;
}
