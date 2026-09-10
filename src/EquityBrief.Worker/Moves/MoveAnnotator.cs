using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Moves;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Moves;

public sealed record MoveOutcome(int NamesExamined, int RowsWritten, int RowsDropped);

// The move annotator. Reads the stored bars for every name the store holds and
// writes the largest single-day and multi-day moves of the stored year, which
// become the rows of the how-it-got-here table.
//
// It makes no request and calls no model, for the reason the swing finder does
// not: everything it needs is already in the store.
// see: The nightly run is arithmetic only
//
// The cause of each move is not written here. It is a researched claim and lives
// in `research_section` with its source, so the table's cause column is
// explicitly absent until phase 6 rather than blank. An absence stated and an
// absence drawn as emptiness are different things.
//
// The arithmetic is in `MoveSeries`, as a pure function of a session-ordered
// series. What is here is reading, writing and the run log.
// see: Code owns every number
public sealed class MoveAnnotator : IComponent
{
    // The last of the six computed tables to gain a deleter, which is what 4.0
    // ruled and 4.2 implemented for the other five: every computed table's
    // writer is its own deleter. `move` waited for this checkpoint because the
    // component did not exist until now, and a deleter declared before its
    // component deletes is a declaration with nothing behind it.
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Move, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "moves";

    // Every name with bars, rather than every current member, for the reason the
    // swing finder states: a name that left the index keeps its stored history
    // and a report opened on it should read what it read last night.
    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    const string BarsFor = @"
        SELECT session_date, close
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date;
    ";

    // Insert or update on the primary key. A move is recomputed from the same
    // bars every night and a name's set changes as the window moves, so a
    // session that was a top-eight move last night and is not tonight has to
    // go rather than stand.
    const string Upsert = @"
        INSERT INTO move (ticker, session_date, sessions, change_pct, rank)
        VALUES ($ticker, $session_date, $sessions, $change_pct, $rank)
        ON CONFLICT (ticker, session_date) DO UPDATE SET
            sessions = excluded.sessions,
            change_pct = excluded.change_pct,
            rank = excluded.rank;
    ";

    // The rows this name no longer holds. Written as a delete of everything for
    // the name outside tonight's set rather than a delete of the whole name and
    // a reinsert, because the second would take rows out of a set it then puts
    // back and this table is read between the two by nothing at all only while
    // that is true.
    const string DropFallenOut = @"
        DELETE FROM move WHERE ticker = $ticker AND session_date > $oldest AND rank > $kept;
    ";

    // The retention drop, one year back from the newest stored session, which is
    // the boundary the other five computed tables drop at. A move does not
    // outlive the bar it sits on.
    const string DropOlderThan = @"
        DELETE FROM move WHERE session_date < $oldest;
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

    public MoveAnnotator(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<MoveOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var tickers = await TickersAsync(connection, cancellation);
        var written = 0;
        var fallenOut = 0;

        foreach (var ticker in tickers)
        {
            var moves = MoveSeries.For(await BarsAsync(connection, ticker, cancellation));

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            foreach (var move in moves)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Upsert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session_date", move.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$sessions", move.Sessions);
                command.Parameters.AddWithValue("$change_pct", move.ChangePct);
                command.Parameters.AddWithValue("$rank", move.Rank);

                await command.ExecuteNonQueryAsync(cancellation);

                written++;
            }

            // Anything this name held above tonight's rank count is a session
            // that was a biggest move and is not one now.
            await using (var drop = connection.CreateCommand())
            {
                drop.Transaction = (SqliteTransaction)transaction;
                drop.CommandText = DropFallenOut;
                drop.Parameters.AddWithValue("$ticker", ticker);
                drop.Parameters.AddWithValue("$oldest", "0000-00-00");
                drop.Parameters.AddWithValue("$kept", moves.Count);

                fallenOut += await drop.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        var dropped = await DroppedAsync(connection, await BoundaryAsync(connection, cancellation), cancellation);

        await RecordAsync(connection, runId, startedAt, tickers.Count, written, dropped + fallenOut, cancellation);

        return new MoveOutcome(tickers.Count, written, dropped + fallenOut);
    }

    async Task<IReadOnlyList<string>> TickersAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = TickersWithBars;

        var tickers = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    // The close is a price and is read as a decimal. The percentage the
    // arithmetic makes of it is a statistic and is a double, and the crossing is
    // in `MoveSeries` where it is named rather than here where it would be
    // incidental.
    async Task<IReadOnlyList<MoveBar>> BarsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BarsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var bars = new List<MoveBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new MoveBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1))));
        }

        return bars;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int names,
        int written,
        int dropped,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", written);
        command.Parameters.AddWithValue("$detail", $"{names} name(s), {written} move(s), {dropped} dropped");

        await command.ExecuteNonQueryAsync(cancellation);
    }

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
