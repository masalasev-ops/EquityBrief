using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Returns;

public sealed record ForwardReturnOutcome(int ListingsExamined, int RowsWritten, int Matured, int Immature);

// The forward return filler. Fills the five and twenty-one session outcomes of
// past listings as those sessions mature, and computes the universe base rate
// for the same windows.
//
// It runs once per night rather than per name, because the base rate is a figure
// over the whole population and a per-name pass would compute it once per name
// from the same rows.
// see: Every forward-return figure is shown against the universe base rate
public sealed class ForwardReturnFiller : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "forward-returns";

    // Every listing, oldest first, because a horizon matures as sessions are
    // added and the oldest are the ones that have.
    const string EveryListing = @"
        SELECT ticker, session_date, plan_at_listing
        FROM listing
        ORDER BY session_date, ticker;
    ";

    // The sessions after a listing's own, in order, which is what a horizon is
    // counted in. Sessions rather than days, because a horizon stated in
    // sessions is a horizon in trading and a month of days is not a month of
    // trading.
    const string SessionsAfter = @"
        SELECT session_date, close
        FROM bar
        WHERE ticker = $ticker AND session_date > $session_date
        ORDER BY session_date;
    ";

    const string CloseOn = @"
        SELECT close
        FROM bar
        WHERE ticker = $ticker AND session_date <= $session_date
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    const string Upsert = @"
        INSERT INTO forward_return (ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate)
        VALUES ($ticker, $session_date, $horizon, $outcome, $resolved_on, $return_pct, $base_rate)
        ON CONFLICT (ticker, session_date, horizon) DO UPDATE SET
            outcome = excluded.outcome,
            resolved_on = excluded.resolved_on,
            return_pct = excluded.return_pct,
            base_rate = excluded.base_rate;
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

    public async Task<ForwardReturnOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var listings = new List<(string Ticker, string SessionDate, string Plan)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EveryListing;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                listings.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
            }
        }

        var written = 0;
        var matured = 0;
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
            var after = await SessionsAfterAsync(connection, listing.Ticker, listing.SessionDate, cancellation);
            var listedAt = await CloseOnAsync(connection, listing.Ticker, listing.SessionDate, cancellation);
            var (stop, target) = PlanBounds(listing.Plan);

            var filled = new List<ForwardReturn>
            {
                ForwardReturnSeries.Over(5, after, listedAt ?? 0m),
                ForwardReturnSeries.Over(21, after, listedAt ?? 0m),
                ForwardReturnSeries.OverSetup(after, stop, target),
            };

            foreach (var outcome in filled)
            {
                await WriteAsync(connection, transaction, listing.Ticker, listing.SessionDate, outcome, cancellation);

                byHorizon[outcome.Horizon].Add(outcome.Outcome);

                written++;

                if (outcome.Outcome is not null)
                {
                    matured++;
                }
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

        await RecordAsync(connection, runId, startedAt, listings.Count, written, matured, cancellation);

        return new ForwardReturnOutcome(listings.Count, written, matured, written - matured);
    }

    // The stop and the first traded target as they stood the night of the
    // listing. Bars can be replayed and the plan cannot, which is why the
    // listing carries it rather than this reading tonight's ladder.
    static (decimal? Stop, decimal? Target) PlanBounds(string plan)
    {
        using var document = JsonDocument.Parse(plan);
        var root = document.RootElement;

        decimal? Read(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture)
                : null;

        return (Read("stop"), Read("firstTradedTarget"));
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

    static async Task<decimal?> CloseOnAsync(
        SqliteConnection connection,
        string ticker,
        string sessionDate,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CloseOn;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);

        return await command.ExecuteScalarAsync(cancellation) is string close
            ? Money.FromStorage(close)
            : null;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int listings,
        int written,
        int matured,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", written);

        // The immature count beside the matured one, because a night that
        // matured nothing and a night that wrote nothing are different nights
        // and a single total hides which.
        command.Parameters.AddWithValue(
            "$detail",
            $"{listings} listing(s), {written} row(s), {matured} matured, {written - matured} not yet matured");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
