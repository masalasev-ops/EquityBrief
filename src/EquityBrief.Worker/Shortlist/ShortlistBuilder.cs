using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

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
            new StoreTouch(Store.Listing, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "listings";

    // The population, and it is the index rather than the names with bars. A
    // member the store holds nothing for still gets a row, because the row count
    // per night is asserted against the index size and a name quietly absent
    // would make that count wrong in the direction nobody looks.
    const string CurrentMembers = @"
        SELECT ticker
        FROM membership
        WHERE index_code = $index AND ""left"" IS NULL
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

    // Sessions between tonight and the next dated event, counted in stored
    // sessions rather than in days, because the horizon is stated in sessions
    // and a month of days is not a month of trading.
    const string SessionsToNextEventFor = @"
        SELECT COUNT(*)
        FROM bar
        WHERE ticker = $ticker AND session_date > $session_date AND session_date <= $event_date;
    ";

    const string NextEventFor = @"
        SELECT event_date
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $on_or_after
        ORDER BY event_date
        LIMIT 1;
    ";

    // The facts row for tonight, read so the listing and the facts file agree
    // about which session they are about. A listing written against a different
    // session from the facts beside it is a row two later readers disagree over.
    const string FactsSessionFor = @"
        SELECT session_date
        FROM facts
        WHERE ticker = $ticker
        ORDER BY session_date DESC
        LIMIT 1;
    ";

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

    public async Task<ShortlistOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, cancellation);
        var asOf = DateOnly.FromDateTime(clock.UtcNow.UtcDateTime);
        var fired = 0;
        var reasonsFired = 0;

        // One transaction around the whole loop rather than one per row. A row
        // that commits on its own costs a disk sync, and a sync costs the same
        // whatever the row holds, so the price is per row and not per byte. The
        // committed fixture has four names and cannot show it; the first night
        // over 503 did.
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var ticker in members)
        {
            var (inputs, sessionDate, plan) = await InputsAsync(connection, ticker, cancellation);
            var outcomes = ShortlistSeries.For(inputs);
            var count = outcomes.Count(outcome => outcome.Fired);

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Upsert;
            command.Parameters.AddWithValue("$ticker", ticker);

            // The session the row is about, which is the last one the store
            // holds for the name. A member with no bars is dated by the night
            // rather than by a session it does not have, so it still gets a row
            // and the count per night still equals the index size.
            command.Parameters.AddWithValue(
                "$session_date",
                (sessionDate ?? asOf).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            command.Parameters.AddWithValue("$reasons", Serialised(outcomes));
            command.Parameters.AddWithValue("$fired_count", count);
            command.Parameters.AddWithValue("$plan_at_listing", PlanAtListing(plan));

            // Registered candidates, evaluated the same way and shown nowhere.
            // The register arrives at 7.3, so the list is empty and says why
            // rather than being absent: an empty column and a column that has
            // never been written read the same on a page and only one is true.
            command.Parameters.AddWithValue(
                "$shadow_reasons",
                JsonSerializer.Serialize(new { candidates = Array.Empty<string>(), note = "the candidate register arrives at 7.3" }));

            await command.ExecuteNonQueryAsync(cancellation);

            if (count > 0)
            {
                fired++;
            }

            reasonsFired += count;
        }

        await transaction.CommitAsync(cancellation);

        await RecordAsync(connection, runId, startedAt, members.Count, fired, reasonsFired, cancellation);

        return new ShortlistOutcome(members.Count, members.Count, fired, reasonsFired);
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
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);

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

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EdgesFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var role = reader.GetString(2);

                edges.Add(new EdgeAt(Money.FromStorage(reader.GetString(0)), role));
                edges.Add(new EdgeAt(Money.FromStorage(reader.GetString(1)), role));
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

        int? sessionsToEvent = null;

        if (sessionDate is { } dated)
        {
            await using var next = connection.CreateCommand();

            next.CommandText = NextEventFor;
            next.Parameters.AddWithValue("$ticker", ticker);
            next.Parameters.AddWithValue("$on_or_after", dated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            if (await next.ExecuteScalarAsync(cancellation) is string eventDate)
            {
                await using var counted = connection.CreateCommand();

                counted.CommandText = SessionsToNextEventFor;
                counted.Parameters.AddWithValue("$ticker", ticker);
                counted.Parameters.AddWithValue("$session_date", dated.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                counted.Parameters.AddWithValue("$event_date", eventDate);

                // Sessions the store holds between tonight and the event. A date
                // beyond the stored series counts the sessions there are, which
                // is fewer than the horizon and is why the reason reads the
                // count rather than the date.
                sessionsToEvent = Convert.ToInt32(await counted.ExecuteScalarAsync(cancellation));
            }
        }

        // The facts row is read so the listing and the facts file agree about
        // which session they are about. It is read and not used for a reason,
        // which is what its declared read is: a listing dated differently from
        // the facts beside it is a row two later readers disagree over.
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FactsSessionFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            if (await command.ExecuteScalarAsync(cancellation) is string factsSession
                && sessionDate is { } tonight
                && !string.Equals(factsSession, tonight.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"The facts file for {ticker} is dated {factsSession} and the bars end on {tonight}. " +
                    "A listing written against a different session from the facts beside it is a row " +
                    "two later readers disagree over, so the night stops rather than storing one.");
            }
        }

        return (
            new ReasonInputs(
                close,
                previousClose,
                volume,
                average,
                edges,
                zones,
                trendState,
                previousTrendState,
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
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", members);

        // The fired count is the headline, because it is the market's mood and
        // it is the one number the twenty drawn rows cannot tell you.
        command.Parameters.AddWithValue(
            "$detail",
            $"{members} member(s), {fired} fired, {reasonsFired} reason(s) fired");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
