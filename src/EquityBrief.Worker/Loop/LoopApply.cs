using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Candidates;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// What the apply step did with one proposal: its run, index, family and name, whether it was applied, and why in words.
public sealed record LoopApplication(string Run, string Index, string Family, string Proposal, bool Applied, string Words);

// What the apply step did: its answers, the S&P 500 approvals it left for a night that can register them, and the
// replaced rules it retired.
public sealed record LoopApplyOutcome(IReadOnlyList<LoopApplication> Applications, int Waiting = 0, IReadOnlyList<string>? Retired = null, DateTimeOffset? WroteFrom = null)
{
    public int Applied => Applications.Count(one => one.Applied);

    public int Refused => Applications.Count(one => !one.Applied);
}

// The apply step. At the start of the swing filter's step, before anything in it reads the register, it takes every
// proposal the operator approved and the step has not yet answered, and, only where the adopt setting reads automatic,
// every proposal of an index's newest run that passed and holds no decision, a declined one only where it was put again;
// and every restore the operator approved. A change to an S&P 400 or 600 swing family is written on top of the setting
// the family stands at as a new row of the stored settings, which the family reads from the night on, on that index
// alone. A change to the S&P 500's breakout or drift is written the same way on the S&P 500 and registered as the
// family's live rule in the same transaction, on a night whose session is the first a registration made then counts
// from, and left waiting otherwise; the rule it replaces is retired once the block after the change is reached. A
// change to the S&P 500's pullback's hooks is written as a stored setting on the S&P 500 alone, which the family's
// list reads, and a shape move of it opens the swing filter's next version through the shape command on the same
// nights a registration is made. A sector heavyweights' book's change is refused with its reason. Every answer is one
// row and none is written twice. It reads no market data, makes no request and calls no model.
// see: An approved change is applied by the night's own build on the index it was approved on alone
// see: An approved change reaches the S&P 500's breakout and drift through the stored settings and a registration, and the rule it replaces runs beside it for 63 sessions
// see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
public sealed class LoopApply(IClock clock, string databaseFile, string? adopt = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.LoopRun, Touch.Read),
            new StoreTouch(Store.LoopProposal, Touch.Read),
            new StoreTouch(Store.LoopDecision, Touch.Read),
            new StoreTouch(Store.LoopApplied, Touch.Read | Touch.Insert),
            new StoreTouch(Store.ProvisionalSetting, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "loop-apply";

    public const string Verb = "loop-apply";

    public const string NoChangeRefused = "the proposal states no change in the form an approval applies";

    public const string LargeSettingRefused = "the S&P 500's breakout and drift stand at their own settings, so an approval changes their hooks alone";

    public const string NoLiveRuleRefused = "the family holds no registered live rule for the change to replace";

    public const string StandsAlreadyRefused = "the change leaves the family's live rule where it stands";

    public const string ShapeRefused = "a shape move names the S&P 500 swing filter's settings alone, and stands alone in its change";

    // The words a shape move's answer opens with.
    public const string ShapeOpened = "the swing filter opens version";

    // Every approved decision the step has not answered, oldest first.
    const string Approved = @"
        SELECT d.run_id, d.index_code, d.family, d.proposal, p.change
        FROM loop_decision d
        LEFT JOIN loop_proposal p ON p.run_id = d.run_id AND p.index_code = d.index_code AND p.family = d.family AND p.proposal = d.proposal
        LEFT JOIN loop_applied a ON a.run_id = d.run_id AND a.index_code = d.index_code AND a.family = d.family AND a.proposal = d.proposal
        WHERE d.decision = 'approved' AND a.run_id IS NULL
        ORDER BY d.decided_at, d.rowid;
    ";

    // Each index's newest run's proposals that passed and hold no decision, in families that hold no approval and no
    // applied change in the run, the strongest by its adjusted p-value first, with the blocks a decline of the same
    // proposal on an earlier run read, which the automatic setting alone reads.
    const string Undecided = @"
        SELECT p.run_id, p.index_code, p.family, p.proposal, p.change, p.blocks,
            (SELECT MAX(q.blocks) FROM loop_decision e
                JOIN loop_proposal q ON q.run_id = e.run_id AND q.index_code = e.index_code AND q.family = e.family AND q.proposal = e.proposal
                WHERE e.decision = 'declined' AND e.index_code = p.index_code AND e.family = p.family AND e.proposal = p.proposal AND COALESCE(q.change, '') = COALESCE(p.change, ''))
        FROM loop_proposal p
        WHERE p.passed = 1
            AND p.run_id = (SELECT r.run_id FROM loop_run r WHERE r.index_code = p.index_code ORDER BY r.rowid DESC LIMIT 1)
            AND NOT EXISTS (SELECT 1 FROM loop_decision d WHERE d.run_id = p.run_id AND d.index_code = p.index_code AND d.family = p.family AND (d.proposal = p.proposal OR d.decision = 'approved'))
            AND NOT EXISTS (SELECT 1 FROM loop_applied a WHERE a.run_id = p.run_id AND a.index_code = p.index_code AND a.family = p.family AND (a.proposal = p.proposal OR a.outcome = 'applied'))
        ORDER BY p.index_code, p.family, p.adjusted, p.proposal;
    ";

    // The settings a family has stood at, oldest first.
    const string Standing = "SELECT id, change FROM provisional_setting WHERE index_code = $index AND family = $family ORDER BY id;";

    // The night's session: the newest the store holds.
    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string InsertSetting = @"
        INSERT INTO provisional_setting (index_code, family, change, words, set_at, run_id, proposal)
        VALUES ($index, $family, $change, $words, $set_at, $run_id, $proposal);
    ";

    const string InsertApplied = @"
        INSERT INTO loop_applied (run_id, index_code, family, proposal, applied_at, outcome, words)
        VALUES ($run_id, $index, $family, $proposal, $applied_at, $outcome, $words);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows_written, 0, 0, '0', $detail);";

    // One proposal waiting for its answer, and whether it waits on the automatic setting rather than an approval.
    sealed record Waiting(string Run, string Index, string Family, string Proposal, string? Change, bool Automatic);

    // The step run by hand: the S&P 400's and 600's changes alone, the S&P 500's left for the night, which alone registers
    // one.
    public Task<LoopApplyOutcome> RunAsync(string runId, CancellationToken cancellation = default) =>
        ApplyAsync(runId, large: false, cancellation);

    // The step the night runs, every index's changes and the S&P 500's replaced rules' retirements.
    public Task<LoopApplyOutcome> NightAsync(string runId, CancellationToken cancellation = default) =>
        ApplyAsync(runId, large: true, cancellation);

    async Task<LoopApplyOutcome> ApplyAsync(string runId, bool large, CancellationToken cancellation)
    {
        var mode = LoopDecisions.AdoptOf(adopt);
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var waiting = new List<Waiting>();

        await foreach (var row in RowsAsync(connection, null, Approved, [], cancellation))
        {
            waiting.Add(new Waiting(row.GetString(0), row.GetString(1), row.GetString(2), row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4), false));
        }

        // On the automatic setting, the strongest of a family's undecided passing proposals in its index's newest run, one
        // change a family a run as an approval is.
        if (mode == LoopDecisions.Automatic)
        {
            await foreach (var row in RowsAsync(connection, null, Undecided, [], cancellation))
            {
                var declinedAt = row.IsDBNull(6) ? (int?)null : row.GetInt32(6);
                var (run, index, family) = (row.GetString(0), row.GetString(1), row.GetString(2));

                if (waiting.Any(one => one.Run == run && one.Index == index && one.Family == family))
                {
                    continue;
                }

                if (declinedAt is null || LoopDecisions.PutAgain(declinedAt.Value, row.GetInt32(5), passedNow: true))
                {
                    waiting.Add(new Waiting(run, index, family, row.GetString(3), row.IsDBNull(4) ? null : row.GetString(4), true));
                }
            }
        }

        var night = await ScalarAsync(connection, NewestSession, cancellation) is string newest ? Date(newest) : (DateOnly?)null;
        var registrar = new CandidateRegistrar(clock, databaseFile);
        var applications = new List<LoopApplication>();
        var answered = new List<(Waiting One, bool Applied, string Words, LoopChange? Written)>();

        // The S&P 500's breakout and drift, each registered as the family's live rule with its setting written beside the
        // registration in one transaction, on a night whose session is the first a registration made now counts from, so
        // the rule is evaluated from the night its record counts from; left waiting by hand and on any other night.
        var largeOnes = waiting.Where(one => IsLarge(one.Index, one.Family)).ToList();
        var shapeOnes = waiting.Where(IsShapeMove).ToList();
        var registersTonight = large && night is { } session && FamilyRecords.FirstSession(clock.UtcNow) == session;
        var left = registersTonight ? 0 : largeOnes.Count + shapeOnes.Count;

        // A shape move of the S&P 500's swing filter, opening the next filter version with the move made and registering
        // the swing family again under it, on the same nights a registration is made, the move's row written in the
        // version's transaction.
        // see: The shape clock proposes moves tested like any proposal, and an approved move opens the next filter version restarting the live filter's record
        if (registersTonight)
        {
            foreach (var one in shapeOnes)
            {
                var standing = await StandingAsync(connection, null, one.Index, one.Family, cancellation);
                var (applied, said, written) = Answer(one.Run, one.Index, one.Family, one.Proposal, one.Change, standing);

                if (!applied || written?.Shape is not { Count: > 0 } move)
                {
                    answered.Add((one, false, said, null));

                    continue;
                }

                var evidence = (one.Automatic ? $"the move that passed, applied with {LoopDecisions.AdoptSetting} reading {LoopDecisions.Automatic}" : "the move approved on the Loop page")
                    + $", '{one.Proposal}' of {one.Run}: {written.Words()}";
                var opened = await new EquityBrief.Worker.Filter.ShapeCommand(clock, databaseFile).ApplyTheMoveAsync(
                    move,
                    evidence,
                    async (writing, transaction, version, token) =>
                        await WriteAppliedAsync(writing, transaction, one, true, $"{ShapeOpened} {version} at {written.Words()}", token),
                    runId,
                    cancellation);

                if (opened.Outcome == EquityBrief.Worker.Filter.ShapeCommand.Accepted)
                {
                    applications.Add(new LoopApplication(one.Run, one.Index, one.Family, one.Proposal, true, $"{ShapeOpened} {opened.Version} at {written.Words()}; {opened.Detail}"));
                }
                else
                {
                    answered.Add((one, false, opened.Detail, null));
                }
            }
        }

        if (registersTonight)
        {
            foreach (var one in largeOnes)
            {
                var standing = await StandingAsync(connection, null, one.Index, one.Family, cancellation);
                var (applied, said, written) = Answer(one.Run, one.Index, one.Family, one.Proposal, one.Change, standing);

                if (!applied || written is null)
                {
                    answered.Add((one, false, said, null));

                    continue;
                }

                var words = one.Run == LoopDecisions.RestoreRun ? said : "the family stands from tonight at " + written.Words();

                var family = CandidateFamily.In(CandidateFamily.Standing(await registrar.RowsAsync(cancellation), clock.UtcNow), one.Family);
                var live = FamilyRecords.LiveOf(family);
                var registration = live is null ? null : LargeRegistration(one.Family, written.Hooks, live);

                if (live is null || registration is null || string.Equals(live.Candidate, registration.Candidate, StringComparison.Ordinal))
                {
                    answered.Add((one, false, live is null ? NoLiveRuleRefused : StandsAlreadyRefused, null));

                    continue;
                }

                var evidence = (one.Run == LoopDecisions.RestoreRun ? "the restore " : "the change ")
                    + (one.Automatic ? $"that passed, applied with {LoopDecisions.AdoptSetting} reading {LoopDecisions.Automatic}" : "approved on the Loop page")
                    + $", '{one.Proposal}' of {one.Run}, the family standing from tonight at {written.Words()}";
                var registered = await registrar.RegisterTheChangeAsync(
                    registration,
                    evidence,
                    async (writing, transaction, token) =>
                    {
                        await WriteSettingAsync(writing, transaction, one, written, token);
                        await WriteAppliedAsync(writing, transaction, one, true, words, token);
                    },
                    runId,
                    cancellation);

                if (registered.Outcome == CandidateRegistrar.Refused)
                {
                    answered.Add((one, false, registered.Detail, null));
                }
                else
                {
                    applications.Add(new LoopApplication(one.Run, one.Index, one.Family, one.Proposal, true, words + "; " + registered.Detail));
                }
            }
        }

        // A rule a change replaced, retired on the night whose session reaches the block after the session its successor's
        // record counts from, so it ran beside the change for one whole block and not the night after it.
        var retired = new List<string>();

        if (large && night is { } tonight)
        {
            foreach (var name in LoopDecisions.LargeFamilies)
            {
                var family = CandidateFamily.In(CandidateFamily.Standing(await registrar.RowsAsync(cancellation), clock.UtcNow), name);

                foreach (var replaced in family.Where(row => FamilyRecords.IsReplaced(row.Candidate, family)).ToArray())
                {
                    var successor = family
                        .Where(row => FamilyRecords.IsLive(row.Candidate) && (row.RegisteredAt, row.Id).CompareTo((replaced.RegisteredAt, replaced.Id)) > 0)
                        .MinBy(row => (row.RegisteredAt, row.Id));

                    if (successor is null || Blocks.Of(FamilyRecords.FirstSession(successor.RegisteredAt), tonight) < 1)
                    {
                        continue;
                    }

                    var first = FamilyRecords.FirstSession(successor.RegisteredAt);
                    var retiring = await registrar.RetireAsync(
                        replaced.Candidate,
                        FormattableString.Invariant($"replaced by '{successor.Candidate}', whose record counts from {first:yyyy-MM-dd}, and the {LoopDecisions.ReplacedSessions} sessions it ran beside it have passed by {tonight:yyyy-MM-dd}"),
                        runId,
                        cancellation);

                    if (retiring.Outcome == CandidateRegistrar.Retired)
                    {
                        retired.Add(replaced.Candidate);
                    }
                }
            }

            // An S&P 400's or 600's rule of the day before its freeze, kept in the background beside the frozen live rule,
            // retired on the night whose session reaches the block after the session its stand began, so it ran beside the
            // freeze for one whole block and not the night after it, its stand read over the rows naming it, which a moved
            // rule's registration again in one second does not restart.
            // see: A freeze on the S&P 400 or 600 registers a market variant beside each swing rule and keeps the rule it replaces in the background for 63 sessions
            var everyRow = await registrar.RowsAsync(cancellation);

            foreach (var background in CandidateFamily.Standing(everyRow, clock.UtcNow).Where(row => FamilyRecords.IsBackground(row.Candidate)).ToArray())
            {
                var since = FamilyRecords.StoodSince(everyRow, background.Candidate);

                if (Blocks.Of(since, tonight) < 1)
                {
                    continue;
                }

                var live = FamilyRecords.LiveOf(CandidateFamily.In(CandidateFamily.Standing(everyRow, clock.UtcNow), CandidateFamily.SetupFamilyOf(background.Evaluator)));
                var retiring = await registrar.RetireAsync(
                    background.Candidate,
                    FormattableString.Invariant($"the rule of the day before the freeze of '{live?.Candidate ?? "its family"}', kept beside it in the background from {since:yyyy-MM-dd}, and the {LoopDecisions.ReplacedSessions} sessions it ran beside it have passed by {tonight:yyyy-MM-dd}"),
                    runId,
                    cancellation);

                if (retiring.Outcome == CandidateRegistrar.Retired)
                {
                    retired.Add(background.Candidate);
                }
            }
        }

        // The families whose live rule stands registered on an S&P 400 or 600, which draw its page in the provisional
        // rule's place.
        var frozen = EquityBrief.Worker.Indices.IndexFamilies.StandingRules(await registrar.RowsAsync(cancellation), started).Rules
            .Where(rule => rule.Live)
            .Select(rule => (rule.Index, rule.Family))
            .ToHashSet();

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var one in waiting.Where(one => !IsLarge(one.Index, one.Family) && !IsShapeMove(one)))
        {
            var standing = await StandingAsync(connection, transaction, one.Index, one.Family, cancellation);
            var (applied, words, written) = frozen.Contains((one.Index, one.Family)) ? (false, LoopDecisions.FrozenRefused, null) : Answer(one.Run, one.Index, one.Family, one.Proposal, one.Change, standing);

            answered.Add((one, applied, words, written));
        }

        foreach (var (one, applied, words, written) in answered)
        {
            if (written is not null)
            {
                await WriteSettingAsync(connection, transaction, one, written, cancellation);
            }

            await WriteAppliedAsync(connection, transaction, one, applied, words, cancellation);
            applications.Add(new LoopApplication(one.Run, one.Index, one.Family, one.Proposal, applied, words));
        }

        // Every application on the S&P 500 but a hook change of its pullback, which writes a stored setting alone, wrote to
        // the register: a breakout's or drift's registration, or a shape move's.
        var wrote = retired.Count > 0 || applications.Any(one => one.Applied && one.Index == MarketCloses.Index && (IsLarge(one.Index, one.Family) || one.Words.StartsWith(ShapeOpened, StringComparison.Ordinal)));
        var outcome = new LoopApplyOutcome(applications, left, retired, wrote ? started : null);
        var detail = (applications.Count == 0
                ? $"no approved change waiting, the adopt setting reading {mode}"
                : FormattableString.Invariant($"{outcome.Applied} applied and {outcome.Refused} refused, the adopt setting reading {mode}: ")
                    + string.Join("; ", applications.Select(one => $"{one.Index} {one.Family}, {one.Proposal}, {(one.Applied ? "applied" : "refused")}: {one.Words}")))
            + (left == 0 ? string.Empty : FormattableString.Invariant($"; {left} S&P 500 change(s) left for a night whose session a registration made then counts from"))
            + (retired.Count == 0 ? string.Empty : "; retired " + string.Join(", ", retired.Select(name => $"'{name}'")));

        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId), ("$stage", Stage), ("$started_at", Stamp(started)), ("$ended_at", Stamp(clock.UtcNow)),
            ("$rows_written", applications.Count + applications.Count(one => one.Applied)), ("$detail", detail),
        ], cancellation);

        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // The instant a night's swing filter's step reads the register at: the night's start, or, where the apply step wrote to
    // the register from an instant on, the second after the newest row written since, so the step reads the rules standing
    // when the night started with the apply step's own registration and retirement, the only rows a night's window adds.
    // see: A register row in the same second as the instant it is compared with is read as after it
    public static DateTimeOffset ReadFrom(IReadOnlyList<RegisterRow> register, DateTimeOffset nightStartedAt, DateTimeOffset? wroteFrom)
    {
        if (wroteFrom is not { } from)
        {
            return nightStartedAt;
        }

        var written = register.Where(row => row.RegisteredAt >= Second(from)).Select(row => row.RegisteredAt).ToArray();

        return written.Length == 0 ? nightStartedAt : Second(written.Max()).AddSeconds(1);
    }

    static DateTimeOffset Second(DateTimeOffset at) => new(at.UtcTicks - (at.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);

    // Whether a change is one the S&P 500's breakout or drift registers.
    static bool IsLarge(string index, string family) =>
        index == MarketCloses.Index && LoopDecisions.LargeFamilies.Contains(family, StringComparer.Ordinal);

    // Whether a change is a shape move of the S&P 500's swing filter, which opens a filter version.
    static bool IsShapeMove(Waiting one) =>
        one.Index == MarketCloses.Index && one.Family == SetupFamilies.Pullback && LoopChange.Read(one.Change)?.Shape is { Count: > 0 };

    // The rule a change to the S&P 500's breakout or drift registers: the family's own setting as its page reads it with
    // the hooks the change leaves it standing at, named as the family's live rule with the hooks' words, its rule and test
    // the live rule's; none for a family the S&P 500's approvals do not reach.
    public static Registration? LargeRegistration(string family, IReadOnlyDictionary<string, double> hooks, RegisterRow live)
    {
        var (own, words) = family switch
        {
            BreakoutRule.Name => (BreakoutCandidate.ParametersOf(BreakoutRule.Live), TheSetupFamilies.Words(BreakoutRule.Live)),
            DriftRule.Name => (DriftCandidate.ParametersOf(DriftRule.Live), TheSetupFamilies.Words(DriftRule.Live)),
            _ => ((IReadOnlyDictionary<string, double>?)null, string.Empty),
        };

        if (own is null)
        {
            return null;
        }

        var parameters = own.Concat(hooks).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var hooked = RuleHooks.Of(hooks).Words();

        return new Registration(
            FamilyRecords.LivePrefix + words + MarketSwitches.None.Words + (hooked is null ? string.Empty : "; " + hooked),
            live.Rule,
            live.Test,
            live.Evaluator,
            parameters);
    }

    // What the step does with one proposal: a restore writes again the setting standing before the change it names, or
    // the family's code setting where none stood before it; a swing family's change is written on top of the setting it
    // stands at, the S&P 500's hooks alone but for the fundamentals-first family's, whose setting moves there as on the S&P
    // 400 and 600; every other is refused with its reason.
    public static (bool Applied, string Words, LoopChange? Written) Answer(string run, string index, string family, string proposal, string? change, IReadOnlyList<(long Id, string Change)> standing)
    {
        if (LoopDecisions.Refusal(index, family) is { } refused)
        {
            return (false, refused, null);
        }

        var current = standing.Count > 0 ? LoopChange.Read(standing[^1].Change) : null;

        if (run == LoopDecisions.RestoreRun)
        {
            var undone = standing.Select((row, at) => (row, at)).FirstOrDefault(one => LoopDecisions.RestoreProposal(one.row.Id) == proposal);

            if (undone.row.Change is null)
            {
                return (false, "the restore names a change the family's settings do not hold", null);
            }

            var before = undone.at > 0 ? LoopChange.Read(standing[undone.at - 1].Change) : null;
            var restored = before ?? new LoopChange(null, null, null, LoopChange.NoHooks);

            return (true, "restored " + (before is null ? "the family's own setting" : restored.Words()), restored);
        }

        if (LoopChange.Read(change) is not { Moves: true } made)
        {
            return (false, NoChangeRefused, null);
        }

        if (index == MarketCloses.Index && made.Places is not null && family != FundamentalsRule.Name)
        {
            return (false, LargeSettingRefused, null);
        }

        if (family == SetupFamilies.Pullback && made.Places is not null)
        {
            return (false, "the pullback's setting on its grid is not moved by an approval, only its hooks", null);
        }

        // A shape move names the S&P 500 swing filter's settings, which no other family holds.
        if (made.Shape is { Count: > 0 })
        {
            return index == MarketCloses.Index && family == SetupFamilies.Pullback && made.Hooks.Count == 0
                ? (true, "the swing filter opens its next version at " + made.Words(), made)
                : (false, ShapeRefused, null);
        }

        var stands = made.OnTopOf(current);

        return (true, "the family stands from the next night at " + stands.Words(), stands);
    }

    async Task WriteSettingAsync(SqliteConnection connection, SqliteTransaction transaction, Waiting one, LoopChange setting, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, InsertSetting,
        [
            ("$index", one.Index), ("$family", one.Family), ("$change", setting.Json), ("$words", setting.Words()),
            ("$set_at", Stamp(clock.UtcNow)), ("$run_id", one.Run), ("$proposal", one.Proposal),
        ], cancellation);

    async Task WriteAppliedAsync(SqliteConnection connection, SqliteTransaction transaction, Waiting one, bool applied, string words, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, InsertApplied,
        [
            ("$run_id", one.Run), ("$index", one.Index), ("$family", one.Family), ("$proposal", one.Proposal), ("$applied_at", Stamp(clock.UtcNow)),
            ("$outcome", applied ? LoopDecisions.Applied : LoopDecisions.Refused), ("$words", words),
        ], cancellation);

    static async Task<IReadOnlyList<(long Id, string Change)>> StandingAsync(SqliteConnection connection, SqliteTransaction? transaction, string index, string family, CancellationToken cancellation)
    {
        var standing = new List<(long Id, string Change)>();

        await foreach (var row in RowsAsync(connection, transaction, Standing, [("$index", index), ("$family", family)], cancellation))
        {
            standing.Add((row.GetInt64(0), row.GetString(1)));
        }

        return standing;
    }

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        return await command.ExecuteScalarAsync(cancellation);
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
        SqliteTransaction? transaction,
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
