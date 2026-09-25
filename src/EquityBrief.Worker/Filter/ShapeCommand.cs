using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Candidates;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

public sealed record ShapeOutcome(string Outcome, string? Version, string Detail);

// The `shape` verb: a shape proposal accepted or rejected, or the settings the operator ruled opened
// as a version. The one writer of the filter's versions and of a proposal's decision, and never a
// night step, because an acceptance is a decision a person takes: a filter that moved its own
// thresholds would be tuning itself toward whatever it last saw.
//
// An acceptance closes the open version and opens the next at one instant. Where the live filter's
// candidate stands registered, the same write retires it and registers the accepted settings through
// the registrar, since the settings a candidate runs with never change while it runs, and the bound
// on what that costs is held by `SwingFamily`. A rejection writes its decision and reason on the
// proposal's row and nothing else. Every attempt, a refusal included, is one row on the run log.
// see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts
// see: The shape proposer moves one setting a gate, nearest first, and never applies what it proposes
public sealed class ShapeCommand : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ShapeProposal, Touch.Read | Touch.Update),
            new StoreTouch(Store.FilterVersion, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Name = "shape";

    public const string Stage = "shape";

    public const string RunPrefix = "shape-";

    public const string Accepted = "accepted";

    public const string Rejected = "rejected";

    public const string Refused = "refused";

    public static IReadOnlyList<VerbForm> Forms { get; } =
    [
        new("--accept", ["--accept"], ["--restarts"], []),
        new("--settings", ["--settings", "--evidence"], ["--trade", "--restarts"], []),
        new("--reject", ["--reject", "--reason"], [], []),
    ];

    const string OpenVersion = "SELECT version, settings FROM filter_version WHERE closed_at IS NULL ORDER BY opened_at DESC LIMIT 1;";

    const string VersionCount = "SELECT COUNT(*) FROM filter_version;";

    const string CloseVersion = "UPDATE filter_version SET closed_at = $at WHERE closed_at IS NULL;";

    const string OpenNext = @"
        INSERT INTO filter_version (version, settings, opened_at, closed_at, evidence)
        VALUES ($version, $settings, $at, NULL, $evidence);
    ";

    const string ProposalRow = "SELECT version, ordinary, settings, decision FROM shape_proposal WHERE id = $id;";

    const string Decide = @"
        UPDATE shape_proposal
        SET decision = $decision, decided_at = $at, reason = $reason, opened = $opened
        WHERE id = $id AND decision IS NULL;
    ";

    const string NewestNight = "SELECT MAX(session_date) FROM listing;";

    // The live candidate's fired setups and the first night that evaluated it, read as the run page's
    // record region reads every candidate's, so the blocks the command holds an acceptance to are the
    // blocks the page draws beside the proposal.
    const string LiveSetups = @"
        SELECT l.session_date, f.outcome,
               f.null_win, f.null_win_at_sensitivity, f.break_even, f.return_pct, f.planned_risk, f.on_earnings
        FROM listing l, json_each(COALESCE(l.shadow_reasons, '{}'), '$.candidates') c
        LEFT JOIN forward_return f
            ON f.ticker = l.ticker AND f.session_date = l.session_date AND f.horizon = $horizon
        WHERE json_extract(c.value, '$.fired') = 1 AND json_extract(c.value, '$.candidate') = $candidate
        ORDER BY l.session_date;
    ";

    const string LiveFirstNight = @"
        SELECT MIN(session_date) FROM (
            SELECT l.session_date
            FROM listing l, json_each(COALESCE(l.shadow_reasons, '{}'), '$.candidates') c
            WHERE json_extract(c.value, '$.candidate') = $candidate
            UNION
            SELECT l.session_date
            FROM listing l, json_each(COALESCE(l.shadow_reasons, '{}'), '$.skipped') s
            WHERE json_extract(s.value, '$.candidate') = $candidate);
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

    public ShapeCommand(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The run id, to the ten-millionth of a second, so two commands a second apart never share one.
    public static string RunIdAt(DateTimeOffset at) => FormattableString.Invariant($"{RunPrefix}{at:yyyyMMddTHHmmss.fffffffZ}");

    public static async Task<int> RunAsync(string[] args, IClock clock, string databaseFile, TextWriter output, TextWriter error)
    {
        if (VerbStore.Refusal(databaseFile) is { } refused)
        {
            await error.WriteLineAsync("shape: " + refused);

            return 1;
        }

        var command = new ShapeCommand(clock, databaseFile);
        var outcome = await command.FormAsync(args, RunIdAt(clock.UtcNow));

        await (outcome.Outcome == Refused ? error : output).WriteLineAsync("shape: " + outcome.Detail);

        return outcome.Outcome == Refused ? 1 : 0;
    }

    async Task<ShapeOutcome> FormAsync(string[] args, string runId)
    {
        var (form, refusal) = VerbArguments.FormOf(args, Forms);

        if (form is null)
        {
            return await RefuseAsync(runId, refusal!);
        }

        string? Given(string flag) => VerbArguments.Value(args, flag);

        int? restarts = null;

        if (Given("--restarts") is { } stated)
        {
            if (!int.TryParse(stated, NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                return await RefuseAsync(runId, $"--restarts '{stated}' is not a count of blocks.");
            }

            restarts = count;
        }

        if (form.Flag == "--reject")
        {
            return Id(Given("--reject")!) is { } rejected
                ? await RejectAsync(rejected, Given("--reason")!, runId)
                : await RefuseAsync(runId, $"'{Given("--reject")}' is not a proposal's number.");
        }

        if (form.Flag == "--accept")
        {
            return Id(Given("--accept")!) is { } accepted
                ? await AcceptProposalAsync(accepted, restarts, runId)
                : await RefuseAsync(runId, $"'{Given("--accept")}' is not a proposal's number.");
        }

        return await AcceptSettingsAsync(Given("--settings")!, Given("--trade"), Given("--evidence")!, restarts, runId);
    }

    // A proposal accepted: its settings opened as the next version, the proposal marked with it.
    public async Task<ShapeOutcome> AcceptProposalAsync(long id, int? restarts, string runId, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var open = await OpenVersionAsync(connection, cancellation);

        if (await ProposalAsync(connection, id, cancellation) is not { } proposal)
        {
            return await RefuseAsync(runId, FormattableString.Invariant($"no shape proposal {id} is stored."));
        }

        if (proposal.Decision is { } decided)
        {
            return await RefuseAsync(runId, FormattableString.Invariant($"shape proposal {id} was {decided} already, and a decision is never written twice."));
        }

        if (proposal.Version != (open?.Version ?? ShapeClock.NoVersion))
        {
            return await RefuseAsync(
                runId,
                FormattableString.Invariant($"shape proposal {id} was written for filter version {proposal.Version}, and {open?.Version ?? "no version"} is open now, so it proposes a move from settings no longer held."));
        }

        return await AcceptAsync(
            connection,
            open,
            proposal.Settings,
            FormattableString.Invariant($"shape proposal {id}, over {proposal.Ordinary} ordinary nights under filter version {proposal.Version}"),
            id,
            restarts,
            runId,
            cancellation);
    }

    // Settings the operator ruled, opened as the next version: the open version's settings, or
    // section 17's proposed values where none is open, with the named ones replaced.
    public async Task<ShapeOutcome> AcceptSettingsAsync(string given, string? trade, string evidence, int? restarts, string runId, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        var open = await OpenVersionAsync(connection, cancellation);
        FilterSettings settings;

        try
        {
            settings = With(open?.Settings ?? FilterSettings.Proposed, VerbArguments.Parameters(given), trade);
        }
        catch (FormatException unreadable)
        {
            return await RefuseAsync(runId, unreadable.Message);
        }

        return await AcceptAsync(connection, open, settings, evidence, null, restarts, runId, cancellation);
    }

    // A proposal rejected: its decision and reason, and nothing else.
    public async Task<ShapeOutcome> RejectAsync(long id, string reason, string runId, CancellationToken cancellation = default)
    {
        await using var connection = await OpenAsync(cancellation);

        if (await ProposalAsync(connection, id, cancellation) is not { } proposal)
        {
            return await RefuseAsync(runId, FormattableString.Invariant($"no shape proposal {id} is stored."));
        }

        if (proposal.Decision is { } decided)
        {
            return await RefuseAsync(runId, FormattableString.Invariant($"shape proposal {id} was {decided} already, and a decision is never written twice."));
        }

        var startedAt = clock.UtcNow;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await DecideAsync(connection, transaction, id, Rejected, reason, null, startedAt, cancellation);

        var detail = FormattableString.Invariant($"shape proposal {id} rejected, and no setting, version or registration changed: {reason}");

        await RecordAsync(connection, transaction, runId, startedAt, Rejected, 1, detail, cancellation);
        await transaction.CommitAsync(cancellation);

        return new ShapeOutcome(Rejected, null, detail);
    }

    async Task<ShapeOutcome> AcceptAsync(
        SqliteConnection connection,
        (string Version, FilterSettings Settings)? open,
        FilterSettings settings,
        string evidence,
        long? proposal,
        int? restarts,
        string runId,
        CancellationToken cancellation)
    {
        var startedAt = clock.UtcNow;

        if (Unusable(settings) is { } unusable)
        {
            return await RefuseAsync(runId, unusable);
        }

        if (open is { } held && held.Settings.Write() == settings.Write())
        {
            return await RefuseAsync(runId, $"filter version {held.Version} already holds these settings, so accepting them would change nothing.");
        }

        var registrar = new CandidateRegistrar(clock, databaseFile);
        var rows = await registrar.RowsAsync(cancellation);
        var live = SwingFamily.Standing(rows, startedAt);
        var blocks = live is null ? 0 : await LiveBlocksAsync(connection, live.Candidate, cancellation);

        if (SwingFamily.BoundRefusal(live, SwingFamily.AcceptedWhileLive(rows), blocks, restarts) is { } bound)
        {
            return await RefuseAsync(runId, bound + " Nothing was changed.");
        }

        var next = FormattableString.Invariant($"{await CountAsync(connection, cancellation) + 1}");
        var opened = FormattableString.Invariant($"filter version {next} opened{(open is { } closed ? $", closing {closed.Version}" : string.Empty)}, on the evidence: {evidence}");

        async Task Write(SqliteConnection writing, SqliteTransaction transaction, CancellationToken token)
        {
            await ExecuteAsync(writing, transaction, CloseVersion, token, ("$at", Stamp(startedAt)));
            await ExecuteAsync(
                writing,
                transaction,
                OpenNext,
                token,
                ("$version", next),
                ("$settings", settings.Write()),
                ("$at", Stamp(startedAt)),
                ("$evidence", evidence));

            if (proposal is { } id)
            {
                await DecideAsync(writing, transaction, id, Accepted, null, next, startedAt, token);
            }
        }

        if (live is null)
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            await Write(connection, transaction, cancellation);

            var detail = opened + "; no live filter candidate is registered, so it restarts nothing";

            await RecordAsync(connection, transaction, runId, startedAt, Accepted, 1, detail, cancellation);
            await transaction.CommitAsync(cancellation);

            return new ShapeOutcome(Accepted, next, detail);
        }

        var parameters = Carried(Registered(live.Parameters), settings);
        var replaced = await registrar.ReplaceAsync(
            live.Candidate,
            new Registration(SwingFamily.LiveCandidate(next), live.Rule, live.Test, live.Evaluator, parameters),
            FormattableString.Invariant($"a shape acceptance opening filter version {next}, restarting {blocks} non-empty block(s): {evidence}"),
            Write,
            runId,
            cancellation);

        if (replaced.Outcome == CandidateRegistrar.Refused)
        {
            return await RefuseAsync(runId, replaced.Detail + " Nothing was changed.");
        }

        var said = FormattableString.Invariant($"{opened}; {replaced.Detail}, restarting {blocks} non-empty block(s)");

        await RecordAsync(connection, null, runId, startedAt, Accepted, 1, said, cancellation);

        return new ShapeOutcome(Accepted, next, said);
    }

    // The live candidate's parameters with every one the settings name carried at the accepted value,
    // and any other as it stood, so the registration reads the parameters its evaluator reads.
    static IReadOnlyDictionary<string, double> Carried(IReadOnlyDictionary<string, double> standing, FilterSettings settings)
    {
        var accepted = SwingFilterRule.ParametersOf(settings);

        return standing.ToDictionary(
            pair => pair.Key,
            pair => accepted.TryGetValue(pair.Key, out var value) ? value : pair.Value,
            StringComparer.Ordinal);
    }

    // The live candidate's parameters as its register row holds them, each a name and a number. Read here
    // rather than through the evaluators' reader, since this carries them into a registration and runs
    // no evaluation.
    static IReadOnlyDictionary<string, double> Registered(string parameters)
    {
        using var document = JsonDocument.Parse(parameters);

        return document.RootElement.EnumerateObject()
            .ToDictionary(parameter => parameter.Name, parameter => parameter.Value.GetDouble(), StringComparer.Ordinal);
    }

    // The settings with the named ones replaced: a name the settings do not hold is refused rather than
    // dropped, since a version opened without the value asked for is not the version asked for.
    static FilterSettings With(FilterSettings settings, IReadOnlyDictionary<string, double> given, string? trade)
    {
        var numbers = Numbers(settings).ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

        foreach (var (name, value) in given)
        {
            if (!numbers.ContainsKey(name))
            {
                throw new FormatException($"'{name}' is not a setting the filter holds. It holds {string.Join(", ", numbers.Keys)}.");
            }

            numbers[name] = value;
        }

        var input = trade ?? (settings.Trade == TradeInput.Ladder ? "ladder" : "swing");

        if (input is not ("ladder" or "swing"))
        {
            throw new FormatException($"--trade '{trade}' is neither 'ladder' nor 'swing'.");
        }

        var written = numbers.ToDictionary(pair => pair.Key, pair => (object)pair.Value, StringComparer.Ordinal);
        written["trade"] = input;

        return FilterSettings.Read(JsonSerializer.Serialize(written));
    }

    static IReadOnlyDictionary<string, double> Numbers(FilterSettings settings)
    {
        using var document = JsonDocument.Parse(settings.Write());

        return document.RootElement.EnumerateObject()
            .Where(setting => setting.Value.ValueKind == JsonValueKind.Number)
            .ToDictionary(setting => setting.Name, setting => setting.Value.GetDouble(), StringComparer.Ordinal);
    }

    // Why settings cannot be opened as a version, or null: a value no comparison can be made against,
    // or a range whose low end sits above its high.
    static string? Unusable(FilterSettings settings)
    {
        if (Numbers(settings).Where(pair => !double.IsFinite(pair.Value)).Select(pair => pair.Key).FirstOrDefault() is { } infinite)
        {
            return $"'{infinite}' is not a finite number, and a gate compared against one passes every name or none.";
        }

        if (settings.DepthLow > settings.DepthHigh)
        {
            return "depthLow sits above depthHigh, so no pullback's depth falls between them.";
        }

        return settings.StopLow > settings.StopHigh
            ? "stopLow sits above stopHigh, so no trade's stop falls between them."
            : null;
    }

    // The non-empty blocks the live candidate's clock has run by the newest night the listings hold,
    // counted by the arithmetic the run page counts them with.
    static async Task<int> LiveBlocksAsync(SqliteConnection connection, string candidate, CancellationToken cancellation)
    {
        DateOnly? night;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestNight;
            night = await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
        }

        if (night is not { } until)
        {
            return 0;
        }

        DateOnly? first;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LiveFirstNight;
            command.Parameters.AddWithValue("$candidate", candidate);
            first = await command.ExecuteScalarAsync(cancellation) is string earliest ? Date(earliest) : null;
        }

        var setups = new List<CandidateSetup>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LiveSetups;
            command.Parameters.AddWithValue("$candidate", candidate);
            command.Parameters.AddWithValue("$horizon", ForwardReturnSeries.Setup);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                double? Real(int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);

                setups.Add(new CandidateSetup(
                    Date(reader.GetString(0)),
                    reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                    Real(2),
                    Real(3),
                    Real(4),
                    Real(5),
                    Real(6),
                    !reader.IsDBNull(7) && reader.GetInt64(7) == 1));
            }
        }

        return SwingFamily.Blocks(setups, first, until);
    }

    async Task<SqliteConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        return connection;
    }

    static async Task<(string Version, FilterSettings Settings)?> OpenVersionAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = OpenVersion;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) ? (reader.GetString(0), FilterSettings.Read(reader.GetString(1))) : null;
    }

    static async Task<long> CountAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = VersionCount;

        return (long)(await command.ExecuteScalarAsync(cancellation))!;
    }

    sealed record StoredProposal(string Version, int Ordinary, FilterSettings Settings, string? Decision);

    static async Task<StoredProposal?> ProposalAsync(SqliteConnection connection, long id, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ProposalRow;
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new StoredProposal(reader.GetString(0), reader.GetInt32(1), FilterSettings.Read(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3))
            : null;
    }

    static Task DecideAsync(SqliteConnection connection, SqliteTransaction transaction, long id, string decision, string? reason, string? opened, DateTimeOffset at, CancellationToken cancellation) =>
        ExecuteAsync(
            connection,
            transaction,
            Decide,
            cancellation,
            ("$id", id),
            ("$decision", decision),
            ("$at", Stamp(at)),
            ("$reason", reason),
            ("$opened", opened));

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellation, params (string Name, object? Value)[] values)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in values)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task<ShapeOutcome> RefuseAsync(string runId, string refusal)
    {
        await using var connection = await OpenAsync(CancellationToken.None);

        await RecordAsync(connection, null, runId, clock.UtcNow, Refused, 0, refusal, CancellationToken.None);

        return new ShapeOutcome(Refused, null, refusal);
    }

    async Task RecordAsync(SqliteConnection connection, SqliteTransaction? transaction, string runId, DateTimeOffset startedAt, string outcome, int written, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Stamp(startedAt));
        command.Parameters.AddWithValue("$ended_at", Stamp(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", written);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static long? Id(string given) => long.TryParse(given, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Stamp(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
