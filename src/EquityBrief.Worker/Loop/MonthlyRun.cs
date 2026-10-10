using System.Globalization;
using System.Text;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// One step of the monthly run: its name and its work, which is handed the run's id and says whether it held and why.
public sealed record MonthlyStep(string Name, Func<string, CancellationToken, Task<(bool Held, string Words)>> Work);

// What a monthly run did: the steps it ran and their words, the steps an earlier try of the month had already held,
// and the step it stopped on, none where every step held.
public sealed record MonthlyOutcome(string RunId, IReadOnlyList<(string Step, string Words)> Ran, IReadOnlyList<string> Skipped, string? Stopped);

// The monthly run. On the first Saturday of a month, from a clean copy of main's commit, it runs its steps in order:
// each index's point-in-time check, the SEC's facts asked whole, each index's tester run with its engines inside it,
// and the month's report, which names for each family on each index the one proposal the run puts to the operator,
// the strongest that passed, or that none passed. Each step writes one row on the run log under a stage of its own;
// a step that does not hold stops the run with why, and a run started again for the month goes on from the first step
// no try of the month held. Before each step it waits while the night holds its lock or the night's window is open,
// and it holds the drain's lock, which the store's copy holds while it copies, for as long as the step runs. It makes
// no request and calls no model of its own; the facts step asks the SEC's archive, free, as the whole refresh does.
// see: The monthly run puts at most one proposal a family an index to the operator, from a clean copy of main's commit on the first Saturday of the month
public sealed class MonthlyRun(IClock clock, string databaseFile, string dataRoot, TextWriter output, IReadOnlyList<MonthlyStep> steps, Func<TimeSpan, CancellationToken, Task>? wait = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LoopRun, Touch.Read),
            new StoreTouch(Store.LoopProposal, Touch.Read),
            new StoreTouch(Store.LoopDecision, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "loop-month";

    // Each step's row carries this before the step's own name.
    public const string StagePrefix = "loop-month-";

    // The loop's folder under the data root, a folder a month beneath it holding the month's report.
    public const string Folder = "loop";

    public const string ReportFile = "report.txt";

    // How long a step is taken to run when the night's window is read, so none starts that would run into it.
    public static readonly TimeSpan StepEstimate = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan Poll = SetupLedger.Poll;

    public static IReadOnlyList<string> Indices { get; } = [IndexFamilies.LargeIndex, "MID", "SML"];

    // The steps' names in the order they run: each index's check, the facts, each index's tester run, and the report.
    public static IReadOnlyList<string> StepNames { get; } =
    [
        .. Indices.Select(index => "check-" + index),
        "facts",
        .. Indices.Select(index => "test-" + index),
        "report",
    ];

    public static string RunOf(string month, DateTimeOffset started) =>
        FormattableString.Invariant($"{Verb}-{month}-{started:yyyyMMddTHHmmssZ}");

    // The month a run is for where none is named: the month of the session date the clock reads.
    public static string MonthOf(IClock clock) => clock.SessionDateAt(clock.UtcNow).ToString("yyyy-MM", CultureInfo.InvariantCulture);

    // The steps any try of the month held.
    const string HeldBefore = "SELECT DISTINCT stage FROM run_log WHERE run_id LIKE $month AND stage LIKE $prefix AND outcome = 'ok';";

    const string AppendRow = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, 0, 0, 0, '0', $detail);";

    // Each index's newest tester run for the month.
    const string RunsOfTheMonth = @"
        SELECT r.index_code, r.run_id FROM loop_run r
        WHERE r.month = $month AND r.rowid = (SELECT MAX(s.rowid) FROM loop_run s WHERE s.month = r.month AND s.index_code = r.index_code);";

    // A run's proposals with the blocks the same change was last declined over on an earlier run, which hold one back.
    const string ProposalsOf = @"
        SELECT p.family, p.proposal, p.words, p.current_words, p.unit, p.units, p.blocks, p.adjusted, p.gate, p.stable, p.counted, p.better, p.trimmed, p.counts, p.detectable, p.stable_folds, p.passed, p.finding, p.change,
            (SELECT MAX(q.blocks) FROM loop_decision e
                JOIN loop_proposal q ON q.run_id = e.run_id AND q.index_code = e.index_code AND q.family = e.family AND q.proposal = e.proposal
                WHERE e.decision = 'declined' AND e.index_code = p.index_code AND e.family = p.family AND e.proposal = p.proposal AND e.run_id <> p.run_id
                    AND COALESCE(q.change, '') = COALESCE(p.change, ''))
        FROM loop_proposal p WHERE p.run_id = $run_id ORDER BY p.family, p.proposal;";

    public async Task<MonthlyOutcome> RunAsync(string month, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = RunOf(month, started);
        var pause = wait ?? Task.Delay;
        var held = await HeldAsync(month, cancellation);
        var ran = new List<(string, string)>();
        var skipped = new List<string>();

        output.WriteLine($"{Verb}: the run for {month}, {runId}");

        foreach (var step in steps)
        {
            if (held.Contains(StagePrefix + step.Name))
            {
                skipped.Add(step.Name);
                output.WriteLine($"{Verb}: {step.Name} held on an earlier try of the month, so it is not run again");

                continue;
            }

            await WaitForTheNightAsync(pause, cancellation);

            var stepStarted = clock.UtcNow;
            bool stood;
            string words;

            using (await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation))
            {
                try
                {
                    (stood, words) = await step.Work(runId, cancellation);
                }
                catch (Exception failure) when (failure is SqliteException or InvalidOperationException or IOException or ArgumentException or HttpRequestException)
                {
                    (stood, words) = (false, $"{failure.GetType().Name}: {failure.Message}");
                }
            }

            await AppendAsync(runId, StagePrefix + step.Name, stepStarted, stood ? "ok" : "failed", words, cancellation);
            ran.Add((step.Name, words));
            output.WriteLine($"{Verb}: {step.Name} {(stood ? "held" : "did not hold")}: {words}");

            if (!stood)
            {
                output.WriteLine($"{Verb}: stopped on {step.Name}; run it again for {month} to go on from that step");

                return new MonthlyOutcome(runId, ran, skipped, step.Name);
            }
        }

        output.WriteLine($"{Verb}: every step of {month} held");

        return new MonthlyOutcome(runId, ran, skipped, null);
    }

    // The month's report: for each index the tester ran on for the month, each family's one proposal put to the
    // operator, the strongest that passed, with its change in words and its figures, or that none passed; written into
    // the month's folder under the data root, a run again writing it over the one before. Says which indices hold no
    // run for the month.
    public async Task<(bool Held, string Words)> ReportAsync(string month, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var runs = new Dictionary<string, string>(StringComparer.Ordinal);

        await using (var command = Command(connection, RunsOfTheMonth, [("$month", month)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                runs[reader.GetString(0)] = reader.GetString(1);
            }
        }

        var text = new StringBuilder();
        var put = 0;

        text.AppendLine(FormattableString.Invariant($"The monthly run for {month}, written {clock.UtcNow:yyyy-MM-dd HH:mm} UTC."));
        text.AppendLine("Each family on each index puts at most one proposal to you: the strongest that passed the tester, by its adjusted p-value.");

        foreach (var index in Indices)
        {
            text.AppendLine();

            if (!runs.TryGetValue(index, out var run))
            {
                text.AppendLine($"The {DecisionCards.NameOf(index)}: no tester run for {month}.");

                continue;
            }

            var proposals = new List<LoopProposalRow>();
            var heldBack = new HashSet<string>(StringComparer.Ordinal);

            await using (var command = Command(connection, ProposalsOf, [("$run_id", run)]))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    if (!reader.IsDBNull(19) && !LoopDecisions.PutAgain(reader.GetInt32(19), reader.GetInt32(6), passedNow: true))
                    {
                        heldBack.Add(reader.GetString(0) + "|" + reader.GetString(1));
                    }

                    proposals.Add(new LoopProposalRow(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? null : reader.GetString(2),
                        reader.GetString(3),
                        reader.GetString(4),
                        reader.GetInt32(5),
                        reader.GetInt32(6),
                        reader.IsDBNull(7) ? null : reader.GetDouble(7),
                        reader.GetInt64(8) == 1,
                        reader.GetInt64(9) == 1,
                        reader.GetInt32(10),
                        reader.GetInt32(11),
                        reader.IsDBNull(12) ? null : reader.GetDouble(12),
                        reader.GetInt64(13) == 1,
                        reader.IsDBNull(14) ? null : reader.GetDouble(14),
                        reader.GetInt32(15),
                        reader.GetInt64(16) == 1,
                        reader.IsDBNull(17) ? null : reader.GetString(17),
                        reader.IsDBNull(18) ? null : reader.GetString(18)));
                }
            }

            text.AppendLine($"The {DecisionCards.NameOf(index)}, run {run}:");

            foreach (var family in proposals.GroupBy(proposal => proposal.Family).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                var passed = family.Count(proposal => proposal.Passed);

                if (LoopDecisions.PutOf(family, proposal => heldBack.Contains(proposal.Family + "|" + proposal.Proposal)) is { } one)
                {
                    put++;
                    text.AppendLine(FormattableString.Invariant(
                        $"  {family.Key}: put to you, {one.Proposal}: {one.Words ?? "no change in words"}; its adjusted p {one.Adjusted:0.####} against {LoopGate.Bar:0.####}, better in {one.Better} of {one.Counted} counted years, over {one.Units} units in {one.Blocks} blocks."));
                }
                else if (passed == 0)
                {
                    text.AppendLine(FormattableString.Invariant($"  {family.Key}: none of its {family.Count()} proposals passed."));
                }
                else
                {
                    text.AppendLine(FormattableString.Invariant($"  {family.Key}: {passed} of its {family.Count()} proposals passed and none is put to you, each stating no change or declined before over as many blocks as this run reads."));
                }
            }
        }

        var folder = Path.Combine(dataRoot, Folder, month);

        Directory.CreateDirectory(folder);

        var report = Path.Combine(folder, ReportFile);

        File.WriteAllText(report, text.ToString());

        return (true, FormattableString.Invariant($"{put} proposal(s) put to you over {runs.Count} index run(s), the report in {Folder}/{month}/{ReportFile}"));
    }

    async Task<IReadOnlySet<string>> HeldAsync(string month, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = Command(connection, HeldBefore, [("$month", $"{Verb}-{month}-%"), ("$prefix", StagePrefix + "%")]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        var held = new HashSet<string>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            held.Add(reader.GetString(0));
        }

        return held;
    }

    async Task WaitForTheNightAsync(Func<TimeSpan, CancellationToken, Task> pause, CancellationToken cancellation)
    {
        var said = false;

        while (SetupLedger.MustWait(clock.UtcNow, NightLock.Holder(dataRoot) is not null, StepEstimate))
        {
            if (!said)
            {
                output.WriteLine(NightLock.Holder(dataRoot) is { } holder ? $"{Verb}: waiting while {holder} holds the night's lock" : $"{Verb}: pausing for the night's window");
                said = true;
            }

            await pause(Poll, cancellation);
        }
    }

    async Task AppendAsync(string runId, string stage, DateTimeOffset started, string outcome, string detail, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = Command(connection, AppendRow,
        [
            ("$run_id", runId), ("$stage", stage), ("$started_at", Stamp(started)), ("$ended_at", Stamp(clock.UtcNow)),
            ("$outcome", outcome), ("$detail", detail),
        ]);

        await command.ExecuteNonQueryAsync(cancellation);
    }

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

    static string Stamp(DateTimeOffset instant) => instant.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
