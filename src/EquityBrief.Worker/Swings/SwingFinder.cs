using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Swings;

public sealed record SwingOutcome(int NamesExamined, int RowsWritten, int Highs, int Lows);

// The swing finder. Reads the stored bars for every name the store holds and
// writes the days whose high or low is the most extreme within three bars either
// side, each carrying the date its lookback completed.
//
// It makes no request and calls no model, for the reason the indicator engine
// does not: everything it needs is already in the store.
// see: The nightly run is arithmetic only
//
// The arithmetic is in SwingSeries, as a pure function of a session-ordered
// series. What is here is reading, writing and the run log.
// see: Code owns every number
public sealed class SwingFinder : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "swings";

    // Every name with bars, rather than every current member, for the reason the
    // indicator engine states: a name that left the index keeps its stored
    // history and a chart opened on it should draw what it drew last night.
    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    const string BarsFor = @"
        SELECT session_date, high, low
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date;
    ";

    // Insert or update on the primary key, and no delete, which is what SCHEMA
    // declares: `swing` has an inserter and an updater and no deleter at all.
    //
    // That is sound rather than an oversight, and the argument is worth stating
    // because a recomputed table that cannot delete usually is an oversight. A
    // swing at one session is decided by seven bars, all of which are fixed once
    // the third one after it has closed, and bars are append-only. So a
    // confirmed swing never stops being one. The single case that moves it is a
    // corporate action, which refetches the year and rescales every price by one
    // factor, and a uniform positive factor cannot reorder seven prices. The
    // swing survives with a new price, which is exactly what the update covers,
    // and nothing has to be removed.
    //
    // The sessions at the young end of the window are the case worth checking
    // against that argument. A session three bars from the start of the stored
    // series has no full window and produces nothing, and it keeps producing
    // nothing as the series grows, because it moves further from the start
    // rather than closer. The end is the mirror: a session three bars from the
    // end has no window tonight and may gain one tomorrow, which is an insert.
    const string Upsert = @"
        INSERT INTO swing (ticker, session_date, direction, price, confirmed_on)
        VALUES ($ticker, $session_date, $direction, $price, $confirmed_on)
        ON CONFLICT (ticker, session_date, direction) DO UPDATE SET
            price = excluded.price,
            confirmed_on = excluded.confirmed_on;
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

    public SwingFinder(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<SwingOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var tickers = await TickersAsync(connection, cancellation);

        var highs = 0;
        var lows = 0;

        foreach (var ticker in tickers)
        {
            var bars = await BarsAsync(connection, ticker, cancellation);
            var swings = SwingSeries.For(bars);

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            foreach (var swing in swings)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Upsert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$session_date", swing.SessionDate.ToString("yyyy-MM-dd"));
                command.Parameters.AddWithValue("$direction", swing.Direction);
                Money.Bind(command, "$price", swing.Price);
                command.Parameters.AddWithValue("$confirmed_on", swing.ConfirmedOn.ToString("yyyy-MM-dd"));

                await command.ExecuteNonQueryAsync(cancellation);

                if (swing.Direction == SwingSeries.High)
                {
                    highs++;
                }
                else
                {
                    lows++;
                }
            }

            await transaction.CommitAsync(cancellation);
        }

        await RecordAsync(connection, runId, startedAt, tickers.Count, highs, lows, cancellation);

        return new SwingOutcome(tickers.Count, highs + lows, highs, lows);
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

    // No money crossing anywhere in this component. A swing is a price, it is
    // read as a decimal and written as one, and the indicator engine's
    // Statistic.FromPrice has no place here.
    async Task<IReadOnlyList<SwingBar>> BarsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BarsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var bars = new List<SwingBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new SwingBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2))));
        }

        return bars;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int names,
        int highs,
        int lows,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", highs + lows);

        // The two directions separately on the row a person reads. A night that
        // marked forty peaks and no troughs is a night whose comparison ran one
        // way, and a single total hides that completely.
        command.Parameters.AddWithValue(
            "$detail",
            $"{names} name(s), {highs} high(s), {lows} low(s)");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
