using System.Globalization;
using EquityBrief.Core.Analysts;
using EquityBrief.Core.Providers;
using EquityBrief.Data;
using EquityBrief.Data.Analysts;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// What a chart page's ask for its analysts, or its press for TipRanks' ratings, came to: whether a row was written, the
// instant of the ask that stands for the stock, and the line the reply carries.
public sealed record AnalystAsk(bool Written, DateTimeOffset AskedAt, string Line);

// ---- the analysts ----
//
// A chart page opened asks for its stock's analysts, this surface writes the ask, and the worker's analyst job answers it
// from Yahoo; the page's press asks for TipRanks' ratings the same way and the ratings job answers it. The page draws the
// newest answer stored for the stock, and the month's TipRanks calls as the ratings job last read them off its run.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
// see: TipRanks' analyst ratings are asked only on a press, once a stock a day and never past the month's limit
public sealed partial class ReadApi
{
    public const string FromYahoo = "yahoo";

    public const string FromTipRanks = "tipranks";

    const string NewestAnalystAsk = "SELECT MAX(asked_at) FROM analyst_request WHERE ticker = $ticker AND source = $source;";

    const string NewestAnalystView = @"
        SELECT asked_at, currency, target_low, target_mean, target_median, target_high, analysts, recommendation, months, actions
        FROM analyst_view
        WHERE ticker = $ticker
        ORDER BY asked_at DESC
        LIMIT 1;
    ";

    const string NewestAnalystRating = @"
        SELECT asked_at, as_of, total, counts, ratings
        FROM analyst_rating
        WHERE ticker = $ticker
        ORDER BY asked_at DESC
        LIMIT 1;
    ";

    // The newest run of the ratings job that read the month's calls.
    const string NewestCalls = @"
        SELECT json_extract(detail, '$.used'), json_extract(detail, '$.limit'), json_extract(detail, '$.resetsAt'), json_extract(detail, '$.readAt')
        FROM run_log
        WHERE stage = $stage AND json_extract(detail, '$.used') IS NOT NULL
        ORDER BY rowid DESC
        LIMIT 1;
    ";

    // Writes Yahoo's ask unless an answer stored on the stock's New York day stands or its newest ask is inside the minutes
    // between two asks, and says which.
    public async Task<AnalystAsk> AskAnalystsAsync(string ticker)
    {
        var now = clock.UtcNow;

        await using var connection = Open();

        if (await NewestViewAsync(connection, ticker) is { } stored && stored.AskedAt >= AnalystLimits.DayOf(now, clock.SessionZone).From)
        {
            return new AnalystAsk(false, stored.AskedAt, $"Yahoo answered for {ticker} at {AskStamp(stored.AskedAt)}, on this New York day, so nothing was asked.");
        }

        if (await NewestAskAsync(connection, ticker, FromYahoo) is { } last && now - last < TimeSpan.FromMinutes(AnalystLimits.AskAgainMinutes))
        {
            return new AnalystAsk(false, last, $"Yahoo was asked for {ticker} at {AskStamp(last)}, inside the {AnalystLimits.AskAgainMinutes} minutes between two asks, so nothing was asked.");
        }

        return await WriteAskAsync(connection, ticker, FromYahoo, now, $"Yahoo is being asked for {ticker}'s analysts.");
    }

    // Writes TipRanks' ask unless an answer stored on the stock's New York day stands or the newest count of the month's
    // calls says they are spent, and says which.
    public async Task<AnalystAsk> AskRatingsAsync(string ticker)
    {
        var now = clock.UtcNow;

        await using var connection = Open();

        if (await NewestRatingAsync(connection, ticker) is { } stored && stored.AskedAt >= AnalystLimits.DayOf(now, clock.SessionZone).From)
        {
            return new AnalystAsk(false, stored.AskedAt, $"TipRanks answered for {ticker} at {AskStamp(stored.AskedAt)}, on this New York day, so no call was made.");
        }

        if (await CallsAsync(connection) is { } calls && calls.Spent(now))
        {
            return new AnalystAsk(false, now, $"The month's {calls.Limit} TipRanks calls are used until {AskStamp(calls.ResetsAt)}, so no call was made.");
        }

        return await WriteAskAsync(connection, ticker, FromTipRanks, now, $"TipRanks is being asked for {ticker}'s ratings, one of the month's calls.");
    }

    // The newest answer Yahoo gave for the stock, none where none is stored.
    public async Task<YahooAnalystView?> NewestAnalystsAsync(string ticker)
    {
        await using var connection = Open();

        return await NewestViewAsync(connection, ticker);
    }

    // The newest answer TipRanks gave for the stock, none where none is stored.
    public async Task<TipRanksView?> NewestRatingsAsync(string ticker)
    {
        await using var connection = Open();

        return await NewestRatingAsync(connection, ticker);
    }

    // The month's TipRanks calls as the ratings job last read them, none where no run has read them.
    public async Task<TipRanksCalls?> TipRanksCallsAsync()
    {
        await using var connection = Open();

        return await CallsAsync(connection);
    }

    // The newest run of the analyst job or the ratings job for the stock: when it started, what it came to and its line,
    // which the page states where a run after its ask stored nothing.
    const string NewestAnalystRun = @"
        SELECT started_at, outcome, json_extract(detail, '$.detail')
        FROM run_log
        WHERE stage = $stage AND json_extract(detail, '$.ticker') = $ticker
        ORDER BY rowid DESC
        LIMIT 1;
    ";

    public async Task<(DateTimeOffset StartedAt, string Outcome, string Detail)?> NewestAnalystRunAsync(string stage, string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestAnalystRun;
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() && !reader.IsDBNull(1)
            ? (Instant(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? string.Empty : reader.GetString(2))
            : null;
    }

    async Task<AnalystAsk> WriteAskAsync(SqliteConnection connection, string ticker, string source, DateTimeOffset now, string line)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AskAnalysts;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$asked_at", AskStamp(now));
        command.Parameters.AddWithValue("$source", source);

        try
        {
            await command.ExecuteNonQueryAsync();
        }
        catch (SqliteException failure) when (failure.SqliteErrorCode == 19)
        {
            return new AnalystAsk(false, now, $"{ticker} was asked in this same second, so nothing more was asked.");
        }

        return new AnalystAsk(true, now, line);
    }

    static async Task<DateTimeOffset?> NewestAskAsync(SqliteConnection connection, string ticker, string source)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestAnalystAsk;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$source", source);

        return await command.ExecuteScalarAsync() is string newest ? Instant(newest) : null;
    }

    static async Task<YahooAnalystView?> NewestViewAsync(SqliteConnection connection, string ticker)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestAnalystView;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

        return new YahooAnalystView(
            Instant(reader.GetString(0)),
            new YahooAnalystAnswer(
                reader.IsDBNull(1) ? null : reader.GetString(1),
                Price(2),
                Price(3),
                Price(4),
                Price(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                StoredAnalysts.MonthsOf(reader.GetString(8)),
                StoredAnalysts.ActionsOf(reader.GetString(9))));
    }

    static async Task<TipRanksView?> NewestRatingAsync(SqliteConnection connection, string ticker)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestAnalystRating;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync())
        {
            return null;
        }

        return new TipRanksView(
            Instant(reader.GetString(0)),
            new TipRanksAnswer(
                reader.IsDBNull(1) ? null : DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                StoredAnalysts.CountsOf(reader.GetString(3)),
                StoredAnalysts.RatingsOf(reader.GetString(4))));
    }

    static async Task<TipRanksCalls?> CallsAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestCalls;
        command.Parameters.AddWithValue("$stage", AnalystLimits.RatingsStage);

        await using var reader = await command.ExecuteReaderAsync();

        if (!await reader.ReadAsync() || reader.IsDBNull(1) || reader.IsDBNull(2) || reader.IsDBNull(3))
        {
            return null;
        }

        return new TipRanksCalls(
            Convert.ToInt32(reader.GetValue(0), CultureInfo.InvariantCulture),
            Convert.ToInt32(reader.GetValue(1), CultureInfo.InvariantCulture),
            Instant(reader.GetString(2)),
            Instant(reader.GetString(3)));
    }

    static string AskStamp(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
