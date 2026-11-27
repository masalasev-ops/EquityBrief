using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Candidates;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// A waiting variant as entered: the name it registers under, the live rule it moves from, the moves and the session after
// which its live rule's orders are walked.
public sealed record WaitingVariant(long Id, string Candidate, string Live, string Evaluator, IReadOnlyDictionary<string, double> Moves, DateOnly WalkedFrom);

// The background variants a full family has no room for, on the operator's answers of 2026-11-27, item 2: each night every
// waiting variant whose family has a slot is registered through the candidate registrar, the first entered first, retiring
// nothing; every other one is walked from its live rule's own orders since it was entered, each order walked again under the
// variant's floor and written once it has ended, its picks the live rule's own, since a floor moves only whether an order
// fills. It writes no rule's trade and moves no rule, list or account.
// see: A background variant a full family has no room for is walked each night from its live rule's own orders and registers in the first slot that frees
public sealed class WaitingVariants(IClock clock, string databaseFile) : IComponent
{
    public const string Stage = "waiting-variants";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.WaitingVariant, Touch.Read),
            new StoreTouch(Store.WaitingVariantTrade, Touch.Read | Touch.Insert),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.RulePick, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.IndexRuleTrade, Touch.Read),
            new StoreTouch(Store.DipTrade, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    const string Waiting = "SELECT id, candidate, live, evaluator, moves, walked_from FROM waiting_variant ORDER BY id;";

    const string Walked = "SELECT session_date, ticker FROM waiting_variant_trade WHERE waiting_id = $id;";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string InsertTrade = @"
        INSERT INTO waiting_variant_trade (waiting_id, session_date, ticker, ended_on, filled_on, fill, result, live_result, under_floor, run_id)
        VALUES ($waiting_id, $session_date, $ticker, $ended_on, $filled_on, $fill, $result, $live_result, $under_floor, $run_id);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    // The table a live rule's orders are stored in, read by the rule's name, none for an evaluator storing no order under a
    // plan.
    public static string? OrdersOf(CandidateEvaluator evaluator) => evaluator switch
    {
        DipCandidate => "SELECT ticker, session_date, entry, stop, plan FROM dip_trade WHERE rule = $live AND session_date > $from AND plan IS NOT NULL ORDER BY session_date, ticker;",
        IndexRuleCandidate => "SELECT ticker, session_date, entry, stop, plan FROM index_rule_trade WHERE candidate = $live AND session_date > $from AND plan IS NOT NULL ORDER BY session_date, ticker;",
        SwingFilterRule => "SELECT ticker, session_date, entry, stop, plan FROM rule_pick WHERE rule = $live AND session_date > $from AND plan IS NOT NULL ORDER BY session_date, ticker;",
        FamilyRuleEvaluator => "SELECT ticker, session_date, entry, stop, plan FROM family_trade WHERE candidate = $live AND session_date > $from AND plan IS NOT NULL ORDER BY session_date, ticker;",
        _ => null,
    };

    // Whether a variant's moves are the floor alone, the one variant its live rule's own orders can be walked again under.
    public static bool FloorAlone(IReadOnlyDictionary<string, double> moves) =>
        moves.TryGetValue(PlanHook.RangeFloorParameter, out var floor) && floor > 0
        && moves.Keys.All(key => key is PlanHook.RangeFloorParameter || (key is PlanHook.RangeCeilingParameter && moves[key] == 0));

    // A live order with the variant's floor under its level, as the order its plan places with that floor would hold it: its
    // level less the floor's typical days, where the order buys at a level with no range and that price stands above nought.
    public static PlanOrder Floored(PlanOrder order, double floor) =>
        order.Range is null && order.Level is { } price && order.TypicalDay > 0 && price - (floor * order.TypicalDay) > 0
            ? order with { LevelFloor = price - (floor * order.TypicalDay) }
            : order;

    // A live order's end under the variant's floor beside its own: none while either still waits or is held where the
    // variant's has not ended; the variant's end, its fill, its result, the live rule's result on the same order and whether
    // the floor alone kept it from a fill the live rule took.
    public static (PlanEnd End, double? LiveResult, bool UnderFloor)? EndOf(PlanOrder order, double floor, decimal entry, decimal stop, DateOnly session, PlanBars bars)
    {
        if (PlanTrades.Where(Floored(order, floor), entry, stop, session, bars) is not { End: { } end })
        {
            return null;
        }

        var live = PlanTrades.Where(order, entry, stop, session, bars);

        return (end, live?.End?.Result, end.FilledOn is null && live?.FilledOn is not null);
    }

    public async Task<string> NightAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var (words, rows) = await WalkAsync(runId, cancellation);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Stamp(started));
        command.Parameters.AddWithValue("$ended_at", Stamp(clock.UtcNow));
        command.Parameters.AddWithValue("$rows", rows);
        command.Parameters.AddWithValue("$detail", words);
        await command.ExecuteNonQueryAsync(cancellation);

        return words;
    }

    async Task<(string Words, int Rows)> WalkAsync(string runId, CancellationToken cancellation)
    {
        var registrar = new CandidateRegistrar(clock, databaseFile);
        var waiting = await WaitingAsync(databaseFile, cancellation);

        if (waiting.Count == 0)
        {
            return ("no background variant waiting for a slot", 0);
        }

        var standing = CandidateFamily.Standing(await registrar.RowsAsync(cancellation), clock.UtcNow);
        var (registered, walked, stood) = (0, 0, 0);

        foreach (var variant in waiting)
        {
            if (standing.Any(row => row.Candidate == variant.Candidate))
            {
                stood++;

                continue;
            }

            if (!standing.Any(row => row.Candidate == variant.Live) || CandidateEvaluators.Find(variant.Evaluator) is not { } evaluator)
            {
                continue;
            }

            if (CandidateFamily.Maximum - CandidateFamily.In(standing, CandidateFamily.SetupFamilyOf(variant.Evaluator)).Count > 0)
            {
                var outcome = await registrar.RegisterBackgroundAsync(
                    variant.Live,
                    variant.Moves,
                    FormattableString.Invariant($"a slot freed in its family; waiting since its entry, walked from the orders after {variant.WalkedFrom:yyyy-MM-dd}"),
                    FormattableString.Invariant($"{runId}-waiting-{variant.Id}"),
                    enter: false,
                    cancellation: cancellation);

                if (outcome.Outcome == CandidateRegistrar.Registered)
                {
                    registered++;
                    standing = CandidateFamily.Standing(await registrar.RowsAsync(cancellation), clock.UtcNow);

                    continue;
                }
            }

            walked += await WalkOneAsync(variant, evaluator, runId, cancellation);
        }

        return (FormattableString.Invariant($"{waiting.Count - stood} background variant(s) waiting, {registered} registered in a slot that freed, {walked} order(s) ended under a waiting variant's floor written"), walked + registered);
    }

    // The live rule's orders since the variant's session not yet written, each walked again under its floor and written once
    // it has ended.
    async Task<int> WalkOneAsync(WaitingVariant variant, CandidateEvaluator evaluator, string runId, CancellationToken cancellation)
    {
        if (OrdersOf(evaluator) is not { } query || !variant.Moves.TryGetValue(PlanHook.RangeFloorParameter, out var floor))
        {
            return 0;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        DateOnly night;

        await using (var newest = connection.CreateCommand())
        {
            newest.CommandText = NewestSession;

            if (await newest.ExecuteScalarAsync(cancellation) is not string stored)
            {
                return 0;
            }

            night = Day(stored);
        }

        var written = new HashSet<(string, string)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Walked;
            command.Parameters.AddWithValue("$id", variant.Id);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                written.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        var orders = new List<(string Ticker, string Session, decimal Entry, decimal Stop, PlanOrder Order)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = query;
            command.Parameters.AddWithValue("$live", variant.Live);
            command.Parameters.AddWithValue("$from", Text(variant.WalkedFrom));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (!written.Contains((reader.GetString(1), reader.GetString(0))) && PlanOrder.FromJson(reader.GetString(4)) is { } order)
                {
                    orders.Add((reader.GetString(0), reader.GetString(1), Money.FromStorage(reader.GetString(2)), Money.FromStorage(reader.GetString(3)), order));
                }
            }
        }

        var count = 0;

        foreach (var (ticker, session, entry, stop, order) in orders)
        {
            var bars = PlanBars.Of(await PlanTrades.BarsAsync(connection, null, ticker, night, cancellation));

            if (EndOf(order, floor, entry, stop, Day(session), bars) is not { } ended)
            {
                continue;
            }

            await using var insert = connection.CreateCommand();

            insert.CommandText = InsertTrade;
            insert.Parameters.AddWithValue("$waiting_id", variant.Id);
            insert.Parameters.AddWithValue("$session_date", session);
            insert.Parameters.AddWithValue("$ticker", ticker);
            insert.Parameters.AddWithValue("$ended_on", Text(ended.End.On));
            insert.Parameters.AddWithValue("$filled_on", ended.End.FilledOn is { } on ? Text(on) : DBNull.Value);
            insert.Parameters.AddWithValue("$fill", ended.End.Fill is { } fill ? Money.ToStorage(Statistic.ToPrice(fill)) : DBNull.Value);
            insert.Parameters.AddWithValue("$result", ended.End.Result is { } result ? result : DBNull.Value);
            insert.Parameters.AddWithValue("$live_result", ended.LiveResult is { } live ? live : DBNull.Value);
            insert.Parameters.AddWithValue("$under_floor", ended.UnderFloor ? 1 : 0);
            insert.Parameters.AddWithValue("$run_id", runId);
            await insert.ExecuteNonQueryAsync(cancellation);
            count++;
        }

        return count;
    }

    // Every waiting variant as entered, the first entered first.
    public static async Task<IReadOnlyList<WaitingVariant>> WaitingAsync(string databaseFile, CancellationToken cancellation)
    {
        var waiting = new List<WaitingVariant>();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = Waiting;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            waiting.Add(new WaitingVariant(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                JsonSerializer.Deserialize<Dictionary<string, double>>(reader.GetString(4))!,
                Day(reader.GetString(5))));
        }

        return waiting;
    }

    static DateOnly Day(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
