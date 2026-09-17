using EquityBrief.Core.Providers;

namespace EquityBrief.Worker;

// The feeds a night runs against, resolved before the night starts.
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
    ICorporateActionFeed Corporate,
    IEarningsCalendarFeed Calendar,
    INewsFeed News)
{
    // What the night cost, read off the feeds rather than stated by the caller.
    // A caller that wrote the figure would be recording its own intention.
    public int Requests =>
        Membership.Requests + Historical.Requests + Bulk.Requests + Corporate.Requests
        + Calendar.Requests + News.Requests;

    // The same night in the units the provider bills in, which is the unit
    // `RUNBOOK.md` states the allowance in. Composed from the roles rather than
    // declared on each feed, because the role decides the endpoint.
    // see: The night's cost is counted in weighted calls against the stated daily allowance
    public int WeightedCalls =>
        (Membership.Requests * ProviderWeights.Fundamentals)
        + (Historical.Requests * ProviderWeights.HistoricalPerTicker)
        + (Bulk.Requests * ProviderWeights.BulkEndOfDay)
        + (Corporate.Requests * ProviderWeights.BulkEndOfDay)
        + (Calendar.Requests * ProviderWeights.EarningsCalendar)
        + (News.Requests * ProviderWeights.News);

    public const string ConstituentsFile = "index-constituents.json";

    public static NightFeeds FromFixture(string folder) =>
        new(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(folder, ConstituentsFile)),
            RecordedHistoricalBarFeed.FromFolder(folder),
            RecordedBulkPriceFeed.FromFolder(folder),
            RecordedCorporateActionFeed.FromFolder(folder),
            RecordedEarningsCalendarFeed.FromFolder(folder),
            RecordedNewsFeed.FromFolder(folder));

    // The live bulk feed, from the two settings, or a refusal naming the one
    // that is missing.
    //
    // Taken as strings rather than as a configuration, so the refusal is a
    // property this can be asked about directly instead of one reachable only
    // by starting a process. The command line still has to be run to prove the
    // operator sees it, because a claim that something is stated is a claim
    // about a surface.
    public static IBulkPriceFeed LiveBulk(string? baseAddress, string? apiKey) =>
        EodhdBulkPriceFeed.Live(Address(baseAddress), Key(apiKey));

    // Every feed live, which is what 2.6 selects between and what a night against
    // the provider runs on.
    public static NightFeeds Live(string? baseAddress, string? apiKey)
    {
        var address = Address(baseAddress);
        var key = Key(apiKey);

        return new(
            EodhdIndexMembershipFeed.Live(address, key),
            EodhdHistoricalBarFeed.Live(address, key),
            EodhdBulkPriceFeed.Live(address, key),
            EodhdCorporateActionFeed.Live(address, key),
            EodhdEarningsCalendarFeed.Live(address, key),
            EodhdNewsFeed.Live(address, key));
    }

    // A blank base address falls back and a blank key does not. The address has
    // a right answer that does not vary by machine; the key has no answer this
    // code could invent, and inventing one sends an anonymous request whose
    // rejection names nothing.
    static string Address(string? baseAddress) =>
        string.IsNullOrWhiteSpace(baseAddress) ? EodhdBulkPriceFeed.DefaultBaseAddress : baseAddress;

    static ProviderCredentials Key(string? apiKey) => new(apiKey ?? string.Empty);

    // The four settings, which are `FeedSource`'s since 6.1 and are named here
    // because the citations use these names. One value with two names is safe
    // where the second is the first; two literals would be two places holding one
    // fact.
    public const string SourceKey = FeedSource.SourceKey;

    public const string FixtureKey = FeedSource.FixtureKey;

    public const string LiveSource = FeedSource.Live;

    public const string FixtureSource = FeedSource.Fixture;

    // Where tonight's feeds come from, decided before the night starts.
    //
    // A setting rather than an argument, which is the whole of this checkpoint.
    // Until now the choice lived in whichever overload the caller happened to
    // call, so a scheduled night's source was a property of a shell script.
    //
    // Both directions refuse and neither falls back, and that is the property
    // rather than a courtesy. A fixture folder that does not exist must not
    // resolve to the provider, because a mistyped path would then spend the
    // allowance and store live bars where a replay was meant. A live source with
    // no key must not resolve to a capture, because a night that quietly
    // replayed yesterday would look exactly like a night that ran.
    public static NightFeeds Resolve(
        string? source,
        string? fixtureFolder,
        string? baseAddress,
        string? apiKey) =>
        FeedSource.Resolve(
            source,
            fixtureFolder,
            FromFixture,
            () => Live(baseAddress, apiKey),
            "a night");

    // Whether a set of feeds can reach the network at all, asked of the objects
    // rather than of the setting that produced them.
    //
    // A fixture night makes no request because the recorded feeds hold no
    // client, and this is what says so: every recorded double is named, so a feed
    // added live and forgotten here reads as one that cannot.
    //
    // The calendar was exactly that until 6.1. It arrived at 4.3 as the sixth
    // member of this record and was left out of this reader and out of its test,
    // both of which said five and meant six, so a set holding a live calendar and
    // five captures answered that it reaches no network. Nothing configurable
    // produces a mixed set, which is why it cost nothing today and why the proof
    // that this reader can fail is now a constructed one rather than an
    // all-or-nothing pair.
    public bool ReachesTheNetwork =>
        Membership is not RecordedIndexMembershipFeed
        || Historical is not RecordedHistoricalBarFeed
        || Bulk is not RecordedBulkPriceFeed
        || Corporate is not RecordedCorporateActionFeed
        || Calendar is not RecordedEarningsCalendarFeed
        || News is not RecordedNewsFeed;
}
