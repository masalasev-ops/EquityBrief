using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Calendar;

public sealed record CalendarOutcome(
    int EventsReturned,
    int RowsWritten,
    int NotMembers,
    int RowsDropped,
    int Requests,
    DateOnly From,
    DateOnly To);

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
        Feeds: [Feed.EarningsCalendar]);

    public const string Stage = "calendar";

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
        WHERE index_code = $index
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

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly IEarningsCalendarFeed feed;
    readonly IClock clock;
    readonly string databaseFile;

    public CalendarFetcher(IEarningsCalendarFeed feed, IClock clock, string databaseFile)
    {
        this.feed = feed;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<CalendarOutcome> RunAsync(
        string indexCode,
        DateOnly session,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var from = session.AddDays(-HistoryDays);
        var to = session.AddDays(WindowDays);

        var events = await feed.EventsAsync(from, to, cancellation).ConfigureAwait(false);

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, session, cancellation);

        var written = 0;
        var notMembers = 0;

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
                command.Parameters.AddWithValue("$observed_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

                await command.ExecuteNonQueryAsync(cancellation);

                written++;
            }

            await transaction.CommitAsync(cancellation);
        }

        var dropped = await DroppedAsync(connection, from, cancellation);

        var outcome = new CalendarOutcome(events.Count, written, notMembers, dropped, feed.Requests, from, to);

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

    // What the provider carries about the event beyond its date. The estimate
    // and the actual are kept as they were sent, as strings, because nothing
    // computes with them and a double here would round a figure the report
    // quotes.
    static string Serialised(CalendarEvent entry) =>
        JsonSerializer.Serialize(new
        {
            periodEnd = entry.PeriodEnd?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            estimate = entry.Estimate,
            actual = entry.Actual,
        });

    static async Task<HashSet<string>> MembersAsync(
        SqliteConnection connection,
        string indexCode,
        DateOnly session,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);
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
            $"{outcome.RowsDropped} dropped, {outcome.Requests} request(s)");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
