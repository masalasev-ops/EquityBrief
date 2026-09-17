using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
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

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var rows = await RowsAsync(connection, cancellation);

        if (Refusal(rows, candidate, evaluator, parameters, startedAt) is { } refusal)
        {
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

        return new RegistrationOutcome(Registered, id, detail);
    }

    public async Task<RegistrationOutcome> RetireAsync(
        string candidate,
        string evidence,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var rows = await RowsAsync(connection, cancellation);

        if (LiveReasonRefusal(candidate) is { } live)
        {
            await RecordAsync(connection, runId, startedAt, Refused, 0, live, cancellation);

            return new RegistrationOutcome(Refused, null, live);
        }

        if (!CandidateFamily.StandsAt(rows, candidate, startedAt))
        {
            var refusal =
                $"'{candidate}' does not stand registered, so there is nothing to retire. A retirement " +
                "names a candidate the register holds, and one naming nothing would leave the divisor " +
                "reading as though something had been withdrawn.";

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

        return new RegistrationOutcome(Retired, id, detail);
    }

    // A live reason's name, refused in both directions.
    //
    // The six live reasons are section 11's and the code's, a family of their own,
    // and none of them is a row here. Registering a candidate under one's name
    // would give a retirement of that name two meanings, and retiring one through
    // this door would withdraw a live reason on the strength of a register row.
    // A live reason is retired only by a person changing section 11 and the code
    // together, once its record holds the higher floor section 17 states.
    // see: An unresolved setup is never a win
    public static string? LiveReasonRefusal(string candidate) =>
        ShortlistSeries.Reasons.Contains(candidate.Trim(), StringComparer.OrdinalIgnoreCase)
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
        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
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
