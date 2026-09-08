using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The splits and dividends feed, answered from recorded responses.
//
// The parser was written against captured responses and not the other way
// round, which is the obligation 1.2 filed against this checkpoint: a parser
// whose only fixture was written by the session writing the parser is checked
// against itself. Three requests settled the shapes, and two of them would have
// been written wrong from the endpoint's name alone.
//
// A split's value is a ratio in a string, "4.000000/1.000000", not a number. A
// dividend's value is also a string, and four of its ten fields arrive as JSON
// null rather than being absent. And the exchange is under `exchange` here,
// where the price bulk file calls the same thing `exchange_short_name`, so one
// reader for both would have silently dropped every row of one of them.
public sealed class RecordedCorporateActionFeed(
    IReadOnlyDictionary<ActionKind, string> responses) : ICorporateActionFeed
{
    public const string SplitsPrefix = "actions-splits-";
    public const string DividendsPrefix = "actions-dividends-";

    public int Requests { get; private set; }

    public static RecordedCorporateActionFeed FromFolder(string folder)
    {
        var found = new Dictionary<ActionKind, string>();

        foreach (var (kind, prefix) in new[]
                 {
                     (ActionKind.Split, SplitsPrefix),
                     (ActionKind.Dividend, DividendsPrefix),
                 })
        {
            var files = Directory.GetFiles(folder, prefix + "*.json");

            if (files.Length == 1)
            {
                found[kind] = File.ReadAllText(files[0]);
            }
            else if (files.Length > 1)
            {
                throw new InvalidOperationException(
                    $"{folder} holds {files.Length} {kind} responses and a night fetches one.");
            }
        }

        return new RecordedCorporateActionFeed(found);
    }

    // Two requests, one per kind, and both counted. The night's claim is that
    // its cost does not grow with the universe, and two is a constant.
    public Task<IReadOnlyList<CorporateAction>> ActionsAsync(
        string exchange,
        DateOnly session,
        CancellationToken cancellation = default)
    {
        var actions = new List<CorporateAction>();

        foreach (var (kind, response) in responses)
        {
            Requests++;
            actions.AddRange(Parse(response, exchange, kind).Where(action => action.Date == session));
        }

        return Task.FromResult<IReadOnlyList<CorporateAction>>(
            [.. actions.OrderBy(action => action.Ticker, StringComparer.Ordinal).ThenBy(action => action.Kind)]);
    }

    const string Code = "code";
    const string Exchange = "exchange";

    public static IReadOnlyList<CorporateAction> Parse(string json, string exchange, ActionKind kind)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                $"The captured {kind} response is not an array of rows. A payload that cannot be " +
                "parsed must fail rather than answer with no actions, which a night would store " +
                "as a day on which nothing split and nothing paid.");
        }

        return
        [
            .. document.RootElement
                .EnumerateArray()
                .Select(entry => Read(entry, exchange, kind))
                .Where(action => action is not null)
                .Select(action => action!),
        ];
    }

    static CorporateAction? Read(JsonElement entry, string exchange, ActionKind kind)
    {
        if (!entry.TryGetProperty(Code, out var code) || code.ValueKind is not JsonValueKind.String)
        {
            throw new FormatException(
                $"A {kind} row carries no {Code}, so it cannot be attributed to a name.");
        }

        // `exchange` here and `exchange_short_name` in the price bulk file. The
        // two endpoints name the same thing differently, which is the sort of
        // thing a parser written before the capture gets wrong once and then
        // agrees with its own fixture about.
        if (entry.TryGetProperty(Exchange, out var from)
            && from.ValueKind is JsonValueKind.String
            && !string.Equals(from.GetString(), exchange, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var field = kind == ActionKind.Split ? "split" : "dividend";

        if (!entry.TryGetProperty("date", out var date) || date.ValueKind is not JsonValueKind.String
            || !DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on))
        {
            throw new FormatException($"A {kind} row for {code.GetString()} carries no date in yyyy-MM-dd.");
        }

        // Both arrive as strings. Kept as sent, because nothing computes with
        // them: the only question is whether an action happened.
        var value = entry.TryGetProperty(field, out var raw) && raw.ValueKind is not JsonValueKind.Null
            ? raw.ValueKind is JsonValueKind.String ? raw.GetString()! : raw.ToString()
            : throw new FormatException(
                $"A {kind} row for {code.GetString()} carries no {field}. A row that says an action " +
                "happened and does not say what it was is not an action.");

        return new CorporateAction(code.GetString()!, on, kind, value);
    }
}
