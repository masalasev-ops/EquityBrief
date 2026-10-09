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

// The walk-forward tester by hand on one index: Part 0's procedures and the engines, each run inside every fold on the
// years before its test year and scored on that year against the index's current rule, the learned score alone learning
// on the ledger's setups of all three indices and tested on the index's own year, the proposals tested for one rule read
// together by the gate's step-down and each by its three screens; then the run, each proposal and each fold's test year
// written in one transaction under the drain's lock with every score it fitted. It reads the pulled history as the sweeps
// read it and writes only its own tables; it waits for no night but refuses to start inside the night's window or while a
// night holds the store.
// see: A fitted statistical model is a rule
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
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.LoopRun, Touch.Insert),
            new StoreTouch(Store.LoopProposal, Touch.Insert),
            new StoreTouch(Store.LoopTest, Touch.Insert),
            new StoreTouch(Store.LoopFinding, Touch.Insert),
            new StoreTouch(Store.LoopReading, Touch.Insert),
            new StoreTouch(Store.LoopModel, Touch.Insert),
            new StoreTouch(Store.LoopReference, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "loop-test";

    // How long a run is allowed for before the night's window, which it does not start inside.
    public static readonly TimeSpan Expected = TimeSpan.FromHours(1);

    // The indices a run reads: the S&P 400 and 600, which Part 0's procedures searched, and the S&P 500, whose proposals
    // come from the engines alone.
    public static IReadOnlyList<string> Indices { get; } = ["GSPC", "MID", "SML"];

    public const string LargeIndex = "GSPC";

    static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    const string InsertRun = @"
        INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds)
        VALUES ($run_id, $month, $index_code, $through, $started_at, $ended_at, $folds);
    ";

    const string InsertProposal = @"
        INSERT INTO loop_proposal (run_id, index_code, family, proposal, words, current_words, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, detectable, stable_folds, passed, finding, change)
        VALUES ($run_id, $index_code, $family, $proposal, $words, $current_words, $unit, $units, $blocks, $adjusted, $gate, $stable, $counted, $better, $trimmed, $counts, $detectable, $stable_folds, $passed, $finding, $change);
    ";

    const string InsertReference = @"
        INSERT INTO loop_reference (run_id, index_code, family, place, entered, edge)
        VALUES ($run_id, $index_code, $family, $place, $entered, $edge);
    ";

    const string InsertFinding = @"
        INSERT INTO loop_finding (run_id, index_code, family, figure, value, trades, words)
        VALUES ($run_id, $index_code, $family, $figure, $value, $trades, $words);
    ";

    const string InsertReading = @"
        INSERT INTO loop_reading (run_id, index_code, family, reading, units, winners, losers, winners_median, losers_median, deciles)
        VALUES ($run_id, $index_code, $family, $reading, $units, $winners, $losers, $winners_median, $losers_median, $deciles);
    ";

    const string InsertModel = @"
        INSERT INTO loop_model (run_id, index_code, family, year, learned_from, learned_before, setups, readings, parameters, hash, pin, words)
        VALUES ($run_id, $index_code, $family, $year, $learned_from, $learned_before, $setups, $readings, $parameters, $hash, $pin, $words);
    ";

    // A family's finished setups on all three indices, each with the session its path ended on, its edge in risks, the
    // stop's distance in typical moves and every reading, in the ledger's own key order.
    static readonly string FinishedSetups =
        "SELECT index_code, session_date, ended_on, edge, risk_moves, " + string.Join(", ", EquityBrief.Core.Ledger.LedgerReadings.All.Select(reading => reading.Column))
        + " FROM setup WHERE family = $family AND settled = 1 AND edge IS NOT NULL AND risk_moves IS NOT NULL AND ended_on IS NOT NULL"
        + " ORDER BY index_code, ticker, session_date;";

    const string FinishedCount = "SELECT COUNT(*) FROM setup WHERE family = $family AND settled = 1 AND edge IS NOT NULL AND risk_moves IS NOT NULL AND ended_on IS NOT NULL;";

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
            output.WriteLine($"{Verb}: name an index with '--index', GSPC, MID or SML");

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
        var large = index == LargeIndex;
        var inputs = large
            ? await history.ReadAsync(through, output.WriteLine, cancellation)
            : await history.ReadAsync(through, output.WriteLine, cancellation, index: index, asItStood: true);

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

        // Part 0's procedures on the S&P 400 and 600; then each family a rule's hooks reach, the S&P 400's and 600's
        // provisional pullback and the breakout and the drift on every index, read once and handed to the autopsy's exits,
        // to winners against losers and to the learned score, fitted on the family's finished setups of all three
        // indices; then the sector heavyweights' two exits on every index; each family's proposals read together by the
        // step-down.
        var proposals = new List<LoopProposalRead>();
        var findings = new Dictionary<string, IReadOnlyList<AutopsyFigure>>(StringComparer.Ordinal);
        var spreads = new Dictionary<string, IReadOnlyList<ReadingSpread>>(StringComparer.Ordinal);
        var lay = await HeavyweightLay.ReadAsync(read, history, through, output.WriteLine, cancellation);

        if (!large)
        {
            proposals.Add(LoopProcedures.Swing(read, BreakoutRule.Name, output.WriteLine));
            proposals.Add(LoopProcedures.Swing(read, DriftRule.Name, output.WriteLine));
            proposals.AddRange(LoopProcedures.Heavyweights(read, lay, output.WriteLine));
        }

        var rules = new List<RuleWalk>();

        if (!large)
        {
            rules.Add(RuleWalk.Pullback(read, await history.MarketAsync(through, cancellation), output.WriteLine));
        }

        rules.Add(RuleWalk.Swing(read, BreakoutRule.Name, large));
        rules.Add(RuleWalk.Swing(read, DriftRule.Name, large));

        var readings = await LoopReadings.ReadAsync(read, databaseFile, through, cancellation);
        var scores = new List<FittedScore>();

        foreach (var rule in rules)
        {
            var (exits, figures) = ExitProcedures.Autopsy(read, rule, output.WriteLine);
            var (conditions, spread) = ConditionProcedures.Run(read, rule, readings, output.WriteLine);
            var (scored, fitted) = ScoreProcedures.Run(read, rule, readings, await FinishedAsync(databaseFile, rule.Family, cancellation), output.WriteLine);

            proposals.AddRange(exits);
            proposals.AddRange(conditions);
            proposals.AddRange(scored);
            scores.AddRange(fitted);
            findings[rule.Family] = figures;
            spreads[rule.Family] = spread;
        }

        proposals.AddRange(ExitProcedures.Heavyweights(read, lay, output.WriteLine));

        var tested = Judge(read, proposals);
        var runId = FormattableString.Invariant($"{Verb}-{index}-{started:yyyyMMddTHHmmssZ}");

        if (!print)
        {
            await WriteAsync(runId, runMonth, index, through, started, read.Folds.Count, tested, cancellation, findings, spreads, scores);
        }

        foreach (var (family, figures) in findings)
        {
            output.WriteLine($"the autopsy of the {family}: " + string.Join("; ", figures.Select(figure => figure.Words)));
        }

        foreach (var score in scores)
        {
            output.WriteLine(ScoreProcedures.Line(score));
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

    // A family's finished setups on all three indices as the learned score learns on them, none on a store whose ledger
    // holds none.
    public static async Task<ScoreRows> FinishedAsync(string databaseFile, string family, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var count = connection.CreateCommand();

        count.CommandText = FinishedCount;
        count.Parameters.AddWithValue("$family", family);

        var rows = new ScoreRows(Convert.ToInt32(await count.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture));

        await using var command = connection.CreateCommand();

        command.CommandText = FinishedSetups;
        command.Parameters.AddWithValue("$family", family);

        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var readings = new double?[EquityBrief.Core.Ledger.LedgerReadings.Count];

        while (await reader.ReadAsync(cancellation))
        {
            for (var at = 0; at < readings.Length; at++)
            {
                readings[at] = reader.IsDBNull(5 + at) ? null : reader.GetDouble(5 + at);
            }

            rows.Add(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                readings,
                reader.GetDouble(3),
                reader.GetDouble(4));
        }

        return rows;
    }

    // The history an index's procedures are run over, which the fundamentals-first family's search reads too.
    internal static async Task<LoopRead> ReadAsync(string index, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, CancellationToken cancellation)
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
    public async Task WriteAsync(
        string runId,
        string month,
        string index,
        DateOnly through,
        DateTimeOffset started,
        int folds,
        IReadOnlyList<Tested> tested,
        CancellationToken cancellation = default,
        IReadOnlyDictionary<string, IReadOnlyList<AutopsyFigure>>? findings = null,
        IReadOnlyDictionary<string, IReadOnlyList<ReadingSpread>>? spreads = null,
        IReadOnlyList<FittedScore>? scores = null)
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
                ("$finding", (object?)proposal.Finding ?? DBNull.Value),
                ("$change", (object?)proposal.Change?.Json ?? DBNull.Value),
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

        // Each family's reference, the rule today's units over the test years, once a family: the first proposal of the
        // family carrying one, every proposal of a family being tested against the same rule.
        // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
        foreach (var family in tested.Select(one => one.Proposal).Where(one => one.Reference is not null).GroupBy(one => one.Family, StringComparer.Ordinal))
        {
            var place = 0;

            foreach (var unit in family.First().Reference!.OrderBy(one => one.Entered))
            {
                await ExecuteAsync(connection, transaction, InsertReference,
                [
                    ("$run_id", runId),
                    ("$index_code", index),
                    ("$family", family.Key),
                    ("$place", ++place),
                    ("$entered", unit.Entered.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                    ("$edge", unit.Edge),
                ], cancellation);
            }
        }

        foreach (var (family, figures) in findings ?? new Dictionary<string, IReadOnlyList<AutopsyFigure>>())
        {
            foreach (var figure in figures)
            {
                await ExecuteAsync(connection, transaction, InsertFinding,
                [
                    ("$run_id", runId),
                    ("$index_code", index),
                    ("$family", family),
                    ("$figure", figure.Figure),
                    ("$value", (object?)figure.Value ?? DBNull.Value),
                    ("$trades", figure.Trades),
                    ("$words", figure.Words),
                ], cancellation);
            }
        }

        foreach (var (family, spread) in spreads ?? new Dictionary<string, IReadOnlyList<ReadingSpread>>())
        {
            foreach (var one in spread)
            {
                await ExecuteAsync(connection, transaction, InsertReading,
                [
                    ("$run_id", runId),
                    ("$index_code", index),
                    ("$family", family),
                    ("$reading", one.Column),
                    ("$units", one.Units),
                    ("$winners", one.Winners),
                    ("$losers", one.Losers),
                    ("$winners_median", (object?)one.WinnersMedian ?? DBNull.Value),
                    ("$losers_median", (object?)one.LosersMedian ?? DBNull.Value),
                    ("$deciles", System.Text.Json.JsonSerializer.Serialize(one.Deciles)),
                ], cancellation);
            }
        }

        foreach (var score in scores ?? [])
        {
            await ExecuteAsync(connection, transaction, InsertModel,
            [
                ("$run_id", runId),
                ("$index_code", index),
                ("$family", score.Family),
                ("$year", score.Year),
                ("$learned_from", score.LearnedFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$learned_before", score.LearnedBefore.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$setups", score.Model.Rows),
                ("$readings", string.Join(",", score.Model.Readings.Select(reading => EquityBrief.Core.Ledger.LedgerReadings.All[reading].Column))),
                ("$parameters", score.Model.Canonical()),
                ("$hash", score.Model.Hash),
                ("$pin", RidgeScore.Version),
                ("$words", RidgeScore.Words(score.Model)),
            ], cancellation);
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
            $"{proposal.Family}, {proposal.Proposal}: {(one.Passed ? "passed" : "did not pass")}; {proposal.Words ?? "no setting chosen on all finished data"}; {gate}; better in {verdict.Better} of {verdict.Counted} counted year(s){(verdict.Stable ? string.Empty : ", short of the stability screen")}; {verdict.Units} {proposal.Unit} unit(s){(verdict.Counts ? string.Empty : ", short of the count")}; {proposal.StableFolds} of {proposal.Folds.Count} folds {(proposal.Proposal.StartsWith(RidgeScore.Proposal, StringComparison.Ordinal) ? "fitted a score" : "within a step")}");
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
