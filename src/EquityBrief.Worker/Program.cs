using Microsoft.Data.Sqlite;
using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Data.Migrations;
using EquityBrief.Worker;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Facts;
using EquityBrief.Worker.Fundamentals;
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
    "drain" => await Drain(),
    "register" => await Register(args),
    "version" => await VersionWindows(args),
    _ => NoVerb(),
};

static int NoVerb()
{
    Console.Error.WriteLine(
        "EquityBrief.Worker: no verb given. Seven are built: 'migrate' applies pending migrations, " +
        "'nightly --fixture <folder>' runs the night's steps in order, " +
        "'fundamentals --ticker <TICKER>' fetches one name's quarters and balance sheet, " +
        "'research --ticker <TICKER>' writes the sections of one name's research that are not written or have gone " +
        "stale, with '--refresh' to write every section again and '--paid-for-local' to have the paid model write the " +
        "local lane's sections as well, " +
        "'drain' works through the reports a screen asked for, oldest first, running the research verb for each, and " +
        "'register --candidate <name> --rule <rule> --test <test> --evaluator <evaluator> --parameters <name=value,...>' " +
        "registers a candidate condition before anything scores it, with '--retire <name> --evidence <figures>' " +
        "writing the new row that withdraws one, and " +
        "'version --rule <rule> --live-window' opens a ladder rule's live window, '--version <name> --parameters <name=value,...>' " +
        "opens a version beside it, '--replace <name> --with <name> --parameters <name=value,...> --evidence <text>' closes one and " +
        "opens the version replacing it, '--close <name> --evidence <text>' closes one, '--backfill <yyyy-MM-dd>' scores a past " +
        "night the store computed under the windows open now, and '--list' names the open windows. '--live' " +
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
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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
// see: A ladder rule's version is measured beside that rule's live window, and both count against the bound
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
        feeds = OnDemandFeeds.Resolve(
            wantsLive ? FeedSource.Live
                : wantsFixture ? FeedSource.Fixture
                : configuration[FeedSource.SourceKey],
            Argument(args, "--fixture") ?? configuration[FeedSource.FixtureKey],
            configuration[EodhdBulkPriceFeed.BaseAddressKey],
            configuration[ProviderCredentials.ApiKeyName],
            configuration[ArchiveAgent.ContactName],
            configuration[TavilySearchFeed.ApiKeyName],
            LocalLane.Settings(configuration),
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
// request count is zero and its model call count is zero, and a pass is both. The name
// page's control starts this verb for the name it is on, and the operator can run it by
// hand for the same result.
// see: Everything expensive happens when a name is opened
// see: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
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

    try
    {
        local = LocalLane.Settings(configuration);
        research = ResearchLane.Settings(configuration);
        lane = LocalLane.Sections(configuration);
        caps = ResearchLane.Caps(configuration);
        lists = SourceLists.Read(Path.Combine(AppContext.BaseDirectory, SourceLists.FileName));
        feeds = OnDemandFeeds.Resolve(
            wantsLive ? FeedSource.Live
                : wantsFixture ? FeedSource.Fixture
                : configuration[FeedSource.SourceKey],
            Argument(args, "--fixture") ?? configuration[FeedSource.FixtureKey],
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

    var clock = SystemClock.ForUnitedStatesSessions();
    var runId = PassRun.IdFor(clock.UtcNow, ticker);
    var database = store.DatabaseFile;

    // The name's fundamentals first, where the store holds none, because the archive is
    // addressed by the identifier that fetch stores and the pass reads the company's own
    // release from there. Held fundamentals are left as they are.
    var fundamentals = await new FundamentalsFetcher(feeds.Fundamentals, clock, database, feeds.Archive).RunAsync(ticker, null, runId);

    // Where that fetch stored a filing the night had not seen, the night's facts file is
    // assembled again and its changes read again before the pass, so the sections are
    // written from the quarter just fetched rather than from a file that carries none of
    // it. A re-run replaces only the files that now differ, which is this name's.
    // see: A re-run replaces a night's facts file where the store now computes a different one
    // see: A name's facts file is assembled again for its night when an open fetches its fundamentals
    if (fundamentals.RowsWritten > 0)
    {
        await new FactsAssembler(clock, database).RunAsync(runId);
        await new ChangeDetector(clock, database).RunAsync(runId);
    }

    var cap = new SpendCap(feeds.ResearchModel, caps, clock, database);
    var checker = new ClaimChecker(clock, database);

    var outcome = await new ResearchRunner(
        new StalenessJudge(clock, database),
        sections => new ProseWriter(feeds.LocalModel, local, sections, clock, database),
        cap,
        checker,
        new ThemeResearchRunner(cap, checker, feeds.Search, lists.Industry, research.Pricing, clock, database),
        feeds.Archive,
        feeds.NameNews,
        lane,
        clock,
        database).RunAsync(ticker, runId, new ResearchPassRequest(args.Contains("--refresh"), args.Contains("--paid-for-local")));

    Console.WriteLine(
        FormattableString.Invariant($"research: {ticker} {outcome.Outcome}, {outcome.Written.Count} section(s) written, ")
        + FormattableString.Invariant($"{outcome.NotWritten.Count} not written, {outcome.Fetched} document(s) fetched and {outcome.Admitted} admitted")
        + (outcome.Reason is { } reason ? ": " + reason : string.Empty));

    return outcome.Outcome == ResearchRunner.AlreadyRunning ? 1 : 0;
}

// The queue, worked through oldest first. Each request is the research verb over that
// name, so one code path writes a pass whether a person asked for it at a shell or a
// press on a screen put it here.
// see: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
static async Task<int> Drain()
{
    var configuration = Configuration();
    var store = new StoreLocation(configuration[StoreLocation.DataRootKey] ?? string.Empty);
    var clock = SystemClock.ForUnitedStatesSessions();

    var taken = 0;
    var written = 0;

    while (true)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));

        await connection.OpenAsync();

        var request = await RequestDrain.ClaimAsync(connection, clock.UtcNow);

        if (request is null)
        {
            break;
        }

        taken++;

        // The request carries the lane the press meant, so a queue drained a day later
        // writes under it rather than under whatever configuration now says.
        string[] pass = request.Lane == "paid"
            ? ["research", "--ticker", request.Ticker, "--paid-for-local"]
            : ["research", "--ticker", request.Ticker];

        await ResearchPass(pass);

        // What this request's own pass came to, and not whether the verb exited zero. A
        // verb that exits zero has run, and a pass that ran is not a pass that wrote: a
        // pass the model could not be reached for exits zero and writes nothing, and one
        // refused before the runner starts writes no run for this request at all.
        var (runId, outcome) = await RequestDrain.PassAsync(connection, request);
        var (state, reason) = RequestDrain.SettlementFor(outcome);

        if (reason is null)
        {
            written++;
        }

        await RequestDrain.SettleAsync(connection, request, state, reason, runId, clock.UtcNow);
    }

    Console.WriteLine(FormattableString.Invariant(
        $"drain: {taken} request(s) taken, {written} written and {taken - written} refused"));

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
    var runId = RunId(named) ?? FormattableString.Invariant($"night-{clock.UtcNow:yyyyMMddTHHmmssZ}");

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
    NightQueue queue;

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
            configuration[ProviderCredentials.ApiKeyName]);

        // The overnight queue's local model, from the night's own source, with the lane, the limit
        // and the hold. Resolved here with the feeds rather than at the queue's own step, so a lane
        // naming a section nobody can write or a limit that is not a number refuses the
        // night before its first step rather than after its arithmetic.
        queue = NightQueue.From(configuration, source, fixture, new MachineAwake());
    }
    catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
    {
        return await RefusedAsync(store, runId, clock, refusal.Message);
    }

    return await Nightly.RunAsync(store, feeds, queue, index, clock, Console.Out, Console.Error, runId);
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

static IConfiguration Configuration() =>
    new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: false)
        .AddJsonFile("appsettings.Secrets.json", optional: true)
        .AddEnvironmentVariables()
        .Build();

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
