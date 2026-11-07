using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Charts;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

public sealed record DataCheckOutcome(string RunId, int Stocks, int Sessions, DateOnly? First, DateOnly? Last, IReadOnlyList<DataFinding> Findings, bool Written, IReadOnlyList<string>? Unread = null);

// The data check run over every stock's pulled history, asking nothing of anyone: the calendar read from the stocks that traded
// each day, every stock's jumps counted across the stocks before any is read, then each stock classified by the core's rules and
// the run's findings written whole in one transaction, the run before's removed in it. No pulled row, bar or mark moves.
// From 23.4, asked for the long history, it reads every code's long prices from 2004 in place of the pull, with their splits and
// dividends, and writes the findings from 2005 on, the year before read behind them.
// see: The data check classifies every stock's pulled sessions by stated rules into a table of its own, and moves no price row
// see: The long history's prices are asked once a code from 2004-01-02 and kept apart from the bars, the pull's tables and every night
public sealed class DataChecker(IClock clock, string databaseFile, bool longHistory = false) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.TickerJoin, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledSplit, Touch.Read),
            new StoreTouch(Store.LongBar, Touch.Read),
            new StoreTouch(Store.LongSplit, Touch.Read),
            new StoreTouch(Store.LongDividend, Touch.Read),
            new StoreTouch(Store.SplitEvent, Touch.Read),
            new StoreTouch(Store.DividendEvent, Touch.Read),
            new StoreTouch(Store.SessionMark, Touch.Read),
            new StoreTouch(Store.DataCheck, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "data-check";

    public const string LongStage = "data-check-long";

    // The first session the long history's findings are written from; the year before is read behind it.
    public static DateOnly LongFindingsFrom { get; } = new(2005, 1, 1);

    const string Holders = "SELECT session_date, COUNT(DISTINCT ticker) FROM pulled_bar GROUP BY session_date;";
    const string Spans = "SELECT ticker, MIN(session_date), MAX(session_date) FROM pulled_bar GROUP BY ticker ORDER BY ticker;";
    const string LongHolders = "SELECT session_date, COUNT(*) FROM long_bar GROUP BY session_date;";
    const string LongSpans = "SELECT ticker, MIN(session_date), MAX(session_date) FROM long_bar GROUP BY ticker ORDER BY ticker;";
    const string LongSplits = "SELECT ticker, ex_date, new_shares, old_shares FROM long_split;";
    const string LongDividends = "SELECT ticker, ex_date, amount FROM long_dividend;";
    const string LongSeriesOf = "SELECT session_date, open, high, low, close, raw_close, volume FROM long_bar WHERE ticker = $ticker ORDER BY session_date;";
    const string Members = @"SELECT ticker, name, joined, ""left"" FROM membership;";
    const string Joins = "SELECT ticker, earlier FROM ticker_join;";
    const string PulledSplits = "SELECT ticker, ex_date, new_shares, old_shares FROM pulled_split;";
    const string NightSplits = "SELECT ticker, ex_date, new_shares, old_shares FROM split_event;";
    const string Dividends = "SELECT ticker, ex_date, amount FROM dividend_event;";
    const string Marks = "SELECT ticker, session_date, kind, says FROM session_mark;";

    // Each session from the newest pull holding it, as the sweeps read the pull.
    const string SeriesOf = @"
        SELECT session_date, open, high, low, close, raw_close, volume FROM pulled_bar AS row
        WHERE ticker = $ticker AND pull = (SELECT MAX(pull) FROM pulled_bar AS newer WHERE newer.ticker = row.ticker AND newer.session_date = row.session_date)
        ORDER BY session_date;
    ";

    const string ClearRuns = "DELETE FROM data_check;";

    const string InsertRow = @"
        INSERT INTO data_check (ticker, session_date, class, kind, era, says, run_id)
        VALUES ($ticker, $session, $class, $kind, $era, $says, $run)
        ON CONFLICT (ticker, session_date, class, kind) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);
    ";

    static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static decimal Price(string text) => decimal.Parse(text, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

    static bool Readable(SqliteDataReader reader, int column, out decimal price)
    {
        price = 0;

        return !reader.IsDBNull(column) && reader.GetFieldType(column) == typeof(string)
            && decimal.TryParse(reader.GetString(column), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out price);
    }

    public async Task<DataCheckOutcome> RunAsync(string runId, bool write = true, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        var opened = StoreConnection.Builder(databaseFile);

        if (!write)
        {
            opened.Mode = SqliteOpenMode.ReadOnly;
        }

        await using var connection = new SqliteConnection(opened.ConnectionString);
        await connection.OpenAsync(cancellation);

        var holders = new Dictionary<DateOnly, int>();
        var spans = new List<(string Ticker, DateOnly First, DateOnly Last)>();

        await ReadAsync(connection, longHistory ? LongHolders : Holders, reader => holders[Date(reader.GetString(0))] = reader.GetInt32(1), cancellation);
        await ReadAsync(connection, longHistory ? LongSpans : Spans, reader => spans.Add((reader.GetString(0), Date(reader.GetString(1)), Date(reader.GetString(2)))), cancellation);

        var calendar = new PulledCalendar(holders, spans.Select(span => (span.First, span.Last)));
        var splits = new Dictionary<string, List<CheckedSplit>>(StringComparer.Ordinal);
        var dividends = new Dictionary<string, List<CheckedDividend>>(StringComparer.Ordinal);
        var marks = new Dictionary<string, List<SessionMark>>(StringComparer.Ordinal);

        async Task Splits(string sql) => await ReadAsync(connection, sql, reader =>
        {
            var split = new CheckedSplit(Date(reader.GetString(1)), Price(reader.GetString(2)), Price(reader.GetString(3)));
            var held = splits.TryGetValue(reader.GetString(0), out var list) ? list : splits[reader.GetString(0)] = [];

            if (split.NewShares > 0 && split.OldShares > 0 && !held.Any(other => other.ExDate == split.ExDate))
            {
                held.Add(split);
            }
        }, cancellation);

        await Splits(longHistory ? LongSplits : PulledSplits);
        await Splits(NightSplits);

        foreach (var sql in longHistory ? [LongDividends, Dividends] : new[] { Dividends })
        {
            await ReadAsync(connection, sql, reader =>
            {
                var held = dividends.TryGetValue(reader.GetString(0), out var list) ? list : dividends[reader.GetString(0)] = [];
                var paid = new CheckedDividend(Date(reader.GetString(1)), Price(reader.GetString(2)));

                if (!held.Any(other => other.ExDate == paid.ExDate))
                {
                    held.Add(paid);
                }
            }, cancellation);
        }

        await ReadAsync(connection, Marks, reader => (marks.TryGetValue(reader.GetString(0), out var list) ? list : marks[reader.GetString(0)] = []).Add(new(Date(reader.GetString(1)), reader.GetString(2), reader.GetString(3))), cancellation);

        var changes = await ChangesAsync(connection, cancellation);
        var jumpsEachSession = new Dictionary<DateOnly, int>();
        var unread = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (var (ticker, _, _) in spans)
        {
            if (await SeriesOrNoneAsync(connection, longHistory ? LongSeriesOf : SeriesOf, ticker, unread, cancellation) is not { } series)
            {
                continue;
            }

            foreach (var day in DataCheck.Jumps(series))
            {
                jumpsEachSession[day] = jumpsEachSession.GetValueOrDefault(day) + 1;
            }
        }

        var firstRead = spans.Count == 0 ? default : spans.Min(span => span.First);
        var findings = new List<DataFinding>();
        var sessions = 0;

        foreach (var (ticker, _, _) in spans)
        {
            if (unread.ContainsKey(ticker) || await SeriesOrNoneAsync(connection, longHistory ? LongSeriesOf : SeriesOf, ticker, unread, cancellation) is not { } series)
            {
                continue;
            }

            sessions += series.Count;
            findings.AddRange(DataCheck.Read(
                new HeldStock(ticker, series, splits.GetValueOrDefault(ticker) ?? [], dividends.GetValueOrDefault(ticker) ?? [], changes.TryGetValue(ticker, out var change) ? change : null, marks.GetValueOrDefault(ticker)),
                calendar,
                jumpsEachSession,
                firstRead).Where(finding => !longHistory || finding.Session >= LongFindingsFrom));
        }

        var outcome = new DataCheckOutcome(runId, spans.Count - unread.Count, sessions, spans.Count == 0 ? null : firstRead, spans.Count == 0 ? null : spans.Max(span => span.Last), findings, write, [.. unread.Select(stock => stock.Key + " " + stock.Value)]);

        if (write)
        {
            await WriteAsync(connection, outcome, started, cancellation);
        }

        return outcome;
    }

    // A stock's ticker change: the store's join, or a row under another ticker naming the same company that left within five days of
    // the day this one joined, which is how the index files a change of ticker.
    static async Task<Dictionary<string, (string Old, DateOnly? On)>> ChangesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var rows = new List<(string Ticker, string Name, DateOnly? Joined, DateOnly? Left)>();

        await ReadAsync(connection, Members, reader => rows.Add((
            reader.GetString(0),
            ChartChecker.Plain(reader.IsDBNull(1) ? null : reader.GetString(1)),
            reader.IsDBNull(2) ? null : Date(reader.GetString(2)),
            reader.IsDBNull(3) ? null : Date(reader.GetString(3)))), cancellation);

        var changes = new Dictionary<string, (string Old, DateOnly? On)>(StringComparer.Ordinal);

        foreach (var joined in rows.Where(row => row.Name.Length > 0 && row.Joined is not null))
        {
            var old = rows.FirstOrDefault(row => row.Name == joined.Name && row.Ticker != joined.Ticker && !row.Ticker.EndsWith("_old", StringComparison.Ordinal)
                && row.Left is { } left && Math.Abs(left.DayNumber - joined.Joined!.Value.DayNumber) <= 5);

            if (old.Ticker is not null && !changes.ContainsKey(joined.Ticker))
            {
                changes[joined.Ticker] = (old.Ticker, joined.Joined);
            }
        }

        await ReadAsync(connection, Joins, reader => changes[reader.GetString(0)] = (reader.GetString(1), null), cancellation);

        return changes;
    }

    // A series holding a row the check cannot read is left out whole and named with its session, the other stocks read on.
    static async Task<IReadOnlyList<HeldSession>?> SeriesOrNoneAsync(SqliteConnection connection, string sql, string ticker, IDictionary<string, string> unread, CancellationToken cancellation)
    {
        try
        {
            return await SeriesAsync(connection, sql, ticker, cancellation);
        }
        catch (SeriesUnread refused)
        {
            unread[ticker] = refused.Message;

            return null;
        }
    }

    sealed class SeriesUnread(string message) : Exception(message);

    static async Task<IReadOnlyList<HeldSession>> SeriesAsync(SqliteConnection connection, string sql, string ticker, CancellationToken cancellation)
    {
        var series = new List<HeldSession>();

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var session = reader.GetString(0);

            if (!DateOnly.TryParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                || !Readable(reader, 1, out var open) || !Readable(reader, 2, out var high) || !Readable(reader, 3, out var low) || !Readable(reader, 4, out var close))
            {
                throw new SeriesUnread(FormattableString.Invariant($"{session}: a price or the date the check cannot read"));
            }

            series.Add(new(
                day,
                open,
                high,
                low,
                close,
                reader.IsDBNull(5) || !Readable(reader, 5, out var raw) ? null : raw,
                reader.IsDBNull(6) ? 0 : reader.GetInt64(6)));
        }

        return series;
    }

    async Task WriteAsync(SqliteConnection connection, DataCheckOutcome outcome, DateTimeOffset started, CancellationToken cancellation)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = ClearRuns;
            await clear.ExecuteNonQueryAsync(cancellation);
        }

        var written = 0;

        await using (var insert = connection.CreateCommand())
        {
            insert.CommandText = InsertRow;

            var (ticker, session, kind, era, says) = (insert.Parameters.Add("$ticker", SqliteType.Text), insert.Parameters.Add("$session", SqliteType.Text), insert.Parameters.Add("$kind", SqliteType.Text), insert.Parameters.Add("$era", SqliteType.Text), insert.Parameters.Add("$says", SqliteType.Text));
            var kindOf = insert.Parameters.Add("$class", SqliteType.Text);

            insert.Parameters.AddWithValue("$run", outcome.RunId);

            foreach (var finding in outcome.Findings)
            {
                (ticker.Value, session.Value, kindOf.Value, kind.Value, era.Value, says.Value) = (finding.Ticker, Text(finding.Session), finding.Class, finding.Kind, DataCheck.Era(finding.Session), finding.Says);
                written += await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        await using (var run = connection.CreateCommand())
        {
            run.CommandText = AppendRun;
            run.Parameters.AddWithValue("$run_id", outcome.RunId);
            run.Parameters.AddWithValue("$stage", longHistory ? LongStage : Stage);
            run.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$rows", written);
            run.Parameters.AddWithValue("$detail", Read(outcome, written));
            await run.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);
    }

    // What the run read, as its row on the run log and the History page say it: the stocks and sessions, the span, the findings, and
    // each stock whose series it could not read.
    public static string Read(DataCheckOutcome outcome, int findings) =>
        FormattableString.Invariant($"{outcome.Stocks:N0} stock(s) and {outcome.Sessions:N0} session(s) read from {outcome.First:yyyy-MM-dd} to {outcome.Last:yyyy-MM-dd}, {findings:N0} finding(s)")
        + (outcome.Unread is { Count: > 0 } unread ? FormattableString.Invariant($"; {unread.Count} stock(s) not read: {string.Join(", ", unread)}") : string.Empty);

    // Every finding a line, as the store would hold it, for a print a person reads beside another count.
    public static IEnumerable<string> Each(DataCheckOutcome outcome) =>
        outcome.Findings.Select(finding => string.Join('|', finding.Ticker, Text(finding.Session), finding.Class, finding.Kind));

    // The counts a person reads: each class and kind with its findings and its stocks.
    public static IReadOnlyList<string> Lines(DataCheckOutcome outcome)
    {
        var lines = new List<string>
        {
            "data-check: " + Read(outcome, outcome.Findings.Count) + (outcome.Written ? ", written" : ", nothing written"),
        };

        foreach (var group in outcome.Findings.GroupBy(finding => finding.Class).OrderBy(group => IndexOf(DataCheck.Classes, group.Key)))
        {
            lines.Add(FormattableString.Invariant($"  {group.Key}: {group.Count()} on {group.Select(finding => finding.Ticker).Distinct(StringComparer.Ordinal).Count()}"));

            foreach (var kind in group.GroupBy(finding => finding.Kind).OrderBy(kind => DataCheck.Kinds.ToList().IndexOf((group.Key, kind.Key))))
            {
                var eras = string.Join(", ", kind.GroupBy(finding => DataCheck.Era(finding.Session)).OrderBy(era => era.Key, StringComparer.Ordinal).Select(era => FormattableString.Invariant($"{era.Key} {era.Count()}")));

                lines.Add(FormattableString.Invariant($"    {kind.Key}: {kind.Count()} on {kind.Select(finding => finding.Ticker).Distinct(StringComparer.Ordinal).Count()} ({eras})"));
            }
        }

        return lines;
    }

    static int IndexOf(IReadOnlyList<string> list, string value) => list.ToList().IndexOf(value);

    static async Task ReadAsync(SqliteConnection connection, string sql, Action<SqliteDataReader> row, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            row(reader);
        }
    }
}
