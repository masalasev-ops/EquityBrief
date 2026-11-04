using System.Globalization;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Components;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Charts;

// The marks the operator has ruled on sessions of the pulled history: written by a verb a person runs, one session a row, and no
// pulled row moves.
// see: The operator's word on a session of the pull is a mark beside it and no pulled row moves, and only the chart's reads and the chart check read it until phase 23's restart
public sealed class SessionMarks(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.SessionMark, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "session-mark";

    const string Insert = @"
        INSERT INTO session_mark (ticker, session_date, kind, says, ruled, run_id)
        VALUES ($ticker, $session, $kind, $says, $ruled, $run_id)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $started_at, 'ok', $rows, 0, 0, '0', $detail);
    ";

    // Writes the mark, a second word on the same session changing nothing, and states what it did.
    public async Task<int> MarkAsync(string ticker, DateOnly session, string kind, string says, string runId)
    {
        if (!SessionMarking.Kinds.Contains(kind))
        {
            throw new ArgumentException($"a session's mark is one of: {string.Join(", ", SessionMarking.Kinds)}", nameof(kind));
        }

        var now = clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using var insert = connection.CreateCommand();
        insert.CommandText = Insert;
        insert.Parameters.AddWithValue("$ticker", ticker);
        insert.Parameters.AddWithValue("$session", day);
        insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.AddWithValue("$says", says);
        insert.Parameters.AddWithValue("$ruled", now);
        insert.Parameters.AddWithValue("$run_id", runId);

        var written = await insert.ExecuteNonQueryAsync();

        await using var run = connection.CreateCommand();
        run.CommandText = AppendRun;
        run.Parameters.AddWithValue("$run_id", runId);
        run.Parameters.AddWithValue("$stage", Stage);
        run.Parameters.AddWithValue("$started_at", now);
        run.Parameters.AddWithValue("$rows", written);
        run.Parameters.AddWithValue("$detail", FormattableString.Invariant($"{ticker} {day} {kind}, {written} row written"));

        await run.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        return written;
    }
}
