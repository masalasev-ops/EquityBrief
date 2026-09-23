using System.Globalization;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
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
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
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

    // ---- the night's own request ----
    //
    // After the night has run, it asks for a report on the first name drawn on its list, marked as
    // asked by the night, and starts the drain as a press does. The night writes this one row and
    // starts one process; the pass is the drain's own run, with its calls on its own rows.
    // see: The night asks for a report on the first name of its list

    // How many names the night asks for, counted from the top of its list: the first alone, on the
    // operator's ruling of 2026-09-23, "just the top 1st name for now".
    public const int NightAsksFor = 1;

    // What a request the night wrote is marked as asked from, beside the two screens a press
    // comes from.
    public const string FromNight = "night";

    // Every name that fired on the night, with what the order it is drawn in reads off the row.
    const string FiredOnTheNight = @"
        SELECT ticker, fired_count, plan_at_listing FROM listing
        WHERE session_date = $night AND fired_count > 0;
    ";

    // A request for the name nobody has settled, being one outstanding or being written.
    const string Waiting = @"
        SELECT state FROM research_request
        WHERE ticker = $ticker AND state IN ('outstanding', 'writing')
        LIMIT 1;
    ";

    const string AskFromTheNight = @"
        INSERT INTO research_request (ticker, asked_at, asked_from, lane, state)
        VALUES ($ticker, $asked_at, $asked_from, 'paid', 'outstanding');
    ";

    // The night's own stage on the run log, which the night's step writes under.
    public const string NightStage = "report";

    const string AppendNightRow = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    // The night's own row for its request, written once the drain has been asked to start so it
    // says what was asked for and what the drain came to. It calls no model and makes no request:
    // the pass is the drain's own run, with its calls and requests on its own rows.
    public static async Task RecordTheNightAsync(
        string databaseFile,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        int asked,
        string detail,
        CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        await using var command = connection.CreateCommand();

        command.CommandText = AppendNightRow;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", NightStage);
        command.Parameters.AddWithValue("$started_at", Stamped(startedAt));
        command.Parameters.AddWithValue("$ended_at", Stamped(endedAt));
        command.Parameters.AddWithValue("$rows_written", asked);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // What the night's ask came to: the names it asked for, and the line the night's own run log
    // row carries, which says why where it asked for none.
    public sealed record NightAsk(IReadOnlyList<string> Asked, string Line);

    public static async Task<NightAsk> AskForTheNightAsync(
        string databaseFile,
        DateOnly night,
        IClock clock,
        CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        var fired = new List<(string Ticker, int Fired, decimal? RewardToRisk)>();

        await using (var reading = connection.CreateCommand())
        {
            reading.CommandText = FiredOnTheNight;
            reading.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await reading.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                fired.Add((reader.GetString(0), reader.GetInt32(1), DrawnOrder.FirstTranche(reader.GetString(2)).RewardToRisk));
            }
        }

        var first = DrawnOrder.Ordered(fired, row => row.Fired, row => row.RewardToRisk, row => row.Ticker)
            .Take(NightAsksFor)
            .Select(row => row.Ticker)
            .ToArray();

        if (first.Length == 0)
        {
            return new NightAsk([], FormattableString.Invariant($"no name fired on {night:yyyy-MM-dd}, so no report was asked for"));
        }

        var asked = new List<string>();
        var said = new List<string>();

        foreach (var ticker in first)
        {
            await using var waiting = connection.CreateCommand();

            waiting.CommandText = Waiting;
            waiting.Parameters.AddWithValue("$ticker", ticker);

            if (await waiting.ExecuteScalarAsync(cancellation) is string state)
            {
                said.Add($"{ticker} is first on the list and has a request {state} already, so none was added");

                continue;
            }

            await using var asking = connection.CreateCommand();

            asking.CommandText = AskFromTheNight;
            asking.Parameters.AddWithValue("$ticker", ticker);
            asking.Parameters.AddWithValue("$asked_at", Stamped(clock.UtcNow));
            asking.Parameters.AddWithValue("$asked_from", FromNight);

            await asking.ExecuteNonQueryAsync(cancellation);

            asked.Add(ticker);
            said.Add($"{ticker} is first on the list, and a report on it was asked for");
        }

        return new NightAsk(asked, string.Join("; ", said));
    }

    const string Outstanding = @"
        SELECT COUNT(*) FROM research_request WHERE state = 'outstanding';
    ";

    // The queue, worked through oldest first until nothing is outstanding. The pass is
    // handed in, so the loop that decides which run a request settles under is the one the
    // worker runs and not a copy of it beside a test.
    //
    // Each request is claimed at the clock's own instant, which is the lower bound on the
    // run its pass may settle under: a claim dated earlier would read an older pass of the
    // same name as this request's.
    //
    // The wait is handed in with the prices, so a test chooses the instant a wait ends at
    // and the loop that decides whether to wait is the one the worker runs.
    public static async Task<(int Taken, int Written)> DrainAsync(
        string databaseFile,
        IClock clock,
        Func<string[], Task> pass,
        ResearchPricing pricing,
        Func<DateTimeOffset, Task> waitUntil,
        CancellationToken cancellation = default)
    {
        var taken = 0;
        var written = 0;

        while (true)
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

            await connection.OpenAsync(cancellation);

            if (await OutstandingAsync(connection, cancellation) == 0)
            {
                break;
            }

            // Every pass is paid at the off-peak rate. A drain started inside a peak window,
            // or reaching one between passes, waits for the pricing's first off-peak instant
            // before it claims, so the request it will take stays outstanding while it waits
            // and the queue screen reads it as waiting rather than as being written. A drain
            // with nothing outstanding has ended above, and waits for nothing.
            // see: Queued work runs off-peak, and every schedule is written in UTC
            var now = clock.UtcNow;

            if (pricing.IsPeak(now))
            {
                await connection.CloseAsync();
                await waitUntil(pricing.OffPeakFrom(now));

                continue;
            }

            var request = await ClaimAsync(connection, clock.UtcNow, cancellation);

            if (request is null)
            {
                break;
            }

            taken++;

            // The request carries the lane the press meant, so a queue drained a day later
            // writes under it rather than under whatever configuration now says.
            string[] verb = request.Lane == "paid"
                ? ["research", "--ticker", request.Ticker, "--paid-for-local"]
                : ["research", "--ticker", request.Ticker];

            await pass(verb);

            // What this request's own pass came to, and not whether the verb exited zero. A
            // verb that exits zero has run, and a pass that ran is not a pass that wrote: a
            // pass the model could not be reached for exits zero and writes nothing, and one
            // refused before the runner starts writes no run for this request at all.
            var (runId, outcome) = await PassAsync(connection, request, cancellation);
            var (state, reason) = SettlementFor(outcome);

            if (reason is null)
            {
                written++;
            }

            await SettleAsync(connection, request, state, reason, runId, clock.UtcNow, cancellation);
        }

        return (taken, written);
    }

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
