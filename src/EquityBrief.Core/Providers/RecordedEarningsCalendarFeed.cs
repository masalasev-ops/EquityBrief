using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The earnings calendar, answered from a recorded response.
//
// The parser was written against a captured response and not the other way
// round, which is the rule 1.6 and 1.7 set and which this endpoint immediately
// justified. Three things about the payload would have been written wrong from
// the endpoint's name and the schema alone.
//
// The rows sit under an `earnings` key beside an envelope naming the window,
// rather than at the top level as the bulk price file and both action feeds do.
//
// `code` is a ticker and an exchange, `AAPL.US`, so a reader taking it whole
// would key every row on a symbol the store does not hold.
//
// And there are two dates. `report_date` is when the report lands; `date` is the
// fiscal period it covers, and the two are weeks apart. A reader taking `date`
// for the event would put every print a month early, which is exactly the shape
// of an error nothing downstream would question.
//
// One thing the payload does not carry is a confirmed-or-estimated flag. 4.0
// gave the table a `status` column for it and the provider files no such field,
// which is what capturing first is for.
public sealed class RecordedEarningsCalendarFeed(string response) : IEarningsCalendarFeed
{
    public const string Prefix = "calendar-";

    public int Requests { get; private set; }

    public static RecordedEarningsCalendarFeed FromFolder(string folder)
    {
        var files = Directory.GetFiles(folder, Prefix + "*.json");

        return files.Length switch
        {
            1 => new RecordedEarningsCalendarFeed(File.ReadAllText(files[0])),
            0 => new RecordedEarningsCalendarFeed(string.Empty),
            _ => throw new InvalidOperationException(
                $"{folder} holds {files.Length} calendar responses and a night fetches one."),
        };
    }

    // One request for the window, whatever the universe size, and counted.
    public Task<IReadOnlyList<CalendarEvent>> EventsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(Parse(response, from, to));
    }

    // The window is applied here rather than trusted from the envelope. A
    // capture is a fixed range and a night asks for its own, so a recorded feed
    // that handed back everything it held would answer a question it was not
    // asked and the count of events would depend on when the capture was taken.
    public static IReadOnlyList<CalendarEvent> Parse(string response, DateOnly from, DateOnly to)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return [];
        }

        using var document = JsonDocument.Parse(response);

        if (!document.RootElement.TryGetProperty("earnings", out var rows))
        {
            throw new InvalidOperationException(
                "The calendar response carries no `earnings` array. The rows sit under that key " +
                "rather than at the top level, and a payload without it is a different answer " +
                "rather than a day on which nobody reports.");
        }

        var events = new List<CalendarEvent>();

        foreach (var row in rows.EnumerateArray())
        {
            var code = Text(row, "code");
            var reported = Date(Text(row, "report_date"));

            if (code is null || reported is not { } date || date < from || date > to)
            {
                continue;
            }

            events.Add(new CalendarEvent(
                Ticker(code),
                date,
                Timing(Text(row, "before_after_market")),
                Date(Text(row, "date")),
                Number(row, "estimate"),
                Number(row, "actual")));
        }

        return
        [
            .. events
                .OrderBy(entry => entry.EventDate)
                .ThenBy(entry => entry.Ticker, StringComparer.Ordinal),
        ];
    }

    // The ticker without its exchange. The store holds `AAPL` and the provider
    // sends `AAPL.US`, and a row keyed on the whole code would match no member.
    // A code with no suffix is taken whole rather than refused, because what is
    // being read is a name and not a routing decision.
    static string Ticker(string code)
    {
        var at = code.LastIndexOf('.');

        return at <= 0 ? code : code[..at];
    }

    static EventTiming Timing(string? filed) => filed switch
    {
        "BeforeMarket" => EventTiming.Before,
        "AfterMarket" => EventTiming.After,
        _ => EventTiming.Unstated,
    };

    static string? Text(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Kept as the provider sent it, in the invariant form, because nothing
    // computes with it. A double here would put a rounding into a figure the
    // report quotes.
    static string? Number(JsonElement row, string name) =>
        row.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? value.GetRawText()
            : null;

    static DateOnly? Date(string? stored) =>
        stored is not null
        && DateOnly.TryParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
}
