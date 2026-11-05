using System.Globalization;
using EquityBrief.Core.Bars;
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
    IReadOnlyList<SpentName>? Spent = null,
    // Members back after sessions in no index the night reads, whose years this night
    // asked for whole.
    IReadOnlyList<string>? Returning = null,
    // Members whose stored year misses a session the exchange traded, other than those
    // an action, a retry or a return asked for, whose years this night asked for whole.
    IReadOnlyList<string>? Missing = null,
    // The dividends the night's answer carried for the names the night stores, whole, which the dividend keeper keeps.
    // see: Each dividend a member paid is kept from the night's bulk answer and from one history run, and read as the provider restated it on the day it was read
    IReadOnlyList<DividendPaid>? Paid = null,
    // Members an action the provider posted after its own night landed on, read from the day files of the sessions before
    // this one, and how many of those files could not be read.
    IReadOnlyList<string>? Late = null,
    int LateFilesUnread = 0,
    // Members whose newest stored bar is behind the night, and members holding a copied candle.
    IReadOnlyList<string>? Behind = null,
    IReadOnlyList<string>? Copied = null,
    // The dividends an answer has not yet carried, which keep the row partial so the run page names them.
    IReadOnlyList<string>? Waiting = null);

// A suspect name whose nightly retries are spent: when it was last asked for, as the UTC
// instant its row carries, and the reason that refetch failed for.
public sealed record SpentName(string Ticker, string LastAskedAt, string Reason);

// The corporate action check. One bulk request per kind per night, and a
// full-year refetch of any current member whose adjusted prices an action has
// moved, that returns after sessions in no index the night reads, or whose
// stored year misses a session the exchange traded.
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
            new StoreTouch(Store.SplitEvent, Touch.Insert),
            new StoreTouch(Store.DividendWait, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
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
    // see: A feed is tried three times with a doubling backoff and the night's news query waits ninety seconds a try, and the night has a two-hour deadline it cannot move
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

    // How many sessions before the night's the check reads the day files of again, ten requests a night whatever the
    // universe, since the provider posts some actions after the night has asked.
    // see: The corporate action check reads the day files of the five sessions before its own, and refetches a member an action the provider posted late landed on
    public const int LateFileSessions = 5;

    // The most members a night asks for because they are behind or hold a copied candle, so the requests follow the
    // provider's faults and a day file missing the whole index does not ask for every member; those past it are marked
    // suspect with the reason and asked on a later night.
    // see: A member holding a copied candle or behind the night is asked for its year again, and stays suspect until an answer clears it
    public const int FaultAskLimit = 20;

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
        WHERE " + IndexScope.Condition + @" AND (""left"" IS NULL OR ""left"" > $session);
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
              WHERE m." + IndexScope.Condition + @" AND m.ticker = s.ticker
                AND (m.""left"" IS NULL OR m.""left"" > $session));
    ";

    // The members back after sessions in no index the night reads: each holding a span
    // that closed by tonight, with the latest such close and the instant this check last
    // asked for its year, if it ever did. The night stored no bar for a name on the
    // sessions it was in no index, and saw no action on it either, so its stored year
    // stops where it left and may sit on an adjustment the provider has since moved.
    // A name moving between indices on one night closes one span and opens another on
    // the same session, so the sessions between them are none.
    // see: A member that returns after sessions in no index the night reads has its year asked for again whole
    const string ClosedSpans = @"
        SELECT m.ticker, MAX(c.""left""), s.checked_at FROM membership m
        JOIN membership c ON c.ticker = m.ticker AND c.""left"" IS NOT NULL AND c.""left"" <= $session
        LEFT JOIN series_state s ON s.ticker = m.ticker
        WHERE m." + IndexScope.Condition + @" AND (m.""left"" IS NULL OR m.""left"" > $session)
        GROUP BY m.ticker;
    ";

    // The sessions a name holds from a day up to tonight's, which is what the
    // sessions it was in no index are read against.
    const string HeldSince = @"
        SELECT session_date FROM bar
        WHERE ticker = $ticker AND session_date >= $from AND session_date < $session;
    ";

    // A name holding no bar at all is the backfill's, which asks for its year.
    const string FirstHeld = "SELECT MIN(session_date) FROM bar WHERE ticker = $ticker;";

    // The newest session a name holds to tonight's, which the fetch has stored before this
    // check runs and which a refetch's year has to reach before it replaces the year.
    const string NewestHeld = @"
        SELECT MAX(session_date) FROM bar
        WHERE ticker = $ticker AND session_date <= $session;
    ";

    // Every name's first stored bar, every session each holds from a day to tonight's, which
    // the fetch has stored before this check reads, and the instant this check last asked for
    // each, which the members missing a session are read from in three reads whatever the
    // universe.
    const string FirstHeldEach = "SELECT ticker, MIN(session_date) FROM bar GROUP BY ticker;";

    const string HeldEach = @"
        SELECT ticker, session_date FROM bar
        WHERE session_date >= $from AND session_date <= $session;
    ";

    const string CheckedEach = "SELECT ticker, checked_at FROM series_state;";

    // Each stored bar with no volume with the close of the session before it, which a copied candle is read from.
    const string NoVolumeEach = @"
        SELECT b.ticker, b.session_date, b.open, b.high, b.low, b.close, (
            SELECT p.close FROM bar p
            WHERE p.ticker = b.ticker AND p.session_date < b.session_date
            ORDER BY p.session_date DESC LIMIT 1)
        FROM bar b
        WHERE b.volume = 0;
    ";

    const string NewestEach = "SELECT ticker, MAX(session_date) FROM bar WHERE session_date <= $session GROUP BY ticker;";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string StoredYear = "SELECT session_date, close, volume FROM bar WHERE ticker = $ticker;";

    const string WaitsLeft = @"
        DELETE FROM dividend_wait
        WHERE NOT EXISTS (
            SELECT 1 FROM membership m
            WHERE m." + IndexScope.Condition + @" AND m.ticker = dividend_wait.ticker
              AND (m.""left"" IS NULL OR m.""left"" > $session));
    ";

    const string WaitsOpen = "SELECT ticker, ex_date, amount, asked_on, retries FROM dividend_wait ORDER BY ticker, ex_date;";

    const string WaitsOfName = "SELECT ex_date, amount FROM dividend_wait WHERE ticker = $ticker;";

    const string WaitAsked = @"
        INSERT INTO dividend_wait (ticker, ex_date, amount, asked_on, retries, run_id)
        VALUES ($ticker, $ex_date, $amount, $asked_on, 0, $run_id)
        ON CONFLICT (ticker, ex_date) DO UPDATE SET
            retries = retries + CASE WHEN asked_on = excluded.asked_on THEN 0 ELSE 1 END,
            asked_on = excluded.asked_on,
            run_id = excluded.run_id;
    ";

    const string WaitOver = "DELETE FROM dividend_wait WHERE ticker = $ticker AND ex_date = $ex_date;";

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

    // The wider indices are the S&P 400 and 600 the night reads beside its own, whose members' stored years an action
    // lands on as the index's own do.
    public async Task<ActionCheckOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? wider = null)
    {
        var started = clock.UtcNow;
        var session = clock.SessionDateAt(started);
        var observed = started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();

        var members = await MembersAsync(connection, indexCode, session, wider);
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

        // And every member an action of the sessions before landed on that this check has not asked for since that
        // session, which is an action the provider posted after its own night read the file.
        // see: The corporate action check reads the day files of the five sessions before its own, and refetches a member an action the provider posted late landed on
        var (lateActions, late, lateUnread) = await LateActionsAsync(connection, session, members, cancellationToken);

        acted.UnionWith(late);

        await KeepSplitsAsync(connection, today.Concat(lateActions).Where(action => members.Contains(action.Ticker)), started, runId, cancellationToken);

        // And every name an earlier night left suspect, asked for again nightly
        // while its retries last and weekly after that, until one refetch
        // succeeds. A name whose retries are spent is asked for on a night its
        // week has come round or an action lands on it, and named on every other
        // night it stays suspect.
        var suspects = await SuspectAsync(connection, indexCode, session, wider);

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

        var counted = suspects.ToDictionary(name => name.Ticker, name => name.Retries, StringComparer.OrdinalIgnoreCase);

        // And every name holding a dividend an earlier answer did not carry, on each of the five nights after and weekly, ending
        // when an answer carries it or the name leaves the index. Its state stays ok, since the rules go on reading its prices.
        // see: A dividend the provider's answer does not yet carry is asked for again on the five nights after and weekly, and the stock's pages and the run page say so while it waits
        var waitsDue = (await WaitsAsync(connection, indexCode, session, wider))
            .Where(wait => wait.AskedOn == session || wait.Retries < RetryNights || session >= wait.AskedOn.AddDays(WeeklyRetryDays))
            .Select(wait => wait.Ticker)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // And every member back after sessions in no index the night reads, on its first
        // night back. A name this check has asked for since its span closed is not asked
        // again, so a stretch the provider cannot serve costs one request and then stands
        // as a gap, named as any gap is.
        // see: A member that returns after sessions in no index the night reads has its year asked for again whole
        var returning = await ReturningAsync(connection, indexCode, session, wider);

        // And every other member whose stored year misses a session the exchange traded,
        // once for the newest session missing, so a session the price file did not carry
        // costs one request and what the provider cannot serve then stands as a gap.
        // see: A member missing a session the exchange traded has its year asked for again whole, once for the newest session missing
        var missing = await MissingAsync(
            connection,
            members,
            session,
            acted.Concat(retried).Concat(returning).Concat(waitsDue).ToHashSet(StringComparer.OrdinalIgnoreCase));

        // A new reason to ask starts a name's count again as an action does, so a spent
        // name asked for tonight is not named as spent.
        var fresh = acted.Concat(returning).Concat(missing).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // And every other member holding a copied candle or behind the night, a suspect name left to its own count so that
        // a fault the provider does not clear is asked for on the five nights after and weekly, up to the limit a night.
        // see: A member holding a copied candle or behind the night is asked for its year again, and stays suspect until an answer clears it
        var (behindAll, copiedAll, reach) = await FaultsAsync(connection, members, session);
        var suspectNames = suspects.Select(name => name.Ticker).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var asking = fresh.Concat(retried).Concat(waitsDue).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var faultAsks = copiedAll.Keys
            .Concat(behindAll.Keys)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(name => !asking.Contains(name) && !suspectNames.Contains(name))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
        var faultsAsked = faultAsks.Take(FaultAskLimit).ToArray();
        var faultsLeft = faultAsks.Skip(FaultAskLimit).ToArray();

        var spent = suspects
            .Where(name => !Due(name) && !fresh.Contains(name.Ticker))
            .Select(name => new SpentName(name.Ticker, name.CheckedAt, name.Reason))
            .ToArray();

        var affected = acted
            .Concat(retried)
            .Concat(returning)
            .Concat(missing)
            .Concat(faultsAsked)
            .Concat(waitsDue)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(ticker => ticker, StringComparer.Ordinal)
            .ToArray();

        var paid = today.Concat(lateActions)
            .Where(action => action.Paid is not null)
            .GroupBy(action => action.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(action => action.Paid!).ToArray(), StringComparer.OrdinalIgnoreCase);

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
                var year = await ReplaceYearAsync(connection, ticker, session, observed, cancellationToken);

                await SettleDividendsAsync(connection, ticker, year, paid.GetValueOrDefault(ticker) ?? [], session, runId, cancellationToken);

                // The year is the provider's whole answer, so where it still ends short of the night or still holds a
                // copied candle the name stays suspect with why, on the count a failure holds.
                if ((behindAll.ContainsKey(ticker) || copiedAll.ContainsKey(ticker)) && StillFaulty(year, reach ?? session) is { } fault)
                {
                    await MarkAsync(connection, ticker, Suspect, fault, observed, counted.GetValueOrDefault(ticker) + (askedTonight.Contains(ticker) ? 0 : 1));
                    await transaction.CommitAsync();

                    refetched++;
                    suspect.Add(ticker);

                    continue;
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

                // A night an action lands on the name, it returns or a session goes missing
                // starts its count again, a session the count already holds adds nothing, and
                // any other night it is asked for counts one more. The count is of sessions,
                // not runs.
                var retries = fresh.Contains(ticker)
                    ? 0
                    : counted.GetValueOrDefault(ticker) + (askedTonight.Contains(ticker) ? 0 : 1);

                await MarkAsync(connection, ticker, Suspect, failure.Message, observed, retries);
                suspect.Add(ticker);
            }
        }

        foreach (var ticker in faultsLeft)
        {
            await MarkAsync(
                connection,
                ticker,
                Suspect,
                SuspectReason.PastTheLimit(FaultAskLimit, behindAll.TryGetValue(ticker, out var newest) ? SuspectReason.Behind(newest) : SuspectReason.Copied(copiedAll[ticker])),
                observed,
                0);
            suspect.Add(ticker);
        }

        var refetchRequests = history.Requests - refetchRequestsBefore;
        var waiting = await WaitsAsync(connection, indexCode, session, wider);

        var outcome = new ActionCheckOutcome(
            today.Count,
            refetched,
            await CountAsync(connection) - before,
            actions.Requests - actionRequestsBefore + refetchRequests,
            suspect,
            refetchRequests,
            retried,
            spent,
            returning,
            missing,
            [.. today.Concat(lateActions).Where(action => action.Paid is not null && members.Contains(action.Ticker)).Select(action => action.Paid!)],
            late,
            lateUnread,
            [.. behindAll.Keys.OrderBy(name => name, StringComparer.Ordinal)],
            [.. copiedAll.Keys.OrderBy(name => name, StringComparer.Ordinal)],
            [.. waiting.Select(wait => FormattableString.Invariant($"{wait.Ticker} {Text(wait.ExDate)}, last asked for on {Text(wait.AskedOn)}"))]);

        await AppendAsync(connection, runId, observed, outcome);

        return outcome;
    }

    // A year asked for by hand for each name given, as a refetch after an action is, each in its own transaction: a name
    // whose answer cannot replace its year keeps its year and is marked suspect. One request a name, never from a night.
    // see: Adjusted history is re-fetched after a corporate action
    public async Task<ActionCheckOutcome> RefetchNamesAsync(IReadOnlyList<string> names, string runId, CancellationToken cancellationToken = default)
    {
        var started = clock.UtcNow;
        var session = clock.SessionDateAt(started);
        var observed = started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellationToken);

        var before = await CountAsync(connection);
        var requestsBefore = history.Requests;
        var suspect = new List<string>();
        var refetched = 0;

        await using var newestStored = connection.CreateCommand();
        newestStored.CommandText = NewestSession;

        var reach = await newestStored.ExecuteScalarAsync(cancellationToken) is string stored
            ? DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : session;

        foreach (var ticker in names)
        {
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                var year = await ReplaceYearAsync(connection, ticker, session, observed, cancellationToken);

                await SettleDividendsAsync(connection, ticker, year, [], session, runId, cancellationToken);

                // By hand the day's own session may not have closed, so the year is held to the newest session the store holds.
                if (StillFaulty(year, reach) is { } fault)
                {
                    await MarkAsync(connection, ticker, Suspect, fault, observed, 0);
                    suspect.Add(ticker);
                }
                else
                {
                    await MarkAsync(connection, ticker, Ok, null, observed, 0);
                }

                await transaction.CommitAsync(cancellationToken);

                refetched++;
            }
            catch (Exception failure) when (!(failure is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                await transaction.RollbackAsync(cancellationToken);
                await MarkAsync(connection, ticker, Suspect, failure.Message, observed, 0);
                suspect.Add(ticker);
            }
        }

        var requests = history.Requests - requestsBefore;
        var outcome = new ActionCheckOutcome(0, refetched, await CountAsync(connection) - before, requests, suspect, requests);

        await AppendAsync(connection, runId, observed, outcome);

        return outcome;
    }

    // Replaces the name's whole year inside the caller's transaction with the provider's answer, refused where that would
    // empty it or drop a session the store holds.
    async Task<IReadOnlyList<ProviderBar>> ReplaceYearAsync(SqliteConnection connection, string ticker, DateOnly session, string observed, CancellationToken cancellationToken)
    {
        var year = await history.BarsAsync(ticker, session.AddYears(-1), session, cancellationToken);

        if (year.Count == 0)
        {
            throw new InvalidOperationException(
                "the refetch returned no bars, so replacing the year would empty it");
        }

        // A year the provider has not yet brought up to the sessions the night stored is
        // refused as an empty one is, the stored year left as it was and the name asked
        // for again on the nights after, rather than replaced by a shorter one that would
        // leave the name stale and the figures stored over those sessions ahead of its bars.
        // see: A member missing a session the exchange traded has its year asked for again whole, once for the newest session missing
        await using (var newest = connection.CreateCommand())
        {
            newest.CommandText = NewestHeld;
            newest.Parameters.AddWithValue("$ticker", ticker);
            newest.Parameters.AddWithValue("$session", Text(session));

            var last = year.Max(bar => bar.SessionDate);

            if (await newest.ExecuteScalarAsync() is string held
                && DateOnly.ParseExact(held, "yyyy-MM-dd", CultureInfo.InvariantCulture) > last)
            {
                throw new InvalidOperationException(
                    $"the refetch's year ends on {Text(last)} before {held}, the newest session the night stored, " +
                    "so replacing the year would drop sessions the store holds");
            }
        }

        // see: A refetch answer whose day's move the stored closes do not show is refused, and the stock keeps its year and is asked again
        if (RefetchAnswer.Refused(await StoredYearAsync(connection, ticker), [.. year.Select(bar => (bar.SessionDate, bar.Close))]) is { } refused)
        {
            throw new InvalidOperationException(FormattableString.Invariant(
                $"the refetch's answer moves {refused.AnswerMove * 100:+0.0;-0.0;0.0}% from {Text(refused.Before)} to {Text(refused.Session)} where the stored closes move {refused.StoredMove * 100:+0.0;-0.0;0.0}%, so it may be another stock's prices"));
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

        return year;
    }

    sealed record WaitRow(string Ticker, DateOnly ExDate, DateOnly AskedOn, int Retries);

    // The dividends still waiting, a name that has left the index taking its rows with it.
    static async Task<IReadOnlyList<WaitRow>> WaitsAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider)
    {
        await using (var left = connection.CreateCommand())
        {
            left.CommandText = WaitsLeft;
            IndexScope.Bind(left, indexCode, wider);
            left.Parameters.AddWithValue("$session", Text(session));

            await left.ExecuteNonQueryAsync();
        }

        var waits = new List<WaitRow>();

        await using var command = connection.CreateCommand();
        command.CommandText = WaitsOpen;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            waits.Add(new WaitRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(reader.GetString(3), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(4)));
        }

        return waits;
    }

    // Each dividend the name waits on and each the day files carry for it, read against the answer that just replaced its year:
    // one the answer does not carry waits and counts this night, and one it carries is over. The amount is what was paid, since the
    // answer's raw closes are as they traded.
    static async Task SettleDividendsAsync(SqliteConnection connection, string ticker, IReadOnlyList<ProviderBar> year, IReadOnlyList<DividendPaid> paid, DateOnly session, string runId, CancellationToken cancellationToken)
    {
        var dividends = new Dictionary<DateOnly, decimal>();

        foreach (var dividend in paid)
        {
            dividends[dividend.ExDate] = dividend.Unadjusted ?? dividend.Amount;
        }

        await using (var open = connection.CreateCommand())
        {
            open.CommandText = WaitsOfName;
            open.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await open.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                dividends[DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture)] = Money.FromStorage(reader.GetString(1));
            }
        }

        (DateOnly Session, decimal Close, decimal RawClose)[] closes = [.. year.Select(bar => (bar.SessionDate, bar.Close, bar.RawClose))];

        foreach (var (exDate, amount) in dividends)
        {
            await using var write = connection.CreateCommand();
            write.Parameters.AddWithValue("$ticker", ticker);
            write.Parameters.AddWithValue("$ex_date", Text(exDate));

            if (DividendCarry.NotCarried(DividendCarry.GapAcross(closes, exDate, amount)))
            {
                write.CommandText = WaitAsked;
                Money.Bind(write, "$amount", amount);
                write.Parameters.AddWithValue("$asked_on", Text(session));
                write.Parameters.AddWithValue("$run_id", runId);
            }
            else
            {
                write.CommandText = WaitOver;
            }

            await write.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    static async Task<IReadOnlyDictionary<DateOnly, (decimal Close, long Volume)>> StoredYearAsync(SqliteConnection connection, string ticker)
    {
        var stored = new Dictionary<DateOnly, (decimal Close, long Volume)>();

        await using var command = connection.CreateCommand();
        command.CommandText = StoredYear;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            stored[DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture)] = (Money.FromStorage(reader.GetString(1)), reader.GetInt64(2));
        }

        return stored;
    }

    // Why a replaced year is still not right, none where it reaches the night and holds no copied candle.
    static string? StillFaulty(IReadOnlyList<ProviderBar> year, DateOnly session)
    {
        var ordered = year.OrderBy(bar => bar.SessionDate).ToArray();
        var newest = ordered[^1].SessionDate;

        if (ExchangeClosures.SessionsBetween(newest, session.AddDays(1)).Count > 0)
        {
            return SuspectReason.Behind(newest);
        }

        var copied = Enumerable.Range(1, ordered.Length - 1)
            .Where(at => CopiedCandle.Is(ordered[at].Volume, ordered[at].Open, ordered[at].High, ordered[at].Low, ordered[at].Close, ordered[at - 1].Close))
            .Select(at => ordered[at].SessionDate)
            .ToArray();

        return copied.Length > 0 ? SuspectReason.Copied(copied) : null;
    }

    // The members behind the night, each with its newest stored bar, and the members holding a copied candle in the year,
    // each with the sessions it holds one on.
    //
    // Behind is read against the newest session any member holds, which is the night the fetch stored: a night whose file
    // stored nothing leaves no member behind another, and is the fetch's fault to name rather than every member's.
    static async Task<(IReadOnlyDictionary<string, DateOnly> Behind, IReadOnlyDictionary<string, IReadOnlyList<DateOnly>> Copied, DateOnly? Reach)> FaultsAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> members,
        DateOnly session)
    {
        var newestEach = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        var copied = new Dictionary<string, List<DateOnly>>(StringComparer.OrdinalIgnoreCase);
        var held = members.ToHashSet(StringComparer.OrdinalIgnoreCase);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestEach;
            command.Parameters.AddWithValue("$session", Text(session));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (held.Contains(reader.GetString(0)))
                {
                    newestEach[reader.GetString(0)] = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                }
            }
        }

        DateOnly? reach = newestEach.Count > 0 ? newestEach.Values.Max() : null;
        var behind = newestEach
            .Where(pair => ExchangeClosures.SessionsBetween(pair.Value, reach!.Value.AddDays(1)).Count > 0)
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NoVolumeEach;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (held.Contains(reader.GetString(0)) && !reader.IsDBNull(6)
                    && CopiedCandle.Is(0, Money.FromStorage(reader.GetString(2)), Money.FromStorage(reader.GetString(3)), Money.FromStorage(reader.GetString(4)), Money.FromStorage(reader.GetString(5)), Money.FromStorage(reader.GetString(6))))
                {
                    if (!copied.TryGetValue(reader.GetString(0), out var sessions))
                    {
                        copied[reader.GetString(0)] = sessions = [];
                    }

                    sessions.Add(DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture));
                }
            }
        }

        return (behind, copied.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<DateOnly>)pair.Value, StringComparer.OrdinalIgnoreCase), reach);
    }

    // Each split the day files carry for a member, the first kept standing, which the chart check reads to see that the stored
    // bars carry it. A ratio that is not two numbers is left out.
    // see: The chart check reads every member's chart after the bars land and asks nothing of the network, and lists what it finds the morning it appears
    static async Task KeepSplitsAsync(SqliteConnection connection, IEnumerable<CorporateAction> actions, DateTimeOffset started, string runId, CancellationToken cancellationToken)
    {
        foreach (var split in actions.Where(action => action.Kind == ActionKind.Split))
        {
            var parts = split.Value.Split('/');

            if (parts.Length != 2
                || !decimal.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var newShares)
                || !decimal.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var oldShares)
                || newShares <= 0 || oldShares <= 0)
            {
                continue;
            }

            await using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO split_event (ticker, ex_date, new_shares, old_shares, run_id) VALUES ($ticker, $ex_date, $new_shares, $old_shares, $run_id) ON CONFLICT (ticker, ex_date) DO NOTHING;";
            insert.Parameters.AddWithValue("$ticker", split.Ticker);
            insert.Parameters.AddWithValue("$ex_date", Text(split.Date));
            Money.Bind(insert, "$new_shares", newShares);
            Money.Bind(insert, "$old_shares", oldShares);
            insert.Parameters.AddWithValue("$run_id", runId);

            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    // The actions of the sessions before the night's, the members among them this check has not asked for since their
    // session, and how many day files could not be read, which names no member and fails nothing, since tonight's own
    // answer is what the night stands on. A member holding no state row has never been asked.
    async Task<(IReadOnlyList<CorporateAction> Actions, IReadOnlyList<string> Names, int Unread)> LateActionsAsync(
        SqliteConnection connection,
        DateOnly session,
        IReadOnlyCollection<string> members,
        CancellationToken cancellationToken)
    {
        var asked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = CheckedEach;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                asked[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var actionsRead = new List<CorporateAction>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var unread = 0;
        var held = members.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var day in ExchangeClosures.SessionsBetween(session.AddDays(-14), session).TakeLast(LateFileSessions))
        {
            try
            {
                foreach (var action in await actions.ActionsAsync(Exchange, day, cancellationToken))
                {
                    actionsRead.Add(action);

                    if (held.Contains(action.Ticker) && !(asked.TryGetValue(action.Ticker, out var checkedAt) && SessionAsked(checkedAt) >= action.Date))
                    {
                        names.Add(action.Ticker);
                    }
                }
            }
            catch (Exception failure) when (!(failure is OperationCanceledException && cancellationToken.IsCancellationRequested))
            {
                unread++;
            }
        }

        return (actionsRead, [.. names.OrderBy(name => name, StringComparer.Ordinal)], unread);
    }

    sealed record SuspectRow(string Ticker, int Retries, string Reason, string CheckedAt);

    // The session a suspect name was last asked for on, read off the instant its row
    // carries in the form this check writes it, through the clock the night runs on.
    DateOnly LastAskedSession(SuspectRow name) => SessionAsked(name.CheckedAt);

    DateOnly SessionAsked(string checkedAt) =>
        clock.SessionDateAt(DateTimeOffset.ParseExact(
            checkedAt,
            "yyyy-MM-ddTHH:mm:ssZ",
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));

    // The members back after sessions in no index the night reads that this check has not
    // asked for since: each holding a bar, and missing a session the exchange traded from
    // the latest close of its spans to tonight, read from its first stored bar within the
    // year the night keeps.
    async Task<IReadOnlyList<string>> ReturningAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider)
    {
        var closed = new List<(string Ticker, DateOnly Left, string? CheckedAt)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ClosedSpans;
            IndexScope.Bind(command, indexCode, wider);
            command.Parameters.AddWithValue("$session", Text(session));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                closed.Add((
                    reader.GetString(0),
                    DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        var returning = new List<string>();

        foreach (var (ticker, left, checkedAt) in closed.OrderBy(name => name.Ticker, StringComparer.Ordinal))
        {
            if (checkedAt is not null && SessionAsked(checkedAt) >= left)
            {
                continue;
            }

            await using var first = connection.CreateCommand();
            first.CommandText = FirstHeld;
            first.Parameters.AddWithValue("$ticker", ticker);

            if (await first.ExecuteScalarAsync() is not string firstHeld)
            {
                continue;
            }

            var from = new[]
            {
                left,
                session.AddYears(-1),
                ExchangeClosures.CoveredFrom,
                DateOnly.ParseExact(firstHeld, "yyyy-MM-dd", CultureInfo.InvariantCulture),
            }.Max();

            var traded = ExchangeClosures.SessionsBetween(from.AddDays(-1), session);

            if (traded.Count == 0)
            {
                continue;
            }

            await using var held = connection.CreateCommand();
            held.CommandText = HeldSince;
            held.Parameters.AddWithValue("$ticker", ticker);
            held.Parameters.AddWithValue("$from", Text(from));
            held.Parameters.AddWithValue("$session", Text(session));

            var sessions = new HashSet<DateOnly>();
            await using var reader = await held.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                sessions.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
            }

            if (!traded.All(sessions.Contains))
            {
                returning.Add(ticker);
            }
        }

        return returning;
    }

    // The members holding a stored bar whose year misses a session the exchange traded, read
    // between the first stored bar within the year the night keeps and the newest, tonight's
    // included, that this check has not asked for since the newest session missing. Sessions
    // after the newest stored bar are not a gap and are read as the member being behind. A name
    // tonight already asks for is left to the reason asking for it, and a name holding no bar
    // is the backfill's.
    // see: A member missing a session the exchange traded has its year asked for again whole, once for the newest session missing
    async Task<IReadOnlyList<string>> MissingAsync(
        SqliteConnection connection,
        IReadOnlyCollection<string> members,
        DateOnly session,
        IReadOnlySet<string> askedFor)
    {
        var year = new[] { session.AddYears(-1), ExchangeClosures.CoveredFrom }.Max();

        var first = new Dictionary<string, DateOnly>(StringComparer.OrdinalIgnoreCase);
        var held = new Dictionary<string, HashSet<DateOnly>>(StringComparer.OrdinalIgnoreCase);
        var asked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FirstHeldEach;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                first[reader.GetString(0)] = DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = HeldEach;
            command.Parameters.AddWithValue("$from", Text(year));
            command.Parameters.AddWithValue("$session", Text(session));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var ticker = reader.GetString(0);

                if (!held.TryGetValue(ticker, out var sessions))
                {
                    held[ticker] = sessions = [];
                }

                sessions.Add(DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = CheckedEach;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                asked[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var missing = new List<string>();

        foreach (var ticker in members.Where(name => !askedFor.Contains(name)).OrderBy(name => name, StringComparer.Ordinal))
        {
            if (!first.TryGetValue(ticker, out var firstHeld))
            {
                continue;
            }

            if (!held.TryGetValue(ticker, out var sessions))
            {
                continue;
            }

            var from = firstHeld > year ? firstHeld : year;

            var gone = ExchangeClosures.SessionsBetween(from.AddDays(-1), sessions.Max())
                .Where(traded => !sessions.Contains(traded))
                .ToArray();

            if (gone.Length == 0)
            {
                continue;
            }

            if (asked.TryGetValue(ticker, out var checkedAt) && SessionAsked(checkedAt) >= gone[^1])
            {
                continue;
            }

            missing.Add(ticker);
        }

        return missing;
    }

    static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static async Task<IReadOnlyList<SuspectRow>> SuspectAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SuspectNames;
        command.Parameters.AddWithValue("$suspect", Suspect);
        IndexScope.Bind(command, indexCode, wider);
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

    static async Task<HashSet<string>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        IndexScope.Bind(command, indexCode, wider);
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
        command.Parameters.AddWithValue("$outcome", outcome.Suspect.Count == 0 && spent.Count == 0 && (outcome.Waiting ?? []).Count == 0 ? "ok" : "partial");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsReplaced);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue(
            "$detail",
            $"{outcome.Actions} action(s), {outcome.Refetched} refetched, " +
            $"{outcome.RefetchRequests} of the {outcome.Requests} request(s) per name, " +
            $"{(outcome.Retried ?? []).Count} retried from an earlier night" +
            ((outcome.Retried ?? []).Count == 0 ? string.Empty : ": " + string.Join(", ", outcome.Retried!)) +
            ((outcome.Returning ?? []).Count == 0
                ? string.Empty
                : $", {outcome.Returning!.Count} back after sessions in no index the night reads: " + string.Join(", ", outcome.Returning!)) +
            ((outcome.Missing ?? []).Count == 0
                ? string.Empty
                : $", {outcome.Missing!.Count} missing a session the exchange traded: " + string.Join(", ", outcome.Missing!)) +
            ((outcome.Late ?? []).Count == 0
                ? string.Empty
                : $", {outcome.Late!.Count} an action the provider posted late landed on: " + string.Join(", ", outcome.Late!)) +
            (outcome.LateFilesUnread == 0 ? string.Empty : $", {outcome.LateFilesUnread} earlier day file(s) unread") +
            ((outcome.Behind ?? []).Count == 0
                ? string.Empty
                : $", {outcome.Behind!.Count} behind the night: " + string.Join(", ", outcome.Behind!)) +
            ((outcome.Copied ?? []).Count == 0
                ? string.Empty
                : $", {outcome.Copied!.Count} holding a copied candle: " + string.Join(", ", outcome.Copied!)) +
            (outcome.Suspect.Count == 0 ? string.Empty : ", suspect: " + string.Join(", ", outcome.Suspect)) +
            ((outcome.Waiting ?? []).Count == 0
                ? string.Empty
                : $", {outcome.Waiting!.Count} dividend(s) the provider's answer does not yet carry, each asked for on the {RetryNights} nights after and every {WeeklyRetryDays} days after that: " + string.Join("; ", outcome.Waiting!)) +
            (spent.Count == 0
                ? string.Empty
                : $", {spent.Count} left suspect with {RetryNights} nightly retries spent, each asked for again {WeeklyRetryDays} days after the session it was last asked for: " +
                  string.Join("; ", spent.Select(name => $"{name.Ticker}, last asked for at {name.LastAskedAt}, because {name.Reason}"))));

        await command.ExecuteNonQueryAsync();
    }
}
