using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Worker.Shortlist;

public sealed record ShortlistOutcome(int MembersConsidered, int RowsWritten, int Fired, int ReasonsFired);

// The shortlist builder. Evaluates the six reasons and records which fired, with
// the values that made each one true.
//
// A row is written for every index member every night, whether or not a reason
// fired. The population is the index rather than the names with bars, and a
// name the night computed nothing for gets a row saying so: a shadow candidate
// has to be evaluated on the nights it would have fired, and most of those are
// nights no live reason surfaced that name.
// see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
//
// It selects on chart state alone, with no fundamentals and no model in the
// decision, which is contradiction L's resolution: what it reads is levels,
// indicators, ladders, the calendar, the facts file and the bar store, being
// tonight's close and today's volume, and four of the six reasons need those.
// see: Tonight's list is built from stated conditions, not a score
public sealed class ShortlistBuilder : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "listings";

    // The population, and it is the index rather than the names with bars. A
    // member the store holds nothing for still gets a row, because the row count
    // per night is asserted against the index size and a name quietly absent
    // would make that count wrong in the direction nobody looks.
    //
    // Members on tonight's session, being joined by it where the join date is
    // known and not left by it. `left IS NULL` until the phase 5 sign-off, which
    // listed four names a week before they joined and dropped three a week
    // before they left.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string CurrentMembers = @"
        SELECT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    // Tonight and the session before it, which is what the crossed-a-level
    // reason compares. Two rows rather than two queries, so the pair is read at
    // one grain.
    const string LastTwoSessionsFor = @"
        SELECT session_date, close, volume
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date DESC
        LIMIT 2;
    ";

    const string VolumeAverageFor = @"
        SELECT value
        FROM indicator
        WHERE ticker = $ticker AND name = $name
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    // Tonight's indicators and the session before, which is what a registered
    // candidate is evaluated over. Two sessions rather than one, because a
    // crossing is a fact about two and a condition keyed on tonight's sign alone
    // would fire every night of a month the reading spent on one side.
    //
    // Every indicator name rather than the ones the evaluators happen to read:
    // which figures the next candidate wants is the thing nobody knows yet, and a
    // query narrowed to today's evaluators would have to be edited by whoever
    // registers a candidate reading a different one, which is a code change
    // inside what is meant to be a registration.
    const string IndicatorsForLastTwoSessionsOf = @"
        SELECT session_date, name, value
        FROM indicator
        WHERE ticker = $ticker
          AND session_date IN (
              SELECT session_date FROM indicator
              WHERE ticker = $ticker
              GROUP BY session_date
              ORDER BY session_date DESC
              LIMIT 2)
        ORDER BY session_date DESC, name;
    ";

    // The register, whole, read once for the night rather than once per name.
    const string Register = @"
        SELECT id, candidate, rule, test, evaluator, parameters, evaluator_version,
               event, retires, registered_at, evidence
        FROM candidate_register
        ORDER BY id;
    ";

    // The suffix a previous session's value is offered to an evaluator under.
    public const string PreviousSuffix = "_previous";

    const string EdgesFor = @"
        SELECT low_edge, high_edge, role
        FROM level
        WHERE ticker = $ticker
              AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = $ticker)
        ORDER BY low_edge;
    ";

    // The last two ladder rows, which is what the trend-changed reason compares.
    const string LastTwoLaddersFor = @"
        SELECT as_of, trend_state, plan
        FROM ladder
        WHERE ticker = $ticker
        ORDER BY as_of DESC
        LIMIT 2;
    ";

    const string NextEventFor = @"
        SELECT event_date
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $on_or_after
        ORDER BY event_date
        LIMIT 1;
    ";

    // The newest facts row the name has, read so the listing and the facts file
    // agree about which session they are about. A listing written behind a facts
    // file that already exists is a row two later readers disagree over.
    //
    // The newest row and not tonight's, and the comparison below is one way for
    // that reason. Section 14 writes the listings before the facts, so on every
    // evening after a store's first the newest facts row is
    // last night's when this runs, and the facts step that follows makes it
    // tonight's. This read said "tonight's" and compared for equality until the
    // phase 5 sign-off, which refused every second evening: the first scheduled
    // night stopped here on 2026-09-10 with eleven clean stages behind it.
    const string FactsSessionFor = @"
        SELECT session_date
        FROM facts
        WHERE ticker = $ticker
        ORDER BY session_date DESC
        LIMIT 1;
    ";


    // Every session the name holds, for the gap check alone.
    const string SessionsFor = "SELECT session_date FROM bar WHERE ticker = $ticker ORDER BY session_date;";

    const string Upsert = @"
        INSERT INTO listing (ticker, session_date, reasons, fired_count, plan_at_listing, shadow_reasons)
        VALUES ($ticker, $session_date, $reasons, $fired_count, $plan_at_listing, $shadow_reasons)
        ON CONFLICT (ticker, session_date) DO UPDATE SET
            reasons = excluded.reasons,
            fired_count = excluded.fired_count,
            plan_at_listing = excluded.plan_at_listing,
            shadow_reasons = excluded.shadow_reasons;
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

    public ShortlistBuilder(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The night's start is handed in rather than read here, because this stage starts minutes after the night does.
    public async Task<ShortlistOutcome> RunAsync(
        string indexCode,
        string runId,
        DateTimeOffset nightStartedAt,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        // The session date and not the UTC date, for the reason the ladder
        // builder's matching line gives: after eight in New York the two differ.
        var asOf = clock.SessionDateAt(clock.UtcNow);
        var members = await MembersAsync(connection, indexCode, asOf, cancellation);
        var fired = 0;
        var reasonsFired = 0;
        var stop = new SeriesGapStop();
        var beyondTheCalendar = new List<string>();

        // One transaction around the whole loop rather than one per row. A row
        // that commits on its own costs a disk sync, and a sync costs the same
        // whatever the row holds, so the price is per row and not per byte. The
        // committed fixture has four names and cannot show it; the first night
        // over 503 did.
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        // The register, read once at the top of the stage, and the candidates
        // standing at the instant the night started.
        //
        // Once rather than per name, so every name in the index is evaluated
        // against the same set: a register read inside the loop would give a
        // candidate registered mid-night to the names below it in the alphabet
        // and not to the ones above, and nothing afterwards could say which.
        var registered = await RegisterAsync(connection, cancellation);
        var standing = ShadowColumn.StandingAt(registered, nightStartedAt);
        var faults = new Dictionary<string, string>(StringComparer.Ordinal);
        var withheldBy = new Dictionary<ShadowSkipCause, int>();
        var evaluated = 0;

        // The newest session any name holds, which is the night the list is about.
        // Read off the store rather than the clock, for the reason the close
        // stage and the run page's stale names read it there: a replay run on a
        // weekend has a clock a session ahead of every bar, and every name would
        // read as stale against it.
        var newest = await NewestSessionAsync(connection, cancellation);

        foreach (var ticker in members)
        {
            var (inputs, sessionDate, plan) = await InputsAsync(connection, ticker, cancellation);

            // A member whose newest bar is older than the newest session any name
            // holds, being a name the day's file carried nothing for. It is listed
            // on that night as a member with no bar for it is, with nothing fired:
            // every reason reads a close and a level, and the ones it holds are a
            // session it did not trade that night.
            //
            // Until the phase 5 sign-off such a row was dated by the name's own
            // last session, so EQR and PSTG, carried nothing for since
            // 2026-08-17 and 2026-04-16, had no row for any night after that and
            // one row dated months back that went on firing on old prices, and
            // `listings-coverage` could not see it because the fixture holds no
            // such member.
            // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
            var stale = sessionDate is { } last && newest is { } night && last < night;

            // A member whose stored series has an interior hole, evaluated
            // over nothing for the reason a stale member is. Every reason but
            // one reads a close and a level computed across the hole, and the
            // one that does not, earnings soon, reads the calendar alone: a
            // gapped name with a print inside the horizon would otherwise reach
            // tonight's list carrying an empty plan and nothing saying why. The
            // row is still written, because every member gets one.
            // see: A gap is a session the exchange traded and the store does not hold
            var gapped = stop.Stops(ticker, await SessionsAsync(connection, ticker, cancellation));

            // The session the row is about, which is the night's. A member with
            // no bars is dated by the clock's session and a member with none for
            // the night by the night, rather than by a session it has, so each
            // still gets a row and the count per night still equals the index
            // size.
            var session = stale ? newest!.Value : sessionDate ?? asOf;

            // A member evaluated over nothing still carries the date the calendar
            // holds on or after its row's session, with no count made and the
            // reason why.
            // see: A member the night evaluates over nothing keeps the dated event the calendar holds and says no count was made
            var notEvaluated = stale
                ? NoBarThisSession(sessionDate!.Value)
                : gapped
                    ? GapAt(stop.Gaps[^1].SessionDate)
                    : sessionDate is null ? NoBarStored : null;

            var outcomes = ShortlistSeries.For(notEvaluated is { } why
                ? NoBarTonight with { NextEvent = await NextEventAsync(connection, ticker, session, cancellation), NotCountedBecause = why }
                : inputs);
            var count = outcomes.Count(outcome => outcome.Fired);

            // A dated event the exchange calendar cannot count to, named on the
            // stage's own row with the table's end, so a refusal is visible on the
            // run page and not only in a hover.
            if (!(stale || gapped) && inputs.NextEvent is not null && inputs.SessionsToNextEvent is null)
            {
                beyondTheCalendar.Add(ticker);
            }

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Upsert;
            command.Parameters.AddWithValue("$ticker", ticker);

            command.Parameters.AddWithValue("$session_date", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue("$reasons", Serialised(outcomes));
            command.Parameters.AddWithValue("$fired_count", count);
            command.Parameters.AddWithValue(
                "$plan_at_listing",
                stale
                    ? JsonSerializer.Serialize(new { note = NoBarThisSession(sessionDate!.Value) })
                    : PlanAtListing(plan));

            // Registered candidates, evaluated the same way and shown nowhere.
            //
            // Evaluated for every member, including the ones no live reason fired
            // on, which is what the every-name listing row exists for: a
            // candidate has to be scored on the nights it would have fired, and
            // most of those are nights nothing surfaced the name. A stale or
            // gapped name is evaluated over the same nothing the live reasons
            // are, so a shadow score is never computed across a hole the live
            // reasons refused to compute across.
            // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
            var withheld = stale
                ? new NameWithheld(ShadowSkipCause.Stale, NoBarThisSession(sessionDate!.Value))
                : gapped
                    ? new NameWithheld(ShadowSkipCause.Gapped, GapAt(stop.Gaps[^1].SessionDate) + ", so nothing is computed across it")
                    : null;

            var shadow = ShadowColumn.Evaluate(
                standing,
                new CandidateNight(
                    ticker,
                    session,
                    withheld is null
                        ? await IndicatorsAsync(connection, ticker, cancellation)
                        : new Dictionary<string, double>(StringComparer.Ordinal)),
                withheld);

            evaluated += shadow.Outcomes.Count;

            foreach (var skip in shadow.Skipped)
            {
                if (skip.IsFault)
                {
                    // Once per candidate: a fault's reason is the same on every name-night.
                    faults.TryAdd(skip.Candidate, skip.Reason);
                }
                else
                {
                    withheldBy[skip.Cause] = withheldBy.GetValueOrDefault(skip.Cause) + 1;
                }
            }

            command.Parameters.AddWithValue("$shadow_reasons", Serialised(shadow));

            await command.ExecuteNonQueryAsync(cancellation);

            if (count > 0)
            {
                fired++;
            }

            reasonsFired += count;
        }

        await transaction.CommitAsync(cancellation);

        var beyond = beyondTheCalendar.Count == 0
            ? string.Empty
            : "; " + beyondTheCalendar.Count.ToString(CultureInfo.InvariantCulture) +
                " name(s) with a dated event beyond the exchange calendar, which ends " +
                ExchangeClosures.CoveredThrough.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                ", not counted and not fired: " + string.Join(", ", beyondTheCalendar);

        // The shadow column's own sentence on the stage's row, and it is a
        // failure rather than a note where a candidate went unevaluated.
        //
        // A shadow candidate has to be evaluated on every name-night, so a night
        // that skipped one has a hole in the record the correction will later
        // divide by, and a note nobody reads is not a record of having skipped
        // one. The night does not stop: every listing row was written and every
        // live reason was scored, so the stage did the thing the night depends on.
        // What it did not do is named where the run page draws failures.
        var shadowSaid =
            standing.Count == 0
                ? "; no candidate stands registered, so nothing was evaluated in shadow"
                : FormattableString.Invariant(
                    $"; {standing.Count} candidate(s) registered, {evaluated} shadow evaluation(s) written, ") +
                    FormattableString.Invariant(
                        $"{withheldBy.Values.Sum()} skipped on a name-night without the readings: {withheldBy.GetValueOrDefault(ShadowSkipCause.Stale)} stale, ") +
                    FormattableString.Invariant(
                        $"{withheldBy.GetValueOrDefault(ShadowSkipCause.Gapped)} gapped, {withheldBy.GetValueOrDefault(ShadowSkipCause.NotAvailable)} with a reading not available")
                    + (faults.Count == 0
                        ? string.Empty
                        : FormattableString.Invariant($"; FAILURE: {faults.Count} registered candidate(s) skipped on every name, the code carrying no evaluator by its name or a moved one: ")
                            + string.Join("; ", faults.Select(entry => $"'{entry.Key}' {entry.Value}")));

        await RecordAsync(
            connection,
            runId,
            startedAt,
            members.Count,
            fired,
            reasonsFired,
            stop.Report() + beyond + shadowSaid,
            faults.Count == 0 ? Ok : Failed,
            cancellation);

        return new ShortlistOutcome(members.Count, members.Count, fired, reasonsFired);
    }

    public const string Ok = "ok";

    // The stage ran and a registered candidate went unevaluated. Its own value
    // rather than the night's failure word, because the night did not stop and a
    // reader of the run log has to be able to tell the two apart.
    public const string Failed = "ok, with a registered candidate's evaluator missing or moved";

    static async Task<IReadOnlyList<RegisterRow>> RegisterAsync(
        SqliteConnection connection,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Register;

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

    // Tonight's indicators for a name, and the session before under a suffixed
    // name, which is what lets an evaluator ask about a crossing without the
    // builder knowing which crossing it means.
    static async Task<IReadOnlyDictionary<string, double>> IndicatorsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsForLastTwoSessionsOf;
        command.Parameters.AddWithValue("$ticker", ticker);

        var values = new Dictionary<string, double>(StringComparer.Ordinal);
        DateOnly? newest = null;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var session = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            // Set before the null check, so a session whose values are all null is still the newest.
            newest ??= session;

            // A null value is a reading not available; left out, a candidate that reads it is skipped.
            if (reader.IsDBNull(2))
            {
                continue;
            }

            values[session == newest ? reader.GetString(1) : reader.GetString(1) + PreviousSuffix] = reader.GetDouble(2);
        }

        return values;
    }

    // What the shadow column holds: the evaluations, and the candidates the night
    // could not evaluate with the reason for each. Both, because a candidate that
    // did not fire and a candidate nothing evaluated are opposite statements and
    // a column carrying only the first cannot tell them apart afterwards.
    static string Serialised(ShadowResult shadow) =>
        JsonSerializer.Serialize(new
        {
            candidates = shadow.Outcomes
                .Select(outcome => new { candidate = outcome.Candidate, fired = outcome.Fired, values = outcome.Values })
                .ToArray(),
            skipped = shadow.Skipped
                .Select(skip => new { candidate = skip.Candidate, reason = skip.Reason })
                .ToArray(),
        });

    static async Task<DateOnly?> NewestSessionAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }


    async Task<IReadOnlyList<DateOnly>> SessionsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SessionsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var sessions = new List<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            sessions.Add(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        return sessions;
    }

    // What a member with no bar for tonight is evaluated over, which is nothing:
    // the same inputs a member the store holds no bar for at all has.
    static readonly ReasonInputs NoBarTonight = new(null, null, null, null, [], [], [], null, null, null, null);

    // Why a member is evaluated over nothing, as its row states it.
    static string NoBarThisSession(DateOnly last) =>
        "no bar for this session; the last session stored for the name is " + last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string GapAt(DateOnly gap) =>
        "the stored series has a gap at " + gap.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    const string NoBarStored = "no bar is stored for the name";

    // The calendar's next dated event on or after a session, where it holds one.
    static async Task<DateOnly?> NextEventAsync(SqliteConnection connection, string ticker, DateOnly session, CancellationToken cancellation)
    {
        await using var next = connection.CreateCommand();

        next.CommandText = NextEventFor;
        next.Parameters.AddWithValue("$ticker", ticker);
        next.Parameters.AddWithValue("$on_or_after", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await next.ExecuteScalarAsync(cancellation) is string eventDate
            ? DateOnly.ParseExact(eventDate, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    // The plan as it stood tonight: the entry zone, the stop and the first
    // traded target. Bars can be replayed and this cannot, because by the time a
    // verdict is possible the rules may have changed and recomputing would score
    // old listings under new ones.
    static string PlanAtListing(string? plan)
    {
        if (plan is null)
        {
            return JsonSerializer.Serialize(new { note = "no ladder row for this name tonight" });
        }

        using var document = JsonDocument.Parse(plan);
        var root = document.RootElement;

        var tranche = root.GetProperty("tranches").EnumerateArray().FirstOrDefault();
        var target = root.GetProperty("exits").EnumerateArray()
            .FirstOrDefault(exit => exit.GetProperty("traded").GetBoolean());

        return JsonSerializer.Serialize(new
        {
            entryLow = tranche.ValueKind == JsonValueKind.Object ? tranche.GetProperty("lowEdge").GetString() : null,
            entryHigh = tranche.ValueKind == JsonValueKind.Object ? tranche.GetProperty("highEdge").GetString() : null,
            stop = tranche.ValueKind == JsonValueKind.Object ? tranche.GetProperty("stop").GetString() : null,
            firstTradedTarget = target.ValueKind == JsonValueKind.Object ? target.GetProperty("lowEdge").GetString() : null,
            invalidation = root.GetProperty("invalidation").GetString(),
        });
    }

    static string Serialised(IReadOnlyList<ReasonOutcome> outcomes) =>
        JsonSerializer.Serialize(outcomes.Select(outcome => new
        {
            name = outcome.Name,
            fired = outcome.Fired,
            values = outcome.Values,
        }));

    static async Task<IReadOnlyList<string>> MembersAsync(
        SqliteConnection connection,
        string indexCode,
        DateOnly session,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    async Task<(ReasonInputs Inputs, DateOnly? SessionDate, string? Plan)> InputsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        DateOnly? sessionDate = null;
        decimal? close = null;
        decimal? previousClose = null;
        long? volume = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LastTwoSessionsFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync(cancellation);
            var at = 0;

            while (await reader.ReadAsync(cancellation))
            {
                if (at == 0)
                {
                    sessionDate = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);
                    close = Money.FromStorage(reader.GetString(1));
                    volume = reader.GetInt64(2);
                }
                else
                {
                    previousClose = Money.FromStorage(reader.GetString(1));
                }

                at++;
            }
        }

        double? average = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = VolumeAverageFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$name", IndicatorSeries.VolAvg50);

            var value = await command.ExecuteScalarAsync(cancellation);

            average = value is null or DBNull ? null : Convert.ToDouble(value);
        }

        var edges = new List<EdgeAt>();
        var bands = new List<Band>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EdgesFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var role = reader.GetString(2);
                var low = Money.FromStorage(reader.GetString(0));
                var high = Money.FromStorage(reader.GetString(1));

                edges.Add(new EdgeAt(low, role));
                edges.Add(new EdgeAt(high, role));
                bands.Add(new Band(low, high));
            }
        }

        string? trendState = null;
        string? previousTrendState = null;
        string? plan = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LastTwoLaddersFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync(cancellation);
            var at = 0;

            while (await reader.ReadAsync(cancellation))
            {
                if (at == 0)
                {
                    trendState = reader.GetString(1);
                    plan = reader.GetString(2);
                }
                else
                {
                    previousTrendState = reader.GetString(1);
                }

                at++;
            }
        }

        var zones = new List<Zone>();

        if (plan is not null)
        {
            using var document = JsonDocument.Parse(plan);

            zones.AddRange(document.RootElement.GetProperty("tranches").EnumerateArray()
                .Select(tranche => new Zone(
                    decimal.Parse(tranche.GetProperty("lowEdge").GetString()!, CultureInfo.InvariantCulture),
                    decimal.Parse(tranche.GetProperty("highEdge").GetString()!, CultureInfo.InvariantCulture))));
        }

        DateOnly? nextEvent = null;
        int? sessionsToEvent = null;

        if (sessionDate is { } dated)
        {
            nextEvent = await NextEventAsync(connection, ticker, dated, cancellation);

            // The sessions the exchange trades between tonight and the event, off
            // its own calendar and never the stored bars after tonight, which a
            // live store never holds. A date past the closure table's end has no
            // count, and the reason says so rather than guessing.
            // see: Sessions to a dated event are counted on the exchange calendar and never on stored bars
            sessionsToEvent = nextEvent is { } dateOnFile ? ExchangeClosures.SessionsUntil(dated, dateOnFile) : null;
        }

        // The facts row is read so the listing and the facts file agree about
        // which session they are about. It is read and not used for a reason,
        // which is what its declared read is: a listing dated behind the facts
        // beside it is a row two later readers disagree over.
        //
        // Refused only where the facts are newer than the bars. Older facts are
        // last night's file, which is what a second evening holds at the listings step,
        // and equal facts are a re-run of the same session. ISO dates sort
        // lexically in the order they sort chronologically, which SCHEMA states
        // and which is why this is an ordinal comparison.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FactsSessionFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            if (await command.ExecuteScalarAsync(cancellation) is string factsSession
                && sessionDate is { } tonight
                && string.CompareOrdinal(factsSession, tonight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) > 0)
            {
                throw new InvalidOperationException(
                    $"The facts file for {ticker} is dated {factsSession} and the bars end on " +
                    $"{tonight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. A listing written behind " +
                    "the facts beside it is a row two later readers disagree over, so the night stops " +
                    "rather than storing one.");
            }
        }

        return (
            new ReasonInputs(
                close,
                previousClose,
                volume,
                average,
                edges,
                bands,
                zones,
                trendState,
                previousTrendState,
                nextEvent,
                sessionsToEvent),
            sessionDate,
            plan);
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int members,
        int fired,
        int reasonsFired,
        string gaps,
        string outcome,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", members);

        // The fired count is the headline, because it is the market's mood and
        // it is the one number the twenty drawn rows cannot tell you.
        command.Parameters.AddWithValue(
            "$detail",
            $"{members} member(s), {fired} fired, {reasonsFired} reason(s) fired{gaps}");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
