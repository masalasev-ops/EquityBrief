using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Calendar;

public sealed record CalendarOutcome(
    int EventsReturned,
    int RowsWritten,
    int NotMembers,
    int RowsDropped,
    int Requests,
    DateOnly From,
    DateOnly To,
    int NoLongerFiled = 0,
    int ExDividends = 0,
    string? DividendsFault = null);

// The calendar fetcher. One request for the whole index's dated events over the
// window, stored for current members only.
//
// The earnings date is needed nightly by the ladder builder and the shortlist
// builder, and the only other component that could fetch it runs on demand in
// phase 6, so it is on the nightly path and it is one request whatever the
// universe size.
// see: A calendar event is fetched once for the whole index, and the calendar holds provider events only
// see: The nightly run is arithmetic only
//
// It stores what the provider files. A dated item a research pass found never
// reaches this table.
public sealed class CalendarFetcher : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.EarningsCalendar, Feed.DividendCalendar]);

    public const string Stage = "calendar";

    // The kind a declared ex-dividend date is filed under, beside the earnings.
    public const string ExDividend = "ex-dividend";

    // The sessions after the night whose ex-dividend dates the night asks for, one request a session: a little over a
    // month, the time by which most of a swing rule's trades have ended.
    // see: The night asks the dividend calendar for each of the next 21 sessions, one request a session
    public const int DividendSessions = 21;

    // The kind this endpoint files. The column admits others and the provider
    // supplies one, which is why the value is written rather than assumed by the
    // reader: a second kind arriving later must not read as this one.
    public const string Earnings = "earnings";

    // A quarter ahead. Every name reports once a quarter, so ninety days holds
    // every member's next print, and a window equal to the twenty-session
    // horizon would mean a date arrives already inside it: the earnings-soon
    // condition would fire on the day the provider published the date rather
    // than on the name approaching it.
    public const int WindowDays = 90;

    // And a year behind, which 4.8 added and which the endpoint gives in the
    // same request: it answers with historical and upcoming events over
    // whatever range it is asked for.
    //
    // The earnings rule states the last two prints' one-day moves against the
    // stop distance, so the plan needs the dates those moves happened on. A
    // window that held only what is coming could not state them, and the moves
    // themselves are in the bars, which are kept for a year: a calendar reaching
    // further back than the bars would name a print whose session the store does
    // not hold.
    public const int HistoryDays = 365;

    // Members on tonight's session: joined by it, where the join date is known,
    // and not left by it. The whole window is fetched every night, so a joining
    // name's events arrive on its first night as a member without being stored
    // ahead of it.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string CurrentMembers = @"
        SELECT ticker
        FROM membership
        WHERE " + IndexScope.Condition + @"
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session);
    ";

    const string Upsert = @"
        INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at)
        VALUES ($ticker, $event_date, $kind, $timing, $detail, $observed_at)
        ON CONFLICT (ticker, event_date, kind) DO UPDATE SET
            timing = excluded.timing,
            detail = excluded.detail,
            observed_at = excluded.observed_at;
    ";

    // Rows that have fallen behind the window the fetcher asks for, which is now
    // a year rather than the session itself. A print older than the stored bars
    // is a date whose session the store cannot show, so it goes with them.
    const string DropBefore = @"
        DELETE FROM calendar WHERE event_date < $from;
    ";

    // Rows inside the window tonight's answer does not carry: a date the provider
    // has moved, whose old date the upsert cannot reach because the date is part of
    // the key, or a print it no longer files. Each is removed in the write that
    // stores the answer, so the window holds what the provider files tonight rather
    // than everything it ever filed. Every row the answer carries was stamped with
    // tonight's instant by the upsert, which is how the two are told apart.
    // see: The calendar holds each member's own listing's prints, and each night's answer replaces what its window held
    const string DropUnfiled = @"
        DELETE FROM calendar
        WHERE kind = $kind
          AND event_date >= $from
          AND event_date <= $to
          AND observed_at <> $observed_at;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly IEarningsCalendarFeed feed;
    readonly IDividendCalendarFeed dividends;
    readonly IClock clock;
    readonly string databaseFile;

    public CalendarFetcher(IEarningsCalendarFeed feed, IClock clock, string databaseFile, IDividendCalendarFeed? dividends = null)
    {
        this.feed = feed;
        this.dividends = dividends ?? RecordedDividendCalendarFeed.None;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The sessions after the night the dividend calendar is asked for, read off the exchange's calendar.
    public static IReadOnlyList<DateOnly> DividendWindow(DateOnly night)
    {
        var sessions = new List<DateOnly>();

        for (var day = night.AddDays(1); sessions.Count < DividendSessions; day = day.AddDays(1))
        {
            if (EquityBrief.Core.Bars.ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return sessions;
    }

    // Each member's declared ex-dividend dates over the window, one request a session, stored under a kind of their own
    // and replacing what the window held where the answers stored any member's date. A calendar that refuses or answers
    // in a form that cannot be read stores nothing and is named, and the earnings already stored stand.
    // see: The night asks the dividend calendar for each of the next 21 sessions, one request a session
    async Task<(int Stored, string? Fault)> ExDividendsAsync(SqliteConnection connection, DateOnly session, HashSet<string> members, string observedAt, CancellationToken cancellation)
    {
        var window = DividendWindow(session);
        var found = new List<ExDividend>();

        try
        {
            foreach (var day in window)
            {
                found.AddRange(await dividends.ExDividendsAsync(day, cancellation).ConfigureAwait(false));
            }
        }
        catch (Exception failure) when (failure is ProviderRefusal or FormatException or JsonException)
        {
            return (0, failure.Message);
        }

        var stored = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var row in found.Where(row => members.Contains(row.Ticker)).DistinctBy(row => (row.Ticker, row.Date)))
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = Upsert;
            command.Parameters.AddWithValue("$ticker", row.Ticker);
            command.Parameters.AddWithValue("$event_date", row.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$kind", ExDividend);
            command.Parameters.AddWithValue("$timing", Filed(EventTiming.Unstated));
            command.Parameters.AddWithValue("$detail", "{}");
            command.Parameters.AddWithValue("$observed_at", observedAt);

            await command.ExecuteNonQueryAsync(cancellation);

            stored++;
        }

        if (stored > 0)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = DropUnfiled;
            command.Parameters.AddWithValue("$kind", ExDividend);
            command.Parameters.AddWithValue("$from", window[0].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$to", window[^1].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$observed_at", observedAt);

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return (stored, null);
    }

    // The wider indices are the S&P 400 and 600 the night reads beside its own, whose members' events the one window
    // request already carries.
    public async Task<CalendarOutcome> RunAsync(
        string indexCode,
        DateOnly session,
        string runId,
        CancellationToken cancellation = default,
        IReadOnlyList<string>? wider = null)
    {
        var startedAt = clock.UtcNow;
        var from = session.AddDays(-HistoryDays);
        var to = session.AddDays(WindowDays);

        var events = await feed.EventsAsync(from, to, cancellation).ConfigureAwait(false);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, session, wider, cancellation);

        var written = 0;
        var notMembers = 0;
        var unfiled = 0;
        var observedAt = startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await using (var transaction = await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var entry in events)
            {
                // A name the index does not hold is not stored. The payload is
                // the whole market, so most of it is not this universe, and a
                // departed constituent is in the store's membership and not in
                // the index tonight.
                if (!members.Contains(entry.Ticker))
                {
                    notMembers++;

                    continue;
                }

                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Upsert;
                command.Parameters.AddWithValue("$ticker", entry.Ticker);
                command.Parameters.AddWithValue("$event_date", entry.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$kind", Earnings);
                command.Parameters.AddWithValue("$timing", Filed(entry.Timing));
                command.Parameters.AddWithValue("$detail", Serialised(entry));
                command.Parameters.AddWithValue("$observed_at", observedAt);

                await command.ExecuteNonQueryAsync(cancellation);

                written++;
            }

            // An answer that stored no member's print removes nothing, because a
            // provider answering nothing for the index is not one saying that
            // nothing is scheduled.
            if (written > 0)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = DropUnfiled;
                command.Parameters.AddWithValue("$kind", Earnings);
                command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$observed_at", observedAt);

                unfiled = await command.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        var (exDividends, dividendsFault) = await ExDividendsAsync(connection, session, members, observedAt, cancellation);
        var dropped = await DroppedAsync(connection, from, cancellation);

        var outcome = new CalendarOutcome(events.Count, written, notMembers, dropped, feed.Requests + dividends.Requests, from, to, unfiled, exDividends, dividendsFault);

        await RecordAsync(connection, runId, startedAt, outcome, cancellation);

        return outcome;
    }

    // The stored form of the timing, lower case and one word, so a query reads
    // as the column's own note does.
    public static string Filed(EventTiming timing) => timing switch
    {
        EventTiming.Before => "before",
        EventTiming.After => "after",
        _ => "unstated",
    };

    // What the provider carries about the event beyond its date. The estimate,
    // the actual and the provider's surprise are kept as they were sent, as
    // strings, because a double here would round a figure the report quotes.
    static string Serialised(CalendarEvent entry) =>
        JsonSerializer.Serialize(new
        {
            periodEnd = entry.PeriodEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            estimate = entry.Estimate,
            actual = entry.Actual,
            surprise = entry.Surprise,
        });

    static async Task<HashSet<string>> MembersAsync(
        SqliteConnection connection,
        string indexCode,
        DateOnly session,
        IReadOnlyList<string>? wider,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        IndexScope.Bind(command, indexCode, wider);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<int> DroppedAsync(
        SqliteConnection connection,
        DateOnly from,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = DropBefore;
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        CalendarOutcome outcome,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);

        command.Parameters.AddWithValue(
            "$detail",
            FormattableString.Invariant($"{outcome.EventsReturned} event(s) over {outcome.From:yyyy-MM-dd} to {outcome.To:yyyy-MM-dd}, ") +
            $"{outcome.RowsWritten} stored, {outcome.NotMembers} for names the index does not hold, " +
            $"{outcome.NoLongerFiled} no longer filed, {outcome.RowsDropped} dropped, {outcome.Requests} request(s)" +
            (dividends.Requests == 0 && outcome.DividendsFault is null
                ? string.Empty
                : outcome.DividendsFault is { } fault
                    ? $"; the dividend calendar not read: {fault}"
                    : $"; {outcome.ExDividends} ex-dividend date(s) over the next {DividendSessions} sessions"));

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
