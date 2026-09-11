using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// `Requests` is every request the stage made, being the action feed's and the
// historical feed's together, and `RefetchRequests` is the per-name part of it.
// The run log carried the action feed's alone until the phase 5 sign-off, so the
// one per-name request the cost rule carves out by name was the one the store
// never counted: the third by-hand night of 2026-09-09 made eleven requests at
// this stage and its row said two.
public sealed record ActionCheckOutcome(
    int Actions,
    int Refetched,
    int RowsReplaced,
    int Requests,
    IReadOnlyList<string> Suspect,
    int RefetchRequests = 0,
    // Names an earlier night left suspect and this one refetched again.
    IReadOnlyList<string>? Retried = null);

// The corporate action check. One bulk request per kind per night, and a
// full-year refetch of any current member whose adjusted prices an action has
// moved.
//
// The refetch replaces the name's whole year inside one transaction, which is
// the only sanctioned removal of a bar besides retention. A partial replacement
// would leave a series half at the old adjustment and half at the new, which is
// worse than either: every level computed over it would sit between two price
// scales and nothing downstream would notice.
// see: Adjusted history is re-fetched after a corporate action
// see: Bars are never interpolated
//
// A failure of the check itself marks the name suspect rather than passing.
// That is the whole of contradiction C: without somewhere to put it, a check
// that could not run looked exactly like a check that found nothing.
public sealed class CorporateActionChecker : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read | Touch.Insert | Touch.Delete),
            // Read as well as written, from the phase 5 sign-off. The mark is an
            // upsert, so a name that was suspect and now refetches cleanly is set
            // back to ok by the run that established it, and a name that fails
            // again is set to suspect again with the new reason. Until then it
            // was written and never read, so a name whose refetch failed stayed
            // suspect with its adjusted history out of step with the provider,
            // and nothing asked for it again unless another action landed on it.
            new StoreTouch(Store.SeriesState, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.SplitsAndDividends, Feed.HistoricalPrice]);

    public const string Stage = "actions";
    public const string Source = "refetch";
    public const string Exchange = "US";

    public const string Ok = "ok";
    public const string Suspect = "suspect";

    readonly ICorporateActionFeed actions;
    readonly IHistoricalBarFeed history;
    readonly IClock clock;
    readonly string databaseFile;

    public CorporateActionChecker(
        ICorporateActionFeed actions,
        IHistoricalBarFeed history,
        IClock clock,
        string databaseFile)
    {
        this.actions = actions;
        this.history = history;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The names whose bars the night stores, being every name that has not left
    // by tonight's session, so a joining name's year is adjusted as well as
    // stored before it joins.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string CurrentMembers = @"
        SELECT ticker FROM membership
        WHERE index_code = $index AND (""left"" IS NULL OR ""left"" > $session);
    ";

    // The names a night before this one marked suspect, among the names the
    // night stores. Each is refetched again tonight whether or not an action
    // landed on it today, because the action that made it suspect is still in
    // its stored history unadjusted. A refetch is the per-name request the cost
    // rule carves out, and this adds one per suspect name, which grows with the
    // failures and never with the index.
    // see: Adjusted history is re-fetched after a corporate action
    const string SuspectNames = @"
        SELECT s.ticker FROM series_state s
        WHERE s.state = $suspect
          AND EXISTS (
              SELECT 1 FROM membership m
              WHERE m.index_code = $index AND m.ticker = s.ticker
                AND (m.""left"" IS NULL OR m.""left"" > $session));
    ";

    // The only sanctioned removal of a bar besides retention, and it is paired
    // with the insert that follows it inside one transaction.
    const string DropYear = @"
        DELETE FROM bar WHERE ticker = $ticker;
    ";

    const string InsertBar = @"
        INSERT INTO bar (
            ticker, session_date, open, high, low, close,
            volume, source, observed_at, raw_close)
        VALUES (
            $ticker, $session_date, $open, $high, $low, $close,
            $volume, $source, $observed_at, $raw_close);
    ";

    const string MarkState = @"
        INSERT INTO series_state (ticker, state, reason, checked_at)
        VALUES ($ticker, $state, $reason, $checked_at)
        ON CONFLICT (ticker) DO UPDATE SET
            state = excluded.state,
            reason = excluded.reason,
            checked_at = excluded.checked_at;
    ";

    const string BarCount = "SELECT COUNT(*) FROM bar;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<ActionCheckOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var started = clock.UtcNow;
        var session = clock.SessionDateAt(started);
        var observed = started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync();

        var members = await MembersAsync(connection, indexCode, session);
        var before = await CountAsync(connection);

        // Both feeds counted from here, as deltas, so the figure is this run's
        // and not whatever a feed shared with an earlier stage had already made.
        var actionRequestsBefore = actions.Requests;
        var refetchRequestsBefore = history.Requests;

        var today = await actions.ActionsAsync(Exchange, session, cancellationToken);

        // Current members only. An action on a name the index does not hold is
        // not this system's concern, and the fixture carries three of them so
        // the filter has something to reject.
        //
        // And every name an earlier night left suspect, retried until one
        // refetch succeeds.
        var retried = await SuspectAsync(connection, indexCode, session);

        var affected = today
            .Where(action => members.Contains(action.Ticker))
            .Select(action => action.Ticker)
            .Concat(retried)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ticker => ticker, StringComparer.Ordinal)
            .ToArray();

        var suspect = new List<string>();
        var refetched = 0;

        foreach (var ticker in affected)
        {
            // Each name in its own transaction, so one name's failure marks
            // that name and leaves the others' replacements committed. A single
            // transaction over every name would make one bad response undo work
            // that was correct.
            await using var transaction = await connection.BeginTransactionAsync();

            try
            {
                var year = await history.BarsAsync(ticker, session.AddYears(-1), session, cancellationToken);

                if (year.Count == 0)
                {
                    throw new InvalidOperationException(
                        "the refetch returned no bars, so replacing the year would empty it");
                }

                await using (var drop = connection.CreateCommand())
                {
                    drop.CommandText = DropYear;
                    drop.Parameters.AddWithValue("$ticker", ticker);

                    await drop.ExecuteNonQueryAsync();
                }

                foreach (var bar in year)
                {
                    await StoreAsync(connection, ticker, bar, observed);
                }

                await MarkAsync(connection, ticker, Ok, null, observed);
                await transaction.CommitAsync();

                refetched++;
            }
            // The night's deadline is not this name's failure and is not caught
            // here. Until the phase 5 sign-off it was: a deadline passed during a
            // refetch marked that name suspect, the stage went on to the next
            // name and finished, and the night stopped on the calendar step's
            // first request, so the run log named the wrong step and a name that
            // had nothing wrong with it was suspect. The transaction's disposal
            // rolls the name back, as a failure's does.
            catch (Exception failure) when (!(failure is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                // The stored series is left as it was, and the name is marked
                // rather than passing. A check that could not run must not look
                // like a check that found nothing.
                await transaction.RollbackAsync();

                await MarkAsync(connection, ticker, Suspect, failure.Message, observed);
                suspect.Add(ticker);
            }
        }

        var refetchRequests = history.Requests - refetchRequestsBefore;

        var outcome = new ActionCheckOutcome(
            today.Count,
            refetched,
            await CountAsync(connection) - before,
            actions.Requests - actionRequestsBefore + refetchRequests,
            suspect,
            refetchRequests,
            retried);

        await AppendAsync(connection, runId, observed, outcome);

        return outcome;
    }

    static async Task<IReadOnlyList<string>> SuspectAsync(SqliteConnection connection, string indexCode, DateOnly session)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SuspectNames;
        command.Parameters.AddWithValue("$suspect", Suspect);
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    static async Task<HashSet<string>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<int> CountAsync(SqliteConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BarCount;

        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    static async Task StoreAsync(SqliteConnection connection, string ticker, ProviderBar bar, string observed)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = InsertBar;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        Money.Bind(command, "$open", bar.Open);
        Money.Bind(command, "$high", bar.High);
        Money.Bind(command, "$low", bar.Low);
        Money.Bind(command, "$close", bar.Close);
        Money.Bind(command, "$raw_close", bar.RawClose);

        command.Parameters.AddWithValue("$volume", bar.Volume);
        command.Parameters.AddWithValue("$source", Source);
        command.Parameters.AddWithValue("$observed_at", observed);

        await command.ExecuteNonQueryAsync();
    }

    static async Task MarkAsync(
        SqliteConnection connection,
        string ticker,
        string state,
        string? reason,
        string observed)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MarkState;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$checked_at", observed);

        await command.ExecuteNonQueryAsync();
    }

    async Task AppendAsync(
        SqliteConnection connection,
        string runId,
        string started,
        ActionCheckOutcome outcome)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", started);
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome.Suspect.Count == 0 ? "ok" : "partial");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsReplaced);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue(
            "$detail",
            $"{outcome.Actions} action(s), {outcome.Refetched} refetched, " +
            $"{outcome.RefetchRequests} of the {outcome.Requests} request(s) per name, " +
            $"{(outcome.Retried ?? []).Count} retried from an earlier night" +
            ((outcome.Retried ?? []).Count == 0 ? string.Empty : ": " + string.Join(", ", outcome.Retried!)) +
            (outcome.Suspect.Count == 0 ? string.Empty : ", suspect: " + string.Join(", ", outcome.Suspect)));

        await command.ExecuteNonQueryAsync();
    }
}
