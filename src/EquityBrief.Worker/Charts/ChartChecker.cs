using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Chart;
using EquityBrief.Core.Components;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Charts;

public sealed record ChartCheckOutcome(DateOnly Night, int Stocks, IReadOnlyDictionary<string, int> Flags, int PulledYears, int Notes, IReadOnlyList<string> New);

// The chart audit run over every member after the bars land and the averages are worked, asking nothing of the network: each
// finding of the core's check written for the night, and the notes the chart page draws. The night's row is partial where a
// flag is new since the night before, so the run page names it the morning it appears, and the whole list stands at the proof page.
// see: The chart check reads every member's chart after the bars land and asks nothing of the network, and lists what it finds the morning it appears
public sealed class ChartChecker(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.TickerJoin, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.KeptBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.PulledSplit, Touch.Read),
            new StoreTouch(Store.SessionMark, Touch.Read),
            new StoreTouch(Store.SplitEvent, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.ChartAverage, Touch.Read),
            new StoreTouch(Store.ChartCheck, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.DividendEvent, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.SecTicker, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read),
            new StoreTouch(Store.SeriesState, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "chart-check";

    // How many of the new flags the night's row names; the proof page holds them all.
    public const int NewNamed = 20;

    const string Members = @"
        SELECT ticker, name, joined FROM membership
        WHERE " + IndexScope.Condition + @" AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session);
    ";

    const string FormerRows = @"SELECT ticker, name, ""left"" FROM membership WHERE ""left"" IS NOT NULL AND ""left"" <= $session;";
    const string Joins = "SELECT ticker, earlier FROM ticker_join;";
    const string NewestStored = "SELECT MAX(session_date) FROM bar WHERE session_date <= $session;";
    const string Reactions = "SELECT ticker, report_date, reaction_session FROM earnings_reaction;";
    const string CalendarReports = "SELECT ticker, event_date FROM calendar WHERE kind = 'earnings';";
    const string PulledReports = "SELECT DISTINCT ticker, event_date FROM pulled_earnings;";
    const string Quarters = "SELECT DISTINCT ticker, report_date, filing_date FROM reported_quarter;";
    const string Filers = "SELECT DISTINCT ticker, cik FROM sec_ticker;";
    const string FiledDays = "SELECT DISTINCT cik, filed FROM filed_fact;";
    const string ReleaseDays = "SELECT DISTINCT cik, filed FROM release_document;";
    const string Dividends = "SELECT ticker, ex_date, amount FROM dividend_event;";
    const string PulledSplits = "SELECT ticker, ex_date, new_shares, old_shares FROM pulled_split;";
    const string NightSplits = "SELECT ticker, ex_date, new_shares, old_shares FROM split_event;";
    const string States = "SELECT ticker, state FROM series_state;";
    const string BarsOf = "SELECT session_date, open, high, low, close, raw_close, volume FROM bar WHERE ticker = $ticker AND session_date <= $session ORDER BY session_date;";
    const string PullHolding = "SELECT pull, close FROM pulled_bar WHERE ticker = $ticker AND session_date = $first ORDER BY pull DESC LIMIT 1;";
    const string PullOf = "SELECT session_date, open, high, low, close, raw_close, volume FROM pulled_bar WHERE ticker = $ticker AND pull = $pull AND " + SessionMarking.FromItsStart + " ORDER BY session_date;";
    const string Marks = "SELECT ticker, session_date, kind, says FROM session_mark;";
    const string KeptRaw = "SELECT raw_close FROM kept_bar WHERE ticker = $ticker AND session_date = $session;";
    const string IndicatorsOf = "SELECT name, session_date, value FROM indicator WHERE ticker = $ticker AND name IN ('sma20', 'sma50', 'sma200') AND value IS NOT NULL;";
    const string WarmedOf = "SELECT name, sessions FROM chart_average WHERE ticker = $ticker;";
    const string PreviousNight = "SELECT MAX(night) FROM chart_check WHERE night < $night;";
    const string RowsOfNight = "SELECT ticker, flag, kind, session_date FROM chart_check WHERE night = $night;";
    const string ClearOld = "DELETE FROM chart_check WHERE night = $night OR night < $previous;";
    const string ClearNight = "DELETE FROM chart_check WHERE night = $night;";

    const string InsertRow = @"
        INSERT INTO chart_check (night, ticker, flag, kind, session_date, says, run_id)
        VALUES ($night, $ticker, $flag, $kind, $session, $says, $run)
        ON CONFLICT (night, ticker, flag, kind, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows, 0, 0, '0', $detail);
    ";

    sealed class Events
    {
        public HashSet<DateOnly> Results { get; } = [];

        public HashSet<DateOnly> Filings { get; } = [];

        public List<CheckedDividend> Dividends { get; } = [];

        public List<CheckedSplit> Splits { get; } = [];

        public StockEvents Read() => new(Results, Filings, Dividends, Splits);
    }

    static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public async Task<ChartCheckOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default, IReadOnlyList<string>? wider = null)
    {
        var started = clock.UtcNow;
        var session = clock.SessionDateAt(started);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var (night, stocks, findings) = await FindingsAsync(connection, indexCode, session, wider, cancellation);

        var previous = await PreviousAsync(connection, night, cancellation);
        var known = previous is null ? null : await KeysAsync(connection, previous, cancellation);
        var fresh = known is null
            ? []
            : findings
                .Where(row => row.Finding.Flag != ChartCheck.Note && !known.Contains(Key(row.Ticker, row.Finding)))
                .Select(row => FormattableString.Invariant($"{row.Ticker} {row.Finding.Flag} {row.Finding.Kind} {Text(row.Finding.Session)}"))
                .ToArray();

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.CommandText = previous is null ? ClearNight : ClearOld;
            clear.Parameters.AddWithValue("$night", Text(night));

            if (previous is not null)
            {
                clear.Parameters.AddWithValue("$previous", previous);
            }

            await clear.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var (ticker, finding) in findings)
        {
            await using var insert = connection.CreateCommand();
            insert.CommandText = InsertRow;
            insert.Parameters.AddWithValue("$night", Text(night));
            insert.Parameters.AddWithValue("$ticker", ticker);
            insert.Parameters.AddWithValue("$flag", finding.Flag);
            insert.Parameters.AddWithValue("$kind", finding.Kind);
            insert.Parameters.AddWithValue("$session", Text(finding.Session));
            insert.Parameters.AddWithValue("$says", finding.Says);
            insert.Parameters.AddWithValue("$run", runId);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var flags = findings
            .Where(row => row.Finding.Flag != ChartCheck.Note)
            .GroupBy(row => row.Finding.Flag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var notes = findings.Count(row => row.Finding.Flag == ChartCheck.Note);
        var pulledYears = findings.Count(row => row.Finding.Kind.EndsWith(ChartCheck.InThePulledYears, StringComparison.Ordinal));
        var outcome = new ChartCheckOutcome(night, stocks, flags, pulledYears, notes, fresh);

        await AppendAsync(connection, runId, started, findings.Count, fresh.Length > 0 ? "partial" : "ok", Detail(outcome), cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // Every member's findings over what the connection holds, writing nothing: a move's explanation waits until every member's
    // moves are counted.
    public static async Task<(DateOnly Night, int Stocks, IReadOnlyList<(string Ticker, ChartFinding Finding)> Findings)> FindingsAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider, CancellationToken cancellation = default)
    {
        var night = await NightAsync(connection, session, cancellation);
        var members = await MembersAsync(connection, indexCode, wider, session, cancellation);
        var events = await EventsAsync(connection, cancellation);
        var states = await PairsAsync(connection, States, cancellation);
        var changes = await ChangesAsync(connection, members, session, cancellation);
        var marks = await MarksAsync(connection, cancellation);
        var traded =ExchangeClosures.SessionsBetween(new[] { night.AddYears(-1), ExchangeClosures.CoveredFrom }.Max().AddDays(-1), night.AddDays(1));
        var findings = new List<(string Ticker, ChartFinding Finding)>();
        var moves = new List<(string Ticker, IReadOnlyList<SeenMove> Moves)>();
        var bySession = new Dictionary<DateOnly, int>();

        foreach (var ticker in members.Keys.Order(StringComparer.Ordinal))
        {
            var stock = await StockAsync(connection, ticker, session, states, changes, marks.GetValueOrDefault(ticker), cancellation);

            if (stock is null)
            {
                continue;
            }

            var theirs = events.TryGetValue(ticker, out var held) ? held.Read() : new StockEvents(new HashSet<DateOnly>(), new HashSet<DateOnly>(), [], []);
            var seen = ChartCheck.MovesOf(stock.Sessions, stock.Marks);

            findings.AddRange(ChartCheck.Read(stock, theirs, night, traded).Select(finding => (ticker, finding)));
            moves.Add((ticker, seen));

            foreach (var move in seen)
            {
                bySession[move.Session] = bySession.GetValueOrDefault(move.Session) + 1;
            }
        }

        foreach (var (ticker, seen) in moves)
        {
            var theirs = events.TryGetValue(ticker, out var held) ? held.Read() : new StockEvents(new HashSet<DateOnly>(), new HashSet<DateOnly>(), [], []);

            findings.AddRange(ChartCheck.ReadMoves(seen, theirs, bySession).Select(finding => (ticker, finding)));
        }

        return (night, members.Count, findings);
    }

    // A check that could not finish is named on its own row, and the night goes on to the rules.
    public async Task RecordNotReadAsync(string runId, DateTimeOffset started, string why, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await AppendAsync(connection, runId, started, 0, "partial", "the chart check did not finish: " + why, cancellation);
    }

    public static string Detail(ChartCheckOutcome outcome) =>
        FormattableString.Invariant($"{outcome.Stocks} stock(s) read over {Text(outcome.Night)}: ") +
        string.Join(", ", new[] { "a", "b", "c", "e" }.Select(flag => FormattableString.Invariant($"{outcome.Flags.GetValueOrDefault(flag)} flag {flag}"))) +
        FormattableString.Invariant($", {outcome.PulledYears} of them in the pulled years, {outcome.Notes} note(s), no request; {outcome.New.Count} new since the night before") +
        (outcome.New.Count == 0 ? string.Empty : ": " + string.Join("; ", outcome.New.Take(NewNamed)) + (outcome.New.Count > NewNamed ? FormattableString.Invariant($" and {outcome.New.Count - NewNamed} more, at /proof/charts") : string.Empty));

    static string Key(string ticker, ChartFinding finding) => string.Join('|', ticker, finding.Flag, finding.Kind, Text(finding.Session));

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset started, int rows, string outcome, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<DateOnly> NightAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestStored;
        command.Parameters.AddWithValue("$session", Text(session));

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : session;
    }

    static async Task<string?> PreviousAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = PreviousNight;
        command.Parameters.AddWithValue("$night", Text(night));

        return await command.ExecuteScalarAsync(cancellation) as string;
    }

    static async Task<HashSet<string>> KeysAsync(SqliteConnection connection, string night, CancellationToken cancellation)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = RowsOfNight;
        command.Parameters.AddWithValue("$night", night);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            keys.Add(string.Join('|', reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return keys;
    }

    static async Task<Dictionary<string, (string? Name, string? Joined)>> MembersAsync(SqliteConnection connection, string indexCode, IReadOnlyList<string>? wider, DateOnly session, CancellationToken cancellation)
    {
        var members = new Dictionary<string, (string? Name, string? Joined)>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = Members;
        IndexScope.Bind(command, indexCode, wider);
        command.Parameters.AddWithValue("$session", Text(session));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members[reader.GetString(0)] = (reader.IsDBNull(1) ? null : reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
        }

        return members;
    }

    static async Task<Dictionary<string, string>> PairsAsync(SqliteConnection connection, string query, CancellationToken cancellation)
    {
        var pairs = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = query;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            pairs[reader.GetString(0)] = reader.GetString(1);
        }

        return pairs;
    }

    // A name written without its company words, so two rows of one company read alike.
    public static string Plain(string? name) => string.Join(' ', Regex.Replace((name ?? string.Empty).ToLowerInvariant(), "[^a-z0-9 ]", " ")
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Where(word => word is not ("inc" or "corp" or "corporation" or "co" or "company" or "ltd" or "plc" or "the" or "holdings" or "holding" or "group" or "class" or "a" or "b" or "common" or "stock" or "new" or "llc" or "sa" or "nv" or "limited" or "trust" or "reit" or "and" or "of")));

    // A member's ticker change: the store's join, or a row under another ticker naming the same company that left within five days
    // of the day this member joined, which is how the index files a change of ticker.
    static async Task<Dictionary<string, (string Old, DateOnly? On)>> ChangesAsync(SqliteConnection connection, Dictionary<string, (string? Name, string? Joined)> members, DateOnly session, CancellationToken cancellation)
    {
        var changes = new Dictionary<string, (string Old, DateOnly? On)>(StringComparer.Ordinal);
        var byName = members
            .Where(member => Plain(member.Value.Name).Length > 0)
            .GroupBy(member => Plain(member.Value.Name), StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FormerRows;
            command.Parameters.AddWithValue("$session", Text(session));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (old, name, left) = (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), Date(reader.GetString(2)));

                if (old.EndsWith("_old", StringComparison.Ordinal) || !byName.TryGetValue(Plain(name), out var member) || member.Key == old
                    || member.Value.Joined is not { } joined || Math.Abs(Date(joined).DayNumber - left.DayNumber) > 5)
                {
                    continue;
                }

                changes[member.Key] = (old, left);
            }
        }

        foreach (var (ticker, earlier) in await PairsAsync(connection, Joins, cancellation))
        {
            changes[ticker] = (earlier, null);
        }

        return changes;
    }

    static async Task<Dictionary<string, Events>> EventsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var events = new Dictionary<string, Events>(StringComparer.Ordinal);

        Events Of(string ticker)
        {
            if (!events.TryGetValue(ticker, out var held))
            {
                events[ticker] = held = new Events();
            }

            return held;
        }

        async Task Each(string query, Action<SqliteDataReader> row)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = query;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                row(reader);
            }
        }

        await Each(Reactions, reader =>
        {
            Of(reader.GetString(0)).Results.Add(Date(reader.GetString(1)));
            Of(reader.GetString(0)).Results.Add(Date(reader.GetString(2)));
        });
        await Each(CalendarReports, reader => Of(reader.GetString(0)).Results.Add(Date(reader.GetString(1))));
        await Each(PulledReports, reader => Of(reader.GetString(0)).Results.Add(Date(reader.GetString(1))));
        await Each(Quarters, reader =>
        {
            foreach (var column in new[] { 1, 2 })
            {
                if (!reader.IsDBNull(column))
                {
                    Of(reader.GetString(0)).Filings.Add(Date(reader.GetString(column)));
                }
            }
        });
        await Each(Dividends, reader => Of(reader.GetString(0)).Dividends.Add(new CheckedDividend(Date(reader.GetString(1)), Money.FromStorage(reader.GetString(2)))));

        foreach (var query in new[] { PulledSplits, NightSplits })
        {
            await Each(query, reader =>
            {
                var split = new CheckedSplit(Date(reader.GetString(1)), Money.FromStorage(reader.GetString(2)), Money.FromStorage(reader.GetString(3)));

                if (!Of(reader.GetString(0)).Splits.Any(held => held.ExDate == split.ExDate))
                {
                    Of(reader.GetString(0)).Splits.Add(split);
                }
            });
        }

        var tickersOf = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        await Each(Filers, reader =>
        {
            if (!tickersOf.TryGetValue(reader.GetString(1), out var tickers))
            {
                tickersOf[reader.GetString(1)] = tickers = [];
            }

            tickers.Add(reader.GetString(0));
        });

        void Days(SqliteDataReader reader, Func<Events, HashSet<DateOnly>> set)
        {
            if (tickersOf.TryGetValue(reader.GetString(0), out var tickers))
            {
                foreach (var ticker in tickers)
                {
                    set(Of(ticker)).Add(Date(reader.GetString(1)));
                }
            }
        }

        await Each(ReleaseDays, reader => Days(reader, held => held.Results));
        await Each(FiledDays, reader => Days(reader, held => held.Filings));

        return events;
    }

    static async Task<Dictionary<string, List<SessionMark>>> MarksAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var marks = new Dictionary<string, List<SessionMark>>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();
        command.CommandText = Marks;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!marks.TryGetValue(reader.GetString(0), out var theirs))
            {
                marks[reader.GetString(0)] = theirs = [];
            }

            theirs.Add(new SessionMark(Date(reader.GetString(1)), reader.GetString(2), reader.GetString(3)));
        }

        return marks;
    }

    static async Task<StockToCheck?> StockAsync(SqliteConnection connection, string ticker, DateOnly session, Dictionary<string, string> states, Dictionary<string, (string Old, DateOnly? On)> changes, IReadOnlyList<SessionMark>? marks, CancellationToken cancellation)
    {
        async Task<List<CheckedSession>> Sessions(string query, bool pulled, params (string Name, string Value)[] bound)
        {
            var rows = new List<CheckedSession>();

            await using var command = connection.CreateCommand();
            command.CommandText = query;
            command.Parameters.AddWithValue("$ticker", ticker);

            foreach (var (name, value) in bound)
            {
                command.Parameters.AddWithValue(name, value);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                rows.Add(new CheckedSession(
                    Date(reader.GetString(0)),
                    Money.FromStorage(reader.GetString(1)),
                    Money.FromStorage(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3)),
                    Money.FromStorage(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                    reader.GetInt64(6),
                    pulled));
            }

            return rows;
        }

        var stored = await Sessions(BarsOf, false, ("$session", Text(session)));

        if (stored.Count == 0)
        {
            return null;
        }

        var first = stored[0];
        string? pull = null;
        decimal? pulledClose = null;

        await using (var holding = connection.CreateCommand())
        {
            holding.CommandText = PullHolding;
            holding.Parameters.AddWithValue("$ticker", ticker);
            holding.Parameters.AddWithValue("$first", Text(first.Session));

            await using var reader = await holding.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                pull = reader.GetString(0);
                pulledClose = Money.FromStorage(reader.GetString(1));
            }
        }

        var over = new Dictionary<DateOnly, (decimal Close, decimal? RawClose)>();
        var before = new List<CheckedSession>();
        decimal? scale = null;
        decimal? kept = null;

        if (pull is not null && pulledClose is > 0)
        {
            scale = ChartSeries.Ratio(first.Close, pulledClose.Value);

            foreach (var row in await Sessions(PullOf, true, ("$pull", pull)))
            {
                if (row.Session < first.Session)
                {
                    var bar = ChartSeries.AtStoreScale(new SessionBar(row.Session, row.Open, row.High, row.Low, row.Close, row.Volume), scale.Value);

                    before.Add(row with { Open = bar.Open, High = bar.High, Low = bar.Low, Close = bar.Close });
                }
                else
                {
                    over[row.Session] = (row.Close, row.RawClose);
                }
            }

            if (before.Count > 0)
            {
                await using var raw = connection.CreateCommand();
                raw.CommandText = KeptRaw;
                raw.Parameters.AddWithValue("$ticker", ticker);
                raw.Parameters.AddWithValue("$session", Text(before[^1].Session));

                kept = await raw.ExecuteScalarAsync(cancellation) is string keptClose ? Money.FromStorage(keptClose) : null;
            }
        }

        var averages = new Dictionary<string, Dictionary<DateOnly, double>>(StringComparer.Ordinal);

        Dictionary<DateOnly, double> Named(string name)
        {
            if (!averages.TryGetValue(name, out var values))
            {
                averages[name] = values = [];
            }

            return values;
        }

        await using (var warmed = connection.CreateCommand())
        {
            warmed.CommandText = WarmedOf;
            warmed.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await warmed.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                using var sessions = JsonDocument.Parse(reader.GetString(1));

                foreach (var pair in sessions.RootElement.EnumerateArray())
                {
                    Named(reader.GetString(0))[Date(pair[0].GetString()!)] = pair[1].GetDouble();
                }
            }
        }

        await using (var indicators = connection.CreateCommand())
        {
            indicators.CommandText = IndicatorsOf;
            indicators.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await indicators.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                Named(reader.GetString(0))[Date(reader.GetString(1))] = reader.GetDouble(2);
            }
        }

        return new StockToCheck(
            ticker,
            [.. before, .. stored],
            averages.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<DateOnly, double>)pair.Value, StringComparer.Ordinal),
            over,
            scale,
            kept,
            states.GetValueOrDefault(ticker, "ok"),
            before.Count == 0 && first.Session <= session.AddYears(-1).AddDays(7),
            changes.TryGetValue(ticker, out var change) ? change : null,
            marks);
    }
}
