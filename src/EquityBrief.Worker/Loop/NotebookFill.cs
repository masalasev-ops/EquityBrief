using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// One source's count in a fill: the rows the source holds, those the notebook held before and those written now.
public sealed record NotebookFillCount(string Source, int Held, int Before, int Written);

// Fills the notebook with every idea the store already holds a test of: each proposal the monthly runs tested, each search
// over the long history and each test of a family's buy and sell points, a row each keyed on its source row, so a fill run
// again writes only what it has not; and the two ideas the operator added, entered waiting for the shapes the grammar does
// not hold yet. It reads the three sources and writes the notebook and its own run log row alone.
// see: Every idea from any source is tested by the one tester and kept in a notebook nothing is deleted from
public sealed class NotebookFill(IClock clock, string databaseFile)
{
    public const string Verb = "notebook-fill";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LoopRun, Touch.Read),
            new StoreTouch(Store.LoopProposal, Touch.Read),
            new StoreTouch(Store.LongSearch, Touch.Read),
            new StoreTouch(Store.PlanPoint, Touch.Read),
            new StoreTouch(Store.Notebook, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string MonthlyRun = "the monthly run";

    public const string LongSearchSource = "a search over the long history";

    public const string PlanTest = "a plan test";

    public const string Operator = "the operator";

    // The operator's two ideas, each waiting for the take-profit shape that writes it.
    public static IReadOnlyList<(string Key, string Words)> OperatorIdeas { get; } =
    [
        ("operator|three-parts", "For a stock with no earlier turned-down price above its buy: sell in three equal parts at 1.5, 2 and 3 times the risk, the stop moving up to the buy once the first part sells; for the drift, breakout and pullback on each index."),
        ("operator|keep-the-last-part", "The one close that reaches the last staged level and the half's price together: keep the last part instead of selling it all."),
    ];

    public const string WaitingForShape = "Waiting for its shape: the grammar has no take-profit shape that writes it yet.";

    const string Proposals = @"
        SELECT p.run_id, p.index_code, p.family, p.proposal, p.words, p.current_words, p.blocks, p.adjusted, p.gate, p.stable, p.counts, p.passed,
               (SELECT substr(r.started_at, 1, 10) FROM loop_run r WHERE r.run_id = p.run_id)
        FROM loop_proposal p ORDER BY p.run_id, p.index_code, p.family, p.proposal;";

    const string Searches = @"
        SELECT run_id, index_code, family, words, current_words, blocks, adjusted, gate, stable, counts, passed, through
        FROM long_search ORDER BY run_id, index_code, family;";

    const string Plans = @"
        SELECT run_id, index_code, family, combination, picks, edge_per_pick, later_fills, later_edge_per_pick, traded_later_edge_per_pick, through
        FROM plan_point ORDER BY run_id, index_code, family;";

    const string Held = "SELECT source_key FROM notebook WHERE source_key IS NOT NULL;";

    const string Insert = @"
        INSERT INTO notebook (entered_at, source, source_key, index_code, family, words, line, built_on, early_picks, early_edge, late_picks, late_edge, blocks, adjusted, bar, verdict, why, tested_on, run_id)
        VALUES ($entered_at, $source, $source_key, $index_code, $family, $words, NULL, $built_on, $early_picks, $early_edge, $late_picks, $late_edge, $blocks, $adjusted, $bar, $verdict, $why, $tested_on, $run_id);";

    const string InsertRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    sealed record Row(
        string Source,
        string Key,
        string? Index,
        string? Family,
        string Words,
        string BuiltOn,
        int? EarlyPicks,
        double? EarlyEdge,
        int? LatePicks,
        double? LateEdge,
        int? Blocks,
        double? Adjusted,
        double? Bar,
        string Verdict,
        string Why,
        string? TestedOn);

    // Why a monthly run's proposal or a search failed, in the parts the tester reads, or that it passed all four.
    public static string WhyOf(bool passed, bool chose, bool gate, bool stable, bool counts)
    {
        if (passed)
        {
            return "Passed all four parts of its test.";
        }

        if (!chose)
        {
            return "Chose no change on the years it was chosen on.";
        }

        var short_ = new[] { (gate, "its gate"), (stable, "its stability"), (counts, "its count") }.Where(part => !part.Item1).Select(part => part.Item2).ToArray();

        return short_.Length == 0 ? "Fell short of its test." : "Fell short on " + string.Join(" and ", short_) + ".";
    }

    public async Task<IReadOnlyList<NotebookFillCount>> FillAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var held = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Held;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                held.Add(reader.GetString(0));
            }
        }

        var rows = new List<Row>();

        static double? Real(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);

        static int? Whole(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : reader.GetInt32(at);

        static bool Flag(SqliteDataReader reader, int at) => !reader.IsDBNull(at) && reader.GetInt64(at) == 1;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Proposals;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (run, index, family, proposal) = (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3));
                var words = reader.IsDBNull(4) ? null : reader.GetString(4);
                var passed = Flag(reader, 11);

                rows.Add(new Row(
                    MonthlyRun,
                    $"loop_proposal|{run}|{index}|{family}|{proposal}",
                    index,
                    family,
                    $"{proposal}: {words ?? "no change chosen"}, against {(reader.IsDBNull(5) ? "the rule as it stood" : reader.GetString(5))}.",
                    $"the monthly run {run}, judged year by year from its first test year",
                    null, null, null, null,
                    Whole(reader, 6), Real(reader, 7), LoopGate.Bar,
                    passed ? IdeaTester.Passed : IdeaTester.Failed,
                    WhyOf(passed, words is not null, Flag(reader, 8), Flag(reader, 9), Flag(reader, 10)),
                    reader.IsDBNull(12) ? null : reader.GetString(12)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Searches;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (run, index, family) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
                var words = reader.IsDBNull(3) ? null : reader.GetString(3);
                var passed = Flag(reader, 10);

                rows.Add(new Row(
                    LongSearchSource,
                    $"long_search|{run}|{index}|{family}",
                    index,
                    family,
                    $"The {family}'s settings searched over the long history: {words ?? "no setting met the floors"}, against {(reader.IsDBNull(4) ? "the rule as it stood" : reader.GetString(4))}.",
                    $"the search {run}, re-run every third test year with its setting carried between",
                    null, null, null, null,
                    Whole(reader, 5), Real(reader, 6), LoopGate.Bar,
                    passed ? IdeaTester.Passed : IdeaTester.Failed,
                    WhyOf(passed, words is not null, Flag(reader, 7), Flag(reader, 8), Flag(reader, 9)),
                    reader.GetString(11)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Plans;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (run, index, family) = (reader.GetString(0), reader.GetString(1), reader.GetString(2));
                var combination = reader.IsDBNull(3) ? null : reader.GetString(3);
                var later = Real(reader, 7);
                var traded = Real(reader, 8);
                var beat = combination is not null && later is { } own && traded is { } plan && own > plan;

                rows.Add(new Row(
                    PlanTest,
                    $"plan_point|{run}|{index}|{family}",
                    index,
                    family,
                    $"The {family}'s buy and sell points: {combination ?? "no combination rests on enough fills"}.",
                    $"the test of the buy and sell points {run}, chosen on the years before each and read from 2019",
                    Whole(reader, 4), Real(reader, 5), Whole(reader, 6), later,
                    null, null, null,
                    beat ? IdeaTester.Passed : IdeaTester.Failed,
                    combination is null ? "No combination rested on enough fills."
                        : beat ? FormattableString.Invariant($"Beat the traded plan from 2019: {later:+0.00;-0.00} against {traded:+0.00;-0.00} per $1 risked.")
                        : FormattableString.Invariant($"Did not beat the traded plan from 2019: {later:+0.00;-0.00;0.00} against {traded:+0.00;-0.00;0.00} per $1 risked."),
                    reader.GetString(9)));
            }
        }

        rows.AddRange(OperatorIdeas.Select(idea => new Row(Operator, idea.Key, null, null, idea.Words, "the operator's word of 2026-11-17", null, null, null, null, null, null, null, IdeaTester.Waiting, WaitingForShape, null)));

        var counts = rows.GroupBy(row => row.Source).Select(group => new NotebookFillCount(group.Key, group.Count(), group.Count(row => held.Contains(row.Key)), group.Count(row => !held.Contains(row.Key)))).ToList();
        var entered = Stamp(clock.UtcNow);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var row in rows.Where(row => !held.Contains(row.Key)))
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$entered_at", entered);
            command.Parameters.AddWithValue("$source", row.Source);
            command.Parameters.AddWithValue("$source_key", row.Key);
            command.Parameters.AddWithValue("$index_code", (object?)row.Index ?? DBNull.Value);
            command.Parameters.AddWithValue("$family", (object?)row.Family ?? DBNull.Value);
            command.Parameters.AddWithValue("$words", row.Words);
            command.Parameters.AddWithValue("$built_on", row.BuiltOn);
            command.Parameters.AddWithValue("$early_picks", (object?)row.EarlyPicks ?? DBNull.Value);
            command.Parameters.AddWithValue("$early_edge", (object?)row.EarlyEdge ?? DBNull.Value);
            command.Parameters.AddWithValue("$late_picks", (object?)row.LatePicks ?? DBNull.Value);
            command.Parameters.AddWithValue("$late_edge", (object?)row.LateEdge ?? DBNull.Value);
            command.Parameters.AddWithValue("$blocks", (object?)row.Blocks ?? DBNull.Value);
            command.Parameters.AddWithValue("$adjusted", (object?)row.Adjusted ?? DBNull.Value);
            command.Parameters.AddWithValue("$bar", (object?)row.Bar ?? DBNull.Value);
            command.Parameters.AddWithValue("$verdict", row.Verdict);
            command.Parameters.AddWithValue("$why", row.Why);
            command.Parameters.AddWithValue("$tested_on", (object?)row.TestedOn ?? DBNull.Value);
            command.Parameters.AddWithValue("$run_id", runId);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = InsertRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Verb);
            command.Parameters.AddWithValue("$started_at", Stamp(started));
            command.Parameters.AddWithValue("$ended_at", Stamp(clock.UtcNow));
            command.Parameters.AddWithValue("$rows", counts.Sum(count => count.Written));
            command.Parameters.AddWithValue("$detail", "the notebook filled: " + string.Join("; ", counts.Select(count => FormattableString.Invariant($"{count.Source} {count.Held} held, {count.Before} before, {count.Written} written"))));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return counts;
    }

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
