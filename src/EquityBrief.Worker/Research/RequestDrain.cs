using System.Globalization;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// One request the drain took, as the worker reads it.
public sealed record TakenRequest(string Ticker, string AskedAt, string Lane);

// The worker's half of the request store: taking the oldest request nobody has started,
// and saying what came of it.
//
// The read surface writes the ask and this writes what the ask came to, so the table has
// two writers and no operation has two. Nothing here writes research: the pass does that,
// and this moves the request beside it.
// see: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
public static class RequestDrain
{
    const string Instant = "yyyy-MM-ddTHH:mm:ssZ";

    // The oldest request nobody has started, which is the order a reader is shown and the
    // order the drain works in. Taken and marked in one statement so two drains cannot
    // take the same row.
    const string Claim = @"
        UPDATE research_request
        SET state = 'writing'
        WHERE (ticker, asked_at) = (
            SELECT ticker, asked_at FROM research_request
            WHERE state = 'outstanding'
            ORDER BY asked_at, ticker
            LIMIT 1)
        RETURNING ticker, asked_at, lane;
    ";

    // The run is written at the settle rather than at the claim, because a pass names its
    // own run from the instant it starts and this cannot know that before it runs.
    const string Settle = @"
        UPDATE research_request
        SET state = $state, settled_at = $settled_at, reason = $reason, run_id = $run_id
        WHERE ticker = $ticker AND asked_at = $asked_at;
    ";

    // The newest research run for a name and what it came to, which is the pass this
    // request just ran. What it came to decides the request's state: a verb that exits
    // zero has run, and a pass that ran is not a pass that wrote, which is the shape the
    // first drain recorded two unavailable passes as written under.
    const string NewestRun = @"
        SELECT run_id, outcome FROM run_log
        WHERE stage = 'research' AND run_id LIKE '%-' || $ticker
        ORDER BY started_at DESC, rowid DESC LIMIT 1;
    ";

    // What the research runner calls a pass that ran to its end.
    public const string Ok = "ok";

    // What a request is settled as, decided by what the pass came to and never by whether
    // the verb exited without failing. A verb that exits zero has run, and a pass that ran
    // is not a pass that wrote: a pass the model could not be reached for exits zero and
    // writes nothing, which the first drain recorded two of as written.
    //
    // A function of the outcome alone, so the rule can be read and asserted rather than
    // living inside the loop that applies it.
    public static (string State, string? Reason) SettlementFor(string? outcome) =>
        string.Equals(outcome, Ok, StringComparison.Ordinal)
            ? ("written", null)
            : ("refused", $"the pass came to {outcome ?? "no run at all"}, and its own run says why");

    const string Outstanding = @"
        SELECT COUNT(*) FROM research_request WHERE state = 'outstanding';
    ";

    public static async Task<TakenRequest?> ClaimAsync(SqliteConnection connection, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Claim;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new TakenRequest(reader.GetString(0), reader.GetString(1), reader.GetString(2))
            : null;
    }

    public static async Task SettleAsync(
        SqliteConnection connection,
        TakenRequest request,
        string state,
        string? reason,
        DateTimeOffset at,
        CancellationToken cancellation = default)
    {
        var (runId, _) = await PassAsync(connection, request.Ticker, cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = Settle;
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$settled_at", at.ToString(Instant, CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$ticker", request.Ticker);
        command.Parameters.AddWithValue("$asked_at", request.AskedAt);
        command.Parameters.AddWithValue("$run_id", (object?)runId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // The run the pass wrote and what it came to, for the settle and for the drain's own
    // decision about which state the request lands in.
    public static async Task<(string? RunId, string? Outcome)> PassAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestRun;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (reader.GetString(0), reader.GetString(1))
            : (null, null);
    }

    public static async Task<int> OutstandingAsync(SqliteConnection connection, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Outstanding;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }
}
