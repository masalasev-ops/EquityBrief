using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Moves;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Moves;

public sealed record MoveOutcome(int NamesExamined, int RowsWritten, int RowsDropped, int PeerReadings = 0, int Reactions = 0, int Unreached = 0);

// The move annotator. Reads the stored bars for every name the store holds and
// writes the largest single-day and multi-day moves of the stored year, which
// become the rows of the how-it-got-here table, each beside its group's median
// move over the same sessions, read off the membership on the night's session.
// Over the same bars it writes each name's two readings for the peers table,
// beside the group its moves are read against, and each print's earnings
// reaction off the calendar.
//
// It makes no request and calls no model, for the reason the swing finder does
// not: everything it needs is already in the store.
// see: The nightly run is arithmetic only
//
// The cause of each move is not written here. It is a researched claim and lives
// in `research_section` with its source, so the table's cause column is
// explicitly absent until phase 6 rather than blank. An absence stated and an
// absence drawn as emptiness are different things.
//
// The arithmetic is in `MoveSeries`, as a pure function of a session-ordered
// series. What is here is reading, writing and the run log.
// see: Code owns every number
public sealed class MoveAnnotator : IComponent
{
    // The last of the six computed tables to gain a deleter, which is what 4.0
    // ruled and 4.2 implemented for the other five: every computed table's
    // writer is its own deleter. `move` waited for this checkpoint because the
    // component did not exist until now, and a deleter declared before its
    // component deletes is a declaration with nothing behind it.
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Move, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.PeerReading, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.EarningsReaction, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "moves";

    // Every name with bars, rather than every current member, for the reason the
    // swing finder states: a name that left the index keeps its stored history
    // and a report opened on it should read what it read last night.
    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    // Every stored bar at once, because a move's group median reads other names' closes
    // on the move's own sessions, which a read one name at a time cannot reach. The high is
    // read for the peers table's distance below the year's high, and the low with it for the
    // earnings rule, whose bars are the ladder's.
    const string AllBars = @"
        SELECT ticker, session_date, high, low, close
        FROM bar
        ORDER BY ticker, session_date;
    ";

    // Every print the calendar holds on or before the night's session, which is its year behind,
    // with what the provider filed about each.
    const string PastPrints = @"
        SELECT ticker, event_date, timing, detail
        FROM calendar
        WHERE kind = 'earnings' AND event_date <= $session
        ORDER BY ticker, event_date;
    ";

    // A print's reaction, one row per name and print, replaced every night.
    const string UpsertReaction = @"
        INSERT INTO earnings_reaction (ticker, report_date, timing, reaction_session, estimate, actual, surprise_pct, move_pct)
        VALUES ($ticker, $report_date, $timing, $reaction_session, $estimate, $actual, $surprise_pct, $move_pct)
        ON CONFLICT (ticker, report_date) DO UPDATE SET
            timing = excluded.timing,
            reaction_session = excluded.reaction_session,
            estimate = excluded.estimate,
            actual = excluded.actual,
            surprise_pct = excluded.surprise_pct,
            move_pct = excluded.move_pct;
    ";

    // The prints this name no longer holds a reaction for tonight: fallen out of the calendar's
    // year, or out of the bars' reach with them.
    const string DropFallenOutReactions = @"
        DELETE FROM earnings_reaction
        WHERE ticker = $ticker
          AND report_date NOT IN (SELECT value FROM json_each($kept));
    ";

    // A name's two readings, one row per name, replaced every night.
    const string UpsertReading = @"
        INSERT INTO peer_reading (ticker, session_date, group_kind, group_name, year_high, below_high_pct, return_pct, bars)
        VALUES ($ticker, $session_date, $group_kind, $group_name, $year_high, $below_high_pct, $return_pct, $bars)
        ON CONFLICT (ticker) DO UPDATE SET
            session_date = excluded.session_date,
            group_kind = excluded.group_kind,
            group_name = excluded.group_name,
            year_high = excluded.year_high,
            below_high_pct = excluded.below_high_pct,
            return_pct = excluded.return_pct,
            bars = excluded.bars;
    ";

    // The readings of a name the gap stop withheld tonight, and of a name that holds no bars
    // any more, which a table of one row per name would otherwise keep beside tonight's close.
    const string DropWithheldReadings = @"
        DELETE FROM peer_reading
        WHERE ticker IN (SELECT value FROM json_each($withheld))
           OR ticker NOT IN (SELECT DISTINCT ticker FROM bar);

        DELETE FROM earnings_reaction
        WHERE ticker IN (SELECT value FROM json_each($withheld))
           OR ticker NOT IN (SELECT DISTINCT ticker FROM bar);
    ";

    // The members on the night's session, being joined by it where the join date is known
    // and not left by it, with the sector and the industry each one's row names, which is
    // what a name's group is read from.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string MembersOnTheSession = @"
        SELECT m.ticker, m.sector, m.industry
        FROM membership m
        WHERE (m.joined IS NULL OR m.joined <= $session)
          AND (m.""left"" IS NULL OR m.""left"" > $session);
    ";

    // Insert or update on the primary key. A move is recomputed from the same
    // bars every night and a name's set changes as the window moves, so a
    // session that was a top-eight move last night and is not tonight has to
    // go rather than stand.
    const string Upsert = @"
        INSERT INTO move (ticker, session_date, sessions, change_pct, rank, group_kind, group_name, group_members, group_counted, group_median)
        VALUES ($ticker, $session_date, $sessions, $change_pct, $rank, $group_kind, $group_name, $group_members, $group_counted, $group_median)
        ON CONFLICT (ticker, session_date) DO UPDATE SET
            sessions = excluded.sessions,
            change_pct = excluded.change_pct,
            rank = excluded.rank,
            group_kind = excluded.group_kind,
            group_name = excluded.group_name,
            group_members = excluded.group_members,
            group_counted = excluded.group_counted,
            group_median = excluded.group_median;
    ";

    // The rows this name no longer holds: every session for the name that is not
    // in tonight's set, whatever rank it carried.
    //
    // Until the phase 5 sign-off this deleted only rows ranked below tonight's
    // count, which catches a name whose set shrank and misses the ordinary case:
    // a new move enters the top eight, the one it pushes out is not written
    // tonight, and its row stands with the rank it had, beside tonight's move at
    // that rank. The operator's store held nine names with two moves ranked
    // eighth. The set is named by its sessions, which is the key, rather than by
    // a rank, which is a property the stale row still has.
    const string DropFallenOut = @"
        DELETE FROM move
        WHERE ticker = $ticker
          AND session_date NOT IN (SELECT value FROM json_each($kept));
    ";

    // The retention drop, one year back from the newest stored session, which is
    // the boundary the other five computed tables drop at. A move does not
    // outlive the bar it sits on.
    const string DropOlderThan = @"
        DELETE FROM move WHERE session_date < $oldest;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public MoveAnnotator(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<MoveOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var tickers = await TickersAsync(connection, cancellation);
        var series = await AllBarsAsync(connection, cancellation);
        var closes = series.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(bar => bar.SessionDate, bar => bar.Close), StringComparer.Ordinal);
        var members = await MembersAsync(connection, clock.SessionDateAt(clock.UtcNow), cancellation);
        var prints = await PastPrintsAsync(connection, clock.SessionDateAt(clock.UtcNow), cancellation);
        var written = 0;
        var readings = 0;
        var reactions = 0;
        var unreached = 0;
        var fallenOut = 0;
        var withheld = new List<string>();
        var stop = new SeriesGapStop();

        foreach (var ticker in tickers)
        {
            var held = series.TryGetValue(ticker, out var found) ? found : [];
            MoveBar[] bars = [.. held.Select(bar => new MoveBar(bar.SessionDate, bar.Close))];
            PeerBar[] peerBars = [.. held.Select(bar => new PeerBar(bar.SessionDate, bar.High, bar.Close))];

            // A name whose stored series has an interior hole is
            // computed for nothing and named on the run log.
            // see: A gap is a session the exchange traded and the store does not hold
            if (stop.Stops(ticker, [.. bars.Select(bar => bar.SessionDate)]))
            {
                withheld.Add(ticker);
                continue;
            }

            var moves = MoveSeries.For(bars);
            var group = Groups.Of(ticker, members);

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            foreach (var move in moves)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Upsert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session_date", move.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$sessions", move.Sessions);
                command.Parameters.AddWithValue("$change_pct", move.ChangePct);
                command.Parameters.AddWithValue("$rank", move.Rank);

                // The members' moves over the move's own sessions: from the close the move
                // was measured from to the close it ended on, each read off the member's own
                // stored bars on those two sessions.
                var from = bars[bars.Select(bar => bar.SessionDate).ToList().IndexOf(move.SessionDate) - move.Sessions].SessionDate;
                var median = Groups.MedianMove([.. group.Members.Select(member => (CloseOn(closes, member, from), CloseOn(closes, member, move.SessionDate)))]);

                command.Parameters.AddWithValue("$group_kind", group.Kind);
                command.Parameters.AddWithValue("$group_name", (object?)group.Name ?? DBNull.Value);
                command.Parameters.AddWithValue("$group_members", group.Members.Count);
                command.Parameters.AddWithValue("$group_counted", median.Counted);
                command.Parameters.AddWithValue("$group_median", (object?)median.Median ?? DBNull.Value);

                await command.ExecuteNonQueryAsync(cancellation);

                written++;
            }

            // The name's two readings for the peers table, over the same bars and beside the
            // group its moves are read against, so the table lists the members the medians were
            // taken over.
            // see: Peers are shown by price alone, in section 2 beside the move table
            if (PeerReadings.Of(peerBars) is { } reading)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = UpsertReading;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session_date", reading.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$group_kind", group.Kind);
                command.Parameters.AddWithValue("$group_name", (object?)group.Name ?? DBNull.Value);
                command.Parameters.AddWithValue("$year_high", Money.ToStorage(reading.YearHigh));
                command.Parameters.AddWithValue("$below_high_pct", reading.BelowHighPct);
                command.Parameters.AddWithValue("$return_pct", (object?)reading.ReturnPct ?? DBNull.Value);
                command.Parameters.AddWithValue("$bars", reading.Bars);

                await command.ExecuteNonQueryAsync(cancellation);

                readings++;
            }

            // The name's earnings reaction record, each print's session the one the earnings rule
            // takes for it and its move off the same bars, and a print the bars do not reach left
            // out and counted.
            // see: Each print's reaction is read from the nightly calendar and the stored bars, and reaches no reason, gate or plan
            var onFile = EarningsReactions.Of(prints.TryGetValue(ticker, out var filed) ? filed : [], held);

            foreach (var reaction in onFile.Reactions)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = UpsertReaction;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$report_date", reaction.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$timing", TimingWord(reaction.Timing));
                command.Parameters.AddWithValue("$reaction_session", reaction.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$estimate", (object?)reaction.Estimate ?? DBNull.Value);
                command.Parameters.AddWithValue("$actual", (object?)reaction.Actual ?? DBNull.Value);
                command.Parameters.AddWithValue("$surprise_pct", (object?)reaction.SurprisePct ?? DBNull.Value);
                command.Parameters.AddWithValue("$move_pct", reaction.MovePct);

                await command.ExecuteNonQueryAsync(cancellation);
            }

            await using (var drop = connection.CreateCommand())
            {
                drop.Transaction = (SqliteTransaction)transaction;
                drop.CommandText = DropFallenOutReactions;
                drop.Parameters.AddWithValue("$ticker", ticker);
                drop.Parameters.AddWithValue(
                    "$kept",
                    System.Text.Json.JsonSerializer.Serialize(
                        onFile.Reactions.Select(reaction => reaction.ReportDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

                await drop.ExecuteNonQueryAsync(cancellation);
            }

            reactions += onFile.Reactions.Count;
            unreached += onFile.Unreached;

            // Any session this name held that is not in tonight's set was a
            // biggest move and is not one now.
            await using (var drop = connection.CreateCommand())
            {
                drop.Transaction = (SqliteTransaction)transaction;
                drop.CommandText = DropFallenOut;
                drop.Parameters.AddWithValue("$ticker", ticker);
                drop.Parameters.AddWithValue(
                    "$kept",
                    System.Text.Json.JsonSerializer.Serialize(
                        moves.Select(move => move.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))));

                fallenOut += await drop.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        var dropped = await DroppedAsync(connection, await BoundaryAsync(connection, cancellation), cancellation);

        await using (var drop = connection.CreateCommand())
        {
            drop.CommandText = DropWithheldReadings;
            drop.Parameters.AddWithValue("$withheld", System.Text.Json.JsonSerializer.Serialize(withheld));

            await drop.ExecuteNonQueryAsync(cancellation);
        }

        await RecordAsync(connection, runId, startedAt, tickers.Count - stop.Stopped, written, readings, reactions, unreached, dropped + fallenOut, stop.Report(), cancellation);

        return new MoveOutcome(tickers.Count - stop.Stopped, written, dropped + fallenOut, readings, reactions, unreached);
    }

    async Task<IReadOnlyList<string>> TickersAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = TickersWithBars;

        var tickers = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    // Every stored bar, by name in session order. The high and the close are prices and are
    // read as decimals; the percentages the arithmetic makes of them are statistics and are
    // doubles, and the crossing is in `MoveSeries`, `Groups` and `PeerReadings`, where it is
    // named.
    static async Task<IReadOnlyDictionary<string, IReadOnlyList<LadderBar>>> AllBarsAsync(
        SqliteConnection connection,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AllBars;

        var series = new Dictionary<string, List<LadderBar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!series.TryGetValue(ticker, out var bars))
            {
                series[ticker] = bars = [];
            }

            bars.Add(new LadderBar(
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4))));
        }

        return series.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<LadderBar>)pair.Value, StringComparer.Ordinal);
    }

    // Every print on file by name, with the estimate, the actual and the surprise as the provider
    // sent them, read off the calendar row's detail.
    static async Task<IReadOnlyDictionary<string, IReadOnlyList<ReactionPrint>>> PastPrintsAsync(
        SqliteConnection connection,
        DateOnly session,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = PastPrints;
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var prints = new Dictionary<string, List<ReactionPrint>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!prints.TryGetValue(ticker, out var filed))
            {
                prints[ticker] = filed = [];
            }

            using var detail = System.Text.Json.JsonDocument.Parse(reader.IsDBNull(3) ? "{}" : reader.GetString(3));

            string? Filed(string name) =>
                detail.RootElement.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
                    ? value.GetString()
                    : null;

            filed.Add(new ReactionPrint(
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2) switch
                {
                    "before" => EventTiming.Before,
                    "after" => EventTiming.After,
                    _ => EventTiming.Unstated,
                },
                Filed("estimate"),
                Filed("actual"),
                Filed("surprise")));
        }

        return prints.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ReactionPrint>)pair.Value, StringComparer.Ordinal);
    }

    // The timing as the calendar and the reaction table write it.
    static string TimingWord(EventTiming timing) => timing switch
    {
        EventTiming.Before => "before",
        EventTiming.After => "after",
        _ => "unstated",
    };

    static async Task<IReadOnlyList<GroupMember>> MembersAsync(
        SqliteConnection connection,
        DateOnly session,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOnTheSession;
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = new List<GroupMember>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(new GroupMember(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return members;
    }

    // A name's close on a session, or none where the name holds no bar for it.
    static decimal? CloseOn(IReadOnlyDictionary<string, Dictionary<DateOnly, decimal>> closes, string ticker, DateOnly session) =>
        closes.TryGetValue(ticker, out var byDate) && byDate.TryGetValue(session, out var close) ? close : null;

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int names,
        int written,
        int readings,
        int reactions,
        int unreached,
        int dropped,
        string gaps,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", written + readings + reactions);
        command.Parameters.AddWithValue("$detail", $"{names} name(s), {written} move(s), {dropped} dropped, {readings} peer reading(s), {reactions} earnings reaction(s), {unreached} print(s) the bars do not reach{gaps}");

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<DateOnly?> BoundaryAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        var newest = await command.ExecuteScalarAsync(cancellation);

        return newest is null or DBNull
            ? null
            : DateOnly.ParseExact((string)newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .AddYears(-BarFetcher.RetentionYears);
    }

    static async Task<int> DroppedAsync(
        SqliteConnection connection,
        DateOnly? boundary,
        CancellationToken cancellation)
    {
        if (boundary is not { } oldest)
        {
            return 0;
        }

        await using var command = connection.CreateCommand();

        command.CommandText = DropOlderThan;
        command.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }
}
