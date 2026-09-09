using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Time;
using EquityBrief.Core.Volume;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Volume;

public sealed record ProfileOutcome(int NamesProfiled, int NamesTooShort, int RowsWritten);

// The volume profile builder. Reads the stored bars, spreads each day's volume
// across that day's range into twenty price bands over the last sixty sessions,
// and writes one row per band with the shares in it and its share of the period.
//
// It makes no request and calls no model.
// see: The nightly run is arithmetic only
//
// The arithmetic is in VolumeProfileSeries. What is here is reading, writing and
// the run log, and the one decision this component owns rather than borrows: a
// name with fewer than sixty sessions is counted and skipped rather than given a
// profile over a shorter period.
// see: Code owns every number
public sealed class VolumeProfileBuilder : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "volume-profile";

    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    // The whole stored series, not the last sixty. The window is applied by the
    // arithmetic, which is where the rule about a short history lives, so a
    // query that took sixty rows would decide the same question twice and would
    // hand a short name sixty rows it does not have without saying so.
    const string BarsFor = @"
        SELECT session_date, high, low, volume
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date;
    ";

    // The as-of date is in the primary key, so a night recomputing its own date
    // replaces its own rows and a later night writes a set of its own. That is
    // what SCHEMA's grain says and it is the difference from the indicator and
    // swing tables, whose keys carry the session the value belongs to rather
    // than the date the computation was run for.
    const string Upsert = @"
        INSERT INTO volume_profile (ticker, as_of, band_low, band_high, share_count, share_of_period)
        VALUES ($ticker, $as_of, $band_low, $band_high, $share_count, $share_of_period)
        ON CONFLICT (ticker, as_of, band_low) DO UPDATE SET
            band_high = excluded.band_high,
            share_count = excluded.share_count,
            share_of_period = excluded.share_of_period;
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

    public VolumeProfileBuilder(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<ProfileOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var tickers = await TickersAsync(connection, cancellation);

        var profiled = 0;
        var tooShort = 0;
        var rows = 0;

        foreach (var ticker in tickers)
        {
            var bars = await BarsAsync(connection, ticker, cancellation);
            var bands = VolumeProfileSeries.For(bars);

            if (bands.Count == 0)
            {
                tooShort++;

                continue;
            }

            // The last session the name has, which is what the profile is as of.
            // Read from the series rather than from the clock, because a name
            // whose last bar is older than tonight has a profile as of its own
            // last session and not as of a date it has no bars for.
            var asOf = bars[^1].SessionDate;

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            foreach (var band in bands)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Upsert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd"));
                Money.Bind(command, "$band_low", band.Low);
                Money.Bind(command, "$band_high", band.High);
                command.Parameters.AddWithValue("$share_count", band.Shares);
                command.Parameters.AddWithValue("$share_of_period", band.ShareOfPeriod);

                await command.ExecuteNonQueryAsync(cancellation);

                rows++;
            }

            await transaction.CommitAsync(cancellation);

            profiled++;
        }

        await RecordAsync(connection, runId, startedAt, profiled, tooShort, rows, cancellation);

        return new ProfileOutcome(profiled, tooShort, rows);
    }

    async Task<IReadOnlyList<string>> TickersAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = TickersWithBars;

        var tickers = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    async Task<IReadOnlyList<ProfileBar>> BarsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BarsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var bars = new List<ProfileBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new ProfileBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                reader.GetInt64(3)));
        }

        return bars;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int profiled,
        int tooShort,
        int rows,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", rows);

        // The skipped names are on the row a person reads, because a name with
        // no profile has no row anywhere else to say so. A count that rises the
        // night a backfill fails is the only place that would show.
        command.Parameters.AddWithValue(
            "$detail",
            $"{profiled} name(s) profiled, {tooShort} with fewer than " +
            $"{VolumeProfileSeries.Window} session(s), {rows} band row(s)");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
