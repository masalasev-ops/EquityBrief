using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The walk-forward tester by hand on one index: Part 0's procedures, each run inside every fold on the years before its
// test year and scored on that year against the index's current rule, the proposals tested for one rule read together by
// the gate's step-down and each by its three screens; then the run, each proposal and each fold's test year written in
// one transaction under the drain's lock. It reads the pulled history as the sweeps read it and writes only its own
// three tables; it waits for no night but refuses to start inside the night's window or while a night holds the store.
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
// see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
// see: Each family on each index is proposed for, tested on and approved separately, on its own index's history alone
public sealed class WalkForwardTester(IClock clock, string databaseFile, string dataRoot, TextWriter output, Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.PulledMarketBar, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.PulledShares, Touch.Read),
            new StoreTouch(Store.PulledSplit, Touch.Read),
            new StoreTouch(Store.PulledRevenue, Touch.Read),
            new StoreTouch(Store.PulledMember, Touch.Read),
            new StoreTouch(Store.PulledIncome, Touch.Read),
            new StoreTouch(Store.PulledSnapshot, Touch.Read),
            new StoreTouch(Store.PulledHolding, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read),
            new StoreTouch(Store.LoopRun, Touch.Insert),
            new StoreTouch(Store.LoopProposal, Touch.Insert),
            new StoreTouch(Store.LoopTest, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "loop-test";

    // How long a run is allowed for before the night's window, which it does not start inside.
    public static readonly TimeSpan Expected = TimeSpan.FromHours(1);

    // The indices Part 0's procedures searched; the S&P 500's first proposals come from the engines.
    public static IReadOnlyList<string> Indices { get; } = ["MID", "SML"];

    static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    const string InsertRun = @"
        INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds)
        VALUES ($run_id, $month, $index_code, $through, $started_at, $ended_at, $folds);
    ";

    const string InsertProposal = @"
        INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed)
        VALUES ($run_id, $index_code, $family, $proposal, $words, $current_words, $unit, $units, $blocks, $adjusted, $gate, $stable, $counted, $better, $trimmed, $counts, $detectable, $stable_folds, $passed);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows_written, 0, 0, '0', $detail);";

    const string InsertTest = @"
        INSERT INTO loop_test (run_id, index_code, family, proposal, year, complete, chosen, current_units, proposed_units, current_total, proposed_total)
        VALUES ($run_id, $index_code, $family, $proposal, $year, $complete, $chosen, $current_units, $proposed_units, $current_total, $proposed_total);
    ";

    // One proposal and its verdict, as the run writes them.
    public sealed record Tested(LoopProposalRead Proposal, LoopVerdict Verdict)
    {
        public bool Passed => Verdict.Passes && Proposal.Words is not null;
    }

    // A run on an index for a month, written whole; with print, read and printed and nothing written.
    public async Task<int> RunAsync(string index, string? month, CancellationToken cancellation = default, bool print = false)
    {
        if (!Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name an index with '--index', MID or SML; the S&P 500's families have no Part 0 procedure, and their first proposals come from the engines");

            return 2;
        }

        if (month is not null && !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            output.WriteLine($"{Verb}: '--month' takes a month as yyyy-MM, not '{month}'");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var runMonth = month ?? FormattableString.Invariant($"{started:yyyy-MM}");
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation, index: index, asItStood: true);

        if (inputs.Names.Count == 0)
        {
            output.WriteLine($"{Verb}: the store holds no member of the {DecisionCards.NameOf(index)}; pull them first with 'history-pull --members --index {index}', then their history");

            return 2;
        }

        var read = await ReadAsync(index, history, through, inputs, cancellation);

        if (read.Folds.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: the history ends {through:yyyy-MM-dd}, before the first test year, {LoopFolds.FirstTestYear}"));

            return 2;
        }

        var proposals = new List<LoopProposalRead>
        {
            LoopProcedures.Swing(read, BreakoutRule.Name, output.WriteLine),
            LoopProcedures.Swing(read, DriftRule.Name, output.WriteLine),
        };
        var lay = await HeavyweightLay.ReadAsync(read, history, through, output.WriteLine, cancellation);

        proposals.AddRange(LoopProcedures.Heavyweights(read, lay, output.WriteLine));

        var tested = Judge(read, proposals);
        var runId = FormattableString.Invariant($"{Verb}-{index}-{started:yyyyMMddTHHmmssZ}");

        if (!print)
        {
            await WriteAsync(runId, runMonth, index, through, started, read.Folds.Count, tested, cancellation);
        }

        foreach (var one in tested)
        {
            output.WriteLine(Line(one));

            foreach (var (fold, chosen) in one.Proposal.Folds)
            {
                var year = one.Proposal.Evidence.Years.First(pair => pair.Year == fold.Year);

                output.WriteLine(FormattableString.Invariant($"  {fold.Year}{(fold.Complete ? string.Empty : " to date")}: {chosen ?? "no setting met the floors on its learning years"}; current {year.CurrentTotal:0.###} over {year.CurrentUnits}, proposed {year.ProposedTotal:0.###} over {year.ProposedUnits}"));
            }
        }

        output.WriteLine(print
            ? FormattableString.Invariant($"{Verb}: {tested.Count(one => one.Passed)} of {tested.Count} proposal(s) passed, printed and not written")
            : FormattableString.Invariant($"{Verb}: run {runId}, {tested.Count(one => one.Passed)} of {tested.Count} proposal(s) passed, for {runMonth}"));

        return 0;
    }

    // The history an index's procedures are run over.
    static async Task<LoopRead> ReadAsync(string index, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, CancellationToken cancellation)
    {
        var companies = await history.HeavyweightAsync(through, cancellation);
        var income = await history.IncomeAsync(through, cancellation);
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        return new LoopRead(
            index,
            inputs,
            series,
            SweepColumns.Sessions(series, calendar),
            SweepBenchmark.On(series, calendar.Length),
            companies,
            income,
            calendar,
            firstScored,
            LoopFolds.Of(calendar, through));
    }

    // Each rule's proposals judged together, in the order they were brought.
    public static IReadOnlyList<Tested> Judge(LoopRead read, IReadOnlyList<LoopProposalRead> proposals)
    {
        var verdicts = new Dictionary<LoopProposalRead, LoopVerdict>();

        foreach (var rule in proposals.GroupBy(proposal => proposal.Family, StringComparer.Ordinal))
        {
            var group = rule.ToArray();
            var judged = LoopJudge.Judge([.. group.Select(proposal => proposal.Evidence)], read.Opens, read.Newest, group.Max(proposal => proposal.Cap));

            for (var at = 0; at < group.Length; at++)
            {
                verdicts[group[at]] = judged[at];
            }
        }

        return [.. proposals.Select(proposal => new Tested(proposal, verdicts[proposal]))];
    }

    // The run, its proposals and their test years, in one transaction under the drain's lock.
    public async Task WriteAsync(string runId, string month, string index, DateOnly through, DateTimeOffset started, int folds, IReadOnlyList<Tested> tested, CancellationToken cancellation = default)
    {
        using var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(Poll, cancellation), cancellation);
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        await using var transaction = connection.BeginTransaction();

        await ExecuteAsync(connection, transaction, InsertRun,
        [
            ("$run_id", runId),
            ("$month", month),
            ("$index_code", index),
            ("$through", through.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            ("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$folds", folds),
        ], cancellation);

        foreach (var (proposal, verdict) in tested.Select(one => (one.Proposal, one.Verdict)))
        {
            await ExecuteAsync(connection, transaction, InsertProposal,
            [
                ("$run_id", runId),
                ("$index_code", index),
                ("$family", proposal.Family),
                ("$proposal", proposal.Proposal),
                ("$words", (object?)proposal.Words ?? DBNull.Value),
                ("$current_words", proposal.Current),
                ("$unit", proposal.Unit),
                ("$units", verdict.Units),
                ("$blocks", verdict.Blocks),
                ("$adjusted", (object?)verdict.Adjusted ?? DBNull.Value),
                ("$gate", verdict.Gate ? 1 : 0),
                ("$stable", verdict.Stable ? 1 : 0),
                ("$counted", verdict.Counted),
                ("$better", verdict.Better),
                ("$trimmed", (object?)verdict.Trimmed ?? DBNull.Value),
                ("$counts", verdict.Counts ? 1 : 0),
                ("$detectable", (object?)verdict.Detectable ?? DBNull.Value),
                ("$stable_folds", proposal.StableFolds),
                ("$passed", verdict.Passes && proposal.Words is not null ? 1 : 0),
            ], cancellation);

            foreach (var (fold, chosen) in proposal.Folds)
            {
                var year = proposal.Evidence.Years.First(one => one.Year == fold.Year);

                await ExecuteAsync(connection, transaction, InsertTest,
                [
                    ("$run_id", runId),
                    ("$index_code", index),
                    ("$family", proposal.Family),
                    ("$proposal", proposal.Proposal),
                    ("$year", fold.Year),
                    ("$complete", fold.Complete ? 1 : 0),
                    ("$chosen", (object?)chosen ?? DBNull.Value),
                    ("$current_units", year.CurrentUnits),
                    ("$proposed_units", year.ProposedUnits),
                    ("$current_total", year.CurrentTotal),
                    ("$proposed_total", year.ProposedTotal),
                ], cancellation);
            }
        }

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Verb),
            ("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$rows_written", 1 + tested.Count + tested.Sum(one => one.Proposal.Folds.Count)),
            ("$detail", FormattableString.Invariant($"{tested.Count(one => one.Passed)} of {tested.Count} proposal(s) passed on the {DecisionCards.NameOf(index)} for {month}, through {through:yyyy-MM-dd}, over {folds} fold(s)")),
        ], cancellation);

        await transaction.CommitAsync(cancellation);
    }

    // A proposal's line for the console.
    public static string Line(Tested one)
    {
        var (proposal, verdict) = (one.Proposal, one.Verdict);
        var gate = verdict.Adjusted is { } p ? FormattableString.Invariant($"adjusted p {p:0.####} against {LoopGate.Bar:0.####}") : FormattableString.Invariant($"{verdict.Blocks} block(s), under the floor of {EquityBrief.Core.Returns.Blocks.Floor}");

        return FormattableString.Invariant(
            $"{proposal.Family}, {proposal.Proposal}: {(one.Passed ? "passed" : "did not pass")}; {proposal.Words ?? "no setting chosen on all finished data"}; {gate}; better in {verdict.Better} of {verdict.Counted} counted year(s){(verdict.Stable ? string.Empty : ", short of the stability screen")}; {verdict.Units} {proposal.Unit} unit(s){(verdict.Counts ? string.Empty : ", short of the count")}; {proposal.StableFolds} of {proposal.Folds.Count} folds within a step");
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
}
