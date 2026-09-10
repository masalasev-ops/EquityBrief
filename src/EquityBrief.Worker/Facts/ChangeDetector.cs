using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Facts;

public sealed record ChangeOutcome(int RowsExamined, int RowsWritten, int ChangesRecorded);

// The change detector. Compares tonight's facts against the last stored ones for
// each name and writes only what changed, on its own column of the same row.
//
// It owns `material_changes` and touches neither `payload` nor `payload_hash`.
// The assembler owns those two, the grain is the same, and the sets are disjoint,
// which is what permits a table with an inserter and a different updater under a
// rule that forbids two owners for one operation. That split is a property
// rather than a coincidence of ordering: a facts re-run must not blank the
// change list, and the assembler's own statement names two columns and no more.
//
// From 5.4 it also empties the payload of a past night the name did not fire on,
// which is the retention section 16 states. That read is of the listings store
// and waits for the checkpoint that creates it, because a component declaring a
// read of a table nothing has created is a declaration with nothing behind it.
public sealed class ChangeDetector : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Facts, Touch.Read | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "changes";

    // Tonight's row and the one before it, per name, in one pass. The previous
    // row is the newest strictly older session rather than yesterday, because a
    // name whose night did not run has no row for yesterday and the comparison
    // is still against the last facts anybody wrote.
    const string PairsToCompare = @"
        SELECT f.ticker,
               f.session_date,
               f.payload,
               (SELECT p.payload FROM facts p
                WHERE p.ticker = f.ticker AND p.session_date < f.session_date
                ORDER BY p.session_date DESC LIMIT 1)
        FROM facts f
        WHERE f.session_date = (SELECT MAX(s.session_date) FROM facts s WHERE s.ticker = f.ticker)
        ORDER BY f.ticker;
    ";

    // This component's own column and no other. Written as an update rather than
    // an upsert, because a facts row it has not seen is a row the assembler has
    // not written and there is nothing to compare.
    const string RecordChanges = @"
        UPDATE facts
        SET material_changes = $material_changes
        WHERE ticker = $ticker AND session_date = $session_date;
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

    public ChangeDetector(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<ChangeOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var pairs = new List<(string Ticker, string SessionDate, string Payload, string? Previous)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PairsToCompare;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                pairs.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        var written = 0;
        var changes = 0;

        foreach (var pair in pairs)
        {
            var changed = FactsFile.Changed(pair.Previous, pair.Payload);

            await using var command = connection.CreateCommand();

            command.CommandText = RecordChanges;
            command.Parameters.AddWithValue("$ticker", pair.Ticker);
            command.Parameters.AddWithValue("$session_date", pair.SessionDate);

            // An empty list rather than a null where there is nothing to
            // compare against, because a name whose first night this is has no
            // changes rather than an unknown number of them, and the two read
            // differently on a page.
            command.Parameters.AddWithValue("$material_changes", JsonSerializer.Serialize(changed));

            await command.ExecuteNonQueryAsync(cancellation);

            written++;
            changes += changed.Count;
        }

        await RecordAsync(connection, runId, startedAt, pairs.Count, written, changes, cancellation);

        return new ChangeOutcome(pairs.Count, written, changes);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int examined,
        int written,
        int changes,
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
        command.Parameters.AddWithValue(
            "$detail",
            $"{examined} row(s) examined, {written} written, {changes} material change(s)");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
