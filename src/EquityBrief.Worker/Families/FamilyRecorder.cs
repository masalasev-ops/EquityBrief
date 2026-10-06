using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

public sealed record FamilyRecordsOutcome(DateOnly? Night, int Kept, int Ended, int Benchmarked, int Candidates);

// The family recorder. Keeps each registered family rule's own list and the trades on it: every trade a rule
// kept on an earlier night is walked over the closes since, ended where a close falls through its stop, reaches
// its target or ends its cap and given its result in multiples of its risk, and once its cap's sessions have
// passed given the benchmark of the same plan entered at the close on every member the index held that night
// with a bar and a typical move; then each family candidate standing when the night started lists tonight the
// members its verdicts on the family evaluator's rows passed, at most five in the family's own order, none it
// holds a trade on still open. Each rule keeps its own list and is never held by another rule's trade.
//
// It runs in the swing filter's step after the family lister. A trade is stored the night it is made and its
// result and benchmark are written once, because the bars and the typical moves they are read from are kept a
// year. It replaces the trades it listed for the night where the night is run again, makes no request and
// calls no model.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
// see: The nightly run is arithmetic only
public sealed class FamilyRecorder : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.FamilyResult, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "family-records";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // Every trade a rule kept that has not ended, or whose benchmark is not written yet.
    const string Pending = @"
        SELECT candidate, ticker, session_date, entry, stop, target, risk_moves, reward_to_risk, cap, ended_on, result, members IS NOT NULL
        FROM family_trade
        WHERE (ended_on IS NULL OR (benchmark IS NULL AND members IS NULL)) AND session_date < $night
        ORDER BY session_date, candidate, ticker;
    ";

    // Every ended trade holding a result and no cost, with its company's value as the member readings read it on the
    // trade's night, none where they read none.
    const string Unpriced = @"
        SELECT t.candidate, t.ticker, t.session_date, t.entry, t.stop, t.result,
               (SELECT m.company_value FROM member_reading m WHERE m.index_code = $index AND m.session_date = t.session_date AND m.ticker = t.ticker)
        FROM family_trade t
        WHERE t.result IS NOT NULL AND t.cost IS NULL;
    ";

    const string Priced = "UPDATE family_trade SET cost = $cost WHERE candidate = $candidate AND ticker = $ticker AND session_date = $session;";

    // The sessions the store holds from a session on, which a trade's sessions are counted in.
    const string SessionsFrom = "SELECT DISTINCT session_date FROM bar WHERE session_date >= $from ORDER BY session_date;";

    const string ClosesFrom = @"
        SELECT ticker, session_date, close
        FROM bar
        WHERE session_date >= $from
        ORDER BY ticker, session_date;
    ";

    const string TypicalMovesOn = @"
        SELECT ticker, value
        FROM indicator
        WHERE session_date = $session AND name = $typical AND value IS NOT NULL;
    ";

    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session);
    ";

    const string ShadowsOn = @"
        SELECT ticker, family, shadow
        FROM family_result
        WHERE session_date = $night AND shadow IS NOT NULL
        ORDER BY family, ticker;
    ";

    const string HeldOn = @"
        SELECT ticker
        FROM family_trade
        WHERE candidate = $candidate AND session_date < $night AND (ended_on IS NULL OR ended_on >= $night);
    ";

    const string ClearTheNight = "DELETE FROM family_trade WHERE session_date = $night;";

    const string Ended = "UPDATE family_trade SET ended_on = $ended_on, result = $result WHERE candidate = $candidate AND ticker = $ticker AND session_date = $session;";

    const string Benchmarked = "UPDATE family_trade SET benchmark = $benchmark, members = $members WHERE candidate = $candidate AND ticker = $ticker AND session_date = $session;";

    const string Insert = @"
        INSERT INTO family_trade (
            candidate, ticker, session_date, family, place, entry, stop, target, risk_moves, reward_to_risk, cap)
        VALUES (
            $candidate, $ticker, $night, $family, $place, $entry, $stop, $target, $risk_moves, $reward_to_risk, $cap);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public FamilyRecorder(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // One trade a rule kept, as the recorder reads it back.
    sealed record Kept(
        string Candidate,
        string Ticker,
        DateOnly Session,
        decimal Entry,
        decimal Stop,
        decimal? Target,
        double? RiskMoves,
        double? RewardToRisk,
        int Cap,
        DateOnly? EndedOn,
        double? Result,
        bool Benchmarked);

    public async Task<FamilyRecordsOutcome> RunAsync(string indexCode, string runId, IReadOnlyList<RegisterRow> standing, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await NewestAsync(connection, cancellation) is not { } night)
        {
            await AppendAsync(connection, null, runId, startedAt, 0, "no session is stored, so no family rule's trades were recorded", cancellation);

            return new FamilyRecordsOutcome(null, 0, 0, 0, 0);
        }

        var pending = await PendingAsync(connection, night, cancellation);
        var from = pending.Count == 0 ? night : pending.Min(trade => trade.Session);
        var calendar = await SessionsAsync(connection, from, cancellation);
        var closes = await ClosesAsync(connection, from, cancellation);
        var at = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var ended = 0;
        var benchmarked = 0;

        foreach (var trade in pending)
        {
            if (trade.EndedOn is null && Walk(trade, closes.GetValueOrDefault(trade.Ticker), calendar, at) is { } end)
            {
                await ExecuteAsync(connection, transaction, Ended, cancellation,
                    ("$ended_on", Stamp(end.On)), ("$result", end.Result is { } made ? made : DBNull.Value),
                    ("$candidate", trade.Candidate), ("$ticker", trade.Ticker), ("$session", Stamp(trade.Session)));
                ended++;
            }
        }

        // Each ended trade's round trip at the published table, written beside its result and never into it, so the rule's
        // record reads as it did and the run page states the edge after costs beside it: its company valued as the night's
        // member readings read it, one they read none for in the $1 to 2 billion band, and the sale where its result puts it.
        // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
        foreach (var (candidate, ticker, session, entry, stop, result, value) in await UnpricedAsync(connection, transaction, indexCode, cancellation))
        {
            var sale = entry + Statistic.ToPrice(result * Statistic.FromPrice(entry - stop));
            var cost = TradeCost.InRisk(value, entry, stop, Math.Max(sale, 0.01m));

            await ExecuteAsync(connection, transaction, Priced, cancellation,
                ("$cost", double.IsNaN(cost) ? DBNull.Value : cost),
                ("$candidate", candidate), ("$ticker", ticker), ("$session", Stamp(session)));
        }

        // A benchmark is written once every member's trade of the same plan has had its cap's sessions.
        foreach (var listedOn in pending.Where(trade => !trade.Benchmarked && SessionsAfter(trade.Session, at, calendar) >= trade.Cap).GroupBy(trade => trade.Session))
        {
            var members = await MembersOnAsync(connection, indexCode, listedOn.Key, cancellation);
            var moves = await TypicalMovesAsync(connection, listedOn.Key, cancellation);

            foreach (var trade in listedOn)
            {
                var (benchmark, count) = Benchmark(trade, members, moves, closes);

                await ExecuteAsync(connection, transaction, Benchmarked, cancellation,
                    ("$benchmark", benchmark is { } value ? value : DBNull.Value), ("$members", count),
                    ("$candidate", trade.Candidate), ("$ticker", trade.Ticker), ("$session", Stamp(trade.Session)));
                benchmarked++;
            }
        }

        await ExecuteAsync(connection, transaction, ClearTheNight, cancellation, ("$night", Stamp(night)));

        var verdicts = await ShadowsAsync(connection, transaction, night, cancellation);
        var kept = 0;
        var said = new List<string>();

        // The S&P 500's family rules alone: an S&P 400's or 600's rule keeps its list in the index families' step.
        foreach (var rule in standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is FamilyRuleEvaluator and not IndexRuleCandidate))
        {
            var family = ((FamilyRuleEvaluator)CandidateEvaluators.Find(rule.Evaluator)!).Family;
            var cap = SetupFamilies.Named(family)?.CapSessions ?? 0;
            var held = await HeldAsync(connection, transaction, rule.Candidate, night, cancellation);
            var listed = Keep(
                verdicts.Where(verdict => verdict.Family == family && verdict.Outcome.Candidate == rule.Candidate).Select(verdict => (verdict.Ticker, verdict.Outcome)),
                held);

            foreach (var (ticker, place, plan) in listed)
            {
                await ExecuteAsync(connection, transaction, Insert, cancellation,
                    ("$candidate", rule.Candidate), ("$ticker", ticker), ("$night", Stamp(night)), ("$family", family), ("$place", place),
                    ("$entry", Money.ToStorage(plan.Entry)), ("$stop", Money.ToStorage(plan.Stop)),
                    ("$target", plan.Target is { } target ? Money.ToStorage(target) : DBNull.Value),
                    ("$risk_moves", plan.RiskMoves is { } risk ? risk : DBNull.Value),
                    ("$reward_to_risk", plan.RewardToRisk is { } ratio ? ratio : DBNull.Value),
                    ("$cap", cap));
            }

            kept += listed.Count;
            said.Add(FormattableString.Invariant($"'{rule.Candidate}' kept {listed.Count}"));
        }

        var detail = FormattableString.Invariant($"for {night:yyyy-MM-dd}: {kept} trade(s) kept by {said.Count} registered family rule(s), {ended} ended and {benchmarked} benchmarked")
            + (said.Count == 0 ? "; no family rule stands registered" : "; " + string.Join("; ", said));

        await AppendAsync(connection, transaction, runId, startedAt, kept + ended + benchmarked, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new FamilyRecordsOutcome(night, kept, ended, benchmarked, said.Count);
    }

    // The trades one rule keeps on a night from its own verdicts: the members it fired on in the family's own
    // order, the order's figure and then the second figure each largest first and then the ticker, at most five,
    // none it holds a trade on still open and none whose values place no plan. The night's record keeps its list
    // by it, and a replay of the rule keeps the list the same way.
    public static IReadOnlyList<(string Ticker, int Place, (decimal Entry, decimal Stop, decimal? Target, double? RiskMoves, double? RewardToRisk) Plan)> Keep(
        IEnumerable<(string Ticker, ShadowOutcome Outcome)> verdicts,
        IReadOnlySet<string> held)
    {
        var kept = new List<(string, int, (decimal, decimal, decimal?, double?, double?))>();

        foreach (var fire in verdicts
            .Where(verdict => verdict.Outcome.Fired)
            .OrderByDescending(verdict => Number(verdict.Outcome.Values, FamilyRuleEvaluator.OrderValue) ?? double.MinValue)
            .ThenByDescending(verdict => Number(verdict.Outcome.Values, FamilyRuleEvaluator.ThenByValue) ?? double.MinValue)
            .ThenBy(verdict => verdict.Ticker, StringComparer.Ordinal))
        {
            if (kept.Count == SetupFamilies.ListedANight)
            {
                break;
            }

            if (held.Contains(fire.Ticker) || Plan(fire.Outcome.Values) is not { } plan)
            {
                continue;
            }

            kept.Add((fire.Ticker, kept.Count + 1, plan));
        }

        return kept;
    }

    // The plan a verdict's values state: the buy, the stop, the target where the plan names one, the stop's
    // distance in the night's typical moves and the reward to risk; none where the values place no stop below
    // the buy.
    public static (decimal Entry, decimal Stop, decimal? Target, double? RiskMoves, double? RewardToRisk)? Plan(IReadOnlyDictionary<string, string> values)
    {
        if (Price(values, FamilyRuleEvaluator.EntryValue) is not { } entry || Price(values, FamilyRuleEvaluator.StopValue) is not { } stop || stop >= entry || stop <= 0)
        {
            return null;
        }

        var target = Price(values, FamilyRuleEvaluator.TargetValue);
        var risk = Statistic.FromPrice(entry - stop);
        var move = Number(values, FamilyRuleEvaluator.MoveValue);

        return (
            entry,
            stop,
            target,
            move is { } typical && typical > 0 ? risk / typical : null,
            target is { } aimed ? Statistic.FromPrice(aimed - entry) / risk : null);
    }

    // Where a kept trade stands over the closes since its night: ended, with its result in multiples of its
    // risk, at the first close through its stop, at its target or at its cap's close; ended with no result once
    // its cap's sessions have passed with the stock's closes run out before it; and still open otherwise. The
    // plan is read at the scale of the closes as they are stored now, so an adjustment since the night moves
    // the buy, the stop and the target together.
    static (DateOnly On, double? Result)? Walk(Kept trade, IReadOnlyList<(DateOnly Session, double Close)>? series, IReadOnlyList<DateOnly> calendar, IReadOnlyDictionary<DateOnly, int> at) =>
        Walk(trade.Session, trade.Entry, trade.Stop, trade.Target, trade.Cap, series, calendar, at);

    public static (DateOnly On, double? Result)? Walk(
        DateOnly session,
        decimal buy,
        decimal stopAt,
        decimal? targetAt,
        int cap,
        IReadOnlyList<(DateOnly Session, double Close)>? series,
        IReadOnlyList<DateOnly> calendar,
        IReadOnlyDictionary<DateOnly, int> at)
    {
        var index = series is null ? -1 : IndexOf(series, session);

        if (series is not null && index >= 0)
        {
            var closes = series.Select(bar => bar.Close).ToArray();
            var scale = closes[index] / Statistic.FromPrice(buy);
            var entry = closes[index];
            var stop = Statistic.FromPrice(stopAt) * scale;
            double? result = targetAt is { } target
                ? FamilyWalks.Fixed(closes, index, entry, stop, Statistic.FromPrice(target) * scale, cap, out var sessions)
                : FamilyWalks.Trailing(closes, index, entry, stop, entry - stop, cap, out sessions);

            if (result is not null)
            {
                return (series[index + sessions].Session, result);
            }
        }

        // The stock's closes ran out before its trade ended: once its cap's sessions have passed it is ended,
        // with no result, and frees the stock from the night after.
        return SessionsAfter(session, at, calendar) >= cap && at.TryGetValue(session, out var from) && from + cap < calendar.Count
            ? (calendar[from + cap], null)
            : null;
    }

    // The same plan entered at the close on every member the index held on the trade's night with a bar and a
    // typical move: the stop the trade's distance in each member's own typical moves beneath, and the target
    // the trade's reward to risk above, or the stop trailing at that distance where the plan names no target;
    // the average result of those whose trade the stored closes reach the end of.
    static (double? Benchmark, int Members) Benchmark(Kept trade, IReadOnlySet<string> members, IReadOnlyDictionary<string, double> moves, IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, double Close)>> closes)
    {
        if (trade.RiskMoves is not { } riskMoves || riskMoves <= 0)
        {
            return (null, 0);
        }

        var sum = 0.0;
        var count = 0;

        foreach (var member in members.OrderBy(ticker => ticker, StringComparer.Ordinal))
        {
            if (!moves.TryGetValue(member, out var move) || move <= 0 || !closes.TryGetValue(member, out var series))
            {
                continue;
            }

            var index = IndexOf(series, trade.Session);

            if (index < 0)
            {
                continue;
            }

            var held = series.Select(bar => bar.Close).ToArray();
            var entry = held[index];
            var risk = riskMoves * move;

            if (!(risk > 0) || entry - risk <= 0)
            {
                continue;
            }

            var result = trade.Target is not null && trade.RewardToRisk is { } ratio
                ? FamilyWalks.Fixed(held, index, entry, entry - risk, entry + (ratio * risk), trade.Cap, out _)
                : trade.Target is null
                    ? FamilyWalks.Trailing(held, index, entry, entry - risk, risk, trade.Cap, out _)
                    : null;

            if (result is { } made)
            {
                sum += made;
                count++;
            }
        }

        return (count > 0 ? sum / count : null, count);
    }

    static int IndexOf(IReadOnlyList<(DateOnly Session, double Close)> series, DateOnly session)
    {
        for (var index = 0; index < series.Count; index++)
        {
            if (series[index].Session == session)
            {
                return index;
            }
        }

        return -1;
    }

    // The sessions the store holds after a trade's night, up to and including the newest.
    static int SessionsAfter(DateOnly session, IReadOnlyDictionary<DateOnly, int> at, IReadOnlyList<DateOnly> calendar) =>
        at.TryGetValue(session, out var index) ? calendar.Count - 1 - index : 0;

    static decimal? Price(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var held) && decimal.TryParse(held, NumberStyles.Number, CultureInfo.InvariantCulture, out var price) ? price : null;

    static double? Number(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var held) && double.TryParse(held, NumberStyles.Float, CultureInfo.InvariantCulture, out var figure) ? figure : null;

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
    }

    static async Task<IReadOnlyList<(string Candidate, string Ticker, DateOnly Session, decimal Entry, decimal Stop, double Result, decimal? Value)>> UnpricedAsync(SqliteConnection connection, SqliteTransaction transaction, string indexCode, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = Unpriced;
        command.Parameters.AddWithValue("$index", indexCode);

        var unpriced = new List<(string, string, DateOnly, decimal, decimal, double, decimal?)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            unpriced.Add((
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.GetDouble(5),
                reader.IsDBNull(6) ? null : Money.FromStorage(reader.GetString(6))));
        }

        return unpriced;
    }

    static async Task<IReadOnlyList<Kept>> PendingAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Pending;
        command.Parameters.AddWithValue("$night", Stamp(night));

        var trades = new List<Kept>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            trades.Add(new Kept(
                reader.GetString(0),
                reader.GetString(1),
                Date(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetInt32(8),
                reader.IsDBNull(9) ? null : Date(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.GetInt64(11) == 1));
        }

        return trades;
    }

    internal static async Task<IReadOnlyList<DateOnly>> SessionsAsync(SqliteConnection connection, DateOnly from, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SessionsFrom;
        command.Parameters.AddWithValue("$from", Stamp(from));

        var sessions = new List<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            sessions.Add(Date(reader.GetString(0)));
        }

        return sessions;
    }

    internal static async Task<IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, double Close)>>> ClosesAsync(SqliteConnection connection, DateOnly from, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ClosesFrom;
        command.Parameters.AddWithValue("$from", Stamp(from));

        var closes = new Dictionary<string, List<(DateOnly Session, double Close)>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!closes.TryGetValue(ticker, out var held))
            {
                closes[ticker] = held = [];
            }

            held.Add((Date(reader.GetString(1)), Statistic.FromPrice(Money.FromStorage(reader.GetString(2)))));
        }

        return closes.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<(DateOnly Session, double Close)>)entry.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<string, double>> TypicalMovesAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = TypicalMovesOn;
        command.Parameters.AddWithValue("$session", Stamp(session));
        command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);

        var moves = new Dictionary<string, double>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            moves[reader.GetString(0)] = reader.GetDouble(1);
        }

        return moves;
    }

    static async Task<IReadOnlySet<string>> MembersOnAsync(SqliteConnection connection, string indexCode, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", Stamp(session));

        var members = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<IReadOnlyList<(string Ticker, string Family, ShadowOutcome Outcome)>> ShadowsAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = ShadowsOn;
        command.Parameters.AddWithValue("$night", Stamp(night));

        var verdicts = new List<(string Ticker, string Family, ShadowOutcome Outcome)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);
            var family = reader.GetString(1);

            verdicts.AddRange(FamilyRuleShadow.Read(reader.GetString(2)).Select(outcome => (ticker, family, outcome)));
        }

        return verdicts;
    }

    static async Task<IReadOnlySet<string>> HeldAsync(SqliteConnection connection, SqliteTransaction transaction, string candidate, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = HeldOn;
        command.Parameters.AddWithValue("$candidate", candidate);
        command.Parameters.AddWithValue("$night", Stamp(night));

        var held = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held.Add(reader.GetString(0));
        }

        return held;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellation, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction? transaction, string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
