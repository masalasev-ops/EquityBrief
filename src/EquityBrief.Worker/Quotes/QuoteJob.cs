using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quotes;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Quotes;

// What the quote job came to: its outcome in a word and the line its run log row and its console carry.
public sealed record QuoteOutcome(string Outcome, string Detail);

// The quote job. A name page open in the regular session asks for the listing's delayed quote at most every five minutes,
// and the read surface writes the request and starts this as a process of its own, as it starts the drain. It asks the
// provider once, inside the session alone and under the day's cap, and stores the price with its own time, the previous
// close and the change on it, and each of the newest night's bands' distances at the price in the name's typical move
// of that night, so the page restates every distance at the quote without computing one. Refused outside the session or
// at the cap, it asks nothing and stores nothing, its run log row saying why.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
// see: Distances are stated as typical days' moves
public sealed class QuoteJob(IClock clock, string databaseFile, IQuoteFeed feed) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LiveQuote, Touch.Read | Touch.Insert),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.Quote]);

    public const string Stage = "quote";

    // The run log's words for what a run came to.
    public const string Quoted = "quoted";

    public const string SessionClosed = "session closed";

    public const string AtTheCap = "cap reached";

    public const string NoPrice = "no price";

    public const string Failed = "failed";

    const string AskedInTheSession = "SELECT COUNT(*) FROM live_quote WHERE asked_at >= $open AND asked_at < $close;";

    const string NewestNight = "SELECT MAX(as_of) FROM level WHERE ticker = $ticker;";

    const string Bands = "SELECT low_edge, high_edge, role FROM level WHERE ticker = $ticker AND as_of = $night;";

    const string TypicalMove = "SELECT value FROM indicator WHERE ticker = $ticker AND session_date = $night AND name = $name;";

    const string InsertQuote = @"
        INSERT INTO live_quote (ticker, asked_at, answered_at, quoted_at, price, previous_close, change, change_pct, night, distances)
        VALUES ($ticker, $asked_at, $answered_at, $quoted_at, $price, $previous_close, $change, $change_pct, $night, $distances);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // A run's id: the stage, the instant it started and the name, so two names asked in one second write two rows.
    public static string RunIdFor(DateTimeOffset started, string ticker) =>
        Stage + "-" + started.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "-" + ticker;

    public async Task<QuoteOutcome> RunAsync(string ticker, DateTimeOffset askedAt, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = RunIdFor(started, ticker);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (RegularSession.On(clock.SessionDateAt(started), clock.SessionZone) is not { } session || started < session.Open || started >= session.Close)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, SessionClosed, "outside the regular session, so no quote was asked", 0, 0, cancellation);
        }

        var asked = Convert.ToInt32(await ScalarAsync(connection, AskedInTheSession, [("$open", Instant(session.Open)), ("$close", Instant(session.Close))], cancellation), CultureInfo.InvariantCulture);

        if (asked >= QuoteLimits.DailyCap)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, AtTheCap, $"{asked} quotes asked this session against a cap of {QuoteLimits.DailyCap}, so no quote was asked", 0, 0, cancellation);
        }

        ProviderQuote? quote;

        try
        {
            quote = await feed.QuoteAsync(ticker, cancellation);
        }
        catch (ProviderRefusal refusal)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, Failed, refusal.Message, 0, feed.Requests, cancellation);
        }
        catch (FormatException unreadable)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, Failed, "the provider's answer could not be read: " + unreadable.Message, 0, feed.Requests, cancellation);
        }

        if (quote is null)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, NoPrice, "the provider holds no price for it", 0, feed.Requests, cancellation);
        }

        var night = await ScalarAsync(connection, NewestNight, [("$ticker", ticker)], cancellation) as string;
        var distances = night is null ? "[]" : await DistancesAsync(connection, ticker, night, quote.Price, cancellation);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = InsertQuote;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$asked_at", Instant(askedAt));
            command.Parameters.AddWithValue("$answered_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$quoted_at", Instant(quote.QuotedAt));
            command.Parameters.AddWithValue("$price", Money.ToStorage(quote.Price));
            command.Parameters.AddWithValue("$previous_close", quote.PreviousClose is { } previous ? Money.ToStorage(previous) : DBNull.Value);
            command.Parameters.AddWithValue("$change", quote.Change is { } change ? Money.ToStorage(change) : DBNull.Value);
            command.Parameters.AddWithValue("$change_pct", quote.ChangePercent is { } percent ? percent : DBNull.Value);
            command.Parameters.AddWithValue("$night", night is null ? DBNull.Value : night);
            command.Parameters.AddWithValue("$distances", distances);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        return await EndAsync(
            connection,
            runId,
            started,
            ticker,
            askedAt,
            Quoted,
            $"{Money.ToStorage(quote.Price)} as of {Instant(quote.QuotedAt)}, {(int)Math.Round((started - quote.QuotedAt).TotalMinutes)} minutes before it was asked",
            1,
            feed.Requests,
            cancellation);
    }

    // Each band of the newest night's levels with its distance from the price in that night's typical move: none inside
    // the band, and otherwise the gap to the nearer edge, as the page states every distance.
    static async Task<string> DistancesAsync(SqliteConnection connection, string ticker, string night, decimal price, CancellationToken cancellation)
    {
        var typical = await ScalarAsync(connection, TypicalMove, [("$ticker", ticker), ("$night", night), ("$name", IndicatorSeries.Atr14)], cancellation) as double?;
        var bands = new List<object>();

        await using var command = connection.CreateCommand();

        command.CommandText = Bands;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$night", night);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var low = Money.FromStorage(reader.GetString(0));
            var high = Money.FromStorage(reader.GetString(1));
            double? days = price >= low && price <= high ? 0 : Distances.InTypicalDays(price, price < low ? low : high, typical);

            bands.Add(new { low = reader.GetString(0), high = reader.GetString(1), role = reader.GetString(2), days });
        }

        return JsonSerializer.Serialize(bands);
    }

    async Task<QuoteOutcome> EndAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset started,
        string ticker,
        DateTimeOffset askedAt,
        string outcome,
        string detail,
        int rowsWritten,
        int requests,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Instant(started));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", rowsWritten);
        command.Parameters.AddWithValue("$network_requests", requests);
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { ticker, askedAt = Instant(askedAt), detail, weighted = requests * ProviderWeights.Quote }));
        await command.ExecuteNonQueryAsync(cancellation);

        return new QuoteOutcome(outcome, detail);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, given) in parameters)
        {
            command.Parameters.AddWithValue(name, given);
        }

        var read = await command.ExecuteScalarAsync(cancellation);

        return read is DBNull ? null : read;
    }

    static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
