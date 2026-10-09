using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// What the alarm read of one family on one index: the periods it wrote tonight, the newest period it holds, and why it
// read none where it read none.
public sealed record AlarmRead(string Index, string Family, int Written, AlarmPeriod? Newest, string? Why);

public sealed record LiveAlarmOutcome(IReadOnlyList<AlarmRead> Reads)
{
    public int Written => Reads.Sum(one => one.Written);

    public int Flagged => Reads.Count(one => one.Newest?.Flagged == true);
}

// The live alarm, in the swing filter's step after the rule cards. For each index and each family the newest tester run
// on it stored a reference for, it reads the units of the family's live rule: on the S&P 500 the breakout's and the
// drift's live rule's own trades and the sector heavyweights' book's holdings, and on the S&P 400 and 600 the trades the
// index's page kept since the setting it stands at, each trade's edge after its round trip against the same plan on every
// member that night and each holding's against its size cut after its round trip. A swing family's period is a month
// and a book's a quarter, a unit counted in the period it ended in. A period is read once it has closed and every unit
// that ended in it holds its edge, against the low the reference gives at its own count, and written once, its run of
// counted periods under the low carried on from the period written before it; a rule two counted periods running under
// its low is flagged. It writes the alarm's rows and its own run log row, reads no market data, makes no request and
// calls no model.
// see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
public sealed class LiveAlarmReader(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.LoopRun, Touch.Read),
            new StoreTouch(Store.LoopReference, Touch.Read),
            new StoreTouch(Store.LoopAlarm, Touch.Read | Touch.Insert),
            new StoreTouch(Store.ProvisionalSetting, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read),
            new StoreTouch(Store.IndexFamilyTrade, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "live-alarm";

    // The indices the alarm reads, in the pages' order.
    public static IReadOnlyList<string> Indices { get; } = [WalkForwardTester.LargeIndex, "MID", "SML"];

    public const string NoRun = "no tester run is stored for the index, so no reference is read";

    const string NewestNight = "SELECT MAX(session_date) FROM bar;";

    const string NewestRun = "SELECT run_id FROM loop_run WHERE index_code = $index ORDER BY rowid DESC LIMIT 1;";

    const string ReferenceOf = "SELECT family, entered, edge FROM loop_reference WHERE run_id = $run AND index_code = $index ORDER BY family, place;";

    const string WrittenOf = "SELECT period, streak FROM loop_alarm WHERE index_code = $index AND family = $family ORDER BY period;";

    // The newest setting an approval stored for a family on an index, the instant it was set.
    const string SettingSince = @"
        SELECT set_at FROM provisional_setting WHERE index_code = $index AND family = $family ORDER BY id DESC LIMIT 1;";

    // The S&P 500's breakout's or drift's trades its registered rules kept that ended with a result.
    const string LargeTrades = @"
        SELECT candidate, ended_on, result, cost, benchmark, members FROM family_trade
        WHERE family = $family AND ended_on IS NOT NULL AND result IS NOT NULL;";

    // The S&P 500's sector heavyweights' holdings sold with a result, beside the company's value on the rebalance that
    // bought each.
    const string LargeBook = @"
        SELECT h.ended_on, h.result, h.cut_return, h.entry_close, h.exit_close,
            (SELECT n.company_value FROM heavyweight_night n WHERE n.session_date = h.entered_on AND n.ticker = h.ticker LIMIT 1)
        FROM heavyweight_holding h
        WHERE h.ended_on IS NOT NULL AND h.result IS NOT NULL AND h.cut_return IS NOT NULL;";

    // An S&P 400's or 600's page trades of a family that ended with a result, each with the session it was kept on.
    const string IndexTrades = @"
        SELECT session_date, ended_on, result, cost, benchmark, members FROM index_family_trade
        WHERE index_code = $index AND family = $family AND ended_on IS NOT NULL AND result IS NOT NULL;";

    // An S&P 400's or 600's book's holdings sold with a result.
    const string IndexBook = @"
        SELECT ended_on, result, cost, cut_return FROM index_heavyweight_holding
        WHERE index_code = $index AND ended_on IS NOT NULL AND result IS NOT NULL AND cut_return IS NOT NULL;";

    const string InsertPeriod = @"
        INSERT INTO loop_alarm (index_code, family, period, trades, edge, edge_floor, counted, under, streak, flagged, reference, run_id)
        VALUES ($index, $family, $period, $trades, $edge, $edge_floor, $counted, $under, $streak, $flagged, $reference, $run_id);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows_written, 0, 0, '0', $detail);";

    // One unit of a live rule: the session it ended on, its edge after its round trip, none where it has none, and
    // whether its edge is settled, a trade's once its benchmark is written.
    public sealed record LiveUnit(DateOnly Ended, double? Edge, bool Settled);

    public async Task<LiveAlarmOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var reads = new List<AlarmRead>();

        if (await ScalarAsync(connection, transaction, NewestNight, [], cancellation) is string newest)
        {
            var night = Date(newest);

            foreach (var index in Indices)
            {
                if (await ScalarAsync(connection, transaction, NewestRun, [("$index", index)], cancellation) is not string run)
                {
                    reads.Add(new AlarmRead(index, string.Empty, 0, null, NoRun));

                    continue;
                }

                var references = new Dictionary<string, List<AlarmUnit>>(StringComparer.Ordinal);

                await foreach (var row in RowsAsync(connection, transaction, ReferenceOf, [("$run", run), ("$index", index)], cancellation))
                {
                    (references.TryGetValue(row.GetString(0), out var held) ? held : references[row.GetString(0)] = []).Add(new AlarmUnit(Date(row.GetString(1)), row.GetDouble(2)));
                }

                foreach (var (family, reference) in references)
                {
                    var units = await UnitsAsync(connection, transaction, index, family, cancellation);

                    reads.Add(await ReadAsync(connection, transaction, index, family, run, runId, night, reference, units, cancellation));
                }
            }
        }

        var outcome = new LiveAlarmOutcome(reads);

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId), ("$stage", Stage), ("$started_at", Stamp(started)), ("$ended_at", Stamp(clock.UtcNow)),
            ("$rows_written", outcome.Written), ("$detail", Detail(outcome)),
        ], cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // What the stage's row says: each family read with its newest period, flagged where it stands flagged.
    public static string Detail(LiveAlarmOutcome outcome) =>
        outcome.Reads.Count == 0
            ? "no session is stored, so no period was read"
            : FormattableString.Invariant($"{outcome.Written} period(s) written, {outcome.Flagged} rule(s) flagged: ")
                + string.Join("; ", outcome.Reads.Select(read => read.Why is { } why
                    ? $"{read.Index}: {why}"
                    : read.Newest is { } period
                        ? FormattableString.Invariant($"{read.Index} {read.Family}, {period.Start:yyyy-MM} on {period.Units} unit(s){(period.Counted ? period.Under ? ", under its low" : ", at or above its low" : ", not counted")}{(period.Flagged ? ", flagged" : string.Empty)}")
                        : $"{read.Index} {read.Family}, no period has closed with its units settled"));

    // Every period of a family's units that has closed and settled and is not yet written, read in order and written,
    // stopping at the first that holds a unit not yet settled so no period is written before one ahead of it.
    async Task<AlarmRead> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        string family,
        string run,
        string runId,
        DateOnly night,
        IReadOnlyList<AlarmUnit> reference,
        IReadOnlyList<LiveUnit> units,
        CancellationToken cancellation)
    {
        var quarterly = family == HeavyweightRule.Name;
        var written = new HashSet<DateOnly>();
        var streak = 0;
        AlarmPeriod? newest = null;

        await foreach (var row in RowsAsync(connection, transaction, WrittenOf, [("$index", index), ("$family", family)], cancellation))
        {
            written.Add(Date(row.GetString(0)));
            streak = row.GetInt32(1);
        }

        var tonight = LiveAlarm.PeriodOf(night, quarterly);
        var count = 0;

        foreach (var period in units.GroupBy(unit => LiveAlarm.PeriodOf(unit.Ended, quarterly)).OrderBy(group => group.Key))
        {
            if (period.Key >= tonight || written.Contains(period.Key))
            {
                continue;
            }

            if (period.Any(unit => !unit.Settled))
            {
                break;
            }

            var read = LiveAlarm.Read([(period.Key, [.. period.Where(unit => unit.Edge is not null).Select(unit => unit.Edge!.Value)])], reference, streak)[0];

            await ExecuteAsync(connection, transaction, InsertPeriod,
            [
                ("$index", index), ("$family", family), ("$period", Stamp(read.Start)), ("$trades", read.Units),
                ("$edge", (object?)read.Edge ?? DBNull.Value), ("$edge_floor", (object?)read.Low ?? DBNull.Value),
                ("$counted", read.Counted ? 1 : 0), ("$under", read.Under ? 1 : 0), ("$streak", read.Streak), ("$flagged", read.Flagged ? 1 : 0),
                ("$reference", run), ("$run_id", runId),
            ], cancellation);

            streak = read.Streak;
            newest = read;
            count++;
        }

        return new AlarmRead(index, family, count, newest, null);
    }

    // A family's live units on an index, as the alarm counts them.
    async Task<IReadOnlyList<LiveUnit>> UnitsAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string family, CancellationToken cancellation)
    {
        var units = new List<LiveUnit>();

        if (index == WalkForwardTester.LargeIndex && family == HeavyweightRule.Name)
        {
            await foreach (var row in RowsAsync(connection, transaction, LargeBook, [], cancellation))
            {
                var cost = TradeCost.InPercent(row.IsDBNull(5) ? null : Money.FromStorage(row.GetString(5)), Money.FromStorage(row.GetString(3)), Money.FromStorage(row.GetString(4))) / 100.0;

                units.Add(new LiveUnit(Date(row.GetString(0)), HeavyweightRule.Edge(row.GetDouble(1) - cost, row.GetDouble(2)), true));
            }
        }
        else if (index == WalkForwardTester.LargeIndex)
        {
            await foreach (var row in RowsAsync(connection, transaction, LargeTrades, [("$family", family)], cancellation))
            {
                if (FamilyRecords.IsLive(row.GetString(0)))
                {
                    units.Add(Trade(Date(row.GetString(1)), row.GetDouble(2), Optional(row, 3), Optional(row, 4), !row.IsDBNull(5)));
                }
            }
        }
        else if (family == HeavyweightRule.Name)
        {
            await foreach (var row in RowsAsync(connection, transaction, IndexBook, [("$index", index)], cancellation))
            {
                units.Add(new LiveUnit(Date(row.GetString(0)), HeavyweightRule.Edge(row.GetDouble(1) - (Optional(row, 2) ?? 0), row.GetDouble(3)), true));
            }
        }
        else
        {
            // The trades kept since the session the rule began standing at an approved setting, where one stands.
            var since = await ScalarAsync(connection, transaction, SettingSince, [("$index", index), ("$family", family)], cancellation) is string setAt
                ? clock.SessionDateAt(DateTimeOffset.ParseExact(setAt, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal))
                : DateOnly.MinValue;

            await foreach (var row in RowsAsync(connection, transaction, IndexTrades, [("$index", index), ("$family", family)], cancellation))
            {
                if (Date(row.GetString(0)) >= since)
                {
                    units.Add(Trade(Date(row.GetString(1)), row.GetDouble(2), Optional(row, 3), Optional(row, 4), !row.IsDBNull(5)));
                }
            }
        }

        return units;
    }

    // A trade's unit: its result less its round trip and its benchmark once the benchmark is written, settled with none
    // where none could be entered.
    static LiveUnit Trade(DateOnly ended, double result, double? cost, double? benchmark, bool benchmarked) =>
        new(ended, benchmarked && benchmark is { } average ? result - (cost ?? 0) - average : null, benchmarked);

    static double? Optional(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var scalar = await command.ExecuteScalarAsync(cancellation);

        return scalar is DBNull ? null : scalar;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
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

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            yield return reader;
        }
    }
}
