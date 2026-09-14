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
// Three members at first, and each is a feed with its own `Requests` member for the
// reason the first one has: the cost of an open is read off the feeds rather than
// stated by the caller.
//
// The third is the local lane's model, from 6.6, which is where it first exists.
// The overnight queue takes it through this record rather than holding a client of
// its own, so a night's model calls are attributable to the one resolution every
// other call it makes goes through.
// see: The on-demand feeds are resolved in one place, as the nightly feeds are
//
// The fourth is the research model, from 6.7, which the spend cap holds for every
// paid call: nothing takes it from this record but the cap.
// see: Every paid call is made through the spend cap, which holds the research model
//
// The fifth is one name's own news, from 6.8, which a research pass reads. It is here
// and not on the night's record, whose news is one dated query for the whole market.
//
// The sixth is the search tool, from 6.9, which a theme pass reads. It is reached from an
// open alone, and never from the night, for the reason the fifth is.
// see: Theme material comes from a search tool, and per-name material never does
public sealed record OnDemandFeeds(
    IFundamentalsFeed Fundamentals,
    IFilingsArchiveFeed Archive,
    ILocalModelFeed LocalModel,
    IResearchModelFeed ResearchModel,
    INameNewsFeed NameNews,
    ISearchFeed Search)
{
    // What the open cost, read off the feeds. A caller that wrote the figure
    // would be recording its own intention. A probe of the research model is a
    // request as well, and billed by nobody.
    public int Requests => Fundamentals.Requests + Archive.Requests + NameNews.Requests + ResearchModel.Probes + Search.Requests;

    // The same open in the units the provider bills in, which is one provider's
    // units and not both. The fundamentals endpoint weighs 10, measured at 6.1
    // against the account's own request counter rather than read from
    // documentation: one request moved it from 1,771 to 1,781. The filings archive
    // is free and needs no key, so it weighs nothing against that allowance and
    // that is why it is named here as contributing zero rather than left out of the
    // arithmetic: a reader checking this figure against `Requests` should find the
    // difference explained. The search tool is another provider with an allowance
    // of its own, counted in searches, and it contributes zero here for that reason.
    // see: The night's cost is counted in weighted calls against the stated daily allowance
    public int WeightedCalls => Fundamentals.Requests * ProviderWeights.Fundamentals + NameNews.Requests * ProviderWeights.News;

    // The model calls an open made, apart from its requests: a call to the operator's
    // own runtime is not a provider request and is billed by nobody, and the run log
    // carries the two in columns of their own.
    public int ModelCalls => LocalModel.Requests + ResearchModel.Requests;

    public static OnDemandFeeds FromFixture(string folder, ResearchModelSettings research) =>
        new(
            RecordedFundamentalsFeed.FromFolder(folder),
            new RecordedFilingsArchiveFeed(folder),
            new RecordedLocalModelFeed(folder),
            new RecordedResearchModelFeed(folder, research),
            new RecordedNameNewsFeed(folder),
            new RecordedSearchFeed(folder));

    public static OnDemandFeeds Live(string? baseAddress, string? apiKey, string? archiveContact, string? searchKey, LocalModelSettings local, ResearchModelSettings research) =>
        new(
            EodhdFundamentalsFeed.Live(
                string.IsNullOrWhiteSpace(baseAddress) ? EodhdBulkPriceFeed.DefaultBaseAddress : baseAddress,
                new ProviderCredentials(apiKey ?? string.Empty)),
            // The agent refuses a blank contact, which is this provider's whole
            // credential path: the archive answers a request naming no user agent
            // with a refusal.
            // see: The archive declares a contact in its user agent, and a blank one refuses at startup
            SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(archiveContact ?? string.Empty)),
            OpenAiCompatibleModelFeed.Live(local),
            ResearchModelFeeds.Live(research),
            EodhdNameNewsFeed.Live(
                string.IsNullOrWhiteSpace(baseAddress) ? EodhdBulkPriceFeed.DefaultBaseAddress : baseAddress,
                new ProviderCredentials(apiKey ?? string.Empty)),
            TavilySearchFeed.Live(searchKey));

    // The local lane's settings are taken on both paths, so a key configured for that
    // lane refuses a fixture run as it refuses a live one: the refusal is about the
    // configuration, and a configuration that is wrong is wrong whatever it is run
    // against.
    public static OnDemandFeeds Resolve(
        string? source,
        string? fixtureFolder,
        string? baseAddress,
        string? apiKey,
        string? archiveContact,
        string? searchKey,
        LocalModelSettings local,
        ResearchModelSettings research) =>
        FeedSource.Resolve(
            source,
            fixtureFolder,
            folder => FromFixture(folder, research),
            () => Live(baseAddress, apiKey, archiveContact, searchKey, local, research),
            "an open");

    // The local model alone, for the overnight queue. The night reaches it and nothing else
    // this record holds, and resolving the whole record would ask a night for the paid
    // model's key, which a night that calls no paid model has no business needing. The same
    // resolution as the whole record's, refusing in both directions.
    public static ILocalModelFeed LocalModelFor(string? source, string? fixtureFolder, LocalModelSettings local) =>
        FeedSource.Resolve<ILocalModelFeed>(
            source,
            fixtureFolder,
            folder => new RecordedLocalModelFeed(folder),
            () => OpenAiCompatibleModelFeed.Live(local),
            "the overnight queue");

    // Whether this set can reach the network at all, asked of the objects rather
    // than of the setting that produced them. Every recorded double is named, so a
    // feed added live and forgotten here reads as one that cannot, which is the
    // fault 6.1 found in the night's own reader.
    public bool ReachesTheNetwork =>
        Fundamentals is not RecordedFundamentalsFeed
        || Archive is not RecordedFilingsArchiveFeed
        || LocalModel is not RecordedLocalModelFeed
        || ResearchModel is not RecordedResearchModelFeed
        || NameNews is not RecordedNameNewsFeed
        || Search is not RecordedSearchFeed;
}
