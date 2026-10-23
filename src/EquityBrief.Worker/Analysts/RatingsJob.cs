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

// The ratings job. A chart page's press asks TipRanks for its stock's recent ratings, and the read surface writes the ask
// and starts this as a process of its own. It asks nothing where the stock was answered on its New York day already;
// otherwise it asks TipRanks' own count of the month's calls, which costs none, asks nothing more where the calls used
// reach the limit the secrets file states or TipRanks' own, whichever is less, and otherwise asks once for the ratings,
// the one call a press spends, and stores them. Every run's row carries the month's calls as last read, which the page
// draws as the calls used and left, and every word a failure carries is redacted of the key before it is written.
// see: TipRanks' analyst ratings are asked only on a press, once a stock a day and never past the month's limit
public sealed class RatingsJob(IClock clock, string databaseFile, IAnalystRatingsFeed feed, int? limit, Func<string, string> redact) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.AnalystRating, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.AnalystRatings]);

    public const string Stage = AnalystLimits.RatingsStage;

    // The run log's words for what a run came to.
    public const string Answered = "answered";

    public const string AskedToday = "asked today";

    public const string AtTheLimit = "limit reached";

    public const string NoRatings = "no ratings";

    public const string Failed = "failed";

    const string AnsweredInTheDay = "SELECT COUNT(*) FROM analyst_rating WHERE ticker = $ticker AND asked_at >= $from AND asked_at < $to;";

    const string InsertRating = @"
        INSERT INTO analyst_rating (ticker, asked_at, answered_at, as_of, total, counts, ratings)
        VALUES ($ticker, $asked_at, $answered_at, $as_of, $total, $counts, $ratings);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // A run's id: the stage, the instant it started and the stock.
    public static string RunIdFor(DateTimeOffset started, string ticker) =>
        Stage + "-" + started.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture) + "-" + ticker;

    public async Task<AnalystOutcome> RunAsync(string ticker, DateTimeOffset askedAt, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = RunIdFor(started, ticker);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var (from, to) = AnalystLimits.DayOf(started, clock.SessionZone);

        await using (var counting = connection.CreateCommand())
        {
            counting.CommandText = AnsweredInTheDay;
            counting.Parameters.AddWithValue("$ticker", ticker);
            counting.Parameters.AddWithValue("$from", Instant(from));
            counting.Parameters.AddWithValue("$to", Instant(to));

            if (Convert.ToInt32(await counting.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) > 0)
            {
                return await EndAsync(connection, runId, started, ticker, askedAt, AskedToday, $"TipRanks answered for {ticker} already this New York day, so no call was made", null, 0, cancellation);
            }
        }

        TipRanksCalls calls;

        try
        {
            var usage = await feed.UsageAsync(cancellation);

            calls = new TipRanksCalls(usage.Used, limit is { } stated ? Math.Min(stated, usage.Limit) : usage.Limit, usage.ResetsAt, clock.UtcNow);
        }
        catch (Exception failure) when (failure is ProviderRefusal or FormatException || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, Failed, "TipRanks' count of the month's calls could not be read, so no call was made: " + redact(Said(failure)), null, feed.Requests, cancellation);
        }

        if (calls.Spent(calls.ReadAt))
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, AtTheLimit, $"{calls.Used} of the month's {calls.Limit} calls are used, so no call was made", calls, feed.Requests, cancellation);
        }

        TipRanksAnswer? answer;

        try
        {
            answer = await feed.RatingsAsync(ticker, cancellation);
        }
        catch (Exception failure) when (failure is ProviderRefusal or FormatException || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, Failed, "TipRanks' ratings could not be read: " + redact(Said(failure)), await AfterAsync(calls, cancellation), feed.Requests, cancellation);
        }

        var after = await AfterAsync(calls, cancellation);

        if (answer is null)
        {
            return await EndAsync(connection, runId, started, ticker, askedAt, NoRatings, "TipRanks holds no ratings for it", after, feed.Requests, cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = InsertRating;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$asked_at", Instant(askedAt));
            command.Parameters.AddWithValue("$answered_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$as_of", answer.AsOf is { } asOf ? asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
            command.Parameters.AddWithValue("$total", answer.Total is { } total ? total : DBNull.Value);
            command.Parameters.AddWithValue("$counts", StoredAnalysts.Counts(answer.Counts));
            command.Parameters.AddWithValue("$ratings", StoredAnalysts.Ratings(answer.Ratings));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        return await EndAsync(connection, runId, started, ticker, askedAt, Answered, $"{answer.Ratings.Count} analysts' ratings, {after.Used} of the month's {after.Limit} calls used", after, feed.Requests, cancellation);
    }

    // The month's calls read again after the call, which costs none, so the row carries the count the call left; the count
    // read before it with one more used where the second read fails, since the call may have been counted.
    async Task<TipRanksCalls> AfterAsync(TipRanksCalls before, CancellationToken cancellation)
    {
        try
        {
            var usage = await feed.UsageAsync(cancellation);

            return new TipRanksCalls(usage.Used, before.Limit, usage.ResetsAt, clock.UtcNow);
        }
        catch (Exception failure) when (failure is ProviderRefusal or FormatException || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            return before with { Used = before.Used + 1, ReadAt = clock.UtcNow };
        }
    }

    // A failure's own words, a timeout named as one.
    static string Said(Exception failure) =>
        failure is OperationCanceledException ? "TipRanks did not answer in time" : failure.Message;

    async Task<AnalystOutcome> EndAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset started,
        string ticker,
        DateTimeOffset askedAt,
        string outcome,
        string detail,
        TipRanksCalls? calls,
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
        command.Parameters.AddWithValue("$rows_written", outcome == Answered ? 1 : 0);
        command.Parameters.AddWithValue("$network_requests", requests);
        command.Parameters.AddWithValue(
            "$detail",
            calls is { } read
                ? JsonSerializer.Serialize(new { ticker, askedAt = Instant(askedAt), detail = redact(detail), used = read.Used, limit = read.Limit, resetsAt = Instant(read.ResetsAt), readAt = Instant(read.ReadAt) })
                : JsonSerializer.Serialize(new { ticker, askedAt = Instant(askedAt), detail = redact(detail) }));
        await command.ExecuteNonQueryAsync(cancellation);

        return new AnalystOutcome(outcome, redact(detail));
    }

    static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
