using System.Globalization;
using System.Text.Json;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Research;
using EquityBrief.Core.Rules;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.FileProviders;

// The read surface and the one page it hosts.
//
// Everything served here is stored or drawn. Nothing is computed and nothing is
// fetched: ReadApi selects, MarkRenderer turns what it selected into SVG, and
// SinglePageApp writes the shell. api-isolation asserts from the compiled
// dependency file that no path from here reaches the worker.

var builder = WebApplication.CreateBuilder(args);

// The settings file beside this build, read under every other source as the worker reads
// its own. Started through its launch settings the surface runs in its project directory and
// otherwise wherever it was started, and only the first holds a settings file; the
// environment and the command line still win.
builder.Configuration.Sources.Insert(0, new JsonConfigurationSource
{
    FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory),
    Path = "appsettings.json",
    Optional = true,
});

// The checkout this build sits in, which a relative data root and the phase report are read
// against, so the store and the report are the checkout's wherever the surface was started.
var checkout = Checkout.Of(AppContext.BaseDirectory);

// The worker's own settings in the checkout, beneath everything the surface reads, so the
// queue page states the peak windows the drain waits out from the one file that states them
// rather than from a second copy of them.
if (checkout is not null && Directory.Exists(Path.Combine(checkout, "src", "EquityBrief.Worker")))
{
    builder.Configuration.Sources.Insert(0, new JsonConfigurationSource
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(checkout, "src", "EquityBrief.Worker")),
        Path = "appsettings.json",
        Optional = true,
    });
}

// The comparison command: started with its verb the surface writes the comparison files into the checkout's folder
// for them, from the checkout's store, and exits without listening.
// see: The drafts compared beside a report and the research template's before and after counts are written to files by a command, and drawn on no page
if (args.Length > 0 && args[0] == ComparisonFiles.Verb)
{
    var store = StoreLocation.Within(checkout, builder.Configuration[StoreLocation.DataRootKey]);

    foreach (var line in await ComparisonCommand.WriteAsync(
        new ReadApi(store.DatabaseFile, SystemClock.ForUnitedStatesSessions()),
        new ComparisonFiles(new MarkRenderer()),
        Path.Combine(checkout ?? Directory.GetCurrentDirectory(), ComparisonFiles.Folder)))
    {
        Console.WriteLine("comparisons: " + line);
    }

    return;
}

builder.Services.AddSingleton<IClock>(SystemClock.ForUnitedStatesSessions());
builder.Services.AddSingleton(_ =>
    StoreLocation.Within(checkout, builder.Configuration[StoreLocation.DataRootKey]));
builder.Services.AddSingleton(services => new ReadApi(
    services.GetRequiredService<StoreLocation>().DatabaseFile,
    services.GetRequiredService<IClock>()));
builder.Services.AddSingleton<MarkRenderer>();

// The two spend caps, read once at startup so a cap that is not an amount of money
// refuses the surface rather than a page, and handed to the screens that state them.
builder.Services.AddSingleton(_ => SpendCaps.From(
    builder.Configuration[SpendCaps.DayKey],
    builder.Configuration[SpendCaps.MonthKey]));
builder.Services.AddSingleton<SinglePageApp>();
builder.Services.AddSingleton<ReportExporter>();

// What a press starts once its request is written: the worker's drain, from a copy of the
// worker's build beside the surface's own. A surface running from any build but its own,
// as the suite hosts it, finds no worker beside it and starts nothing.
// The drain and the rest of a night start from the newest night's own build where the night's script left
// one under the data root, so they run the build the night made and never a newer one.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
builder.Services.AddSingleton<IDrainLauncher>(services =>
{
    var dataRoot = services.GetRequiredService<StoreLocation>().DataRoot;

    return new WorkerDrainLauncher(
        checkout,
        WorkerDrainLauncher.WorkerBuildBeside(checkout, AppContext.BaseDirectory),
        dataRoot,
        services.GetRequiredService<IClock>(),
        nightBuild: () => EquityBrief.Core.Configuration.NightBuild.NewestWorkerBuild(dataRoot));
});

var app = builder.Build();

// Every answer is drawn from the store as it stands, so the browser keeps none: a screen drawn again
// after a press, or after a night, reads the store again rather than an earlier answer.
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store";

    await next();
});

// A store behind this checkout is named on every screen rather than failing on the first column
// it lacks, and the run page still draws its run log, which is where a refused night is.
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;

    if (path.StartsWith("/screens/", StringComparison.Ordinal) || path.StartsWith(ReportExporter.Route, StringComparison.Ordinal))
    {
        var read = context.RequestServices.GetRequiredService<ReadApi>();

        if (await read.SchemaBehindAsync() is { } behind)
        {
            var body = context.RequestServices.GetRequiredService<SinglePageApp>().StoreBehind(behind.Store, behind.Checkout);
            var asked = path.StartsWith("/screens/run/", StringComparison.Ordinal)
                && DateOnly.TryParseExact(path["/screens/run/".Length..], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var named)
                    ? named
                    : (DateOnly?)null;

            if (path.StartsWith("/screens/run", StringComparison.Ordinal) && (asked ?? await read.RunNightAsync()) is { } night)
            {
                var marks = context.RequestServices.GetRequiredService<MarkRenderer>();
                var stages = RunScreen.Stages(await read.RunLogAsync(night));

                body += marks.OperationalHeader(night, stages) + marks.FailedStages(RunScreen.Failed(stages));
            }

            await Results.Content(body, "text/html; charset=utf-8").ExecuteAsync(context);

            return;
        }
    }

    await next();
});

app.MapGet("/", (SinglePageApp page) =>
    Results.Content(page.Shell("EquityBrief", Lane(builder.Configuration)), "text/html; charset=utf-8"));

// The mark, drawn on the server and handed over as SVG. The date range defaults
// to the whole stored series, which is a year by the retention limit.
app.MapGet("/marks/level-chart/{ticker}", async (
    string ticker,
    ReadApi read,
    MarkRenderer marks,
    DateOnly? from,
    DateOnly? to) =>
{
    var bars = await read.BarsAsync(
        ticker,
        from ?? DateOnly.MinValue,
        to ?? DateOnly.MaxValue);

    var drawn = bars
        .Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))
        .ToArray();

    // The averages the chart draws, aligned to the bars session by session
    // rather than by position. The two queries are ordered the same way and
    // over the same window, so the sequences agree today; aligning on the date
    // is what keeps them agreeing when one of them does not, and a line drawn
    // one slot out would look entirely plausible.
    var indicators = await read.IndicatorsAsync(
        ticker,
        from ?? DateOnly.MinValue,
        to ?? DateOnly.MaxValue);

    var bySession = indicators
        .GroupBy(row => row.Name)
        .ToDictionary(
            group => group.Key,
            group => group.ToDictionary(row => row.SessionDate, row => row.Value));

    var averages = new[] { "sma20", "sma50", "sma200" }
        .Where(bySession.ContainsKey)
        .Select(name => new ChartAverage(
            name,
            drawn.Select(bar => bySession[name].GetValueOrDefault(bar.SessionDate)).ToArray()))
        .ToArray();

    return Results.Content(marks.LevelChart(ticker, drawn, averages), "image/svg+xml; charset=utf-8");
});

// The name screen's chart region, read here and composed by the app.
//
// The composition is in EquityBrief.Web rather than in this route so the suite
// asserts the shipped path rather than a copy of it. A route that assembled the
// region itself would be a second composer, and the one thing a test could then
// prove is that the test agrees with itself.
app.MapGet("/screens/name/{ticker}", async (string ticker, ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock, StoreLocation store) =>
{
    var name = await NameAsync(read, marks, page, caps, clock, ticker, builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC", export: false, cardContext: await CardContextAsync(read, store));

    // The link to the file, outside the region, so the file does not carry it.
    return Results.Content(ReportExporter.Link(ticker) + name.Region, "text/html; charset=utf-8");
});

// One name as the store held it on an earlier night, section 15.9's second route.
//
// Every read the page makes is bounded by that night, so what it draws is what the night
// computed rather than tonight's figures under an older date, and the evening it draws is the
// newest the listings hold on or before the date asked for, so a Saturday or a night that
// never ran answers with the evening before it. No control is offered and the file is not
// linked: a pass writes about the company now and the file is tonight's report. A date that
// cannot be read is tonight's page with a line saying what was asked for, as an unknown route
// is tonight's list with one.
// see: A name's page for an earlier night draws what the store held that night and nothing it learned after
app.MapGet("/screens/name/{ticker}/{date}", async (string ticker, string date, ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock, StoreLocation store) =>
{
    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";

    if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var asked))
    {
        var tonight = await NameAsync(read, marks, page, caps, clock, ticker, index, export: false, cardContext: await CardContextAsync(read, store));

        return Results.Content(
            SinglePageApp.NotANight(date) + ReportExporter.Link(ticker) + tonight.Region,
            "text/html; charset=utf-8");
    }

    var name = await NameAsync(read, marks, page, caps, clock, ticker, index, export: false, on: await read.NewestNightAsync(asked) ?? asked);

    return Results.Content(name.Region, "text/html; charset=utf-8");
});

// One name's report as a file to hand to someone, section 15.4's second surface: the name
// screen's region composed by the same code from the same reads, less the research controls
// and the pause, which are the application asking the operator something rather than the
// report. Offered as a download, so where the file is kept is the operator's choice.
// see: A single report can still be exported as a self-contained file
app.MapGet(ReportExporter.Route + "{ticker}", async (string ticker, ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock, ReportExporter exporter) =>
{
    var name = await NameAsync(read, marks, page, caps, clock, ticker, builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC", export: true);

    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(exporter.Document(ticker, name.AsOf, name.Region)),
        "text/html; charset=utf-8",
        ReportExporter.FileName(ticker, name.AsOf));
});

// The name screen's region, read here and composed by the app, for the page and for the file.
static async Task<(string Region, DateOnly? AsOf)> NameAsync(ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock, string ticker, string index, bool export, DateOnly? on = null, CardContext? cardContext = null)
{
    var bars = await read.BarsAsync(ticker, DateOnly.MinValue, on ?? DateOnly.MaxValue);
    var indicators = await read.IndicatorsAsync(ticker, DateOnly.MinValue, on ?? DateOnly.MaxValue);
    var levels = await read.LevelsAsync(ticker, on);
    var profile = await read.ProfileAsync(ticker, on);
    var ladder = await read.LadderAsync(ticker, on);
    var moves = await read.MovesAsync(ticker, on);

    // The night's listing for this name, and its neighbours on the list, so the
    // page can say why it is here and the walk is one pass through.
    var night = on ?? await read.NewestNightAsync();
    var listings = night is { } dated ? await read.ListingsAsync(dated) : [];

    // The index on the night the list is from, so the neighbours are that
    // night's members rather than today's.
    var universe = await read.UniverseAsync(index, night);

    // The newest night, which every move's group was read on whatever night the page is for, and
    // the index on it, which says whether the name was a member when its groups were read.
    var groupsReadOn = on is null ? night : await read.NewestNightAsync();
    var membersThen = groupsReadOn == night ? universe : await read.UniverseAsync(index, groupsReadOn);

    // The neighbours on the list, in the order the list itself is drawn in. The
    // walk is about position, so it takes the same ordering with the same inputs
    // rather than a cheaper one that could order differently. Those inputs are the
    // night walked's own listing rows, which on a page opened with no night is the
    // newest night the listings hold and not the newest plans the store holds: a
    // night can store plans and stop before it lists.
    var ordered = night is { } evening
        ? TonightScreen.Rows(
            evening,
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await read.ClosesToTheNightAsync(evening),
            gates: await read.ListRuleAsync(evening) == ListRules.Filter ? await read.GateResultsAsync(evening) : null,
            readings: await read.FundamentalReadingsAsync(evening))
        : [];

    // On a night the families drew the page's list, the neighbours are the stocks the page lists, in its order.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    if (night is { } drawn && await read.FamilyNightAsync(drawn) is not null)
    {
        ordered = TonightScreen.ListedByFamilies(
            drawn,
            await read.FamilyPicksAsync(drawn),
            listings,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await read.ClosesToTheNightAsync(drawn),
            readings: await read.FundamentalReadingsAsync(drawn));
    }

    var at = ordered.Select((row, position) => (row.Ticker, position))
        .Where(pair => pair.Ticker == ticker)
        .Select(pair => (int?)pair.position)
        .FirstOrDefault();

    // On or after the last stored session, so the strip states what is coming
    // rather than what has been. A night's own session is what the calendar
    // window starts at.
    var nextEvent = await read.NextEventAsync(
        ticker,
        bars.Count > 0 ? bars[^1].SessionDate : DateOnly.MinValue);

    // Every filing this name holds, which the numbers section draws five of. A
    // name nobody has opened holds none and the section says so, because the
    // computed sections render from the nightly store whatever this read returns.
    var fundamentals = await read.FundamentalsAsync(ticker, on);

    // The night's readings of the member's reported quarters and the quarters behind them, which the
    // numbers open with; a night before the readings existed has none and the numbers draw as they did.
    var reading = await read.FundamentalReadingAsync(ticker, on);
    var readQuarters = reading?.FetchedAt is { } fetched ? await read.QuartersOfAFetchAsync(ticker, fetched) : [];

    // The high and the low of the sessions the largest move spans, which the fact
    // strip states beside the close. A name with no annotated move has none, and
    // the strip says so rather than drawing a blank.
    var extremes = await read.MoveExtremesAsync(ticker, on);

    // The written sections and the documents they cite, which the dates-and-sources
    // region draws with the calendar from the newest stored session on. The industry
    // cycle among them is the theme's, read for the industry the index names the member in.
    var written = await read.WrittenSectionsAsync(ticker, on);

    // The index on the page's night and every name's two readings, which the peers table draws for the
    // members of the name's group the night chose, and those members' stored closes, which the picture
    // beside each one's ticker draws. The readings are the newest night's alone, so an earlier night reads none.
    var peerReadings = on is null ? await read.PeerReadingsAsync() : [];
    var peerCloses = await read.ClosesAsync([.. NameScreen.PickedPeers(ticker, peerReadings).Select(pick => pick.Ticker)]);

    // The name's swing filter result for the page's night, or its newest where the page is tonight's, and
    // where the night's list is the swing filter's and the name missed exactly one gate of it with nothing
    // excluding it, the gate it missed and by how much.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    var gate = await read.GateResultAsync(ticker, on);
    var missed = gate is not null && night is { } shown && gate.SessionDate == shown && CloseScreen.MissedOne(gate) && await read.ListRuleAsync(shown) == ListRules.Filter
        ? CloseScreen.MissOf(
            gate,
            await FilterSettingsOf(read, gate.Version),
            gate.Trigger ? null : EquityBrief.Core.Filter.NearMiss.FiredSessionsBefore(await read.TriggerEventsAsync(ticker, shown)))
        : null;

    var region = NameScreen.Region(
        page, marks, ticker, bars, indicators, levels, profile, ladder, nextEvent, moves,
        fundamentals,
        extremes,
        listings.FirstOrDefault(listing => listing.Ticker == ticker),
        at is > 0 ? ordered[at.Value - 1].Ticker : null,
        at is { } position && position + 1 < ordered.Count ? ordered[position + 1].Ticker : null,
        await read.SectionStatesAsync(ticker, on ?? DateOnly.MaxValue),
        // The staleness verdict, the newest pass and what research has spent are the
        // application asking about the company now, so a page about an earlier night carries
        // none of them and offers no control, as the exported file carries none.
        on is null ? await read.StalenessAsync(ticker) : null,
        written,
        on is null ? await read.NewestPassAsync(ticker) : null,
        export || on is not null ? null : await SpendNow(read, caps, clock),
        await read.CitedDocumentsAsync(NameScreen.Cited(written)),
        await read.EventsAsync(ticker, bars.Count > 0 ? bars[^1].SessionDate : DateOnly.MinValue),
        export || on is not null ? null : await read.PaidCallSpendsAsync(),
        on ?? clock.SessionDateAt(clock.UtcNow),
        // Whether the name's stored series is suspect, which the page opens with and the
        // file carries, since it is a statement about the figures rather than a question
        // the application asks.
        (await read.SuspectSeriesAsync()).FirstOrDefault(row => string.Equals(row.Ticker, ticker, StringComparison.Ordinal)),
        // Where the name holds no bar, whether the backfill asked for its year and none came back.
        bars.Count == 0 ? await read.NoYearAsync(ticker) : null,
        // The membership row the masthead names the company, its sector and industry from.
        universe.FirstOrDefault(row => string.Equals(row.Ticker, ticker, StringComparison.Ordinal)),
        on,
        universe,
        peerReadings,
        // The name's earnings reaction record as of the page's night.
        await read.ReactionsAsync(ticker, on),
        NameScreen.NotAMemberOn(ticker, groupsReadOn, membersThen),
        // The name's swing readings for the page's night, or its newest where the page is tonight's.
        await read.SwingReadingAsync(ticker, on),
        gate,
        // Whether the operator watches the name, which its header's press says. An exported file carries
        // no press, because nothing it is opened beside can answer one.
        export ? null : (await read.WatchedAsync()).Any(row => string.Equals(row.Ticker, ticker, StringComparison.Ordinal)),
        // Every night the live list picked the name before the page's own, each trade as it stood on the page's night.
        night is { } picked ? PicksScreen.Before(await read.PicksAsync(picked, ticker), picked) : null,
        reading,
        readQuarters,
        peerCloses,
        missed,
        // The plan the gate row's version reads, by its word, naming the plan the night's live rule read where
        // the row stores no input.
        gate is null ? null : await VersionPlanOf(read, gate.Version),
        // What was written about the company in the thirty days before the night, each article with the newest
        // label the labeller wrote for it, whether the name was on the night's list and the labeller's own run
        // for the night, which say why a name holds no label.
        news: night is { } storied
            ? NewsScreen.Build(ticker, storied, await read.NewsAsync(ticker, storied), gate is not null && gate.SessionDate == storied && gate.Passed, NewsScreen.RunFor(await read.LabellerRunsAsync(), storied))
            : null,
        // The name's rows on the page's list for the night, where the setup families drew it.
        familyPicks: night is { } drawnOn ? await read.FamilyPicksAsync(drawnOn) : null,
        // The sector heavyweights' holdings as of the night, which say whether they hold the name.
        heavyweights: night is { } heldOn ? await read.HeavyweightHoldingsAsync(heldOn) : null,
        // The name's member readings for the page's night, or its newest where the page is tonight's, and its company's
        // rating counts as its newest fetch on or before that night filed them.
        memberReading: await read.MemberReadingAsync(ticker, on),
        ratings: await read.RatingsAsync(ticker, on),
        decisionCards: night is { } cardsOn ? await read.DecisionCardsOfAsync(ticker, cardsOn) : null,
        // The account and the taken trades on tonight's page alone: an export and an earlier night's page draw neither.
        cardContext: export ? null : cardContext);

    return (region, bars.Count > 0 ? bars[^1].SessionDate : null);
}

// The settings a filter version holds, which a gate result stored under it was decided by, and the proposed
// settings for a result stored under no opened version.
static async Task<EquityBrief.Core.Filter.FilterSettings> FilterSettingsOf(ReadApi read, string version) =>
    await read.FilterSettingsAsync(version) is { } json
        ? EquityBrief.Core.Filter.FilterSettings.Read(json)
        : EquityBrief.Core.Filter.FilterSettings.Proposed;

// The plan a filter version's trade gate reads, by its word, read off the version's stored settings as Past
// picks reads it, and section 17's proposed input where no version row is stored, which is what the filter
// ran on.
// see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
static async Task<string> VersionPlanOf(ReadApi read, string version)
{
    if (await read.FilterSettingsAsync(version) is { } json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);

        if (document.RootElement.TryGetProperty("trade", out var trade) && trade.GetString() is { } word)
        {
            return word;
        }
    }

    return EquityBrief.Core.Filter.FilterSettings.Word(EquityBrief.Core.Filter.FilterSettings.Proposed.Trade);
}

// Where the pass a page started stands, which the page asks for while it watches one.
//
// It reads the run log and the name's own sections and writes nothing, as every screen does.
// The instant comes from the reply to the press, so the rows read are that pass's and not an
// earlier one's, and a request carrying no instant or one that cannot be read answers for
// nothing rather than for whatever ran last.
// see: A pass the page starts is watched until it ends and the page redraws as each section lands
app.MapGet(SinglePageApp.PassRoute + "{ticker}", async (string ticker, string? since, ReadApi read, MarkRenderer marks) =>
{
    if (!DateTimeOffset.TryParseExact(since ?? string.Empty, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var started))
    {
        return Results.Content(
            "<p class=\"pass-progress\" role=\"status\" data-state=\"unasked\" data-sections=\"0\">no pass was named, so nothing is watched</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status400BadRequest);
    }

    return Results.Content(
        marks.PassProgressLine(
            ticker,
            NameScreen.Progress(
                await read.PassRowsAsync(ticker, started),
                await read.SectionStatesAsync(ticker, DateOnly.MaxValue))),
        "text/html; charset=utf-8");
});

// A press asking for a report, from a row of tonight's list or a name's own page: write
// the request, start the worker's drain, and return at once with the line the page puts
// beside the control.
//
// Refused without the page's own header, so another site's page cannot start a pass, and
// refused for a name the index does not hold, before anything is written or started. The
// request is the one row written here: the pass is the worker's, and its rows are what
// the page reads next. A press that wrote nothing starts nothing, since what it asked for
// is already waiting.
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
// see: A pass is started only by a request carrying the name page's own header
app.MapPost(SinglePageApp.PassRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read, IClock clock, IDrainLauncher launcher) =>
{
    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Results.Content(
            "<p class=\"pass-refused\" data-refused=\"header\">no pass was started: the request did not come from the name page</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";

    if (!await read.IsMemberAsync(index, ticker))
    {
        return Results.Content(
            $"<p class=\"pass-refused\" data-refused=\"membership\">no pass was started: {System.Net.WebUtility.HtmlEncode(ticker)} is not a member of the index</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status404NotFound);
    }

    var form = request.HasFormContentType ? await request.ReadFormAsync() : null;

    // The instant before the pass is started, which the reply carries and the page hands back
    // while it watches: the pass's own run is the first one of this name to start at or after
    // it, so an earlier pass's rows are never read as this one's.
    var watchFrom = clock.UtcNow;

    var from = string.Equals(form?["from"].FirstOrDefault(), ResearchRequests.FromList, StringComparison.Ordinal)
        ? ResearchRequests.FromList
        : ResearchRequests.FromName;

    // A press asking for every section to be written again, which the page offers once a name's
    // research stands and no pass has run for it today.
    var refresh = string.Equals(form?["refresh"].FirstOrDefault(), "true", StringComparison.Ordinal);

    var started = await read.AskAsync(ticker, from, Lane(builder.Configuration), refresh);
    var drain = started.Written ? launcher.Start() : null;
    var line = drain is null ? started.Line : started.Line + " " + drain.Line;

    return Results.Content(
        $"<p class=\"pass-started\" data-started=\"{(started.Written ? "true" : "false")}\" data-drain=\"{(drain?.Started == true ? "true" : "false")}\" data-watch-from=\"{watchFrom.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\">{System.Net.WebUtility.HtmlEncode(line)}</p>",
        "text/html; charset=utf-8",
        statusCode: started.Written ? StatusCodes.Status202Accepted : StatusCodes.Status409Conflict);
});

// The press running the rest of a night left unfinished, from tonight's notice or the Run page: refused
// without the page's own header, refused while a night holds the lock, refused where the newest night is
// not left unfinished, and otherwise starting the worker's run of the rest of the newest night, as the
// report press starts the drain. The reply is the line the page puts beside the press.
// see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file
app.MapPost(SinglePageApp.NightResumeRoute, async (HttpRequest request, ReadApi read, IClock clock, StoreLocation store, IDrainLauncher launcher) =>
{
    static IResult Said(string state, string line, int status) =>
        Results.Content(
            $"<p class=\"night-said\" data-resume=\"{state}\">{System.Net.WebUtility.HtmlEncode(line)}</p>",
            "text/html; charset=utf-8",
            statusCode: status);

    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Said("refused", "The rest of the night was not started: the request did not come from the page.", StatusCodes.Status403Forbidden);
    }

    if (NightLock.Holder(store.DataRoot) is { } holder)
    {
        return Said("held", $"The rest of the night was not started: {holder} holds the night's lock and is still running.", StatusCodes.Status409Conflict);
    }

    var newest = await read.RunNightAsync();
    var state = newest is { } ran ? NightFrom(await read.RunLogAsync(ran), ran, clock, store).State : null;

    if (state != NightStates.Unfinished)
    {
        return Said("not-unfinished", $"The rest of the night was not started: the newest night is {state ?? "not on the run log"}, not left unfinished.", StatusCodes.Status409Conflict);
    }

    var started = launcher.StartTheRestOfTheNight();

    return Said(started.Started ? "started" : "not-started", started.Line, started.Started ? StatusCodes.Status202Accepted : StatusCodes.Status409Conflict);
});

// A press on the queue screen taking a report out before anybody started writing it.
//
// Refused without the page's own header, as the press that asks for one is, and refused
// once the worker has claimed the request, because what the operator asked to remove is a
// report that has not been generated.
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
// see: A pass is started only by a request carrying the name page's own header
app.MapPost(SinglePageApp.WithdrawRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read) =>
{
    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Results.Content(
            "<p class=\"pass-refused\" data-refused=\"header\">nothing was taken out: the request did not come from the queue screen</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    var form = request.HasFormContentType ? await request.ReadFormAsync() : null;

    if (!DateTimeOffset.TryParseExact(form?["askedAt"].FirstOrDefault() ?? string.Empty, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var askedAt))
    {
        return Results.Content(
            "<p class=\"pass-refused\" data-refused=\"instant\">nothing was taken out: no request was named</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status400BadRequest);
    }

    var taken = await read.WithdrawAsync(ticker, askedAt);

    return Results.Content(
        $"<p class=\"pass-withdrawn\" data-withdrawn=\"{(taken.Written ? "true" : "false")}\">{System.Net.WebUtility.HtmlEncode(taken.Line)}</p>",
        "text/html; charset=utf-8",
        statusCode: taken.Written ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
});

// The queue with each request's time, as the queue page states them and tonight's rows read
// them, worked out once for both.
async Task<(IReadOnlyList<RequestRow> Rows, IReadOnlyList<RequestTime> Times, PassEstimate Estimate)> QueueRead(ReadApi read, IClock clock)
{
    var rows = await read.QueueAsync();
    var estimate = QueueTimes.Estimate(await read.FinishedPassesAsync());

    return (
        rows,
        QueueTimes.For(
            rows,
            await read.PassStartsAsync(rows.Where(row => row.State == ResearchRequests.Writing)),
            estimate,
            QueuePrices(builder.Configuration),
            clock.UtcNow),
        estimate);
}

// The peak windows the queue page states, read from the profile the research job uses and
// never its key, or none where the prices cannot be read, which the page says rather than
// refusing to draw the queue.
static EquityBrief.Core.Providers.ResearchPricing? QueuePricing(IConfiguration configuration) =>
    Profile(configuration, EquityBrief.Core.Providers.ModelProfiles.ResearchJob)?.Pricing;

// The windows the drain waits out, the job's profile's and each its sections name, read as the
// drain reads them, or none where they cannot be read.
static IReadOnlyList<EquityBrief.Core.Providers.ResearchPricing> QueuePrices(IConfiguration configuration)
{
    try
    {
        return EquityBrief.Core.Providers.ModelProfiles.PricesFor(
            key => configuration[key],
            key => configuration.GetSection(key).GetChildren().Select(child => child.Value),
            EquityBrief.Core.Providers.ModelProfiles.ResearchJob);
    }
    catch (InvalidOperationException)
    {
        return [];
    }
}

// A paid job's profile as configuration names it, without its key, or none where the job
// names no profile or one that cannot be read.
static EquityBrief.Core.Providers.ModelProfile? Profile(IConfiguration configuration, string job)
{
    try
    {
        return EquityBrief.Core.Providers.ModelProfiles.Describe(
            key => configuration[key],
            key => configuration.GetSection(key).GetChildren().Select(child => child.Value),
            job);
    }
    catch (InvalidOperationException)
    {
        return null;
    }
}

// Which lane would write a report, read off configuration at the press rather than
// chosen per request, so a queue drained a day later writes under the lane the press
// meant. The local lane is drawn and refused until the two lanes' reports have been
// compared, so a setting naming it is not honoured and the paid lane is what a request
// carries.
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
static string Lane(IConfiguration configuration)
{
    var asked = configuration["EquityBrief:Research:Lane"];

    // The lanes a press may be written under. The local lane is drawn beside this one and
    // is not among them, so a setting naming it is read and not honoured rather than
    // silently taken: a request carries the lane that would write it, and nothing writes
    // the local lane's sections on their own until the two have been compared.
    string[] offered = [SinglePageApp.PaidLane];

    return Array.Exists(offered, lane => string.Equals(lane, asked, StringComparison.Ordinal))
        ? asked!
        : SinglePageApp.PaidLane;
}

// Where research stands against the caps now, which is what a name page states a
// pause from. The month's rows to this instant, judged by the rule the spend cap
// refuses a call by, with no call in hand.
static async Task<SpendVerdict> SpendNow(ReadApi read, SpendCaps caps, IClock clock)
{
    var now = clock.UtcNow;

    return NameScreen.Spend(await read.SpentRowsAsync(SpendLedger.MonthStart(now), now.AddSeconds(1)), caps, now);
}

// A night's spend rows, over its UTC month to the end of its UTC day.
static async Task<IReadOnlyList<SpentRow>> SpentOn(ReadApi read, DateOnly night)
{
    var (from, to) = TonightScreen.SpendWindow(night);

    return await read.SpentRowsAsync(from, to);
}

// What the cards a page draws are handed beyond the night's rows: the operator's account where it is set, their taken
// trades, and the card's presses, which a page the operator reads draws and an export never does.
// see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
static async Task<CardContext> CardContextAsync(ReadApi read, StoreLocation store) =>
    new(EquityBrief.Core.Cards.AccountFile.Read(store.DataRoot), await read.OpenTakenTradesAsync(), await read.TakenTradesAsync(), Pressable: true, await read.TakenRecordsAsync());

// How a night went, the one view the Run page's headline and tonight's notice are both handed, read off the
// night's run log rows and the night holding the lock, if one does.
// see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
static NightView NightFrom(IReadOnlyList<RunStageRow> log, DateOnly night, IClock clock, StoreLocation store) =>
    RunScreen.Night(log, night, clock.UtcNow, EquityBrief.Core.Providers.RetryPolicy.Standard.Deadline, NightLock.Holder(store.DataRoot), RefusalOf(night, clock, store));

// The reason the night's script refused the night of a session before any worker existed, read off the file
// it left under the data root where that refusal fell on the session's own evening, and none otherwise.
// see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main
static string? RefusalOf(DateOnly night, IClock clock, StoreLocation store) =>
    EquityBrief.Core.Configuration.NightBuild.Refusal(store.DataRoot) is { } refusal && clock.SessionDateAt(refusal.At) == night
        ? refusal.Reason
        : null;

// The session tonight's notice is about: the date the page was asked for, and with none the session the clock
// is in where the exchange trades it, whose night may not have drawn its list yet, and otherwise the night the
// page draws.
static DateOnly NoticeSession(string? asked, DateOnly drawn, IClock clock)
{
    if (asked is { Length: > 0 })
    {
        return drawn;
    }

    var now = clock.SessionDateAt(clock.UtcNow);

    return Traded(now) && now >= drawn ? now : drawn;
}

// The run log of each of the seven nights up to a night that the store holds an evening for, oldest first,
// which the Run page's research region counts over.
static async Task<IReadOnlyList<(DateOnly Night, IReadOnlyList<RunStageRow> Log)>> WeekOf(ReadApi read, DateOnly night)
{
    var week = new List<(DateOnly, IReadOnlyList<RunStageRow>)>();

    foreach (var held in (await read.NightsAsync()).Where(one => one <= night).OrderDescending().Take(7).Order())
    {
        week.Add((held, await read.RunLogAsync(held)));
    }

    return week;
}

// The phase report the harness last wrote, read as text and handed to the
// projection rather than opened by it, so nothing on the read surface reaches
// the filesystem for a store it does not own. A machine with no report says so
// rather than showing four zeros.
//
// Two screens read it since 5.8: section 15.10 gives the harness a region of its
// own on the run page, and section 15.7 states the verdict in tonight's header.
// One function rather than one per route, so the two cannot come to disagree
// about which file is the report.
static string? PhaseReport(WebApplicationBuilder builder, string? checkout)
{
    var path = builder.Configuration["EquityBrief:PhaseReport"]
        ?? Path.Combine(checkout ?? builder.Environment.ContentRootPath, "artifacts", "phase-report.json");

    return File.Exists(path) ? File.ReadAllText(path) : null;
}

// Whether the exchange traded on a day, for the run page's overnight queue region, and
// a day the closure table cannot place is one the region does not name, rather than a
// page that fails to draw for a night nobody asked about.
static bool Traded(DateOnly day)
{
    try
    {
        return EquityBrief.Core.Bars.ExchangeClosures.IsSession(day);
    }
    catch (InvalidOperationException)
    {
        return false;
    }
}

// Tonight's list, section 15.7, read here and composed by the app.
//
// `/screens/tonight` resolves to the newest night the listings hold and
// `/screens/tonight/<date>` to an earlier one, which is the pair 15.3's routes
// name.
app.MapGet("/screens/tonight/{night?}", async (
    string? night,
    HttpRequest request,
    ReadApi read,
    MarkRenderer marks,
    SinglePageApp page,
    SpendCaps caps,
    IClock clock,
    StoreLocation store) =>
{
    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";

    var asOf = night is { Length: > 0 }
        ? DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        : await read.NewestNightAsync();

    if (asOf is not { } dated)
    {
        return Results.Content(
            "<p class=\"degraded\" data-night=\"none\">no night has been recorded yet</p>",
            "text/html; charset=utf-8");
    }

    // The notice at the top, from the same view the Run page's headline is handed, for the night the page
    // was asked for or with none for the night the clock is in, whose list may not be drawn yet.
    // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
    var about = NoticeSession(night, dated, clock);
    var notice = page.NightNotice(marks, NightFrom(await read.RunLogAsync(about), about, clock, store));

    // The index the page reads, chosen under Universe and kept in the link, the S&P 500 where it names none.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var selector = Cards.Universe(reading, await read.MembersByIndexAsync(dated), night is { Length: > 0 } ? SinglePageApp.NightRoute + night : "#/");

    if (reading != Universes.Large)
    {
        var indexNight = await read.IndexNightAsync(reading.Code, dated);
        var indexHeld = await read.IndexNightsAsync(reading.Code);

        if (indexNight is null)
        {
            return Results.Content(
                notice + page.IndexTonightRegion(marks, dated, reading, selector, indexHeld, null, [], null),
                "text/html; charset=utf-8");
        }

        var members = await read.IndexMembersAsync(reading.Code, dated);
        var indexResults = await read.IndexResultsAsync(reading.Code, dated);
        var (indexQueued, indexTimes, _) = await QueueRead(read, clock);
        // Each card with its family's recorded sweep answers on the index, each line gone with the family's next passing
        // sweep or its freeze, and a frozen family's card live with its rule's words.
        // see: No family on any index is set aside or hidden by a test result without the operator's word
        var indexAnswers = await read.SweepAnswersAsync(reading.Code, TonightScreen.PastTheNight(dated));
        var indexRegister = await read.RegisteredCandidatesAsync();
        var indexCardContext = await CardContextAsync(read, store);
        // Each card drawn by the rule the link chooses for it, the provisional rule or the live rule where it names none,
        // with its funnel, its stretch line and, under the breakout card, the breakouts forming.
        // see: A variant's picks are shown on its card when chosen and its results only under its tests
        var indexCards = RuleScreen.WithRules(
            CardScreen.WithCards(
                TonightScreen.WithSweepLines(
                    TonightScreen.WithIndexFreezes(
                        TonightScreen.IndexCards(
                            reading,
                            indexNight,
                            await read.IndexPicksAsync(reading.Code, dated),
                            indexResults,
                            members,
                            await read.ResearchedAsync(),
                            QueueTimes.States(indexQueued, indexTimes, clock.SessionZone),
                            await read.FundamentalReadingsAsync(dated)),
                        reading,
                        indexRegister,
                        indexNight),
                    indexAnswers,
                    family => TonightScreen.IndexFrozenAt(reading.Code, family, indexRegister, indexNight)),
                await read.DecisionCardsAsync(reading.Code, dated),
                indexCardContext),
            reading.Name,
            false,
            dated,
            indexRegister,
            family => request.Query[RuleScreen.QueryKey(family)].FirstOrDefault(),
            await read.RuleNightsAsync(reading.Code, dated),
            await read.RulePicksAsync(reading.Code, dated),
            await read.FormingRowsAsync(reading.Code, dated),
            indexNight.MarketOpen);
        var indexOpen = (await read.IndexTradesAsync(reading.Code, dated)).Count(trade => trade.Listed < dated && trade.EndedOn is null);

        // The heavyweights' card drawn from the index's own book, or from its live rule's book where a freeze stands and the
        // night kept it.
        // see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
        var heavyweightRule = TonightScreen.IndexLiveRule(reading.Code, EquityBrief.Core.Families.HeavyweightRule.Name, indexRegister, indexNight);
        var boughtCards = await read.BoughtCardsAsync(reading.Code, dated);
        var heavyweightCard = heavyweightRule is null
            ? TonightScreen.IndexHeavyweights(
                reading,
                indexNight,
                await read.IndexHoldingsAsync(reading.Code, dated),
                await read.IndexLastRebalanceAsync(reading.Code, dated),
                await read.IndexHoldingClosesAsync(reading.Code, dated),
                members)
            : TonightScreen.WithIndexHeavyweightFreeze(
                TonightScreen.IndexHeavyweights(
                    reading,
                    indexNight,
                    await read.IndexRuleHoldingsAsync(heavyweightRule, dated),
                    await read.IndexRuleLastRebalanceAsync(heavyweightRule, dated),
                    await read.IndexRuleHoldingClosesAsync(heavyweightRule, dated),
                    members),
                reading,
                indexRegister,
                indexNight);

        // The card's selector, as the S&P 500's: the provisional or live rule first and each registered variant after
        // it, a chosen variant's own book drawn under the band.
        // see: A variant's picks are shown on its card when chosen and its results only under its tests
        var (indexHeavyweightChoice, indexHeavyweightVariant) = RuleScreen.Heavyweights(
            reading.Name,
            false,
            dated,
            indexRegister,
            request.Query[RuleScreen.QueryKey(EquityBrief.Core.Families.HeavyweightRule.Name)].FirstOrDefault(),
            heavyweightCard.Rule);

        heavyweightCard = (indexHeavyweightVariant is { } chosenBook
            ? TonightScreen.IndexHeavyweights(
                reading,
                indexNight,
                await read.IndexRuleHoldingsAsync(chosenBook, dated),
                await read.IndexRuleLastRebalanceAsync(chosenBook, dated),
                await read.IndexRuleHoldingClosesAsync(chosenBook, dated),
                members)
            : heavyweightCard) with { RuleChoice = indexHeavyweightChoice };

        return Results.Content(
            notice + page.IndexTonightRegion(
                marks,
                dated,
                reading,
                selector,
                indexHeld,
                TonightScreen.IndexLine(reading, indexNight, indexCards, indexResults, indexOpen),
                indexCards,
                CardScreen.WithCards(
                    heavyweightCard with
                    {
                        SweepFoundNone = EquityBrief.Core.Sweep.SweepLine.Drawn(
                            [.. indexAnswers.Where(answer => answer.Family == EquityBrief.Core.Families.HeavyweightRule.Name)],
                            TonightScreen.IndexFrozenAt(reading.Code, EquityBrief.Core.Families.HeavyweightRule.Name, indexRegister, indexNight)),
                    },
                    boughtCards,
                    indexCardContext),
                indexNight.Fault is null ? null : TonightScreen.NotComputed(reading)),
            "text/html; charset=utf-8");
    }

    // The record starts on the swing filter's first night, and an evening before it is not drawn.
    // see: The dated screens open from the swing filter's first night, and no evening before it is drawn
    var first = await read.FirstFilterNightAsync();
    var held = (await read.NightsAsync()).Where(one => first is not { } from || one >= from).ToArray();

    if (first is { } start && dated < start)
    {
        return Results.Content(
            notice + SinglePageApp.BeforeTheRecord(dated, start, "Tonight", SinglePageApp.NightRoute, "#/", held),
            "text/html; charset=utf-8");
    }

    var listings = await read.ListingsAsync(dated);

    // Section 18's banner. A night the store has no listings for shows the data
    // date it does have rather than a list built from older bars.
    if (listings.Count == 0)
    {
        return Results.Content(
            notice + page.StaleBanner(dated, await read.NewestNightAsync()),
            "text/html; charset=utf-8");
    }

    // The index on the night shown, so a name on that night's list has the close
    // that night stored even after it has left.
    var universe = await read.UniverseAsync(index, dated);

    // The plan's reward to risk the ordering breaks ties on is read off the
    // night shown's own listing rows, which kept the plan as it stood that night,
    // so an earlier night is ordered by its own plans and not by the newest. The
    // close each row shows comes from the night's own bar, beside the session
    // before it, because a close from one session and a change computed from
    // another is one row saying two things.
    //
    // The three the row states beside the name, the close and the reasons. The
    // distance mark takes the universe screen's own cell, so tonight's list and
    // the universe table draw one shape from one set of numbers; the day change
    // needs the session before the night, which is a second read rather than a
    // column on any row.
    var cells = UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal);

    // The rule the night's list was drawn by, and on a night the swing filter drew it, each member's gates,
    // which order the list and say why each row is on it.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    var rule = await read.ListRuleAsync(dated);
    var gates = rule == ListRules.Filter ? await read.GateResultsAsync(dated) : null;
    var market = await read.MarketReadingAsync(dated);

    // The names whose stored series is suspect, which a row says beside the name.
    var closes = await read.ClosesToTheNightAsync(dated);
    var suspects = await read.SuspectSeriesAsync();
    var researched = await read.ResearchedAsync();
    var readings = await read.FundamentalReadingsAsync(dated);
    var listed = TonightScreen.Rows(
        dated,
        listings,
        cells,
        closes,
        suspects,
        researched,
        gates,
        readings,
        news: await read.NewsCountsAsync(dated));

    // Every trade the live list recommended up to the night, which "Still open" reads and the list's rows
    // are marked from where one repeats a trade still open.
    // see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
    IReadOnlyList<PickRow> picks = gates is null ? [] : await read.PicksAsync(dated);

    listed = TonightScreen.MarkedAsRepeats(dated, listed, picks);

    // On a night the families drew the page's list, the rows are the stocks the page lists, in its order,
    // and the list is drawn as a card a family.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    var onThePage = gates is null ? null : await read.FamilyNightAsync(dated);
    IReadOnlyList<FamilyPickRow> familyPicks = onThePage is null ? [] : await read.FamilyPicksAsync(dated);

    if (onThePage is not null)
    {
        listed = TonightScreen.ListedByFamilies(
            dated,
            familyPicks,
            listings,
            cells,
            closes,
            suspects,
            researched,
            gates,
            readings,
            await read.NewsCountsAsync(dated));
    }

    // The members one gate short, on a night the swing filter listed, each measured against the settings of
    // the version its result was stored under, and a missed trigger against the firings its own results show.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    IReadOnlyList<EquityBrief.Web.Marks.ListingCell>? near = null;

    if (gates is not null)
    {
        var settings = new Dictionary<string, EquityBrief.Core.Filter.FilterSettings>(StringComparer.Ordinal);

        foreach (var version in gates.Where(CloseScreen.MissedOne).Select(gate => gate.Version).Distinct(StringComparer.Ordinal))
        {
            settings[version] = await FilterSettingsOf(read, version);
        }

        var fired = new Dictionary<string, int?>(StringComparer.Ordinal);

        foreach (var gate in gates.Where(gate => CloseScreen.MissedOne(gate) && !gate.Trigger))
        {
            fired[gate.Ticker] = EquityBrief.Core.Filter.NearMiss.FiredSessionsBefore(await read.TriggerEventsAsync(gate.Ticker, dated));
        }

        near = TonightScreen.Close(
            dated,
            listings,
            cells,
            closes,
            gates,
            version => settings.TryGetValue(version, out var held) ? held : EquityBrief.Core.Filter.FilterSettings.Proposed,
            fired,
            suspects,
            researched,
            readings);
    }

    // What the queue holds for each listed name, from the times the queue page states, so a
    // row and the selected name say a report is queued or being written and when.
    // see: The queue page states when each request will be written
    var (queued, times, _) = await QueueRead(read, clock);
    var states = QueueTimes.States(queued, times, clock.SessionZone);
    IReadOnlyList<EquityBrief.Web.Marks.ListingCell> rows = [.. listed.Select(row => states.TryGetValue(row.Ticker, out var state) ? row with { Queue = state } : row)];
    IReadOnlyList<EquityBrief.Web.Marks.ListingCell>? nearRows = near is null ? null : [.. near.Select(row => states.TryGetValue(row.Ticker, out var state) ? row with { Queue = state } : row)];

    // Whichever row the reader selected, from the hash, and the first row when
    // they have selected none. Section 15.7's region is for whichever row is
    // selected, and a composition fixed at the first answers a question nobody
    // asked. Its plan and its bands are the ones the night shown stored, drawn
    // against that night's close. A row of either list can be selected.
    var asked = request.Query["name"].FirstOrDefault();
    var selection = TonightScreen.Selected(rows, asked, nearRows);

    var member = selection is { } picked ? universe.FirstOrDefault(row => row.Ticker == picked.Ticker) : null;
    var selected = selection is { } chosen
        ? NameScreen.PlanRegion(
            page,
            marks,
            chosen.Ticker,
            await read.LadderAsync(chosen.Ticker, dated),
            member?.Close ?? 0m,
            await read.LevelsAsync(chosen.Ticker, dated),
            member?.TypicalMove,
            member?.Close is not null)
        : string.Empty;

    // The record beside each reason and the evening's own totals, which are
    // 15.7's two halves that need a reason to have a history. The record is
    // over every night the store holds rather than over this one, for the
    // reason the run page states: it is a property of the reason and not of the
    // evening.
    var records = RunScreen.Records(
        await read.ListingsAsync(),
        RunScreen.Resolved(await read.ForwardReturnsAsync()));

    // On a night the families drew: each family's card, the shared list of stocks a single gate short under
    // any setup, and the line the page opens them on.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    var ruleView = TonightScreen.RuleView(rule, gates, market);
    var familyResults = onThePage is null ? [] : await read.FamilyResultsAsync(dated);
    var pullbackSettings = gates?.FirstOrDefault() is { } stored ? await FilterSettingsOf(read, stored.Version) : null;
    // Each card with the line its family's recorded sweep answers draw, read against the instant its live rule was
    // registered as of the night.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    var register = await read.RegisteredCandidatesAsync();
    var answers = await read.SweepAnswersAsync(reading.Code, TonightScreen.PastTheNight(dated));
    // Each pick with the card the night stored for it, opened in place beneath its row.
    // see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
    var cardContext = await CardContextAsync(read, store);
    // Each card drawn by the rule the link chooses for it, the live rule where it names none, with its funnel, its
    // stretch line and, under a breakout card, the breakouts forming.
    // see: A variant's picks are shown on its card when chosen and its results only under its tests
    var cards = onThePage is null || gates is null
        ? null
        : RuleScreen.WithRules(
            CardScreen.WithCards(
                TonightScreen.WithSweepLines(
                    TonightScreen.Families(dated, onThePage, familyPicks, rows, gates, register, ruleView, familyResults, pullbackSettings),
                    answers,
                    family => TonightScreen.FrozenAt(family, register, dated)),
                await read.DecisionCardsAsync(reading.Code, dated),
                cardContext),
            reading.Name,
            true,
            dated,
            register,
            family => request.Query[RuleScreen.QueryKey(family)].FirstOrDefault(),
            await read.RuleNightsAsync(reading.Code, dated),
            await read.RulePicksAsync(reading.Code, dated),
            await read.FormingRowsAsync(reading.Code, dated),
            ruleView.MarketOpen);
    var closeAcross = cards is null ? null : TonightScreen.CloseAcross(onThePage!, nearRows, familyResults, cells);

    // The sector heavyweights' card, drawn after the swing families' on a night they drew the page, whatever the
    // market check read.
    // see: The market check closes every swing family's list together, and the sector heavyweights read none
    // The card drawn by the rule its selector chooses, a registered variant's own book under the band where one is.
    // see: A variant's picks are shown on its card when chosen and its results only under its tests
    var (heavyweightChoice, heavyweightVariant) = RuleScreen.Heavyweights(
        reading.Name,
        true,
        dated,
        register,
        request.Query[RuleScreen.QueryKey(EquityBrief.Core.Families.HeavyweightRule.Name)].FirstOrDefault(),
        RuleWords.Heavyweights(EquityBrief.Core.Families.HeavyweightRule.Live));
    var heavyweights = cards is null
        ? null
        : CardScreen.WithCards(
            TonightScreen.Heavyweights(dated, await read.HeavyweightHoldingsAsync(dated, heavyweightVariant), await read.HeavyweightReadAsync(dated, heavyweightVariant), await read.HeavyweightClosesAsync(dated, heavyweightVariant), cells, register) with
            {
                SweepFoundNone = EquityBrief.Core.Sweep.SweepLine.Drawn(
                    [.. answers.Where(answer => answer.Family == EquityBrief.Core.Families.HeavyweightRule.Name)],
                    TonightScreen.FrozenAt(EquityBrief.Core.Families.HeavyweightRule.Name, register, dated)),
                RuleChoice = heavyweightChoice,
            },
            heavyweightVariant is null ? await read.BoughtCardsAsync(reading.Code, dated) : [],
            cardContext);
    var line = cards is null
        ? null
        : TonightScreen.Line(
            ruleView,
            cards,
            closeAcross!.Count,
            // A trade listed on the night itself is one of its buy points; the open trades are the earlier ones.
            PicksScreen.Cells(picks, dated).Count(trade => trade.Night < dated && trade.Status == EquityBrief.Web.Marks.PickStatus.Open && trade.RepeatOf is null),
            // The members a setup passed: the swing filter's passes, which are the pullback's, and every other family's.
            gates!.Where(gate => gate.Passed).Select(gate => gate.Ticker).Concat(familyResults.Where(result => result.Passed).Select(result => result.Ticker)).Distinct(StringComparer.Ordinal).Count(),
            universe.Count);

    return Results.Content(
        notice + page.TonightRegion(
            marks,
            dated,
            universe.Count,
            TonightScreen.Fired(listings),
            await read.NightDurationAsync(dated),
            rows,
            (await read.WatchedAsync()).Count,
            selected,
            RunScreen.Harness(PhaseReport(builder, checkout)),
            selection?.Ticker,
            records,
            RunScreen.Tracks(TonightScreen.Totals(listings)),
            TonightScreen.Spend(dated, await SpentOn(read, dated), caps),
            TonightScreen.Prose(dated, await read.WrittenOnOrBeforeAsync(dated)),
            TonightScreen.WrittenBeforeTheCorrection(listings),
            RunScreen.Market(market),
            ruleView,
            TonightScreen.Listed(listings),
            held: held,
            close: nearRows,
            stillOpen: gates is null ? null : TonightScreen.StillOpen(dated, gates, picks),
            families: cards,
            line: line,
            closeAcross: closeAcross,
            heavyweights: heavyweights,
            selector: selector),
        "text/html; charset=utf-8");
});

// The watch list page, section 15.16: the names the operator follows on the newest night, each drawn from
// that night's own rows whether or not the list holds it, with what the swing filter said of it.
// see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
app.MapGet("/screens/watch", async (ReadApi read, SinglePageApp page) =>
{
    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";
    var watched = await read.WatchedAsync();

    if (await read.NewestNightAsync() is not { } night)
    {
        return Results.Content(page.WatchRegion(null, [], ReadApi.WatchLimit), "text/html; charset=utf-8");
    }

    var universe = await read.UniverseAsync(index, night);

    return Results.Content(
        page.WatchRegion(
            night,
            TonightScreen.Watched(
                night,
                watched,
                await read.ListingsAsync(night),
                UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
                await read.ClosesToTheNightAsync(night),
                universe,
                await read.GateResultsAsync(night),
                await read.ResearchedAsync()),
            ReadApi.WatchLimit),
        "text/html; charset=utf-8");
});

// A name put on the watch list, or taken off it, by the operator's press. Refused without the page's own
// header, as every press is, and a name that is not a member of the index tonight, or one past the list's
// limit, is refused with the line saying why.
// see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
// see: A pass is started only by a request carrying the name page's own header
app.MapPost(SinglePageApp.WatchPostRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read) =>
{
    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Results.Content(
            "<p class=\"watch-refused\" data-refused=\"header\">nothing was added: the request did not come from a page of this tool</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";
    var members = (await read.UniverseAsync(index, await read.NewestNightAsync())).Select(row => row.Ticker).ToHashSet(StringComparer.Ordinal);
    var written = await read.WatchAsync(ticker, members);

    return Results.Content(
        $"<p class=\"watch-said-line\" data-written=\"{(written.Written ? "true" : "false")}\">{System.Net.WebUtility.HtmlEncode(written.Line)}</p>",
        "text/html; charset=utf-8",
        statusCode: written.Written ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
});

app.MapPost(SinglePageApp.UnwatchPostRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read) =>
{
    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Results.Content(
            "<p class=\"watch-refused\" data-refused=\"header\">nothing was taken out: the request did not come from a page of this tool</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    var written = await read.UnwatchAsync(ticker);

    return Results.Content(
        $"<p class=\"watch-said-line\" data-written=\"{(written.Written ? "true" : "false")}\">{System.Net.WebUtility.HtmlEncode(written.Line)}</p>",
        "text/html; charset=utf-8",
        statusCode: written.Written ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
});

// The account's page and its press: the settings read from their own file under the data root and written whole by
// the press, never into the store, a log or the run log.
// see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
app.MapGet("/screens/account", (SinglePageApp page, StoreLocation store) =>
{
    var held = EquityBrief.Core.Cards.AccountFile.Read(store.DataRoot);

    return Results.Content(
        page.AccountRegion(held?.Size, held?.RiskPercent, held?.PositionCap, EquityBrief.Core.Cards.AccountSettings.ProposedCap),
        "text/html; charset=utf-8");
});

app.MapPost(SinglePageApp.AccountPostRoute, async (HttpRequest request, StoreLocation store) =>
{
    if (!FromAPage(request))
    {
        return Refused("nothing was saved");
    }

    var form = request.HasFormContentType ? await request.ReadFormAsync() : null;

    if (Amount(form?["size"].ToString()) is not { } size || Amount(form?["risk"].ToString()) is not { } risk || Amount(form?["cap"].ToString()) is not { } cap)
    {
        return Said(false, "Nothing was saved: each of the three must be a number.");
    }

    if (EquityBrief.Core.Cards.AccountSettings.Refusal(size, risk, cap) is { } refusal)
    {
        return Said(false, "Nothing was saved: " + refusal);
    }

    EquityBrief.Core.Cards.AccountFile.Write(store.DataRoot, new EquityBrief.Core.Cards.AccountSettings(size, risk, cap));

    return Said(true, "Saved. Each pick's card sizes its plan from these.");
});

// A pick's card's presses: a trade taken from it, one removed before a night has followed it, and an exit recorded.
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
app.MapPost(SinglePageApp.TakenPostRoute + "{index}/{night}/{family}/{ticker}", async (string index, string night, string family, string ticker, HttpRequest request, ReadApi read) =>
{
    if (!FromAPage(request))
    {
        return Refused("nothing was taken");
    }

    if (!DateOnly.TryParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on))
    {
        return Said(false, "Nothing was taken: the card's night could not be read.");
    }

    var form = request.HasFormContentType ? await request.ReadFormAsync() : null;
    var price = form?["price"].ToString() is { Length: > 0 } typed ? Amount(typed) : null;
    var date = form?["date"].ToString() is { Length: > 0 } dated ? Day(dated) : null;

    if ((form?["price"].ToString() is { Length: > 0 } && price is null) || (form?["date"].ToString() is { Length: > 0 } && date is null))
    {
        return Said(false, "Nothing was taken: a fill must be a number and its date a day.");
    }

    var written = await read.TakeAsync(index, on, family, ticker, price, date);

    return Said(written.Written, written.Line);
});

app.MapPost(SinglePageApp.NotTakenPostRoute + "{ticker}/{takenAt}", async (string ticker, string takenAt, HttpRequest request, ReadApi read) =>
{
    if (!FromAPage(request))
    {
        return Refused("nothing was removed");
    }

    var written = await read.NotTakenAsync(ticker, takenAt);

    return Said(written.Written, written.Line);
});

app.MapPost(SinglePageApp.ExitPostRoute + "{ticker}/{takenAt}", async (string ticker, string takenAt, HttpRequest request, ReadApi read) =>
{
    if (!FromAPage(request))
    {
        return Refused("no exit was recorded");
    }

    var form = request.HasFormContentType ? await request.ReadFormAsync() : null;

    if (Amount(form?["price"].ToString()) is not { } price || Day(form?["date"].ToString()) is not { } date)
    {
        return Said(false, "No exit was recorded: its price must be a number and its date a day.");
    }

    var written = await read.ExitAsync(ticker, takenAt, price, date);

    return Said(written.Written, written.Line);
});

// Whether a press came from a page of this tool, by the header every press carries.
static bool FromAPage(HttpRequest request) =>
    string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal);

static IResult Refused(string what) =>
    Results.Content(
        $"<p class=\"card-refused\" data-refused=\"header\">{what}: the request did not come from a page of this tool</p>",
        "text/html; charset=utf-8",
        statusCode: StatusCodes.Status403Forbidden);

static IResult Said(bool written, string line) =>
    Results.Content(
        $"<p class=\"card-said-line\" data-written=\"{(written ? "true" : "false")}\">{System.Net.WebUtility.HtmlEncode(line)}</p>",
        "text/html; charset=utf-8",
        statusCode: written ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);

static decimal? Amount(string? text) =>
    decimal.TryParse(text?.Trim().Replace(",", string.Empty, StringComparison.Ordinal), NumberStyles.Number, CultureInfo.InvariantCulture, out var amount) ? amount : null;

static DateOnly? Day(string? text) =>
    DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;

// The universe screen, section 15.8, read here and composed by the app for the
// reason the name route gives: the composition is in EquityBrief.Web so the
// suite asserts the shipped path rather than a copy of it.
//
// The filters arrive in the query the shell passes through from the hash, so a
// filtered view is a link.
app.MapGet("/screens/universe", async (
    HttpRequest request,
    ReadApi read,
    MarkRenderer marks,
    SinglePageApp page) =>
{
    // The index, from configuration with the same default the worker takes, so
    // the screen and the night are over one universe rather than two, or the S&P 400 or
    // 600 where the link chose one under Universe.
    // see: One universe now, the seam for more built now
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var index = reading == Universes.Large ? builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC" : reading.Code;

    // The index on the newest night the listings hold, which is the night the
    // screen's figures are from.
    var members = await read.UniverseAsync(index, await read.NewestNightAsync());

    // The listing history behind the two right-hand columns and the sector
    // strip's count, over the window section 15.8 states, which the S&P 500's
    // members alone hold.
    var history = new Dictionary<string, IReadOnlyList<ListingRow>>(StringComparer.Ordinal);

    foreach (var member in members)
    {
        history[member.Ticker] = reading == Universes.Large ? await read.ListingsAsync(member.Ticker, UniverseScreen.StripSessions) : [];
    }

    // The night the column counts sessions from, and every name's next dated
    // event in one read rather than five hundred.
    var night = await read.NewestNightAsync();
    var events = (await read.NextEventsAsync(night ?? DateOnly.MinValue))
        .ToDictionary(row => row.Ticker, row => row.EventDate, StringComparer.Ordinal);

    // Every member's swing readings for the night, which the table's four swing columns draw.
    var readings = night is { } readOn
        ? (await read.SwingReadingsAsync(readOn)).ToDictionary(row => row.Ticker, StringComparer.Ordinal)
        : new Dictionary<string, SwingReadingRow>(StringComparer.Ordinal);

    var cells = UniverseScreen.Rows(members, history, events, night, readings);

    var trend = request.Query["trend"].FirstOrDefault();
    var sector = request.Query["sector"].FirstOrDefault();

    // The page, from the hash beside the filters. The filters and the cut are
    // one call, because the order between them is the property: a page taken
    // from the whole table and then filtered is a short page about the wrong
    // names, and nothing on the screen contradicts it.
    var shown = UniverseScreen.Rows(
        cells,
        trend,
        sector,
        int.TryParse(request.Query["page"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked) ? asked : null);

    // The stored closes of the names the page draws and of no other, each drawn as its year line
    // beside its ticker.
    var drawn = UniverseScreen.WithYears(shown.Page, await read.ClosesAsync([.. shown.Page.Select(cell => cell.Ticker)]));

    return Results.Content(
        page.UniverseRegion(
            marks,
            cells,
            UniverseScreen.Sectors(cells),
            trend,
            sector,
            drawn,
            shown.At,
            UniverseScreen.PageSize,
            TonightScreen.WrittenBeforeTheCorrection(history.Values.SelectMany(rows => rows)),
            night,
            reading,
            night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.UniverseRoute) : string.Empty),
        "text/html; charset=utf-8");
});

// The masthead search's list, section 15.13: every current member of the index by its
// ticker, with its company's name and the day its research was written.
app.MapGet("/screens/find", async (ReadApi read, SinglePageApp page) =>
{
    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";
    var names = await read.FindableAsync(index, await read.NewestNightAsync());

    return Results.Content(
        page.FindOptions([.. names.Select(name => new Findable(name.Ticker, name.Name, name.Researched))]),
        "text/html; charset=utf-8");
});

// The Past picks screen, section 15.17: every trade the live list recommended from the swing filter's
// first night, as of the newest night, under the status filter the hash carries.
// see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
app.MapGet("/screens/picks", async (HttpRequest request, ReadApi read, MarkRenderer marks, SinglePageApp page) =>
{
    var night = await read.NewestNightAsync();

    // The index the page reads, chosen under Universe and kept in the link: an S&P 400's or 600's trades and holdings
    // where it names one, and the S&P 500's otherwise.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var selector = night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.PicksRoute) : string.Empty;

    if (reading != Universes.Large)
    {
        var members = night is { } on ? await read.IndexMembersAsync(reading.Code, on) : [];
        var companies = members.ToDictionary(member => member.Ticker, member => member.Company, StringComparer.Ordinal);

        return Results.Content(
            page.IndexPicksRegion(
                marks,
                night,
                reading,
                selector,
                night is { } through ? PicksScreen.IndexTrades(await read.IndexTradesAsync(reading.Code, through), companies) : [],
                night is { } kept ? PicksScreen.IndexHeavyweights(await read.IndexHoldingsAsync(reading.Code, kept)) : []) +
            page.YourTradesRegion(reading.Name, PicksScreen.YourTrades(await read.TakenTradesAsync(), reading.Code)),
            "text/html; charset=utf-8");
    }

    var cells = night is { } asOf
        ? PicksScreen.Cells(await read.PicksAsync(asOf), asOf, TonightScreen.ProvisionalSetups(await read.RegisteredCandidatesAsync(), asOf))
        : [];
    var status = request.Query["status"].FirstOrDefault();

    // The setup the hash names keeps that family's trades, and the counts follow it. The chips are the
    // setups the page has listed a trade under, in the page's order.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    var setup = request.Query["setup"].FirstOrDefault();
    var under = PicksScreen.OfSetup(cells, setup);
    var setups = PicksScreen.Setups(cells);

    // The sector heavyweights' holdings, in percent, beneath the swing trades.
    // see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
    var heavyweights = night is { } held ? PicksScreen.Heavyweights(await read.HeavyweightHoldingsAsync(held)) : [];

    return Results.Content(
        page.PicksRegion(marks, night, PicksScreen.Summary(under), PicksScreen.Filtered(under, status), status, setup, setups, heavyweights, selector) +
        page.YourTradesRegion(reading.Name, PicksScreen.YourTrades(await read.TakenTradesAsync(), reading.Code)),
        "text/html; charset=utf-8");
});

// The sweep's report, section 15.18: the page the newest run wrote beside the store, served as it stands with
// the earlier runs linked above it, and a named run's page under the route. The surface computes none of it;
// where no run has written one, the page says so.
// see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night
app.MapGet(EquityBrief.Core.Sweep.SweepFolder.Route, (StoreLocation store) => SweepReportPage(store, null));
app.MapGet(EquityBrief.Core.Sweep.SweepFolder.Route + "/{run}", (StoreLocation store, string run) => SweepReportPage(store, run));

IResult SweepReportPage(StoreLocation store, string? run)
{
    var root = EquityBrief.Core.Sweep.SweepFolder.Resolve(builder.Configuration[EquityBrief.Core.Sweep.SweepFolder.Key], store.DataRoot);
    var runs = EquityBrief.Core.Sweep.SweepFolder.Runs(root);
    string? report;

    if (run is null)
    {
        report = EquityBrief.Core.Sweep.SweepFolder.NewestReport(root);
    }
    else if (EquityBrief.Core.Sweep.SweepFolder.IsRunName(run) && File.Exists(Path.Combine(root, run, EquityBrief.Core.Sweep.SweepFolder.ReportFile)))
    {
        report = Path.Combine(root, run, EquityBrief.Core.Sweep.SweepFolder.ReportFile);
    }
    else
    {
        return Results.NotFound();
    }

    if (report is null)
    {
        return Results.Content(
            "<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><title>Sweep report</title></head><body><p>No sweep has written its report yet. The sweep writes it when it finishes, or where it stopped.</p></body></html>",
            "text/html; charset=utf-8");
    }

    var links = string.Join(" ", runs.Select(name => $"<a href=\"{EquityBrief.Core.Sweep.SweepFolder.Route}/{name}\">{System.Net.WebUtility.HtmlEncode(name)}</a>"));
    var page = File.ReadAllText(report);
    var nav = $"<nav class=\"runs\" data-runs=\"{runs.Count}\">Runs: {links}</nav>";
    var at = page.IndexOf("<main>", StringComparison.Ordinal);

    return Results.Content(at < 0 ? nav + page : page.Insert(at + "<main>".Length, nav), "text/html; charset=utf-8");
}

// The researched names, section 15.8's researched region on a route of its own.
// The names holding research among the members of the index chosen under Universe: an S&P 400's or 600's current
// members where the link names one, and every other name where it names none, the S&P 500's former members among them.
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
// The setup ledger's page under Universe: the summary the ledger's writers refresh for the index chosen, one family's
// newest settled setups, its first family's where the link names none, and a chosen setup's path, named in the link as
// its stock and its session.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
app.MapGet("/screens/ledger", async (HttpRequest request, ReadApi read, MarkRenderer marks, SinglePageApp page) =>
{
    var night = await read.NewestNightAsync();
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var selector = night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.LedgerRoute) : string.Empty;
    var years = await read.LedgerYearsAsync(reading.Code);
    var asked = request.Query["family"].FirstOrDefault();
    var family = years.Any(year => year.Family == asked) ? asked : years.FirstOrDefault()?.Family;
    var settled = family is null ? [] : await read.LedgerSettledAsync(reading.Code, family, 12);
    EquityBrief.Core.Ledger.LedgerPath? path = null;

    // The chosen setup named as its stock and its session, the last full stop parting the two.
    if (family is not null && request.Query["setup"].FirstOrDefault() is { } chosen && chosen.LastIndexOf('.') is var cut and > 0
        && DateOnly.TryParseExact(chosen[(cut + 1)..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var session))
    {
        path = await read.LedgerPathAsync(reading.Code, family, chosen[..cut], session, night ?? session);
    }

    return Results.Content(page.LedgerRegion(marks, night, reading, selector, years, family, settled, path), "text/html; charset=utf-8");
});

app.MapGet("/screens/loop", async (HttpRequest request, ReadApi read, MarkRenderer marks, SinglePageApp page) =>
{
    var night = await read.NewestNightAsync();
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var selector = night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.LoopRoute) : string.Empty;
    var months = await read.LoopMonthsAsync(reading.Code);
    var asked = request.Query["month"].FirstOrDefault();
    var month = months.Contains(asked ?? string.Empty, StringComparer.Ordinal) ? asked : months.FirstOrDefault();
    var run = month is null ? null : await read.LoopRunAsync(reading.Code, month);
    var proposals = run is null ? [] : await read.LoopProposalsAsync(run);
    var tests = run is null ? [] : await read.LoopTestsAsync(run);
    var findings = run is null ? [] : await read.LoopFindingsAsync(run);

    return Results.Content(page.LoopRegion(marks, night, reading, selector, months, run, proposals, tests, findings), "text/html; charset=utf-8");
});

app.MapGet("/screens/researched", async (HttpRequest request, ReadApi read, SinglePageApp page) =>
{
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var night = await read.NewestNightAsync();
    var wider = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

    foreach (var index in Universes.Offered.Where(choice => choice != Universes.Large))
    {
        wider[index.Code] = night is { } on ? [.. (await read.IndexMembersAsync(index.Code, on)).Select(member => member.Ticker)] : [];
    }

    bool Reads(string ticker) => reading == Universes.Large
        ? !wider.Values.Any(members => members.Contains(ticker))
        : wider[reading.Code].Contains(ticker);

    return Results.Content(
        page.ResearchedRegion(
            [.. (await read.ResearchedAsync()).Where(row => Reads(row.Ticker)).Select(row => new ResearchedCell(row.Ticker, row.Name, row.Sector, row.Written, row.Sections))],
            reading,
            night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.ResearchedRoute) : string.Empty),
        "text/html; charset=utf-8");
});

// The queue, section 15.15, read here and composed by the app. It reads the request
// store and writes nothing: the presses that write it are the two routes above. When each
// request will be written is worked out from the queue, the passes that ran to their end,
// the peak windows the worker's prices state and the instant the page is drawn at, and
// stated in New York's time with UTC beside it.
// see: The queue page states when each request will be written
app.MapGet("/screens/queue", async (ReadApi read, SinglePageApp page, IClock clock) =>
{
    var (rows, times, estimate) = await QueueRead(read, clock);
    var stop = await read.DrainStoppedAsync();

    return Results.Content(
        page.QueueRegion(
        [
            .. rows.Select((row, at) => new QueuedCell(
                row.Ticker,
                row.AskedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                row.AskedFrom,
                row.Lane,
                row.State,
                row.SettledAt?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                row.RunId,
                row.Reason,
                new QueuedTime(
                    times[at].Basis.ToString(),
                    times[at].Starts?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    times[at].Ends?.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    QueueTimes.Words(times[at], row, clock.SessionZone)))),
        ],
        new QueueEstimate(
            estimate.Count,
            estimate.Median is { } median ? QueueTimes.Minutes(median) : null,
            estimate.Longest is { } longest ? QueueTimes.Minutes(longest) : null),
        stop is null
            ? null
            : new QueueStop(
                stop.StartedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                QueueTimes.Stated(stop.StartedAt, clock.SessionZone),
                stop.Error)),
        "text/html; charset=utf-8");
});

// The run page, section 15.10, read here and composed by the app.
//
// `/screens/run` resolves to the newest night the run log holds and
// `/screens/run/<date>` to an earlier one, which is the pair the tonight route
// already answers and which keeps `#/run/<date>` a link.
app.MapGet("/screens/run/{night?}", async (
    string? night,
    string? version,
    string? universe,
    ReadApi read,
    MarkRenderer marks,
    SinglePageApp page,
    SpendCaps caps,
    IClock clock,
    StoreLocation store) =>
{
    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";

    // The newest night that ran rather than the newest that listed, so a night
    // that stopped before its list is the one the page opens on.
    var asOf = night is { Length: > 0 }
        ? DateOnly.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture)
        : await read.RunNightAsync();

    if (asOf is not { } dated)
    {
        return Results.Content(
            "<p class=\"degraded\" data-night=\"none\">no night has been recorded yet</p>",
            "text/html; charset=utf-8");
    }

    // The record starts on the swing filter's first night, and a night before it is not drawn.
    // see: The dated screens open from the swing filter's first night, and no evening before it is drawn
    var first = await read.FirstFilterNightAsync();
    var held = (await read.NightsAsync()).Where(one => first is not { } from || one >= from).ToArray();

    if (first is { } start && dated < start)
    {
        return Results.Content(
            SinglePageApp.BeforeTheRecord(dated, start, "Run evidence", SinglePageApp.RunRoute, SinglePageApp.RunRoute, held),
            "text/html; charset=utf-8");
    }

    // The index the page reads, chosen under Universe and kept in the link: an S&P 400's or 600's own night and setups
    // where it names one, and the S&P 500's page otherwise.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    var reading = Universes.Of(universe);
    var selector = Cards.Universe(reading, await read.MembersByIndexAsync(dated), SinglePageApp.RunRoute + dated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

    if (reading != Universes.Large)
    {
        var indexNight = await read.IndexNightAsync(reading.Code, dated);
        var indexPicks = await read.IndexPicksAsync(reading.Code, dated);
        var indexTrades = await read.IndexTradesAsync(reading.Code, dated);
        var indexHoldings = await read.IndexHoldingsAsync(reading.Code, dated);
        var indexRegister = await read.RegisteredCandidatesAsync();

        // Each registered rule's record, from the index's first freeze on, and its family's setup row live beside it.
        var indexRecords = TonightScreen.IndexRecordRows(reading, indexRegister, await read.IndexRuleTradesAsync(reading.Code, dated), dated);

        return Results.Content(
            page.IndexRunRegion(
                marks,
                dated,
                reading,
                selector,
                await read.IndexNightsAsync(reading.Code),
                indexNight is null ? null : TonightScreen.IndexRun(indexNight, indexPicks, await read.IndexResultsAsync(reading.Code, dated), indexTrades, indexHoldings),
                TonightScreen.WithIndexFreezes(TonightScreen.IndexFamilyRun(indexPicks, indexTrades, indexHoldings), reading, indexRegister, indexRecords, indexNight, dated),
                indexRecords,
                RuleScreen.Flagged(await read.RuleNightsAsync(reading.Code, dated), dated)),
            "text/html; charset=utf-8");
    }

    // The paid calls with a recorded cost, which the operational header draws and the calibration's
    // spend cap line counts the passes of.
    var priced = RunScreen.Priced(
        await read.PaidCallSpendsAsync(),
        QueuePricing(builder.Configuration) is { } prices ? (await read.PaidCallAnswersAsync()).Count(prices.IsPeak) : 0);

    // The reason record is over every night the store holds and not over this
    // one. A record is a property of the reason across every name it ever fired
    // for, and a record over one evening would be a statement about that
    // evening wearing the clothes of a verdict.
    var everyListing = await read.ListingsAsync();
    var returns = await read.ForwardReturnsAsync();
    var log = await read.RunLogAsync(dated);

    // The store's copies as their own rows state them, read once for the checklist's two items and the line beneath it.
    var storeCopy = RunScreen.StoreCopy(await read.StoreBackupsAsync(), store.DataRoot, clock.UtcNow);
    var stages = RunScreen.Stages(log);
    var records = RunScreen.Records(everyListing, RunScreen.Resolved(returns));
    var flips = await read.LabelReturnsAsync();

    // How the night went, from its own run log rows, and the market and the funnel it drew, each read
    // once and handed to both the pictures at the top and the tables folded beneath them.
    // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
    var how = NightFrom(log, dated, clock, store);
    var funnel = RunScreen.Funnel(await read.GateResultsAsync(dated), dated, await read.ListRuleAsync(dated));
    var market = RunScreen.Market(await read.MarketReadingAsync(dated));
    var picture = RunScreen.Pictured(
        dated,
        market,
        funnel is { } ran ? await read.FilterSettingsAsync(ran.Version) : null,
        await read.BreadthLineAsync(dated, MarkRenderer.BreadthLineSessions));

    // The versions running beside the live list and tonight's picks beside the one the link names, read
    // off the shadow column: picks only, a version's outcomes waiting for its look.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    var register = await read.RegisteredCandidatesAsync();
    var edge = EdgeScreen.Edge(register, await read.CandidateNightsAsync(), await read.CandidateSetupsAsync(), dated, clock.UtcNow);
    var shadowPicks = await read.ShadowPicksAsync(dated);
    var versions = RunScreen.Versions(register, edge, shadowPicks);
    var compare = RunScreen.Compare(dated, versions, register, version, shadowPicks, await read.EvaluatedOnAsync(dated), await read.GateResultsAsync(dated));

    // The research region's seven nights, and each report written over them read section by section.
    // see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports, and no trial's drafts
    var week = await WeekOf(read, dated);

    // The news labeller's own rows, for its line on the night and its count toward the nights its limits settle from.
    var labellerRuns = await read.LabellerRunsAsync();
    var reports = RunScreen.Reports(await read.ReportRowsAsync(), await read.ReportVersionsAsync(), week.Count > 0 ? week[0].Night : dated, dated);

    return Results.Content(
        page.RunRegion(
            marks,
            dated,
            stages,
            RunScreen.Failed(stages),
            records,
            RunScreen.Tracks(records),
            RunScreen.BaseRates(returns),
            RunScreen.Nights(everyListing),
            await read.StaleNamesAsync(index, dated),
            RunScreen.Refused(await read.RefusedDocumentsAsync(dated)),
            RunScreen.FellBack(await read.FellBackAsync(dated)),
            RunScreen.Queue(await read.QueueRowsAsync(), dated, Traded),
            RunScreen.Harness(PhaseReport(builder, checkout)),
            RunScreen.Shadow(
                await read.RegisteredCandidatesAsync(),
                await read.CandidateNightsAsync(),
                await read.CandidateSetupsAsync(),
                dated,
                clock.UtcNow),
            priced,
            TonightScreen.WrittenBeforeTheCorrection(await read.ListingsAsync(dated)),
            RunScreen.Orders(everyListing, dated),
            RunScreen.Candidates(
                await read.RegisteredCandidatesAsync(),
                await read.CandidateNightsAsync(),
                await read.CandidateSetupsAsync(),
                dated,
                clock.UtcNow),
            RunScreen.TrendVersions(
                await read.OpenVersionsAsync(LadderRules.TrendRule),
                await read.VersionLabelsAsync(LadderRules.TrendRule, dated),
                await read.LiveLabelsAsync(dated),
                await read.VersionBlocksAsync(LadderRules.TrendRule),
                flips.Returns,
                flips.Nights,
                dated),
            RunScreen.Calibration(
                await read.GateNightsAsync(),
                await read.MarketRatiosAsync(),
                RunScreen.Firings(everyListing),
                await read.OpenFilterVersionAsync(),
                dated),
            market,
            funnel,
            RunScreen.Triggers(await read.TriggerReadsAsync(), priced, NewsScreen.Trigger(labellerRuns)),
            RunScreen.Proposal(
                await read.LatestShapeProposalAsync(),
                await read.RegisteredCandidatesAsync(),
                await read.CandidateNightsAsync(),
                await read.CandidateSetupsAsync(),
                everyListing,
                clock.UtcNow),
            RunScreen.Overlap(everyListing, dated),
            edge,
            await read.OpenFilterVersionAsync() is { } open
                ? EdgeScreen.NearMisses(open, await read.NearMissRowsAsync(open), dated)
                : EdgeScreen.NearMisses(null, [], dated),
            held: held,
            how: how,
            picture: picture,
            // The live rule's own trades: the pullback's, whatever the other setups listed.
            trades: PicksScreen.Summary(PicksScreen.OfSetup(PicksScreen.Cells(await read.PicksAsync(dated), dated), EquityBrief.Core.Families.SetupFamilies.Pullback)),
            fresh: RunScreen.Freshness(everyListing, dated, first),
            research: new ResearchPicture(
                TonightScreen.Spend(dated, await SpentOn(read, dated), caps),
                RunScreen.Research(week),
                NewsScreen.Line(dated, NewsScreen.RunFor(labellerRuns, dated))),
            worries: RunScreen.Worries(
                await read.StaleNamesAsync(index, dated),
                how,
                RunScreen.Refused(await read.RefusedDocumentsAsync(dated)),
                RunScreen.FellBack(await read.FellBackAsync(dated)),
                log,
                [.. EquityBrief.Core.Providers.ModelProfiles.Jobs.Select(job => Profile(builder.Configuration, job)).OfType<EquityBrief.Core.Providers.ModelProfile>()],
                storeCopy,
                RuleScreen.Flagged(await read.RuleNightsAsync(index, dated), dated)),
            background: versions,
            compare: compare,
            checkpoints: RunScreen.Checkpoints(edge),
            reports: reports,
            // The setups the page is drawn from, on a night the families drew its list, and each registered rule
            // of a new setup read over its own trades.
            setupFamilies: await read.FamilyNightAsync(dated) is null
                ? null
                : TonightScreen.FamilyRun(
                    dated,
                    await read.FamilyPicksAsync(dated),
                    PicksScreen.Cells(await read.PicksAsync(dated), dated),
                    await read.RegisteredCandidatesAsync(),
                    await read.FamilyTradesAsync(dated),
                    await read.FamilyReplaysAsync()),
            familyRecords: TonightScreen.FamilyRecordRows(await read.RegisteredCandidatesAsync(), await read.FamilyTradesAsync(dated), dated, await read.FamilyReplaysAsync()),
            storeCopy: storeCopy,
            selector: selector),
        "text/html; charset=utf-8");
});

// One run log row for the surface coming up, which is the grain SCHEMA declares
// and what section 15.10's run page reads. Written after the host is built so a
// store that cannot be opened fails the start rather than a request.
await app.Services.GetRequiredService<ReadApi>().RecordStartAsync(
    FormattableString.Invariant($"read-api-{app.Services.GetRequiredService<IClock>().UtcNow:yyyyMMddTHHmmssZ}"),
    "the read surface started");

app.Run();

// So the suite can reach the host's composition. A test project referencing a
// top-level program needs the generated class to be visible.
public partial class Program;
