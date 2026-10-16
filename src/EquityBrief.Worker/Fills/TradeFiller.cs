using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Fills;

// What the fill step did with one table's trades: how many it priced at the open, how many hold no next session or no
// open on it, how many still wait on their next session, how many it left as an earlier night priced them, and how many
// whose plan put up no risk at its close.
public sealed record FillCount(string Source, int Filled, int NoNextSession, int NoOpen, int Waiting, int Standing, int NoRisk = 0);

public sealed record FillOutcome(IReadOnlyList<FillCount> Sources)
{
    public int Filled => Sources.Sum(one => one.Filled);

    public int NoNextSession => Sources.Sum(one => one.NoNextSession);

    public int NoOpen => Sources.Sum(one => one.NoOpen);

    public int Waiting => Sources.Sum(one => one.Waiting);

    public int Standing => Sources.Sum(one => one.Standing);

    public int NoRisk => Sources.Sum(one => one.NoRisk);
}

// The fill step. A trade is bought at the open of the session after the night that listed it, and every stored trade's
// table keeps the figures it was walked to at the listing's close as its writer wrote them: the registered rules' lists
// and books, the S&P 400's and 600's page lists and books, the S&P 500 variants' picks and the S&P 500 page list's
// scored plans. Each night after the forward returns, and by hand over every stored trade, it reads each trade's next
// session's open from the stored bars and prices the trade there through the core's one pricing function: its fill at
// the trade's own scale, whether that open was past its stop or its target, and its result, benchmark and edge at the
// open in the risk the close set. The benchmark's move is read over the members the index held that night with a bar on
// both sessions and a typical move stored, each by its own gap over its own risk, a book's over its size cut. A trade
// whose next session is not stored yet waits and has no row; one whose stock holds no bar or no open on it is written
// with why. A row is written again only while the trade's own figures it was priced from are still arriving, so a fill
// once complete stands as written, as the trades do, since the bars and typical moves it read are kept a year. It reads
// the store and writes its own table and its run log row, asks for nothing and calls no model, and moves no rule's
// version, since no source a rule pins changes.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public sealed class TradeFiller(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.KeptBar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.IndexFamilyTrade, Touch.Read),
            new StoreTouch(Store.IndexRuleTrade, Touch.Read),
            new StoreTouch(Store.RulePick, Touch.Read),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read),
            new StoreTouch(Store.HeavyweightRuleHolding, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightRuleHolding, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.TradeFill, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "fills";

    public const string LargeIndex = "GSPC";

    // The tables a trade is stored in, in the order the step reads them.
    public static IReadOnlyList<string> Sources { get; } =
    [
        "family_trade", "index_family_trade", "index_rule_trade", "rule_pick",
        "heavyweight_holding", "heavyweight_rule_holding", "index_heavyweight_holding", "index_heavyweight_rule_holding",
        "forward_return",
    ];

    // One stored trade as the step reads it: where it is stored and its key there, its listing night, its buy and its
    // plan at the trade's own scale, its result and benchmark at the close, the stop's distance in typical moves where its
    // table keeps one, and a book's size cut.
    public sealed record StoredTrade(
        string Source,
        string Index,
        string Owner,
        string Ticker,
        DateOnly Session,
        double Entry,
        double? Stop,
        double? Target,
        double? RiskMoves,
        double? Result,
        double? Benchmark,
        IReadOnlyList<string>? Cut,
        bool Relative = false)
    {
        public bool Book => Cut is not null;
    }

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string Sessions = "SELECT session_date FROM bar UNION SELECT session_date FROM kept_bar ORDER BY 1;";

    const string StandingFills = "SELECT source, index_code, owner, ticker, session_date, result, benchmark, reason FROM trade_fill;";

    const string FamilyTrades = "SELECT candidate, ticker, session_date, entry, stop, target, risk_moves, result, benchmark FROM family_trade;";

    const string IndexFamilyTrades = "SELECT index_code, family, ticker, session_date, entry, stop, target, risk_moves, result, benchmark FROM index_family_trade;";

    const string IndexRuleTrades = "SELECT index_code, candidate, ticker, session_date, entry, stop, target, risk_moves, result, benchmark FROM index_rule_trade;";

    const string RulePicks = "SELECT index_code, rule, ticker, session_date, entry, stop, target, result FROM rule_pick;";

    const string LargeBook = "SELECT ticker, entered_on, entry_close, result, cut_return, cut FROM heavyweight_holding;";

    const string LargeRuleBooks = "SELECT candidate, ticker, entered_on, entry_close, result, cut_return, cut FROM heavyweight_rule_holding;";

    const string IndexBooks = "SELECT index_code, ticker, entered_on, entry_close, result, cut_return, cut FROM index_heavyweight_holding;";

    const string IndexRuleBooks = "SELECT index_code, candidate, ticker, entered_on, entry_close, result, cut_return, cut FROM index_heavyweight_rule_holding;";

    // The S&P 500 page list's scored plans: each a plan bought at the listing's close, its risk a share of that close and
    // its result its return over that risk, its target where its break-even states one. The filler writes a plan's risk
    // once the plan resolves, so a plan the list traded that is still open is read with none; the market's two session
    // horizons and the twenty-session context carry no plan, and a reason's setup is read only where its risk is written.
    static readonly string ScoredPlans = $@"
        SELECT horizon, ticker, session_date, return_pct, planned_risk, break_even
        FROM forward_return
        WHERE planned_risk > 0
           OR horizon NOT IN ('{ForwardReturnSeries.FiveSessions}', '{ForwardReturnSeries.TwentyOneSessions}', '{ForwardReturnSeries.Setup}', '{ForwardReturnSeries.SwingTwenty}', '{ForwardReturnSeries.ClearTwenty}');";

    const string Members = "SELECT index_code, ticker, joined, \"left\" FROM membership;";

    const string UpsertFill = @"
        INSERT INTO trade_fill (source, index_code, owner, ticker, session_date, filled_on, fill, past, result, benchmark, members, edge, reason, filled_at)
        VALUES ($source, $index, $owner, $ticker, $session, $filled_on, $fill, $past, $result, $benchmark, $members, $edge, $reason, $filled_at)
        ON CONFLICT (source, index_code, owner, ticker, session_date) DO UPDATE SET
            filled_on = excluded.filled_on, fill = excluded.fill, past = excluded.past, result = excluded.result,
            benchmark = excluded.benchmark, members = excluded.members, edge = excluded.edge, reason = excluded.reason,
            filled_at = excluded.filled_at;";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows_written, 0, 0, '0', $detail);";

    // `again` prices every stored trade over what an earlier run wrote, as the remedy does.
    public async Task<FillOutcome> RunAsync(string runId, bool again = false, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var calendar = new List<DateOnly>();

        await using (var command = Command(connection, null, Sessions, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                calendar.Add(Date(reader.GetString(0)));
            }
        }

        var trades = await TradesAsync(connection, cancellation);
        var standing = await StandingAsync(connection, cancellation);
        var due = trades.Where(trade => again || Due(trade, standing.TryGetValue(Key(trade), out var held) ? held : null)).ToArray();
        var dueKeys = due.Select(Key).ToHashSet();
        var next = due
            .Select(trade => trade.Session)
            .Distinct()
            .ToDictionary(session => session, session => NextOf(calendar, session));
        var read = await PricesAsync(connection, due, next, cancellation);
        var counts = Sources.ToDictionary(source => source, _ => new int[6], StringComparer.Ordinal);

        foreach (var trade in trades)
        {
            if (!dueKeys.Contains(Key(trade)))
            {
                counts[trade.Source][4]++;
            }
        }

        var filledAt = clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var written = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var trade in due)
        {
            if (next[trade.Session] is not { } on)
            {
                counts[trade.Source][3]++;

                continue;
            }

            var (fill, reason) = Price(trade, on, read);

            counts[trade.Source][fill is not null ? 0 : reason == TradeFills.NoOpen ? 2 : reason == TradeFills.NoRisk ? 5 : 1]++;

            await ExecuteAsync(connection, transaction, UpsertFill,
            [
                ("$source", trade.Source), ("$index", trade.Index), ("$owner", trade.Owner), ("$ticker", trade.Ticker),
                ("$session", Stamp(trade.Session)), ("$filled_on", fill is null ? DBNull.Value : Stamp(fill.On)),
                ("$fill", fill is null ? DBNull.Value : Money.ToStorage(Statistic.ToPrice(fill.Fill))),
                ("$past", fill?.Past is { } past ? past : DBNull.Value),
                ("$result", fill?.Result is { } result ? result : DBNull.Value),
                ("$benchmark", fill?.Benchmark is { } benchmark ? benchmark : DBNull.Value),
                ("$members", fill is { Members: > 0 } ? fill.Members : DBNull.Value),
                ("$edge", fill?.Edge is { } edge ? edge : DBNull.Value),
                ("$reason", reason is null ? DBNull.Value : reason),
                ("$filled_at", filledAt),
            ], cancellation);

            written++;
        }

        var outcome = new FillOutcome([.. Sources.Select(source => new FillCount(source, counts[source][0], counts[source][1], counts[source][2], counts[source][3], counts[source][4], counts[source][5]))]);

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId), ("$stage", Stage), ("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)), ("$rows_written", written), ("$detail", Detail(outcome)),
        ], cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // The step's row in words: the trades priced at the open, those holding no next session or no open on it, those whose
    // plan put up no risk, those waiting on their next session, and those an earlier night priced in full.
    public static string Detail(FillOutcome outcome) => FormattableString.Invariant(
        $"{outcome.Filled} trade(s) priced at the next session's open, {outcome.NoNextSession} whose stock holds no bar on its next session, {outcome.NoOpen} with no open on it, {outcome.NoRisk} whose plan put up no risk at its close, {outcome.Waiting} buying at the next open, {outcome.Standing} standing as priced before");

    // A trade is priced again while it holds no row, or while its own figures have arrived since its row was written: a
    // result, or a benchmark, that its row holds none of.
    static bool Due(StoredTrade trade, (double? Result, double? Benchmark, string? Reason)? held) =>
        held is not { } row
        || (row.Reason is null && ((trade.Result is not null && row.Result is null) || (trade.Benchmark is not null && row.Benchmark is null)));

    // The first stored session after a listing night, none where the store holds none yet.
    public static DateOnly? NextOf(IReadOnlyList<DateOnly> calendar, DateOnly session)
    {
        var at = calendar is List<DateOnly> list ? list.BinarySearch(session) : -1;
        var from = at >= 0 ? at + 1 : ~at;

        return from < calendar.Count ? calendar[from] : null;
    }

    // What the step reads of the bars and the typical moves for the trades it prices: each stock's open and close on a
    // listing night and on the session after it, its typical move on the night, and the indices' members on each night.
    public sealed record FillPrices(
        IReadOnlyDictionary<(string Ticker, DateOnly Session), (double Open, double Close)> Bars,
        IReadOnlyDictionary<(string Ticker, DateOnly Session), double> Moves,
        IReadOnlyList<(string Index, string Ticker, DateOnly? Joined, DateOnly? Left)> Spans,
        IReadOnlyDictionary<DateOnly, DateOnly?> Next)
    {
        public IEnumerable<string> MembersOn(string index, DateOnly session) =>
            Spans
                .Where(span => span.Index == index && (span.Joined is not { } joined || joined <= session) && (span.Left is not { } left || left > session))
                .Select(span => span.Ticker)
                .Distinct(StringComparer.Ordinal);
    }

    // One trade priced at its next session's open, or why it is not: its stock holding no bar on the night or on the
    // session after, or no open there.
    public static (TradeFill? Fill, string? Reason) Price(StoredTrade trade, DateOnly on, FillPrices read)
    {
        if (!read.Bars.TryGetValue((trade.Ticker, trade.Session), out var night) || !(night.Close > 0)
            || !read.Bars.TryGetValue((trade.Ticker, on), out var after))
        {
            return (null, TradeFills.NoNextSession);
        }

        if (!(after.Open > 0))
        {
            return (null, TradeFills.NoOpen);
        }

        // A plan stated as shares of its close is priced at the night's close as the bars hold it.
        if (trade.Relative)
        {
            trade = trade with { Entry = night.Close, Stop = trade.Stop * night.Close, Target = trade.Target * night.Close };
        }

        // The open at the trade's own scale: the bars as stored now over the close the trade was bought at.
        var scale = trade.Entry / night.Close;
        var open = after.Open * scale;

        if (trade.Book)
        {
            var cut = trade.Cut!
                .Select(ticker => read.Bars.TryGetValue((ticker, trade.Session), out var was) && read.Bars.TryGetValue((ticker, on), out var then)
                    ? new FillMember(true, was.Close, was.Close, then.Open)
                    : new FillMember(false, 0, 0, null))
                .ToArray();

            return Priced(TradeFills.Holding(on, trade.Entry, open, trade.Result, trade.Benchmark, cut));
        }

        // The stop's distance in typical moves: the trade's own where its table keeps one, else read off its stock's
        // typical move on the night.
        var riskMoves = trade.RiskMoves is > 0 ? trade.RiskMoves
            : trade.Stop is { } floor && read.Moves.TryGetValue((trade.Ticker, trade.Session), out var move) && move > 0
                ? (trade.Entry - floor) / (move * scale)
                : null;
        var members = trade.Benchmark is null || riskMoves is not { } moves
            ? []
            : read.MembersOn(trade.Index, trade.Session)
                .Select(ticker => read.Bars.TryGetValue((ticker, trade.Session), out var was) && read.Bars.TryGetValue((ticker, on), out var then) && read.Moves.TryGetValue((ticker, trade.Session), out var typical) && typical > 0
                    ? new FillMember(true, was.Close, moves * typical, then.Open)
                    : new FillMember(false, 0, 0, null))
                .ToArray();

        return Priced(TradeFills.Swing(on, trade.Entry, trade.Stop, trade.Target, open, trade.Result, trade.Benchmark, members));
    }

    // An open held and no fill is a plan that put up no risk at its close.
    static (TradeFill? Fill, string? Reason) Priced(TradeFill? fill) => fill is null ? (null, TradeFills.NoRisk) : (fill, null);

    static (string, string, string, string, DateOnly) Key(StoredTrade trade) => (trade.Source, trade.Index, trade.Owner, trade.Ticker, trade.Session);

    async Task<Dictionary<(string, string, string, string, DateOnly), (double? Result, double? Benchmark, string? Reason)>> StandingAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var held = new Dictionary<(string, string, string, string, DateOnly), (double?, double?, string?)>();

        await using var command = Command(connection, null, StandingFills, []);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held[(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), Date(reader.GetString(4)))] =
                (Real(reader, 5), Real(reader, 6), reader.IsDBNull(7) ? null : reader.GetString(7));
        }

        return held;
    }

    // Every stored trade of the nine tables.
    public static async Task<List<StoredTrade>> TradesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var trades = new List<StoredTrade>();

        async Task ReadAsync(string sql, Func<SqliteDataReader, StoredTrade?> read)
        {
            await using var command = Command(connection, null, sql, []);
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (read(reader) is { } trade)
                {
                    trades.Add(trade);
                }
            }
        }

        await ReadAsync(FamilyTrades, reader => new StoredTrade("family_trade", LargeIndex, reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)),
            Price(reader, 3)!.Value, Price(reader, 4), Price(reader, 5), Real(reader, 6), Real(reader, 7), Real(reader, 8), null));
        await ReadAsync(IndexFamilyTrades, reader => new StoredTrade("index_family_trade", reader.GetString(0), reader.GetString(1), reader.GetString(2), Date(reader.GetString(3)),
            Price(reader, 4)!.Value, Price(reader, 5), Price(reader, 6), Real(reader, 7), Real(reader, 8), Real(reader, 9), null));
        await ReadAsync(IndexRuleTrades, reader => new StoredTrade("index_rule_trade", reader.GetString(0), reader.GetString(1), reader.GetString(2), Date(reader.GetString(3)),
            Price(reader, 4)!.Value, Price(reader, 5), Price(reader, 6), Real(reader, 7), Real(reader, 8), Real(reader, 9), null));
        await ReadAsync(RulePicks, reader => new StoredTrade("rule_pick", reader.GetString(0), reader.GetString(1), reader.GetString(2), Date(reader.GetString(3)),
            Price(reader, 4)!.Value, Price(reader, 5), Price(reader, 6), null, Real(reader, 7), null, null));
        await ReadAsync(LargeBook, reader => new StoredTrade("heavyweight_holding", LargeIndex, string.Empty, reader.GetString(0), Date(reader.GetString(1)),
            Price(reader, 2)!.Value, null, null, null, Real(reader, 3), Real(reader, 4), CutOf(reader, 5)));
        await ReadAsync(LargeRuleBooks, reader => new StoredTrade("heavyweight_rule_holding", LargeIndex, reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)),
            Price(reader, 3)!.Value, null, null, null, Real(reader, 4), Real(reader, 5), CutOf(reader, 6)));
        await ReadAsync(IndexBooks, reader => new StoredTrade("index_heavyweight_holding", reader.GetString(0), string.Empty, reader.GetString(1), Date(reader.GetString(2)),
            Price(reader, 3)!.Value, null, null, null, Real(reader, 4), Real(reader, 5), CutOf(reader, 6)));
        await ReadAsync(IndexRuleBooks, reader => new StoredTrade("index_heavyweight_rule_holding", reader.GetString(0), reader.GetString(1), reader.GetString(2), Date(reader.GetString(3)),
            Price(reader, 4)!.Value, null, null, null, Real(reader, 5), Real(reader, 6), CutOf(reader, 7)));

        // A scored plan is stated as shares of its close, read here at a close of one and priced at the night's own close
        // as the bars hold it: its stop its risk's share under it, its target the reward its break-even states over that
        // risk, and its result its return over its risk. One whose risk is not written yet has no stop and no result.
        await ReadAsync(ScoredPlans, reader =>
        {
            if (Real(reader, 4) is not { } planned || !(planned > 0))
            {
                return new StoredTrade("forward_return", LargeIndex, reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)),
                    1, null, null, null, null, null, null, Relative: true);
            }

            var risk = planned / 100;
            double? target = Real(reader, 5) is { } share && share > 0 && share < 100 ? 1 + (risk * (1 - (share / 100)) / (share / 100)) : null;

            return new StoredTrade("forward_return", LargeIndex, reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)),
                1, 1 - risk, target, null,
                Real(reader, 3) is { } returned ? returned / 100 / risk : null, null, null, Relative: true);
        });

        return trades;
    }

    static IReadOnlyList<string> CutOf(SqliteDataReader reader, int at)
    {
        if (reader.IsDBNull(at))
        {
            return [];
        }

        using var document = JsonDocument.Parse(reader.GetString(at));

        return [.. document.RootElement.EnumerateArray()
            .Select(member => member.ValueKind == JsonValueKind.Object && member.TryGetProperty("ticker", out var ticker) ? ticker.GetString() : member.ValueKind == JsonValueKind.String ? member.GetString() : null)
            .OfType<string>()];
    }

    // The bars of every stock a due trade or its benchmark reads, on its listing nights and the sessions after them, the
    // typical moves on those nights, and the indices' membership spans.
    public static async Task<FillPrices> PricesAsync(SqliteConnection connection, IReadOnlyList<StoredTrade> due, IReadOnlyDictionary<DateOnly, DateOnly?> next, CancellationToken cancellation)
    {
        var nights = due.Select(trade => trade.Session).Distinct().ToArray();
        var sessions = nights.Concat(next.Values.OfType<DateOnly>()).Distinct().Select(Stamp).ToArray();
        var bars = new Dictionary<(string, DateOnly), (double, double)>();
        var moves = new Dictionary<(string, DateOnly), double>();
        var spans = new List<(string, string, DateOnly?, DateOnly?)>();

        foreach (var table in new[] { "kept_bar", "bar" })
        {
            await using var command = Command(connection, null, $"SELECT ticker, session_date, open, close FROM {table} WHERE session_date IN (SELECT value FROM json_each($sessions));", [("$sessions", JsonSerializer.Serialize(sessions))]);
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                bars[(reader.GetString(0), Date(reader.GetString(1)))] = (Price(reader, 2) ?? 0, Price(reader, 3) ?? 0);
            }
        }

        await using (var command = Command(connection, null, "SELECT ticker, session_date, value FROM indicator WHERE name = $name AND value IS NOT NULL AND session_date IN (SELECT value FROM json_each($nights));",
            [("$name", IndicatorSeries.Atr14), ("$nights", JsonSerializer.Serialize(nights.Select(Stamp)))]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                moves[(reader.GetString(0), Date(reader.GetString(1)))] = reader.GetDouble(2);
            }
        }

        await using (var command = Command(connection, null, Members, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                spans.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : Date(reader.GetString(2)), reader.IsDBNull(3) ? null : Date(reader.GetString(3))));
            }
        }

        return new FillPrices(bars, moves, spans, next);
    }

    static double? Price(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(at)));

    static double? Real(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);

    static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
