using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

public sealed record ActionCheckOutcome(
    int Actions,
    int Refetched,
    int RowsReplaced,
    int Requests,
    IReadOnlyList<string> Suspect);

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
            // Written and never read. The mark is an upsert, so the check does
            // not ask what the state was before setting it: a name that was
            // suspect and now refetches cleanly is set back to ok by the run
            // that established it, and a name that fails again is set to
            // suspect again with the new reason.
            new StoreTouch(Store.SeriesState, Touch.Insert | Touch.Update),
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

    const string CurrentMembers = @"
        SELECT ticker FROM membership WHERE index_code = $index AND left IS NULL;
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
        var observed = started.ToString("yyyy-MM-ddTHH:mm:ssZ");

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync();

        var members = await MembersAsync(connection, indexCode);
        var before = await CountAsync(connection);

        var today = await actions.ActionsAsync(Exchange, session, cancellationToken);

        // Current members only. An action on a name the index does not hold is
        // not this system's concern, and the fixture carries three of them so
        // the filter has something to reject.
        var affected = today
            .Where(action => members.Contains(action.Ticker))
            .Select(action => action.Ticker)
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
            catch (Exception failure)
            {
                // The stored series is left as it was, and the name is marked
                // rather than passing. A check that could not run must not look
                // like a check that found nothing.
                await transaction.RollbackAsync();

                await MarkAsync(connection, ticker, Suspect, failure.Message, observed);
                suspect.Add(ticker);
            }
        }

        var outcome = new ActionCheckOutcome(
            today.Count,
            refetched,
            await CountAsync(connection) - before,
            actions.Requests,
            suspect);

        await AppendAsync(connection, runId, observed, outcome);

        return outcome;
    }

    static async Task<HashSet<string>> MembersAsync(SqliteConnection connection, string indexCode)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);

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
        command.Parameters.AddWithValue("$session_date", bar.SessionDate.ToString("yyyy-MM-dd"));

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
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", outcome.Suspect.Count == 0 ? "ok" : "partial");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsReplaced);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue(
            "$detail",
            outcome.Suspect.Count == 0
                ? $"{outcome.Actions} action(s), {outcome.Refetched} refetched"
                : $"{outcome.Actions} action(s), {outcome.Refetched} refetched, suspect: " +
                    string.Join(", ", outcome.Suspect));

        await command.ExecuteNonQueryAsync();
    }
}
