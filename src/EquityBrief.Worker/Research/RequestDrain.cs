using System.Globalization;
using EquityBrief.Core.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// One request the drain took, as the worker reads it. The instant it was claimed at is
// carried with it, because the run that settles it is one that started at or after that
// instant and an earlier pass's run is not this request's.
public sealed record TakenRequest(string Ticker, string AskedAt, string Lane, DateTimeOffset ClaimedAt);

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

    // This request's own run and what it came to, being one of this name's passes that
    // started at or after the request was claimed. What it came to decides the request's
    // state: a verb that exits zero has run, and a pass that ran is not a pass that wrote.
    //
    // The instant is what makes the run this request's rather than this name's. A pass
    // that refuses before the runner starts writes no run at all, and without the bound
    // the newest run for the name is whatever that name last did, so a request for a name
    // already holding research settles under a run that said it wrote.
    //
    // The pattern is the one a pass names its runs by rather than a second spelling of it.
    const string NewestRun = @"
        SELECT run_id, outcome FROM run_log
        WHERE stage = 'research' AND run_id LIKE $like AND started_at >= $since
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
            : ("refused", outcome is null
                ? "the pass left no run at all, so it stopped before it could write one"
                : $"the pass came to {outcome}, and its own run says why");

    const string Outstanding = @"
        SELECT COUNT(*) FROM research_request WHERE state = 'outstanding';
    ";

    public static async Task<TakenRequest?> ClaimAsync(SqliteConnection connection, DateTimeOffset at, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Claim;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new TakenRequest(reader.GetString(0), reader.GetString(1), reader.GetString(2), at)
            : null;
    }

    // The run is handed in rather than read again here, so the row this settles under is
    // the one the state was decided from and the two cannot come to disagree.
    public static async Task SettleAsync(
        SqliteConnection connection,
        TakenRequest request,
        string state,
        string? reason,
        string? runId,
        DateTimeOffset at,
        CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Settle;
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$settled_at", Stamped(at));
        command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$ticker", request.Ticker);
        command.Parameters.AddWithValue("$asked_at", request.AskedAt);
        command.Parameters.AddWithValue("$run_id", (object?)runId ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // The run this request's pass wrote and what it came to, for the settle and for the
    // drain's own decision about which state the request lands in. A request whose pass
    // wrote no run of its own reads as no run at all rather than as an older one.
    public static async Task<(string? RunId, string? Outcome)> PassAsync(
        SqliteConnection connection,
        TakenRequest request,
        CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestRun;
        command.Parameters.AddWithValue("$like", PassRun.Like(request.Ticker));
        command.Parameters.AddWithValue("$since", Stamped(request.ClaimedAt));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (reader.GetString(0), reader.GetString(1))
            : (null, null);
    }

    // The form a run's own instant is stored in, so the bound compares against the column
    // character by character rather than against a second rendering of the same clock.
    static string Stamped(DateTimeOffset at) =>
        at.UtcDateTime.ToString(Instant, CultureInfo.InvariantCulture);

    public static async Task<int> OutstandingAsync(SqliteConnection connection, CancellationToken cancellation = default)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Outstanding;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }
}
