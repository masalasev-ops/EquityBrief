using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Candidates;

public sealed record RegistrationOutcome(string Outcome, long? Id, string Detail);

// The candidate registrar. The one door into the candidate register, and the
// only component SCHEMA gives an insert on it.
//
// It offers a registration and a retirement and nothing else. There is no update
// and no delete here because there is no update and no delete anywhere: a
// correction is a new row naming what it retires, which is the same convention
// the decision record uses for a superseded entry. A caller asking to change a
// candidate that stands registered is asking for an edit, and that is refused
// here with the attempt on the run log, so the refusal is a thing a person can
// read the morning it happens rather than an absence they have to notice.
//
// The store refuses the same write one layer down, through the triggers the
// migrations create. Both layers rather than either: the triggers hold for
// anything that reaches the file without coming through here, and this holds for
// the caller that came through the front door and gets told why.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
public sealed class CandidateRegistrar : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.CandidateRegister, Touch.Read | Touch.Insert),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "candidate-register";

    public const string Registered = "registered";

    public const string Retired = "retired";

    public const string Refused = "refused";

    const string ReadRegister = @"
        SELECT id, candidate, rule, test, evaluator, parameters, evaluator_version,
               event, retires, registered_at, evidence
        FROM candidate_register
        ORDER BY id;
    ";

    const string Append = @"
        INSERT INTO candidate_register (
            id, candidate, rule, test, evaluator, parameters, evaluator_version,
            event, retires, registered_at, evidence)
        VALUES (
            $id, $candidate, $rule, $test, $evaluator, $parameters, $evaluator_version,
            $event, $retires, $registered_at, $evidence);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public CandidateRegistrar(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<RegistrationOutcome> RegisterAsync(
        string candidate,
        string rule,
        string test,
        string evaluator,
        IReadOnlyDictionary<string, double> parameters,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The row and its run log row are one write, so a row that cannot be recorded
        // leaves no register row behind it; a refusal is recorded once the write is let go.
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var rows = await RowsAsync(connection, cancellation);

        if ((Unstated(rule, test) ?? Refusal(rows, candidate, evaluator, parameters, startedAt)) is { } refusal)
        {
            await transaction.RollbackAsync(cancellation);
            await RecordAsync(connection, runId, startedAt, Refused, 0, refusal, cancellation);

            return new RegistrationOutcome(Refused, null, refusal);
        }

        var carried = CandidateEvaluators.Find(evaluator)!;

        var id = await AppendAsync(
            connection,
            rows,
            candidate,
            rule,
            test,
            evaluator,
            CandidateEvaluator.Write(parameters),
            carried.Version,
            Registered,
            retires: null,
            startedAt,
            evidence: null,
            cancellation);

        var detail =
            $"registered '{candidate}' as {id} on {evaluator} at {carried.Version}, " +
            FormattableString.Invariant($"family of {CandidateFamily.Standing(rows, startedAt).Count + 1} of {CandidateFamily.Maximum}");

        await RecordAsync(connection, runId, startedAt, Registered, 1, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new RegistrationOutcome(Registered, id, detail);
    }

    // Several candidates registered at one instant, in one transaction, or none of them.
    //
    // At one instant because a candidate's level is divided across the candidates the first night
    // evaluated it also evaluated: registered one at a time, the first would be evaluated on a
    // night the others did not exist for and would be tested at the whole level rather than its
    // share. All or none for the same reason: a set half written leaves the level of the ones that
    // landed divided by a family that never stood.
    // see: The three candidates are registered at one instant
    // see: The candidate family is at most eight and the threshold is divided by it
    public async Task<RegistrationOutcome> RegisterTogetherAsync(
        IReadOnlyList<Registration> registrations,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var rows = await RowsAsync(connection, cancellation);

        if (registrations.Count == 0)
        {
            await transaction.RollbackAsync(cancellation);

            const string nothing = "a registration of nothing is not a registration.";

            await RecordAsync(connection, runId, startedAt, Refused, 0, nothing, cancellation);

            return new RegistrationOutcome(Refused, null, nothing);
        }

        // Each is judged against the register as it stands plus the ones already taken in this
        // command, so a set naming one candidate twice, or one more than the family admits, is
        // refused here rather than written and counted afterwards.
        var taken = rows.ToList();

        foreach (var one in registrations)
        {
            if ((Unstated(one.Rule, one.Test) ?? Refusal(taken, one.Candidate, one.Evaluator, one.Parameters, startedAt)) is { } refusal)
            {
                await transaction.RollbackAsync(cancellation);

                var said = $"'{one.Candidate}' was refused, so none of the {registrations.Count} was registered: {refusal}";

                await RecordAsync(connection, runId, startedAt, Refused, 0, said, cancellation);

                return new RegistrationOutcome(Refused, null, said);
            }

            taken.Add(new RegisterRow(
                taken.Count == 0 ? 1 : taken.Max(row => row.Id) + 1,
                one.Candidate,
                one.Rule,
                one.Test,
                one.Evaluator,
                CandidateEvaluator.Write(one.Parameters),
                CandidateEvaluators.Find(one.Evaluator)!.Version,
                Registered,
                null,
                startedAt,
                null));
        }

        var written = new List<string>();
        var appended = rows;

        foreach (var one in registrations)
        {
            var carried = CandidateEvaluators.Find(one.Evaluator)!;

            var id = await AppendAsync(
                connection,
                appended,
                one.Candidate,
                one.Rule,
                one.Test,
                one.Evaluator,
                CandidateEvaluator.Write(one.Parameters),
                carried.Version,
                Registered,
                retires: null,
                startedAt,
                evidence: null,
                cancellation);

            appended = [.. appended, taken.Single(row => row.Id == id)];
            written.Add($"'{one.Candidate}' as {id} on {one.Evaluator} at {carried.Version}");
        }

        var detail = FormattableString.Invariant(
            $"registered {written.Count} at one instant, family of {CandidateFamily.Standing(appended, startedAt).Count} of {CandidateFamily.Maximum}: ")
            + string.Join("; ", written);

        await RecordAsync(connection, runId, startedAt, Registered, written.Count, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new RegistrationOutcome(Registered, null, detail);
    }

    const string OpenVersion = "SELECT version, settings FROM filter_version WHERE closed_at IS NULL ORDER BY opened_at DESC LIMIT 1;";

    // The swing family's registration: phase 10's three retired, each on the words saying no result of it
    // was read, and the six registered, the live filter at the open filter version's settings, all at one
    // instant or none. Refused whole where no version is open, since the live filter's candidate states
    // the settings the list runs on, or where any one row would be refused.
    // see: The three phase 10 candidates are retired when the swing family registers, and each retirement says no result of theirs was read
    // see: The swing filter opens loose on the swing trade's own plan, and each of its five variants moves one setting to its other side
    public async Task<RegistrationOutcome> RegisterTheFamilyAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        (string Version, FilterSettings Settings)? open = null;

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = OpenVersion;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                open = (reader.GetString(0), FilterSettings.Read(reader.GetString(1)));
            }
        }

        async Task<RegistrationOutcome> RefuseAsync(string said)
        {
            await transaction.RollbackAsync(cancellation);
            await RecordAsync(connection, runId, startedAt, Refused, 0, said, cancellation);

            return new RegistrationOutcome(Refused, null, said);
        }

        if (open is not { } held)
        {
            return await RefuseAsync(
                "no filter version is open, so the live filter's candidate has no settings to state, and none of the nine was written. " +
                "Open the first version with the shape command, then register the family.");
        }

        var rows = await RowsAsync(connection, cancellation);
        var taken = rows.ToList();

        foreach (var retire in TheSwingFamily.Retires)
        {
            if (RetirementRefusal(taken, retire, TheSwingFamily.Evidence, startedAt, ShortlistSeries.Reasons) is { } refusal)
            {
                return await RefuseAsync($"'{retire}' was refused, so none of the nine was written: {refusal}");
            }

            var standing = taken.Last(row => row.Event == CandidateFamily.Registered && string.Equals(row.Candidate, retire, StringComparison.Ordinal));

            taken.Add(standing with { Id = taken.Max(row => row.Id) + 1, Event = Retired, Retires = retire, RegisteredAt = startedAt, Evidence = TheSwingFamily.Evidence });
        }

        var family = TheSwingFamily.For(held.Version, held.Settings);

        foreach (var one in family)
        {
            if ((Unstated(one.Rule, one.Test) ?? Refusal(taken, one.Candidate, one.Evaluator, one.Parameters, startedAt)) is { } refusal)
            {
                return await RefuseAsync($"'{one.Candidate}' was refused, so none of the nine was written: {refusal}");
            }

            taken.Add(new RegisterRow(
                taken.Count == 0 ? 1 : taken.Max(row => row.Id) + 1,
                one.Candidate,
                one.Rule,
                one.Test,
                one.Evaluator,
                CandidateEvaluator.Write(one.Parameters),
                CandidateEvaluators.Find(one.Evaluator)!.Version,
                Registered,
                null,
                startedAt,
                null));
        }

        var appended = rows;

        foreach (var row in taken.Skip(rows.Count))
        {
            var id = await AppendAsync(
                connection, appended, row.Candidate, row.Rule, row.Test, row.Evaluator, row.Parameters,
                row.EvaluatorVersion, row.Event, row.Retires, startedAt, row.Evidence, cancellation);

            appended = [.. appended, row with { Id = id }];
        }

        var detail = FormattableString.Invariant(
            $"retired {TheSwingFamily.Retires.Count} and registered {family.Count} at one instant, the live filter at filter version {held.Version}, ")
            + FormattableString.Invariant($"family of {CandidateFamily.Standing(appended, startedAt).Count} of {CandidateFamily.Maximum}: ")
            + string.Join("; ", family.Select(one => $"'{one.Candidate}'"));

        await RecordAsync(connection, runId, startedAt, Registered, taken.Count - rows.Count, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new RegistrationOutcome(Registered, null, detail);
    }

    // Every standing candidate whose evaluator the code no longer carries at the version it was
    // registered with, retired on the evidence given and registered again unchanged at the version the
    // code carries now, all at one instant or none. Unchanged because nothing about the candidate was
    // decided again: its name, rule, test and parameters are the row that stood, and only the pin of the
    // code it runs through moved. At one instant for the reason a family registers at one, since each
    // candidate's level is divided across the candidates the first night evaluated beside it. Refused
    // where no standing candidate has moved, and where one's evaluator is no longer carried at all,
    // since that one is a retirement and not a registration again.
    // see: A candidate whose evaluator a code change moved is registered again unchanged, every one at one instant
    public async Task<RegistrationOutcome> RegisterMovedAgainAsync(string evidence, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        async Task<RegistrationOutcome> RefuseAsync(string said)
        {
            await transaction.RollbackAsync(cancellation);
            await RecordAsync(connection, runId, startedAt, Refused, 0, said, cancellation);

            return new RegistrationOutcome(Refused, null, said);
        }

        var rows = await RowsAsync(connection, cancellation);
        var standing = CandidateFamily.Standing(rows, startedAt);

        if (standing.FirstOrDefault(row => CandidateEvaluators.Find(row.Evaluator) is null) is { } gone)
        {
            return await RefuseAsync(
                $"'{gone.Candidate}' runs on '{gone.Evaluator}', which the code no longer carries, so it cannot be registered again " +
                "and none was written. Retire it with its evidence, then run this again.");
        }

        var moved = standing
            .Where(row => !string.Equals(CandidateEvaluators.Find(row.Evaluator)!.Version, row.EvaluatorVersion, StringComparison.Ordinal))
            .ToArray();

        if (moved.Length == 0)
        {
            return await RefuseAsync("no standing candidate's evaluator has moved, so nothing needs registering again and none was written.");
        }

        var taken = rows.ToList();

        foreach (var row in moved)
        {
            if (RetirementRefusal(taken, row.Candidate, evidence, startedAt, ShortlistSeries.Reasons) is { } refusal)
            {
                return await RefuseAsync($"'{row.Candidate}' was refused, so none of the {moved.Length} was written: {refusal}");
            }

            taken.Add(row with { Id = taken.Max(one => one.Id) + 1, Event = Retired, Retires = row.Candidate, RegisteredAt = startedAt, Evidence = evidence });
        }

        foreach (var row in moved)
        {
            if ((Unstated(row.Rule, row.Test) ?? Refusal(taken, row.Candidate, row.Evaluator, CandidateEvaluator.Read(row.Parameters), startedAt)) is { } refusal)
            {
                return await RefuseAsync($"'{row.Candidate}' was refused, so none of the {moved.Length} was written: {refusal}");
            }

            taken.Add(row with
            {
                Id = taken.Max(one => one.Id) + 1,
                EvaluatorVersion = CandidateEvaluators.Find(row.Evaluator)!.Version,
                Event = Registered,
                Retires = null,
                RegisteredAt = startedAt,
                Evidence = null,
            });
        }

        var appended = rows;

        foreach (var row in taken.Skip(rows.Count))
        {
            var id = await AppendAsync(
                connection, appended, row.Candidate, row.Rule, row.Test, row.Evaluator, row.Parameters,
                row.EvaluatorVersion, row.Event, row.Retires, startedAt, row.Evidence, cancellation);

            appended = [.. appended, row with { Id = id }];
        }

        var detail = FormattableString.Invariant($"retired and registered again {moved.Length} at one instant, each unchanged: ")
            + string.Join("; ", moved.Select(row => $"'{row.Candidate}' on {row.Evaluator} from {row.EvaluatorVersion} to {CandidateEvaluators.Find(row.Evaluator)!.Version}"))
            + $", on the evidence: {evidence}";

        await RecordAsync(connection, runId, startedAt, Registered, taken.Count - rows.Count, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new RegistrationOutcome(Registered, null, detail);
    }

    public async Task<RegistrationOutcome> RetireAsync(
        string candidate,
        string evidence,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var rows = await RowsAsync(connection, cancellation);

        if (RetirementRefusal(rows, candidate, evidence, startedAt, ShortlistSeries.Reasons) is { } refusal)
        {
            await transaction.RollbackAsync(cancellation);
            await RecordAsync(connection, runId, startedAt, Refused, 0, refusal, cancellation);

            return new RegistrationOutcome(Refused, null, refusal);
        }

        // The retiring row carries the retired candidate's own rule, test,
        // evaluator and version rather than blanks, because the row has to say
        // what was withdrawn and a reader of the register alone is the person who
        // needs to know it.
        var standing = rows.Last(row => row.Event == CandidateFamily.Registered
            && string.Equals(row.Candidate, candidate, StringComparison.Ordinal));

        var id = await AppendAsync(
            connection,
            rows,
            candidate,
            standing.Rule,
            standing.Test,
            standing.Evaluator,
            standing.Parameters,
            standing.EvaluatorVersion,
            Retired,
            retires: candidate,
            startedAt,
            evidence,
            cancellation);

        var detail = $"retired '{candidate}' as {id}, on the evidence: {evidence}";

        await RecordAsync(connection, runId, startedAt, Retired, 1, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new RegistrationOutcome(Retired, id, detail);
    }

    // A retirement and the registration that replaces it at one instant, with the caller's own write in
    // the same transaction, or none of the three. What a shape acceptance writes when the live filter's
    // candidate stands: the accepted settings are a new candidate, and the version they open is the
    // caller's row, so the register and the version can never disagree about which settings are live.
    // see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts
    public async Task<RegistrationOutcome> ReplaceAsync(
        string retire,
        Registration with,
        string evidence,
        Func<SqliteConnection, SqliteTransaction, CancellationToken, Task> alongside,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var rows = await RowsAsync(connection, cancellation);

        if (RetirementRefusal(rows, retire, evidence, startedAt, ShortlistSeries.Reasons) is { } refusal)
        {
            await transaction.RollbackAsync(cancellation);
            await RecordAsync(connection, runId, startedAt, Refused, 0, refusal, cancellation);

            return new RegistrationOutcome(Refused, null, refusal);
        }

        var standing = rows.Last(row => row.Event == CandidateFamily.Registered
            && string.Equals(row.Candidate, retire, StringComparison.Ordinal));

        // The registration is judged against the register as it will stand once the retirement is in it.
        var afterRetiring = rows
            .Append(new RegisterRow(
                rows.Count == 0 ? 1 : rows.Max(row => row.Id) + 1,
                retire, standing.Rule, standing.Test, standing.Evaluator, standing.Parameters, standing.EvaluatorVersion,
                Retired, retire, startedAt, evidence))
            .ToArray();

        if ((Unstated(with.Rule, with.Test) ?? Refusal(afterRetiring, with.Candidate, with.Evaluator, with.Parameters, startedAt)) is { } refused)
        {
            await transaction.RollbackAsync(cancellation);

            var said = $"'{with.Candidate}' was refused, so '{retire}' was not retired either: {refused}";

            await RecordAsync(connection, runId, startedAt, Refused, 0, said, cancellation);

            return new RegistrationOutcome(Refused, null, said);
        }

        var retired = await AppendAsync(
            connection, rows, retire, standing.Rule, standing.Test, standing.Evaluator, standing.Parameters,
            standing.EvaluatorVersion, Retired, retires: retire, startedAt, evidence, cancellation);

        var carried = CandidateEvaluators.Find(with.Evaluator)!;
        var registered = await AppendAsync(
            connection, afterRetiring, with.Candidate, with.Rule, with.Test, with.Evaluator,
            CandidateEvaluator.Write(with.Parameters), carried.Version, Registered, retires: null, startedAt, evidence: null, cancellation);

        await alongside(connection, transaction, cancellation);

        var detail = $"retired '{retire}' as {retired} and registered '{with.Candidate}' as {registered} at one instant, on the evidence: {evidence}";

        await RecordAsync(connection, runId, startedAt, Registered, 2, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new RegistrationOutcome(Registered, registered, detail);
    }

    // Why a retirement is refused, or null. A candidate standing registered is retired
    // whatever the live reasons carry, so a promoted candidate can leave the family.
    // see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone
    public static string? RetirementRefusal(
        IReadOnlyList<RegisterRow> rows,
        string candidate,
        string evidence,
        DateTimeOffset at,
        IReadOnlyCollection<string> liveReasons)
    {
        if (string.IsNullOrWhiteSpace(evidence))
        {
            return $"'{candidate}' is not retired on no evidence. A retirement states the figures that produced it.";
        }

        if (CandidateFamily.StandsAt(rows, candidate, at))
        {
            return null;
        }

        return LiveReasonRefusal(candidate, liveReasons)
            ?? $"'{candidate}' does not stand registered, so there is nothing to retire. A retirement " +
               "names a candidate the register holds, and one naming nothing would leave the divisor " +
               "reading as though something had been withdrawn.";
    }

    // A registration with no rule or no test is a row that does not say what was registered.
    public static string? Unstated(string rule, string test) =>
        string.IsNullOrWhiteSpace(rule) || string.IsNullOrWhiteSpace(test)
            ? "a registration states its rule and its test, and a row missing either does not say what was registered."
            : null;

    public static string? LiveReasonRefusal(string candidate) => LiveReasonRefusal(candidate, ShortlistSeries.Reasons);

    // A live reason's name, refused as a registration and, where it does not stand registered,
    // as a retirement: a live reason is section 11's and the code's, never a register row.
    // see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone
    public static string? LiveReasonRefusal(string candidate, IReadOnlyCollection<string> liveReasons) =>
        liveReasons.Contains(candidate.Trim(), StringComparer.OrdinalIgnoreCase)
            ? $"'{candidate}' is a live reason, which is not a row in the register. A live reason is retired " +
              "only by a change to section 11 and the code's reasons together, once its record holds " +
              FormattableString.Invariant($"{ReasonVerdict.MinimumBeforeALiveReasonIsRetired} resolved setups, ") +
              "and a candidate under its name would give that name two meanings."
            : null;

    // Why a registration is refused, or null. Apart from the write so the check
    // reads the same reader the registrar does rather than a copy of it, and so
    // each refusal can be put to it over constructed rows.
    public static string? Refusal(
        IReadOnlyList<RegisterRow> rows,
        string candidate,
        string evaluator,
        IReadOnlyDictionary<string, double> parameters,
        DateTimeOffset at)
    {
        if (LiveReasonRefusal(candidate) is { } live)
        {
            return live;
        }

        if (CandidateFamily.StandsAt(rows, candidate, at))
        {
            return
                $"'{candidate}' already stands registered. A registered candidate is never edited: " +
                "register a new candidate and retire this one, which leaves both rows in the register " +
                "and the divisor counting what was actually tried.";
        }

        var carried = CandidateEvaluators.Find(evaluator);

        if (carried is null)
        {
            return
                $"no evaluator named '{evaluator}' is carried by the code. The register says what will " +
                $"run rather than describing it, so a name nothing implements is refused here rather " +
                $"than on the first night that tried to run it. Carried: {string.Join(", ", CandidateEvaluators.Names)}.";
        }

        var given = parameters.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();
        var wanted = carried.Parameters.OrderBy(name => name, StringComparer.Ordinal).ToArray();

        if (!given.SequenceEqual(wanted, StringComparer.Ordinal))
        {
            return
                $"'{evaluator}' is registered with [{string.Join(", ", given)}] and reads " +
                $"[{string.Join(", ", wanted)}]. A registration whose parameters the evaluator does not " +
                "read is a row that does not say what will run.";
        }

        // A value no comparison can be made against fires on every name-night or on none.
        var notFinite = parameters
            .Where(pair => !double.IsFinite(pair.Value))
            .Select(pair => pair.Key)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        if (notFinite.Length > 0)
        {
            return
                $"'{evaluator}' is registered with [{string.Join(", ", notFinite)}] at a value that is not a finite " +
                "number. A condition compared against one fires on every name-night or on none, which is a row " +
                "that does not say what will run.";
        }

        var standing = CandidateFamily.Standing(rows, at).Count;

        return standing >= CandidateFamily.Maximum
            ? FormattableString.Invariant(
                $"{standing} candidates already stand registered, which is the maximum family of ")
                + FormattableString.Invariant($"{CandidateFamily.Maximum}. ")
                + "Retire one before registering another, because the threshold is divided by the family "
                + "and a family that grows without bound is a correction that stops correcting."
            : null;
    }

    public async Task<IReadOnlyList<RegisterRow>> RowsAsync(CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        return await RowsAsync(connection, cancellation);
    }

    static async Task<IReadOnlyList<RegisterRow>> RowsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReadRegister;

        var rows = new List<RegisterRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new RegisterRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                DateTimeOffset.ParseExact(reader.GetString(9), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return rows;
    }

    static async Task<long> AppendAsync(
        SqliteConnection connection,
        IReadOnlyList<RegisterRow> rows,
        string candidate,
        string rule,
        string test,
        string evaluator,
        string parameters,
        string version,
        string @event,
        string? retires,
        DateTimeOffset at,
        string? evidence,
        CancellationToken cancellation)
    {
        // One past the highest id the register holds, read in the connection that writes it, which the one-writer rule makes safe.
        var id = rows.Count == 0 ? 1 : rows.Max(row => row.Id) + 1;

        await using var command = connection.CreateCommand();

        command.CommandText = Append;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$candidate", candidate);
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$test", test);
        command.Parameters.AddWithValue("$evaluator", evaluator);
        command.Parameters.AddWithValue("$parameters", parameters);
        command.Parameters.AddWithValue("$evaluator_version", version);
        command.Parameters.AddWithValue("$event", @event);
        command.Parameters.AddWithValue("$retires", (object?)retires ?? DBNull.Value);
        command.Parameters.AddWithValue("$registered_at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$evidence", (object?)evidence ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellation);

        return id;
    }

    // A refusal the verb reaches before the registrar does, recorded as the registrar's own are.
    public async Task RecordRefusalAsync(string runId, string refusal, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await RecordAsync(connection, runId, clock.UtcNow, Refused, 0, refusal, cancellation);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        string outcome,
        int written,
        string detail,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", written);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
