using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

using EquityBrief.Worker.Bars;

namespace EquityBrief.Worker.Indicators;

public sealed record IndicatorOutcome(int NamesComputed, int RowsWritten, int NotAvailable, int RowsDropped);

// The indicator engine. Reads the stored bars for every current member and
// writes the ten values SCHEMA's name column enumerates, per session, each row
// carrying the bar count it was computed from.
//
// It makes no request and calls no model. Everything it needs is already in the
// store, which is what puts it in the free half of figure 5.1.
// see: The nightly run is arithmetic only
//
// The arithmetic is not here. It is in IndicatorSeries, as pure functions of a
// session-ordered series, so the numbers can be asserted against arithmetic done
// by hand with no store in the way. What is here is reading, the money crossing,
// and writing.
// see: Code owns every number
public sealed class IndicatorEngine : IComponent
{
    // Reads bars, writes indicators, appends to the run log. Section 16's row
    // said Listings until 3.1 repaired contradiction K; this declaration is what
    // that repair had to happen before.
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "indicators";

    // Every name the store holds bars for, rather than every current member.
    //
    // A name that left the index keeps its stored history, and a chart opened on
    // it should draw the same averages it drew the night before rather than lose
    // them because the name is no longer a member. The bar store is the
    // population, and it is stated here because a figure over current members
    // only would be a figure over the wrong one.
    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    const string BarsFor = @"
        SELECT session_date, high, low, close, volume
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date;
    ";

    // Recomputed nightly, so a re-run replaces rather than duplicates. The
    // conflict target is the declared primary key, and the update is what makes
    // the night idempotent in what it records: two runs over one session leave
    // one row saying the same thing.
    // The retention drop, one year back from the as-of date this run wrote.
    //
    // Section 16 states one year over the six computed tables and SCHEMA gave
    // Delete to nobody until 4.2, which is contradiction A and contradiction H
    // in a third place: a retention window nobody owns is a table that grows
    // forever while the document says it does not.
    //
    // Nothing here can remove a row from inside a set it leaves standing. The
    // boundary is a date and every row below it goes, for every name at once,
    // which is the same shape BarFetcher's drop has and the same reason it is
    // sanctioned.
    // Keyed on the session, so this replaces with the series and grows only
    // as the series does. The boundary is the one the bar fetcher uses, so an
    // indicator whose bar has gone does not outlive it.
    const string DropOlderThan = @"
        DELETE FROM indicator WHERE session_date < $oldest;
    ";

    const string Upsert = @"
        INSERT INTO indicator (ticker, session_date, name, value, bar_count)
        VALUES ($ticker, $session_date, $name, $value, $bar_count)
        ON CONFLICT (ticker, session_date, name) DO UPDATE SET
            value = excluded.value,
            bar_count = excluded.bar_count;
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

    public IndicatorEngine(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<IndicatorOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var tickers = await TickersAsync(connection, cancellation);

        var rows = 0;
        var absent = 0;

        foreach (var ticker in tickers)
        {
            var bars = await BarsAsync(connection, ticker, cancellation);
            var points = IndicatorSeries.For(bars);

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            foreach (var point in points)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Upsert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session_date", point.SessionDate.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("$name", point.Name);
                command.Parameters.AddWithValue("$value", (object?)point.Value ?? DBNull.Value);
                command.Parameters.AddWithValue("$bar_count", point.BarCount);

                await command.ExecuteNonQueryAsync(cancellation);

                rows++;

                if (point.Value is null)
                {
                    absent++;
                }
            }

            await transaction.CommitAsync(cancellation);
        }

        var dropped = await DroppedAsync(
            connection,
            DropOlderThan,
            await BoundaryAsync(connection, cancellation),
            cancellation);

        await RecordAsync(connection, runId, startedAt, tickers.Count, rows, absent, dropped, cancellation);

        return new IndicatorOutcome(tickers.Count, rows, absent, dropped);
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

    // The money crossing, made once and named for itself. A price is decimal in
    // code and the arithmetic below it is double, and Statistic.FromPrice is the
    // only thing that goes between them.
    async Task<IReadOnlyList<SeriesBar>> BarsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BarsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var bars = new List<SeriesBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new SeriesBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Statistic.FromPrice(Money.FromStorage(reader.GetString(1))),
                Statistic.FromPrice(Money.FromStorage(reader.GetString(2))),
                Statistic.FromPrice(Money.FromStorage(reader.GetString(3))),
                Statistic.FromVolume(reader.GetInt64(4))));
        }

        return bars;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int names,
        int rows,
        int absent,
        int dropped,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok, {dropped} dropped");
        command.Parameters.AddWithValue("$rows_written", rows);

        // The absent count is on the row a person reads rather than inferred
        // from a query nobody runs. An indicator with no value is the ordinary
        // state at the start of a series, and a jump in that count is how a
        // series that stopped arriving would show.
        command.Parameters.AddWithValue(
            "$detail",
            $"{names} name(s), {rows} row(s), {absent} not available, {dropped} dropped");

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // One year back from the newest stored session, which is the boundary
    // BarFetcher already drops bars at. Read from the store rather than passed
    // in, so a component run on its own drops the same rows a night would.
    //
    // A store with no bars has no boundary and nothing to drop, which is a
    // different thing from a boundary of today.
    static async Task<DateOnly?> BoundaryAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        var newest = await command.ExecuteScalarAsync(cancellation);

        return newest is null or DBNull
            ? null
            : DateOnly.ParseExact((string)newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .AddYears(-BarFetcher.RetentionYears);
    }

    // Every row below the boundary, for every name at once, so what is left is
    // still the window ending tonight.
    static async Task<int> DroppedAsync(
        SqliteConnection connection,
        string statement,
        DateOnly? boundary,
        CancellationToken cancellation)
    {
        if (boundary is not { } oldest)
        {
            return 0;
        }

        await using var command = connection.CreateCommand();

        command.CommandText = statement;
        command.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

}
