using System.Globalization;
using System.Text.Json;
using EquityBrief.Api.Passes;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

// The read surface and the one page it hosts.
//
// Everything served here is stored or drawn. Nothing is computed and nothing is
// fetched: ReadApi selects, MarkRenderer turns what it selected into SVG, and
// SinglePageApp writes the shell. api-isolation asserts from the compiled
// dependency file that no path from here reaches the worker.

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IClock>(SystemClock.ForUnitedStatesSessions());
builder.Services.AddSingleton(_ =>
    new StoreLocation(builder.Configuration[StoreLocation.DataRootKey] ?? string.Empty));
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

// What the name page's control starts: the worker's research verb, from the checkout this
// surface's build sits in, told to write the store this surface reads.
builder.Services.AddSingleton<IPassStarter>(services => new WorkerPassStarter(
    WorkerPassStarter.Checkout(AppContext.BaseDirectory),
    services.GetRequiredService<StoreLocation>().DataRoot));

var app = builder.Build();

app.MapGet("/", (SinglePageApp page) =>
    Results.Content(page.Shell("EquityBrief"), "text/html; charset=utf-8"));

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
app.MapGet("/screens/name/{ticker}", async (string ticker, ReadApi read, MarkRenderer marks, SinglePageApp page, SpendCaps caps, IClock clock) =>
{
    var bars = await read.BarsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue);
    var indicators = await read.IndicatorsAsync(ticker, DateOnly.MinValue, DateOnly.MaxValue);
    var levels = await read.LevelsAsync(ticker);
    var profile = await read.ProfileAsync(ticker);
    var ladder = await read.LadderAsync(ticker);
    var moves = await read.MovesAsync(ticker);

    // Tonight's listing for this name, and its neighbours on the list, so the
    // page can say why it is here and the walk is one pass through.
    var night = await read.NewestNightAsync();
    var listings = night is { } dated ? await read.ListingsAsync(dated) : [];
    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";

    // The index on the night the list is from, so the neighbours are that
    // night's members rather than today's.
    var universe = await read.UniverseAsync(index, night);

    var strengths = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var member in universe)
    {
        var found = await read.LevelsAsync(member.Ticker);

        strengths[member.Ticker] = found.Count == 0 ? 0 : found.Max(band => band.Strength);
    }

    // The neighbours on the list, in the order the list itself is drawn in. The
    // walk is about position, so it takes the same ordering with the same inputs
    // rather than a cheaper one that could order differently.
    var ordered = night is { } on
        ? TonightScreen.Rows(
            on,
            listings,
            strengths,
            UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal),
            await read.ClosesToTheNightAsync(on))
        : [];

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
    var fundamentals = await read.FundamentalsAsync(ticker);

    // The high and the low of the sessions the largest move spans, which the fact
    // strip states beside the close. A name with no annotated move has none, and
    // the strip says so rather than drawing a blank.
    var extremes = await read.MoveExtremesAsync(ticker);

    // The written sections and the documents they cite, which the dates-and-sources
    // region draws with the calendar from the newest stored session on. The industry
    // cycle among them is the theme's, read for the industry the index names the member in.
    var written = await read.WrittenSectionsAsync(ticker);

    return Results.Content(
        NameScreen.Region(
            page, marks, ticker, bars, indicators, levels, profile, ladder, nextEvent, moves,
            fundamentals,
            extremes,
            listings.FirstOrDefault(listing => listing.Ticker == ticker),
            at is > 0 ? ordered[at.Value - 1].Ticker : null,
            at is { } position && position + 1 < ordered.Count ? ordered[position + 1].Ticker : null,
            await read.SectionStatesAsync(ticker, DateOnly.MaxValue),
            await read.StalenessAsync(ticker),
            written,
            await read.NewestPassAsync(ticker),
            await SpendNow(read, caps, clock),
            await read.CitedDocumentsAsync(NameScreen.Cited(written)),
            await read.EventsAsync(ticker, bars.Count > 0 ? bars[^1].SessionDate : DateOnly.MinValue),
            await read.PaidCallSpendsAsync(),
            clock.SessionDateAt(clock.UtcNow)),
        "text/html; charset=utf-8");
});

// A press of the name page's control: start the worker's research verb for the name, and
// return at once with the line the page puts beside the control.
//
// Refused without the page's own header, so another site's page cannot start a pass, and
// refused for a name the index does not hold, before anything is started. Nothing here
// writes a store: the pass is the worker's, and its rows are what the page reads next.
// see: The name page's control starts the worker's research verb, and the read API writes nothing it starts
// see: A pass is started only by a request carrying the name page's own header
app.MapPost(SinglePageApp.PassRoute + "{ticker}", async (string ticker, HttpRequest request, ReadApi read, IPassStarter starter) =>
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

    var started = starter.Start(new PassRequest(
        ticker,
        string.Equals(form?["refresh"].FirstOrDefault(), "true", StringComparison.Ordinal),
        string.Equals(form?["paidForLocal"].FirstOrDefault(), "true", StringComparison.Ordinal)));

    return Results.Content(
        $"<p class=\"pass-started\" data-started=\"{(started.Started ? "true" : "false")}\">{System.Net.WebUtility.HtmlEncode(started.Line)}</p>",
        "text/html; charset=utf-8",
        statusCode: started.Started ? StatusCodes.Status202Accepted : StatusCodes.Status500InternalServerError);
});

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

// The phase report the harness last wrote, read as text and handed to the
// projection rather than opened by it, so nothing on the read surface reaches
// the filesystem for a store it does not own. A machine with no report says so
// rather than showing four zeros.
//
// Two screens read it since 5.8: section 15.10 gives the harness a region of its
// own on the run page, and section 15.7 states the verdict in tonight's header.
// One function rather than one per route, so the two cannot come to disagree
// about which file is the report.
static string? PhaseReport(WebApplicationBuilder builder)
{
    var path = builder.Configuration["EquityBrief:PhaseReport"]
        ?? Path.Combine(builder.Environment.ContentRootPath, "artifacts", "phase-report.json");

    return File.Exists(path) ? File.ReadAllText(path) : null;
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
    SpendCaps caps) =>
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

    var listings = await read.ListingsAsync(dated);

    // Section 18's banner. A night the store has no listings for shows the data
    // date it does have rather than a list built from older bars.
    if (listings.Count == 0)
    {
        return Results.Content(
            page.StaleBanner(dated, await read.NewestNightAsync()),
            "text/html; charset=utf-8");
    }

    // The index on the night shown, so a name on that night's list has the close
    // that night stored even after it has left.
    var universe = await read.UniverseAsync(index, dated);

    // The band strength the ordering breaks ties on, read from what the night
    // stored rather than worked out here. The close each row shows comes from the
    // night's own bar, beside the session before it, because a close from one
    // session and a change computed from another is one row saying two things.
    var strengths = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var row in universe)
    {
        var bands = await read.LevelsAsync(row.Ticker);

        strengths[row.Ticker] = bands.Count == 0 ? 0 : bands.Max(band => band.Strength);
    }

    // The three the row states beside the name, the close and the reasons. The
    // distance mark takes the universe screen's own cell, so tonight's list and
    // the universe table draw one shape from one set of numbers; the day change
    // needs the session before the night, which is a second read rather than a
    // column on any row.
    var cells = UniverseScreen.Rows(universe).ToDictionary(cell => cell.Ticker, StringComparer.Ordinal);

    var rows = TonightScreen.Rows(
        dated,
        listings,
        strengths,
        cells,
        await read.ClosesToTheNightAsync(dated));

    // Whichever row the reader selected, from the hash, and the first row when
    // they have selected none. Section 15.7's region is for whichever row is
    // selected, and a composition fixed at the first answers a question nobody
    // asked.
    var asked = request.Query["name"].FirstOrDefault();
    var selection = TonightScreen.Selected(rows, asked);

    var selected = selection is { } chosen
        ? NameScreen.PlanRegion(
            page,
            marks,
            chosen.Ticker,
            await read.LadderAsync(chosen.Ticker),
            universe.FirstOrDefault(row => row.Ticker == chosen.Ticker)?.Close ?? 0m,
            await read.LevelsAsync(chosen.Ticker))
        : string.Empty;

    // The record beside each reason and the evening's own totals, which are
    // 15.7's two halves that need a reason to have a history. The record is
    // over every night the store holds rather than over this one, for the
    // reason the run page states: it is a property of the reason and not of the
    // evening.
    var records = RunScreen.Records(
        await read.ListingsAsync(),
        RunScreen.Resolved(await read.ForwardReturnsAsync()));

    return Results.Content(
        page.TonightRegion(
            marks,
            dated,
            universe.Count,
            TonightScreen.Fired(listings),
            await read.NightDurationAsync(dated),
            rows,
            [],
            selected,
            RunScreen.Harness(PhaseReport(builder)),
            selection?.Ticker,
            records,
            RunScreen.Tracks(TonightScreen.Totals(listings)),
            TonightScreen.Spend(dated, await SpentOn(read, dated), caps),
            TonightScreen.Prose(dated, await read.WrittenOnOrBeforeAsync(dated))),
        "text/html; charset=utf-8");
});

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
    // the screen and the night are over one universe rather than two.
    // see: One universe now, the seam for more built now
    var index = builder.Configuration["EquityBrief:IndexCode"] ?? "GSPC";

    // The index on the newest night the listings hold, which is the night the
    // screen's figures are from.
    var members = await read.UniverseAsync(index, await read.NewestNightAsync());

    // The listing history behind the two right-hand columns and the sector
    // strip's count, over the window section 15.8 states.
    var history = new Dictionary<string, IReadOnlyList<ListingRow>>(StringComparer.Ordinal);

    foreach (var member in members)
    {
        history[member.Ticker] = await read.ListingsAsync(member.Ticker, UniverseScreen.StripSessions);
    }

    // The night the column counts sessions from, and every name's next dated
    // event in one read rather than five hundred.
    var night = await read.NewestNightAsync();
    var events = (await read.NextEventsAsync(night ?? DateOnly.MinValue))
        .ToDictionary(row => row.Ticker, row => row.EventDate, StringComparer.Ordinal);

    var cells = UniverseScreen.Rows(members, history, events, night);

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

    return Results.Content(
        page.UniverseRegion(
            marks,
            cells,
            UniverseScreen.Sectors(cells),
            trend,
            sector,
            shown.Page,
            shown.At,
            UniverseScreen.PageSize),
        "text/html; charset=utf-8");
});

// The run page, section 15.10, read here and composed by the app.
//
// `/screens/run` resolves to the newest night the run log holds and
// `/screens/run/<date>` to an earlier one, which is the pair the tonight route
// already answers and which keeps `#/run/<date>` a link.
app.MapGet("/screens/run/{night?}", async (
    string? night,
    ReadApi read,
    MarkRenderer marks,
    SinglePageApp page) =>
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

    // The reason record is over every night the store holds and not over this
    // one. A record is a property of the reason across every name it ever fired
    // for, and a record over one evening would be a statement about that
    // evening wearing the clothes of a verdict.
    var everyListing = await read.ListingsAsync();
    var returns = await read.ForwardReturnsAsync();
    var stages = RunScreen.Stages(await read.RunLogAsync(dated));
    var records = RunScreen.Records(everyListing, RunScreen.Resolved(returns));

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
            RunScreen.Harness(PhaseReport(builder)),
            RunScreen.Priced(await read.PaidCallSpendsAsync())),
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
