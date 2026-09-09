using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// One stored bar, handed over exactly as the store holds it.
//
// The prices are decimal because the money rule is decimal in code and TEXT in
// storage, and they cross that boundary through Money and nowhere else. Volume
// is a long because the column is INTEGER.
public sealed record BarRow(
    string Ticker,
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

// One stored indicator, handed over exactly as the store holds it.
//
// Value is a nullable double because the column is REAL and nullable, and an
// indicator whose window is longer than the history behind its session has no
// value. BarCount is what makes that null legible, and it is served rather than
// dropped: a reader who sees no long average is owed the count that explains it.
public sealed record IndicatorRow(
    string Ticker,
    DateOnly SessionDate,
    string Name,
    double? Value,
    int BarCount);

// The read surface. Serves what the nightly run stored, and nothing else.
//
// It computes nothing and fetches nothing, which section 7's row states and
// this checkpoint's done condition requires be proved rather than asserted.
// Both halves are meant literally. No value leaving here is derived from
// another: every field is the stored column, converted between the storage form
// and the code form and not otherwise touched. And the project has no feed, no
// client and no reference to the worker, which api-isolation reads from the
// compiled dependency file rather than from the project file.
//
// The seam matters more than it looks. A read surface that computes is a second
// place the arithmetic lives, and the day it disagrees with the nightly run
// nothing says which one is the system.
// see: Code owns every number
// see: A screen reads and renders, and computes nothing
public sealed class ReadApi : IComponent
{
    // Reads every store and appends to the run log, which is section 7's row
    // for this component and the eleven R cells plus one W in its matrix row.
    //
    // Every store means every store the matrix has a column for. The candidate
    // register has no column and is not in the catalogue's phrase, so it is not
    // declared here either; it arrives with the registrar in phase 7.
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Move, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.Fundamentals, Touch.Read),
            new StoreTouch(Store.NewsPulse, Touch.Read),
            new StoreTouch(Store.ResearchSection, Touch.Read),
            new StoreTouch(Store.ThemeSection, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "read-api";

    readonly string databaseFile;
    readonly IClock clock;

    public ReadApi(string databaseFile, IClock clock)
    {
        this.databaseFile = databaseFile;
        this.clock = clock;
    }

    // Ordered by session so the caller does not have to sort, which is the one
    // thing this does beyond selecting. Ordering is not computation: it changes
    // which row comes first and never what a row says.
    const string BarsForName = @"
        SELECT ticker, session_date, open, high, low, close, volume
        FROM bar
        WHERE ticker = $ticker AND session_date >= $from AND session_date <= $to
        ORDER BY session_date;
    ";

    // Ordered by session and then by name, for the reason the bars query is
    // ordered: the caller does not have to sort and ordering says nothing about
    // what a row holds. The null value is served as a null and never as a zero,
    // because a zero is a reading and an absent indicator is not one.
    const string IndicatorsForName = @"
        SELECT ticker, session_date, name, value, bar_count
        FROM indicator
        WHERE ticker = $ticker AND session_date >= $from AND session_date <= $to
        ORDER BY session_date, name;
    ";

    // One row per process start, stage read-api, which is the grain SCHEMA
    // declares for the run log: one row per run per stage. A row per served
    // request would break that key and would grow the operational record by
    // something that is not an operation.
    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $started_at, 'started',
            0, 0, 0, '0', $detail);
    ";

    SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();

        return connection;
    }

    public async Task<IReadOnlyList<BarRow>> BarsAsync(string ticker, DateOnly from, DateOnly to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = BarsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var bars = new List<BarRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bars.Add(new BarRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                Money.FromStorage(reader.GetString(5)),
                reader.GetInt64(6)));
        }

        return bars;
    }

    public async Task<IReadOnlyList<IndicatorRow>> IndicatorsAsync(string ticker, DateOnly from, DateOnly to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var rows = new List<IndicatorRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new IndicatorRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                await reader.IsDBNullAsync(3) ? null : reader.GetDouble(3),
                reader.GetInt32(4)));
        }

        return rows;
    }

    // The operational record of the read surface coming up, which section
    // 15.10's run page reads. Appended rather than updated, because the run log
    // has no updater declared and every component appends to it.
    public async Task RecordStartAsync(string runId, string detail)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync();
    }
}
