using System.Globalization;
using System.Text;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Paper;

// The 4:30 pm check, a process of its own outside the night on each trading day: every live rule's order at a price still
// waiting on the three indices, its stock's day low asked of the provider's live quote a page of symbols a request, each
// written with the low it read, and one phone message naming the orders whose low reached their buy price, to be confirmed by
// the night, whose run stays the record. It waits until half past four in New York, so a run scheduled earlier reads the
// day's whole session, and on a day the exchange is closed it asks nothing. Without the word it states what it would ask and
// asks, writes and sends nothing.
// see: The fill check asks EODHD's live quotes after the close on its own run, one request a page of symbols
public sealed class FillCheck(IClock clock, string databaseFile, IDayLowFeed feed, IPhoneMessageFeed phone) : IComponent
{
    public const string Stage = "fill-check";

    public const string Title = "Filled today, to be confirmed tonight";

    // The time in New York the check reads at: half an hour after the close.
    public static TimeOnly ReadsAt { get; } = new(16, 30);

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.FillCheck, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.LiveQuotes, Feed.PhoneMessages]);

    // One order the check reads: its index, who placed it, the stock, its night and its buy price.
    public sealed record Waiting(string Index, string Who, string Ticker, DateOnly Listed, decimal Price);

    // The instant the check reads at on a day, in UTC.
    public static DateTimeOffset ReadingAt(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(ReadsAt);

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    // Every live rule's order at a price waiting on the day: placed before it, its last day not before it.
    public static async Task<IReadOnlyList<Waiting>> WaitingAsync(string databaseFile, DateOnly day)
    {
        var waiting = new List<Waiting>();

        foreach (var index in PaperFiguresRun.Indices)
        {
            var name = Core.Candidates.OwnStepCandidate.NameOf(index);
            var planned = await new PaperReads(databaseFile).PlannedTradesAsync(index, day);

            waiting.AddRange(PlannedTrades.LiveBuying(planned)
                .Where(row => PlannedTrades.IsWaiting(row) && !row.Order.PlacesNoPrice() && row.Listed < day && PlannedTrades.LastDay(row.Listed, row.Order.Window) >= day && PlannedTrades.Price(row) is not null)
                .Select(row => new Waiting(index, SetupNames.WhoOf(row.Family, name), row.Ticker, row.Listed, PlannedTrades.Price(row)!.Value)));
        }

        return waiting;
    }

    public async Task<int> RunAsync(DateOnly day, IReadOnlyList<Waiting> waiting, bool word, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        if (!ExchangeClosures.IsSession(day))
        {
            output.WriteLine(FormattableString.Invariant($"{Stage}: {day:yyyy-MM-dd} is no trading day, so nothing is asked"));

            return 0;
        }

        var tickers = waiting.Select(order => order.Ticker).Distinct(StringComparer.Ordinal).ToArray();

        output.WriteLine(FormattableString.Invariant($"{Stage}: {waiting.Count} waiting order(s) on {day:yyyy-MM-dd}, {tickers.Length} stock(s), {(tickers.Length + EodhdDayLowFeed.Page - 1) / EodhdDayLowFeed.Page} request(s) of the live quote at {EodhdDayLowFeed.Page} symbols a request"));

        if (!word)
        {
            output.WriteLine($"{Stage}: nothing asked, written or sent; give '--word' to ask");

            return 2;
        }

        var lows = tickers.Length == 0 ? [] : (await feed.LowsAsync(tickers, cancellation)).GroupBy(low => low.Ticker, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var runId = FormattableString.Invariant($"{Stage}-{day:yyyyMMdd}-{started:yyyyMMddTHHmmssZ}");
        var filled = waiting.Where(order => lows.TryGetValue(order.Ticker, out var low) && low.Low <= order.Price).ToArray();

        await WriteAsync(runId, day, waiting, lows, cancellation);

        var message = Message(day, filled, lows);

        output.Write(message);

        string sent;

        try
        {
            sent = await phone.SendAsync(Title, message, cancellation) ? "the message sent" : "no message sent: no ntfy topic is saved";
        }
        catch (ProviderRefusal refusal)
        {
            sent = "the message not sent: " + refusal.Message;
        }

        var detail = FormattableString.Invariant($"{waiting.Count} waiting order(s) read on {day:yyyy-MM-dd}, {lows.Count} low(s) answered, {filled.Length} reaching their buy price; {sent}");

        await LogAsync(runId, started, waiting.Count, feed.Requests + (phone.CanSend ? 1 : 0), detail, cancellation);
        output.WriteLine($"{Stage}: {detail}");

        return 0;
    }

    // The message: the day, then each order whose low reached its buy price, or none.
    public static string Message(DateOnly day, IReadOnlyList<Waiting> filled, IReadOnlyDictionary<string, DayLow> lows)
    {
        var text = new StringBuilder();

        text.Append(Title).Append(", ").Append(OrderChanges.Day(day)).Append('\n');

        if (filled.Count == 0)
        {
            text.Append("None today\n");
        }

        foreach (var order in filled)
        {
            text.Append("- ").Append(order.Ticker).Append(", ").Append(order.Who).Append(": low $").Append(OrderChanges.Price(lows[order.Ticker].Low))
                .Append(", at or under its buy at $").Append(OrderChanges.Price(order.Price)).Append('\n');
        }

        return text.ToString();
    }

    async Task WriteAsync(string runId, DateOnly day, IReadOnlyList<Waiting> waiting, IReadOnlyDictionary<string, DayLow> lows, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var order in waiting)
        {
            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = @"
                INSERT OR IGNORE INTO fill_check (run_id, session_date, index_code, who, ticker, listed, price, low, quoted_at)
                VALUES ($run_id, $session, $index, $who, $ticker, $listed, $price, $low, $at);";
            insert.Parameters.AddWithValue("$run_id", runId);
            insert.Parameters.AddWithValue("$session", Text(day));
            insert.Parameters.AddWithValue("$index", order.Index);
            insert.Parameters.AddWithValue("$who", order.Who);
            insert.Parameters.AddWithValue("$ticker", order.Ticker);
            insert.Parameters.AddWithValue("$listed", Text(order.Listed));
            insert.Parameters.AddWithValue("$price", Money.ToStorage(order.Price));
            insert.Parameters.AddWithValue("$low", lows.TryGetValue(order.Ticker, out var low) ? Money.ToStorage(low.Low) : DBNull.Value);
            insert.Parameters.AddWithValue("$at", lows.TryGetValue(order.Ticker, out var quoted) ? Instant(quoted.At) : DBNull.Value);
            await insert.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);
    }

    async Task LogAsync(string runId, DateTimeOffset started, int rows, int requests, string detail, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var insert = connection.CreateCommand();

        insert.CommandText = @"
            INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
            VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, $requests, '0', $detail);";
        insert.Parameters.AddWithValue("$run_id", runId);
        insert.Parameters.AddWithValue("$stage", Stage);
        insert.Parameters.AddWithValue("$started_at", Instant(started));
        insert.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        insert.Parameters.AddWithValue("$rows", rows);
        insert.Parameters.AddWithValue("$requests", requests);
        insert.Parameters.AddWithValue("$detail", detail);
        await insert.ExecuteNonQueryAsync(cancellation);
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
