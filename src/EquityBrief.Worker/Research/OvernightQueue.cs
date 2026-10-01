using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Nights;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace EquityBrief.Worker.Research;

// One name's pass the queue ran: the sections it asked the writer for, what came of
// each, how many local calls it made and how long it took.
public sealed record QueuePass(
    string Ticker,
    string RunId,
    IReadOnlyList<string> Asked,
    IReadOnlyList<WrittenSection> Written,
    IReadOnlyList<UnwrittenSection> NotWritten,
    int ModelCalls,
    double Seconds);

public sealed record QueueOutcome(
    DateOnly Night,
    string Outcome,
    TimeSpan Limit,
    IReadOnlyList<string> Listed,
    IReadOnlyList<string> Queued,
    IReadOnlyList<QueuePass> Completed,
    IReadOnlyList<string> Left,
    string Awake,
    string? Reason,
    QueuePass? Stopped = null)
{
    // Every call the queue's passes made, the one that found the local model not answering
    // included, since a call that was attempted is one the runtime was asked for.
    public int ModelCalls => Completed.Sum(pass => pass.ModelCalls) + (Stopped?.ModelCalls ?? 0);
}

// The overnight queue. After the arithmetic has closed, every name in the index whose
// research is missing or stale, the names on tonight's list first in the swing filter's
// order, then the names one gate short in the order "Close to a buy point" draws them,
// each given a pass of the local lane's sections, until the configured number of
// hours has passed. Every name rather than the listed ones, because the one section the
// queue writes explains the night's figures and every name's page draws them.
// see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
//
// It decides nothing a component already decides and writes no research itself. The
// staleness judge says what stands; the prose writer writes the local lane; the claim
// checker moves what the writer wrote. What the queue owns is the order, the limit, the
// machine held awake, and its own row saying whether it ran and what it left.
// see: The overnight queue is bounded by time, not by a count of names
// see: The overnight run holds the machine awake and reports whether it ran
//
// It hands the writer no document. The night fetches nothing for a name, and a section
// resting on documents rests on ones only a pass that fetched them stored, so such a
// section waits for the pass an open starts, and what the queue writes is what the facts
// file alone supports.
// see: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about
public sealed class OvernightQueue(
    StalenessJudge judge,
    Func<IReadOnlyList<string>, ProseWriter> writerFor,
    ClaimChecker checker,
    IReadOnlyList<string> localLane,
    TimeSpan limit,
    IMachineAwake awake,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.FamilyNight, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.FundamentalReading, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "overnight queue";

    // What the queue came to, which its row's outcome column holds and the run page
    // reads back.
    public const string Ran = "ok";
    public const string StoppedAtItsLimit = "limit";
    public const string Unavailable = "unavailable";

    // The hours after which the queue starts no pass, and the value where configuration
    // states none: section 17's figure, which 6.10 set from the pass it measured on this
    // machine, an hour covering every member of the index at the slowest pass measured.
    public const string HoursKey = "EquityBrief:Queue:Hours";
    public const int DefaultHours = 1;

    // What the queue asks the operating system to hold the machine awake for, which is
    // the words a person reading the machine's own power requests sees.
    public const string AwakeReason = "EquityBrief's overnight queue is writing tonight's drafts";

    // A listing row is written for every name in the index every night, so the night's
    // rows are the index that night, and the names on tonight's list lead in the order
    // the list is drawn in: improving businesses first where the night stored its
    // readings, and the swing filter's own order within each state and on a night that
    // stored none. On a night the families drew the page's list, the stocks it lists lead, in the page's order.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    static readonly string MembersOnNight = @"
        SELECT ticker, listed
        FROM (
            SELECT l.ticker AS ticker,
                   CASE WHEN " + FamilyList.OnTheList("l.ticker", "l.session_date", "g.passed = 1") + @" THEN 1 ELSE 0 END AS listed,
                   IFNULL(" + FamilyList.PlaceOn("l.ticker", "l.session_date") + @", 0) AS place,
                   " + FundamentalState.PlaceIn("f.state") + @" AS state_place,
                   g.rank AS rank
            FROM listing l
            LEFT JOIN gate_result g ON g.ticker = l.ticker AND g.session_date = l.session_date
            LEFT JOIN fundamental_reading f ON f.ticker = l.ticker AND f.session_date = l.session_date
            WHERE l.session_date = $session)
        ORDER BY CASE WHEN listed = 1 THEN 0 ELSE 1 END,
                 place,
                 CASE WHEN listed = 1 THEN state_place ELSE 0 END,
                 rank, ticker;
    ";

    // Each listed member's stored result on the night, as "Close to a buy point" reads it, with the state its
    // reported quarters gave it; a replayed result is none of the night's.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    static readonly string ResultsOnNight = @"
        SELECT g.ticker, g.version, g.market, g.trend, g.setup, g.trigger_pass, g.trade, g.exclusions, g.passed,
               g.gates, g.strength, g.band_strength, f.state
        FROM gate_result g
        JOIN listing l ON l.ticker = g.ticker AND l.session_date = g.session_date
        LEFT JOIN fundamental_reading f ON f.ticker = g.ticker AND f.session_date = g.session_date
        WHERE g.session_date = $session AND g.version <> '" + ReplayedResults.Version + @"';
    ";

    const string SettingsOfVersion = "SELECT settings FROM filter_version WHERE version = $version;";

    const string TriggerEventsUpTo = "SELECT trigger_event FROM gate_result WHERE ticker = $ticker AND session_date <= $session ORDER BY session_date DESC;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, $model_calls, 0, '0', $detail);
    ";

    static readonly IReadOnlyDictionary<string, IReadOnlyList<StoredDocument>> NoDocuments =
        new Dictionary<string, IReadOnlyList<StoredDocument>>(StringComparer.Ordinal);

    // The limit as configuration states it, in whole hours above zero, and the default
    // where it states none. A value that is not one is refused rather than replaced,
    // because a setting that silently became another value does something other than
    // what the file says.
    public static TimeSpan Limit(IConfiguration configuration)
    {
        var value = configuration[HoursKey];

        if (string.IsNullOrWhiteSpace(value))
        {
            return TimeSpan.FromHours(DefaultHours);
        }

        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var hours) && hours > 0
            ? TimeSpan.FromHours(hours)
            : throw new InvalidOperationException(
                $"'{HoursKey}' is '{value}', which is not a whole number of hours above zero. It is read as written " +
                "rather than replaced by the default, because a queue bounded by a figure nobody wrote is not bounded " +
                "by the one in the file.");
    }

    // Each name's pass under a run of its own, so the judge's, the writer's and the
    // checker's rows for one name are one run, and the queue's own row, under the night's
    // run, names every pass it ran.
    public static string PassRunId(string nightRunId, string ticker) =>
        nightRunId + "-queue-" + ticker.ToUpperInvariant();

    public async Task<QueueOutcome> RunAsync(string nightRunId, DateOnly night, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var stopAt = startedAt + limit;
        var today = clock.SessionDateAt(startedAt);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, night, cancellation);
        var listed = members.Where(member => member.Passed > 0).Select(member => member.Ticker).ToArray();

        // The members one gate short follow tonight's list in the order "Close to a buy point" draws them,
        // and every other member follows them.
        // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
        var close = await CloseAsync(connection, night, cancellation);
        var near = close.ToHashSet(StringComparer.Ordinal);

        members =
        [
            .. members.Where(member => member.Passed > 0),
            .. close.Select(ticker => (ticker, 0)),
            .. members.Where(member => member.Passed == 0 && !near.Contains(member.Ticker)),
        ];

        using var held = awake.Hold(AwakeReason);

        // Every name judged first, which spends nothing, so the queue is known before a
        // pass starts and a name left at the limit is one the queue wanted.
        //
        // A name is queued for the lane's sections a pass would write that rest on no
        // document. One resting on documents is not asked for, since the queue hands the
        // writer none and a pass asked for it could only report that, so a name whose only
        // work is such a section is not queued at all, rather than counted every night as a
        // pass that wrote nothing.
        var queued = new List<(string Ticker, string RunId, string[] Sections)>();

        foreach (var (ticker, _) in members)
        {
            var runId = PassRunId(nightRunId, ticker);
            var verdict = await judge.JudgeAsync(ticker, refresh: false, runId, cancellation);
            var wanted = localLane
                .Where(section => !ClaimRules.IsResearched(section)
                    && ResearchRunner.Warranted(
                        section,
                        verdict.Sections.FirstOrDefault(standing => string.Equals(standing.Section, section, StringComparison.Ordinal)),
                        verdict,
                        today))
                .ToArray();

            if (wanted.Length > 0)
            {
                queued.Add((ticker, runId, wanted));
            }
        }

        var completed = new List<QueuePass>();
        var left = new List<string>();
        var outcome = Ran;
        string? reason = null;
        QueuePass? stopped = null;

        for (var at = 0; at < queued.Count; at++)
        {
            var (ticker, runId, sections) = queued[at];

            // The bound is read before a pass starts, and a pass started inside it runs to
            // its end, because one cut off inside a call leaves a draft the checker never
            // read.
            // see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed
            if (clock.UtcNow >= stopAt)
            {
                left.AddRange(queued.Skip(at).Select(one => one.Ticker));
                outcome = StoppedAtItsLimit;

                break;
            }

            var passStarted = clock.UtcNow;
            var prose = await writerFor(sections).WriteAsync(ticker, NoDocuments, runId, cancellation: cancellation);

            // The local model not answering stops the queue rather than the pass alone,
            // since a runtime that did not answer this name does not answer the next, and
            // the row says the queue could not run and what it left.
            if (prose.Unavailable)
            {
                left.AddRange(queued.Skip(at).Select(one => one.Ticker));
                outcome = Unavailable;
                reason = prose.NotWritten
                    .Select(section => section.Reason)
                    .FirstOrDefault(line => line.StartsWith(ProseWriter.Unavailable, StringComparison.Ordinal))
                    ?? ProseWriter.Unavailable;

                // The pass that found out, named on the row with the call it attempted, so
                // every model call the night made sits on a pass the queue's row names.
                stopped = new QueuePass(ticker, runId, sections, prose.Written, prose.NotWritten, prose.ModelCalls, (clock.UtcNow - passStarted).TotalSeconds);

                break;
            }

            var written = prose.Written.ToList();
            var notWritten = prose.NotWritten.ToList();
            var calls = prose.ModelCalls;

            if (written.Count > 0)
            {
                // The retries, as a pass on demand takes them: a section the checker
                // refused on this pass is written again, told why, until it is accepted
                // or the checker leaves it out at its last retry.
                // see: A retry names each thing the check refused, and a section refused on its third retry is left out
                var check = await checker.RunAsync(runId, cancellation: cancellation);

                for (var round = 2; round <= 1 + checker.RetriesAllowed; round++)
                {
                    var retry = check.Checked
                        .Where(section => string.Equals(section.Subject, ticker, StringComparison.Ordinal)
                            && string.Equals(section.Status, ClaimChecker.Rejected, StringComparison.Ordinal))
                        .Select(section => section.Section)
                        .Distinct(StringComparer.Ordinal)
                        .ToArray();

                    if (retry.Length == 0)
                    {
                        break;
                    }

                    var again = await writerFor(retry).WriteAsync(ticker, NoDocuments, runId, ResearchRunner.Round(round), cancellation);

                    written.AddRange(again.Written);
                    notWritten.AddRange(again.NotWritten);
                    calls += again.ModelCalls;

                    if (again.Written.Count == 0)
                    {
                        break;
                    }

                    check = await checker.RunAsync(runId, ResearchRunner.Round(round), cancellation);
                }
            }

            completed.Add(new QueuePass(ticker, runId, sections, written, notWritten, calls, (clock.UtcNow - passStarted).TotalSeconds));
        }

        var result = new QueueOutcome(night, outcome, limit, listed, [.. queued.Select(one => one.Ticker)], completed, left, held.Line, reason, stopped);

        await using var record = connection.CreateCommand();

        record.CommandText = AppendRun;
        record.Parameters.AddWithValue("$run_id", nightRunId);
        record.Parameters.AddWithValue("$stage", Stage);
        record.Parameters.AddWithValue("$started_at", Instant(startedAt));
        record.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        record.Parameters.AddWithValue("$outcome", result.Outcome);
        record.Parameters.AddWithValue("$model_calls", result.ModelCalls);
        record.Parameters.AddWithValue("$detail", Detail(result));

        await record.ExecuteNonQueryAsync(cancellation);

        return result;
    }

    // The row's detail, as JSON, which is what the run page reads back: the night, what
    // the queue came to and why, the limit, the names listed and queued, every pass it ran
    // with its run and what it wrote, the names it left, and whether the machine was held.
    public static string Detail(QueueOutcome outcome) =>
        JsonSerializer.Serialize(new
        {
            night = outcome.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            outcome = outcome.Outcome,
            reason = outcome.Reason,
            limitHours = outcome.Limit.TotalHours,
            listed = outcome.Listed,
            queued = outcome.Queued,
            completed = outcome.Completed.Select(Pass),
            stopped = outcome.Stopped is { } stopped ? Pass(stopped) : null,
            left = outcome.Left,
            awake = outcome.Awake,
        });

    static object Pass(QueuePass pass) => new
    {
        ticker = pass.Ticker,
        runId = pass.RunId,
        asked = pass.Asked,
        written = pass.Written.Select(section => section.Section),
        notWritten = pass.NotWritten.Select(section => new { section = section.Section, reason = section.Reason }),
        modelCalls = pass.ModelCalls,
        seconds = pass.Seconds,
    };

    static async Task<IReadOnlyList<(string Ticker, int Passed)>> MembersAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOnNight;
        command.Parameters.AddWithValue("$session", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = new List<(string, int)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add((reader.GetString(0), reader.GetInt32(1)));
        }

        return members;
    }

    // The members "Close to a buy point" draws on the night, in its order: each result one gate short with
    // nothing excluding it, measured against the settings of the version it was stored under, and a missed
    // trigger against the firings the member's own results show.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    static async Task<IReadOnlyList<string>> CloseAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        var session = night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var results = new List<(NearMissResult Result, string Version, string? State)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ResultsOnNight;
            command.Parameters.AddWithValue("$session", session);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                results.Add((
                    new NearMissResult(
                        reader.GetString(0),
                        reader.GetInt32(2) == 1,
                        reader.GetInt32(3) == 1,
                        reader.GetInt32(4) == 1,
                        reader.GetInt32(5) == 1,
                        reader.GetInt32(6) == 1,
                        (JsonSerializer.Deserialize<string[]>(reader.GetString(7)) ?? []).Length,
                        reader.GetInt32(8) == 1,
                        reader.GetString(9),
                        reader.IsDBNull(10) ? null : reader.GetDouble(10),
                        reader.IsDBNull(11) ? null : reader.GetInt32(11)),
                    reader.GetString(1),
                    reader.IsDBNull(12) ? null : reader.GetString(12)));
            }
        }

        var settings = new Dictionary<string, FilterSettings>(StringComparer.Ordinal);
        var close = new List<(NearMissResult Result, MissedGate Missed, string? State)>();

        foreach (var (result, version, state) in results.Where(one => NearMiss.MissedOne(one.Result)))
        {
            if (!settings.TryGetValue(version, out var held))
            {
                await using var command = connection.CreateCommand();

                command.CommandText = SettingsOfVersion;
                command.Parameters.AddWithValue("$version", version);

                held = settings[version] = await command.ExecuteScalarAsync(cancellation) is string json
                    ? FilterSettings.Read(json)
                    : FilterSettings.Proposed;
            }

            int? fired = null;

            if (!result.Trigger)
            {
                await using var command = connection.CreateCommand();

                command.CommandText = TriggerEventsUpTo;
                command.Parameters.AddWithValue("$ticker", result.Ticker);
                command.Parameters.AddWithValue("$session", session);

                var events = new List<bool?>();

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    events.Add(reader.IsDBNull(0) ? null : reader.GetInt64(0) == 1);
                }

                fired = NearMiss.FiredSessionsBefore(events);
            }

            if (NearMiss.MissOf(result, held, fired) is { } missed)
            {
                close.Add((result, missed, state));
            }
        }

        return
        [
            .. NearMiss.Ordered(
                close,
                one => one.Result.Ticker,
                one => one.Missed.Distance,
                one => FundamentalState.Place(one.State),
                one => NearMiss.RewardToRisk(one.Result.Gates),
                one => one.Result.Strength,
                one => one.Result.BandStrength)
                .Select(one => one.Result.Ticker),
        ];
    }

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
