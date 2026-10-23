using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Analysts;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Data.Analysts;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Analysts;

// What the analyst job or the ratings job came to: its outcome in a word and the line its run log row and its console
// carry.
public sealed record AnalystOutcome(string Outcome, string Detail);

// The analyst job. A chart page opened asks for its stock's analysts, and the read surface writes the ask and starts this
// as a process of its own, as a name page's ask starts the quote job. It asks Yahoo once, under the New York day's cap,
// and stores what Yahoo sent: the targets with how many analysts set them, its recommendation, the counts of each month
// and every firm's action. At the cap, for an answer holding no analysts and for a failure it stores nothing, its run log
// row saying why.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
public sealed class AnalystJob(IClock clock, string databaseFile, IAnalystFeed feed) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.AnalystView, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.Analyst]);

    public const string Stage = AnalystLimits.AnalystsStage;

    // The run log's words for what a run came to.
    public const string Answered = "answered";

    public const string AtTheCap = "cap reached";

    public const string NoAnalysts = "no analysts";

    public const string Failed = "failed";

    // The day's asks are the runs of this stage that asked Yahoo anything.
    const string AskedInTheDay = "SELECT COUNT(*) FROM run_log WHERE stage = $stage AND started_at >= $from AND started_at < $to AND network_requests > 0;";

    const string InsertView = @"
        INSERT INTO analyst_view (ticker, asked_at, answered_at, currency, target_low, target_mean, target_median, target_high, analysts, recommendation, months, actions)
        VALUES ($ticker, $asked_at, $answered_at, $currency, $target_low, $target_mean, $target_median, $target_high, $analysts, $recommendation, $months, $actions);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // A run's id: the stage, the instant it started and the stock, so two stocks asked in one second write two rows.
    public static string RunIdFor(DateTimeOffset started, string ticker) =>
        Stage + "-" + started.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "-" + ticker;

    public async Task<AnalystOutcome> RunAsync(string ticker, DateTimeOffset askedAt, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = RunIdFor(started, ticker);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var (from, to) = AnalystLimits.DayOf(started, clock.SessionZone);
        int asked;

        await using (var counting = connection.CreateCommand())
        {
            counting.CommandText = AskedInTheDay;
            counting.Parameters.AddWithValue("$stage", Stage);
            counting.Parameters.AddWithValue("$from", Instant(from));
            counting.Parameters.AddWithValue("$to", Instant(to));
            asked = Convert.ToInt32(await counting.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
        }

        if (asked >= AnalystLimits.DailyCap)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, AtTheCap, $"{asked} asks of Yahoo this New York day against a cap of {AnalystLimits.DailyCap}, so nothing was asked", 0, 0, cancellation);
        }

        YahooAnalystAnswer? answer;

        try
        {
            answer = await feed.AnalystsAsync(ticker, cancellation);
        }
        catch (ProviderRefusal refusal)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, Failed, Unaddressed(refusal.Message), 0, feed.Requests, cancellation);
        }
        catch (FormatException unreadable)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, Failed, "Yahoo's answer could not be read: " + Unaddressed(unreadable.Message), 0, feed.Requests, cancellation);
        }
        catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, Failed, "Yahoo did not answer in time on any of its tries", 0, feed.Requests, cancellation);
        }

        if (answer is null)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, NoAnalysts, "Yahoo holds no analysts for it", 0, feed.Requests, cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = InsertView;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$asked_at", Instant(askedAt));
            command.Parameters.AddWithValue("$answered_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$currency", answer.Currency is { } currency ? currency : DBNull.Value);
            command.Parameters.AddWithValue("$target_low", answer.TargetLow is { } low ? Money.ToStorage(low) : DBNull.Value);
            command.Parameters.AddWithValue("$target_mean", answer.TargetMean is { } mean ? Money.ToStorage(mean) : DBNull.Value);
            command.Parameters.AddWithValue("$target_median", answer.TargetMedian is { } median ? Money.ToStorage(median) : DBNull.Value);
            command.Parameters.AddWithValue("$target_high", answer.TargetHigh is { } high ? Money.ToStorage(high) : DBNull.Value);
            command.Parameters.AddWithValue("$analysts", answer.Analysts is { } analysts ? analysts : DBNull.Value);
            command.Parameters.AddWithValue("$recommendation", answer.Recommendation is { } word ? word : DBNull.Value);
            command.Parameters.AddWithValue("$months", StoredAnalysts.Months(answer.Months));
            command.Parameters.AddWithValue("$actions", StoredAnalysts.Actions(answer.Actions));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        return await EndAsync(
            connection,
            runId,
            started,
            ticker,
            askedAt,
            Answered,
            FormattableString.Invariant($"{answer.Analysts?.ToString(CultureInfo.InvariantCulture) ?? "no count of"} analysts, a mean target of {(answer.TargetMean is { } target ? Money.ToStorage(target) : "none")}, {answer.Months.Count} months and {answer.Actions.Count} firm actions"),
            1,
            feed.Requests,
            cancellation);
    }

    // A failure's words with any address taken out, since a request's address is never written down.
    static string Unaddressed(string said) =>
        System.Text.RegularExpressions.Regex.Replace(said, @"[a-zA-Z][a-zA-Z0-9+.\-]*://\S*", ProviderCredentials.AddressWithheld);

    async Task<AnalystOutcome> EndAsync(
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
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new { ticker, askedAt = Instant(askedAt), detail }));
        await command.ExecuteNonQueryAsync(cancellation);

        return new AnalystOutcome(outcome, detail);
    }

    static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
