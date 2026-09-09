using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ladders;

public sealed record LadderOutcome(
    int MembersConsidered,
    int RowsWritten,
    int RowsDropped,
    int Uptrend,
    int Downtrend,
    int Range,
    int NotClassified,
    int WithoutBars);

// The ladder builder. Reads the levels, the indicators and the calendar, and
// writes one row per index member per night carrying the trend state and the
// plan.
//
// A row for every member, whether or not it carries a tranche.
// see: A ladder row is written for every index member every night
//
// At 4.1 the plan is empty on every row and states why: the tranches, the stops,
// the exits and the event book arrive at 4.4 through 4.7. That is the shape the
// checkpoint owes rather than a placeholder. A row whose plan says the tranches
// are not built yet is a row a screen can draw and a reader can understand, and
// the alternative, writing no row until the plan exists, is what leaves a name
// without a yesterday on the night its band went ineligible.
//
// It makes no request and calls no model.
// see: The nightly run is arithmetic only
public sealed class LadderBuilder : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "ladders";

    // The population is the index, not the names with bars. A member the store
    // holds nothing for still gets a row saying so, because the row count per
    // night is asserted against the index size and a name quietly absent would
    // make that count wrong in the direction nobody looks.
    const string CurrentMembers = @"
        SELECT ticker
        FROM membership
        WHERE index_code = $index AND ""left"" IS NULL
        ORDER BY ticker;
    ";

    const string LastSessionFor = @"
        SELECT session_date, close
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    // The retention drop, one year back from the as-of date this run wrote.
    //
    // Section 16 states one year over the six computed tables and SCHEMA gave
    // Delete to nobody until 4.2, which is contradiction A and contradiction H
    // in a third place: a retention window nobody owns is a table that grows
    // forever while the document says it does not.
    //
    // Nothing here can remove a row from inside a set it leaves standing. The
    // boundary is a date and every row below it goes, for every name at once,
    // which is the same shape BarFetcher's drop has and the same reason it is
    // sanctioned.
    // Keyed on the as-of date, and one row per index member every night
    // whether or not it carries a plan, so this is the table whose growth is
    // most exactly the index size times the nights.
    const string DropOlderThan = @"
        DELETE FROM ladder WHERE as_of < $oldest;
    ";

    const string Upsert = @"
        INSERT INTO ladder (ticker, as_of, trend_state, plan)
        VALUES ($ticker, $as_of, $trend_state, $plan)
        ON CONFLICT (ticker, as_of) DO UPDATE SET
            trend_state = excluded.trend_state,
            plan = excluded.plan;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public LadderBuilder(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<LadderOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, cancellation);

        var written = 0;
        var withoutBars = 0;
        var counts = TrendState.All.ToDictionary(state => state, _ => 0, StringComparer.Ordinal);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var ticker in members)
        {
            var session = await LastSessionAsync(connection, ticker, cancellation);

            // A member with no stored bar at all. It still gets a row, dated by
            // the clock rather than by a session it does not have, because the
            // row count is the claim and a missing row is the failure the count
            // exists to catch.
            var asOf = session?.SessionDate ?? DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);

            var trend = session is { } bar
                ? await TrendClassifier.ForAsync(connection, ticker, bar.SessionDate, bar.Close, cancellation)
                : new Trend(TrendState.NotClassified, "no stored bars");

            if (session is null)
            {
                withoutBars++;
            }

            counts[trend.State]++;

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Upsert;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$trend_state", trend.State);
            command.Parameters.AddWithValue("$plan", Plan(trend));

            await command.ExecuteNonQueryAsync(cancellation);

            written++;
        }

        await transaction.CommitAsync(cancellation);

        // The retention drop, after the write and outside its transaction, so
        // a night that fails partway leaves the old sets standing rather than
        // dropping them for rows it never wrote.
        var dropped = await DroppedAsync(connection, await BoundaryAsync(connection, cancellation), cancellation);

        var outcome = new LadderOutcome(
            members.Count,
            written,
            dropped,
            counts[TrendState.Uptrend],
            counts[TrendState.Downtrend],
            counts[TrendState.Range],
            counts[TrendState.NotClassified],
            withoutBars);

        await RecordAsync(connection, runId, startedAt, outcome, cancellation);

        return outcome;
    }

    // The plan, as SCHEMA's column describes it. Empty at 4.1 and saying why,
    // rather than absent: an empty plan stating its reason and an absent plan
    // are different objects and only the first is readable on a screen.
    //
    // The reason a name carries no tranche is the trend classifier's when the
    // state is not classified, and it is the checkpoint's until 4.4 builds the
    // tranches. Both are stated here rather than one standing for the other.
    static string Plan(Trend trend) =>
        JsonSerializer.Serialize(new
        {
            tranches = Array.Empty<object>(),
            exits = Array.Empty<object>(),
            invalidation = (string?)null,
            events = Array.Empty<object>(),
            reason = trend.Classified
                ? "the tranches, stops, exits and event setups are built from 4.4"
                : trend.Reason,
        });

    async Task<IReadOnlyList<string>> MembersAsync(
        SqliteConnection connection,
        string indexCode,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<(DateOnly SessionDate, decimal Close)?> LastSessionAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = LastSessionFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
               Money.FromStorage(reader.GetString(1)))
            : null;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        LadderOutcome outcome,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);

        command.Parameters.AddWithValue(
            "$detail",
            $"{outcome.RowsWritten} row(s) for {outcome.MembersConsidered} member(s), " +
            $"{outcome.Uptrend} uptrend, {outcome.Downtrend} downtrend, {outcome.Range} range, " +
            $"{outcome.NotClassified} not classified, {outcome.WithoutBars} with no stored bars, " +
            $"{outcome.RowsDropped} dropped");

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // One year back from the newest stored session, which is the boundary
    // BarFetcher already drops bars at. Read from the store rather than passed
    // in, so a component run on its own drops the same rows a night would.
    static async Task<DateOnly?> BoundaryAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        var newest = await command.ExecuteScalarAsync(cancellation);

        return newest is null or DBNull
            ? null
            : DateOnly.ParseExact((string)newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .AddYears(-BarFetcher.RetentionYears);
    }

    static async Task<int> DroppedAsync(
        SqliteConnection connection,
        DateOnly? boundary,
        CancellationToken cancellation)
    {
        if (boundary is not { } oldest)
        {
            return 0;
        }

        await using var command = connection.CreateCommand();

        command.CommandText = DropOlderThan;
        command.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

}
