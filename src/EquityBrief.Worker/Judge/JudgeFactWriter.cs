using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Judge;

// The AI judge's fact writer: for a night, every member of the three indices the night's ledger read, its fields, its
// short row and its full sheet as the judge is shown them under the field list's version, a night written again replacing
// its own rows, with one run log row of its own. It reads the night's stored rows alone, asks nothing of a network and
// calls no model, and no rule reads what it writes.
// see: The AI judge sees the fields the operator approved and nothing that names a company, a date or a price
public sealed class JudgeFactWriter(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LedgerReading, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.FundamentalScore, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.NewsArticle, Touch.Read),
            new StoreTouch(Store.NewsLabel, Touch.Read),
            new StoreTouch(Store.JudgeFact, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "judge-facts";

    const string DeleteNight = "DELETE FROM judge_fact WHERE session_date = $session;";

    const string Insert = @"
        INSERT INTO judge_fact (index_code, session_date, ticker, version, fields, row_text, sheet_text, written_at, run_id)
        VALUES ($index, $session, $ticker, $version, $fields, $row, $sheet, $written_at, $run_id);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    public async Task<int> WriteAsync(DateOnly session, string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var facts = await JudgeFactReader.ReadAsync(connection, session, null, cancellation);
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, DeleteNight, [("$session", day)], cancellation);

        foreach (var fact in facts)
        {
            await ExecuteAsync(connection, transaction, Insert,
            [
                ("$index", fact.Index), ("$session", day), ("$ticker", fact.Ticker), ("$version", JudgeFacts.Version),
                ("$fields", JsonSerializer.Serialize(fact.Shown.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToDictionary(pair => pair.Key, pair => pair.Value))),
                ("$row", JudgeFacts.RowText(fact)), ("$sheet", JudgeFacts.SheetText(fact)),
                ("$written_at", Stamp(clock.UtcNow)), ("$run_id", runId),
            ], cancellation);
        }

        var counts = string.Join(", ", facts.GroupBy(fact => fact.Index).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => FormattableString.Invariant($"{group.Key} {group.Count()}")));

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId), ("$stage", Stage), ("$started_at", Stamp(started)), ("$ended_at", Stamp(clock.UtcNow)), ("$rows", facts.Count),
            ("$detail", FormattableString.Invariant($"the judge's facts of {day} under field list {JudgeFacts.Version}: {facts.Count} member(s), {(facts.Count == 0 ? "none" : counts)}")),
        ], cancellation);

        await transaction.CommitAsync(cancellation);

        return facts.Count;
    }

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
