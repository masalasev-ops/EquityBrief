using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The bulk price feed, answered from a recorded response rather than from the
// network. One captured file for the night, holding the whole exchange.
//
// It ships for the same reason the other doubles do: the fixture replay runs
// the pipeline rather than the tests, and a double the shipped code cannot be
// pointed at is one the pipeline cannot replay through. It holds no HTTP
// client, so a test using it cannot fall back to the live provider.
//
// The parser was written against a captured response and not the other way
// round, which is what 1.2 learned the hard way: a membership parser read a
// field the provider does not send, and its fixture agreed with it for two
// checkpoints because the same session wrote both. One request settled this
// payload's shape, and the shape is not what the historical endpoint returns.
// The name is under `code` rather than a ticker field, and each row carries the
// exchange it came from.
public sealed class RecordedBulkPriceFeed(string response) : IBulkPriceFeed
{
    public const string FilePrefix = "bulk-";

    public int Requests { get; private set; }

    public static RecordedBulkPriceFeed FromFolder(string folder)
    {
        var files = Directory.GetFiles(folder, FilePrefix + "*.json");

        return files.Length == 1
            ? new RecordedBulkPriceFeed(File.ReadAllText(files[0]))
            : throw new InvalidOperationException(
                $"{folder} holds {files.Length} bulk responses and a night fetches one. Two would " +
                "leave the night's session date decided by whichever file was listed first.");
    }

    // Every row the file holds, for every name, unfiltered.
    //
    // Counted once per call rather than once per name, because that count is
    // the whole claim the nightly path rests on: one request for the night
    // whatever the universe is, and nightly-cost reads the figure back off the
    // run log.
    readonly List<string> notSessions = [];

    public IReadOnlyList<string> NotSessions => notSessions;

    public Task<IReadOnlyList<BulkBar>> RowsAsync(string exchange, CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(Parse(response, exchange, notSessions));
    }

    // Shared by this and the live feed, which is what makes the double and the
    // provider the same reader. A second parser would be a second opinion about
    // what the provider sends, and the fixture would exercise only one of them.
    public static IReadOnlyList<BulkBar> Parse(
        string json,
        string exchange,
        ICollection<string>? notSessions = null)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind is not JsonValueKind.Array)
        {
            throw new FormatException(
                "The captured bulk response is not an array of rows. A payload that cannot be " +
                "parsed must fail rather than answer with no bars, which a night would store as " +
                "an exchange that did not trade.");
        }

        var rows = new List<BulkBar>();
        var skipped = 0;

        foreach (var entry in document.RootElement.EnumerateArray())
        {
            var (row, ticker) = Read(entry, exchange);

            if (row is not null)
            {
                rows.Add(row);
            }
            else if (ticker is not null)
            {
                // Listed and did not trade, which is a fact about an exchange
                // file rather than a broken payload.
                skipped++;
                notSessions?.Add(ticker);
            }
        }

        // A payload that produced nothing is a payload that could not be read,
        // whatever the reason each row gave. Answering with no bars would be
        // stored as an exchange that did not trade, which is the failure this
        // parser has refused since 1.4 and which skipping rows would reopen.
        return rows.Count == 0 && skipped > 0
            ? throw new FormatException(
                $"Every one of the {skipped} rows the bulk response carried for {exchange} is a " +
                "symbol that did not trade. An exchange on which nothing traded is not a result " +
                "this system has any use for, and storing it would read as a night that ran.")
            : rows;
    }

    const string Code = "code";
    const string Exchange = "exchange_short_name";

    // The row, and the ticker when the row was a symbol that did not trade.
    //
    // Three outcomes rather than two, kept apart because they mean different
    // things. A row from another exchange is not this night's and is dropped
    // with nothing recorded. A row that is this night's and did not trade is
    // dropped and named, so the count is visible. Anything else throws.
    static (BulkBar? Row, string? NotASession) Read(JsonElement entry, string exchange)
    {
        if (!entry.TryGetProperty(Code, out var code) || code.ValueKind is not JsonValueKind.String)
        {
            throw new FormatException(
                $"A bulk row carries no {Code}. The provider names the ticker there rather than in " +
                "a ticker field, and a row without one cannot be attributed to a name.");
        }

        var ticker = code.GetString()!;

        // The file is one exchange's day, and the row says which exchange it is
        // from. A row from another one is skipped rather than stored: the night
        // asks for one exchange and a store keyed on ticker alone would collide
        // two listings of the same symbol.
        if (entry.TryGetProperty(Exchange, out var from)
            && from.ValueKind is JsonValueKind.String
            && !string.Equals(from.GetString(), exchange, StringComparison.OrdinalIgnoreCase))
        {
            return (null, null);
        }

        return ProviderBarReader.TryRead(entry, ticker, out var bar)
            ? (new BulkBar(ticker, bar!), null)
            : (null, ticker);
    }
}
