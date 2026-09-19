using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Returns;

public sealed record ForwardReturnOutcome(int ListingsExamined, int RowsWritten, int Matured, int Immature, int Kept);

// The forward return filler. Fills the five and twenty-one session outcomes of
// past listings as those sessions mature, and computes the universe base rate
// for the same windows.
//
// It runs once per night rather than per name, because the base rate is a figure
// over the whole population and a per-name pass would compute it once per name
// from the same rows.
// see: Every forward-return figure is shown against the universe base rate
// see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor
public sealed class ForwardReturnFiller : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "forward-returns";

    // Every listing with the rows already written for it, oldest first.
    const string EveryListing = @"
        SELECT l.ticker, l.session_date, l.plan_at_listing, f.horizon, f.outcome
        FROM listing l
        LEFT JOIN forward_return f ON f.ticker = l.ticker AND f.session_date = l.session_date
        ORDER BY l.session_date, l.ticker;
    ";

    // The sessions after a listing's own, in order, which is what a horizon is
    // counted in. Sessions rather than days, because a horizon stated in
    // sessions is a horizon in trading and a month of days is not a month of
    // trading.
    const string SessionsAfter = @"
        SELECT session_date, close
        FROM bar
        WHERE ticker = $ticker AND session_date > $session_date
        ORDER BY session_date
        LIMIT $sessions;
    ";

    const string CloseOn = @"
        SELECT close, raw_close
        FROM bar
        WHERE ticker = $ticker AND session_date <= $session_date
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    const string Upsert = @"
        INSERT INTO forward_return (
            ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even)
        VALUES (
            $ticker, $session_date, $horizon, $outcome, $resolved_on, $return_pct, $base_rate, $break_even)
        ON CONFLICT (ticker, session_date, horizon) DO UPDATE SET
            outcome = excluded.outcome,
            resolved_on = excluded.resolved_on,
            return_pct = excluded.return_pct,
            base_rate = excluded.base_rate,
            break_even = excluded.break_even;
    ";

    // The base rate is written across every row of a horizon once it is known,
    // which is what makes "no forward-return figure without its base rate" a
    // property of the store rather than a habit of the page.
    // see: `base_rate` is stored per row on purpose, and the reason is that the row is what gets read
    const string SetBaseRate = @"
        UPDATE forward_return SET base_rate = $base_rate WHERE horizon = $horizon;
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

    public ForwardReturnFiller(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    sealed record Listing(string Ticker, string SessionDate, string Plan, Dictionary<string, string?> Outcomes);

    public async Task<ForwardReturnOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var listings = new List<Listing>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EveryListing;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (ticker, session) = (reader.GetString(0), reader.GetString(1));

                if (listings.Count == 0 || listings[^1].Ticker != ticker || listings[^1].SessionDate != session)
                {
                    listings.Add(new Listing(ticker, session, reader.GetString(2), new Dictionary<string, string?>(StringComparer.Ordinal)));
                }

                if (!reader.IsDBNull(3))
                {
                    listings[^1].Outcomes[reader.GetString(3)] = reader.IsDBNull(4) ? null : reader.GetString(4);
                }
            }
        }

        var (written, matured, immature, kept) = (0, 0, 0, 0);
        var byHorizon = ForwardReturnSeries.Horizons.ToDictionary(
            horizon => horizon,
            _ => new List<string?>(),
            StringComparer.Ordinal);

        // One transaction around every write this stage makes, rather than one
        // per row. A row that commits on its own costs a disk sync and a sync
        // costs the same whatever the row holds, so the price is paid per row
        // and not per byte. Over the four names of the committed fixture that is
        // invisible; over 503 listings at three horizons each it was measured at
        // 334 seconds, which was more than half the night.
        //
        // It also makes the stage atomic, which is what it should have been: a
        // fill interrupted halfway left some horizons of some listings written
        // and the base rate belonging to none of them.
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var listing in listings)
        {
            var open = new List<string>();

            foreach (var horizon in ForwardReturnSeries.Horizons)
            {
                if (listing.Outcomes.TryGetValue(horizon, out var stored) && Decided(stored))
                {
                    byHorizon[horizon].Add(stored);
                    kept++;
                }
                else
                {
                    open.Add(horizon);
                }
            }

            if (open.Count == 0)
            {
                continue;
            }

            var (stop, target, entryHigh) = PlanBounds(listing.Plan);

            // A setup with no plan never resolves, so it alone reads no bars. A
            // listing whose close the store no longer holds has nothing to measure
            // from, so it is handed no sessions and stays open.
            var scorable = open.Any(horizon => horizon != ForwardReturnSeries.Setup || stop is not null && target is not null);
            var origin = scorable
                ? await CloseOnAsync(connection, listing.Ticker, listing.SessionDate, cancellation)
                : null;
            var after = origin is null
                ? []
                : await SessionsAfterAsync(connection, listing.Ticker, listing.SessionDate, cancellation);

            foreach (var horizon in open)
            {
                var outcome = horizon switch
                {
                    ForwardReturnSeries.Setup => ForwardReturnSeries.OverSetup(
                        after, stop, target, entryHigh, origin?.Close, RawClose(listing, origin, stop, target)),
                    _ => ForwardReturnSeries.Over(
                        int.Parse(horizon, CultureInfo.InvariantCulture), after, origin?.Close ?? 0m),
                };

                if (!listing.Outcomes.ContainsKey(horizon) || outcome.Outcome is not null)
                {
                    await WriteAsync(connection, transaction, listing.Ticker, listing.SessionDate, outcome, cancellation);
                    written++;
                }

                if (outcome.Outcome is null)
                {
                    immature++;
                }
                else
                {
                    matured++;
                }

                byHorizon[horizon].Add(outcome.Outcome);
            }
        }

        // The base rate per horizon, over every name-night rather than over the
        // rows where a reason fired. The setup horizon has none, because there
        // is no universe-wide notion of target before stop for a name with no
        // plan that night.
        foreach (var horizon in ForwardReturnSeries.Horizons)
        {
            if (horizon == ForwardReturnSeries.Setup)
            {
                continue;
            }

            var rate = ForwardReturnSeries.BaseRate(byHorizon[horizon]);

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = SetBaseRate;
            command.Parameters.AddWithValue("$horizon", horizon);
            command.Parameters.AddWithValue("$base_rate", (object?)rate ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        await RecordAsync(connection, runId, startedAt, listings.Count, written, matured, immature, kept, cancellation);

        return new ForwardReturnOutcome(listings.Count, written, matured, immature, kept);
    }

    static bool Decided(string? outcome) => outcome is not null;

    // Every writer stores a raw close beside the adjusted one, so a listing bar
    // without one is a fault in the store rather than a setup to score.
    static decimal? RawClose(Listing listing, (decimal Close, decimal? RawClose)? origin, decimal? stop, decimal? target) =>
        origin is not { } bar || stop is null || target is null
            ? null
            : bar.RawClose is { } raw && raw > 0
                ? raw
                : throw new InvalidOperationException(
                    $"{listing.Ticker}'s bar for the listing of {listing.SessionDate} carries no raw close above " +
                    "zero, so the adjustment its plan is scored at cannot be read.");

    // The stop, the first traded target and the entry zone's top edge as they
    // stood the night of the listing. Bars can be replayed and the plan cannot,
    // which is why the listing carries it rather than this reading tonight's
    // ladder.
    static (decimal? Stop, decimal? Target, decimal? EntryHigh) PlanBounds(string plan)
    {
        using var document = JsonDocument.Parse(plan);
        var root = document.RootElement;

        decimal? Read(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture)
                : null;

        return (Read("stop"), Read("firstTradedTarget"), Read("entryHigh"));
    }

    async Task WriteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string ticker,
        string sessionDate,
        ForwardReturn outcome,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = Upsert;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);
        command.Parameters.AddWithValue("$horizon", outcome.Horizon);
        command.Parameters.AddWithValue("$outcome", (object?)outcome.Outcome ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$resolved_on",
            outcome.ResolvedOn is { } on ? on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$return_pct", (object?)outcome.ReturnPct ?? DBNull.Value);

        // The bar this plan set for itself, on the setup horizon's row alone and
        // only where the entry close it is measured from is known. It is written
        // beside the setup rather than summed per reason, because the row is what
        // the page reads and what a later session scores each setup's own wins
        // against.
        // see: A condition is judged against the break-even its own plan demands
        command.Parameters.AddWithValue("$break_even", (object?)outcome.BreakEven ?? DBNull.Value);

        // The base rate is written by the pass below rather than here, because
        // it is not known until every listing has been read.
        command.Parameters.AddWithValue("$base_rate", DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<IReadOnlyList<ReturnBar>> SessionsAfterAsync(
        SqliteConnection connection,
        string ticker,
        string sessionDate,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SessionsAfter;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);
        command.Parameters.AddWithValue("$sessions", ForwardReturnSeries.SetupSessionCap);

        var bars = new List<ReturnBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new ReturnBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1))));
        }

        return bars;
    }

    static async Task<(decimal Close, decimal? RawClose)?> CloseOnAsync(
        SqliteConnection connection,
        string ticker,
        string sessionDate,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CloseOn;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (Money.FromStorage(reader.GetString(0)), reader.IsDBNull(1) ? null : Money.FromStorage(reader.GetString(1)))
            : null;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int listings,
        int written,
        int matured,
        int immature,
        int kept,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", written);

        // The kept and immature counts beside the matured one, because a night that
        // matured nothing and a night that wrote nothing are different nights and a
        // single total hides which.
        command.Parameters.AddWithValue(
            "$detail",
            $"{listings} listing(s), {written} row(s) written, {kept} kept as decided, " +
            $"{matured} newly matured, {immature} not yet matured");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
