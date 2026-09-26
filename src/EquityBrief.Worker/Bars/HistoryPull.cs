using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// The history pull.
//
// On the operator's command and never on a night. Every name the index held on any session from a
// date to tonight is asked once for its daily bars over that whole span, and the earnings calendar
// once for each calendar month of it, and what comes back is stored in two tables of its own. The
// span reaches tonight rather than stopping where the store's year begins, because the night drops
// its oldest session every night, and a pull that stopped at the store's first session would leave a
// hole between the two within a week.
//
// Each row carries the run id of the pull that wrote it. No night reads either table, so removing a
// pull whole moves nothing any night, listing, score or page read, which is what lets these rows be
// removed where a stored bar may not be.
// see: The history pulled before the store's year sits apart from its bars, marked by the pull that wrote it, read by no night and removed whole by that pull
public sealed class HistoryPull(
    IHistoricalBarFeed bars,
    IEarningsCalendarFeed earnings,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.PulledEarnings, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.HistoricalPrice, Feed.EarningsCalendar]);

    public const string Stage = "history-pull";
    public const string PurgeStage = "history-purge";

    // The run ids a pull and a purge are written under, which the run page reads as runs by hand.
    public const string RunPrefix = "history-pull-";
    public const string PurgePrefix = "history-purge-";

    // A pull any name went unanswered on.
    public const string Partial = "partial";

    // Every name the index held on at least one session of the span.
    const string NamesHeld = @"
        SELECT DISTINCT ticker FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $through)
          AND (""left"" IS NULL OR ""left"" > $from)
        ORDER BY ticker;
    ";

    // Insert only. A session some earlier pull already holds for a name keeps that pull's row, so
    // a second pull adds what the first did not reach and a purge of either removes its own rows.
    const string InsertBar = @"
        INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull)
        VALUES ($ticker, $session_date, $open, $high, $low, $close, $raw_close, $volume, $pull)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string InsertEarnings = @"
        INSERT INTO pulled_earnings (ticker, event_date, timing, pull)
        VALUES ($ticker, $event_date, $timing, $pull)
        ON CONFLICT (ticker, event_date) DO NOTHING;
    ";

    const string BarCount = "SELECT COUNT(*) FROM pulled_bar;";
    const string EarningsCount = "SELECT COUNT(*) FROM pulled_earnings;";

    const string BarsOfPull = "SELECT COUNT(*) FROM pulled_bar WHERE pull = $pull;";
    const string EarningsOfPull = "SELECT COUNT(*) FROM pulled_earnings WHERE pull = $pull;";

    // The removal, which takes a pull's rows whole and nothing else.
    const string DeleteBarsOfPull = "DELETE FROM pulled_bar WHERE pull = $pull;";
    const string DeleteEarningsOfPull = "DELETE FROM pulled_earnings WHERE pull = $pull;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public static string RunIdAt(string prefix, DateTimeOffset at) => FormattableString.Invariant($"{prefix}{at:yyyyMMddTHHmmss.fffffffZ}");

    // The verb a person runs: `history-pull --from <yyyy-MM-dd>`, or `history-pull --purge <pull>`. The
    // feeds are asked for only by a pull, so removing one needs no key and reaches no provider.
    public static async Task<int> RunAsync(
        string[] args,
        Func<NightFeeds> feeds,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (VerbArguments.Value(args, "--purge") is { } pull)
        {
            try
            {
                var purged = await PurgeAsync(clock, databaseFile, pull, RunIdAt(PurgePrefix, clock.UtcNow));

                output.WriteLine(FormattableString.Invariant($"removed the pull {purged.Pull}: {purged.Bars} bar(s) and {purged.Earnings} earnings print(s)"));

                return 0;
            }
            catch (ArgumentException refusal)
            {
                error.WriteLine("history-pull: " + refusal.Message);

                return 1;
            }
        }

        if (VerbArguments.Value(args, "--from") is not { } given
            || !DateOnly.TryParseExact(given, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
        {
            error.WriteLine("history-pull: name the first session to pull with '--from <yyyy-MM-dd>', or a pull to remove with '--purge <pull>'.");

            return 2;
        }

        NightFeeds resolved;

        try
        {
            resolved = feeds();
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }

        var runId = RunIdAt(RunPrefix, clock.UtcNow);

        try
        {
            var outcome = await new HistoryPull(resolved.Historical, resolved.Calendar, clock, databaseFile)
                .PullAsync(VerbArguments.Value(args, "--index") ?? "GSPC", from, runId, error.WriteLine);

            output.WriteLine("pull " + runId);
            output.WriteLine(Detail(outcome));

            return 0;
        }
        catch (ArgumentException refusal)
        {
            error.WriteLine("history-pull: " + refusal.Message);

            return 1;
        }
    }

    // Pulls every name the index held from a date to tonight, and the earnings prints over the same
    // span. A date on or after tonight's session asks for nothing and is refused before any request.
    public async Task<HistoryPullOutcome> PullAsync(
        string indexCode,
        DateOnly from,
        string runId,
        Action<string>? progress = null,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var through = clock.SessionDateAt(startedAt);

        if (from >= through)
        {
            throw new ArgumentException(
                FormattableString.Invariant($"A pull reaches from a date before tonight's session, {through:yyyy-MM-dd}, and {from:yyyy-MM-dd} is not before it."),
                nameof(from));
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var names = await NamesAsync(connection, indexCode, from, through, cancellation);
        var requestsBefore = bars.Requests + earnings.Requests;

        // Fetched before anything is stored, so the calendar the holes are read against is the whole
        // set rather than whichever names happened to arrive first.
        var fetched = new Dictionary<string, IReadOnlyList<ProviderBar>>(StringComparer.OrdinalIgnoreCase);
        var unanswered = new List<string>();

        foreach (var ticker in names)
        {
            try
            {
                var series = await bars.BarsAsync(ticker, from, through, cancellation);

                if (series.Count == 0)
                {
                    unanswered.Add(FormattableString.Invariant($"{ticker}: the provider sent no session"));
                }
                else
                {
                    fetched[ticker] = series;
                }
            }
            catch (ProviderRefusal refusal)
            {
                unanswered.Add($"{ticker}: {refusal.Message}");
            }

            if ((fetched.Count + unanswered.Count) % 50 == 0)
            {
                progress?.Invoke(FormattableString.Invariant($"{fetched.Count + unanswered.Count} of {names.Count} name(s) asked"));
            }
        }

        // A hole is a session the other names traded and one name's series does not hold between its
        // own first and last. It is stored as the provider sent it and named, and a reader of the
        // pulled series is the one that decides what a hole stops.
        var dates = fetched.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyCollection<DateOnly>)[.. entry.Value.Select(bar => bar.SessionDate)],
            StringComparer.OrdinalIgnoreCase);
        var holes = TradingCalendar.CanDetect(dates) ? TradingCalendar.GapsIn(dates) : [];

        var held = names.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var prints = new Dictionary<(string Ticker, DateOnly Date), CalendarEvent>();
        var monthsUnanswered = new List<string>();
        var months = MonthsOf(from, through);

        foreach (var (first, last) in months)
        {
            try
            {
                foreach (var print in await earnings.EventsAsync(first, last, cancellation))
                {
                    if (held.Contains(print.Ticker))
                    {
                        prints.TryAdd((print.Ticker, print.EventDate), print);
                    }
                }
            }
            catch (ProviderRefusal refusal)
            {
                monthsUnanswered.Add(FormattableString.Invariant($"{first:yyyy-MM}: {refusal.Message}"));
            }
        }

        var barsBefore = await CountAsync(connection, BarCount, null, cancellation);
        var earningsBefore = await CountAsync(connection, EarningsCount, null, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var (ticker, series) in fetched.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            foreach (var bar in series)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertBar;
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$session_date", Text(bar.SessionDate));
                Money.Bind(insert, "$open", bar.Open);
                Money.Bind(insert, "$high", bar.High);
                Money.Bind(insert, "$low", bar.Low);
                Money.Bind(insert, "$close", bar.Close);
                Money.Bind(insert, "$raw_close", bar.RawClose);
                insert.Parameters.AddWithValue("$volume", bar.Volume);
                insert.Parameters.AddWithValue("$pull", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        foreach (var print in prints.Values.OrderBy(print => print.Ticker, StringComparer.Ordinal).ThenBy(print => print.EventDate))
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = InsertEarnings;
            insert.Parameters.AddWithValue("$ticker", print.Ticker);
            insert.Parameters.AddWithValue("$event_date", Text(print.EventDate));
            insert.Parameters.AddWithValue("$timing", EquityBrief.Worker.Calendar.CalendarFetcher.Filed(print.Timing));
            insert.Parameters.AddWithValue("$pull", runId);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var barsWritten = await CountAsync(connection, BarCount, null, cancellation) - barsBefore;
        var earningsWritten = await CountAsync(connection, EarningsCount, null, cancellation) - earningsBefore;
        var requests = bars.Requests + earnings.Requests - requestsBefore;

        var outcome = new HistoryPullOutcome(
            from,
            through,
            names.Count,
            fetched.Count,
            unanswered,
            holes,
            barsWritten,
            earningsWritten,
            requests,
            months.Count,
            monthsUnanswered);

        await AppendAsync(
            connection,
            runId,
            Stage,
            startedAt,
            clock.UtcNow,
            unanswered.Count == 0 && monthsUnanswered.Count == 0 ? "ok" : Partial,
            barsWritten + earningsWritten,
            requests,
            JsonSerializer.Serialize(new
            {
                from = Text(from),
                through = Text(through),
                names = names.Count,
                stored = fetched.Count,
                bars = barsWritten,
                earnings = earningsWritten,
                months = months.Count,
                unanswered,
                monthsUnanswered,
                holes = holes
                    .GroupBy(gap => gap.Ticker, StringComparer.Ordinal)
                    .Select(group => FormattableString.Invariant($"{group.Key} is missing {group.Count()} session(s), the first {group.Min(gap => gap.SessionDate):yyyy-MM-dd}"))
                    .ToArray(),
            }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // Removes one pull's rows from both tables and says so on the run log. A pull no row carries is
    // refused and nothing is written, so a mistyped id cannot record a removal that removed nothing.
    public static async Task<HistoryPurgeOutcome> PurgeAsync(
        IClock clock,
        string databaseFile,
        string pull,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var barsHeld = await CountAsync(connection, BarsOfPull, pull, cancellation);
        var earningsHeld = await CountAsync(connection, EarningsOfPull, pull, cancellation);

        if (barsHeld == 0 && earningsHeld == 0)
        {
            throw new ArgumentException($"No pulled row carries the pull '{pull}', so there is nothing to remove.", nameof(pull));
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var removal in new[] { DeleteBarsOfPull, DeleteEarningsOfPull })
        {
            await using var delete = connection.CreateCommand();
            delete.CommandText = removal;
            delete.Parameters.AddWithValue("$pull", pull);

            await delete.ExecuteNonQueryAsync(cancellation);
        }

        await AppendAsync(
            connection,
            runId,
            PurgeStage,
            startedAt,
            clock.UtcNow,
            "ok",
            0,
            0,
            JsonSerializer.Serialize(new { pull, bars = barsHeld, earnings = earningsHeld }),
            cancellation);

        await transaction.CommitAsync(cancellation);

        return new HistoryPurgeOutcome(pull, barsHeld, earningsHeld);
    }

    // The windows the earnings calendar is asked for: each calendar month the span touches, cut to
    // the span at both ends.
    public static IReadOnlyList<(DateOnly From, DateOnly To)> MonthsOf(DateOnly from, DateOnly through)
    {
        var months = new List<(DateOnly, DateOnly)>();

        for (var start = new DateOnly(from.Year, from.Month, 1); start <= through; start = start.AddMonths(1))
        {
            var last = start.AddMonths(1).AddDays(-1);

            months.Add((start < from ? from : start, last > through ? through : last));
        }

        return months;
    }

    // What a pull did, as the operator reads it at the prompt.
    public static string Detail(HistoryPullOutcome outcome) =>
        FormattableString.Invariant($"pulled {outcome.From:yyyy-MM-dd} to {outcome.Through:yyyy-MM-dd}: {outcome.Stored} of {outcome.Names} name(s) answered, {outcome.BarsWritten} bar(s) and {outcome.EarningsWritten} earnings print(s) stored, {outcome.Holes.Select(gap => gap.Ticker).Distinct(StringComparer.Ordinal).Count()} name(s) with a missing session, {outcome.Months} calendar month(s) asked, {outcome.Requests} request(s)")
        + string.Concat(outcome.Unanswered.Select(line => Environment.NewLine + "  unanswered: " + line))
        + string.Concat(outcome.MonthsUnanswered.Select(line => Environment.NewLine + "  month unanswered: " + line));

    static async Task<IReadOnlyList<string>> NamesAsync(SqliteConnection connection, string indexCode, DateOnly from, DateOnly through, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NamesHeld;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$from", Text(from));
        command.Parameters.AddWithValue("$through", Text(through));

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    static async Task<int> CountAsync(SqliteConnection connection, string sql, string? pull, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (pull is not null)
        {
            command.Parameters.AddWithValue("$pull", pull);
        }

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static async Task AppendAsync(
        SqliteConnection connection,
        string runId,
        string stage,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        string outcome,
        int rowsWritten,
        int requests,
        string detail,
        CancellationToken cancellation)
    {
        await using var log = connection.CreateCommand();
        log.CommandText = AppendRun;
        log.Parameters.AddWithValue("$run_id", runId);
        log.Parameters.AddWithValue("$stage", stage);
        log.Parameters.AddWithValue("$started_at", startedAt.ToString("O", CultureInfo.InvariantCulture));
        log.Parameters.AddWithValue("$ended_at", endedAt.ToString("O", CultureInfo.InvariantCulture));
        log.Parameters.AddWithValue("$outcome", outcome);
        log.Parameters.AddWithValue("$rows_written", rowsWritten);
        log.Parameters.AddWithValue("$network_requests", requests);
        log.Parameters.AddWithValue("$detail", detail);

        await log.ExecuteNonQueryAsync(cancellation);
    }

    static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// What one pull did: the span, the names the index held over it and how many answered, each name the
// provider did not answer and why, the sessions the answered names miss against one another, the rows
// stored, the requests made, and the calendar months asked and unanswered.
public sealed record HistoryPullOutcome(
    DateOnly From,
    DateOnly Through,
    int Names,
    int Stored,
    IReadOnlyList<string> Unanswered,
    IReadOnlyList<Gap> Holes,
    int BarsWritten,
    int EarningsWritten,
    int Requests,
    int Months,
    IReadOnlyList<string> MonthsUnanswered);

// What one purge removed.
public sealed record HistoryPurgeOutcome(string Pull, int Bars, int Earnings);
