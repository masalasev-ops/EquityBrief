using System.Globalization;
using System.Text.Json;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Quotes;
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
builder.Services.AddSingleton<RouteTimes>();

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

// Every request's route and its server time, from 19.11, kept in memory for the Run page and written to no store, timed
// around everything after it so a page's time is the whole of what the server did for it.
// see: Each request's route and server time is kept in memory for the read surface's life and drawn on the Run page, and no store holds it
app.Use(async (context, next) =>
{
    var watch = System.Diagnostics.Stopwatch.StartNew();

    await next();

    context.RequestServices.GetRequiredService<RouteTimes>().Record(RouteTimes.Of(context), watch.Elapsed.TotalMilliseconds);
});

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

// A stock's chart page, section 15.22, which every link that opens a stock opens first: the name page's masthead, the
// chart at the interval the link names and the grid of stored figures, each read as tonight's page reads it so the two
// pages draw one figure alike. Only the interval shown is read, so the daily chart reads no pulled bar.
// see: Every link that opens a stock opens its chart page first, and the chart page links to its report with the controls that write it again
app.MapGet("/screens/chart/{ticker}", async (string ticker, string? interval, string? universe, ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock, StoreLocation store) =>
{
    var shown = EquityBrief.Core.Chart.ChartSeries.Read(interval);
    var today = clock.SessionDateAt(clock.UtcNow);
    var session = RegularSession.On(today, clock.SessionZone);
    var bars = await read.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue);
    var night = await read.NewestNightAsync();
    var through = night ?? (bars.Count > 0 ? bars[^1].SessionDate : today);
    var indexCode = await read.IndexOfAsync(ticker, night ?? today);
    var member = indexCode is null ? null : (await read.UniverseAsync(indexCode, night)).FirstOrDefault(row => string.Equals(row.Ticker, ticker, StringComparison.Ordinal));
    var quote = session is { } open ? await read.NewestQuoteAsync(ticker, open.Open, open.Close) : null;
    var quoteSession = session is { } window ? new QuoteSession(window.Open, window.Close, RegularSession.IsOpen(clock.UtcNow, clock.SessionZone)) : null;
    var live = SinglePageApp.QuoteDrawn(quote, quoteSession, null, bars.Count > 0 ? bars[^1].SessionDate : null);
    var reportInputs = await read.ReportInputsAsync(ticker, null, through);
    var fundamentals = await read.FundamentalsAsync(ticker);
    var reactions = await read.ReactionsAsync(ticker);
    var priceDrawn = live?.Price ?? (bars.Count > 0 ? bars[^1].Close : null);
    var indicators = await read.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue);
    IReadOnlyList<DecisionCardRow> cards = night is { } listed ? await CardsOfTheName(read, ticker, listed) : [];
    var report = NameScreen.Report(
        ticker,
        reportInputs,
        fundamentals,
        await read.FundamentalReadingAsync(ticker),
        priceDrawn,
        await read.ForwardRateAsync(ticker, null),
        bars,
        night ?? (bars.Count > 0 ? bars[^1].SessionDate : DateOnly.MinValue));

    var view = ChartScreen.View(new ChartInputs(
        ticker,
        shown,
        bars,
        indicators,
        await read.ChartAveragesAsync(ticker),
        shown != EquityBrief.Core.Chart.ChartInterval.Daily && bars.Count > 0 ? await read.PulledSeriesAsync(ticker, bars[0].SessionDate) : null,
        await read.LevelsAsync(ticker),
        cards,
        NameScreen.Events(bars, reactions, reportInputs.Dividends, await read.RawClosesAsync(ticker)),
        member,
        Universes.ByCode(indexCode ?? string.Empty)?.Name,
        fundamentals,
        await read.FiledFactsAsync(ticker, through.AddDays(-EquityBrief.Core.Report.FiguresChecked.DaysRead)),
        report,
        await read.YearExtremesAsync(ticker),
        await read.LongViewAsync(ticker),
        NameScreen.PickedPeers(ticker, await read.PeerReadingsAsync()),
        await read.NextEventAsync(ticker, bars.Count > 0 ? bars[^1].SessionDate : DateOnly.MinValue),
        member?.Researched,
        await read.ProviderSharesAsync(ticker),
        reactions,
        priceDrawn));

    // From 20.13 the analysts as Yahoo and TipRanks last answered for the stock, the month's TipRanks calls, and whether
    // the page asks Yahoo again, which it does where no answer of this New York day is stored.
    // see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
    if (view.Graphs is { } graphs)
    {
        var now = clock.UtcNow;
        var yahoo = await read.NewestAnalystsAsync(ticker);

        view = view with
        {
            Graphs = graphs with
            {
                Yahoo = yahoo,
                AsksYahoo = indexCode is not null && (yahoo is null || yahoo.AskedAt < EquityBrief.Core.Analysts.AnalystLimits.DayOf(now, clock.SessionZone).From),
                TipRanks = await read.NewestRatingsAsync(ticker),
                Calls = await read.TipRanksCallsAsync(),
                Now = now,
            },
        };
    }

    // A written report's standing with the controls that write it again, read as the name page reads them, the day its
    // industry section was written read off its theme as the name page reads it.
    if (view.ReportWritten is { } written)
    {
        view = view with { Standing = NameScreen.Standing(await read.StalenessAsync(ticker), NameScreen.Pass(await read.NewestPassAsync(ticker)), await SpendNow(read, caps, clock), await read.PaidCallSpendsAsync(), today, written, (await read.ThemeCycleAsync(ticker))?.AsOf) };
    }

    // From 22.4 the brief read atop the page, on a night the store holds, and from 22.8 the fundamentals card beneath it,
    // its scorecard the newest the nights stored on or before it.
    // see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
    // see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
    if (night is { } briefed)
    {
        var score = await read.FundamentalScoreAsync(ticker, briefed);

        view = view with
        {
            Brief = await BriefAsync(read, store, ticker, indexCode, briefed, member, indicators, report, reactions, cards, score),
            Fundamentals = FundamentalsScreen.Of(
                score,
                Universes.ByCode(indexCode ?? string.Empty)?.Name,
                indexCode is { } code ? await read.OwnLedgerReadingsAsync(code, briefed, ticker) : null,
                report.Analysts,
                report.Insiders),
            Grid = ChartScreen.WithFiledMultiple(view.Grid, score),
        };
    }

    var mast = new NameMast(member?.Name, member?.Sector, member?.Industry, NameScreen.DayChange(ticker, bars), Universes.ByCode(indexCode ?? string.Empty)?.Name);
    var drawn = bars.Select(bar => new ChartBar(bar.SessionDate, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume)).ToArray();

    return Results.Content(
        page.ChartRegion(
            marks,
            view,
            drawn,
            mast,
            quote,
            quoteSession,
            NameScreen.Suspect((await read.SuspectSeriesAsync()).FirstOrDefault(row => string.Equals(row.Ticker, ticker, StringComparison.Ordinal))),
            (await read.WatchedAsync()).Any(row => string.Equals(row.Ticker, ticker, StringComparison.Ordinal)),
            universe),
        "text/html; charset=utf-8");
});

// One name's report as a file to hand to someone, section 15.4's second surface: the name
// screen's region composed by the same code from the same reads, less the research controls
// and the pause, which are the application asking the operator something rather than the
// report. Offered as a download, so where the file is kept is the operator's choice.
// see: A single report is exported as one self-contained file in the page's design, opening light with a dark toggle and stamped with the price it was exported at
app.MapGet(ReportExporter.Route + "{ticker}", async (string ticker, ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock, ReportExporter exporter) =>
{
    var name = await NameAsync(read, marks, page, caps, clock, ticker, builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC", export: true);

    return Results.File(
        System.Text.Encoding.UTF8.GetBytes(exporter.Document(ticker, name.AsOf, name.Region, clock.UtcNow)),
        "text/html; charset=utf-8",
        ReportExporter.FileName(ticker, name.AsOf));
});

// The name screen's region, read here and composed by the app, for the page and for the file.
static async Task<(string Region, DateOnly? AsOf)> NameAsync(ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock, string ticker, string index, bool export, DateOnly? on = null, CardContext? cardContext = null)
{
    var session = RegularSession.On(clock.SessionDateAt(clock.UtcNow), clock.SessionZone);
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

    // The name's own index, whose members its masthead names it from and its group is read within: an S&P 400 or 600
    // member is no member of the index the page's list is drawn from, and its group was read within its own.
    var own = await read.IndexOfAsync(ticker, night ?? clock.SessionDateAt(clock.UtcNow)) ?? index;
    var members = own == index ? universe : await read.UniverseAsync(own, night);

    // The newest night, which every move's group was read on whatever night the page is for, and
    // the index on it, which says whether the name was a member when its groups were read.
    var groupsReadOn = on is null ? night : await read.NewestNightAsync();
    var membersThen = groupsReadOn == night ? members : await read.UniverseAsync(own, groupsReadOn);

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

    // The year's high and low among the stored bars up to the night, each with its
    // session, which the fact strip states beside the close. A name holding no bar
    // has none, and the strip says so rather than drawing a blank.
    var year = await read.YearExtremesAsync(ticker, on);

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

    // The stored stories of the thirty days before the night with their labels, which the news region draws and each
    // move's cause reads.
    var stories = night is { } storiedOn ? await read.NewsAsync(ticker, storiedOn) : [];

    // What the typical earnings move, the long view, the chart's marks and the moves' causes read. The typical move and
    // the long view are the newest night's alone, so a page for an earlier night draws neither.
    var historyInputs = new HistoryInputs(
        on is null ? await read.EarningsMoveAsync(ticker) : null,
        on is null ? await read.LongViewAsync(ticker) : null,
        bars.Count > 0 ? await read.FiledBetweenAsync(ticker, bars[0].SessionDate, bars[^1].SessionDate) : [],
        await read.RawClosesAsync(ticker, on),
        stories,
        clock.SessionZone,
        Newest: on is null);

    // The chart of what is behind the stock, from the readings its index's night stored and, where a family's card lists
    // it on the night, that family's learned score on the index.
    // see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
    var behindNight = night ?? (bars.Count > 0 ? bars[^1].SessionDate : (DateOnly?)null);
    BehindItView? behind = null;

    if (behindNight is { } behindOn && await read.IndexOfAsync(ticker, behindOn) is { } behindCode && Universes.ByCode(behindCode) is { } behindIndex)
    {
        var listedBy = (await CardsOfTheName(read, ticker, behindOn)).FirstOrDefault()?.Family;
        var (behindModel, behindPassed, behindUnread) = listedBy is null ? (null, false, null) : await read.RidgeScoreAsync(behindCode, listedBy, behindOn);

        behind = BehindScreen.Of(ticker, behindIndex.Name, await read.LedgerReadingsAsync(behindCode, behindOn), behindModel, behindPassed, behindUnread);
    }

    var region = NameScreen.Region(
        page, marks, ticker, bars, indicators, levels, profile, ladder, nextEvent, moves,
        fundamentals,
        year,
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
        // The membership row the masthead names the company, its sector and industry from, in the name's own index.
        members.FirstOrDefault(row => string.Equals(row.Ticker, ticker, StringComparison.Ordinal)),
        on,
        members,
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
            ? NewsScreen.Build(ticker, storied, stories, gate is not null && gate.SessionDate == storied && gate.Passed, NewsScreen.RunFor(await read.LabellerRunsAsync(), storied))
            : null,
        // The name's rows on the page's list for the night, where the setup families drew it, and the short-term dip's on
        // the name's own index beside them.
        familyPicks: night is { } drawnOn ? await PicksOfTheName(read, ticker, drawnOn) : null,
        // The sector heavyweights' holdings as of the night, which say whether they hold the name.
        heavyweights: night is { } heldOn ? await read.HeavyweightHoldingsAsync(heldOn) : null,
        // The name's member readings for the page's night, or its newest where the page is tonight's, and its company's
        // rating counts as its newest fetch on or before that night filed them.
        memberReading: await read.MemberReadingAsync(ticker, on),
        ratings: await read.RatingsAsync(ticker, on),
        decisionCards: night is { } cardsOn ? await CardsOfTheName(read, ticker, cardsOn) : null,
        // The account and the taken trades on tonight's page alone: an export and an earlier night's page draw neither.
        cardContext: export ? null : cardContext,
        warmed: await read.ChartAveragesAsync(ticker),
        // The regular session of the day the page is opened on and the newest quote stored inside it, which tonight's
        // page draws in its masthead while the session is open; an earlier night's page and the file draw the close.
        quote: on is null && session is { } open ? await read.NewestQuoteAsync(ticker, open.Open, open.Close) : null,
        quoteSession: session is { } window ? new QuoteSession(window.Open, window.Close, RegularSession.IsOpen(clock.UtcNow, clock.SessionZone)) : null,
        tileQuarters: await read.TileQuartersAsync(ticker, on),
        forwardRate: await read.ForwardRateAsync(ticker, on),
        indexCode: await read.IndexOfAsync(ticker, night ?? clock.SessionDateAt(clock.UtcNow)),
        asksForQuotes: !export && on is null,
        // What the latest quarter, the margins, the analysts, the dividend and the valuation read, as of the page's night.
        reportInputs: await read.ReportInputsAsync(ticker, on, night ?? (bars.Count > 0 ? bars[^1].SessionDate : clock.SessionDateAt(clock.UtcNow))),
        historyInputs: historyInputs,
        // The filer's facts as first filed over the year before the page's night, which the sources check the latest
        // quarter's lines against.
        filedFacts: await read.FiledFactsAsync(ticker, (night ?? clock.SessionDateAt(clock.UtcNow)).AddDays(-EquityBrief.Core.Report.FiguresChecked.DaysRead), on),
        behind: behind,
        // From 22.8 the fundamentals scorecard the newest night on or before the page's stored, and the stock's own
        // readings its index's night stored, which the card in the business section is written from.
        // see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
        score: behindNight is { } scoredOn ? await read.FundamentalScoreAsync(ticker, scoredOn) : null,
        scoreReadings: behindNight is { } readOn && await read.IndexOfAsync(ticker, readOn) is { } scoreIndex ? await read.OwnLedgerReadingsAsync(scoreIndex, readOn, ticker) : null,
        // From 22.13 where the Fundamentals family bought the name on the night, keeps a trade in it or flagged it.
        improving: night is { } improvedOn && await read.IndexOfAsync(ticker, improvedOn) is { } improvingIndex
            ? TonightScreen.OnTheFundamentalsCard(ticker, improvedOn, await read.ImprovingResultsAsync(improvingIndex, improvedOn), await read.ImprovingTradesAsync(improvingIndex, improvedOn))
            : null);

    return (region, bars.Count > 0 ? bars[^1].SessionDate : null);
}

// The settings a filter version holds, which a gate result stored under it was decided by, and the proposed
// settings for a result stored under no opened version.
static async Task<EquityBrief.Core.Filter.FilterSettings> FilterSettingsOf(ReadApi read, string version) =>
    await read.FilterSettingsAsync(version) is { } json
        ? EquityBrief.Core.Filter.FilterSettings.Read(json)
        : EquityBrief.Core.Filter.FilterSettings.Proposed;

// A name's rows on the page's list for a night: the S&P 500's own families', the families its own index's step drew,
// and the short-term dip's on that index, its label on the row of the family it gave way to; from 22.13 none of the
// fundamentals-first family's and its label on no row, its rule running out of sight.
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
// see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
static async Task<IReadOnlyList<FamilyPickRow>> PicksOfTheName(ReadApi read, string ticker, DateOnly night)
{
    var index = await read.IndexOfAsync(ticker, night);

    return
    [
        .. TonightScreen.WithDipPicks(
                [.. await read.FamilyPicksAsync(night), .. index is null ? [] : await read.IndexPicksAsync(index, night)],
                index is null ? [] : await read.DipPicksAsync(index, night),
                ticker)
            .Where(pick => pick.Family != EquityBrief.Core.Families.FundamentalsRule.Name)
            .Select(pick => pick with { Also = [.. pick.Also.Where(name => name != EquityBrief.Core.Families.FundamentalsRule.Name)] }),
    ];
}

// A stock's cards on a night as its page and its chart page draw them: from 22.13 none of the fundamentals-first family's,
// its rule running out of sight.
// see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
static async Task<IReadOnlyList<DecisionCardRow>> CardsOfTheName(ReadApi read, string ticker, DateOnly night) =>
    [.. (await read.DecisionCardsOfAsync(ticker, night)).Where(card => card.Family != EquityBrief.Core.Families.FundamentalsRule.Name)];

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
// see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
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

// A name page's ask for its delayed quote, which its script makes at most every five minutes while the page is shown in
// the regular session: refused without the page's own header and outside the session, writing nothing either way. An
// ask inside the interval of the name's newest writes nothing and starts nothing, and otherwise the request is written and
// the quote job started as a process of its own, as a report press starts the drain.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
app.MapPost(SinglePageApp.QuoteRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read, IClock clock, IDrainLauncher launcher) =>
{
    var named = ticker.ToUpperInvariant();

    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Results.Content(
            "<p class=\"quote-refused\" data-refused=\"header\">no quote was asked: the request did not come from the name page</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    if (!RegularSession.IsOpen(clock.UtcNow, clock.SessionZone))
    {
        return Results.Content(
            "<p class=\"quote-refused\" data-refused=\"closed\">no quote was asked: the regular session is closed, so the page draws the last close</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status409Conflict);
    }

    var asked = await read.AskQuoteAsync(named);
    var started = asked.Written ? launcher.StartTheQuote(named, asked.AskedAt) : null;
    var line = started is null ? asked.Line : asked.Line + " " + started.Line;

    return Results.Content(
        $"<p class=\"quote-asked\" data-asked=\"{(asked.Written ? "true" : "false")}\" data-started=\"{(started?.Started == true ? "true" : "false")}\" data-asked-at=\"{asked.AskedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}\">{System.Net.WebUtility.HtmlEncode(line)}</p>",
        "text/html; charset=utf-8",
        statusCode: asked.Written ? StatusCodes.Status202Accepted : StatusCodes.Status200OK);
});

// The newest quote stored for the name inside today's session, its price as the masthead draws it, the line beneath it
// and each band's distance at its price as the levels draw it, which the page's script swaps in. Outside the session, or
// where none is stored, the reply says there is none and the page keeps the close.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
app.MapGet(SinglePageApp.QuoteRoute + "{ticker}", async (string ticker, ReadApi read, MarkRenderer marks, IClock clock) =>
{
    var now = clock.UtcNow;
    var named = ticker.ToUpperInvariant();

    if (RegularSession.On(clock.SessionDateAt(now), clock.SessionZone) is not { } session
        || await read.NewestQuoteAsync(named, session.Open, session.Close) is not { } quote)
    {
        return Results.Json(new { live = false });
    }

    // Once the session has closed, its last quote where the bars hold no close of the session yet, marked closed, which
    // the page's script draws once and asks no more.
    if (!RegularSession.IsOpen(now, clock.SessionZone))
    {
        var window = new QuoteSession(session.Open, session.Close, false);

        return (await read.BarsAsync(named, window.Day, DateOnly.MaxValue)).Count > 0
            ? Results.Json(new { live = false })
            : Results.Json(new
            {
                live = false,
                closed = true,
                askedAt = quote.AskedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                price = SinglePageApp.PriceNow([], null, quote, closed: true),
                asOf = SinglePageApp.QuoteAsOf(quote, quote.Night, closed: true),
            });
    }

    // The tiles at the quote's price, worked by the functions the page's own tiles are.
    // see: Four tiles under the headline are worked by code from stored figures at the price the page draws
    var quarters = await read.TileQuartersAsync(named, null);
    var year = await read.YearExtremesAsync(named, null);
    var today = clock.SessionDateAt(now);
    var inputs = await read.ReportInputsAsync(named, null, today);
    var forwardRate = await read.ForwardRateAsync(named, null);
    var tiles = new TilesView(
        EquityBrief.Core.Tiles.NameTiles.Earnings(quarters),
        EquityBrief.Core.Tiles.NameTiles.Growth(inputs.Trend, inputs.TrendFetchedOn, quarters),
        EquityBrief.Core.Report.DividendSafety.TileYield(forwardRate, inputs.Dividends, quote.Price, inputs.TenYears, today),
        year is { } extremes ? EquityBrief.Core.Tiles.NameTiles.High(quote.Price, extremes.High, extremes.HighOn, extremes.Low, extremes.LowOn) : null,
        quote.Price,
        Live: true,
        PaysNone: EquityBrief.Core.Report.DividendSafety.PaysNone(forwardRate, inputs.Dividends, today));

    return Results.Json(new
    {
        live = true,
        askedAt = quote.AskedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        price = SinglePageApp.PriceNow([], null, quote),
        asOf = SinglePageApp.QuoteAsOf(quote, quote.Night),
        tiles = marks.LeadTiles(tiles),
        // The price as the ladder figure writes it, and each band's distance in the level table's words and the figure's,
        // on the side of the quote's price the band stands.
        // see: Distances are stated as typical days' moves
        priceText = Figures.Price(quote.Price),
        distances = quote.Distances.Select(band => new
        {
            low = band.Low.ToString(CultureInfo.InvariantCulture),
            high = band.High.ToString(CultureInfo.InvariantCulture),
            away = band.Days is { } days ? days.ToString("0.0", CultureInfo.InvariantCulture) + " typical days" : "not measured",
            ladder = MarkRenderer.Away(band.Days, band.Low > quote.Price),
            value = band.Days is { } held ? held.ToString(CultureInfo.InvariantCulture) : "none",
        }),
    });
});

// A chart page's ask for its stock's analysts, which its script makes as the page opens where no answer of the New York
// day is stored: refused without the page's own header and for a stock no index the night reads holds, writing nothing
// either way. An answer of the day or an ask inside the minutes between two asks writes nothing and starts nothing, and
// otherwise the ask is written and the analyst job started as a process of its own, as a name page's ask starts the quote
// job.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
app.MapPost(SinglePageApp.AnalystsRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read, IClock clock, IDrainLauncher launcher) =>
{
    var named = ticker.ToUpperInvariant();

    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Results.Content(
            "<p class=\"analysts-refused\" data-refused=\"header\">nothing was asked: the request did not come from the chart page</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    if (await read.IndexOfAsync(named, clock.SessionDateAt(clock.UtcNow)) is null)
    {
        return Results.Content(
            $"<p class=\"analysts-refused\" data-refused=\"membership\">nothing was asked: {System.Net.WebUtility.HtmlEncode(named)} is in no index the night reads</p>",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status404NotFound);
    }

    var asked = await read.AskAnalystsAsync(named);
    var started = asked.Written ? launcher.StartTheAnalysts(named, asked.AskedAt) : null;
    var line = started is null ? asked.Line : asked.Line + " " + started.Line;

    return Results.Content(
        $"<p class=\"analysts-asked\" data-asked=\"{(asked.Written ? "true" : "false")}\" data-started=\"{(started?.Started == true ? "true" : "false")}\">{System.Net.WebUtility.HtmlEncode(line)}</p>",
        "text/html; charset=utf-8",
        statusCode: asked.Written ? StatusCodes.Status202Accepted : StatusCodes.Status200OK);
});

// The newest answer Yahoo gave for the stock drawn as the chart page's analysts section, which the page's script swaps
// in once it is newer than the one drawn, at the price the page draws; and the newest run of the analyst job for the
// stock, which the page states where it stored nothing.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
app.MapGet(SinglePageApp.AnalystsRoute + "{ticker}", async (string ticker, ReadApi read, MarkRenderer marks, IClock clock) =>
{
    var named = ticker.ToUpperInvariant();
    var run = await read.NewestAnalystRunAsync(EquityBrief.Core.Analysts.AnalystLimits.AnalystsStage, named);
    var ran = run is { } last ? new { startedAt = last.StartedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), outcome = last.Outcome, detail = last.Detail } : null;

    if (await read.NewestAnalystsAsync(named) is not { } yahoo)
    {
        return Results.Json(new { answered = false, run = ran });
    }

    var now = clock.UtcNow;
    var today = clock.SessionDateAt(now);
    var quote = RegularSession.On(today, clock.SessionZone) is { } session && RegularSession.IsOpen(now, clock.SessionZone) ? await read.NewestQuoteAsync(named, session.Open, session.Close) : null;
    var price = quote?.Price ?? (await read.BarsAsync(named, today.AddDays(-14), today)).LastOrDefault()?.Close;
    var graphs = new ChartGraphs([], null, [], string.Empty, null, price, yahoo);

    return Results.Json(new
    {
        answered = true,
        answeredAt = yahoo.AskedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        html = SinglePageApp.AnalystsSection(marks, named, graphs),
        run = ran,
    });
});

// A chart page's press asking TipRanks for its stock's ratings: refused without the page's own header and for a stock no
// index the night reads holds, writing nothing either way. An answer TipRanks gave for the stock in the 15 days before or a
// spent month writes nothing and starts nothing, saying which, and otherwise the ask is written and the ratings job started
// as a process of its own.
// see: TipRanks' analyst ratings are asked only on a press and never past the month's limit, a stock again only 15 days after its last answer
app.MapPost(SinglePageApp.RatingsRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read, IClock clock, IDrainLauncher launcher) =>
{
    var named = ticker.ToUpperInvariant();

    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Results.Content(
            "no call was made: the request did not come from the chart page",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    if (await read.IndexOfAsync(named, clock.SessionDateAt(clock.UtcNow)) is null)
    {
        return Results.Content(
            $"no call was made: {System.Net.WebUtility.HtmlEncode(named)} is in no index the night reads",
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status404NotFound);
    }

    var asked = await read.AskRatingsAsync(named);
    var started = asked.Written ? launcher.StartTheRatings(named, asked.AskedAt) : null;
    var line = started is null ? asked.Line : asked.Line + " " + started.Line;

    return Results.Content(
        System.Net.WebUtility.HtmlEncode(line),
        "text/html; charset=utf-8",
        statusCode: asked.Written && started?.Started == true ? StatusCodes.Status202Accepted : asked.Written ? StatusCodes.Status200OK : StatusCodes.Status409Conflict);
});

// The newest answer TipRanks gave for the stock drawn as the chart page's ratings section with the month's calls used
// and left, which the page's script swaps in once it is newer than its press; and the newest run of the ratings job for
// the stock, which the page states where it stored nothing.
// see: TipRanks' analyst ratings are asked only on a press and never past the month's limit, a stock again only 15 days after its last answer
app.MapGet(SinglePageApp.RatingsRoute + "{ticker}", async (string ticker, ReadApi read, MarkRenderer marks, IClock clock) =>
{
    var named = ticker.ToUpperInvariant();
    var run = await read.NewestAnalystRunAsync(EquityBrief.Core.Analysts.AnalystLimits.RatingsStage, named);
    var tipranks = await read.NewestRatingsAsync(named);

    return Results.Json(new
    {
        answered = tipranks is not null,
        answeredAt = tipranks?.AskedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
        html = SinglePageApp.RatingsSection(marks, named, tipranks, await read.TipRanksCallsAsync(), clock.UtcNow),
        run = run is { } last ? new { startedAt = last.StartedAt.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), outcome = last.Outcome, detail = last.Detail } : null,
    });
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
// see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
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

// The queue page's Write now press: one outstanding report started at once, ahead of the queue, by the drain
// writing that request alone. Refused without the page's own header and for a stock holding no request
// nobody has started. Inside a peak window it is refused, with the window's end and the price's multiple,
// unless the press says the operator confirmed the peak price, and then the drain writes it at that price.
// The press writes nothing: the drain claims and settles the request as it claims any other.
// see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
// see: A pass is started only by a request carrying the name page's own header
app.MapPost(SinglePageApp.WriteNowRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read, IClock clock, IDrainLauncher launcher) =>
{
    static IResult Said(string state, string line, int status) =>
        Results.Content(
            $"<p class=\"pass-now\" data-now=\"{state}\">{System.Net.WebUtility.HtmlEncode(line)}</p>",
            "text/html; charset=utf-8",
            statusCode: status);

    if (!string.Equals(request.Headers[SinglePageApp.PassHeader].FirstOrDefault(), SinglePageApp.PassHeaderValue, StringComparison.Ordinal))
    {
        return Said("header", "Nothing was started: the request did not come from the queue screen.", StatusCodes.Status403Forbidden);
    }

    var named = ticker.ToUpperInvariant();

    if (!(await read.QueueAsync()).Any(row => row.Ticker == named && row.State == ResearchRequests.Outstanding))
    {
        return Said("state", $"Nothing was started: {named} has no request nobody has started, so there is nothing to write now.", StatusCodes.Status409Conflict);
    }

    var form = request.HasFormContentType ? await request.ReadFormAsync() : null;
    var peak = QueueTimes.Peak(QueuePrices(builder.Configuration), clock.UtcNow);

    if (peak is not null && !string.Equals(form?["confirm"].FirstOrDefault(), SinglePageApp.WriteNowAtPeak, StringComparison.Ordinal))
    {
        return Said(
            "peak",
            $"Nothing was started: the research model is at its peak price, {QueueTimes.Times(peak.Multiple)} its off-peak price, until {QueueTimes.Stated(peak.Ends, clock.SessionZone)}, and writing {named} now needs that price confirmed.",
            StatusCodes.Status409Conflict);
    }

    var started = launcher.StartNow(named, peak is not null);

    return Said(started.Started ? "started" : "not-started", started.Line, started.Started ? StatusCodes.Status202Accepted : StatusCodes.Status409Conflict);
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
// see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
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

// Each pick's card on a page with the chart of what is behind it, the index's stored readings read once and each family's
// learned score once.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
static async Task<IReadOnlyList<FamilyCardView>> WithBehindAsync(ReadApi read, IReadOnlyList<FamilyCardView> cards, UniverseChoice universe, DateOnly night)
{
    if (!cards.Any(card => card.Picks.Any(pick => pick.Card is not null)))
    {
        return cards;
    }

    var readings = await read.LedgerReadingsAsync(universe.Code, night);
    var drawn = new List<FamilyCardView>();

    foreach (var card in cards)
    {
        var (model, passed, unread) = card.Picks.Any(pick => pick.Card is not null) ? await read.RidgeScoreAsync(universe.Code, card.Family, night) : (null, false, null);

        drawn.Add(card with
        {
            Picks = [.. card.Picks.Select(pick => pick.Card is { } decision ? pick with { Card = decision with { Behind = BehindScreen.Of(pick.Row.Ticker, universe.Name, readings, model, passed, unread) } } : pick)],
        });
    }

    return drawn;
}

// Every name's next dated event on or after a night in one read, which the sessions to a name's next report are counted
// to on the Watch list and on Tonight's card of the stocks one gate short, as the universe's column counts them.
static async Task<IReadOnlyDictionary<string, DateOnly>> NextEventsAsync(ReadApi read, DateOnly night) =>
    (await read.NextEventsAsync(night)).ToDictionary(row => row.Ticker, row => row.EventDate, StringComparer.Ordinal);

// Each card with its own rows close to a buy point and the numbers its rows draw in place of sentences, for each stock a
// chosen variant picks or that stands close to a buy point: on the S&P 500, whose family results are handed in, off the
// stored answer holding them, the swing filter's for the pullback and the night's result for the breakout and the
// drift; and otherwise a family's own reading off the stock's setup of the family the ledger stored on the night, a
// drift's, where its index's window is handed in, off the one stored on its report day within that window, and every
// other off its ledger readings on the night, each read once and each number they do not hold saying why.
// see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
static async Task<IReadOnlyList<FamilyCardView>> WithNumbersAsync(ReadApi read, IReadOnlyList<FamilyCardView> cards, UniverseChoice universe, DateOnly night, IReadOnlyList<CloseToBuyCell>? close = null, IReadOnlyList<FamilyResultRow>? results = null, int? driftWindow = null)
{
    var drawn = cards.Select(card =>
    {
        var own = (close ?? []).Where(row => row.Family == card.Family).ToArray();

        return (Card: card, Own: own, Tickers: own.Select(row => row.Ticker).Concat((card.RuleChoice?.Picks ?? []).Select(pick => pick.Ticker)).Distinct(StringComparer.Ordinal).ToArray());
    }).ToArray();
    var readings = await read.LedgerReadingsAsync(universe.Code, night);
    var setups = await read.NightSetupReadingsAsync(universe.Code, night);
    var reportDays = driftWindow is null
        ? null
        : await read.NewestSetupsAsync(universe.Code, EquityBrief.Core.Families.DriftRule.Name, night, [.. drawn.Where(one => one.Card.Family == EquityBrief.Core.Families.DriftRule.Name).SelectMany(one => one.Tickers).Distinct(StringComparer.Ordinal)]);
    var swing = results is null ? new Dictionary<string, string>(StringComparer.Ordinal) : await read.SwingGatesAsync(night);
    var answers = (results ?? []).GroupBy(result => (result.Family, result.Ticker)).ToDictionary(group => group.Key, group => group.First().Gates);

    IReadOnlyList<EquityBrief.Core.Filter.Gate>? AnswerOf(string family, string ticker) =>
        family == EquityBrief.Core.Families.SetupFamilies.Pullback && swing.TryGetValue(ticker, out var filtered)
            ? EquityBrief.Core.Families.FamilyRule.GatesOf(filtered)
            : answers.TryGetValue((family, ticker), out var stored) ? EquityBrief.Core.Families.FamilyRule.GatesOf(stored) : null;

    IReadOnlyList<double?>? SetupOf(string family, string ticker) =>
        family == EquityBrief.Core.Families.DriftRule.Name && driftWindow is { } window
            ? reportDays!.TryGetValue(ticker, out var newest) ? EquityBrief.Core.Cards.CardNumbers.AtReportDay(newest.Session, newest.Readings, night, window) : null
            : setups.GetValueOrDefault((family, ticker));

    return
    [
        .. drawn.Select(one => one.Card with
        {
            Close = one.Own,
            Numbers = one.Tickers.ToDictionary(
                ticker => ticker,
                ticker => EquityBrief.Core.Cards.CardNumbers.Cells(one.Card.Family, AnswerOf(one.Card.Family, ticker), SetupOf(one.Card.Family, ticker), readings.GetValueOrDefault(ticker)),
                StringComparer.Ordinal),
        }),
    ];
}

// From 22.4 a stock's brief read on its chart page, its inputs each read as the full page or the decision card reads them:
// each setup's answer and pick of the night on the stock's index and whether the heavyweights' book holds it, the card of
// the rule listing it, its levels plan and the trade step's reading of it on the S&P 500, the account, its reported
// quarters' readings, its six-month return's place among its index's members off the ledger's readings of the night, and
// its next report after the night.
// see: The chart page opens on a brief read written by code from stored figures, and no rule reads it
static async Task<BriefView> BriefAsync(ReadApi read, StoreLocation store, string ticker, string? indexCode, DateOnly night, UniverseRow? member, IReadOnlyList<IndicatorRow> indicators, ReportView report, IReadOnlyList<ReactionRow> reactions, IReadOnlyList<DecisionCardRow> cards, FundamentalScoreRow? score = null)
{
    var large = indexCode == Universes.Large.Code;
    var gate = large ? await read.GateResultAsync(ticker, night) : null;
    IReadOnlyList<BriefStanding> standing = [];
    IReadOnlyDictionary<string, IReadOnlyList<double?>>? readings = null;

    if (indexCode is { } index)
    {
        bool held;

        if (large)
        {
            held = TonightScreen.OpenHoldings(await read.HeavyweightHoldingsAsync(night)).Contains(ticker, StringComparer.Ordinal);
        }
        else
        {
            var heavyweightRule = await read.IndexNightAsync(index, night) is { } indexNight
                ? TonightScreen.IndexLiveRule(index, EquityBrief.Core.Families.HeavyweightRule.Name, await read.RegisteredCandidatesAsync(), indexNight)
                : null;
            var book = heavyweightRule is null ? await read.IndexHoldingsAsync(index, night) : await read.IndexRuleHoldingsAsync(heavyweightRule, night);

            held = book.Any(row => row.Ticker == ticker && row.EndedOn is null);
        }

        standing = BriefScreen.Standing(new BriefAnswers(
            ticker,
            large,
            gate,
            large ? await read.FamilyResultsAsync(night) : [],
            large ? await read.FamilyPicksAsync(night) : [],
            await read.IndexResultsAsync(index, night),
            await read.IndexPicksAsync(index, night),
            await read.DipResultsAsync(index, night),
            await read.DipPicksAsync(index, night),
            held,
            await read.ImprovingResultsAsync(index, night),
            await read.ImprovingTradesAsync(index, night)));
        readings = await read.LedgerReadingsAsync(index, night);
    }

    BriefListed? listed = null;

    if (standing.FirstOrDefault(one => one.State == BriefStates.Listed) is { } onTheList)
    {
        int? exitLevel = onTheList.Family == EquityBrief.Core.Families.DipRule.Name && indexCode is { } dipIndex && await read.DipNightAsync(dipIndex, night) is { } dipNight
            ? TonightScreen.SettingOf(dipNight).Setting.ExitLevel
            : null;

        listed = new BriefListed(onTheList.Family, cards.FirstOrDefault(card => card.Family == onTheList.Family && card.Index == indexCode), exitLevel);
    }

    var halfYearAt = EquityBrief.Core.Ledger.LedgerReadings.IndexOf("return_half_year");
    var halfYear = readings is not null && readings.TryGetValue(ticker, out var own)
        ? EquityBrief.Core.Ledger.LedgerHeadings.Percentile(own[halfYearAt], [.. readings.Values.Select(one => one[halfYearAt])])
        : null;
    var fundamental = await read.FundamentalReadingAsync(ticker);

    return BriefScreen.Of(new BriefInputs(
        ticker,
        Universes.ByCode(indexCode ?? string.Empty)?.Name,
        night,
        member,
        indicators.GroupBy(row => row.Name, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.OrderBy(row => row.SessionDate).Last().Value, StringComparer.Ordinal),
        fundamental is null ? null : EquityBrief.Core.Quarters.Readings.FromJson(fundamental.Readings),
        halfYear,
        report.Analysts,
        listed,
        large ? NameScreen.PlanRows(await read.LadderAsync(ticker, night)) : [],
        large,
        EquityBrief.Core.Cards.AccountFile.Read(store.DataRoot),
        standing,
        await read.NextReportAsync(ticker, night),
        [.. reactions.Select(row => row.MovePct)],
        report.Insiders,
        gate?.LadderRewardToRisk,
        score));
}

// A heavyweights card with its standing tonight, read off tonight's closes once: each holding's and each sector fund's
// close tonight, a look-back before and at the last rebalance, and, where the book reads a sector's members' mean, every
// member's of the index with its sector.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
static async Task<HeavyweightCardView> WithStandingAsync(
    ReadApi read,
    HeavyweightCardView card,
    UniverseChoice universe,
    DateOnly night,
    IReadOnlyDictionary<string, double?> growth,
    bool fundReturn,
    Func<string, string?> reason,
    bool designB = false)
{
    var largeFunds = universe != Universes.Large;
    var funds = await read.FundStandingClosesAsync([.. EquityBrief.Core.Families.GicsSectors.Funds.Values], night, card.LookBack, card.LastRebalance);
    var held = card.Holdings.Select(holding => holding.Ticker).ToArray();
    var sectors = fundReturn || designB ? new Dictionary<string, string?>(StringComparer.Ordinal) : await read.MemberSectorsAsync(universe.Code, night);
    var stocks = await read.StockStandingClosesAsync([.. held.Concat(sectors.Keys).Distinct(StringComparer.Ordinal)], night, card.LookBack, card.LastRebalance);

    double? SectorReturn(string sector) =>
        fundReturn
            ? EquityBrief.Core.Families.GicsSectors.Funds.TryGetValue(sector, out var fund) && funds.TryGetValue(fund, out var closes) ? TonightScreen.ReturnOf(closes) : null
            : EquityBrief.Core.Families.HeavyweightStandings.MembersMean(sectors.Where(member => member.Value == sector).Select(member => stocks.TryGetValue(member.Key, out var closes) ? TonightScreen.ReturnOf(closes) : null));

    return TonightScreen.WithStanding(card, night, stocks, funds, growth, SectorReturn, reason, designB, largeFunds);
}

// Each open holding's growth carried to the night, the newest buy of a stock taking it.
static IReadOnlyDictionary<string, double?> Growths(IEnumerable<(string Ticker, DateOnly EnteredOn, double? Growth)> open) =>
    open.GroupBy(holding => holding.Ticker, StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.MaxBy(holding => holding.EnteredOn).Growth, StringComparer.Ordinal);

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

// Whether the exchange traded on a day, for tonight's notice, and a day the closure table
// cannot place is one it did not, rather than a page that fails to draw for a night nobody
// asked about.
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
    var aboutNight = NightFrom(await read.RunLogAsync(about), about, clock, store);
    var notice = page.NightNotice(marks, aboutNight);

    // A night not yet run draws no notice, and the date line says when it starts.
    // see: The date line names tonight's night by the start the most of the newest seven nights share
    var due = SinglePageApp.NightDue(aboutNight, EquityBrief.Core.Time.NightTime.Usual(await read.NightRunIdsAsync()), clock.SessionZone);

    // The index the page reads, chosen under Universe and kept in the link, the S&P 500 where it names none.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var selector = Cards.Universe(reading, await read.MembersByIndexAsync(dated), night is { Length: > 0 } ? SinglePageApp.NightRoute + night : "#/");

    // The live alarm's flag on each family of the index chosen, as its newest period stands, drawn on the newest night.
    // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
    if (night is not { Length: > 0 })
    {
        notice += SinglePageApp.LoopAlarmNotice(reading, await read.LoopFlagsAsync(reading.Code));
    }

    if (reading != Universes.Large)
    {
        var indexNight = await read.IndexNightAsync(reading.Code, dated);
        var indexHeld = await read.IndexNightsAsync(reading.Code);

        if (indexNight is null)
        {
            return Results.Content(
                notice + page.IndexTonightRegion(marks, dated, reading, selector, indexHeld, null, [], null, due: due),
                "text/html; charset=utf-8");
        }

        var members = await read.IndexMembersAsync(reading.Code, dated);
        var indexResults = await read.IndexResultsAsync(reading.Code, dated);
        var (indexQueued, indexTimes, _) = await QueueRead(read, clock);
        // Each card with its family's recorded sweep answers on the index, each line kept at the freeze made on its answer
        // and gone with the family's next freeze or its next passing sweep, a frozen family's card live with its rule's
        // words and the figures of the sweep its freeze followed; the register and the answers read as of the instant the
        // night read the register at where a night run again read it later.
        // see: No family on any index is set aside or hidden by a test result without the operator's word
        // see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
        var indexAsOf = TonightScreen.ReadAsOf(indexNight, dated);
        var indexAnswers = await read.SweepAnswersAsync(reading.Code, indexAsOf);
        var indexRegister = await read.RegisteredCandidatesAsync();
        var indexCardContext = await CardContextAsync(read, store);
        // The short-term dip's own rows, its card drawn after every swing family's with its pick cards, funnel and numbers as
        // every family's, and its label on an earlier family's row where it gave way to that family.
        // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
        var indexDipNight = await read.DipNightAsync(reading.Code, dated);
        var indexDipPicks = await read.DipPicksAsync(reading.Code, dated);
        var indexDipResults = await read.DipResultsAsync(reading.Code, dated);
        var indexReadings = await read.FundamentalReadingsAsync(dated);
        var researchedRows = await read.ResearchedAsync();

        // A dip pick's card words its management by the setting the dip's night ran on.
        if (indexDipNight is not null)
        {
            indexCardContext = indexCardContext with { Dip = TonightScreen.SettingOf(indexDipNight).Setting };
        }

        var queueStates = QueueTimes.States(indexQueued, indexTimes, clock.SessionZone);
        // Each card drawn by the rule the link chooses for it, the provisional rule or the live rule where it names none,
        // with its funnel, its stretch line and, under the breakout card, the breakouts forming.
        // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
        var indexCards = RuleScreen.WithRules(
            CardScreen.WithCards(
                TonightScreen.WithSweepLines(
                    TonightScreen.WithIndexFreezes(
                        TonightScreen.WithDip(
                            TonightScreen.IndexCards(
                                reading,
                                indexNight,
                                await read.IndexPicksAsync(reading.Code, dated),
                                indexResults,
                                members,
                                researchedRows,
                                queueStates,
                                indexReadings),
                            TonightScreen.DipCard(reading, indexDipNight, indexDipPicks, indexDipResults, members, researchedRows, queueStates, indexReadings)),
                        reading,
                        indexRegister,
                        indexNight),
                    indexAnswers,
                    family => TonightScreen.IndexFreezesOf(reading.Code, family, indexRegister, indexNight)),
                await read.DecisionCardsAsync(reading.Code, dated),
                indexCardContext),
            reading.Name,
            false,
            dated,
            indexRegister,
            family => request.Query[RuleScreen.QueryKey(family)].FirstOrDefault(),
            await read.RuleNightsAsync(reading.Code, dated),
            [.. await read.RulePicksAsync(reading.Code, dated), .. await read.DipVersionPicksAsync(reading.Code, dated)],
            await read.FormingRowsAsync(reading.Code, dated),
            // The breakouts forming read against the breakout's own market check, as each family reads its own.
            // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
            TonightScreen.FamilyOpen(indexNight, indexResults, EquityBrief.Core.Families.BreakoutRule.Name),
            IndexRuleSettings.Read(indexNight.Settings),
            indexAsOf);
        // From 20.11 the stocks one gate short read off every part's answer the night stored, the market check among them,
        // and the short-term dip's beside them, which its own rows hold on a night the index families stored none.
        // see: The S&P 400's and 600's nights store every part's answer for every member, the market check its own
        var indexDipClose = TonightScreen.DipCloseAcross(indexDipResults, members);
        IReadOnlyList<EquityBrief.Web.Marks.CloseToBuyCell>? indexClose = TonightScreen.IndexCloseAcross(indexResults, members) is { } indexSwingClose
            ? [.. indexSwingClose, .. indexDipClose]
            : indexDipResults.Count > 0 ? indexDipClose : null;
        var indexCloseHref = indexClose is null ? null : MarkRenderer.CloseToBuyHref(night is { Length: > 0 } ? SinglePageApp.NightRoute + night : "#/", reading.Word);

        indexCards = await WithBehindAsync(read, indexCards, reading, dated);
        // A drift's numbers read off the setup the ledger stored on its report day, within the window its rule states.
        var indexSettings = IndexRuleSettings.Read(indexNight.Settings);
        var (liveDrift, _) = RuleScreen.Accounts(EquityBrief.Core.Families.DriftRule.Name, false, reading.Name, indexRegister, dated, indexSettings, indexAsOf);
        indexCards = await WithNumbersAsync(read, indexCards, reading, dated, indexClose, driftWindow: TonightScreen.DriftWindow(liveDrift, indexSettings));
        indexCards = TonightScreen.WithDipChips(TonightScreen.WithDipLabels(indexCards, indexDipPicks), indexDipNight, reading.Code);
        // From 22.3 the dip's card where a frozen rule drew its night, with the line every frozen card draws.
        // see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
        indexCards = TonightScreen.WithDipFreeze(indexCards, reading.Code, indexRegister, indexAnswers, indexDipNight, indexAsOf);

        // Each setup's record so far as Paper trading counts it, its finished trades against what a verdict needs.
        var indexProgress = await PaperProgressAsync(read, reading, dated);

        indexCards = [.. indexCards.Select(card => card with { Progress = indexProgress.GetValueOrDefault(card.Family) })];

        // The fundamentals-first family's card, passes and trades are drawn no more, its rule running out of sight, and the
        // cards left are numbered in the page's order.
        // see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
        var shownCards = indexCards.Where(card => card.Family != EquityBrief.Core.Families.FundamentalsRule.Name).ToArray();

        indexCards = [.. shownCards.Select((card, at) => card with { Place = at + 1, Of = shownCards.Length })];
        indexResults = [.. indexResults.Where(result => result.Family != EquityBrief.Core.Families.FundamentalsRule.Name)];
        indexClose = indexClose is null ? null : [.. indexClose.Where(cell => cell.Family != EquityBrief.Core.Families.FundamentalsRule.Name)];

        var indexTrades = (await read.IndexTradesAsync(reading.Code, dated)).Where(trade => trade.Family != EquityBrief.Core.Families.FundamentalsRule.Name).Concat(await read.DipTradesAsync(reading.Code, dated)).ToArray();
        var indexOpen = indexTrades.Count(trade => trade.Listed < dated && trade.EndedOn is null);

        // The heavyweights' card drawn from the index's own book, or from its live rule's book where a freeze stands and the
        // night kept it.
        // see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
        var heavyweightRule = TonightScreen.IndexLiveRule(reading.Code, EquityBrief.Core.Families.HeavyweightRule.Name, indexRegister, indexNight);
        var boughtCards = await read.BoughtCardsAsync(reading.Code, dated);
        var bookRows = heavyweightRule is null ? await read.IndexHoldingsAsync(reading.Code, dated) : await read.IndexRuleHoldingsAsync(heavyweightRule, dated);
        var readsBefore = await read.PulledBeforeTheYearAsync(dated);
        var heavyweightCard = heavyweightRule is null
            ? TonightScreen.IndexHeavyweights(
                reading,
                indexNight,
                bookRows,
                await read.IndexLastRebalanceAsync(reading.Code, dated),
                await read.IndexHoldingClosesAsync(reading.Code, dated),
                members,
                readsBefore)
            : TonightScreen.WithIndexHeavyweightFreeze(
                TonightScreen.IndexHeavyweights(
                    reading,
                    indexNight,
                    bookRows,
                    await read.IndexRuleLastRebalanceAsync(heavyweightRule, dated),
                    await read.IndexRuleHoldingClosesAsync(heavyweightRule, dated),
                    members,
                    readsBefore),
                reading,
                indexRegister,
                indexNight,
                readsBefore);

        // The card's selector, as the S&P 500's: the provisional or live rule first and each registered variant after
        // it, a chosen variant's own book drawn under the band.
        // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
        var (indexHeavyweightChoice, indexHeavyweightVariant) = RuleScreen.Heavyweights(
            reading.Name,
            false,
            dated,
            indexRegister,
            request.Query[RuleScreen.QueryKey(EquityBrief.Core.Families.HeavyweightRule.Name)].FirstOrDefault(),
            heavyweightCard.Rule,
            await read.SixSettingsAsync(reading.Code),
            IndexRuleSettings.Read(indexNight.Settings),
            indexAsOf);
        var liveHoldings = heavyweightCard.Holdings.Select(holding => holding.Ticker).ToArray();

        if (indexHeavyweightVariant is { } chosenBook)
        {
            bookRows = await read.IndexRuleHoldingsAsync(chosenBook, dated);
            heavyweightCard = TonightScreen.IndexHeavyweights(
                reading,
                indexNight,
                bookRows,
                await read.IndexRuleLastRebalanceAsync(chosenBook, dated),
                await read.IndexRuleHoldingClosesAsync(chosenBook, dated),
                members,
                readsBefore);
        }

        heavyweightCard = heavyweightCard with
        {
            RuleChoice = indexHeavyweightVariant is null
                ? indexHeavyweightChoice
                : indexHeavyweightChoice with { Share = RuleScreen.ShareOf(heavyweightRule is not null, liveHoldings, [.. heavyweightCard.Holdings.Select(holding => holding.Ticker)]) },
        };

        // The book's standing tonight, its sectors read over the index's own members' mean, an empty sector saying the book
        // stores no reading of it, and a design (b) book reading no lead of its own.
        heavyweightCard = await WithStandingAsync(
            read,
            heavyweightCard,
            reading,
            dated,
            Growths(bookRows.Where(holding => holding.EndedOn is null).Select(holding => (holding.Ticker, holding.EnteredOn, holding.Growth))),
            false,
            _ => TonightScreen.StoresNoSectorReading,
            RuleScreen.DesignB(indexHeavyweightVariant ?? heavyweightRule, indexRegister));

        var indexCells = UniverseScreen.Rows(await read.UniverseAsync(reading.Code, dated)).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal);

        return Results.Content(
            notice + page.IndexTonightRegion(
                marks,
                dated,
                reading,
                selector,
                indexHeld,
                TonightScreen.IndexLine(reading, indexNight, indexCards, indexResults, indexOpen, indexClose) with
                {
                    CloseHref = indexCloseHref,
                    // The members a setup passed, the dip's among them, as its buy points are among the line's.
                    Passed = TonightScreen.PassedWithDip(indexResults.Where(result => result.Passed).Select(result => result.Ticker), indexDipResults),
                    Dip = indexDipNight is null ? null : TonightScreen.DipSettingWords(indexDipNight),
                    DipOpen = indexDipNight?.MarketOpen ?? false,
                },
                indexCards,
                CardScreen.WithCards(
                    TonightScreen.WithSweepLine(
                        heavyweightCard,
                        indexAnswers,
                        TonightScreen.IndexFreezesOf(reading.Code, EquityBrief.Core.Families.HeavyweightRule.Name, indexRegister, indexNight)),
                    boughtCards,
                    indexCardContext),
                indexNight.Fault is null ? null : TonightScreen.NotComputed(reading),
                due,
                request.Query["name"].FirstOrDefault(),
                indexClose is null ? null : TonightScreen.IndexCloseToBuy(indexClose, await NextEventsAsync(read, dated), dated),
                request.Query[MarkRenderer.CloseToBuyKey].FirstOrDefault() == MarkRenderer.CloseToBuyOpen,
                ended: TonightScreen.EndedTonight(dated, [], indexTrades),
                quality: await QualityCardAsync(read, reading.Code, dated, indexCells),
                improving: await ImprovingCardAsync(read, reading.Code, dated, indexCells)),
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
    // see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
    var (queued, times, _) = await QueueRead(read, clock);
    var states = QueueTimes.States(queued, times, clock.SessionZone);
    IReadOnlyList<EquityBrief.Web.Marks.ListingCell> rows = [.. listed.Select(row => states.TryGetValue(row.Ticker, out var state) ? row with { Queue = state } : row)];
    IReadOnlyList<EquityBrief.Web.Marks.ListingCell>? nearRows = near is null ? null : [.. near.Select(row => states.TryGetValue(row.Ticker, out var state) ? row with { Queue = state } : row)];

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
    // Each card with the line its family's recorded sweep answers draw, read against its freezes and its live rule as of
    // the night.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    var register = await read.RegisteredCandidatesAsync();
    var answers = await read.SweepAnswersAsync(reading.Code, TonightScreen.PastTheNight(dated));
    // Each pick with the card the night stored for it, opened in place beneath its row.
    // see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
    var cardContext = await CardContextAsync(read, store);
    // Each card drawn by the rule the link chooses for it, the live rule where it names none, with its funnel, its
    // stretch line and, under a breakout card, the breakouts forming.
    // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
    var cards = onThePage is null || gates is null
        ? null
        : RuleScreen.WithRules(
            CardScreen.WithCards(
                TonightScreen.WithSweepLines(
                    TonightScreen.Families(dated, onThePage, familyPicks, rows, gates, register, ruleView, familyResults, pullbackSettings, await read.LoopSettingsAsync(reading.Code)),
                    answers,
                    family => TonightScreen.FreezesOf(family, register, dated)),
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

    // The fundamentals-first family, which the index families' step reads on the S&P 500 under its own rows, draws no card
    // and counts no pass on the page, its rule running out of sight.
    // see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
    IReadOnlyList<IndexResultRow> largeIndexResults = [];

    // The short-term dip's card after every swing family's, read from its own rows on the S&P 500 with its pick cards and
    // funnel as every family's, and its label on an earlier family's row where it gave way to that family.
    // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
    var dipNight = await read.DipNightAsync(reading.Code, dated);
    var dipPicks = await read.DipPicksAsync(reading.Code, dated);
    var dipResults = await read.DipResultsAsync(reading.Code, dated);
    var dipMembers = await read.IndexMembersAsync(reading.Code, dated);

    if (cards is not null && dipNight is not null && TonightScreen.DipCard(reading, dipNight, dipPicks, dipResults, dipMembers, researched, states, await read.FundamentalReadingsAsync(dated)) is { } dipCard)
    {
        // The register and the answers read as of the instant the night read the register at where a night run again
        // read it later, as the S&P 400's and 600's cards read them.
        // see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
        var dipAsOf = TonightScreen.ReadAsOf(await read.IndexNightAsync(reading.Code, dated), dated);

        // A dip pick's card words its management by the setting the dip's night ran on.
        var dips = RuleScreen.WithRules(
            CardScreen.WithCards([dipCard], await read.DecisionCardsAsync(reading.Code, dated), cardContext with { Dip = TonightScreen.SettingOf(dipNight).Setting }),
            reading.Name,
            false,
            dated,
            register,
            family => request.Query[RuleScreen.QueryKey(family)].FirstOrDefault(),
            await read.RuleNightsAsync(reading.Code, dated),
            [.. await read.RulePicksAsync(reading.Code, dated), .. await read.DipVersionPicksAsync(reading.Code, dated)],
            await read.FormingRowsAsync(reading.Code, dated),
            dipNight.MarketOpen,
            asOf: dipAsOf);

        // From 22.3 the dip's card where a frozen rule drew its night, with the line every frozen card draws.
        // see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
        cards = TonightScreen.WithDip(cards, TonightScreen.WithDipFreeze(TonightScreen.WithDipChips(dips, dipNight, reading.Code), reading.Code, register, await read.SweepAnswersAsync(reading.Code, dipAsOf), dipNight, dipAsOf)[0]);
    }

    IReadOnlyList<EquityBrief.Web.Marks.CloseToBuyCell>? closeAcross = cards is null ? null : TonightScreen.CloseAcross(onThePage!, nearRows, familyResults, cells) is { } swingClose ? [.. swingClose, .. TonightScreen.DipCloseAcross(dipResults, dipMembers)] : null;

    if (cards is not null)
    {
        cards = await WithBehindAsync(read, cards, reading, dated);
        cards = await WithNumbersAsync(read, cards, reading, dated, closeAcross, familyResults);
        cards = TonightScreen.WithDipLabels(cards, dipPicks);

        // Each setup's record so far as Paper trading counts it, its finished trades against what a verdict needs.
        var progress = await PaperProgressAsync(read, reading, dated);

        cards = [.. cards.Select(card => card with { Progress = progress.GetValueOrDefault(card.Family) })];
    }

    // A plan and its bands as the night shown stored them, drawn against that night's close.
    async Task<string> PlanOf(string ticker)
    {
        var member = universe.FirstOrDefault(row => row.Ticker == ticker);

        return NameScreen.PlanRegion(
            page,
            marks,
            ticker,
            await read.LadderAsync(ticker, dated),
            member?.Close ?? 0m,
            await read.LevelsAsync(ticker, dated),
            member?.TypicalMove,
            member?.Close is not null);
    }

    // On a night the families drew the list, every buy point folds its pick's card beneath its row, the one the hash names
    // open, and no row is selected where it names none; from the 22.6 correction of 2026-10-25 the card draws the stock's
    // entry and exit plan beneath its setup's own, the plan the night shown stored against that night's close. On a night
    // before them, whichever row the reader selected, and the first row where they selected none: section 15.7's region
    // is for whichever row is selected, and a row of either list can be selected.
    // see: A buy point's fold draws the stock's entry and exit plan beneath its setup's own plan, as the full page draws it
    var asked = request.Query["name"].FirstOrDefault();
    var pointed = cards is null ? [] : cards.SelectMany(card => card.Picks).Select(pick => pick.Row.Ticker).Distinct(StringComparer.Ordinal).ToArray();
    var opened = asked is { } named && pointed.Contains(named, StringComparer.Ordinal) ? named : null;
    var selection = cards is null ? TonightScreen.Selected(rows, asked, nearRows) : null;
    var selected = selection is { } chosen ? await PlanOf(chosen.Ticker) : string.Empty;
    var plans = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var ticker in pointed)
    {
        plans[ticker] = NameScreen.EntryAndExitPlan(marks, ticker, await read.LadderAsync(ticker, dated), universe.FirstOrDefault(row => row.Ticker == ticker)?.Close);
    }

    // The sector heavyweights' card, drawn after the swing families' on a night they drew the page, whatever the
    // market check read.
    // see: The market check closes every swing family's list together but the short-term dip's, and the sector heavyweights read none
    // The card drawn by the rule its selector chooses, a registered variant's own book under the band where one is.
    // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
    var (heavyweightChoice, heavyweightVariant) = RuleScreen.Heavyweights(
        reading.Name,
        true,
        dated,
        register,
        request.Query[RuleScreen.QueryKey(EquityBrief.Core.Families.HeavyweightRule.Name)].FirstOrDefault(),
        RuleWords.Heavyweights(EquityBrief.Core.Families.HeavyweightRule.Live));
    var heavyweightHoldings = await read.HeavyweightHoldingsAsync(dated, heavyweightVariant);
    var heavyweightRead = await read.HeavyweightReadAsync(dated, heavyweightVariant);
    var heavyweightSettings = RuleScreen.HeavyweightSettingsOf(heavyweightVariant, register) ?? EquityBrief.Core.Families.HeavyweightRule.Live;
    var heavyweights = cards is null
        ? null
        : CardScreen.WithCards(
            TonightScreen.WithSweepLine(
                TonightScreen.Heavyweights(dated, heavyweightHoldings, heavyweightRead, await read.HeavyweightClosesAsync(dated, heavyweightVariant), cells, register, await read.PulledBeforeTheYearAsync(dated)),
                answers,
                TonightScreen.FreezesOf(EquityBrief.Core.Families.HeavyweightRule.Name, register, dated)) with
            {
                RuleChoice = heavyweightVariant is null
                    ? heavyweightChoice
                    : heavyweightChoice with
                    {
                        Share = RuleScreen.ShareOf(
                            true,
                            TonightScreen.OpenHoldings(await read.HeavyweightHoldingsAsync(dated, null)),
                            TonightScreen.OpenHoldings(heavyweightHoldings)),
                    },
            },
            heavyweightVariant is null ? await read.BoughtCardsAsync(reading.Code, dated) : [],
            cardContext);

    if (heavyweights is not null)
    {
        heavyweights = await WithStandingAsync(
            read,
            heavyweights,
            reading,
            dated,
            Growths(heavyweightHoldings.Where(holding => holding.EndedOn is null).Select(holding => (holding.Ticker, holding.EnteredOn, holding.Growth))),
            heavyweightSettings.FundReturn,
            sector => TonightScreen.EmptySectorReason(sector, heavyweightRead, heavyweightSettings.FundReturn, heavyweightSettings.HighBeta, heavyweightSettings.LookBack, heavyweightHoldings, heavyweights.NextRebalance));
    }
    var line = cards is null
        ? null
        : TonightScreen.Line(
            ruleView,
            cards,
            closeAcross!.Count,
            // A trade listed on the night itself is one of its buy points; the open trades are the earlier ones, the
            // short-term dip's among them.
            PicksScreen.Cells(picks, dated).Count(trade => trade.Night < dated && trade.Status == EquityBrief.Web.Marks.PickStatus.Open && trade.RepeatOf is null)
                + (await read.DipTradesAsync(reading.Code, dated)).Count(trade => trade.Listed < dated && trade.EndedOn is null),
            // The members a setup passed: the swing filter's passes, which are the pullback's, every other family's, the
            // fundamentals-first family's under the index families' own rows, and the short-term dip's.
            TonightScreen.PassedWithDip(
                gates!.Where(gate => gate.Passed).Select(gate => gate.Ticker)
                    .Concat(familyResults.Where(result => result.Passed).Select(result => result.Ticker))
                    .Concat(largeIndexResults.Where(result => result.Passed).Select(result => result.Ticker)),
                dipResults),
            universe.Count) with
        {
            Dip = dipNight is null ? null : TonightScreen.DipSettingWords(dipNight),
            DipOpen = dipNight?.MarketOpen ?? false,
        };

    // From 19.8 the stocks one gate short across every setup in one card, open where the link keeps it open, its presses
    // and its Universe link on the newest night alone, as a stock's page for an earlier night offers no press.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    var watchedNames = await read.WatchedAsync();
    var closeToBuy = closeAcross is null
        ? null
        : TonightScreen.CloseToBuy(
            closeAcross,
            UniverseScreen.Rows(universe, null, await NextEventsAsync(read, dated), dated).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            watchedNames.Select(row => row.Ticker).ToHashSet(StringComparer.Ordinal));

    return Results.Content(
        notice + page.TonightRegion(
            marks,
            dated,
            universe.Count,
            TonightScreen.Fired(listings),
            rows,
            watchedNames.Count,
            selected,
            opened ?? selection?.Ticker,
            records,
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
            selector: selector,
            due: due,
            closeToBuy: closeToBuy,
            closeOpen: request.Query[MarkRenderer.CloseToBuyKey].FirstOrDefault() == MarkRenderer.CloseToBuyOpen,
            newest: await read.NewestNightAsync() == dated,
            closeHref: closeToBuy is null ? null : MarkRenderer.CloseToBuyHref(night is { Length: > 0 } ? SinglePageApp.NightRoute + night : "#/", reading.Word),
            ended: cards is null ? null : TonightScreen.EndedTonight(dated, PicksScreen.Cells(picks, dated), await read.DipTradesAsync(reading.Code, dated)),
            plans: plans,
            quality: cards is null ? null : await QualityCardAsync(read, reading.Code, dated, cells),
            improving: cards is null ? null : await ImprovingCardAsync(read, reading.Code, dated, cells)),
        "text/html; charset=utf-8");
});

// The watch list page, section 15.16: the names the operator follows on the newest night, each drawn from
// that night's own rows whether or not the list holds it, with what the swing filter said of it.
// see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
app.MapGet("/screens/watch", async (ReadApi read, SinglePageApp page, MarkRenderer marks) =>
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
                UniverseScreen.Rows(universe, null, await NextEventsAsync(read, night), night).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
                await read.ClosesToTheNightAsync(night),
                universe,
                await read.GateResultsAsync(night),
                await read.ResearchedAsync(),
                await read.FamilyResultsAsync(night)),
            ReadApi.WatchLimit,
            marks),
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

// Paper trading, section 15.21: every account on the index the link names, walked from the account settings by the
// core's one function as the page is read, and every figure each part draws beside the walk worked in the core with it,
// each part's choice kept in the link.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
app.MapGet("/screens/paper", async (HttpRequest request, ReadApi read, MarkRenderer marks, SinglePageApp page, StoreLocation store) =>
{
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var night = await read.NewestNightAsync();
    var held = EquityBrief.Core.Cards.AccountFile.Read(store.DataRoot);
    var walkedOn = held ?? EquityBrief.Core.Cards.AccountSettings.Example;
    var paper = night is { } on ? await PaperAccountsAsync(read, reading, on, walkedOn) : null;
    var selector = night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.PaperRoute) : string.Empty;

    // A family a key names, none where it names no family; from 22.11 Quality at a fair price's among them, whose accounts
    // the page keeps after the setups', and from 22.3 the short-term dip's, whose accounts it keeps after the swing
    // families'.
    // see: A quality rule is registered as the family on its index and kept by the family's own step alone
    // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
    string? Family(string key) =>
        request.Query[key].FirstOrDefault() is { } named
            && (EquityBrief.Core.Families.SetupFamilies.PageOrder.Contains(named) || named == EquityBrief.Core.Families.QualityRule.Name)
            ? named
            : null;

    var link = new PaperLink(
        reading.Word,
        Family(PaperLink.FamilyKey),
        request.Query[PaperLink.BoughtKey].FirstOrDefault() == PaperLink.ClosedValue,
        Family(PaperLink.ShowKey),
        Family(PaperLink.VariantsKey),
        Family(PaperLink.RuleKey));
    var variants = link.Variants ?? EquityBrief.Core.Families.SetupFamilies.Order[0];
    var rule = link.Rule ?? EquityBrief.Core.Families.SetupFamilies.Order[0];
    var drawn = paper is { } accounts && night is { } walked
        ? PaperScreen.Page(
            accounts.Sources,
            accounts.Views,
            walked,
            accounts.Sessions ?? [],
            accounts.Close,
            accounts.Stocks ?? new Dictionary<string, PaperStockRow>(StringComparer.Ordinal),
            ticker => accounts.Standings?.GetValueOrDefault(ticker),
            link.Show,
            variants,
            PaperScreen.Fund(reading.Code),
            accounts.Fund ?? (_ => null))
        : new PaperPageView([], [], [], [], new EquityBrief.Core.Cards.PaperNotBought([]), [], null);

    return Results.Content(
        page.PaperRegion(
            marks,
            reading,
            night,
            drawn,
            link,
            variants,
            rule,
            held is { } settings ? (settings.Size, settings.RiskPercent, settings.PositionCap) : null,
            EquityBrief.Core.Cards.AccountSettings.ProposedCap,
            selector,
            held is null ? (walkedOn.Size, walkedOn.RiskPercent, walkedOn.PositionCap) : null),
        "text/html; charset=utf-8");
});

// Quality at a fair price's card on an index as of a night, read off its own rows: its night, its last reshuffle, its
// holdings open on the night or sold since that reshuffle, tonight's answers, and the answers of the last reshuffle and of
// each night a holding was chosen on, which each change since is read from. From 22.11 the card draws the live rule's
// rows where the night kept one, with the day it went live and the line its history test's recorded answer gives it.
// see: Quality at a fair price holds the companies the scorecard rates strongest that are not dear against their own history, reshuffled on each month's first session
// see: A quality rule is registered as the family on its index and kept by the family's own step alone
async Task<QualityCardView> QualityCardAsync(ReadApi read, string index, DateOnly night, IReadOnlyDictionary<string, UniverseCell> cells)
{
    var rule = await read.QualityCardRuleAsync(index, night);
    var last = await read.QualityLastReshuffleAsync(index, night, rule);
    var holdings = await read.QualityHoldingsAsync(index, night, last ?? night, rule);
    var answersOn = new Dictionary<DateOnly, IReadOnlyList<QualityResultRow>>();

    foreach (var on in holdings.Select(holding => holding.ChosenOn).Append(last ?? night).Distinct())
    {
        answersOn[on] = await read.QualityResultsAsync(index, on, rule);
    }

    DateOnly? liveSince = null;
    QualityFixedView? fixedView = null;

    if (rule != ReadApi.QualityProvisional
        && (await read.RegisteredCandidatesAsync()).Where(row => row.Candidate == rule && row.Event == EquityBrief.Core.Candidates.CandidateFamily.Registered).OrderBy(row => row.RegisteredAt).LastOrDefault() is { } registered)
    {
        liveSince = DateOnly.FromDateTime(registered.RegisteredAt.UtcDateTime);

        var answer = (await read.SweepAnswersAsync(index, registered.RegisteredAt.AddSeconds(1)))
            .Where(one => one.Family == EquityBrief.Core.Families.QualityRule.Name)
            .OrderBy(one => one.RecordedAt)
            .LastOrDefault();

        fixedView = TonightScreen.QualityFixed(liveSince.Value, answer);
    }

    return TonightScreen.Quality(index, night, await read.QualityNightAsync(index, night, rule), last, holdings, await read.QualityResultsAsync(index, night, rule), answersOn, cells, liveSince, fixedView);
}

// The Fundamentals family's card on an index as of a night, read off its own rows: its night, tonight's answers, the
// answers of each night an open trade was chosen on, its trades, the index's scorecards and ledger readings, what the
// quality card's rule says of each stock, and the next report of each of the index's stocks.
// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
async Task<ImprovingCardView> ImprovingCardAsync(ReadApi read, string index, DateOnly night, IReadOnlyDictionary<string, UniverseCell> cells)
{
    var trades = await read.ImprovingTradesAsync(index, night);
    var answersOn = new Dictionary<DateOnly, IReadOnlyList<ImprovingResultRow>>();

    foreach (var on in trades.Select(trade => trade.ChosenOn).Distinct())
    {
        answersOn[on] = await read.ImprovingResultsAsync(index, on);
    }

    // A stock the quality card's rule holds tonight, or answered as dearer than its own usual price.
    var qualityRule = await read.QualityCardRuleAsync(index, night);
    var qualityHeld = (await read.QualityHoldingsAsync(index, night, night, qualityRule)).Where(holding => holding.EndedOn is null).Select(holding => holding.Ticker).ToHashSet(StringComparer.Ordinal);
    var quality = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var row in await read.QualityResultsAsync(index, night, qualityRule))
    {
        if (qualityHeld.Contains(row.Ticker))
        {
            quality[row.Ticker] = MarkRenderer.InTheQualityBook;
        }
        else if (row.Reason == EquityBrief.Core.Families.QualityRule.TooDear)
        {
            quality[row.Ticker] = MarkRenderer.AboveItsUsualPrice;
        }
    }

    return TonightScreen.Improving(
        index,
        night,
        await read.ImprovingNightAsync(index, night),
        await read.ImprovingResultsAsync(index, night),
        answersOn,
        trades,
        await read.ImprovingScoresAsync(index, night),
        await read.LedgerReadingsAsync(index, night),
        quality,
        cells,
        await read.ImprovingReportsAsync(night, TonightScreen.ReportingThrough(night)));
}

// Each family's live or provisional rule's record so far on an index as of a night, as Paper trading counts it off the
// same accounts: its finished trades against the count a verdict needs, a book's months against its own.
async Task<IReadOnlyDictionary<string, EquityBrief.Core.Cards.PaperProgress>> PaperProgressAsync(ReadApi read, UniverseChoice reading, DateOnly night)
{
    var large = reading == Universes.Large;
    var indexNight = await read.IndexNightAsync(reading.Code, night);
    var sources = PaperScreen.Accounts(
        large,
        reading.Name,
        night,
        await read.RegisteredCandidatesAsync(),
        indexNight is null ? null : IndexRuleSettings.Read(indexNight.Settings),
        await read.PaperRowsAsync(reading.Code),
        large ? PicksScreen.Cells(await read.PicksAsync(night), night) : [],
        PaperScreen.Yours(await read.TakenTradesAsync(), reading.Code),
        large ? await read.FamilyReplaysAsync() : [],
        TonightScreen.ReadAsOf(indexNight, night));

    return sources
        .Where(account => account.Standing is PaperAccountView.Live or PaperAccountView.Provisional)
        .GroupBy(account => account.Family, StringComparer.Ordinal)
        .ToDictionary(
            group => group.Key,
            group => EquityBrief.Core.Cards.PaperAccount.Progress(group.First().Book, [.. group.First().Trades.Select(one => one.Trade)], group.First().Since, night),
            StringComparer.Ordinal);
}

// An index's paper accounts as of a night: its stored trades in one shape, each costed, walked and judged, with the
// sessions they were walked over, each stock's company and night readings, the fund's closes and each open holding's
// standing tonight.
async Task<PaperAccounts> PaperAccountsAsync(ReadApi read, UniverseChoice reading, DateOnly night, EquityBrief.Core.Cards.AccountSettings? settings)
{
    var large = reading == Universes.Large;
    var rows = await read.PaperRowsAsync(reading.Code);
    var cells = large ? PicksScreen.Cells(await read.PicksAsync(night), night) : [];
    var indexNight = await read.IndexNightAsync(reading.Code, night);
    var provisional = indexNight is null ? null : IndexRuleSettings.Read(indexNight.Settings);
    var register = await read.RegisteredCandidatesAsync();
    // The register read as the night's page reads it, as of the instant a night run again read it at, on the S&P 500 off
    // its own index's night as on the S&P 400 and 600.
    // see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
    var sources = PaperScreen.Accounts(large, reading.Name, night, register, provisional, rows, cells, PaperScreen.Yours(await read.TakenTradesAsync(), reading.Code), large ? await read.FamilyReplaysAsync() : [], TonightScreen.ReadAsOf(indexNight, night));
    (string Ticker, DateOnly Session)[] bought = [.. sources.SelectMany(account => account.Trades.Select(one => (one.Trade.Ticker, one.Trade.Bought))).Distinct()];

    // The trades an account leaves out are read for their trade log's result so far, and walked by nothing.
    (string Ticker, DateOnly Session)[] earlier = [.. sources.SelectMany(account => (account.Earlier ?? []).Select(one => (one.Trade.Ticker, one.Trade.Bought))).Distinct()];
    var from = sources.Select(account => account.Since).OfType<DateOnly>().Concat(bought.Select(one => one.Session)).DefaultIfEmpty(night).Min();
    var earliest = earlier.Select(one => one.Session).Append(from).Min();
    var tickers = bought.Concat(earlier).Select(one => one.Ticker).Distinct(StringComparer.Ordinal).ToArray();
    var closes = await read.ClosesAsync(tickers, earliest);

    sources = PaperScreen.Costed(sources, rows, await read.MemberCostsAsync(reading.Code, bought), closes);

    var fund = await read.FundClosesAsync(PaperScreen.Fund(reading.Code), from);
    var (tests, years) = await read.RuleTestsAsync(reading.Code);
    var sessions = await read.SessionsFromAsync(earliest);
    var stocks = await read.PaperStocksAsync(tickers, night);

    decimal? Close(string ticker, DateOnly session) => closes.TryGetValue((ticker, session), out var close) ? close : null;

    decimal? Fund(DateOnly session) => fund.TryGetValue(session, out var close) ? close : null;

    return new PaperAccounts(
        sources,
        PaperScreen.Views(sources, tests, years, settings, sessions, night, Close, Fund),
        Close,
        sessions,
        stocks,
        Fund,
        await HoldingStandingsAsync(read, reading, night, sources, rows, register, provisional, stocks));
}

// The standing tonight of each holding the family's live or provisional heavyweights account holds open, worked by the
// rule's own functions off tonight's closes as the heavyweights card works it: its return over the book's look-back
// against its sector's, the sector's fund's on the S&P 500 and the index's members' mean on the S&P 400 and 600, and its
// close against its 200-session average; a design (b) book read for no lead of its own.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
static async Task<IReadOnlyDictionary<string, string>> HoldingStandingsAsync(
    ReadApi read,
    UniverseChoice reading,
    DateOnly night,
    IReadOnlyList<PaperAccountSource> sources,
    IReadOnlyList<PaperRow> rows,
    IReadOnlyList<CandidateRow> register,
    IndexRuleSettings? provisional,
    IReadOnlyDictionary<string, PaperStockRow> stocks)
{
    var large = reading == Universes.Large;
    var head = sources.FirstOrDefault(account => account.Family == EquityBrief.Core.Families.HeavyweightRule.Name && account.Standing is PaperAccountView.Live or PaperAccountView.Provisional);
    var open = head is null ? [] : head.Trades.Where(one => one.Trade.Ended is null).Select(one => one.Trade.Ticker).Distinct(StringComparer.Ordinal).ToArray();

    if (head is null || open.Length == 0)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal);
    }

    var settings = large ? null : RuleScreen.HeavyweightSettingsOf(head.Listed, register);
    var lookBack = large
        ? EquityBrief.Core.Families.HeavyweightRule.Live.LookBack
        : settings?.LookBack ?? (provisional is not null && int.TryParse(provisional.Heavyweights.GetValueOrDefault("look-back"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessions) ? sessions : EquityBrief.Core.Families.HeavyweightRule.LookBack);
    var fundReturn = large && EquityBrief.Core.Families.HeavyweightRule.Live.FundReturn;
    var designB = !large && RuleScreen.DesignB(head.Listed, register);
    var funds = await read.FundStandingClosesAsync([.. EquityBrief.Core.Families.GicsSectors.Funds.Values], night, lookBack, null);
    var members = fundReturn || designB ? new Dictionary<string, string?>(StringComparer.Ordinal) : await read.MemberSectorsAsync(reading.Code, night);
    var closes = await read.StockStandingClosesAsync([.. open.Concat(members.Keys).Distinct(StringComparer.Ordinal)], night, lookBack, null);

    double? SectorReturn(string? sector) =>
        sector is null ? null
        : fundReturn
            ? EquityBrief.Core.Families.GicsSectors.Funds.TryGetValue(sector, out var fund) && funds.TryGetValue(fund, out var held) ? TonightScreen.ReturnOf(held) : null
            : EquityBrief.Core.Families.HeavyweightStandings.MembersMean(members.Where(member => member.Value == sector).Select(member => closes.TryGetValue(member.Key, out var held) ? TonightScreen.ReturnOf(held) : null));

    return open.ToDictionary(
        ticker => ticker,
        ticker =>
        {
            var held = closes.GetValueOrDefault(ticker) ?? new StandingCloses(null, null, null);
            var sector = rows.Where(row => row.Book && row.Ticker == ticker && head.Keeps(row) && row.Ended is null).Select(row => row.Sector).FirstOrDefault();
            var average = stocks.TryGetValue(ticker, out var stock) ? stock.Average200 : null;

            return designB
                ? EquityBrief.Core.Families.HeavyweightStandings.NotForThisDesign
                : EquityBrief.Core.Families.HeavyweightStandings.Of(held.Now is { } now ? EquityBrief.Core.Prices.Statistic.FromPrice(now) : null, held.Back is { } back ? EquityBrief.Core.Prices.Statistic.FromPrice(back) : null, average, SectorReturn(sector)).Word;
        },
        StringComparer.Ordinal);
}

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

// The Loop page's presses: approve or decline of a proposal of an index's newest run, a decline with its reason, and
// approve of a restore of a family the live alarm flagged, each one row of the decisions table, applied by the worker.
// see: An approved change is applied by the night's own build on the index it was approved on alone
app.MapPost(SinglePageApp.LoopDecidePostRoute + "{index}/{run}/{family}", async (string index, string run, string family, HttpRequest request, ReadApi read) =>
{
    if (!FromAPage(request))
    {
        return Refused("nothing was decided");
    }

    var form = request.HasFormContentType ? await request.ReadFormAsync() : null;
    var decision = form?["decision"].ToString();

    if (form?["proposal"].ToString() is not { Length: > 0 } proposal || decision is not ("approve" or "decline"))
    {
        return Said(false, "Nothing was decided: the press named no proposal or no decision.");
    }

    var written = await read.DecideAsync(index, run, family, proposal, decision == "approve", form["reason"].ToString());

    return Said(written.Written, written.Line);
});

app.MapPost(SinglePageApp.LoopRestorePostRoute + "{index}/{family}/{setting}", async (string index, string family, long setting, HttpRequest request, ReadApi read) =>
{
    if (!FromAPage(request))
    {
        return Refused("nothing was restored");
    }

    var written = await read.RestoreAsync(index, family, setting);

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

    // The S&P 500's swing filter rows and family answers of the night, which the one gate short chip and the dry-up's
    // ceiling read; from 20.11 the S&P 400's and 600's every part's answer their night stored, the first family in the one
    // order a member is one gate short of taken, and none on a night storing only the first part each member failed.
    // see: The S&P 400's and 600's nights store every part's answer for every member, the market check its own
    var large = reading == Universes.Large;
    IReadOnlyList<GateResultRow> gates = large && night is { } gatesOn ? await read.GateResultsAsync(gatesOn) : [];
    var indexShort = !large && night is { } indexOn
        ? TonightScreen.IndexCloseAcross(await read.IndexResultsAsync(reading.Code, indexOn), await read.IndexMembersAsync(reading.Code, indexOn))
        : null;
    var swingShort = large && night is { } shortOn
        ? UniverseScreen.OneShort(gates, await read.FamilyResultsAsync(shortOn))
        : (indexShort ?? []).GroupBy(cell => cell.Ticker, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (group.First().Family, group.First().Gate), StringComparer.Ordinal);

    // The short-term dip's stocks one gate short beside them, as Tonight's card of those stocks draws them, a stock one
    // gate short of an earlier family in the one order taken under that family.
    // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
    var dipShort = night is { } dipOn
        ? TonightScreen.DipCloseAcross(await read.DipResultsAsync(reading.Code, dipOn), await read.IndexMembersAsync(reading.Code, dipOn))
        : [];
    var oneShort = new Dictionary<string, (string Family, string Gate)>(swingShort, StringComparer.Ordinal);

    foreach (var cell in dipShort)
    {
        oneShort.TryAdd(cell.Ticker, (cell.Family, cell.Gate));
    }

    var shortRead = large || indexShort is not null || dipShort.Count > 0;

    IReadOnlyList<UniverseCell> cells =
    [
        .. UniverseScreen.Rows(members, history, events, night, readings)
            .Select(cell => oneShort.TryGetValue(cell.Ticker, out var shortOf) ? cell with { ShortFamily = shortOf.Family, ShortGate = shortOf.Gate } : cell),
    ];

    var trend = request.Query["trend"].FirstOrDefault();
    var sector = request.Query["sector"].FirstOrDefault();
    var near = request.Query["near"].FirstOrDefault();
    var sort = request.Query["sort"].FirstOrDefault();
    var perPage = request.Query["rows"].FirstOrDefault();
    var query = new UniverseScreen.Query(
        trend,
        sector,
        request.Query["q"].FirstOrDefault() is { Length: > 0 } words ? words.Trim() : null,
        near is UniverseScreen.NearSupport or UniverseScreen.NearResistance ? near : null,
        shortRead && request.Query["short"].FirstOrDefault() == "1",
        sort is not null && UniverseScreen.SortKeys.Contains(sort, StringComparer.Ordinal) ? sort : null,
        request.Query["dir"].FirstOrDefault() == "desc",
        perPage == UniverseScreen.AllRows || (int.TryParse(perPage, NumberStyles.Integer, CultureInfo.InvariantCulture, out var size) && size != UniverseScreen.PageSize && UniverseScreen.PageSizes.Contains(size)) ? perPage : null,
        int.TryParse(request.Query["page"].FirstOrDefault(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asked) ? asked : null);

    // The page, from the hash beside the filters. The filters, the search, the chips and the sort come before the cut,
    // because the order between them is the property: a page taken from the whole table and then filtered is a short
    // page about the wrong names, and nothing on the screen contradicts it.
    var kept = UniverseScreen.Kept(cells, query, oneShort.Keys.ToHashSet(StringComparer.Ordinal));
    var shown = UniverseScreen.Paged(kept, query.Rows, query.Page);

    // The stored closes of the names the page draws and of no other, each drawn as its year line
    // beside its ticker.
    var drawn = UniverseScreen.WithYears(shown.Page, await read.ClosesAsync([.. shown.Page.Select(cell => cell.Ticker)]));

    // A tile a GICS sector: the members tonight's lists hold, the page's list on the S&P 500 and the trades the index's
    // lists kept tonight on the S&P 400 and 600, and each sector fund's return over the live heavyweights look-back.
    // see: Every page names a member's sector by the GICS sector its company fetch files
    var listedTonight = night is not { } listedOn ? new HashSet<string>(StringComparer.Ordinal)
        : large ? cells.Where(cell => cell.LastListed == listedOn).Select(cell => cell.Ticker).ToHashSet(StringComparer.Ordinal)
        : (await read.IndexTradesAsync(reading.Code, listedOn)).Where(trade => trade.Listed == listedOn).Select(trade => trade.Ticker).ToHashSet(StringComparer.Ordinal);
    var funds = night is { } fundsOn
        ? await read.FundStandingClosesAsync([.. EquityBrief.Core.Families.GicsSectors.Funds.Values], fundsOn, EquityBrief.Core.Families.HeavyweightRule.LookBack, null)
        : new Dictionary<string, StandingCloses>(StringComparer.Ordinal);
    var tiles = UniverseScreen.Tiles(
        cells,
        large,
        listedTonight,
        one => EquityBrief.Core.Families.GicsSectors.Funds.TryGetValue(one, out var fund)
            ? (fund, funds.TryGetValue(fund, out var closes) ? TonightScreen.ReturnOf(closes) : null)
            : null);

    var view = new UniverseView(
        new UniverseState(query.Search, query.Near, query.Short, query.Sort, query.Descending, query.Rows),
        kept,
        tiles,
        cells.Count(cell => cell.Sector == UniverseScreen.SectorNotOnFile),
        EquityBrief.Core.Families.HeavyweightRule.LookBack,
        shortRead,
        UniverseScreen.PageSizes,
        gates.Count > 0 ? (await FilterSettingsOf(read, gates[0].Version)).DryUpCeiling : null);

    return Results.Content(
        page.UniverseRegion(
            marks,
            cells,
            UniverseScreen.Sectors(cells),
            trend,
            sector,
            drawn,
            shown.At,
            query.Rows == UniverseScreen.AllRows ? Math.Max(1, kept.Count) : UniverseScreen.SizeOf(query.Rows),
            TonightScreen.WrittenBeforeTheCorrection(history.Values.SelectMany(rows => rows)),
            night,
            reading,
            night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.UniverseRoute) : string.Empty,
            view),
        "text/html; charset=utf-8");
});

// The masthead search's list, section 15.13: every current member of the S&P 500, 400 and 600
// by its ticker, whichever index the page reads, with its company's name and the day its
// research was written.
app.MapGet("/screens/find", async (ReadApi read, SinglePageApp page) =>
{
    var indices = Universes.Offered.Select(choice => choice == Universes.Large ? builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC" : choice.Code).ToList();
    var names = await read.FindableAsync(indices, await read.NewestNightAsync());

    return Results.Content(
        page.FindOptions([.. names.Select(name => new Findable(name.Ticker, name.Name, name.Researched))]),
        "text/html; charset=utf-8");
});

// The Past picks screen, section 15.17: every trade the live list recommended from the swing filter's
// first night, as of the newest night, under the status filter the hash carries.
// see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
app.MapGet("/screens/picks", async (HttpRequest request, ReadApi read, MarkRenderer marks, SinglePageApp page, StoreLocation store) =>
{
    var night = await read.NewestNightAsync();

    // The index the page reads, chosen under Universe and kept in the link: an S&P 400's or 600's trades and holdings
    // where it names one, and the S&P 500's otherwise.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    var reading = Universes.Of(request.Query[Universes.Query].FirstOrDefault());
    var selector = night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.PicksRoute) : string.Empty;

    // Each paper account's trade log, chosen in the link: a live or provisional rule's, a variant's or yours, the trades
    // it counts with its verdict and its record's label beside them and those it leaves out folded beneath; the page's
    // lists where the link names none, with each family's verdict beside them.
    // see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
    var paper = night is { } walkedOn ? await PaperAccountsAsync(read, reading, walkedOn, EquityBrief.Core.Cards.AccountFile.Read(store.DataRoot) ?? EquityBrief.Core.Cards.AccountSettings.Example) : null;
    var askedAccount = request.Query[PaperScreen.AccountQuery].FirstOrDefault();

    if (paper is { } accounts && night is { } logged && accounts.Views.FirstOrDefault(view => view.Key == askedAccount) is { } chosenAccount)
    {
        var source = accounts.Sources.First(one => one.Key == chosenAccount.Key);

        return Results.Content(
            page.AccountLogRegion(marks, reading, night, accounts.Views, chosenAccount, PaperScreen.Log(source, chosenAccount.Walk, logged, accounts.Close), PaperScreen.EarlierLog(source, logged, accounts.Close)),
            "text/html; charset=utf-8");
    }

    selector += paper is null ? string.Empty : SinglePageApp.AccountSelector(paper.Views, null) + SinglePageApp.ListsVerdicts(paper.Views);

    if (reading != Universes.Large)
    {
        var members = night is { } on ? await read.IndexMembersAsync(reading.Code, on) : [];
        var companies = members.ToDictionary(member => member.Ticker, member => member.Company, StringComparer.Ordinal);

        // The families the night read by their live rules, which the page names as frozen and no other.
        // see: The S&P 400's and 600's families freeze at the settings their own sweeps would run, the S&P 400 drift at the setting the operator ruled and business first on the pullback's base
        var frozenOn = night is { } frozenNight ? TonightScreen.FrozenOn(reading, await read.RegisteredCandidatesAsync(), await read.IndexNightAsync(reading.Code, frozenNight)) : [];

        // Every trade the index's lists kept with the short-term dip's among them, each under its setup's label, and the
        // setup the hash names keeping its own; the fundamentals-first family's none, its rule running out of sight.
        // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
        // see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
        var keptTrades = night is { } through
            ? PicksScreen.IndexTrades([.. (await read.IndexTradesAsync(reading.Code, through)).Where(trade => trade.Family != EquityBrief.Core.Families.FundamentalsRule.Name), .. await read.DipTradesAsync(reading.Code, through)], companies)
            : [];
        var keptUnder = request.Query["setup"].FirstOrDefault();

        return Results.Content(
            page.IndexPicksRegion(
                marks,
                night,
                reading,
                selector,
                PicksScreen.OfIndexSetup(keptTrades, keptUnder),
                night is { } kept ? PicksScreen.IndexHeavyweights(await read.IndexHoldingsAsync(reading.Code, kept)) : [],
                frozenOn,
                keptUnder,
                PicksScreen.IndexSetups(keptTrades)) +
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

    // The short-term dip's S&P 500 trades, which its own step keeps apart from every other family's, and its chip among
    // the setups.
    // see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
    var dipTrades = night is { } dipOn
        ? PicksScreen.IndexTrades(
            await read.DipTradesAsync(Universes.Large.Code, dipOn),
            (await read.IndexMembersAsync(Universes.Large.Code, dipOn)).ToDictionary(member => member.Ticker, member => member.Company, StringComparer.Ordinal))
        : [];
    var setups = PicksScreen.Setups(cells, dipTrades.Count);

    // The sector heavyweights' holdings, in percent, beneath the swing trades.
    // see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
    var heavyweights = night is { } held ? PicksScreen.Heavyweights(await read.HeavyweightHoldingsAsync(held)) : [];

    // The fundamentals-first family's trades are drawn on no page's list of families, its rule running out of sight.
    // see: Business first leaves Tonight, paper trading and Past picks while its rule keeps running out of sight
    return Results.Content(
        page.PicksRegion(marks, night, PicksScreen.Summary(under), PicksScreen.Filtered(under, status), status, setup, setups, heavyweights, selector, null, dipTrades) +
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
    var family = years.Any(year => year.Family == asked) ? asked : years.Select(year => year.Family).OrderBy(EquityBrief.Core.Families.SetupFamilies.OrderOf).FirstOrDefault();
    var settled = family is null ? [] : await read.LedgerSettledAsync(reading.Code, family, 12);
    EquityBrief.Core.Ledger.LedgerPath? path = null;

    // The chosen setup named as its stock and its session, the last full stop parting the two.
    if (family is not null && request.Query["setup"].FirstOrDefault() is { } chosen && chosen.LastIndexOf('.') is var cut and > 0
        && DateOnly.TryParseExact(chosen[(cut + 1)..], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var session))
    {
        path = await read.LedgerPathAsync(reading.Code, family, chosen[..cut], session, night ?? session);
    }

    var links = family is null ? null : await read.LedgerLinksAsync(reading.Code, family);
    var answers = family is null ? null : await read.LedgerAnswersAsync(reading.Code, family);

    return Results.Content(page.LedgerRegion(marks, night, reading, selector, years, family, settled, path, links, answers), "text/html; charset=utf-8");
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
    var spreads = run is null ? [] : await read.LoopReadingsAsync(run);
    var models = run is null ? [] : await read.LoopModelsAsync(run);
    var ranks = night is { } ranked ? await read.LoopRanksAsync(reading.Code, ranked) : [];
    var decisions = await read.LoopDecisionsAsync(reading.Code);
    var settings = await read.LoopSettingsAsync(reading.Code);
    var alarms = await read.LoopAlarmsAsync(reading.Code);
    var newest = await read.NewestLoopRunAsync(reading.Code);

    // From 19.7 each family's rule standing on the index as its card's chips draw it, for the summary's rule today, the
    // fundamentals-first family's on the S&P 500 as well off the settings its night stored there, as its card reads them.
    var large = reading == Universes.Large;
    var register = await read.RegisteredCandidatesAsync();
    var loopNight = night is not { } stored ? null : await read.IndexNightAsync(reading.Code, stored);
    var provisional = loopNight is null ? null : IndexRuleSettings.Read(loopNight.Settings);
    // Each index's rules read as its night's page reads them, as of the instant a night run again read them at, on the
    // S&P 500 off its own index's night as on the S&P 400 and 600.
    // see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
    DateTimeOffset? loopAsOf = night is not { } readOn ? null : TonightScreen.ReadAsOf(loopNight, readOn);
    var chips = EquityBrief.Core.Families.SetupFamilies.Order.ToDictionary(
        family => family,
        family =>
        {
            var (live, _) = RuleScreen.Accounts(family, large, reading.Name, register, night ?? DateOnly.MinValue, large ? null : provisional, loopAsOf);
            var readsNight = !large || family == EquityBrief.Core.Families.FundamentalsRule.Name;

            return (IReadOnlyList<string>)[.. (live is not null ? RuleScreen.ChipsOf(live) : readsNight && provisional is not null ? RuleScreen.ProvisionalChips(family, provisional) : []).Select(chip => chip.Words)];
        },
        StringComparer.Ordinal);

    // From 20.8 the local model's cutoff, which a proposal reading a release reading is split at, and from its correction
    // of 2026-10-19 held back by over the run's test years.
    var cutoff = await read.ModelCutoffAsync();

    return Results.Content(page.LoopRegion(marks, night, reading, selector, months, run, proposals, tests, findings, spreads, models, ranks, new EquityBrief.Web.Marks.LoopDecided(decisions, settings, alarms, newest, cutoff, tests), chips), "text/html; charset=utf-8");
});

// The Loop's badge in the menu: the proposals each index's newest run puts to the operator that hold no word yet.
app.MapGet(SinglePageApp.LoopPutRoute, async (ReadApi read) =>
{
    var put = 0;
    var cutoff = await read.ModelCutoffAsync();

    foreach (var index in Universes.Offered)
    {
        if ((await read.LoopMonthsAsync(index.Code)).FirstOrDefault() is { } month && await read.LoopRunAsync(index.Code, month) is { } run)
        {
            put += SinglePageApp.PutWithoutWord(run, await read.LoopProposalsAsync(run), new EquityBrief.Web.Marks.LoopDecided(await read.LoopDecisionsAsync(index.Code), [], [], null, cutoff, await read.LoopTestsAsync(run)));
        }
    }

    return Results.Text(put.ToString(System.Globalization.CultureInfo.InvariantCulture), "text/plain; charset=utf-8");
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

    // From 19.7 each report's staleness by the judge its stock's page calls, the move since it was written off the store's
    // closes, its sections out of the full set and why it was written, the list sorted by the day written or by staleness.
    var researched = (await read.ResearchedAsync()).Where(row => Reads(row.Ticker)).ToArray();
    var requests = await read.WrittenRequestsAsync();
    var closes = researched.Length == 0 ? new Dictionary<(string, DateOnly), decimal>() : await read.ClosesAsync([.. researched.Select(row => row.Ticker)], researched.Min(row => row.Written).AddDays(-10));
    var cells = new List<ResearchedCell>();

    foreach (var row in researched)
    {
        var staleness = await read.StalenessAsync(row.Ticker);

        cells.Add(new ResearchedCell(
            row.Ticker,
            row.Name,
            row.Sector,
            row.Written,
            row.Sections,
            staleness.State == EquityBrief.Core.Research.ResearchState.Stale,
            [.. staleness.Fired.Select(fired => fired.Line)],
            ResearchedScreen.Move(row.Written, closes, row.Ticker),
            EquityBrief.Core.Research.ClaimRules.Sections.Length,
            ResearchedScreen.Why(row.Written, [.. requests.Where(request => request.Ticker == row.Ticker)])));
    }

    var sort = request.Query[ResearchedScreen.SortQuery].FirstOrDefault() == ResearchedScreen.ByStaleness ? ResearchedScreen.ByStaleness : ResearchedScreen.ByWritten;

    return Results.Content(
        page.ResearchedRegion(
            ResearchedScreen.Sorted(cells, sort),
            reading,
            night is { } counted ? Cards.Universe(reading, await read.MembersByIndexAsync(counted), SinglePageApp.ResearchedRoute) : string.Empty,
            sort),
        "text/html; charset=utf-8");
});

// The queue, section 15.15, read here and composed by the app. It reads the request
// store and writes nothing: the presses that write it are the two routes above. When each
// request will be written is worked out from the queue, the passes that ran to their end,
// the peak windows the worker's prices state and the instant the page is drawn at, and
// marked in New York's time with UTC beneath the pointer. What settled is drawn a row a
// stock and New York day over the window to the page's own day, or over every day where
// `all` asks for them, beside the passes no request names with what they cost.
// see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
// see: The queue page draws settled requests a row a stock and New York day over fourteen days, and the passes no request names a row a day
app.MapGet("/screens/queue", async (string? all, ReadApi read, SinglePageApp page, IClock clock) =>
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
                    QueueTimes.Words(times[at], row, clock.SessionZone)),
                QueueTimes.Marked(row.AskedAt, clock.SessionZone))),
        ],
        QueueScreen.Drawn(rows, await read.PaidCallSpendsAsync(), clock.SessionZone, clock.UtcNow, all is { Length: > 0 }),
        new QueueEstimate(
            estimate.Count,
            estimate.Median is { } median ? QueueTimes.Minutes(median) : null,
            estimate.Longest is { } longest ? QueueTimes.Minutes(longest) : null),
        stop is null
            ? null
            : new QueueStop(
                stop.StartedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                QueueTimes.Marked(stop.StartedAt, clock.SessionZone),
                stop.Error),
        QueueTimes.Peak(QueuePrices(builder.Configuration), clock.UtcNow) is { } peak
            ? new QueuePeak(QueueTimes.Marked(peak.Ends, clock.SessionZone), QueueTimes.Times(peak.Multiple))
            : null),
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
    StoreLocation store,
    RouteTimes times) =>
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

        // Each registered rule's record, from the index's first freeze on, and its family's setup row live beside it, the
        // register read as of the instant the night read it at where a night run again read it later.
        // see: A night run again for the newest session reads the register as it stands when it runs where the operator says so, and its page reads the rules that run read
        var indexAsOf = TonightScreen.ReadAsOf(indexNight, dated);
        var indexRecords = RunScreen.Named(
            TonightScreen.IndexRecordRows(reading, indexRegister, await read.IndexRuleTradesAsync(reading.Code, dated), dated, indexAsOf),
            indexRegister,
            dated,
            false,
            reading.Name,
            (await read.RuleTestsAsync(reading.Code)).Rules,
            indexAsOf);

        // The short-term dip's own rows, its row among the setups and its counts in how the night went.
        var runDipNight = await read.DipNightAsync(reading.Code, dated);
        var runDipPicks = await read.DipPicksAsync(reading.Code, dated);
        var runDipTrades = await read.DipTradesAsync(reading.Code, dated);
        var runResults = await read.IndexResultsAsync(reading.Code, dated);

        return Results.Content(
            page.IndexRunRegion(
                marks,
                dated,
                reading,
                selector,
                await read.IndexNightsAsync(reading.Code),
                indexNight is null
                    ? null
                    : TonightScreen.WithDipSummary(
                        TonightScreen.IndexRun(indexNight, indexPicks, runResults, indexTrades, indexHoldings),
                        runResults,
                        await read.DipResultsAsync(reading.Code, dated),
                        runDipPicks,
                        runDipTrades,
                        dated),
                TonightScreen.WithDipRun(
                    TonightScreen.WithIndexFreezes(TonightScreen.IndexFamilyRun(indexPicks, indexTrades, indexHoldings), reading, indexRegister, indexRecords, indexNight, dated),
                    runDipNight,
                    runDipPicks,
                    runDipTrades,
                    indexRegister,
                    indexAsOf),
                indexRecords,
                RuleScreen.Flagged(await read.RuleNightsAsync(reading.Code, dated), dated, indexRegister, indexAsOf)),
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
    var nightListings = await read.ListingsAsync(dated);
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

    // The shape clock, read once for the Calibration region and the monthly run's line; and from 19.7 the S&P 500's newest
    // tester run with its proposals and the operator's words, which the monthly run region reads what was put from.
    var shapeState = RunScreen.Calibration(
        await read.GateNightsAsync(),
        await read.MarketRatiosAsync(),
        RunScreen.Firings(everyListing),
        await read.OpenFilterVersionAsync(),
        dated);
    var monthlyRun = (await read.LoopMonthsAsync(Universes.Large.Code)).FirstOrDefault() is { } newestMonth ? await read.LoopRunAsync(Universes.Large.Code, newestMonth) : null;
    var monthlyProposals = monthlyRun is null ? [] : await read.LoopProposalsAsync(monthlyRun);
    var monthlyDecisions = await read.LoopDecisionsAsync(Universes.Large.Code);

    // From the 20.8 correction of 2026-10-19 the local model's cutoff and the run's test years, which a proposal reading a
    // release reading is held back by.
    var monthlyHeld = new LoopDecided(monthlyDecisions, [], [], null, await read.ModelCutoffAsync(), monthlyRun is null ? [] : await read.LoopTestsAsync(monthlyRun));

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
            shapeState,
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
                NewsScreen.Line(dated, NewsScreen.RunFor(labellerRuns, dated)),
                // The quotes the name pages asked for in the night's own session, against the day's cap.
                // owes: The day's live quotes stay under their cap on the first five sessions
                RegularSession.On(dated, clock.SessionZone) is { } quoted ? await read.QuoteRunsAsync(quoted.Open, quoted.Close) : null),
            worries: RunScreen.Worries(
                await read.StaleNamesAsync(index, dated),
                how,
                RunScreen.Refused(await read.RefusedDocumentsAsync(dated)),
                RunScreen.FellBack(await read.FellBackAsync(dated)),
                log,
                [.. EquityBrief.Core.Providers.ModelProfiles.Jobs.Select(job => Profile(builder.Configuration, job)).OfType<EquityBrief.Core.Providers.ModelProfile>()],
                storeCopy,
                RuleScreen.Flagged(await read.RuleNightsAsync(index, dated), dated, await read.RegisteredCandidatesAsync()),
                await read.NewestMonthlyRunAsync()),
            monthly: RunScreen.Monthly(
                await read.NewestMonthlyRunAsync(),
                monthlyProposals,
                one => monthlyRun is { } tested && MarkRenderer.HeldBack(tested, one, monthlyHeld),
                (await read.RuleTestsAsync(Universes.Large.Code)).Rules,
                shapeState),
            compare: compare,
            reports: reports,
            // The setups the page is drawn from, on a night the families drew its list, and each registered rule
            // of a new setup read over its own trades.
            setupFamilies: await read.FamilyNightAsync(dated) is null
                ? null
                : TonightScreen.WithDipRun(
                    TonightScreen.WithBusinessFirst(
                        TonightScreen.FamilyRun(
                            dated,
                            await read.FamilyPicksAsync(dated),
                            PicksScreen.Cells(await read.PicksAsync(dated), dated),
                            await read.RegisteredCandidatesAsync(),
                            await read.FamilyTradesAsync(dated),
                            await read.FamilyReplaysAsync()),
                        await read.IndexPicksAsync(Universes.Large.Code, dated),
                        await read.IndexTradesAsync(Universes.Large.Code, dated)),
                    await read.DipNightAsync(Universes.Large.Code, dated),
                    await read.DipPicksAsync(Universes.Large.Code, dated),
                    await read.DipTradesAsync(Universes.Large.Code, dated),
                    await read.RegisteredCandidatesAsync(),
                    TonightScreen.ReadAsOf(await read.IndexNightAsync(Universes.Large.Code, dated), dated)),
            familyRecords: RunScreen.Named(
                TonightScreen.FamilyRecordRows(await read.RegisteredCandidatesAsync(), await read.FamilyTradesAsync(dated), dated, await read.FamilyReplaysAsync()),
                await read.RegisteredCandidatesAsync(),
                dated,
                true,
                Universes.Large.Name,
                (await read.RuleTestsAsync(Universes.Large.Code)).Rules),
            storeCopy: storeCopy,
            localRuns: RunScreen.LocalRuns(await read.LocalRunsAsync()),
            selector: selector,
            // From 19.2 the night's operations and its reason totals, which Tonight drew before.
            duration: await read.NightDurationAsync(dated),
            spend: TonightScreen.Spend(dated, await SpentOn(read, dated), caps),
            prose: TonightScreen.Prose(dated, await read.WrittenOnOrBeforeAsync(dated)),
            totals: RunScreen.Tracks(TonightScreen.Totals(nightListings)),
            fired: TonightScreen.Fired(nightListings),
            listedByFilter: await read.ListRuleAsync(dated) == ListRules.Filter,
            // From 19.5 the sessions written before a correction, folded here once the notice is past its age.
            // see: A correction notice older than thirty days by the newest session it affects is folded onto the Run page
            oldCorrections: TonightScreen.WrittenBeforeTheCorrection(everyListing.Where(listing => listing.SessionDate <= dated)),
            // From 19.11 every page's time since the read surface started, kept in memory alone.
            routeTimes: times.Slowest(),
            routeTimesSince: times.Since),
        "text/html; charset=utf-8");
});

// One run log row for each start of the surface, which is the grain SCHEMA declares
// and what section 15.10's run page reads. Written after the host is built so a
// store that cannot be opened fails the start rather than a request.
await app.Services.GetRequiredService<ReadApi>().RecordStartAsync(
    ReadApi.StartRunId(app.Services.GetRequiredService<IClock>().UtcNow),
    "the read surface started");

app.Run();

// So the suite can reach the host's composition. A test project referencing a
// top-level program needs the generated class to be visible.
public partial class Program;
