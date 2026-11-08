using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The pooled step of the monthly per-rule test, after the three indices' own: each index's newest per-rule test of the
// month read with the units it kept, each family's live or provisional rules summed across the indices and each kind of
// change a family registers on two or more indices summed beside them, read by the same gate at the same bar, and each
// verdict written with the index that carried a pass. An index whose test of the month kept no units, or none of the
// month, adds nothing and is named; tests whose sessions do not line up on the one exchange calendar stop the step with
// which. It reads no history, asks nothing of the network and changes no rule: each index's own verdict stands beside it,
// and an approval still changes one index's rule.
// see: One verdict across the three indices stands beside each index's own, and an approval still changes one index's rule
public sealed class PooledTest(IClock clock, string databaseFile, string dataRoot, TextWriter output, Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LoopRule, Touch.Read),
            new StoreTouch(Store.LoopRuleUnit, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.LoopPooled, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "loop-pooled";

    // Each index's newest per-rule test of the month.
    const string RunsOfTheMonth = "SELECT index_code, run_id, MAX(rowid) FROM loop_rule WHERE month = $month AND history = $history GROUP BY index_code;";

    const string RulesOfTheRun = @"
        SELECT family, rule, standing, unit, cap, span, through, judged_from, why
        FROM loop_rule WHERE run_id = $run_id AND index_code = $index ORDER BY rowid;";

    const string UnitsOfTheRun = @"
        SELECT family, rule, session_date, place, figure, compared
        FROM loop_rule_unit WHERE run_id = $run_id AND index_code = $index ORDER BY family, rule, unit;";

    const string Register = "SELECT candidate, parameters FROM candidate_register ORDER BY id;";

    const string InsertPooled = @"
        INSERT INTO loop_pooled (run_id, month, family, kind, indices, rules, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, judged_from, carried_by, history)
        VALUES ($run_id, $month, $family, $kind, $indices, $rules, $unit, $units, $blocks, $adjusted, $gate, $stable, $counted, $better, $trimmed, $counts, $passed, $judged_from, $carried_by, $history);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows_written, 0, 0, '0', $detail);";

    // What a run read: the indices it summed with their per-rule tests, those it could not, and why.
    public sealed record Read(IReadOnlyList<PooledRule> Rules, IReadOnlyList<(string Index, string Run)> Pooled, IReadOnlyList<(string Index, string Why)> Left, string? Refused);

    // The step for a month: each verdict printed, and written with its run log row unless printing.
    public async Task<(bool Held, string Words)> RunAsync(string month, bool print = false, CancellationToken cancellation = default, string history = LongTest.Standing)
    {
        if (NightLock.Holder(dataRoot) is { } holder)
        {
            return (false, $"the night holds the store ({holder}); run it once the night has finished");
        }

        var started = clock.UtcNow;
        var read = await ReadAsync(month, cancellation, history);

        foreach (var (index, why) in read.Left)
        {
            output.WriteLine($"{Verb}: the {DecisionCards.NameOf(index)} adds nothing: {why}");
        }

        if (read.Refused is { } refused)
        {
            return (false, refused);
        }

        if (read.Pooled.Count == 0)
        {
            return (false, $"no index holds a per-rule test of {month} that kept its units, so nothing was summed");
        }

        var verdicts = PooledJudge.Judge(read.Rules);
        var runId = FormattableString.Invariant($"{Verb}-{(history == LongTest.Long ? LongTest.Long + "-" : string.Empty)}{started:yyyyMMddTHHmmssZ}");

        foreach (var verdict in verdicts)
        {
            output.WriteLine(Line(verdict));
        }

        var words = Words(verdicts, read, month);

        if (!print)
        {
            await WriteAsync(runId, month, started, verdicts, read, words, cancellation, history);
        }

        return (true, words + (print ? ", printed and not written" : string.Empty));
    }

    // The month's per-rule tests and their units, each variant's kind read from the register against its family's rule on
    // the same index.
    public async Task<Read> ReadAsync(string month, CancellationToken cancellation = default, string history = LongTest.Standing)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var runs = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var command = Command(connection, RunsOfTheMonth, [("$month", month), ("$history", history)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                runs[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var parameters = new Dictionary<string, IReadOnlyDictionary<string, double>>(StringComparer.Ordinal);

        await using (var command = Command(connection, Register, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                parameters[reader.GetString(0)] = CandidateEvaluator.Read(reader.GetString(1));
            }
        }

        var rules = new List<PooledRule>();
        var pooled = new List<(string, string)>();
        var left = new List<(string, string)>();
        var places = new Dictionary<DateOnly, (int Place, string Index)>();
        string? refused = null;

        foreach (var index in MonthlyRun.Indices)
        {
            if (!runs.TryGetValue(index, out var run))
            {
                left.Add((index, $"it holds no per-rule test of {month}"));

                continue;
            }

            var rows = new List<(string Family, string Rule, string Standing, string Unit, int? Cap, int? Span, string? Through, int? From, string? Why)>();

            await using (var command = Command(connection, RulesOfTheRun, [("$run_id", run), ("$index", index)]))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    rows.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetString(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        reader.IsDBNull(5) ? null : reader.GetInt32(5),
                        reader.IsDBNull(6) ? null : reader.GetString(6),
                        reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        reader.IsDBNull(8) ? null : reader.GetString(8)));
                }
            }

            if (rows.Count == 0 || rows.Any(row => row.Cap is null || row.Span is null || row.Through is null))
            {
                left.Add((index, $"its per-rule test {run} kept no units, being written before the pooled step"));

                continue;
            }

            var units = new Dictionary<(string Family, string Rule), List<PooledUnit>>();

            await using (var command = Command(connection, UnitsOfTheRun, [("$run_id", run), ("$index", index)]))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    var unit = new PooledUnit(
                        Day(reader.GetString(2)),
                        reader.GetInt32(3),
                        reader.IsDBNull(4) ? null : reader.GetDouble(4),
                        reader.IsDBNull(5) ? null : reader.GetDouble(5));

                    if (places.TryGetValue(unit.Session, out var placed) && placed.Place != unit.Place && refused is null)
                    {
                        refused = FormattableString.Invariant($"the per-rule tests of the {DecisionCards.NameOf(placed.Index)} and the {DecisionCards.NameOf(index)} place {unit.Session:yyyy-MM-dd} at sessions {placed.Place} and {unit.Place} from the test span's first, so their blocks do not line up on the one exchange calendar and nothing was summed");
                    }

                    places.TryAdd(unit.Session, (unit.Place, index));

                    var key = (reader.GetString(0), reader.GetString(1));

                    if (!units.TryGetValue(key, out var own))
                    {
                        units[key] = own = [];
                    }

                    own.Add(unit);
                }
            }

            pooled.Add((index, run));

            // The families the loop proposes for alone, a family the night computes no longer left as the loop leaves it.
            // see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
            foreach (var family in rows.Where(row => row.Why is null && LoopDecisions.LoopFamilies.Contains(row.Family, StringComparer.Ordinal)).GroupBy(row => row.Family, StringComparer.Ordinal))
            {
                var head = family.FirstOrDefault(row => row.Standing != RuleTests.Variant);

                foreach (var row in family)
                {
                    var isHead = row.Standing != RuleTests.Variant;
                    string? kind = null;

                    if (!isHead && head.Rule is not null && parameters.TryGetValue(head.Rule, out var live) && parameters.TryGetValue(row.Rule, out var variant))
                    {
                        kind = PooledJudge.KindOf(live, variant);
                    }

                    rules.Add(new PooledRule(
                        index,
                        row.Family,
                        row.Rule,
                        isHead,
                        row.Unit == LoopProcedures.Points,
                        row.Cap!.Value,
                        row.Span!.Value,
                        Day(row.Through!),
                        row.From,
                        units.GetValueOrDefault((row.Family, row.Rule)) ?? [],
                        kind));
                }
            }
        }

        return new Read(rules, pooled, left, refused);
    }

    // A verdict's line for the console.
    public static string Line(PooledVerdict verdict) =>
        FormattableString.Invariant(
            $"{verdict.Family}, all three indexes together, {verdict.Kind}: {(verdict.Passed ? "passed" : "did not pass")}; {(verdict.Verdict.Adjusted is { } p ? FormattableString.Invariant($"adjusted p {p:0.####} against {LoopGate.Bar:0.####}") : FormattableString.Invariant($"{verdict.Verdict.Blocks} block(s), under the floor of {EquityBrief.Core.Returns.Blocks.Floor}"))}; better in {verdict.Verdict.Better} of {verdict.Verdict.Counted} counted year(s); {verdict.Verdict.Units} unit(s) over {string.Join(", ", verdict.Indices.Select(DecisionCards.NameOf))}{(verdict.JudgedFrom is { } from ? FormattableString.Invariant($", judged from {from}") : string.Empty)}{(verdict.CarriedBy is { } carried ? $"; the {DecisionCards.NameOf(carried)} carried it" : string.Empty)}");

    static string Words(IReadOnlyList<PooledVerdict> verdicts, Read read, string month)
    {
        var own = verdicts.Where(verdict => verdict.Kind == PooledJudge.Own).ToArray();
        var kinds = verdicts.Where(verdict => verdict.Kind != PooledJudge.Own).ToArray();

        return FormattableString.Invariant(
            $"{own.Count(verdict => verdict.Passed)} of {own.Length} family verdict(s) and {kinds.Count(verdict => verdict.Passed)} of {kinds.Length} kind(s) of change passed across {string.Join(", ", read.Pooled.Select(one => DecisionCards.NameOf(one.Index)))} for {month}{(read.Left.Count > 0 ? ", " + string.Join(" and ", read.Left.Select(one => "the " + DecisionCards.NameOf(one.Index))) + " adding nothing" : string.Empty)}");
    }

    async Task WriteAsync(string runId, string month, DateTimeOffset started, IReadOnlyList<PooledVerdict> verdicts, Read read, string words, CancellationToken cancellation, string history = LongTest.Standing)
    {
        using var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(SetupLedger.Poll, cancellation), cancellation);
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        await using var transaction = connection.BeginTransaction();

        static object Or(object? value) => value ?? DBNull.Value;

        foreach (var verdict in verdicts)
        {
            var unit = read.Rules.First(rule => rule.Family == verdict.Family).Book ? LoopProcedures.Points : LoopProcedures.Risks;

            await ExecuteAsync(connection, transaction, InsertPooled,
            [
                ("$run_id", runId),
                ("$month", month),
                ("$family", verdict.Family),
                ("$kind", verdict.Kind),
                ("$indices", string.Join(" ", verdict.Indices)),
                ("$rules", JsonSerializer.Serialize(verdict.Rules)),
                ("$unit", unit),
                ("$units", verdict.Verdict.Units),
                ("$blocks", verdict.Verdict.Blocks),
                ("$adjusted", Or(verdict.Verdict.Adjusted)),
                ("$gate", verdict.Verdict.Gate ? 1 : 0),
                ("$stable", verdict.Verdict.Stable ? 1 : 0),
                ("$counted", verdict.Verdict.Counted),
                ("$better", verdict.Verdict.Better),
                ("$trimmed", Or(verdict.Verdict.Trimmed)),
                ("$counts", verdict.Verdict.Counts ? 1 : 0),
                ("$passed", verdict.Passed ? 1 : 0),
                ("$judged_from", Or(verdict.JudgedFrom)),
                ("$carried_by", Or(verdict.CarriedBy)),
                ("$history", history),
            ], cancellation);
        }

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Verb),
            ("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$rows_written", verdicts.Count),
            ("$detail", words + "; read " + string.Join(", ", read.Pooled.Select(one => one.Run))),
        ], cancellation);

        await transaction.CommitAsync(cancellation);
    }

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static SqliteCommand Command(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = Command(connection, sql, parameters);

        command.Transaction = transaction;
        await command.ExecuteNonQueryAsync(cancellation);
    }
}
