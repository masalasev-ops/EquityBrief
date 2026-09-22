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
    IReadOnlyList<string>? Retried = null,
    // Names left suspect with their nightly retries spent and not due their weekly one,
    // which this night did not ask for and names all the same.
    IReadOnlyList<SpentName>? Spent = null);

// A suspect name whose nightly retries are spent: when it was last asked for, as the UTC
// instant its row carries, and the reason that refetch failed for.
public sealed record SpentName(string Ticker, string LastAskedAt, string Reason);

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

    // How many nights after the one that marked it a suspect name is asked for again.
    //
    // Until the 6.0 ruling nothing bounded them, so a failure that lasted made one
    // per-name request on the nightly path every night it lasted. A refetch already
    // carries the retry a transient fault needs inside a night, so a failure that
    // survives it on six nights running is not one the next night clears, and a week of
    // sessions is what an outage would have to outlast. A night an action lands on the
    // name starts the count again, since the action is a new reason to ask, so one action
    // costs at most one request more than this however long its failure lasts.
    //
    // What a spent name is left as is the other half of the rule: still suspect with its
    // reason, named with it on every night it stays so, which puts the stage on the run
    // page's failed region rather than stopping in silence, and asked for again weekly.
    // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
    // see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move
    public const int RetryNights = 5;

    // How many calendar days after the session a spent name was last asked for it is
    // asked for again, from the operator's ruling at 7.0.
    //
    // Until then a spent name waited for another action to land on it, which for a split
    // on a name paying no dividend may never come, so a failure the provider had since
    // cleared stayed in the store until someone noticed. Asked for once a week, it clears
    // without anyone acting once the provider serves the name again, and a failure that
    // lasts costs one request a week for as long as the name is in the index. Counted
    // from the session rather than from the instant, so a night run by hand later in the
    // evening does not move the week, and on or after the seventh day, so a week whose
    // seventh day the exchange did not trade is asked on the next session it did.
    // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
    public const int WeeklyRetryDays = 7;

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
    // night stores, with how many nights each has been asked for again. Each
    // with retries left is refetched again tonight whether or not an action
    // landed on it today, because the action that made it suspect is still in
    // its stored history unadjusted. A refetch is the per-name request the cost
    // rule carves out, and this adds one per suspect name with retries left and
    // one a week per name whose retries are spent, which grows with the failures
    // and never with the index, and ends for a name when it leaves the index. The
    // row's reason and instant are read for the ones whose retries are spent,
    // which the stage names on the nights it does not ask for them.
    // see: Adjusted history is re-fetched after a corporate action
    // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
    const string SuspectNames = @"
        SELECT s.ticker, s.retries, s.reason, s.checked_at FROM series_state s
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
        INSERT INTO series_state (ticker, state, reason, checked_at, retries)
        VALUES ($ticker, $state, $reason, $checked_at, $retries)
        ON CONFLICT (ticker) DO UPDATE SET
            state = excluded.state,
            reason = excluded.reason,
            checked_at = excluded.checked_at,
            retries = excluded.retries;
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

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
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
        var acted = today
            .Where(action => members.Contains(action.Ticker))
            .Select(action => action.Ticker)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // And every name an earlier night left suspect, asked for again nightly
        // while its retries last and weekly after that, until one refetch
        // succeeds. A name whose retries are spent is asked for on a night its
        // week has come round or an action lands on it, and named on every other
        // night it stays suspect.
        var suspects = await SuspectAsync(connection, indexCode, session);

        // A night run again for its session is that night: a name last asked for on
        // tonight's own session was due tonight, so it is asked again as that night
        // asked it, and its count already holds the session.
        bool AskedTonight(SuspectRow name) => LastAskedSession(name) == session;

        bool Due(SuspectRow name) =>
            AskedTonight(name)
            || name.Retries < RetryNights
            || session >= LastAskedSession(name).AddDays(WeeklyRetryDays);

        var askedTonight = suspects
            .Where(AskedTonight)
            .Select(name => name.Ticker)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var retried = suspects
            .Where(Due)
            .Select(name => name.Ticker)
            .ToArray();

        var spent = suspects
            .Where(name => !Due(name) && !acted.Contains(name.Ticker))
            .Select(name => new SpentName(name.Ticker, name.CheckedAt, name.Reason))
            .ToArray();

        var counted = suspects.ToDictionary(name => name.Ticker, name => name.Retries, StringComparer.OrdinalIgnoreCase);

        var affected = acted
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

                await MarkAsync(connection, ticker, Ok, null, observed, 0);
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

                // A night an action lands on the name starts its count again, a
                // session the count already holds adds nothing, and any other night
                // it is asked for counts one more. The count is of sessions, not runs.
                var retries = acted.Contains(ticker)
                    ? 0
                    : counted.GetValueOrDefault(ticker) + (askedTonight.Contains(ticker) ? 0 : 1);

                await MarkAsync(connection, ticker, Suspect, failure.Message, observed, retries);
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
            retried,
            spent);

        await AppendAsync(connection, runId, observed, outcome);

        return outcome;
    }

    sealed record SuspectRow(string Ticker, int Retries, string Reason, string CheckedAt);

    // The session a suspect name was last asked for on, read off the instant its row
    // carries in the form this check writes it, through the clock the night runs on.
    DateOnly LastAskedSession(SuspectRow name) =>
        clock.SessionDateAt(DateTimeOffset.ParseExact(
            name.CheckedAt,
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

    static async Task<IReadOnlyList<SuspectRow>> SuspectAsync(SqliteConnection connection, string indexCode, DateOnly session)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SuspectNames;
        command.Parameters.AddWithValue("$suspect", Suspect);
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var names = new List<SuspectRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(new SuspectRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                reader.GetString(3)));
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
        string observed,
        int retries)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MarkState;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$reason", (object?)reason ?? DBNull.Value);
        command.Parameters.AddWithValue("$checked_at", observed);
        command.Parameters.AddWithValue("$retries", retries);

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
        var spent = outcome.Spent ?? [];

        // Partial while a member's series is suspect and was not refetched tonight,
        // whether its refetch failed tonight or its retries are spent and its week
        // has not come round, so the run page's failed region names a spent name on
        // every night it stays suspect rather than only on the nights it was asked for.
        command.Parameters.AddWithValue("$outcome", outcome.Suspect.Count == 0 && spent.Count == 0 ? "ok" : "partial");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsReplaced);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue(
            "$detail",
            $"{outcome.Actions} action(s), {outcome.Refetched} refetched, " +
            $"{outcome.RefetchRequests} of the {outcome.Requests} request(s) per name, " +
            $"{(outcome.Retried ?? []).Count} retried from an earlier night" +
            ((outcome.Retried ?? []).Count == 0 ? string.Empty : ": " + string.Join(", ", outcome.Retried!)) +
            (outcome.Suspect.Count == 0 ? string.Empty : ", suspect: " + string.Join(", ", outcome.Suspect)) +
            (spent.Count == 0
                ? string.Empty
                : $", {spent.Count} left suspect with {RetryNights} nightly retries spent, each asked for again {WeeklyRetryDays} days after the session it was last asked for: " +
                  string.Join("; ", spent.Select(name => $"{name.Ticker}, last asked for at {name.LastAskedAt}, because {name.Reason}"))));

        await command.ExecuteNonQueryAsync();
    }
}
