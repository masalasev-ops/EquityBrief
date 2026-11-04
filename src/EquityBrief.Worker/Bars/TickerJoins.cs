using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// The joins the operator has ruled: a stock whose ticker changed and whose history the provider keeps under the old ticker.
// Written by a verb a person runs, one stock a row, and read wherever the provider's series is asked for.
// see: A ticker change the provider keeps under two tickers is joined once on the operator's word, and every read of the provider's series reads the join
public sealed class TickerJoins(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.TickerJoin, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "ticker-join";

    const string Insert = @"
        INSERT INTO ticker_join (ticker, earlier, joined, run_id)
        VALUES ($ticker, $earlier, $joined, $run_id)
        ON CONFLICT (ticker) DO NOTHING;
    ";

    const string Every = "SELECT ticker, earlier FROM ticker_join;";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $started_at, 'ok', $rows, 0, 0, '0', $detail);
    ";

    // Writes the join, a second word on the same stock changing nothing, and states what it did.
    public async Task<int> JoinAsync(string ticker, string earlier, string runId)
    {
        var now = clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        await using var insert = connection.CreateCommand();
        insert.CommandText = Insert;
        insert.Parameters.AddWithValue("$ticker", ticker);
        insert.Parameters.AddWithValue("$earlier", earlier);
        insert.Parameters.AddWithValue("$joined", now);
        insert.Parameters.AddWithValue("$run_id", runId);

        var written = await insert.ExecuteNonQueryAsync();

        await using var run = connection.CreateCommand();
        run.CommandText = AppendRun;
        run.Parameters.AddWithValue("$run_id", runId);
        run.Parameters.AddWithValue("$stage", Stage);
        run.Parameters.AddWithValue("$started_at", now);
        run.Parameters.AddWithValue("$rows", written);
        run.Parameters.AddWithValue("$detail", FormattableString.Invariant($"{ticker} joined to {earlier}, {written} row written"));

        await run.ExecuteNonQueryAsync();
        await transaction.CommitAsync();

        return written;
    }

    public static IReadOnlyDictionary<string, string> Read(string databaseFile)
    {
        using var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = Every;

        using var reader = command.ExecuteReader();

        var joins = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        while (reader.Read())
        {
            joins[reader.GetString(0)] = reader.GetString(1);
        }

        return joins;
    }

    // The provider's series with the store's joins read in, the joins read on the first ask.
    public static IHistoricalBarFeed Over(IHistoricalBarFeed feed, string databaseFile) =>
        new JoinedHistoricalBarFeed(feed, () => Read(databaseFile));
}
