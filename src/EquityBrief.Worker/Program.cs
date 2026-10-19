using Microsoft.Data.Sqlite;
using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Research;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Fundamentals;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Local;
using EquityBrief.Worker.News;
using EquityBrief.Worker.Nights;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Rules;
using Microsoft.Extensions.Configuration;

// The nightly run and the overnight queue. Scheduling lives outside the
// application, so this is one command an external scheduler invokes rather than
// a service that schedules itself.
// see: Nothing is written against one operating system

return (args.Length > 0 ? args[0] : string.Empty) switch
{
    "migrate" => Migrate(),
    "nightly" => await NightlyRun(args),
    "fundamentals" => await FundamentalsFetch(args),
    "research" => await ResearchPass(args),
    "drain" => await Drain(args),
    "register" => await Register(args),
    "version" => await VersionWindows(args),
    "filter-counts" => await FilterCountsReport(args),
    "shape" => await Shape(args),
    "filter-history" => await FilterHistoryRun(args),
    "history-pull" => await HistoryPullRun(args),
    "quarters" => await QuartersRun(args),
    "members" => await MembersRun(args),
    "index-families" => await IndexFamiliesRun(args),
    "measure-sources" => await MeasureSources(args),
    "sweep" => await SweepRun(args),
    "sweep-family" => await SweepFamilyRun(args),
    "sweep-index" => await SweepIndexRun(args),
    "sweep-answer" => await SweepAnswerRun(args),
    "rule-record" => await RuleRecordRun(args),
    "ledger-build" => await LedgerBuildRun(args),
    "ledger-check" => await LedgerCheckRun(args),
    "ledger-next-open" => await LedgerNextOpenRun(args),
    "ledger-night" => await LedgerNightRun(),
    "fill" => await FillRun(args),
    "filings" => await FilingsRun(args),
    "insiders-history" => await InsidersHistoryRun(args),
    "insiders" => await InsidersRun(args),
    "sec-tickers" => await SecTickersRun(args),
    "loop-test" => await LoopTestRun(args),
    "loop-rules" => await LoopRulesRun(args),
    "loop-links" => await LoopLinksRun(args),
    "loop-apply" => await LoopApplyRun(),
    "loop-month" => await LoopMonthRun(args),
    "loop-ideas" => await LoopIdeasRun(args),
    "sweep-fundamentals" => await SweepFundamentalsRun(args),
    "sweep-ideas" => await SweepIdeasRun(),
    "sweep-family-ideas" => await SweepFamilyIdeasRun(args),
    "sweep-context" => await SweepContextRun(),
    "sweep-wider" => await SweepWiderRun(),
    "label-news" => await LabelNews(args),
    "news-fill" => await NewsFill(args),
    "backup" => await BackupRun(args),
    "quote" => await QuoteRun(args),
    "dividends" => await DividendsRun(args),
    "local-run" => await LocalRunVerb(args),
    "releases" => await ReleasesVerb(args),
    "compare-labels" => await CompareLabelsVerb(),
    "compare-reviews" => await CompareReviewsVerb(),
    _ => NoVerb(),
};

static int NoVerb()
{
    Console.Error.WriteLine(
        "EquityBrief.Worker: no verb given. 49 are built:'migrate' applies pending migrations, " +
        "'nightly --fixture <folder>' runs the night's steps in order, with '--resume' running the rest of the newest " +
        "night from the first step its tries have not finished, " +
        "'fundamentals --ticker <TICKER>' fetches one name's quarters and balance sheet, " +
        "'research --ticker <TICKER>' writes the sections of one name's research that are not written or have gone " +
        "stale, with '--refresh' to fetch its figures again and write every section again, once a day, and '--paid-for-local' to have the paid model write the " +
        "local lane's sections as well, " +
        "'drain' works through the reports a screen asked for, oldest first, running the research verb for each, with " +
        "'--now <TICKER>' writing that one request alone ahead of the queue and '--at-peak' writing it inside a peak window too, and " +
        "'register --candidate <name> --rule <rule> --test <test> --evaluator <evaluator> --parameters <name=value,...>' " +
        "registers a candidate condition before anything scores it, with '--retire <name> --evidence <figures>' " +
        "writing the new row that withdraws one and '--the-three' writing phase 10's three rows at one instant, and " +
        "'version --rule <rule> --live-window' opens a ladder rule's live window, '--version <name> --parameters <name=value,...>' " +
        "opens a version beside it, '--trend-version <name>' opens one of the trend rule's three versions at the numbers the code " +
        "carries, '--replace <name> --with <name> --parameters <name=value,...> --evidence <text>' closes one and " +
        "opens the version replacing it, '--close <name> --evidence <text>' closes one, '--backfill <yyyy-MM-dd>' scores a past " +
        "night the store computed under the windows open now, and '--list' names the open windows, " +
        "'filter-counts' prints the swing filter's shape counts over the nights the store keeps bands and plans for, " +
        "with '--year' to replay every session of the stored year as well, reading the store and writing nothing, and " +
        "'shape --accept <proposal>' opens a shape proposal's settings as the next filter version, '--settings <name=value,...> " +
        "--evidence <text>' with '--trade ladder' or '--trade swing' opens settings the operator ruled, '--restarts <blocks>' states " +
        "the live filter's blocks an acceptance restarts, and '--reject <proposal> --reason <text>' records a proposal's rejection, and " +
        "'filter-history --from <yyyy-MM-dd> --through <yyyy-MM-dd>' replays the swing filter's results for sessions before its " +
        "first stored night, for the trigger's arrival alone, with '--remove' taking those sessions' replayed results out once no " +
        "night can read them, and " +
        "'history-pull --from <yyyy-MM-dd>' stores the daily bars and earnings prints of every name the index held from that " +
        "date to tonight apart from the store's own, each row marked by its pull, '--surprises' with it stores the earnings " +
        "surprises the calendar files over the span instead, '--market' the index's and the VIX's daily series, " +
        "'--sector-etfs' the eleven sector funds' daily series, '--companies' each name's filer, GICS sector and quarterly " +
        "share counts with the days they were filed, '--splits' each name's splits, 'history-pull --revenue' each pulled " +
        "company's revenue as its filer filed it, 'history-pull --members --index <MID or SML>' the S&P 400's or 600's " +
        "members today, survivors alone, which '--index' then gives every other pull its names from, and " +
        "'--purge <pull>' removes a pull whole, and " +
        "'quarters' runs the night's quarters step by hand, asking for the members due and the next of the fill, " +
        "'quarters --companies' asks instead every member no fetch has stored a company for, storing its filer, GICS " +
        "sector and share counts beside its quarters, 'quarters --refetch-unread' asks instead every member whose newest " +
        "fetch read no interest expense, 'quarters --refetch-unfiled' every member whose newest company row carries no " +
        "filer, and 'quarters --refetch-lines' every member whose newest fetch read none of the " +
        "gross profit and the cash flow lines, each stating its asks and their weighted calls before the first request, '--most <n>' " +
        "caps the asks a run makes, and '--index <MID or SML>' asks a wider index's members, " +
        "'members' runs the night's membership and backfill steps by hand, the S&P 400's and 600's members read " +
        "from their funds' files beside the index's and each member holding no bar asked for its year, " +
        "'index-families' runs the night's index families step by hand over the newest session the store holds, the S&P " +
        "400's and 600's provisional rules read by the sweep's own code into their tables and asking for nothing, with " +
        "'--leavers' instead selling again each holding the books sold on leaving the index at its stock's stored close on " +
        "the session it was sold, its round trip from that close, " +
        "'history-pull --index-funds' pulls SPY's, IJH's, IJR's and HYG's daily series, and " +
        "'measure-sources --sector <sector> --sites <a,b> --industries <x,y>' searches each proposed site for each declined " +
        "industry as a theme pass does and says which would join the sector's sites, writing a report and nothing to the store. '--live' " +
        "'sweep' replays the swing filter over the stored history across its designs and settings, reading the store and " +
        "writing nothing to it, and writes its report in a run folder of its own beside it, '--run <name>' going on with a " +
        "run started before from its last saved chunk, " +
        "'sweep-family --family <name>' replays a setup family's rule over the stored history across its settings, the " +
        "sector heavyweights' book among them over the companies, share counts, splits and funds the history pull stored, " +
        "reading the store and writing nothing to it, and writes its report in a run folder of its own, " +
        "'sweep-family-ideas --family <name>' adds each of the pullback's ideas that fits the breakout or the earnings " +
        "drift to its rule as frozen, one at a time over the stored history and the market series, reading the store " +
        "and writing nothing to it, and writes its report in a run folder of its own, " +
        "'sweep-context' adds the earnings drift's revenue growth, as a filter and as an order, and the pullback's order " +
        "by its RSI's fall to their rules as frozen, one at a time over the stored history and each filer's revenue as " +
        "filed, reading the store and writing nothing to it, and writes its report in a run folder of its own, " +
        "'sweep-wider' replays each swing family at its frozen settings on the S&P 1500, today's 400 and 600 members " +
        "beside the S&P 500's history, against the 500 alone, every 1,500 figure saying it holds survivors only, reading " +
        "the store and writing nothing to it, and writes its report in a run folder of its own, " +
        "'sweep-index --index <MID or SML> --family <name>' sweeps a setup family on the S&P 400 or 600 alone, its " +
        "strength, market check and benchmark read on that index and every result after costs, on membership as it " +
        "stood or with '--survivors' on survivors only, reading the store and writing nothing to it, and writes its " +
        "report and figures in a run folder of its own, " +
        "'sweep-answer --run <name>' records the answer a sweep run states, whether a setting it read met the floors, " +
        "which a family's card reads to say its sweep found none, " +
        "'rule-record' replays each rule a pick's card names on each index at its one setting over the pulled history, " +
        "each trade after its cost, and stores its record for the card, with '--index <GSPC, MID or SML>' one index alone, " +
        "and 'rule-record --nights' replays instead each standing rule's listed count on every session of the pulled " +
        "history, the live rules and the breakout's and the drift's variants at their grid settings, for the cards' " +
        "stretch lines, " +
        "'ledger-build --index <GSPC, MID or SML> --from <yyyy-MM-dd> --through <yyyy-MM-dd>' writes the setup ledger's " +
        "history for one index over the span, every member-session a family's loose gates pass over the pulled bars " +
        "merged with the store's, each replayed to its end, a chunk of sessions at a time, going on from the sessions " +
        "not yet written, waiting for the night and holding the drain's lock while it writes, with '--again' writing " +
        "the span again, " +
        "'ledger-check --index <GSPC, MID or SML>' rebuilds a seeded sample of each year's history setups from the " +
        "history read through the build's own end and cut at each one's session, a swing setup's own readings by its " +
        "family's gates, and names every reading that differs from the one stored, " +
        "'ledger-next-open --index <GSPC, MID or SML>' prices each settled setup at the close of the night that listed " +
        "it and at the next session's open with the same stop, target, trail and cap, and states the differences by " +
        "family and year with how often the next open fell under the stop or over the target, writing nothing, " +
        "'ledger-night' runs the setup ledger's night step over the newest night the store holds, as the night does, " +
        "writing that night's setups and every member's readings again and stating its time, refused while a night holds " +
        "the store, " +
        "'fill' prices every stored trade whose next session is stored at that session's open, as the night's step " +
        "does, with '--again' pricing every one again over what an earlier run wrote, " +
        "'filings' reads the archive's daily index for the days since the refresh last read one and asks the facts of " +
        "the members of the three indices that filed a report, an amendment or a results announcement, as the night's " +
        "step does, and 'filings --whole' asks every filer the store knows for its facts once, each storing every fact " +
        "not yet stored as first filed, " +
        "'insiders-history' reads each quarter of the SEC's insider data sets from 2018q1 through the newest posted that " +
        "no earlier read recorded, one request a quarter and one for the page listing them, and stores every company's " +
        "Forms 4 and 4/A, with '--from <quarter>' and '--through <quarter>' as 2026q3 bounding the quarters and " +
        "'--again' reading every quarter again and storing only the filings not stored, and " +
        "'insiders --from <yyyy-MM-dd>' reads the members' Form 4 and 4/A filings of each weekday's index through " +
        "'--through <yyyy-MM-dd>' or the newest day the refresh has read, as the night's refresh does, with '--compare' " +
        "setting each filing the store holds already against its stored rows and writing nothing, " +
        "'sec-tickers' reads the SEC's own file of every listed ticker and its filer, one request, and keeps it, which " +
        "the quarters fetch reads for a member the provider files no CIK for, " +
        "'loop-test --index <MID or SML>' runs the walk-forward tester over Part 0's procedures, each run inside every " +
        "fold on the years before its test year and scored on that year against the index's current rule, and stores " +
        "each proposal's verdict and test years, with '--month <yyyy-MM>' naming the month the run is for and " +
        "'--print' printing each verdict and writing nothing, " +
        "'loop-rules --index <GSPC, MID or SML>' walks every family rule standing on the index at its own settings over " +
        "the test years and stores each variant's verdict against its live or provisional rule and each of those against " +
        "its benchmark, a rule the history cannot walk stored with why, with '--month <yyyy-MM>' and '--print' as " +
        "'loop-test' reads them, " +
        "'loop-links --index <GSPC, MID or SML>' works which readings go with winners over each family's settled setups " +
        "on the index and stores every reading's row, with '--month <yyyy-MM>' and '--print' as 'loop-test' reads them, " +
        "'loop-apply' applies each change the operator approved on the Loop page that no apply has answered, as the " +
        "night does before the S&P 400's and 600's families, an S&P 400 or 600 swing family's written as the setting it " +
        "stands at from the next night and every other refused with why, " +
        "'loop-ideas --index <GSPC, MID or SML>' puts to the local model the prompts each family's tables make in each of " +
        "the tester's folds on the index, each one its pin has answered already asked nothing again, and stores each " +
        "answer or the cause it was refused for, which the tester's next run reads, " +
        "'loop-month' runs the month's steps in order, the SEC's facts asked whole, the history written again beside the " +
        "standing one and kept where a family would lose its benchmarks, each index's point-in-time check, " +
        "which readings go with winners on each index, the idea generator on each index, " +
        "each index's tester run, each index's per-rule test and the month's report naming the one proposal a family an " +
        "index puts to you, with " +
        "'--month <yyyy-MM>' naming the month, and run again goes on from the first step no try of the month held, as " +
        "tools/monthly runs it from a clean copy of main's commit, " +
        "'sweep-fundamentals --index <GSPC, MID or SML>' searches the fundamentals-first family's 27 registered settings " +
        "over the pullback's listings on the index's history, each walked after its round trip, and prints each setting's " +
        "trades, edge and years against the family floors with what luck passes, writing nothing, " +
        "'sweep-ideas' adds each new idea to the base, today's rule with its reward-to-risk floor at 2, one at a time " +
        "over the stored history and the market series, reading the store and writing nothing to it, and writes its " +
        "report in a run folder of its own, " +
        "'label-news' labels the stored articles of the names on the newest night's list through the news job's paid model, " +
        "as the night starts it after the close, with '--session <yyyy-MM-dd>' naming the night, and " +
        "'news-fill --days <n>' stores the articles of the last n days from the news feed's dated query, one a day, " +
        "for the names the index holds, and " +
        "'backup' copies the store into the copies' folder once no night or drain holds it, opens and reads the copy " +
        "and keeps the newest three, as the night starts it after the labeller with '--after-labeller' waiting for " +
        "the labeller too, and " +
        "'quote --ticker <TICKER> --asked <yyyy-MM-ddTHH:mm:ssZ>' asks the provider once for the name's delayed quote " +
        "inside the regular session and under the day's cap and stores it with each band's distance at its price, as a " +
        "name page's press starts it, and " +
        "'dividends --history --from <yyyy-MM-dd>' asks each member the index holds today for its dividends from that date, " +
        "one request a member, stating its asks and their weighted calls before the first request, and keeps each beside " +
        "the ones the nights kept, with '--index <GSPC, MID or SML>' naming the index and '--names' with codes between " +
        "commas asking only those members, and " +
        "'local-run --slot early' or '--slot late' runs the local run's slot, fetching from the SEC the members' results " +
        "releases accepted since the slot before and checking the newest reading pin before " +
        "anything is read and asking nothing past the slot's last instant, '--pin' writes a new reading version from the " +
        "model's file hashed whole, the runtime a probe's answer states and the instruction's version, and '--twice' " +
        "reads every results release a research pass stored twice under the pin and states how many were answered " +
        "alike, each loading the local model where it is not loaded and leaving the runtime as it found it, and " +
        "'compare-labels' labels again on the local model, under the labeller's own instruction, every article the news " +
        "job's paid profile labelled that the store still holds, and writes the agreement on each label's kind and " +
        "direction and how many of its reasons held no digit to a file under the data root, writing nothing to the store, " +
        "'compare-reviews' asks the local model again every review the run log holds, over the pass's version, its night's " +
        "facts file and the documents it cites, and writes how many outcomes agreed and how many of the drafts' sentences " +
        "both reviews kept or both changed to a file under the data root, writing nothing to the store, and " +
        "'releases --history --from <yyyy-MM-dd>' fetches every ledger filer's results releases from the one before its " +
        "first session in the ledger from that day through its last, with '--until <HH:mm>' stopping cleanly at that " +
        "time in New York, and goes on from where an earlier run stopped. '--live' " +
        "fetches from the provider instead of from a capture, and '--session <yyyy-MM-dd>' runs the " +
        "night for a session the operator names rather than the one the clock falls on.");

    return 1;
}

// A candidate condition registered before anything scores it, and a retirement.
//
// A verb rather than a step, because a registration is a decision a person takes
// and never something a night arrives at: a register that filled itself would be
// the thing pre-registration exists to stop. Nothing evaluates a registered
// candidate at 8.3; the shadow column at 8.4 is what runs them. The verb's work is in
// `RegisterVerb`, so a test runs the verb a person runs rather than a copy of it.
// see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
static async Task<int> Register(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await RegisterVerb.RunAsync(
        args,
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        Console.Out,
        Console.Error);
}

// A ladder rule's window opened, closed or listed.
//
// A verb rather than a step, for the reason registration is one: a version is a
// decision a person takes, and a night that opened or closed its own windows
// would be changing what it measures while it measures it. The verb's work is
// in `VersionVerb`, so a test runs the verb a person runs rather than a copy of it.
// see: A ladder rule's version is measured beside that rule's live window, and both count against a bound of eighteen
static async Task<int> VersionWindows(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await VersionVerb.RunAsync(
        args,
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        Console.Out,
        Console.Error);
}

// A shape proposal rejected, or ruled settings opened as a filter version. A proposal's moves are
// approved on the Loop page and opened by the night, and the verb accepts none.
//
// A verb rather than a step, for the reason a registration is one: a filter that moved its own
// thresholds would be tuning itself toward whatever it last saw. The verb's work is in
// `ShapeCommand`, so a test runs the verb a person runs rather than a copy of it.
// see: The shape clock proposes moves tested like any proposal, and an approved move opens the next filter version restarting the live filter's record
static async Task<int> Shape(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await ShapeCommand.RunAsync(
        args,
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        Console.Out,
        Console.Error);
}

// The swing filter's results replayed for sessions before its first stored night, and removed, by hand and
// never from the night. The verb's work is in `FilterHistory`, so a test runs the verb a person runs.
// see: The swing filter's results are replayed for the sessions before its first stored night for the trigger's arrival alone, and removed once no night can read them
static async Task<int> FilterHistoryRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await FilterHistory.RunAsync(
        args,
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        Console.Out,
        Console.Error);
}

// History before the store's rolling year, pulled by hand and never from the night, and removed whole
// by the pull that wrote it. The verb's work is in `HistoryPull`, so a test runs the verb a person runs.
// see: The history pulled before the store's year sits apart from its bars under the pull that wrote it, read for the name and chart pages' figures alone and removed whole by that pull
static async Task<int> HistoryPullRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var source = args.Contains("--live") ? NightFeeds.LiveSource
        : Argument(args, "--fixture") is not null ? NightFeeds.FixtureSource
        : configuration[NightFeeds.SourceKey];
    var fixture = Argument(args, "--fixture") ?? configuration[NightFeeds.FixtureKey];
    var address = configuration[EodhdBulkPriceFeed.BaseAddressKey];

    return await EquityBrief.Worker.Bars.HistoryPull.RunAsync(
        args,
        () => NightFeeds.Resolve(source, fixture, address, configuration[ProviderCredentials.ApiKeyName]),
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        Console.Out,
        Console.Error,
        () => FeedSource.Resolve<IMarketSeriesFeed>(
            source,
            fixture,
            RecordedMarketSeriesFeed.FromFolder,
            () => EodhdMarketSeriesFeed.Live(
                string.IsNullOrWhiteSpace(address) ? EodhdBulkPriceFeed.DefaultBaseAddress : address,
                new ProviderCredentials(configuration[ProviderCredentials.ApiKeyName] ?? string.Empty)),
            "a market pull"),
        () => FeedSource.Resolve<ICompanyFeed>(
            source,
            fixture,
            _ => throw new InvalidOperationException(NoCapture("company")),
            () => EodhdFundamentalsFeed.Live(
                string.IsNullOrWhiteSpace(address) ? EodhdBulkPriceFeed.DefaultBaseAddress : address,
                new ProviderCredentials(configuration[ProviderCredentials.ApiKeyName] ?? string.Empty)),
            "a companies pull"),
        () => FeedSource.Resolve<ISplitHistoryFeed>(
            source,
            fixture,
            _ => throw new InvalidOperationException(NoCapture("splits")),
            () => EodhdCorporateActionFeed.Live(
                string.IsNullOrWhiteSpace(address) ? EodhdBulkPriceFeed.DefaultBaseAddress : address,
                new ProviderCredentials(configuration[ProviderCredentials.ApiKeyName] ?? string.Empty)),
            "a splits pull"),
        () => FeedSource.Resolve<IFiledRevenueFeed>(
            source,
            fixture,
            _ => throw new InvalidOperationException(NoCapture("revenue")),
            () => SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(configuration[ArchiveAgent.ContactName] ?? string.Empty)),
            "a revenue pull"),
        () => FeedSource.Resolve<IIndexComponentsFeed>(
            source,
            fixture,
            _ => throw new InvalidOperationException(NoCapture("members")),
            () => EodhdIndexComponentsFeed.Live(
                string.IsNullOrWhiteSpace(address) ? EodhdBulkPriceFeed.DefaultBaseAddress : address,
                new ProviderCredentials(configuration[ProviderCredentials.ApiKeyName] ?? string.Empty)),
            "a members pull"),
        () => FeedSource.Resolve<IFundSnapshotFeed>(
            source,
            fixture,
            _ => throw new InvalidOperationException(NoCapture("holdings")),
            () => SecEdgarFundSnapshotFeed.Live(new ArchiveAgent(configuration[ArchiveAgent.ContactName] ?? string.Empty)),
            "a holdings pull"),
        () => FeedSource.Resolve<ISymbolListFeed>(
            source,
            fixture,
            _ => throw new InvalidOperationException(NoCapture("holdings")),
            () => EodhdSymbolListFeed.Live(
                string.IsNullOrWhiteSpace(address) ? EodhdBulkPriceFeed.DefaultBaseAddress : address,
                new ProviderCredentials(configuration[ProviderCredentials.ApiKeyName] ?? string.Empty)),
            "a holdings pull"),
        () => FeedSource.Resolve<IOpenFigiMappingFeed>(
            source,
            fixture,
            _ => throw new InvalidOperationException(NoCapture("holdings")),
            () => OpenFigiMappingFeed.Live(),
            "a holdings pull"));

    // The companies, splits, revenue, members and holdings pulls ask their sources alone: the fixture holds the answers
    // their readers were written against and no replay of a pull, so a pull over the fixture is refused rather than
    // answered with nothing.
    static string NoCapture(string what) =>
        $"The {what} pull asks the provider live and has no recorded answers to replay. Run it with '--live' or the source set to live.";
}

// A sector's proposed sites measured for its declined industries before any joins the industry list. The search
// is the live tool's; nothing is written to the store, and the report goes under the working folder's artifacts.
// see: A theme search adds its sector's sites, and a site joins the list only where a measurement found industry material on it
static async Task<int> MeasureSources(string[] args)
{
    var configuration = Configuration();
    var sector = Argument(args, "--sector");
    string[] sites = Argument(args, "--sites")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
    string[] industries = Argument(args, "--industries")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];

    if (sector is null || sites.Length == 0 || industries.Length == 0)
    {
        Console.Error.WriteLine("measure-sources: name the sector with '--sector', its proposed sites with '--sites a,b' and the declined industries with '--industries x,y'.");

        return 1;
    }

    ISearchFeed search;

    try
    {
        search = TavilySearchFeed.Live(configuration[TavilySearchFeed.ApiKeyName]);
    }
    catch (InvalidOperationException refusal)
    {
        Console.Error.WriteLine("measure-sources: " + refusal.Message);

        return 1;
    }

    var (file, verdicts) = await SourceMeasurementRun.RunAsync(
        search,
        SystemClock.ForUnitedStatesSessions(),
        sector,
        sites,
        industries,
        Path.Combine(Directory.GetCurrentDirectory(), "artifacts"));

    foreach (var verdict in verdicts)
    {
        Console.WriteLine(FormattableString.Invariant(
            $"measure-sources: {verdict.Site} {(verdict.Joins ? "joins" : "stays out")}, {verdict.About} page(s) about a declined industry of {verdict.Admitted} admitted, {verdict.Stored} stored and {verdict.Results} returned"));
    }

    Console.WriteLine("measure-sources: " + search.Requests.ToString(CultureInfo.InvariantCulture) + " search(es), report " + file);

    return 0;
}

// The quarters step run by hand, outside the night, the fourth carve-out's own asks taken when the
// operator says rather than waiting for the nights. The verb's work is in `QuarterFetcher`, so a test
// runs the verb a person runs.
// see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted
static async Task<int> QuartersRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await EquityBrief.Worker.Quarters.QuarterFetcher.RunAsync(
        args,
        () => NightFeeds.Resolve(
            args.Contains("--live") ? NightFeeds.LiveSource
                : Argument(args, "--fixture") is not null ? NightFeeds.FixtureSource
                : configuration[NightFeeds.SourceKey],
            Argument(args, "--fixture") ?? configuration[NightFeeds.FixtureKey],
            configuration[EodhdBulkPriceFeed.BaseAddressKey],
            configuration[ProviderCredentials.ApiKeyName]),
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        Console.Out,
        Console.Error);
}

// The night's membership and backfill steps run by hand, on the session '--session' names or the clock's, for the
// remedy that loads the S&P 400's and 600's members and their years before their first night. A named session is
// refused by the night's own rule. The verb's work is in `MembersVerb`, so a test runs the verb a person runs.
// see: The S&P 400's and 600's members are read each night from their funds' own holdings files
static async Task<int> MembersRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    IClock clock = SystemClock.ForUnitedStatesSessions();

    if (Argument(args, "--session") is { } named)
    {
        if (!DateOnly.TryParseExact(named, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session))
        {
            Console.Error.WriteLine($"members: '--session {named}' is not a date in yyyy-MM-dd.");

            return 1;
        }

        if (NightSession.Refusal(session, clock.SessionDateAt(clock.UtcNow), NightSession.NewestStored(store.DatabaseFile)) is { } refused)
        {
            Console.Error.WriteLine(refused);

            return 1;
        }

        clock = new ReplayClock(
            new DateTimeOffset(session.ToDateTime(new TimeOnly(21, 10)), TimeSpan.Zero),
            SessionZones.ResolveSessionZone(SessionZones.UnitedStates));
    }

    return await EquityBrief.Worker.Membership.MembersVerb.RunAsync(
        args,
        () => NightFeeds.Resolve(
            args.Contains("--live") ? NightFeeds.LiveSource
                : Argument(args, "--fixture") is not null ? NightFeeds.FixtureSource
                : configuration[NightFeeds.SourceKey],
            Argument(args, "--fixture") ?? configuration[NightFeeds.FixtureKey],
            configuration[EodhdBulkPriceFeed.BaseAddressKey],
            configuration[ProviderCredentials.ApiKeyName]),
        clock,
        store.DatabaseFile,
        Console.Out,
        Console.Error);
}

// The night's index families step by hand, over the newest session the store holds and under a run of its own: the S&P
// 400's and 600's provisional rules read by the sweep's own code into their tables as the night reads them, asking the
// provider for nothing.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
static async Task<int> IndexFamiliesRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    IClock clock = SystemClock.ForUnitedStatesSessions();
    var startedAt = clock.UtcNow;
    var runId = FormattableString.Invariant($"{IndexFamilies.ByHandPrefix}{startedAt:yyyyMMddTHHmmss.fffffffZ}");

    // The holdings the books sold on leaving the index, sold again at their stocks' closes and nothing else read.
    // see: A heavyweight leaving the index is sold at its last session's close as a member
    if (VerbArguments.Has(args, "--leavers"))
    {
        Console.Out.WriteLine("index-families: " + await new IndexFamilies(clock, store.DatabaseFile).SellLeaversAgainAsync(runId));

        return 0;
    }

    // The index rules standing when the run started, as a night reads them.
    var register = await new CandidateRegistrar(clock, store.DatabaseFile).RowsAsync();
    var outcome = await new IndexFamilies(clock, store.DatabaseFile).RunAsync(
        runId,
        default,
        register,
        startedAt,
        (index, token) => new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile).ReadingsTonightAsync(index, token));

    Console.Out.WriteLine(outcome.Session is { } session
        ? FormattableString.Invariant($"index-families: {session:yyyy-MM-dd} read under {runId}: {IndexFamilies.Detail(outcome.Nights)}")
        : "index-families: the store holds no bar, so nothing was read.");

    return 0;
}

// The sweep, by hand and never from the night: one process that reads the store, computes in chunks saved
// beside it, pauses for every night and writes its report. Each run writes to a folder of its own under the
// sweep's folder, named by the instant it started, and '--run <name>' goes on with a run started before. The
// verb's work is in `SweepRunner`, so a test runs the runner the verb runs.
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
// A setup family's sweep, by hand: the family's rule replayed over the stored history across its grid, read-only,
// its report written into a run folder of its own. The work is in `FamilySweepRunner`.
// see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors, or brings the strongest where none does
static async Task<int> SweepFamilyRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await new EquityBrief.Worker.Sweep.FamilySweepRunner(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        store.DataRoot,
        configuration[EquityBrief.Core.Sweep.SweepFolder.Key],
        Console.Out).RunAsync(VerbArguments.Value(args, "--family") ?? string.Empty);
}

// A setup family's sweep on the S&P 400 or 600 alone, by hand: the index's members today, survivors only, at the
// provisional floors and profit gate, every edge after each trade's cost. The work is in `IndexSweepRunner`.
// see: Each index runs every family as rules of its own, ranked and benchmarked on that index's members alone
static async Task<int> SweepIndexRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await new EquityBrief.Worker.Sweep.IndexSweepRunner(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        store.DataRoot,
        configuration[EquityBrief.Core.Sweep.SweepFolder.Key],
        Console.Out).RunAsync(
            VerbArguments.Value(args, "--index") ?? string.Empty,
            VerbArguments.Value(args, "--family") ?? string.Empty,
            stopFloor: double.TryParse(VerbArguments.Value(args, "--stop-floor"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var floor) ? floor : 0,
            survivorsOnly: VerbArguments.Has(args, "--survivors"));
}

// The answer a sweep run states, recorded by hand after the run, which a card reads to say the family's sweep found no
// setting that passed the floors. The work is in `SweepAnswers`.
// see: No family on any index is set aside or hidden by a test result without the operator's word
static async Task<int> SweepAnswerRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await new EquityBrief.Worker.Sweep.SweepAnswers(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        EquityBrief.Core.Sweep.SweepFolder.Resolve(configuration[EquityBrief.Core.Sweep.SweepFolder.Key], store.DataRoot),
        Console.Out).RecordAsync(VerbArguments.Value(args, "--run") ?? string.Empty);
}

// The record behind each pick's card, by hand: every rule a card names on each index replayed at its one setting over the
// pulled history, each trade after its cost. The work is in `RuleRecorder`.
// see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
static async Task<int> RuleRecordRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var named = VerbArguments.Value(args, "--index");
    string[] indices = named is null ? [] : named.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    var recorder = new EquityBrief.Worker.Cards.RuleRecorder(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        store.DataRoot,
        Console.Out);

    return args.Contains("--nights", StringComparer.Ordinal)
        ? await recorder.NightsAsync(indices)
        : await recorder.RunAsync(indices);
}

// The setup ledger's history, by hand: one index over a span of sessions, each setup the loose gates pass over the
// pulled bars merged with the store's, replayed to its end where the history reaches it, written a chunk at a time.
// The work is in `SetupLedger`.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
static async Task<int> LedgerBuildRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var index = VerbArguments.Value(args, "--index");
    var from = VerbArguments.Value(args, "--from");
    var through = VerbArguments.Value(args, "--through");

    if (index is null || from is null || through is null
        || !DateOnly.TryParseExact(from, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var fromDay)
        || !DateOnly.TryParseExact(through, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var throughDay))
    {
        Console.Error.WriteLine("ledger-build: give '--index <GSPC, MID or SML> --from <yyyy-MM-dd> --through <yyyy-MM-dd>'.");

        return 1;
    }

    return await new EquityBrief.Worker.Ledger.SetupLedger(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile)
        .BuildAsync(store.DataRoot, index, fromDay, throughDay, Console.Out, again: args.Contains("--again", StringComparer.Ordinal));
}

// The ledger's point-in-time check by hand, over one index's history setups, reading the store and writing its run log
// row alone.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
static async Task<int> LedgerCheckRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (VerbArguments.Value(args, "--index") is not { } index)
    {
        Console.Error.WriteLine("ledger-check: give '--index <GSPC, MID or SML>'.");

        return 1;
    }

    return await new EquityBrief.Worker.Ledger.SetupLedger(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile).CheckAsync(index, Console.Out);
}

// The fill step by hand, over every stored trade, writing the fills and its run log row alone; the remedy runs it with
// '--again'.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
static async Task<int> FillRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var clock = SystemClock.ForUnitedStatesSessions();
    var runId = FormattableString.Invariant($"{EquityBrief.Worker.Fills.TradeFiller.Stage}-{clock.UtcNow:yyyyMMddTHHmmssZ}");
    var outcome = await new EquityBrief.Worker.Fills.TradeFiller(clock, store.DatabaseFile).RunAsync(runId, args.Contains("--again", StringComparer.Ordinal));

    foreach (var source in outcome.Sources)
    {
        Console.WriteLine(FormattableString.Invariant($"fill: {source.Source}: {source.Filled} priced at the next open, {source.NoNextSession} with no next session, {source.NoOpen} with no open on it, {source.NoRisk} with no risk at its close, {source.Waiting} waiting, {source.Standing} standing"));
    }

    Console.WriteLine("fill: " + EquityBrief.Worker.Fills.TradeFiller.Detail(outcome));

    return 0;
}

// The setup ledger's night step by hand over the newest night the store holds, as the night runs it after the families:
// that night's setups and every member's readings written again on each index, a night run again replacing its own
// rows, and the windows closed, its run log row carrying its time; refused while a night holds the store.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
static async Task<int> LedgerNightRun()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var clock = SystemClock.ForUnitedStatesSessions();

    if (EquityBrief.Core.Configuration.NightLock.Holder(store.DataRoot) is { } holder)
    {
        Console.Error.WriteLine($"ledger-night: the night holds the store ({holder}); run it once the night has finished.");

        return 2;
    }

    var watch = System.Diagnostics.Stopwatch.StartNew();
    var outcome = await new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile)
        .NightAsync(FormattableString.Invariant($"ledger-night-{clock.UtcNow:yyyyMMddTHHmmssZ}"));

    Console.WriteLine(FormattableString.Invariant($"ledger-night: {(outcome.Session is { } night ? night.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) : "no night")}, {EquityBrief.Worker.Ledger.SetupLedger.Detail(outcome.Indices)}, in {watch.Elapsed.TotalSeconds:0} s"));

    return outcome.Indices.Any(index => index.Fault is not null) ? 1 : 0;
}

// The ledger's next-open check by hand, over one index's settled setups, reading the store and writing nothing.
// see: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone
static async Task<int> LedgerNextOpenRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (VerbArguments.Value(args, "--index") is not { } index)
    {
        Console.Error.WriteLine("ledger-next-open: give '--index <GSPC, MID or SML>'.");

        return 1;
    }

    return await new EquityBrief.Worker.Ledger.SetupLedger(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile).NextOpenAsync(index, Console.Out);
}

// The walk-forward tester by hand on the S&P 400 or 600, Part 0's procedures run inside every fold, reading the pulled
// history and writing its own three tables.
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
static async Task<int> LoopTestRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (VerbArguments.Value(args, "--index") is not { } index)
    {
        Console.Error.WriteLine("loop-test: give '--index <MID or SML>', with '--month <yyyy-MM>' to name the month the run is for.");

        return 1;
    }

    return await new EquityBrief.Worker.Loop.WalkForwardTester(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile, store.DataRoot, Console.Out)
        .RunAsync(index, VerbArguments.Value(args, "--month"), print: args.Contains("--print", StringComparer.Ordinal));
}

// The monthly per-rule test by hand on one index, every family rule standing on it walked at its own settings over the
// test years and judged, writing its own two tables.
// see: Every registered rule is tested each month on the test years, each variant against its live rule and each live rule against its benchmark
static async Task<int> LoopRulesRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (VerbArguments.Value(args, "--index") is not { } index)
    {
        Console.Error.WriteLine("loop-rules: give '--index <GSPC, MID or SML>', with '--month <yyyy-MM>' to name the month the run is for.");

        return 1;
    }

    return await new EquityBrief.Worker.Loop.WalkForwardTester(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile, store.DataRoot, Console.Out)
        .RulesAsync(index, VerbArguments.Value(args, "--month"), print: args.Contains("--print", StringComparer.Ordinal));
}

// Which readings go with winners by hand on one index, every family's settled setups read from the ledger and every
// reading's row worked over them, writing its own table.
// see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
static async Task<int> LoopLinksRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (VerbArguments.Value(args, "--index") is not { } index)
    {
        Console.Error.WriteLine("loop-links: give '--index <GSPC, MID or SML>', with '--month <yyyy-MM>' to name the month the run is for.");

        return 1;
    }

    return await new EquityBrief.Worker.Loop.WalkForwardTester(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile, store.DataRoot, Console.Out)
        .LinksAsync(index, VerbArguments.Value(args, "--month"), print: args.Contains("--print", StringComparer.Ordinal));
}

// The apply step by hand, as the night runs it before the S&P 400's and 600's families: every change the operator
// approved on the Loop page that no apply has answered, applied or refused with why, under the configured adopt setting.
// see: An approved change is applied by the night's own build on the index it was approved on alone
static async Task<int> LoopApplyRun()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var clock = SystemClock.ForUnitedStatesSessions();
    var outcome = await new EquityBrief.Worker.Loop.LoopApply(clock, store.DatabaseFile, configuration[EquityBrief.Core.Loop.LoopDecisions.AdoptSetting])
        .RunAsync(FormattableString.Invariant($"{EquityBrief.Worker.Loop.LoopApply.Verb}-by-hand-{clock.UtcNow:yyyyMMddTHHmmssZ}"));

    foreach (var one in outcome.Applications)
    {
        Console.Out.WriteLine($"{EquityBrief.Worker.Loop.LoopApply.Verb}: {one.Index} {one.Family}, {one.Proposal}, {(one.Applied ? "applied" : "refused")}: {one.Words}");
    }

    Console.Out.WriteLine(FormattableString.Invariant($"{EquityBrief.Worker.Loop.LoopApply.Verb}: {outcome.Applied} applied and {outcome.Refused} refused"));

    return 0;
}

// The monthly run, by hand or from tools/monthly on the first Saturday of the month: the SEC's facts asked whole, the
// indices' history written again beside the standing one, each index's point-in-time check, which readings go with
// winners on each index, the idea generator on each index, each index's tester run and per-rule test, and the month's
// report, a step at a time, going on from the first step no try of the month held. The history's, the readings', the
// generator's and the testers' steps take the drain's lock themselves as they write.
// see: The monthly run refreshes the SEC's facts and writes each index's history again before its point-in-time check, and puts at most one proposal a family an index to the operator
static async Task<int> LoopMonthRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var clock = SystemClock.ForUnitedStatesSessions();
    var month = VerbArguments.Value(args, "--month") ?? EquityBrief.Worker.Loop.MonthlyRun.MonthOf(clock);
    var contact = configuration[ArchiveAgent.ContactName];
    EquityBrief.Worker.Loop.MonthlyRun? run = null;
    var steps = new List<EquityBrief.Worker.Loop.MonthlyStep>();

    steps.Add(new("facts", async (runId, cancellation) =>
    {
        if (string.IsNullOrWhiteSpace(contact))
        {
            return (false, $"no archive contact: set '{ArchiveAgent.ContactName}' in appsettings.Secrets.json, since the archive refuses a request that names none");
        }

        // The SEC's ticker file first, so a member whose ticker changed is matched to its filer at its next quarters fetch.
        var archive = SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(contact));
        var tickers = await new EquityBrief.Worker.Ledger.SecTickers(clock, store.DatabaseFile).ReadAsync(archive, Console.Out, runId, cancellation);

        if (tickers.Refusal is { } refused)
        {
            return (false, refused);
        }

        var whole = await new EquityBrief.Worker.Ledger.FilingsRefresher(archive, clock, store.DatabaseFile)
            .WholeAsync(runId, Console.Out, cancellation);

        return whole.Refusal is null
            ? (true, FormattableString.Invariant($"{whole.FactsAsked} filer(s) asked and {whole.Facts} fact(s) stored in {whole.Seconds:0} s"))
            : (false, "the archive refused: " + whole.Refusal);
    }));

    steps.Add(new(
        "history",
        (_, cancellation) => new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile).RebuildAsync(store.DataRoot, EquityBrief.Worker.Loop.MonthlyRun.Indices, Console.Out, cancellation: cancellation),
        TakesTheLock: true));

    foreach (var index in EquityBrief.Worker.Loop.MonthlyRun.Indices)
    {
        steps.Add(new("check-" + index, async (_, cancellation) =>
            await new EquityBrief.Worker.Ledger.SetupLedger(clock, store.DatabaseFile).CheckAsync(index, Console.Out, cancellation: cancellation) == 0
                ? (true, "the point-in-time check named no difference")
                : (false, "the point-in-time check named a difference, so the tester does not read the ledger this month")));
    }

    foreach (var index in EquityBrief.Worker.Loop.MonthlyRun.Indices)
    {
        steps.Add(new(
            "links-" + index,
            async (_, cancellation) =>
                await new EquityBrief.Worker.Loop.WalkForwardTester(clock, store.DatabaseFile, store.DataRoot, Console.Out).LinksAsync(index, month, cancellation) == 0
                    ? (true, $"which readings go with winners was worked for {month}")
                    : (false, "which readings go with winners was not worked"),
            TakesTheLock: true));
    }

    // The idea generator on each index before its tester runs. A runtime not running, a model not held or a pin that
    // does not match is the step's words and stops no step, the tester then reading the answers stored before.
    // see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
    foreach (var index in EquityBrief.Worker.Loop.MonthlyRun.Indices)
    {
        steps.Add(new(
            "ideas-" + index,
            async (_, cancellation) =>
            {
                var asks = await new EquityBrief.Worker.Loop.WalkForwardTester(clock, store.DatabaseFile, store.DataRoot, Console.Out).IdeaAsksAsync(index, cancellation);

                if (asks is null)
                {
                    return (false, "the tester's read of the index did not finish, so no prompt was put");
                }

                LocalModelSettings settings;

                try
                {
                    settings = LocalLane.Settings(configuration);
                }
                catch (InvalidOperationException refused)
                {
                    return (true, "no prompt was put, the tester reading the answers stored before: " + refused.Message);
                }

                var outcome = await new IdeaGenerator(LmStudioReadingFeed.Live(settings), settings, clock, store.DatabaseFile, store.DataRoot)
                    .RunAsync(index, asks, Console.Out, cancellation);

                return (true, IdeaGenerator.Words(outcome, asks.Count));
            },
            TakesTheLock: true));
    }

    foreach (var index in EquityBrief.Worker.Loop.MonthlyRun.Indices)
    {
        steps.Add(new(
            "test-" + index,
            async (_, cancellation) =>
                await new EquityBrief.Worker.Loop.WalkForwardTester(clock, store.DatabaseFile, store.DataRoot, Console.Out).RunAsync(index, month, cancellation) == 0
                    ? (true, $"the tester ran for {month}")
                    : (false, "the tester did not finish its run"),
            TakesTheLock: true));
    }

    foreach (var index in EquityBrief.Worker.Loop.MonthlyRun.Indices)
    {
        steps.Add(new(
            "rules-" + index,
            async (_, cancellation) =>
                await new EquityBrief.Worker.Loop.WalkForwardTester(clock, store.DatabaseFile, store.DataRoot, Console.Out).RulesAsync(index, month, cancellation) == 0
                    ? (true, $"the per-rule test ran for {month}")
                    : (false, "the per-rule test did not finish its run"),
            TakesTheLock: true));
    }

    steps.Add(new("report", (_, cancellation) => run!.ReportAsync(month, cancellation)));

    run = new EquityBrief.Worker.Loop.MonthlyRun(clock, store.DatabaseFile, store.DataRoot, Console.Out, steps);

    return (await run.RunAsync(month)).Stopped is null ? 0 : 1;
}

// The idea generator by hand on one index: the prompts the tester's own read makes put to the local model, each one its
// pin has answered already asked nothing again, and each answer stored for the tester's next run.
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
static async Task<int> LoopIdeasRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    IClock clock = SystemClock.ForUnitedStatesSessions();

    if (VerbArguments.Value(args, "--index") is not { } index)
    {
        Console.Error.WriteLine("loop-ideas: give '--index <GSPC, MID or SML>'.");

        return 1;
    }

    LocalModelSettings settings;

    try
    {
        settings = LocalLane.Settings(configuration);
    }
    catch (InvalidOperationException refused)
    {
        Console.Error.WriteLine("loop-ideas: " + refused.Message);

        return 1;
    }

    var asks = await new EquityBrief.Worker.Loop.WalkForwardTester(clock, store.DatabaseFile, store.DataRoot, Console.Out).IdeaAsksAsync(index);

    if (asks is null)
    {
        return 1;
    }

    var outcome = await new IdeaGenerator(LmStudioReadingFeed.Live(settings), settings, clock, store.DatabaseFile, store.DataRoot)
        .RunAsync(index, asks, Console.Out);

    return outcome.Outcome is LocalRun.Finished or LocalRun.NothingToRead ? 0 : 1;
}

// The fundamentals-first family's search by hand on one index, its 27 settings registered before it ran, printed and
// nothing written.
// see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
static async Task<int> SweepFundamentalsRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (VerbArguments.Value(args, "--index") is not { } index)
    {
        Console.Error.WriteLine("sweep-fundamentals: give '--index <GSPC, MID or SML>'.");

        return 1;
    }

    return await EquityBrief.Worker.Loop.FundamentalsSearch.RunAsync(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile, store.DataRoot, index, Console.Out);
}

// The filings refresh by hand: the night's step over the three indices' members, or with '--whole' every filer the
// store knows asked once, over the archive through the configured contact. A run without a contact is refused before
// any request, as the archive refuses a request that names none.
// see: The night refreshes the facts and the insiders' trades of the members that filed since its last read of the archive's daily index, after the close under its own limit
static async Task<int> FilingsRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var contact = configuration[ArchiveAgent.ContactName];

    if (string.IsNullOrWhiteSpace(contact))
    {
        Console.Error.WriteLine($"filings: no archive contact. Set '{ArchiveAgent.ContactName}' in appsettings.Secrets.json, since the archive refuses a request that names none.");

        return 1;
    }

    var clock = SystemClock.ForUnitedStatesSessions();
    var refresher = new EquityBrief.Worker.Ledger.FilingsRefresher(SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(contact)), clock, store.DatabaseFile);
    var runId = FormattableString.Invariant($"filings-by-hand-{clock.UtcNow:yyyyMMddTHHmmss.fffffffZ}");

    if (args.Contains("--whole", StringComparer.Ordinal))
    {
        var whole = await refresher.WholeAsync(runId, Console.Out);

        Console.WriteLine(FormattableString.Invariant($"filings: {whole.FactsAsked} filer(s) asked, {whole.Facts} fact(s) stored in {whole.Seconds:0} s")
            + (whole.Refusal is null ? string.Empty : "; stopped on the archive's refusal: " + whole.Refusal));

        return whole.Refusal is null ? 0 : 1;
    }

    var outcome = await refresher.NightAsync(
        EquityBrief.Worker.Indices.IndexFamilies.LargeIndex,
        runId,
        [.. EquityBrief.Worker.Indices.IndexFamilies.Indices.Where(index => index != EquityBrief.Worker.Indices.IndexFamilies.LargeIndex)]);

    Console.WriteLine("filings: " + EquityBrief.Worker.Ledger.FilingsRefresher.Detail(outcome));

    return outcome.Refusal is null ? 0 : 1;
}

// The SEC's own ticker file by hand, one request, kept under a run of its own for the quarters fetch to read where the
// provider files no CIK; refused while a night holds the store.
// see: A member the provider files no CIK for is matched to its filer by the SEC's own ticker file
static async Task<int> SecTickersRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var contact = configuration[ArchiveAgent.ContactName];

    if (string.IsNullOrWhiteSpace(contact))
    {
        Console.Error.WriteLine($"sec-tickers: no archive contact. Set '{ArchiveAgent.ContactName}' in appsettings.Secrets.json, since the archive refuses a request that names none.");

        return 1;
    }

    if (EquityBrief.Core.Configuration.NightLock.Holder(store.DataRoot) is { } holder)
    {
        Console.Error.WriteLine($"sec-tickers: the night holds the store ({holder}); run it once the night has finished.");

        return 2;
    }

    var outcome = await new EquityBrief.Worker.Ledger.SecTickers(SystemClock.ForUnitedStatesSessions(), store.DatabaseFile)
        .ReadAsync(SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(contact)), Console.Out);

    return outcome.Refusal is null ? 0 : 1;
}

// The insiders' history by hand: each quarter of the SEC's insider data sets the listing page links, from 2018q1 or the
// quarter '--from' names through the newest posted or the one '--through' names, that no earlier read recorded, one
// request a quarter and one for the page, each quarter's Forms 4 and 4/A of every company the store knows stored and the
// quarter recorded; refused while a night holds the store.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
static async Task<int> InsidersHistoryRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var contact = configuration[ArchiveAgent.ContactName];

    if (string.IsNullOrWhiteSpace(contact))
    {
        Console.Error.WriteLine($"insiders-history: no archive contact. Set '{ArchiveAgent.ContactName}' in appsettings.Secrets.json, since the archive refuses a request that names none.");

        return 1;
    }

    if (EquityBrief.Core.Configuration.NightLock.Holder(store.DataRoot) is { } holder)
    {
        Console.Error.WriteLine($"insiders-history: the night holds the store ({holder}); run it once the night has finished.");

        return 2;
    }

    var clock = SystemClock.ForUnitedStatesSessions();
    var outcome = await new EquityBrief.Worker.Insiders.InsiderTrades(clock, store.DatabaseFile).HistoryAsync(
        SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(contact)),
        VerbArguments.Value(args, "--from"),
        VerbArguments.Value(args, "--through"),
        FormattableString.Invariant($"insiders-history-{clock.UtcNow:yyyyMMddTHHmmssZ}"),
        Console.Out,
        again: args.Contains("--again"));

    return outcome.Refusal is null ? 0 : 1;
}

// The insiders' nightly reader by hand over the weekdays from '--from' through '--through', the newest day the filings
// refresh has read where none is named, each day's members' Form 4 and 4/A filings asked and stored and the day recorded,
// at the archive's pace and under no limit; with '--compare', each filing the store holds already read again and set
// against its stored rows, writing nothing; refused while a night holds the store.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
static async Task<int> InsidersRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var contact = configuration[ArchiveAgent.ContactName];

    if (string.IsNullOrWhiteSpace(contact))
    {
        Console.Error.WriteLine($"insiders: no archive contact. Set '{ArchiveAgent.ContactName}' in appsettings.Secrets.json, since the archive refuses a request that names none.");

        return 1;
    }

    if (EquityBrief.Core.Configuration.NightLock.Holder(store.DataRoot) is { } holder)
    {
        Console.Error.WriteLine($"insiders: the night holds the store ({holder}); run it once the night has finished.");

        return 2;
    }

    if (!DateOnly.TryParseExact(VerbArguments.Value(args, "--from") ?? string.Empty, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var from))
    {
        Console.Error.WriteLine("insiders: name the first day with '--from <yyyy-MM-dd>'.");

        return 2;
    }

    DateOnly through;

    if (VerbArguments.Value(args, "--through") is { } named)
    {
        if (!DateOnly.TryParseExact(named, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out through))
        {
            Console.Error.WriteLine($"insiders: '--through' takes a day as yyyy-MM-dd, not '{named}'.");

            return 2;
        }
    }
    else if (await EquityBrief.Worker.Ledger.FilingsRefresher.NewestDayReadAsync(store.DatabaseFile) is { } newest)
    {
        through = newest;
    }
    else
    {
        Console.Error.WriteLine("insiders: the refresh has read no day of the archive's index, so name the last day with '--through <yyyy-MM-dd>'.");

        return 2;
    }

    var clock = SystemClock.ForUnitedStatesSessions();
    var outcome = await new EquityBrief.Worker.Insiders.InsiderTrades(clock, store.DatabaseFile).DaysAsync(
        SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(contact)),
        from,
        through,
        args.Contains("--compare", StringComparer.Ordinal),
        FormattableString.Invariant($"insiders-by-hand-{clock.UtcNow:yyyyMMddTHHmmssZ}"),
        Console.Out);

    return outcome.Refusal is null ? 0 : 1;
}

// The ideas' run on a frozen family, by hand: each of the pullback's ideas that fits the family added to its rule
// as frozen alone, over the stored history, read-only, its report written into a run folder of its own. The work
// is in `FamilyIdeasRunner`.
// see: The frozen families are read with the pullback's ideas one at a time, and nothing they show is frozen or registered
static async Task<int> SweepFamilyIdeasRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await new EquityBrief.Worker.Sweep.FamilyIdeasRunner(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        store.DataRoot,
        configuration[EquityBrief.Core.Sweep.SweepFolder.Key],
        Console.Out).RunAsync(VerbArguments.Value(args, "--family") ?? string.Empty);
}

// The wider universe's first test, by hand: each swing family at its frozen settings on the S&P 1500, today's 400 and
// 600 members beside the S&P 500's history, against the 500 alone, read-only, its report written into a run folder of
// its own. The work is in `WiderUniverseRunner`.
// see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
static async Task<int> SweepWiderRun()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await new EquityBrief.Worker.Sweep.WiderUniverseRunner(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        store.DataRoot,
        configuration[EquityBrief.Core.Sweep.SweepFolder.Key],
        Console.Out).RunAsync();
}

// The context run, by hand: the drift's revenue growth and the pullback's RSI order, each added alone to its family as
// frozen over the stored history, read-only, its report written into a run folder of its own. The work is in
// `ContextIdeasRunner`.
// see: The context checks are read on the frozen families one at a time, and nothing they show is frozen or registered
static async Task<int> SweepContextRun()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await new EquityBrief.Worker.Sweep.ContextIdeasRunner(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        store.DataRoot,
        configuration[EquityBrief.Core.Sweep.SweepFolder.Key],
        Console.Out).RunAsync();
}

// The ideas' run, by hand: each new idea added to the base one at a time over the stored history, read-only, its
// report written into a run folder of its own. The work is in `SweepIdeasRunner`.
// see: A new idea is added to the base one at a time and kept only where it is better in six of eight years
static async Task<int> SweepIdeasRun()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    return await new EquityBrief.Worker.Sweep.SweepIdeasRunner(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        store.DataRoot,
        configuration[EquityBrief.Core.Sweep.SweepFolder.Key],
        Console.Out).RunAsync();
}

static async Task<int> SweepRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var clock = SystemClock.ForUnitedStatesSessions();
    var root = EquityBrief.Core.Sweep.SweepFolder.Resolve(configuration[EquityBrief.Core.Sweep.SweepFolder.Key], store.DataRoot);
    var run = VerbArguments.Value(args, "--run");

    if (run is not null && (!EquityBrief.Core.Sweep.SweepFolder.IsRunName(run) || !Directory.Exists(Path.Combine(root, run))))
    {
        Console.Error.WriteLine($"sweep: no run named '{run}' is under {root}; the runs are {string.Join(", ", EquityBrief.Core.Sweep.SweepFolder.Runs(root).DefaultIfEmpty("none"))}.");

        return 2;
    }

    var folder = Path.Combine(root, run ?? EquityBrief.Core.Sweep.SweepFolder.RunName(clock.UtcNow));

    Directory.CreateDirectory(folder);
    Console.Out.WriteLine("run " + Path.GetFileName(folder));

    return await new EquityBrief.Worker.Sweep.SweepRunner(
        clock,
        store.DataRoot,
        store.DatabaseFile,
        folder,
        Console.Out).RunAsync();
}

// One name's fundamentals, on demand and never from the night.
//
// A verb of its own rather than a step, because this endpoint is per name and the
// night's per-name request count is zero. What decides whether it fetches is the
// filing date passed to it, which is `--filed` here and the staleness judge's
// first question from 6.5.
// see: Everything expensive happens when a name is opened
static async Task<int> FundamentalsFetch(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var ticker = Argument(args, "--ticker");

    if (string.IsNullOrWhiteSpace(ticker))
    {
        Console.Error.WriteLine(
            "fundamentals: no '--ticker' given. This endpoint is per name and fetching the whole " +
            "index would spend 5,030 weighted calls on names nobody opened.");

        return 1;
    }

    DateOnly? filed = null;
    var named = Argument(args, "--filed");

    if (named is not null)
    {
        if (!DateOnly.TryParseExact(named, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on))
        {
            Console.Error.WriteLine(
                $"fundamentals: '--filed {named}' is not a date in yyyy-MM-dd. A date read against the " +
                "machine's locale would be a different date here and a refusal on the runner.");

            return 1;
        }

        filed = on;
    }

    var wantsLive = args.Contains("--live");
    var wantsFixture = Argument(args, "--fixture") is not null;

    if (wantsLive && wantsFixture)
    {
        Console.Error.WriteLine(
            "fundamentals: '--live' and '--fixture' were both given. A fetch runs against one source, " +
            "and choosing between them here would be this command deciding what was meant.");

        return 1;
    }

    OnDemandFeeds feeds;

    try
    {
        var source = wantsLive ? FeedSource.Live
            : wantsFixture ? FeedSource.Fixture
            : configuration[FeedSource.SourceKey];
        var fixture = Argument(args, "--fixture") ?? configuration[FeedSource.FixtureKey];

        feeds = OnDemandFeeds.Resolve(
            source,
            fixture,
            configuration[EodhdBulkPriceFeed.BaseAddressKey],
            configuration[ProviderCredentials.ApiKeyName],
            configuration[ArchiveAgent.ContactName],
            configuration[TavilySearchFeed.ApiKeyName],
            LocalLane.For(configuration, source, fixture),
            ResearchLane.Settings(configuration));
    }
    catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
    {
        // On stderr and in the scheduler's own history rather than on the run log.
        // A refusal before the source is resolved has no store to write to, which
        // is what `RUNBOOK.md` says of the night's own pre-store refusal.
        Console.Error.WriteLine("fundamentals: " + refusal.Message);

        return 1;
    }

    var clock = SystemClock.ForUnitedStatesSessions();

    var outcome = await new FundamentalsFetcher(feeds.Fundamentals, clock, store.DatabaseFile, feeds.Archive)
        .RunAsync(
            ticker,
            filed,
            FormattableString.Invariant($"fundamentals-{clock.UtcNow:yyyyMMddTHHmmssZ}-{ticker}"));

    // The weighted figure is one provider's. The archive is free, so what it cost
    // is stated as the documents it fetched rather than folded into an allowance it
    // does not draw on.
    Console.WriteLine(
        FundamentalsFetcher.Detail(outcome)
        + FormattableString.Invariant($", {feeds.WeightedCalls} weighted call(s) of {ProviderWeights.DailyAllowance}")
        + ", and the archive is free");

    return 0;
}

// One name's research, on demand: its fundamentals where the store holds none, and then
// one pass.
//
// A verb of its own for the reason the fundamentals fetch has one: the night's per-name
// request count is zero and its model call count is zero, and a pass is both. The queue's
// drain runs this verb for each request it takes, and the operator can run it by hand for
// the same result.
// see: Everything expensive happens when a name is opened
// see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
static async Task<int> ResearchPass(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var ticker = Argument(args, "--ticker");

    if (string.IsNullOrWhiteSpace(ticker))
    {
        Console.Error.WriteLine(
            "research: no '--ticker' given. A pass is for one name, and a pass over every name would spend on " +
            "research nobody opened.");

        return 1;
    }

    var wantsLive = args.Contains("--live");
    var wantsFixture = Argument(args, "--fixture") is not null;

    if (wantsLive && wantsFixture)
    {
        Console.Error.WriteLine(
            "research: '--live' and '--fixture' were both given. A pass runs against one source, and choosing " +
            "between them here would be this command deciding what was meant.");

        return 1;
    }

    OnDemandFeeds feeds;
    LocalModelSettings local;
    ResearchModelSettings research;
    IReadOnlyList<string> lane;
    SpendCaps caps;
    SourceLists lists;

    var clock = SystemClock.ForUnitedStatesSessions();
    var runId = PassRun.IdFor(clock.UtcNow, ticker);

    // The research job's settings first, and a refusal of them written as this pass's own row
    // where the store is there to hold it: a key the secrets file does not hold stops the job
    // with a line the run page draws and the drain settles the request under, and no other
    // profile is asked instead.
    // see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names
    // Each section's profile and the trial's with it, a section or a profile the map names that the settings do not
    // hold refusing the job by name the same way.
    // see: Research names a profile per section as well as per job, and a Claude profile states its thinking
    IReadOnlyDictionary<string, ResearchModelSettings> sectionSettings;
    ResearchTrial? trial;
    ResearchTrial? review;

    try
    {
        research = ResearchLane.Settings(configuration);
        sectionSettings = ResearchLane.Sections(configuration, research);
        trial = ResearchLane.Trial(configuration);
        review = ResearchLane.Review(configuration);
    }
    catch (InvalidOperationException refusal)
    {
        Console.Error.WriteLine("research: " + refusal.Message);

        if (File.Exists(store.DatabaseFile))
        {
            await ResearchRunner.RefusedBySettingsAsync(clock, store.DatabaseFile, ticker, runId, refusal.Message);
        }

        return 1;
    }

    try
    {
        var source = wantsLive ? FeedSource.Live
            : wantsFixture ? FeedSource.Fixture
            : configuration[FeedSource.SourceKey];
        var fixture = Argument(args, "--fixture") ?? configuration[FeedSource.FixtureKey];

        local = LocalLane.For(configuration, source, fixture);
        lane = LocalLane.Sections(configuration);
        caps = ResearchLane.Caps(configuration);
        lists = SourceLists.Read(Path.Combine(AppContext.BaseDirectory, SourceLists.FileName));
        feeds = OnDemandFeeds.Resolve(
            source,
            fixture,
            configuration[EodhdBulkPriceFeed.BaseAddressKey],
            configuration[ProviderCredentials.ApiKeyName],
            configuration[ArchiveAgent.ContactName],
            configuration[TavilySearchFeed.ApiKeyName],
            local,
            research);
    }
    catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException or FileNotFoundException)
    {
        Console.Error.WriteLine("research: " + refusal.Message);

        return 1;
    }

    var database = store.DatabaseFile;

    // The company's figures first, and the night's facts file again where they moved, so the
    // sections are written from them.
    var regenerate = args.Contains("--refresh");

    await PassFigures.FetchAsync(
        new FundamentalsFetcher(feeds.Fundamentals, clock, database, feeds.Archive),
        clock,
        database,
        ticker,
        regenerate,
        runId);

    var cap = new SpendCap(feeds.ResearchModel, caps, clock, database);
    var checker = new ClaimChecker(clock, database);

    // One cap a profile, the job's own for its `Use`, so every paid call of the pass goes through a cap holding the
    // model that writes that section.
    var profileCaps = new Dictionary<string, SpendCap>(StringComparer.Ordinal) { [research.Profile] = cap };

    SpendCap CapFor(ResearchModelSettings settings)
    {
        if (!profileCaps.TryGetValue(settings.Profile, out var held))
        {
            profileCaps[settings.Profile] = held = new SpendCap(feeds.ModelFor(settings), caps, clock, database);
        }

        return held;
    }

    var models = new SectionModels(cap, sectionSettings.ToDictionary(entry => entry.Key, entry => CapFor(entry.Value), StringComparer.Ordinal));

    var outcome = await new ResearchRunner(
        new StalenessJudge(clock, database),
        sections => new ProseWriter(feeds.LocalModel, local, sections, clock, database),
        cap,
        checker,
        new ThemeResearchRunner(models.For(ClaimRules.CycleSection), checker, feeds.Search, lists.Industry, (sectionSettings.GetValueOrDefault(ClaimRules.CycleSection) ?? research).Pricing, clock, database, lists.Sectors),
        feeds.Archive,
        feeds.NameNews,
        lane,
        clock,
        database,
        models).RunAsync(ticker, runId, new ResearchPassRequest(regenerate, args.Contains("--paid-for-local"), args.Contains(RequestDrain.AtPeakFlag)));

    // The trial, after a pass that wrote one of its sections through the paid lane at the first draft.
    // see: A trial asks a second profile for named sections beside a report, and ships naming none
    if (trial is not null && outcome.Written.Any(written => trial.Sections.Contains(written.Section, StringComparer.Ordinal) && !written.Retry))
    {
        foreach (var tried in await new SectionTrial(CapFor(trial.Profile), trial, clock, database).RunAsync(ticker, runId))
        {
            Console.WriteLine(FormattableString.Invariant($"research: trial of {tried.Section} on {trial.Profile.Profile}, {tried.Outcome} over {tried.Rounds} round(s) for ${tried.Cost}"));
        }
    }

    // The review, after the trial and on the same condition, where the settings name a profile for it.
    // see: A review asks a section's model to check its own draft against the section's rules, beside a stated number of reports
    if (review is not null && outcome.Written.Any(written => review.Sections.Contains(written.Section, StringComparer.Ordinal) && !written.Retry))
    {
        foreach (var reviewed in await new SectionTrial(CapFor(review.Profile), review, clock, database, review: true).RunAsync(ticker, runId))
        {
            Console.WriteLine(FormattableString.Invariant($"research: review of {reviewed.Section} on {review.Profile.Profile}, {reviewed.Outcome} over {reviewed.Rounds} round(s) for ${reviewed.Cost}"));
        }
    }

    Console.WriteLine(
        FormattableString.Invariant($"research: {ticker} {outcome.Outcome}, {outcome.Written.Count} section(s) written, ")
        + FormattableString.Invariant($"{outcome.NotWritten.Count} not written, {outcome.Fetched} document(s) fetched and {outcome.Admitted} admitted")
        + (outcome.Reason is { } reason ? ": " + reason : string.Empty));

    return outcome.Outcome == ResearchRunner.AlreadyRunning ? 1 : 0;
}

// The queue, worked through oldest first. Each request is the research verb over that
// name, so one code path writes a pass whether a person asked for it at a shell or a
// press on a screen put it here. A press starts this verb from a copy of the worker's
// build, and a person runs it by hand; either way it waits for the off-peak rate before
// a pass, reading the windows from the prices configuration states. With `--now` and a
// name it writes that one request alone, ahead of the queue, which the Write now press
// starts, and with `--at-peak` beside it writes it inside a peak window too.
// see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
static async Task<int> Drain(string[] args)
{
    var named = Argument(args, RequestDrain.NowFlag)?.ToUpperInvariant();
    var atPeak = args.Contains(RequestDrain.AtPeakFlag);

    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var clock = SystemClock.ForUnitedStatesSessions();
    IReadOnlyList<ResearchPricing> pricings;

    // The windows the research job's profiles state, its own and each its sections name, read
    // without their keys: a key the secrets file does not hold stops each pass on the pass's own
    // row, which the drain settles the request under, rather than stopping the drain where
    // nothing would draw why.
    try
    {
        _ = ResearchLane.Profile(configuration)?.Pricing
            ?? throw new InvalidOperationException(
                $"'{ModelProfiles.Use(ModelProfiles.ResearchJob)}' names no profile with prices, so no pass can be priced.");

        pricings = ResearchLane.Prices(configuration);
    }
    catch (InvalidOperationException refusal)
    {
        Console.Error.WriteLine("drain: " + refusal.Message);

        return 1;
    }

    // One drain at a time over a store, so the order the queue page states is the order the
    // passes run in: a drain started while another runs waits for it to end, and then takes
    // whatever it left.
    var said = false;

    Task Pause()
    {
        if (!said)
        {
            Console.WriteLine("drain: another drain holds the queue, so this one waits for it to end");
            said = true;
        }

        return Task.Delay(TimeSpan.FromSeconds(5));
    }

    var held = await DrainLock.AcquireAsync(store.DataRoot, Pause);

    // A drain waiting out a peak window lets the lock go for the wait and takes it again before it claims, so a
    // Write now press and the store's copy are not held behind a wait of hours; it holds no request while it waits.
    async Task WaitUntil(DateTimeOffset until)
    {
        Console.WriteLine(
            "drain: a peak window is open, so the next pass waits until "
            + until.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

        held.Dispose();

        var wait = until - clock.UtcNow;

        if (wait > TimeSpan.Zero)
        {
            await Task.Delay(wait);
        }

        held = await DrainLock.AcquireAsync(store.DataRoot, Pause);
    }

    string? stopped;

    try
    {
        // An error escaping the put-back or the queue stops the drain on a row of its own, so a page says it stopped.
        // see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
        stopped = await RequestDrain.GuardAsync(store.DatabaseFile, store.DataRoot, clock, async () =>
        {
            // A request a drain left being written when it ended is put back first, which only a drain holding the lock may do.
            var putBack = await RequestDrain.PutBackAsync(store.DatabaseFile);

            if (putBack > 0)
            {
                Console.WriteLine(FormattableString.Invariant($"drain: {putBack} request(s) a drain left being written when it ended put back as outstanding"));
            }

            if (named is not null)
            {
                var (one, wrote) = await RequestDrain.WriteNowAsync(store.DatabaseFile, clock, ResearchPass, named, atPeak, pricings, WaitUntil);

                Console.WriteLine(one == 0
                    ? $"drain: {named} has no request nobody has started, so nothing was written now"
                    : $"drain: {named} written now, {(wrote == 1 ? "written" : "refused")}{(atPeak ? " at the peak price the press confirmed" : string.Empty)}");

                return;
            }

            var (taken, written) = await RequestDrain.DrainAsync(store.DatabaseFile, clock, ResearchPass, pricings, WaitUntil);

            Console.WriteLine(FormattableString.Invariant(
                $"drain: {taken} request(s) taken, {written} written and {taken - written} refused"));
        });
    }
    finally
    {
        held.Dispose();
    }

    if (stopped is not null)
    {
        Console.Error.WriteLine("drain: " + stopped);

        return 1;
    }

    return 0;
}

// The night, invoked by tools/nightly and by nothing else in this repository:
// scheduling lives outside the application.
// see: Nothing is written against one operating system
static async Task<int> NightlyRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var index = Argument(args, "--index") ?? "GSPC";

    // The clock, or a session the operator named.
    //
    // A night is scheduled after the close and takes its session from the
    // instant it runs at. `--session` is for the run RUNBOOK asks for by hand,
    // and for the catch-up night after a machine was off: without it a night run
    // this morning asks the provider for a session the exchange has not traded
    // yet, and every member comes back unaccounted for.
    //
    // It resolves to an instant in the middle of that session's evening, so the
    // same derivation runs as on any other night rather than a second one
    // written for this argument, and elapsed time runs on from there for real.
    //
    // A frozen clock was the first form and it froze the run log with it: every
    // stage started and ended at the same instant, so a replayed night reported
    // as having taken no time and the operational header drew a row of zeroes
    // that reads as a measurement. The session is what has to be fixed here; the
    // duration is what the page is for.
    var named = Argument(args, "--session");
    IClock clock;

    // The rest of the newest night, from the first step its tries have not finished, as one more try under
    // that night's id and on that night's session. A night for the session the clock is in runs on the
    // clock; one for an earlier session runs as a night named for it does, asking for no quarters and no
    // report, since what it would store is today's answer and not that night's.
    // see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file
    NightToResume? rest = null;

    if (args.Contains("--resume"))
    {
        IClock now = SystemClock.ForUnitedStatesSessions();

        if (named is not null)
        {
            Console.Error.WriteLine("nightly: '--resume' and '--session' were both given. The rest of a night runs on that night's own session.");

            return 1;
        }

        rest = await NightResume.NewestAsync(store.DatabaseFile, now);

        if (rest is null)
        {
            Console.Error.WriteLine("nightly: '--resume' found no night on the run log, so there is no rest of one to run.");

            return 1;
        }

        if (rest.Finished)
        {
            Console.Out.WriteLine($"nightly: the night of {rest.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} closed its arithmetic, so there is nothing to run.");

            return 0;
        }

        named = rest.Session == now.SessionDateAt(now.UtcNow) ? null : rest.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    if (named is null)
    {
        clock = SystemClock.ForUnitedStatesSessions();
    }
    else if (DateOnly.TryParseExact(named, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var session))
    {
        // Which named sessions are refused, and why, is `NightSession`'s rule:
        // a session later than the one the machine is in, which is 6.0's repair
        // for a phase 5 sign-off finding, and a session older than the newest the
        // store holds, which 8.0 added from the trace of what the corporate
        // action refetch does on that path. Refused here rather than inside the
        // night, because nothing the night does afterwards can tell a replay of
        // an old session from a replay of a future one.
        IClock today = SystemClock.ForUnitedStatesSessions();

        if (NightSession.Refusal(session, today.SessionDateAt(today.UtcNow), NightSession.NewestStored(store.DatabaseFile)) is { } refused)
        {
            Console.Error.WriteLine(refused);

            return 1;
        }

        clock = new ReplayClock(
            new DateTimeOffset(session.ToDateTime(new TimeOnly(21, 10)), TimeSpan.Zero),
            SessionZones.ResolveSessionZone(SessionZones.UnitedStates));
    }
    else
    {
        Console.Error.WriteLine(
            $"nightly: '--session {named}' is not a date in yyyy-MM-dd. A session read against the " +
            "machine's locale would be a different date here and a refusal on the runner.");

        return 1;
    }

    // Where tonight's feeds come from, taken from configuration and overridable
    // for the run RUNBOOK asks for by hand.
    //
    // Configuration rather than an argument, because a scheduled night's source
    // should not be a property of a shell script. The two flags remain for a
    // by-hand run and giving both is refused: a command that said live and
    // fixture at once has no right answer, and picking one would be this code
    // deciding what the operator meant.
    var wantsLive = args.Contains("--live");
    var wantsFixture = Argument(args, "--fixture") is not null;
    var runId = rest?.FirstTry ?? RunId(named) ?? FormattableString.Invariant($"night-{clock.UtcNow:yyyyMMddTHHmmssZ}");

    if (wantsLive && wantsFixture)
    {
        return await RefusedAsync(
            store,
            runId,
            clock,
            "'--live' and '--fixture' were both given. A night runs against one source, " +
            "and choosing between them here would be this command deciding what was meant.");
    }

    NightFeeds feeds;

    try
    {
        var source = wantsLive ? NightFeeds.LiveSource
            : wantsFixture ? NightFeeds.FixtureSource
            : configuration[NightFeeds.SourceKey];
        var fixture = Argument(args, "--fixture") ?? configuration[NightFeeds.FixtureKey];

        // A fixture night takes no key at all, which is why the source is
        // resolved before anything asks for one. A night over a capture makes no
        // request, so demanding a key for one would stop CI on a machine that
        // has no business holding a key; RUNBOOK's promise is about not reaching
        // the provider anonymously rather than about holding a key to replay.
        feeds = NightFeeds.Resolve(
            source,
            fixture,
            configuration[EodhdBulkPriceFeed.BaseAddressKey],
            configuration[ProviderCredentials.ApiKeyName],
            configuration[ArchiveAgent.ContactName]);
    }
    catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
    {
        return await RefusedAsync(store, runId, clock, refusal.Message);
    }

    // The night's own request starts the worker's drain as a press does, from a copy of the build
    // this night runs from, in the checkout it runs in. A night run again for a session the
    // operator named asks for no report, since its list is not tonight's.
    // see: The six reports a night are taken in turn across the three indices, one at a time in the page's order
    var launcher = new WorkerDrainLauncher(
        Directory.GetCurrentDirectory(),
        AppContext.BaseDirectory,
        store.DataRoot,
        SystemClock.ForUnitedStatesSessions());

    // The commit the script built this run from, which the night records; a run started from a build of the
    // caller's own names none.
    // see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
    var build = Argument(args, NightBuild.BuiltFromArgument) is { Length: > 0 } commit
        ? new Nightly.Build(commit, Argument(args, NightBuild.BuildNoteArgument) ?? $"built from {commit}")
        : null;

    // A night the scheduler starts tries again from a step that stopped; a run of the rest of one tries once.
    // see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own
    return await Nightly.RunAsync(
        store, feeds, index, clock, Console.Out, Console.Error, runId,
        launcher: launcher,
        askForTheFirstName: named is null,
        tries: rest is null ? Nightly.TryPlan.Standard : Nightly.TryPlan.Once,
        tryNumber: rest?.NextTry ?? 1,
        resume: rest is not null,
        build: build,
        cards: EquityBrief.Core.Cards.CardSettings.From(key => configuration[key]),
        forming: EquityBrief.Core.Cards.FormingSettings.From(key => configuration[key]),
        adopt: configuration[EquityBrief.Core.Loop.LoopDecisions.AdoptSetting]);
}

// A night refused before its first step, on stderr and on the run log.
static Task<int> RefusedAsync(StoreLocation store, string runId, IClock clock, string message) =>
    Nightly.RefusedAsync(store, runId, clock, message, Console.Error);

// The run id, which a named session cannot take from its own clock.
//
// A night's id is the instant it ran at, because SCHEMA's grain is one row per
// run per stage and a re-run of the same night is a second run. A clock fixed to
// a session gives the same instant every time, so a second by-hand run for the
// same session collides on the run log's key and fails on its first step. The id
// therefore carries the real instant as well as the session it was for, read
// through the clock abstraction like every other instant in this system.
static string? RunId(string? session) =>
    session is null
        ? null
        : FormattableString.Invariant($"night-{SystemClock.ForUnitedStatesSessions().UtcNow:yyyyMMddTHHmmssZ}-for-{session}");

static string? Argument(string[] args, string name) => VerbArguments.Value(args, name);

// The quote job, which a name page's press starts as a process of its own with the name and the instant the page asked:
// one ask of the provider inside the regular session and under the day's cap, and a run log row whatever it came to.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
static async Task<int> QuoteRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (Argument(args, "--ticker") is not { Length: > 0 } ticker
        || Argument(args, "--asked") is not { } asked
        || !DateTimeOffset.TryParseExact(asked, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var askedAt))
    {
        Console.Error.WriteLine("quote: it takes '--ticker <TICKER> --asked <yyyy-MM-ddTHH:mm:ssZ>', the name and the instant its page asked.");

        return 1;
    }

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"quote: no store at '{store.DatabaseFile}', so there is nowhere to keep a quote.");

        return 1;
    }

    ProviderCredentials credentials;

    try
    {
        credentials = new ProviderCredentials(configuration[ProviderCredentials.ApiKeyName] ?? string.Empty);
    }
    catch (InvalidOperationException refusal)
    {
        Console.Error.WriteLine("quote: " + refusal.Message);

        return 1;
    }

    var outcome = await new EquityBrief.Worker.Quotes.QuoteJob(
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        EodhdQuoteFeed.Live(configuration[EodhdBulkPriceFeed.BaseAddressKey], credentials)).RunAsync(ticker.ToUpperInvariant(), askedAt);

    Console.WriteLine($"quote: {outcome.Outcome}, {outcome.Detail}");

    return outcome.Outcome == EquityBrief.Worker.Quotes.QuoteJob.Quoted ? 0 : 1;
}

static IConfiguration Configuration() => WorkerConfiguration.Build();

// The news labeller, run by the night as a process of its own and by hand: the stored articles of the names on
// the newest night's list, labelled by the news job's profile through the spend cap, every call and every dollar
// on this run's own rows. A refusal at the settings or a model that does not answer stops it before any call,
// written as its own row where the store is there to hold it.
// see: The news labeller is a process of its own the off-peak run starts, and its calls and its spend are its own
// The local run, as tools/localrun starts it at its two slots and by hand: '--slot early' or '--slot late', '--pin' or
// '--twice', one form a run. A profile the settings cannot read refuses before the store is opened.
// see: The local run is a process of its own at two slots outside the night and refuses any model file, runtime or prompt but its pin
static async Task<int> LocalRunVerb(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    IClock clock = SystemClock.ForUnitedStatesSessions();

    var slot = Argument(args, "--slot");
    var pin = args.Contains("--pin");
    var twice = args.Contains("--twice");

    if ((slot is null ? 0 : 1) + (pin ? 1 : 0) + (twice ? 1 : 0) != 1 || (slot is not null && !LocalSlots.All.Contains(slot)))
    {
        Console.Error.WriteLine("local-run: name one form, '--slot early', '--slot late', '--pin' or '--twice'.");

        return 1;
    }

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"local-run: no store at '{store.DatabaseFile}'.");

        return 1;
    }

    LocalModelSettings settings;

    try
    {
        settings = LocalLane.Settings(configuration);
    }
    catch (InvalidOperationException refused)
    {
        Console.Error.WriteLine("local-run: " + refused.Message);

        return 1;
    }

    // A slot fetches its members' results releases from the SEC before it reads, under the archive's contact.
    ReleaseFetch? fetch = null;

    if (slot is not null)
    {
        var contact = configuration[ArchiveAgent.ContactName];

        if (string.IsNullOrWhiteSpace(contact))
        {
            Console.Error.WriteLine($"local-run: no archive contact. Set '{ArchiveAgent.ContactName}' in appsettings.Secrets.json, since the archive refuses a request that names none.");

            return 1;
        }

        fetch = new ReleaseFetch(SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(contact)), clock, store.DatabaseFile, store.DataRoot);
    }

    var run = new LocalRun(LmStudioReadingFeed.Live(settings), settings, clock, store.DatabaseFile, store.DataRoot, fetch: fetch);
    var outcome = slot is { } named ? await run.SlotAsync(named, Console.Out)
        : pin ? await run.PinAsync(Console.Out)
        : await run.TwiceAsync(Console.Out);

    return outcome.Outcome is LocalRun.Finished or LocalRun.NothingToRead or LocalRun.PinUnchanged ? 0 : 1;
}

// The results releases by hand. From 20.2 '--history --from <yyyy-MM-dd>': every ledger filer's releases from the one
// before its first session in the ledger from the day named through its last, those not yet stored, stopping cleanly at
// '--until', a time in New York, and going on from where an earlier run stopped, requests of the SEC alone at its pace
// under its contact. From 20.3 '--names', with '--again', each filer's names from its submissions; '--read --history'
// with '--until', every release no reading under the newest pin holds, oldest first; '--probe <kind>', one probe over
// its sample; '--reference --out <folder>', the reference set's releases written out as the model reads them; and
// '--accuracy', with '--labels <file>', every accuracy figure printed and nothing written.
// see: A member's results release is the EX-99 exhibit of its 8-K carrying item 2.02, stored compressed with the instant the SEC accepted it
// see: A release is read with every name and date in it replaced by a neutral word wherever it stands, and product names kept
static async Task<int> ReleasesVerb(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    IClock clock = SystemClock.ForUnitedStatesSessions();

    var forms = new[] { "--history", "--names", "--probe", "--reference", "--accuracy" }.Count(form => args.Contains(form, StringComparer.Ordinal));
    var reading = args.Contains("--read", StringComparer.Ordinal);

    if (forms != 1 || reading && !args.Contains("--history", StringComparer.Ordinal))
    {
        Console.Error.WriteLine("releases: name one form, '--history --from <yyyy-MM-dd>', '--names', '--read --history', '--probe <kind>', '--reference --out <folder>' or '--accuracy', '--until <HH:mm>' in New York's time stopping a history.");

        return 1;
    }

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"releases: no store at '{store.DatabaseFile}'.");

        return 1;
    }

    if (args.Contains("--accuracy", StringComparer.Ordinal))
    {
        ReferenceSet? reference = null;

        if (Argument(args, "--labels") is { } labels)
        {
            reference = ReleaseLabels.Read(File.ReadAllText(labels));
        }

        Console.WriteLine(await new ReleaseAccuracy(clock, store.DatabaseFile).ReportAsync(reference));

        return 0;
    }

    if (reading || args.Contains("--probe", StringComparer.Ordinal) || args.Contains("--reference", StringComparer.Ordinal))
    {
        LocalModelSettings settings;

        try
        {
            settings = LocalLane.Settings(configuration);
        }
        catch (InvalidOperationException refused)
        {
            Console.Error.WriteLine("releases: " + refused.Message);

            return 1;
        }

        var local = new LocalRun(LmStudioReadingFeed.Live(settings), settings, clock, store.DatabaseFile, store.DataRoot);

        if (args.Contains("--reference", StringComparer.Ordinal))
        {
            if (Argument(args, "--out") is not { } folder || settings.KnowledgeCutoff is not { } cutoff)
            {
                Console.Error.WriteLine("releases: '--reference' names its folder with '--out <folder>' and reads the cutoff the local profile names.");

                return 1;
            }

            var sample = await local.ReferenceAsync(folder, cutoff);

            Console.WriteLine($"releases: {sample.Count} reference release(s) written to {folder}");

            return 0;
        }

        LocalRunOutcome read;

        if (reading)
        {
            if (Until(args, clock) is not (true, var stop))
            {
                return 1;
            }

            read = await local.ReadHistoryAsync(stop, Console.Out);
        }
        else
        {
            read = await local.ProbeAsync(Argument(args, "--probe") ?? string.Empty, Console.Out);
        }

        return read.Outcome is LocalRun.Finished or LocalRun.LimitReached ? 0 : 1;
    }

    var contact = configuration[ArchiveAgent.ContactName];

    if (string.IsNullOrWhiteSpace(contact))
    {
        Console.Error.WriteLine($"releases: no archive contact. Set '{ArchiveAgent.ContactName}' in appsettings.Secrets.json, since the archive refuses a request that names none.");

        return 1;
    }

    var feed = new ReleaseFetch(SecEdgarFilingsArchiveFeed.Live(new ArchiveAgent(contact)), clock, store.DatabaseFile, store.DataRoot);

    if (args.Contains("--names", StringComparer.Ordinal))
    {
        var named = await feed.NamesAsync(args.Contains("--again", StringComparer.Ordinal), FormattableString.Invariant($"release-names-{clock.UtcNow:yyyyMMddTHHmmss.fffffffZ}"), Console.Out);

        return named.Refusal is null ? 0 : 1;
    }

    if (Argument(args, "--from") is not { } fromText
        || !DateOnly.TryParseExact(fromText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
    {
        Console.Error.WriteLine("releases: name '--history --from <yyyy-MM-dd>', with '--until <HH:mm>' in New York's time where the run should stop.");

        return 1;
    }

    if (Until(args, clock) is not (true, var until))
    {
        return 1;
    }

    var runId = FormattableString.Invariant($"release-history-{clock.UtcNow:yyyyMMddTHHmmss.fffffffZ}");
    var outcome = await feed.HistoryAsync(from, until, runId, Console.Out);

    return outcome.Refusal is null ? 0 : 1;
}

// A history's '--until <HH:mm>', the operator's time in New York, as the first instant it comes round, printed with its
// instant in UTC; none where it is not given, and refused where it is no time.
static (bool Read, DateTimeOffset? Until) Until(string[] args, IClock clock)
{
    if (Argument(args, "--until") is not { } untilText)
    {
        return (true, null);
    }

    if (!TimeOnly.TryParseExact(untilText, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
    {
        Console.Error.WriteLine($"releases: '--until {untilText}' is no time written as HH:mm.");

        return (false, null);
    }

    var until = ResultsReleases.UntilNext(local, clock.UtcNow, clock.SessionZone);

    Console.WriteLine(FormattableString.Invariant($"releases: stopping at {local:HH:mm} New York time, {until:yyyy-MM-ddTHH:mm:ssZ}"));

    return (true, until);
}

// From 20.5 the news labels compared by hand: every article the news job's paid profile labelled that the store still
// holds labelled again by the local profile's model under the labeller's own instruction, at the news job's answer
// budget, the agreement written to a file under the data root and nothing to the store. The work is in `LocalComparison`.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
static async Task<int> CompareLabelsVerb()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"compare-labels: no store at '{store.DatabaseFile}', so there are no labels to compare.");

        return 1;
    }

    if (NewsLane.Profile(configuration) is not { } paid)
    {
        Console.Error.WriteLine("compare-labels: the news job names no profile, so there are no paid labels to compare against.");

        return 1;
    }

    var budget = configuration[ModelProfiles.JobField(ModelProfiles.NewsJob, ModelProfiles.AnswerTokensField)];

    if (!int.TryParse(budget, NumberStyles.None, CultureInfo.InvariantCulture, out var answerTokens) || answerTokens <= 0)
    {
        Console.Error.WriteLine($"compare-labels: the news job's answer budget is '{budget}', which is not a whole number of tokens above zero.");

        return 1;
    }

    LocalModelSettings settings;

    try
    {
        settings = LocalLane.Settings(configuration);
    }
    catch (InvalidOperationException refused)
    {
        Console.Error.WriteLine("compare-labels: " + refused.Message);

        return 1;
    }

    var outcome = await new LocalComparison(LmStudioReadingFeed.Live(settings), settings, SystemClock.ForUnitedStatesSessions(), store.DatabaseFile, store.DataRoot)
        .LabelsAsync(paid.Name, answerTokens, Console.Out);

    return outcome.Stopped is null ? 0 : 1;
}

// From 20.7 the reviews compared by hand: every review the run log holds asked again of the local profile's model as the
// review asked it, each round checked in memory, the agreement written to a file under the data root and nothing to the
// store. The work is in `LocalComparison`.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
static async Task<int> CompareReviewsVerb()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"compare-reviews: no store at '{store.DatabaseFile}', so there are no reviews to compare.");

        return 1;
    }

    LocalModelSettings settings;

    try
    {
        settings = LocalLane.Settings(configuration);
    }
    catch (InvalidOperationException refused)
    {
        Console.Error.WriteLine("compare-reviews: " + refused.Message);

        return 1;
    }

    var outcome = await new LocalComparison(LmStudioReadingFeed.Live(settings), settings, SystemClock.ForUnitedStatesSessions(), store.DatabaseFile, store.DataRoot)
        .ReviewsAsync(Console.Out);

    return outcome.Stopped is null ? 0 : 1;
}

static async Task<int> LabelNews(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    IClock clock = SystemClock.ForUnitedStatesSessions();
    var runId = NewsLabeller.RunIdFor(clock.UtcNow);

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"label-news: no store at '{store.DatabaseFile}', so there is no list to label the news of.");

        return 1;
    }

    DateOnly session;

    if (Argument(args, "--session") is { } named)
    {
        if (!DateOnly.TryParseExact(named, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out session))
        {
            Console.Error.WriteLine($"label-news: '--session {named}' is not a date in yyyy-MM-dd.");

            return 1;
        }
    }
    else
    {
        session = NightSession.NewestStored(store.DatabaseFile) ?? clock.SessionDateAt(clock.UtcNow);
    }

    ResearchModelSettings news;
    NewsLimits limits;
    SpendCaps caps;

    try
    {
        news = NewsLane.Settings(configuration);
        limits = NewsLane.Limits(configuration);
        caps = ResearchLane.Caps(configuration);
    }
    catch (InvalidOperationException refusal)
    {
        Console.Error.WriteLine("label-news: " + refusal.Message);
        await NewsLabeller.RefusedAsync(store.DatabaseFile, runId, clock.UtcNow, refusal.Message, session);

        return 1;
    }

    var cap = new SpendCap(OpenAiCompatibleResearchFeed.Live(news), caps, clock, store.DatabaseFile);

    // Asked whether the model answers, which bills nothing: one that is not there stops the run here with its
    // line, and never another profile in its place.
    if (await cap.UnreachableAsync() is { } unreachable)
    {
        var line = $"the news job's model, {news.Profile}, did not answer: {unreachable}";

        Console.Error.WriteLine("label-news: " + line);
        await NewsLabeller.RefusedAsync(store.DatabaseFile, runId, clock.UtcNow, line, session);

        return 1;
    }

    var outcome = await new NewsLabeller(cap, news, limits, clock, store.DatabaseFile).RunAsync(session, runId);

    Console.WriteLine(
        FormattableString.Invariant($"label-news: {runId} over the night of {session:yyyy-MM-dd} on {news.Profile}: {outcome.NamesReached} of {outcome.Names} name(s) reached, ") +
        FormattableString.Invariant($"{outcome.Labelled} labelled, {outcome.UnreadableCount} unreadable, {outcome.RefusedByAdmissibility} refused by admissibility, ") +
        FormattableString.Invariant($"{outcome.AlreadyLabelled} labelled before, {outcome.Cost} this run and {outcome.MonthCost} this month, stopped by {outcome.Stop}"));

    return 0;
}

// The store copied into the copies' folder, as the night starts it after its labeller or by hand: it waits while a
// night or a drain holds the store, and for the labeller where '--after-labeller' says the night started one, then
// copies, opens and reads the copy and keeps the newest three.
// see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
static async Task<int> BackupRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    if (args.Skip(1).FirstOrDefault(flag => flag != EquityBrief.Worker.Backup.StoreBackup.AfterTheLabeller) is { } unknown)
    {
        Console.Error.WriteLine($"backup: '{unknown}' is not a flag this verb takes; it takes '{EquityBrief.Worker.Backup.StoreBackup.AfterTheLabeller}' alone.");

        return 1;
    }

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"backup: no store at '{store.DatabaseFile}', so there is nothing to copy.");

        return 1;
    }

    var outcome = await new EquityBrief.Worker.Backup.StoreBackup(
        SystemClock.ForUnitedStatesSessions(),
        store.DataRoot,
        store.DatabaseFile,
        StoreCopies.Folder(configuration[StoreCopies.FolderKey], store.DataRoot),
        NewsLane.Limits(configuration).TimeLimit,
        labellerPricing: NewsPricing(configuration)).RunAsync(args.Contains(EquityBrief.Worker.Backup.StoreBackup.AfterTheLabeller));

    Console.WriteLine("backup: " + outcome.Detail);

    return outcome.Outcome == EquityBrief.Worker.Backup.StoreBackup.Copied ? 0 : 1;
}

// The news profile's prices, which name its peak windows, read without its key, since the copy waits through the windows
// the labeller waits through and calls no model; none where the settings state no prices the copy can read, and the copy
// then waits for the labeller's limit alone.
// see: The news labeller waits for the end of a peak window rather than stopping at one, and its time limit counts the time it labels
static EquityBrief.Core.Providers.ResearchPricing? NewsPricing(IConfiguration configuration)
{
    try
    {
        return NewsLane.Profile(configuration)?.Pricing;
    }
    catch (InvalidOperationException)
    {
        return null;
    }
}

// The articles of the last days stored at once, one dated query a day, for the names the index holds, run by hand on
// the operator's word so the labeller's window is full from its first night rather than filling over a month.
// see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
static async Task<int> NewsFill(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    IClock clock = SystemClock.ForUnitedStatesSessions();
    var index = Argument(args, "--index") ?? "GSPC";

    if (!int.TryParse(Argument(args, "--days") ?? string.Empty, NumberStyles.None, CultureInfo.InvariantCulture, out var days) || days < 1)
    {
        Console.Error.WriteLine("news-fill: '--days <n>' names how many days back to store, a whole number above zero.");

        return 1;
    }

    if (!File.Exists(store.DatabaseFile))
    {
        Console.Error.WriteLine($"news-fill: no store at '{store.DatabaseFile}'.");

        return 1;
    }

    var wantsLive = args.Contains("--live");
    var wantsFixture = Argument(args, "--fixture") is not null;

    if (wantsLive && wantsFixture)
    {
        Console.Error.WriteLine("news-fill: '--live' and '--fixture' were both given. A fill runs against one source.");

        return 1;
    }

    NightFeeds feeds;

    try
    {
        feeds = NightFeeds.Resolve(
            wantsLive ? NightFeeds.LiveSource : wantsFixture ? NightFeeds.FixtureSource : configuration[NightFeeds.SourceKey],
            Argument(args, "--fixture") ?? configuration[NightFeeds.FixtureKey],
            configuration[EodhdBulkPriceFeed.BaseAddressKey],
            configuration[ProviderCredentials.ApiKeyName]);
    }
    catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
    {
        Console.Error.WriteLine("news-fill: " + refusal.Message);

        return 1;
    }

    var runId = FormattableString.Invariant($"news-fill-{clock.UtcNow:yyyyMMddTHHmmssZ}");
    var counter = new NewsPulseCounter(feeds.News, clock, store.DatabaseFile);
    var today = clock.SessionDateAt(clock.UtcNow);
    var stored = 0;

    for (var back = days; back >= 1; back--)
    {
        var day = today.AddDays(-back);
        var outcome = await counter.FillAsync(index, day, runId);

        stored += outcome.ArticlesStored;
        Console.WriteLine(FormattableString.Invariant($"news-fill: {day:yyyy-MM-dd}, {outcome.Articles} article(s) over {outcome.Requests} page(s), {outcome.ArticlesStored} stored"));
    }

    Console.WriteLine(FormattableString.Invariant($"news-fill: {runId}, {stored} article(s) stored over {days} day(s), {feeds.Requests} request(s)"));

    return 0;
}

// The swing filter's shape counts, printed for the operator to rule the starting settings from. It
// opens the configured store read-only and writes nothing, and progress goes to the error stream so
// the report on the output stream is the counts alone.
// see: The swing filter's starting settings are ruled from shape counts before tonight's list switches to it
static async Task<int> FilterCountsReport(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);

    var counter = new FilterCounts(store.DatabaseFile);
    var counts = await counter.CountAsync(
        Argument(args, "--index") ?? "GSPC",
        args.Contains("--year", StringComparer.Ordinal),
        line => Console.Error.WriteLine(line));

    Console.Write(FilterCounts.Report(counts, counter.Notes));
    Console.Write(FilterCounts.ShapeReport(counter.Shapes, counter.Firings));

    return 0;
}

static int Migrate()
{
    // The configuration files sit beside the assembly; the data root they name
    // is resolved against the working directory. The secrets file is registered
    // before the environment so an environment variable still wins.
    var configuration = Configuration();

    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var outcome = MigrationRunner.Standard().Apply(store.DatabaseFile);

    Console.WriteLine($"store: {store.DatabaseFile}");

    if (outcome.Applied.Count == 0)
    {
        Console.WriteLine($"no pending migrations, schema version {outcome.To}");
    }
    else
    {
        Console.WriteLine(
            $"applied {outcome.Applied.Count} migration(s), {outcome.From} to {outcome.To}: " +
            string.Join(", ", outcome.Applied));
    }

    return 0;
}

// The dividends history run: each member an index holds today asked once for its dividends from a date, on the
// operator's command and never by a night, which fills the years before the nights began keeping them.
// see: Each dividend a member paid is kept from the night's bulk answer and from one history run, and read as the provider restated it on the day it was read
static async Task<int> DividendsRun(string[] args)
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var source = args.Contains("--live") ? NightFeeds.LiveSource
        : Argument(args, "--fixture") is not null ? NightFeeds.FixtureSource
        : configuration[NightFeeds.SourceKey];
    var fixture = Argument(args, "--fixture") ?? configuration[NightFeeds.FixtureKey];
    var address = configuration[EodhdBulkPriceFeed.BaseAddressKey];

    return await EquityBrief.Worker.Dividends.DividendKeeper.RunAsync(
        args,
        () => FeedSource.Resolve<IDividendHistoryFeed>(
            source,
            fixture,
            RecordedDividendHistoryFeed.FromFolder,
            () => EodhdCorporateActionFeed.Live(
                string.IsNullOrWhiteSpace(address) ? EodhdBulkPriceFeed.DefaultBaseAddress : address,
                new ProviderCredentials(configuration[ProviderCredentials.ApiKeyName] ?? string.Empty)),
            "a dividends history run"),
        SystemClock.ForUnitedStatesSessions(),
        store.DatabaseFile,
        Console.Out,
        Console.Error);
}
