using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
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
public sealed partial class WalkForwardTester(IClock clock, string databaseFile, string dataRoot, TextWriter output, Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
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
            new StoreTouch(Store.LoopModel, Touch.Read | Touch.Insert),
            new StoreTouch(Store.LoopReference, Touch.Insert),
            new StoreTouch(Store.LoopRule, Touch.Insert),
            new StoreTouch(Store.LoopRuleYear, Touch.Insert),
            new StoreTouch(Store.LoopLink, Touch.Insert),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.ProvisionalSetting, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.ShapeProposal, Touch.Read),
            new StoreTouch(Store.LoopIdea, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "loop-test";

    // The verb the idea generator runs under, whose prompts the tester's own read of an index makes.
    public const string IdeasVerb = "loop-ideas";

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
        INSERT INTO loop_model (run_id, index_code, family, kind, year, learned_from, learned_before, setups, readings, parameters, hash, pin, words)
        VALUES ($run_id, $index_code, $family, $kind, $year, $learned_from, $learned_before, $setups, $readings, $parameters, $hash, $pin, $words);
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

    // One proposal and its verdict, as the run writes them; from 17.15 a proposal of the learned score's challenger
    // passes only where its total edge is above the ridge score's proposal of its kind.
    public sealed record Tested(LoopProposalRead Proposal, LoopVerdict Verdict, bool? BeatsRidge = null)
    {
        public bool Passed => Verdict.Passes && Proposal.Words is not null && BeatsRidge != false;
    }

    // A run on an index for a month, written whole; with print, read and printed and nothing written.
    public Task<int> RunAsync(string index, string? month, CancellationToken cancellation = default, bool print = false) =>
        RunAsync(index, month, print, null, cancellation);

    // The prompts the idea generator puts for an index: each family rule's in each fold and over all finished data, made
    // from the history, the rules and the readings a run reads, with no procedure run and nothing written; none where the
    // run would refuse to start.
    // see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
    public async Task<IReadOnlyList<IdeaAsk>?> IdeaAsksAsync(string index, CancellationToken cancellation = default)
    {
        var asks = new List<IdeaAsk>();

        return await RunAsync(index, null, true, asks, cancellation) == 0 ? asks : null;
    }

    async Task<int> RunAsync(string index, string? month, bool print, List<IdeaAsk>? asks, CancellationToken cancellation)
    {
        var verb = asks is null ? Verb : IdeasVerb;

        if (!Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{verb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (month is not null && !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            output.WriteLine($"{verb}: '--month' takes a month as yyyy-MM, not '{month}'");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

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
            output.WriteLine($"{verb}: the store holds no member of the {DecisionCards.NameOf(index)}; pull them first with 'history-pull --members --index {index}', then their history");

            return 2;
        }

        var read = await ReadAsync(index, history, through, inputs, cancellation);

        if (read.Folds.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"{verb}: the history ends {through:yyyy-MM-dd}, before the first test year, {LoopFolds.FirstTestYear}"));

            return 2;
        }

        // Part 0's procedures on the S&P 400 and 600; then each family a rule's hooks reach, the S&P 400's and 600's
        // provisional pullback, the S&P 500's swing filter with its shape clock's moves, and the breakout and the drift on
        // every index, read once and handed to the autopsy's exits,
        // to winners against losers, to the idea generator's stored answers and to the learned score, fitted on the
        // family's finished setups of all three indices; then the sector heavyweights' two exits on every index; each
        // family's proposals read together by the step-down. The generator's prompts are the rules' own and read no
        // procedure, so the generator's read runs none.
        var procedures = asks is null;
        var proposals = new List<LoopProposalRead>();
        var findings = new Dictionary<string, IReadOnlyList<AutopsyFigure>>(StringComparer.Ordinal);
        var spreads = new Dictionary<string, IReadOnlyList<ReadingSpread>>(StringComparer.Ordinal);
        var lay = procedures ? await HeavyweightLay.ReadAsync(read, history, through, output.WriteLine, cancellation) : null;

        // Each family is tested against the rule as it stands: at the setting an approval stored for it on its index where
        // one did, the S&P 500's breakout and drift at their own setting with the hooks it stores, and at its provisional
        // or its own setting otherwise. A family an approval set hooks on is not searched on its grid, whose walk reads no
        // hook.
        // see: An approved change is applied by the night's own build on the index it was approved on alone
        // see: An approved change reaches the S&P 500's breakout and drift through the stored settings and a registration, and the rule it replaces runs beside it for 63 sessions
        var standing = await StandingAsync(databaseFile, index, cancellation);

        if (!large && procedures)
        {
            foreach (var family in new[] { BreakoutRule.Name, DriftRule.Name })
            {
                if (standing.TryGetValue(family, out var stood) && stood.Hooks.Count > 0)
                {
                    output.WriteLine($"{Verb}: the {family}'s grid is not searched on the {DecisionCards.NameOf(index)}, since the rule stands at {stood.Words()} and the grid's walk reads no hook");

                    continue;
                }

                proposals.Add(LoopProcedures.Swing(read, family, output.WriteLine, standing.GetValueOrDefault(family)));
            }

            proposals.AddRange(LoopProcedures.Heavyweights(read, lay!, output.WriteLine));
        }

        var readings = await LoopReadings.ReadAsync(read, databaseFile, through, cancellation);
        var trees = await StoredTreesAsync(databaseFile, cancellation);
        var rules = new List<RuleWalk>();
        var market = await history.MarketAsync(through, cancellation);
        IdeaReplay? baseReplay = null;

        if (!large)
        {
            baseReplay = SweepIdeasRunner.Read(read.Inputs, market, output.WriteLine).Replay;
            rules.Add(RuleWalk.Pullback(read, market, output.WriteLine, standing.GetValueOrDefault(SetupFamilies.Pullback), readings, trees, candidates: baseReplay));
        }
        else if (await OpenVersionAsync(databaseFile, cancellation) is { } open)
        {
            // The S&P 500's pullback, the swing filter at its open version's settings read by value with the hooks an
            // approval stored, and each move of the newest shape proposal written for that version a proposal of the run.
            // see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
            var (replay, _, candidates) = SweepIdeasRunner.ReadWithCandidates(read.Inputs, market, output.WriteLine);
            var pullbacks = new LargePullback(replay.Series, candidates);

            baseReplay = replay;

            RuleWalk WalkAt(FilterSettings settings) =>
                RuleWalk.LargeSwingFilter(read, pullbacks, open.Version, settings, output.WriteLine, standing.GetValueOrDefault(SetupFamilies.Pullback), readings, trees);

            var filter = WalkAt(open.Settings);

            rules.Add(filter);

            if (procedures && await ShapeMovesAsync(databaseFile, open.Version, cancellation) is { } shape)
            {
                proposals.AddRange(ShapeProcedures.Run(read, filter, WalkAt, open.Settings, shape.Moves, shape.Id, output.WriteLine));
            }
        }

        rules.Add(RuleWalk.Swing(read, BreakoutRule.Name, large, standing.GetValueOrDefault(BreakoutRule.Name), readings, trees));
        rules.Add(RuleWalk.Swing(read, DriftRule.Name, large, standing.GetValueOrDefault(DriftRule.Name), readings, trees));

        // The fundamentals-first family on every index, at the setting an approval stored for it or its provisional one,
        // over the pullback's base listings read once: its 27 settings a procedure where the rule stands at no hook, and
        // the rule as it stands handed to the engines.
        // see: The fundamentals-first family is proposed for on every index over its 27 settings, and an approved setting is read by the night in the provisional one's place
        var fundamentalsStanding = standing.GetValueOrDefault(FundamentalsRule.Name);
        var fundamentalsSetting = FundamentalsRule.StoredOr(fundamentalsStanding?.Places);
        baseReplay ??= SweepIdeasRunner.Read(read.Inputs, market, output.WriteLine).Replay;

        var baseListings = RuleWalk.BaseListings(read, baseReplay);
        var fundamentals = RuleWalk.Fundamentals(read, baseReplay.Series, baseListings, readings, fundamentalsSetting, output.WriteLine, fundamentalsStanding, trees);

        if (procedures && fundamentalsStanding is { Hooks.Count: > 0 })
        {
            output.WriteLine($"{Verb}: the {FundamentalsRule.Name}'s grid is not searched on the {DecisionCards.NameOf(index)}, since the rule stands at {fundamentalsStanding.Words()} and the grid's walk reads no hook");
        }
        else if (procedures)
        {
            output.WriteLine(FormattableString.Invariant($"walking the {FundamentalsRule.Name}'s {FundamentalsSearch.Grid.Settings.Count} setting(s) on the {DecisionCards.NameOf(index)} over the whole history"));
            proposals.Add(LoopProcedures.Fundamentals(
                read,
                [.. FundamentalsSearch.Grid.Settings.Select(setting => RuleWalk.Fundamentals(read, baseReplay.Series, baseListings, readings, FundamentalsRule.At(setting), _ => { }).Own)],
                fundamentals.Own,
                fundamentalsSetting,
                fundamentals.Cap));
        }

        rules.Add(fundamentals);

        if (asks is not null)
        {
            foreach (var rule in rules)
            {
                asks.AddRange(IdeaProcedures.Prompts(read, rule, readings));
            }

            output.WriteLine(FormattableString.Invariant($"{verb}: {asks.Count} prompt(s) over {rules.Count} rule(s) on the {DecisionCards.NameOf(index)}, through {through:yyyy-MM-dd}, over {read.Folds.Count} fold(s)"));

            return 0;
        }

        var scores = new List<FittedScore>();
        var answered = await StoredIdeasAsync(databaseFile, cancellation);

        foreach (var rule in rules)
        {
            var (exits, figures) = ExitProcedures.Autopsy(read, rule, output.WriteLine);
            var (conditions, spread) = ConditionProcedures.Run(read, rule, readings, output.WriteLine);
            var ideas = IdeaProcedures.Run(read, rule, readings, answered, output.WriteLine);
            var (scored, fitted) = ScoreProcedures.Run(read, rule, readings, await FinishedAsync(databaseFile, rule.Family, cancellation), output.WriteLine);

            proposals.AddRange(exits);
            proposals.AddRange(conditions);
            proposals.AddRange(ideas);
            proposals.AddRange(scored);
            scores.AddRange(fitted);
            findings[rule.Family] = figures;
            spreads[rule.Family] = spread;
        }

        proposals.AddRange(ExitProcedures.Heavyweights(read, lay!, output.WriteLine));

        var tested = ScoreProcedures.AgainstRidge(Judge(read, proposals));
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

    // The verb the monthly per-rule test runs under.
    public const string RulesVerb = "loop-rules";

    const string ReadRegister = @"
        SELECT id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence
        FROM candidate_register ORDER BY id;
    ";

    const string InsertRule = @"
        INSERT INTO loop_rule (run_id, month, index_code, family, rule, standing, words, against, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, why)
        VALUES ($run_id, $month, $index_code, $family, $rule, $standing, $words, $against, $unit, $units, $blocks, $adjusted, $gate, $stable, $counted, $better, $trimmed, $counts, $passed, $why);
    ";

    const string InsertRuleYear = @"
        INSERT INTO loop_rule_year (run_id, index_code, family, rule, year, complete, units, total, compared_units, compared_total)
        VALUES ($run_id, $index_code, $family, $rule, $year, $complete, $units, $total, $compared_units, $compared_total);
    ";

    // A row of which readings go with winners, which the readings' step writes with its run.
    const string InsertLink = @"
        INSERT INTO loop_link (run_id, month, index_code, family, reading, setups, years, correlation, same_way, shuffled, words)
        VALUES ($run_id, $month, $index_code, $family, $reading, $setups, $years, $correlation, $same_way, $shuffled, $words);
    ";

    // The monthly per-rule test on an index for a month: every family rule standing on the index when the run begins
    // walked at its own settings over the history and judged, each variant against its live or provisional rule and each
    // of those against its benchmark, written whole; with print, printed and nothing written. It adopts and retires
    // nothing.
    // see: Every registered rule is tested each month on the test years, each variant against its live rule and each live rule against its benchmark
    public async Task<int> RulesAsync(string index, string? month, CancellationToken cancellation = default, bool print = false)
    {
        if (!Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{RulesVerb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (month is not null && !DateOnly.TryParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            output.WriteLine($"{RulesVerb}: '--month' takes a month as yyyy-MM, not '{month}'");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{RulesVerb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{RulesVerb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

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
            output.WriteLine($"{RulesVerb}: the store holds no member of the {DecisionCards.NameOf(index)}; pull them first with 'history-pull --members --index {index}', then their history");

            return 2;
        }

        var read = await ReadAsync(index, history, through, inputs, cancellation);

        if (read.Folds.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"{RulesVerb}: the history ends {through:yyyy-MM-dd}, before the first test year, {LoopFolds.FirstTestYear}"));

            return 2;
        }

        var market = await history.MarketAsync(through, cancellation);
        (string, FilterSettings, LargePullback)? filter = null;
        IdeaReplay? pullbacks = null;

        if (large && await OpenVersionAsync(databaseFile, cancellation) is { } open)
        {
            var (replay, _, candidates) = SweepIdeasRunner.ReadWithCandidates(read.Inputs, market, output.WriteLine);

            filter = (open.Version, open.Settings, new LargePullback(replay.Series, candidates));
            pullbacks = replay;
        }

        var rules = RuleTests.Walk(new RuleTestInputs(
            read,
            Core.Candidates.ShadowColumn.StandingAt(await RegisterAsync(databaseFile, cancellation), started),
            await StandingAsync(databaseFile, index, cancellation),
            market,
            await HeavyweightLay.ReadAsync(read, history, through, output.WriteLine, cancellation),
            await LoopReadings.ReadAsync(read, databaseFile, through, cancellation),
            await StoredTreesAsync(databaseFile, cancellation),
            filter,
            output.WriteLine,
            pullbacks));
        var tested = RuleTests.Judge(read, rules);
        var runId = FormattableString.Invariant($"{RulesVerb}-{index}-{started:yyyyMMddTHHmmssZ}");

        if (!print)
        {
            await WriteRulesAsync(runId, runMonth, index, through, started, read.Folds.Count, tested, cancellation);
        }

        foreach (var one in tested)
        {
            output.WriteLine(RuleLine(one));
        }

        output.WriteLine(print
            ? FormattableString.Invariant($"{RulesVerb}: {RulesDetail(tested, index, runMonth, through, read.Folds.Count)}, printed and not written")
            : FormattableString.Invariant($"{RulesVerb}: run {runId}, {RulesDetail(tested, index, runMonth, through, read.Folds.Count)}"));

        return 0;
    }

    static string RulesDetail(IReadOnlyList<RuleTested> tested, string index, string month, DateOnly through, int folds) =>
        FormattableString.Invariant($"{tested.Count(one => one.Passed)} of {tested.Count} rule(s) passed on the {DecisionCards.NameOf(index)} for {month} and {tested.Count(one => one.Why is not null)} not testable, through {through:yyyy-MM-dd}, over {folds} fold(s)");

    // A rule's line for the console.
    public static string RuleLine(RuleTested one) =>
        one.Why is { } why
            ? $"{one.Rule.Family}, {one.Rule.Rule}: not testable, {why}"
            : FormattableString.Invariant($"{one.Rule.Family}, {one.Rule.Rule}: {(one.Passed ? "passed" : "did not pass")} against {one.Against}; {(one.Verdict!.Adjusted is { } p ? FormattableString.Invariant($"adjusted p {p:0.####} against {LoopGate.Bar:0.####}") : FormattableString.Invariant($"{one.Verdict.Blocks} block(s), under the floor of {EquityBrief.Core.Returns.Blocks.Floor}"))}; better in {one.Verdict.Better} of {one.Verdict.Counted} counted year(s); {one.Verdict.Units} {one.Rule.Unit} unit(s){(one.Verdict.Counts ? string.Empty : ", short of the count")}");

    // The register's rows, which the per-rule test reads the rules standing when its run began off.
    static async Task<IReadOnlyList<Core.Candidates.RegisterRow>> RegisterAsync(string databaseFile, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = ReadRegister;

        var rows = new List<Core.Candidates.RegisterRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new Core.Candidates.RegisterRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return rows;
    }

    // The per-rule test's rows, each rule's and each of its test years', with its run log row, in one transaction under
    // the drain's lock.
    public async Task WriteRulesAsync(string runId, string month, string index, DateOnly through, DateTimeOffset started, int folds, IReadOnlyList<RuleTested> tested, CancellationToken cancellation = default)
    {
        using var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(Poll, cancellation), cancellation);
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        await using var transaction = connection.BeginTransaction();
        var years = 0;

        static object Or(object? value) => value ?? DBNull.Value;

        foreach (var one in tested)
        {
            var verdict = one.Verdict;

            await ExecuteAsync(connection, transaction, InsertRule,
            [
                ("$run_id", runId),
                ("$month", month),
                ("$index_code", index),
                ("$family", one.Rule.Family),
                ("$rule", one.Rule.Rule),
                ("$standing", one.Rule.Standing),
                ("$words", one.Rule.Words),
                ("$against", one.Against),
                ("$unit", one.Rule.Unit),
                ("$units", Or(verdict?.Units)),
                ("$blocks", Or(verdict?.Blocks)),
                ("$adjusted", Or(verdict?.Adjusted)),
                ("$gate", Or(verdict is null ? null : verdict.Gate ? 1 : 0)),
                ("$stable", Or(verdict is null ? null : verdict.Stable ? 1 : 0)),
                ("$counted", Or(verdict?.Counted)),
                ("$better", Or(verdict?.Better)),
                ("$trimmed", Or(verdict?.Trimmed)),
                ("$counts", Or(verdict is null ? null : verdict.Counts ? 1 : 0)),
                ("$passed", one.Passed ? 1 : 0),
                ("$why", Or(one.Why)),
            ], cancellation);

            foreach (var year in one.Evidence?.Years ?? [])
            {
                years++;

                await ExecuteAsync(connection, transaction, InsertRuleYear,
                [
                    ("$run_id", runId),
                    ("$index_code", index),
                    ("$family", one.Rule.Family),
                    ("$rule", one.Rule.Rule),
                    ("$year", year.Year),
                    ("$complete", year.Complete ? 1 : 0),
                    ("$units", year.ProposedUnits),
                    ("$total", year.ProposedTotal),
                    ("$compared_units", year.CurrentUnits),
                    ("$compared_total", year.CurrentTotal),
                ], cancellation);
            }
        }

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", RulesVerb),
            ("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$rows_written", tested.Count + years),
            ("$detail", RulesDetail(tested, index, month, through, folds)),
        ], cancellation);

        await transaction.CommitAsync(cancellation);
    }

    // The swing filter's open version and its settings, none before a version is opened.
    const string OpenVersion = "SELECT version, settings FROM filter_version WHERE closed_at IS NULL ORDER BY opened_at DESC, rowid DESC LIMIT 1;";

    // The newest shape proposal written for a version and not yet decided, with its levers.
    const string NewestShape = "SELECT id, levers FROM shape_proposal WHERE version = $version AND decision IS NULL ORDER BY id DESC LIMIT 1;";

    static async Task<(string Version, FilterSettings Settings)?> OpenVersionAsync(string databaseFile, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = OpenVersion;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) ? (reader.GetString(0), FilterSettings.Read(reader.GetString(1))) : null;
    }

    static async Task<(long Id, IReadOnlyList<(string Setting, double Proposed)> Moves)?> ShapeMovesAsync(string databaseFile, string version, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = NewestShape;
        command.Parameters.AddWithValue("$version", version);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) ? (reader.GetInt64(0), ShapeProcedures.MovesOf(reader.GetString(1))) : null;
    }

    // The answers the idea generator stored, read through the one reader of them: each prompt's newest read into its
    // labels, a refused answer holding none.
    static async Task<Func<string, IReadOnlyList<IdeaLabel>?>> StoredIdeasAsync(string databaseFile, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var stored = await LoopIdeas.NewestAsync(connection, cancellation);

        return key => stored.TryGetValue(key, out var idea) && idea.Outcome == LoopIdeas.Answered ? IdeaPrompt.Read(idea.Answer, null).Labels : null;
    }

    // The tree models earlier runs stored, which a rule standing at an approved change of the learned score's
    // challenger is walked by.
    static async Task<Func<long, TreeModel?>> StoredTreesAsync(string databaseFile, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        return await StoredTrees.ReadAsync(connection, null, cancellation);
    }

    // The setting an approval stored for each family of an index, the newest a family.
    static async Task<IReadOnlyDictionary<string, LoopChange>> StandingAsync(string databaseFile, string index, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        return await EquityBrief.Worker.Indices.IndexFamilies.StoredSettingsAsync(connection, index, cancellation);
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
    public static async Task<LoopRead> ReadAsync(string index, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, CancellationToken cancellation)
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
                ("$kind", score.Kind),
                ("$year", score.Year),
                ("$learned_from", score.LearnedFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$learned_before", score.LearnedBefore.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                ("$setups", score.Model.Rows),
                ("$readings", string.Join(",", score.Model.Readings.Select(reading => EquityBrief.Core.Ledger.LedgerReadings.All[reading].Column))),
                ("$parameters", score.Model.Canonical()),
                ("$hash", score.Model.Hash),
                ("$pin", score.Pin),
                ("$words", score.Words),
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
            $"{proposal.Family}, {proposal.Proposal}: {(one.Passed ? "passed" : "did not pass")}; {proposal.Words ?? "no setting chosen on all finished data"}; {gate}; better in {verdict.Better} of {verdict.Counted} counted year(s){(verdict.Stable ? string.Empty : ", short of the stability screen")}; {verdict.Units} {proposal.Unit} unit(s){(verdict.Counts ? string.Empty : ", short of the count")}{(one.BeatsRidge == false ? ", not above the learned score's proposal of its kind" : string.Empty)}; {proposal.StableFolds} of {proposal.Folds.Count} folds {(proposal.Proposal.StartsWith(RidgeScore.Proposal, StringComparison.Ordinal) || proposal.Proposal.StartsWith(TreeScore.Proposal, StringComparison.Ordinal) ? "fitted a score" : proposal.Proposal.StartsWith(IdeaProcedures.Prefix, StringComparison.Ordinal) ? "named the same idea" : "within a step")}");
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
