using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Moves;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Indicators;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Moves;

public sealed record PageHistoryOutcome(int NamesRead, int NamesWithout, int MovesStated, int RowsWritten, int Stopped);

// The name page's two figures over the years before the store's: each name's typical earnings move over every print the
// store holds, and its long view of month-end closes, read in the chart averages step after the chart's averages, from
// the same pull the averages read, the newest holding the name's first stored session.
//
// The long view is the pull's month-end closes before that session, brought to the bar store's scale by the ratio of the
// two closes on it, and the stored year's after. The typical earnings move is the median size of the reaction to every
// print the calendar and the pull file, a day both file read once as the calendar files it: each print the stored bars
// reach read off them by the reaction record's own function, and every other off the pull by the earnings rule's own
// function, kept only where the pull holds the session the rule took and the one before it with no session between them
// it misses, the sessions read by the rule the pull reads its own holes by. A print whose session is not yet stored is
// not counted at all. Only the name page draws either; no rule, level, listing, gate, card or record reads them, and
// nothing a rule's version pins is edited. Every row is replaced in one write after every name is read, so a stop before
// it leaves each name the rows an earlier night wrote.
// see: A stock's typical earnings move is the median size of its reaction over every print the store holds, read through the history pulled before its year
// see: The long view draws the month-end closes over the pulled history at the bar store's scale with the plan's levels on the same prices
public sealed class PageHistoryReader(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.EarningsMove, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LongView, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "page-history";

    // The outcome of a reader that could not finish, which stops no step the night runs after it.
    public const string NotRead = "partial";

    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    const string BarsFor = "SELECT session_date, close FROM bar WHERE ticker = $ticker ORDER BY session_date;";

    // The newest pull holding the name's first stored session, which the chart's averages read from as well.
    const string PullHolding = @"
        SELECT pull, close FROM pulled_bar
        WHERE ticker = $ticker AND session_date = $first
        ORDER BY pull DESC
        LIMIT 1;
    ";

    const string AnyPulledBefore = "SELECT 1 FROM pulled_bar WHERE ticker = $ticker AND session_date < $first LIMIT 1;";

    // The pull's closes to the name's first stored session, which a print reacting on that session reads its close before
    // from.
    const string PulledTo = @"
        SELECT session_date, close FROM pulled_bar
        WHERE ticker = $ticker AND pull = $pull AND session_date <= $first
        ORDER BY session_date;
    ";

    const string CalendarPrints = @"
        SELECT event_date, timing FROM calendar
        WHERE ticker = $ticker AND kind = 'earnings'
        ORDER BY event_date;
    ";

    // Each print once, as the newest pull holding it files it, since from the 22.11 ruling a pull keeps its whole span
    // beside an earlier pull's rows.
    // see: A pull keeps its whole span beside an earlier pull's rows, and every reader reads the newest pull holding what it reads
    const string PulledPrints = @"
        SELECT event_date, timing FROM pulled_earnings
        WHERE ticker = $ticker
          AND pull = (SELECT MAX(held.pull) FROM pulled_earnings AS held WHERE held.ticker = pulled_earnings.ticker AND held.event_date = pulled_earnings.event_date)
        ORDER BY event_date;
    ";

    const string ClearMove = "DELETE FROM earnings_move WHERE ticker = $ticker;";

    const string ClearView = "DELETE FROM long_view WHERE ticker = $ticker;";

    // A name the bar store no longer holds draws no page of its own, so nothing of its own is kept.
    const string ClearUnheld = @"
        DELETE FROM earnings_move WHERE ticker NOT IN (SELECT DISTINCT ticker FROM bar);
        DELETE FROM long_view WHERE ticker NOT IN (SELECT DISTINCT ticker FROM bar);
    ";

    const string InsertMove = @"
        INSERT INTO earnings_move (ticker, night, prints, unread, first_print, median_move, pull, reason)
        VALUES ($ticker, $night, $prints, $unread, $first_print, $median_move, $pull, $reason);
    ";

    const string InsertView = @"
        INSERT INTO long_view (ticker, night, closes, pull, ratio, reason)
        VALUES ($ticker, $night, $closes, $pull, $ratio, $reason);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    // A print read off the pull, waiting on the sessions every name read gives.
    sealed record PulledReaction(DateOnly ReportDate, EventTiming Timing, DateOnly Session, DateOnly Before, double MovePct);

    // A name read: the newest stored session it was read through, the pull and why none was read, its long view, the
    // prints the stored bars reached, those read off the pull, and how many neither reached.
    sealed record Read(
        string Ticker,
        DateOnly Through,
        string? Pull,
        string? Reason,
        decimal? Ratio,
        IReadOnlyList<MonthEnd> Closes,
        IReadOnlyList<(DateOnly ReportDate, DateOnly Session, double MovePct)> OnTheBars,
        IReadOnlyList<PulledReaction> OffThePull,
        int Unreached);

    public async Task<PageHistoryOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var stop = new SeriesGapStop();
        var holders = new Dictionary<DateOnly, int>();
        var spans = new List<(DateOnly First, DateOnly Last)>();
        var names = new List<Read>();
        var withheld = new List<string>();
        var (read, without) = (0, 0);

        foreach (var ticker in await TickersAsync(connection, cancellation))
        {
            var bars = await BarsAsync(connection, ticker, cancellation);

            // A name whose stored series holds a hole is computed for nothing, as every figure over its year is.
            if (bars.Count == 0 || stop.Stops(ticker, [.. bars.Select(bar => bar.SessionDate)]))
            {
                withheld.Add(ticker);

                continue;
            }

            var first = bars[0];
            var last = bars[^1].SessionDate;
            var (pull, reason, ratio, pulled) = await PullAsync(connection, ticker, first, cancellation);

            // Every session the pull is read over, the name's first stored one among them, counted toward the sessions
            // the pulled years are read on.
            if (pulled.Count > 0)
            {
                spans.Add((pulled[0].SessionDate, pulled[^1].SessionDate));

                foreach (var bar in pulled)
                {
                    holders[bar.SessionDate] = holders.GetValueOrDefault(bar.SessionDate) + 1;
                }
            }

            // A print reacts on the session the earnings rule takes for it, so one reported after the close of the newest
            // stored session, or after the close on it, has not reacted yet and is counted nowhere.
            bool Reacted(ReactionPrint print) =>
                print.Timing == EventTiming.After ? print.ReportDate < last : print.ReportDate <= last;

            ReactionPrint[] calendared = [.. await PrintsAsync(connection, CalendarPrints, ticker, cancellation)];
            var calendarDays = calendared.Select(print => print.ReportDate).ToHashSet();
            ReactionPrint[] prints =
            [
                .. calendared
                    .Concat((await PrintsAsync(connection, PulledPrints, ticker, cancellation)).Where(print => !calendarDays.Contains(print.ReportDate)))
                    .Where(Reacted)
                    .OrderBy(print => print.ReportDate),
            ];

            // Each print the stored bars reach is read off them, and every other off the pull, which holds the sessions
            // before the store's year.
            var onTheBars = EarningsReactions.Of(prints, bars);
            var reached = onTheBars.Reactions.Select(reaction => reaction.ReportDate).ToHashSet();
            var offThePull = new List<PulledReaction>();
            var unreached = 0;

            foreach (var print in prints.Where(print => !reached.Contains(print.ReportDate)))
            {
                IReadOnlyList<PastPrint> taken = pulled.Count == 0 ? [] : LadderSeries.EarningsRuleFor([(print.ReportDate, print.Timing)], pulled, null);

                if (taken.Count == 0)
                {
                    unreached++;

                    continue;
                }

                var at = pulled.First(bar => bar.SessionDate == taken[0].Session);
                var prior = pulled.Last(bar => bar.SessionDate < at.SessionDate);

                offThePull.Add(new PulledReaction(print.ReportDate, print.Timing, at.SessionDate, prior.SessionDate, Statistic.FromRatio((at.Close - prior.Close) / prior.Close) * 100));
            }

            names.Add(new Read(
                ticker,
                last,
                pull,
                reason,
                ratio,
                LongViews.Of(
                    [.. pulled.Where(bar => bar.SessionDate < first.SessionDate).Select(bar => (bar.SessionDate, bar.Close))],
                    ratio ?? 1m,
                    [.. bars.Select(bar => (bar.SessionDate, bar.Close))]),
                [.. onTheBars.Reactions.Select(reaction => (reaction.ReportDate, reaction.Session, reaction.MovePct))],
                offThePull,
                unreached));

            if (pull is null)
            {
                without++;
            }
            else
            {
                read++;
            }
        }

        // The sessions of the years the pull reaches, read off every name it was read for, and each name's typical
        // earnings move with the prints read off the pull that stand against them; every name's rows are replaced in the
        // one write that stores them all.
        var calendar = new PulledCalendar(holders, spans);
        var (stated, rows) = (0, 0);

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var name in names)
            {
                // Each row is dated by the newest session the name's stored year holds, the session its plan is dated
                // by, so the page draws the plan's levels across a long view read through the same session alone.
                var nightText = name.Through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                PulledReaction[] standing = [.. name.OffThePull.Where(print => EarningsMoves.Stands(print.ReportDate, print.Timing, print.Session, print.Before, calendar))];
                var move = EarningsMoves.Of(
                    [.. name.OnTheBars, .. standing.Select(print => (print.ReportDate, print.Session, print.MovePct))],
                    name.Unreached + name.OffThePull.Count - standing.Length);

                await ExecuteAsync(connection, transaction, ClearMove, [("$ticker", name.Ticker)], cancellation);
                await ExecuteAsync(
                    connection,
                    transaction,
                    InsertMove,
                    [
                        ("$ticker", name.Ticker),
                        ("$night", nightText),
                        ("$prints", move.Prints),
                        ("$unread", move.Unread),
                        ("$first_print", move.FirstPrint?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        ("$median_move", move.MedianPct),
                        ("$pull", name.Pull),
                        ("$reason", name.Reason),
                    ],
                    cancellation);
                await ExecuteAsync(connection, transaction, ClearView, [("$ticker", name.Ticker)], cancellation);
                await ExecuteAsync(
                    connection,
                    transaction,
                    InsertView,
                    [
                        ("$ticker", name.Ticker),
                        ("$night", nightText),
                        ("$closes", JsonSerializer.Serialize(name.Closes.Select(point => new[] { point.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), Money.ToStorage(point.Close) }))),
                        ("$pull", name.Pull),
                        ("$ratio", name.Ratio is { } scale ? Money.ToStorage(scale) : null),
                        ("$reason", name.Reason),
                    ],
                    cancellation);

                rows += 2;

                if (move.Stated)
                {
                    stated++;
                }
            }

            foreach (var ticker in withheld)
            {
                await ExecuteAsync(connection, transaction, ClearMove, [("$ticker", ticker)], cancellation);
                await ExecuteAsync(connection, transaction, ClearView, [("$ticker", ticker)], cancellation);
            }

            await ExecuteAsync(connection, transaction, ClearUnheld, [], cancellation);
            await transaction.CommitAsync(cancellation);
        }

        await RecordAsync(
            connection,
            runId,
            startedAt,
            "ok",
            rows,
            FormattableString.Invariant($"{read} name(s) read from a pull, {without} with none, {stated} typical earnings move(s) stated over {EarningsMoves.FewestPrints} prints or more, {rows} row(s){stop.Report()}"),
            cancellation);

        return new PageHistoryOutcome(read, without, stated, rows, stop.Stopped);
    }

    // A reader that could not finish, named on its own row with why; every name keeps the rows an earlier night wrote,
    // since nothing is replaced until every name is read.
    public async Task RecordNotReadAsync(string runId, DateTimeOffset startedAt, string why, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await RecordAsync(connection, runId, startedAt, NotRead, 0, "not read tonight, each name keeping the rows an earlier night wrote: " + why, cancellation);
    }

    // The pull the name's history is read from, the newest holding its first stored session, with the ratio that brings
    // it to the store's scale and its closes to that one; or why none is read, in the words the chart's averages use.
    // Each pulled session is read as its close alone, which is all the long view and the earnings rule read of it.
    static async Task<(string? Pull, string? Reason, decimal? Ratio, IReadOnlyList<LadderBar> Pulled)> PullAsync(
        SqliteConnection connection,
        string ticker,
        LadderBar first,
        CancellationToken cancellation)
    {
        var day = first.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string? pull = null;
        decimal? pulledClose = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PullHolding;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$first", day);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                pull = reader.GetString(0);
                pulledClose = Money.FromStorage(reader.GetString(1));
            }
        }

        if (pull is null || pulledClose is not > 0m)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = AnyPulledBefore;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$first", day);

            return (null, await command.ExecuteScalarAsync(cancellation) is not null ? ChartAverager.NoSharedSession : ChartAverager.NoPull, null, []);
        }

        var pulled = new List<LadderBar>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PulledTo;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$pull", pull);
            command.Parameters.AddWithValue("$first", day);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var close = Money.FromStorage(reader.GetString(1));

                pulled.Add(new LadderBar(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), close, close, close));
            }
        }

        return (pull, null, first.Close / pulledClose.Value, pulled);
    }

    static async Task<IReadOnlyList<ReactionPrint>> PrintsAsync(SqliteConnection connection, string sql, string ticker, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticker", ticker);

        var prints = new List<ReactionPrint>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            prints.Add(new ReactionPrint(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1) switch
                {
                    "before" => EventTiming.Before,
                    "after" => EventTiming.After,
                    _ => EventTiming.Unstated,
                },
                null,
                null,
                null));
        }

        return prints;
    }

    // A name's stored sessions, each read as its close alone, which is all the reaction record reads of a bar.
    static async Task<IReadOnlyList<LadderBar>> BarsAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BarsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var bars = new List<LadderBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var close = Money.FromStorage(reader.GetString(1));

            bars.Add(new LadderBar(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), close, close, close));
        }

        return bars;
    }

    static async Task<IReadOnlyList<string>> TickersAsync(SqliteConnection connection, CancellationToken cancellation)
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

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, (string Name, object? Value)[] parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, string outcome, int rows, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
