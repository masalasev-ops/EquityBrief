using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Paper;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The survivors' desk: an idea the one tester passed is written as a survivor with the line and the early years' cut points it
// is walked by, and admitted at once, registered through the registrar as its setup's variant where it can run there and has
// room, the variant that makes room retired first, or queued with why; and each night every queued survivor is walked to the
// night's setups its line keeps, its picks stored and drawn among no buys, and admitted again, so a slot that opens or a
// setup taught a part its line names registers it. It never registers, approves or retires a running revision.
// see: A survivor that has to queue is walked each night as a record drawn among no buys and carries it when a slot opens
// see: A passed idea registers by itself as its setup's variant where it has room or a variant past its minimum makes room, and queues where none can go
public sealed class SurvivorDesk(IClock clock, string databaseFile, string dataRoot) : IComponent
{
    public const string Stage = "survivors";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Survivor, Touch.Read | Touch.Insert),
            new StoreTouch(Store.SurvivorState, Touch.Read | Touch.Insert),
            new StoreTouch(Store.SurvivorPick, Touch.Read | Touch.Insert),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.SetupNight, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    // The test a survivor's registration states, the one it passed.
    public const string Test = "the one tester: chosen on 2005 to 2018, judged from 2019 at the weekly bar";

    const string InsertSurvivor = @"
        INSERT INTO survivor (notebook_id, index_code, family, line, cuts, passed_on, entered_at, run_id)
        VALUES ($notebook_id, $index_code, $family, $line, $cuts, $passed_on, $entered_at, $run_id) RETURNING id;";

    const string InsertState = @"
        INSERT INTO survivor_state (survivor_id, at, state, candidate, why, run_id)
        VALUES ($survivor_id, $at, $state, $candidate, $why, $run_id);";

    const string Queue = @"
        SELECT s.id, s.index_code, s.family, s.line, s.cuts, s.passed_on
        FROM survivor s
        WHERE (SELECT t.state FROM survivor_state t WHERE t.survivor_id = s.id ORDER BY t.at DESC, t.rowid DESC LIMIT 1) = 'queued'
        ORDER BY s.passed_on, s.id;";

    const string InsertPick = @"
        INSERT INTO survivor_pick (survivor_id, session_date, ticker, last_close, typical_move, run_id)
        SELECT $survivor_id, $session, $ticker, b.close, i.value, $run_id
        FROM bar b JOIN indicator i ON i.ticker = b.ticker AND i.session_date = b.session_date AND i.name = 'atr14'
        WHERE b.ticker = $ticker AND b.session_date = $session
          AND NOT EXISTS (SELECT 1 FROM survivor_pick p WHERE p.survivor_id = $survivor_id AND p.session_date = $session AND p.ticker = $ticker);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    // The name a survivor registers under: its setup's and its own number, never a running revision's.
    public static string NameOf(string family, int survivor) =>
        string.Create(CultureInfo.InvariantCulture, $"the {family} survivor {survivor}");

    // The survivor a registered name is, none for a name no survivor registered under.
    public static long? SurvivorOf(string candidate, string family) =>
        candidate.StartsWith($"the {family} survivor ", StringComparison.Ordinal) && long.TryParse(candidate[$"the {family} survivor ".Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;

    // A survivor written and admitted: its row, then registered, registered in the place of the variant it retires, or
    // queued. A registration the registrar refuses is queued with the registrar's reason.
    public async Task<SurvivorAdmission> AdmitAsync(long notebookId, IdeaLine line, IReadOnlyDictionary<string, double[]> cuts, DateOnly passedOn, string runId, CancellationToken cancellation = default)
    {
        long id;

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);
            await using var command = connection.CreateCommand();

            command.CommandText = InsertSurvivor;
            command.Parameters.AddWithValue("$notebook_id", notebookId);
            command.Parameters.AddWithValue("$index_code", line.Index);
            command.Parameters.AddWithValue("$family", line.Family);
            command.Parameters.AddWithValue("$line", line.Canonical);
            command.Parameters.AddWithValue("$cuts", JsonSerializer.Serialize(cuts));
            command.Parameters.AddWithValue("$passed_on", Text(passedOn));
            command.Parameters.AddWithValue("$entered_at", Stamp(clock.UtcNow));
            command.Parameters.AddWithValue("$run_id", runId);
            id = (long)(await command.ExecuteScalarAsync(cancellation))!;
        }

        return await DecideAsync(id, line, cuts, passedOn, runId, cancellation);
    }

    const string NewestSetups = "SELECT MAX(session_date) FROM setup_night;";

    // Each queued survivor walked to the newest night's setups its line keeps, the night the ledger has just written, and
    // admitted again; the step's row says how many were walked, picked and registered, and is written on a night with none.
    public async Task<string> NightAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var (words, rows) = await WalkTheQueueAsync(runId, cancellation);

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);
            await using var command = connection.CreateCommand();

            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", Stamp(started));
            command.Parameters.AddWithValue("$ended_at", Stamp(clock.UtcNow));
            command.Parameters.AddWithValue("$rows", rows);
            command.Parameters.AddWithValue("$detail", words);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        return words;
    }

    async Task<(string Words, int Rows)> WalkTheQueueAsync(string runId, CancellationToken cancellation)
    {
        var queued = new List<(long Id, IdeaLine Line, IReadOnlyDictionary<string, double[]> Cuts, DateOnly Passed)>();
        DateOnly night;

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            await using (var newest = connection.CreateCommand())
            {
                newest.CommandText = NewestSetups;

                if (await newest.ExecuteScalarAsync(cancellation) is not string stored)
                {
                    return ("no night's setups are stored, so no survivor was walked", 0);
                }

                night = Day(stored);
            }

            await using var command = connection.CreateCommand();

            command.CommandText = Queue;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (IdeaGrammar.Parse(reader.GetString(3)).Line is { } line)
                {
                    queued.Add((reader.GetInt64(0), line, JsonSerializer.Deserialize<Dictionary<string, double[]>>(reader.GetString(4))!, Day(reader.GetString(5))));
                }
            }
        }

        if (queued.Count == 0)
        {
            return ("no survivor queued", 0);
        }

        var (picked, registered) = (0, 0);

        foreach (var (id, line, cuts, passed) in queued)
        {
            picked += await WalkAsync(id, line, cuts, night, runId, cancellation);

            if ((await DecideAsync(id, line, cuts, passed, runId, cancellation)).Outcome == Survivors.Registered)
            {
                registered++;
            }
        }

        return (FormattableString.Invariant($"{queued.Count} survivor(s) queued walked, {picked} pick(s) stored among no buys, {registered} registered"), picked + registered);
    }

    // A queued survivor's picks on a night: the setups of its family on its index that night, those its live setting passed
    // where its line reads them, kept by its line's conditions at its cut points and its cap, each stored with the night's
    // close and typical move.
    async Task<int> WalkAsync(long id, IdeaLine line, IReadOnlyDictionary<string, double[]> cuts, DateOnly night, string runId, CancellationToken cancellation)
    {
        var readings = IdeaTestRun.ReadingsOf(line);
        var setups = new List<IdeaSetup>();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"SELECT ticker{string.Concat(readings.Select(reading => ", " + reading))} FROM setup WHERE index_code = $index AND family = $family AND session_date = $session{(line.Loose ? string.Empty : " AND live_pass = 1")} ORDER BY ticker;";
            command.Parameters.AddWithValue("$index", line.Index);
            command.Parameters.AddWithValue("$family", line.Family);
            command.Parameters.AddWithValue("$session", Text(night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var values = new Dictionary<string, double>(StringComparer.Ordinal);

                for (var at = 0; at < readings.Count; at++)
                {
                    if (!reader.IsDBNull(1 + at))
                    {
                        values[readings[at]] = reader.GetDouble(1 + at);
                    }
                }

                setups.Add(new IdeaSetup(reader.GetString(0), night, values));
            }
        }

        var written = 0;

        foreach (var setup in IdeaTester.Selected(line, setups, cuts))
        {
            await using var command = connection.CreateCommand();

            command.CommandText = InsertPick;
            command.Parameters.AddWithValue("$survivor_id", id);
            command.Parameters.AddWithValue("$session", Text(night));
            command.Parameters.AddWithValue("$ticker", setup.Ticker);
            command.Parameters.AddWithValue("$run_id", runId);
            written += await command.ExecuteNonQueryAsync(cancellation);
        }

        return written;
    }

    async Task<SurvivorAdmission> DecideAsync(long id, IdeaLine line, IReadOnlyDictionary<string, double[]> cuts, DateOnly passed, string runId, CancellationToken cancellation)
    {
        var admission = Survivors.CannotRunYet(line) is { } why
            ? new SurvivorAdmission(Survivors.Queued, null, char.ToUpperInvariant(why[0]) + why[1..] + ".")
            : await RoomAsync(line, passed, cancellation);

        if (admission.Outcome == Survivors.Registered)
        {
            admission = await RegisterAsync(id, line, cuts, admission, passed, runId, cancellation);
        }

        await StateAsync(id, admission.Outcome, admission.Outcome == Survivors.Registered ? NameOf(line.Family, (int)id) : null, admission.Why, runId, cancellation);

        return admission;
    }

    // The setup's slots on the survivor's index as the case page reads them: its running revision and each variant with its
    // nights and its result per $1 risked over the nights it shares with the running revision.
    async Task<SurvivorAdmission> RoomAsync(IdeaLine line, DateOnly night, CancellationToken cancellation)
    {
        var reads = new PaperReads(databaseFile);
        var name = OwnStepCandidate.NameOf(line.Index);
        var worked = await PaperWork.WorkAsync(reads, line.Index, name, line.Index == PaperFiguresRun.Indices[0], night, PaperFiguresRun.SettingsOf(dataRoot));
        var row = PromotionCases.Rows(line.Index, name, worked.Sources, worked.Views, worked.Sessions, [], night).FirstOrDefault(one => one.Family == line.Family);

        if (row is null)
        {
            return new SurvivorAdmission(Survivors.Queued, null, "Its setup holds no running revision on its index.");
        }

        _ = cancellation;

        return Survivors.Admit(line, Survivors.SlotsOf(row));
    }

    async Task<SurvivorAdmission> RegisterAsync(long id, IdeaLine line, IReadOnlyDictionary<string, double[]> cuts, SurvivorAdmission admission, DateOnly night, string runId, CancellationToken cancellation)
    {
        var registrar = new CandidateRegistrar(clock, databaseFile);
        var register = await new PaperReads(databaseFile).RegisteredCandidatesAsync();
        var standing = RuleRegister.Standing(line.Family, line.Index == PaperFiguresRun.Indices[0], OwnStepCandidate.NameOf(line.Index), register, night);
        var (live, variants) = RuleRegister.LiveAndVariants(standing);

        if (live is null)
        {
            return new SurvivorAdmission(Survivors.Queued, null, "Its setup holds no running revision on its index.");
        }

        if (admission.Retires is { } key)
        {
            if (variants.FirstOrDefault(row => RuleRegister.Slug(row.Candidate) == key) is not { } retiring || Survivors.TouchesRunning(retiring.Candidate, live.Candidate))
            {
                return new SurvivorAdmission(Survivors.Queued, null, "The variant that would make room is no longer standing, or is the running revision.");
            }

            var retired = await registrar.RetireAsync(retiring.Candidate, FormattableString.Invariant($"retired to make room for survivor {id}: {admission.Why}"), runId, cancellation);

            if (retired.Id is null)
            {
                return new SurvivorAdmission(Survivors.Queued, null, "The registrar refused the retirement: " + retired.Detail);
            }

            // A survivor retired to make room keeps its record and its place in the notebook; its own row says so.
            if (SurvivorOf(retiring.Candidate, line.Family) is { } former)
            {
                await StateAsync(former, Survivors.Retired, retiring.Candidate, FormattableString.Invariant($"Retired to make room for survivor {id}."), runId, cancellation);
            }
        }

        var candidate = NameOf(line.Family, (int)id);

        if (Survivors.TouchesRunning(candidate, live.Candidate))
        {
            return new SurvivorAdmission(Survivors.Queued, null, "A survivor never registers as a running revision.");
        }

        var outcome = await registrar.RegisterAsync(candidate, line.Words, Test, live.Evaluator, Survivors.Parameters(line, CandidateEvaluator.Read(live.Parameters), cuts), runId, cancellation);

        return outcome.Id is null
            ? new SurvivorAdmission(Survivors.Queued, null, "The registrar refused it: " + outcome.Detail)
            : admission;
    }

    const string Latest = "SELECT state, why FROM survivor_state WHERE survivor_id = $survivor_id ORDER BY at DESC, rowid DESC LIMIT 1;";

    // A state is written where it differs from the survivor's last, so a survivor queued for the same reason night after
    // night holds one row for it.
    async Task StateAsync(long id, string state, string? candidate, string why, string runId, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using (var latest = connection.CreateCommand())
        {
            latest.CommandText = Latest;
            latest.Parameters.AddWithValue("$survivor_id", id);

            await using var reader = await latest.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation) && reader.GetString(0) == state && reader.GetString(1) == why)
            {
                return;
            }
        }

        await using var command = connection.CreateCommand();

        command.CommandText = InsertState;
        command.Parameters.AddWithValue("$survivor_id", id);
        command.Parameters.AddWithValue("$at", Stamp(clock.UtcNow));
        command.Parameters.AddWithValue("$state", state);
        command.Parameters.AddWithValue("$candidate", (object?)candidate ?? DBNull.Value);
        command.Parameters.AddWithValue("$why", why);
        command.Parameters.AddWithValue("$run_id", runId);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
