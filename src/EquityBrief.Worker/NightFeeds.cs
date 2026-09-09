using EquityBrief.Core.Providers;

namespace EquityBrief.Worker;

// The four feeds a night runs against, resolved before the night starts.
//
// Resolution is the caller's and not the run's. Until now `Nightly.RunAsync`
// constructed the doubles inline, which meant there was no choice to make: a
// night ran over a capture because that was the only thing the code could build.
// This is the seam that choice goes through, and 2.6 turns it into a
// configuration rather than an argument.
//
// It holds no client of its own. The live feeds build their own, because
// nightly-cost's exemption is one file per feed and a file earns it by being a
// feed; composition code holding a client would need the carve-out to widen to
// cover something that is not a feed
// (see: The outward-request scan names the files that may hold a client rather than dropping the patterns).
public sealed record NightFeeds(
    IIndexMembershipFeed Membership,
    IHistoricalBarFeed Historical,
    IBulkPriceFeed Bulk,
    ICorporateActionFeed Corporate)
{
    // What the night cost, read off the feeds rather than stated by the caller.
    // A caller that wrote the figure would be recording its own intention.
    public int Requests => Membership.Requests + Historical.Requests + Bulk.Requests + Corporate.Requests;

    public const string ConstituentsFile = "index-constituents.json";

    public static NightFeeds FromFixture(string folder) =>
        new(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(folder, ConstituentsFile)),
            RecordedHistoricalBarFeed.FromFolder(folder),
            RecordedBulkPriceFeed.FromFolder(folder),
            RecordedCorporateActionFeed.FromFolder(folder));

    // The live bulk feed, from the two settings, or a refusal naming the one
    // that is missing.
    //
    // Taken as strings rather than as a configuration, so the refusal is a
    // property this can be asked about directly instead of one reachable only
    // by starting a process. The command line still has to be run to prove the
    // operator sees it, because a claim that something is stated is a claim
    // about a surface.
    //
    // A blank base address falls back and a blank key does not. The address has
    // a right answer that does not vary by machine; the key has no answer this
    // code could invent, and inventing one sends an anonymous request whose
    // rejection names nothing.
    public static IBulkPriceFeed LiveBulk(string? baseAddress, string? apiKey) =>
        EodhdBulkPriceFeed.Live(
            string.IsNullOrWhiteSpace(baseAddress) ? EodhdBulkPriceFeed.DefaultBaseAddress : baseAddress,
            new ProviderCredentials(apiKey ?? string.Empty));
}
