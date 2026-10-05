using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Nights;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// One request the drain took, as the worker reads it. The instant it was claimed at is
// carried with it, because the run that settles it is one that started at or after that
// instant and an earlier pass's run is not this request's. `Refresh` is a press that asked
// for every section to be written again.
public sealed record TakenRequest(string Ticker, string AskedAt, string Lane, DateTimeOffset ClaimedAt, bool Refresh = false);

// The worker's half of the request store: taking the oldest request nobody has started,
// and saying what came of it.
//
// The read surface writes the ask and this writes what the ask came to, so the table has
// two writers and no operation has two. Nothing here writes research: the pass does that,
// and this moves the request beside it.
// see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
public sealed class RequestDrain : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.FamilyNight, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Read),
            new StoreTouch(Store.ResearchRequest, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    RequestDrain()
    {
    }

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
        RETURNING ticker, asked_at, lane, refresh;
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
    // After the night has run, it asks for a report on the first names drawn on its page, each marked
    // as asked by the night, and starts the drain as a press does. The night writes a row a name and
    // starts one process; each pass is the drain's own run, with its calls on its own rows.
    // see: The night asks for a report on the first six names its page draws

    // How many names the night asks for, counted down its page: six across every family, on the
    // operator's ruling of 2026-10-01, where the ruling of 2026-09-23 asked for the first alone.
    public const int NightAsksFor = SetupFamilies.ReportsANight;

    // What a request the night wrote is marked as asked from, beside the two screens a press
    // comes from.
    public const string FromNight = "night";

    // Every name the swing filter passed on the night, in the order the list is drawn in: improving
    // businesses first where the night stored its readings, and the filter's own order within each
    // state and on a night that stored none.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    static readonly string PassedOnTheNight = @"
        SELECT g.ticker FROM gate_result g
        LEFT JOIN fundamental_reading f ON f.ticker = g.ticker AND f.session_date = g.session_date
        WHERE g.session_date = $night AND g.passed = 1
        ORDER BY " + FundamentalState.PlaceIn("f.state") + @", g.rank, g.ticker;
    ";

    // The stocks the sector heavyweights bought on the night, which the page draws after the families' picks and the
    // night asks for in that order; a holding carried from an earlier month is never one of them.
    // see: The night asks for a report on the first six names its page draws
    const string BoughtOnTheNight = "SELECT ticker FROM heavyweight_holding WHERE entered_on = $night ORDER BY sector, ticker;";

    // Each S&P 400's and 600's list on the night in its page's order, then the stocks its sector heavyweights bought.
    const string IndexListedOnTheNight = "SELECT ticker FROM index_family_pick WHERE index_code = $index AND session_date = $night AND state = 'listed' ORDER BY place, ticker;";

    const string IndexBoughtOnTheNight = "SELECT ticker FROM index_heavyweight_holding WHERE index_code = $index AND entered_on = $night ORDER BY sector, ticker;";

    // The names the night asks for, taken in turn from each index's list, one at a time in the order the lists are
    // handed in, each in its own page's order; a list with no name left passes its turn to the next, and a name is
    // taken once.
    public static IReadOnlyList<(string Ticker, int List)> TakenInTurn(IReadOnlyList<IReadOnlyList<string>> lists, int count)
    {
        var taken = new List<(string, int)>();
        var next = new int[lists.Count];

        while (taken.Count < count && Enumerable.Range(0, lists.Count).Any(list => next[list] < lists[list].Count))
        {
            for (var list = 0; list < lists.Count && taken.Count < count; list++)
            {
                while (next[list] < lists[list].Count && taken.Any(one => one.Item1 == lists[list][next[list]]))
                {
                    next[list]++;
                }

                if (next[list] < lists[list].Count)
                {
                    taken.Add((lists[list][next[list]++], list));
                }
            }
        }

        return taken;
    }

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

        // The page's list where the families drew one for the night, in the order the page draws it, and
        // the names the swing filter passed where they drew none.
        var passed = (await FamilyPicks.ListedAsync(connection, night, cancellation))?.ToList();
        var drawnByFamilies = passed is not null;

        if (passed is null)
        {
            passed = [];

            await using var reading = connection.CreateCommand();

            reading.CommandText = PassedOnTheNight;
            reading.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await reading.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                passed.Add(reader.GetString(0));
            }
        }

        await using (var bought = connection.CreateCommand())
        {
            bought.CommandText = BoughtOnTheNight;
            bought.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await bought.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (!passed.Contains(reader.GetString(0), StringComparer.Ordinal))
                {
                    passed.Add(reader.GetString(0));
                }
            }
        }

        // The S&P 400's and 600's lists beside the S&P 500's, the six taken in turn across them.
        var lists = new List<IReadOnlyList<string>> { passed };
        var named = new List<string> { "S&P 500" };

        foreach (var index in IndexFamilies.Indices)
        {
            var listed = new List<string>();

            foreach (var query in new[] { IndexListedOnTheNight, IndexBoughtOnTheNight })
            {
                await using var reading = connection.CreateCommand();

                reading.CommandText = query;
                reading.Parameters.AddWithValue("$index", index);
                reading.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                await using var reader = await reading.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    if (!listed.Contains(reader.GetString(0), StringComparer.Ordinal))
                    {
                        listed.Add(reader.GetString(0));
                    }
                }
            }

            lists.Add(listed);
            named.Add(index == FundHoldings.MidCapIndex ? "S&P 400's" : "S&P 600's");
        }

        var first = TakenInTurn(lists, NightAsksFor);

        if (first.Count == 0)
        {
            return new NightAsk([], drawnByFamilies
                ? FormattableString.Invariant($"no stock is on the page's list for {night:yyyy-MM-dd}, so no report was asked for")
                : FormattableString.Invariant($"no name passed the swing filter on {night:yyyy-MM-dd}, so no report was asked for"));
        }

        var asked = new List<string>();
        var said = new List<string>();

        foreach (var (ticker, list) in first)
        {
            var at = lists[list].ToList().IndexOf(ticker);
            var placed = (at == 0 ? "first" : FormattableString.Invariant($"number {at + 1}")) + (list == 0 ? " on the list" : $" on the {named[list]} list");

            await using var waiting = connection.CreateCommand();

            waiting.CommandText = Waiting;
            waiting.Parameters.AddWithValue("$ticker", ticker);

            if (await waiting.ExecuteScalarAsync(cancellation) is string state)
            {
                said.Add($"{ticker} is {placed} and has a request {state} already, so none was added");

                continue;
            }

            await using var asking = connection.CreateCommand();

            asking.CommandText = AskFromTheNight;
            asking.Parameters.AddWithValue("$ticker", ticker);
            asking.Parameters.AddWithValue("$asked_at", Stamped(clock.UtcNow));
            asking.Parameters.AddWithValue("$asked_from", FromNight);

            await asking.ExecuteNonQueryAsync(cancellation);

            asked.Add(ticker);
            said.Add($"{ticker} is {placed}, and a report on it was asked for");
        }

        return new NightAsk(asked, string.Join("; ", said));
    }

    // The longest pass the store holds that ran to its end, the bound on how long a pass a claim
    // starts may run, and nothing where the store holds none.
    static async Task<TimeSpan> LongestPassAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = PassRun.FinishedPasses;

        var longest = TimeSpan.Zero;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (PassRun.Took(reader.GetString(0), reader.GetString(1)) is { } took && took > longest)
            {
                longest = took;
            }
        }

        return longest;
    }

    const string Outstanding = @"
        SELECT COUNT(*) FROM research_request WHERE state = 'outstanding';
    ";

    const string PutBack = @"
        UPDATE research_request SET state = 'outstanding' WHERE state = 'writing';
    ";

    // Every request a drain left being written when it ended, put back as outstanding so this drain takes it. Run only by
    // a drain holding the drain's lock: one drain runs at a time, so a request being written when a drain takes the lock
    // was left by one that ended before its pass did.
    // see: A request a drain left being written is put back as outstanding by the next drain, and a pass that fails settles its request as refused
    public static async Task<int> PutBackAsync(string databaseFile, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = PutBack;

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    const string AppendStop = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, 0, 0, '0', $detail);
    ";

    // A drain's own work, its put-back and its queue, run so that an error escaping either is written before the
    // drain ends: one row under a run of its own named for the instant the drain started, with the stage `drain`, the
    // outcome `failed` and the error's type and words, no path a machine roots among them. Without it a drain that
    // ended on an error left no row a page reads, and what it had queued waited unseen for the next drain. The line
    // the verb prints comes back where the work stopped, and none where it finished.
    // see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
    public static async Task<string?> GuardAsync(string databaseFile, string dataRoot, IClock clock, Func<Task> work)
    {
        var startedAt = clock.UtcNow;

        try
        {
            await work();

            return null;
        }
        catch (Exception failure)
        {
            var said = $"{failure.GetType().Name}: {failure.Message}";

            try
            {
                await RecordStopAsync(databaseFile, dataRoot, startedAt, clock.UtcNow, said);
            }
            catch (Exception unwritten) when (unwritten is SqliteException or IOException)
            {
                return $"stopped on an error: {said}; its row could not be written either: {unwritten.Message}";
            }

            return $"stopped on an error: {said}";
        }
    }

    // The stop's one row, which the queue page states while it is newer than every pass and the run page's checklist
    // names for the night it fell on.
    public static async Task RecordStopAsync(
        string databaseFile,
        string dataRoot,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string error,
        CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendStop;
        command.Parameters.AddWithValue("$run_id", DrainStops.RunFor(startedAt));
        command.Parameters.AddWithValue("$stage", DrainStops.Stage);
        command.Parameters.AddWithValue("$started_at", Stamped(startedAt));
        command.Parameters.AddWithValue("$ended_at", Stamped(endedAt));
        command.Parameters.AddWithValue("$outcome", DrainStops.Failed);
        command.Parameters.AddWithValue("$detail", NightClose.Portable(error, dataRoot));

        await command.ExecuteNonQueryAsync(cancellation);
    }

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
        IReadOnlyList<ResearchPricing> pricings,
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

            // Every pass is paid at the off-peak rate. A pass's paid calls come at its end, so a
            // request is taken only where the longest pass the store holds would end before the
            // next peak window opens. A drain started inside a window, or whose next pass could
            // run into one, waits for the instant the pricing states before it claims, so the
            // request it will take stays outstanding while it waits and the queue screen reads it
            // as waiting rather than as being written. A drain with nothing outstanding has ended
            // above, and waits for nothing. The windows are those of every profile a pass's sections
            // may be written by.
            // see: Queued work runs off-peak, and every schedule is written in UTC
            // see: A pass starts only where the longest pass the store holds would end before a peak window opens
            var now = clock.UtcNow;
            var startsAt = ResearchPricing.StartFor(pricings, now, await LongestPassAsync(connection, cancellation));

            if (startsAt > now)
            {
                await connection.CloseAsync();
                await waitUntil(startsAt);

                continue;
            }

            var request = await ClaimAsync(connection, clock.UtcNow, cancellation);

            if (request is null)
            {
                break;
            }

            taken++;

            // The request carries the lane the press meant, so a queue drained a day later
            // writes under it rather than under whatever configuration now says, and whether
            // the press asked for every section to be written again, which the paid model
            // writes whole whatever lane the request names.
            // see: Nothing expires on a timer
            // see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
            string[] verb =
            [
                "research", "--ticker", request.Ticker,
                .. request.Lane == "paid" || request.Refresh ? ["--paid-for-local"] : Array.Empty<string>(),
                .. request.Refresh ? ["--refresh"] : Array.Empty<string>(),
            ];

            // A pass that ends on an error it wrote no run for is settled as refused with the error, so its request is
            // never left being written, and the drain goes on to the next.
            // see: A request a drain left being written is put back as outstanding by the next drain, and a pass that fails settles its request as refused
            try
            {
                await pass(verb);
            }
            catch (Exception failure) when (failure is not OperationCanceledException || !cancellation.IsCancellationRequested)
            {
                await SettleAsync(connection, request, SettlementFor(null).State, "the pass stopped on an error before it wrote its run: " + failure.Message, null, clock.UtcNow, cancellation);

                continue;
            }

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

    // Taken inside a transaction that holds the write lock from its start and commits as a statement of its own,
    // so the claim waits for another connection as every statement here does. Run alone, the claim's commit comes
    // as its reader is let go, after its row is read, where the driver's wait does not reach, and it fails on a
    // read another connection holds at that moment.
    //
    // The transaction runs one statement, its write, so a deferred begin would take the write lock at that
    // statement in one step, as the immediate begin takes it at the begin, and the two are equivalent only while
    // that holds. A read placed before the write would take a shared lock first and then need to raise it, which
    // is the wait the driver does not cover, so the begin stays immediate.
    // see: A writer waits up to ten minutes for another, and a pass stores what it fetched in one write
    public static async Task<TakenRequest?> ClaimAsync(SqliteConnection connection, DateTimeOffset at, CancellationToken cancellation = default)
    {
        await using var transaction = connection.BeginTransaction(deferred: false);
        TakenRequest? taken;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = Claim;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            taken = await reader.ReadAsync(cancellation)
                ? new TakenRequest(reader.GetString(0), reader.GetString(1), reader.GetString(2), at, reader.GetInt64(3) == 1)
                : null;
        }

        await transaction.CommitAsync(cancellation);

        return taken;
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
