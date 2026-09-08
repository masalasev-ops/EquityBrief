using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
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
builder.Services.AddSingleton<SinglePageApp>();

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

    return Results.Content(marks.LevelChart(ticker, drawn), "image/svg+xml; charset=utf-8");
});

// One run log row for the surface coming up, which is the grain SCHEMA declares
// and what section 15.10's run page reads. Written after the host is built so a
// store that cannot be opened fails the start rather than a request.
await app.Services.GetRequiredService<ReadApi>().RecordStartAsync(
    $"read-api-{app.Services.GetRequiredService<IClock>().UtcNow:yyyyMMddTHHmmssZ}",
    "the read surface started");

app.Run();

// So the suite can reach the host's composition. A test project referencing a
// top-level program needs the generated class to be visible.
public partial class Program;
