using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The dividend calendar, answered from recorded responses, one file a session.
//
// The parser was written against the 21 answers 16.0 captured for the sessions from 2026-10-07 to 2026-11-04: each a
// `meta` envelope stating the session asked for, the total and the page's limit and offset, and a `data` list of rows
// each carrying only a `symbol` and a `date`, every market's listings together. A symbol is a ticker and an exchange,
// `EIX.US`, so only the index's own exchange is read and the ticker is what precedes it; a class's shares keep their
// dash, `LEN-B`. A folder holding no answer asks nothing, so a night replayed from a capture made before the night asked
// for dividends makes the requests it made then.
public sealed class RecordedDividendCalendarFeed : IDividendCalendarFeed
{
    public const string Prefix = "dividends-";

    readonly IReadOnlyDictionary<DateOnly, string> answers;

    RecordedDividendCalendarFeed(IReadOnlyDictionary<DateOnly, string> answers) => this.answers = answers;

    public static RecordedDividendCalendarFeed None { get; } = new(new Dictionary<DateOnly, string>());

    public int Requests { get; private set; }

    // A recorded feed over the answers given, keyed by the session each was asked for.
    public static RecordedDividendCalendarFeed Of(IReadOnlyDictionary<DateOnly, string> answers) => new(answers);

    public static RecordedDividendCalendarFeed FromFolder(string folder)
    {
        var answers = new Dictionary<DateOnly, string>();

        foreach (var file in Directory.GetFiles(folder, Prefix + "*.json"))
        {
            var day = Path.GetFileNameWithoutExtension(file)[Prefix.Length..];

            answers[DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture)] = File.ReadAllText(file);
        }

        return new(answers);
    }

    // One request a session, counted, where the folder holds answers at all.
    public Task<IReadOnlyList<ExDividend>> ExDividendsAsync(DateOnly session, CancellationToken cancellation = default)
    {
        if (answers.Count == 0)
        {
            return Task.FromResult<IReadOnlyList<ExDividend>>([]);
        }

        Requests++;

        return Task.FromResult(answers.TryGetValue(session, out var response) ? Parse(response, session).Rows : []);
    }

    // The rows on the index's own exchange dated on the session asked for, and the total the page says the answer holds,
    // so a caller reading pages knows whether another is owed.
    public static (IReadOnlyList<ExDividend> Rows, int Total) Parse(string response, DateOnly session)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            return ([], 0);
        }

        using var document = JsonDocument.Parse(response);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
        {
            throw new FormatException("The dividend calendar's answer holds no list of rows under data.");
        }

        var total = root.TryGetProperty("meta", out var meta) && meta.TryGetProperty("total", out var counted) && counted.TryGetInt32(out var whole)
            ? whole
            : data.GetArrayLength();
        var suffix = "." + RecordedEarningsCalendarFeed.IndexExchange;
        var rows = new List<ExDividend>();

        foreach (var row in data.EnumerateArray())
        {
            if (row.TryGetProperty("symbol", out var symbol) && symbol.GetString() is { } code && code.EndsWith(suffix, StringComparison.Ordinal)
                && row.TryGetProperty("date", out var date) && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on)
                && on == session)
            {
                rows.Add(new ExDividend(code[..^suffix.Length], on));
            }
        }

        return (rows, total);
    }
}
