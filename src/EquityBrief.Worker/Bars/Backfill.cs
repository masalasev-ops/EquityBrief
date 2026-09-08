using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// Backfill.
//
// Fetches one year for any member holding no history, one request per name, and
// never again for a name that already holds its year. A single day of bars
// computes nothing, so the store has to start full, and repeating this would
// spend hundreds of requests fetching bars already held.
//
// It runs per ticker deliberately, which is the opposite shape from the nightly
// fetch. The bulk endpoint is priced per request and the historical endpoint per
// name, so a year of past sessions costs 500 calls this way against 25,000
// through the bulk one
// (see: Bars come from EODHD, bulk nightly and per ticker for the backfill).
public sealed class Backfill(
    IHistoricalBarFeed feed,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.HistoricalPrice]);

    public const string Stage = "backfill";
    public const string Source = "historical";

    // Current members, being the names a backfill is owed for. A name that has
    // left keeps its stored history and is not fetched again.
    const string CurrentMembers = @"
        SELECT ticker FROM membership
        WHERE index_code = $index_code AND ""left"" IS NULL
        ORDER BY ticker;
    ";

    // Which of them already hold history. Any stored bar counts: the rule is
    // never repeated for a name that already holds its year, and a name with a
    // short series is a gap question rather than a backfill question
    // (see: A gap is a session the exchange traded and the store does not hold).
    const string TickersHoldingBars = "SELECT DISTINCT ticker FROM bar;";

    // Insert only. Bars are append-only, so a name already holding a session
    // keeps what it has rather than having it rewritten.
    const string InsertBar = @"
        INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at)
        VALUES ($ticker, $session_date, $open, $high, $low, $close, $volume, $source, $observed_at)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // Counted before and after rather than by matching the observation instant.
    //
    // Keying on observed_at looked equivalent and is not: two runs sharing an
    // instant, which is what a fixed clock produces and what a fast machine can
    // produce for real, would each count the other's rows. A delta over the
    // table is measured from the store, which is what SCHEMA requires, and does
    // not rest on the instant happening to be unique.
    const string BarCount = "SELECT COUNT(*) FROM bar;";

    public async Task<BackfillOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var startedAt = clock.UtcNow;
        var observedAt = startedAt.ToString("O");
        var to = clock.SessionDateAt(startedAt);
        var from = to.AddYears(-1);

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var members = await Read(connection, CurrentMembers, indexCode, cancellationToken);
        var holding = await Read(connection, TickersHoldingBars, null, cancellationToken);

        var owed = members.Except(holding, StringComparer.OrdinalIgnoreCase).ToArray();
        var before = feed.Requests;
        var barsBefore = await Count(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var ticker in owed)
        {
            var bars = await feed.BarsAsync(ticker, from, to, cancellationToken);

            foreach (var bar in bars)
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertBar;
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$session_date", bar.SessionDate.ToString("yyyy-MM-dd"));

                // Through the money guard, which refuses anything that is not a
                // decimal. STRICT would take a double and store its rendering.
                Money.Bind(insert, "$open", bar.Open);
                Money.Bind(insert, "$high", bar.High);
                Money.Bind(insert, "$low", bar.Low);
                Money.Bind(insert, "$close", bar.Close);

                insert.Parameters.AddWithValue("$volume", bar.Volume);
                insert.Parameters.AddWithValue("$source", Source);
                insert.Parameters.AddWithValue("$observed_at", observedAt);

                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        var requests = feed.Requests - before;
        var written = await Count(connection, cancellationToken) - barsBefore;

        await using var log = connection.CreateCommand();
        log.CommandText = AppendRun;
        log.Parameters.AddWithValue("$run_id", runId);
        log.Parameters.AddWithValue("$stage", Stage);
        log.Parameters.AddWithValue("$started_at", observedAt);
        log.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("O"));
        log.Parameters.AddWithValue("$outcome", "ok");
        log.Parameters.AddWithValue("$rows_written", written);

        // Measured off the feed, not stated. The done condition is that this
        // equals the number of names lacking history, and a literal could not
        // fail that.
        log.Parameters.AddWithValue("$network_requests", requests);
        log.Parameters.AddWithValue("$detail", DBNull.Value);

        await log.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BackfillOutcome(members.Count, owed.Length, requests, written);
    }

    static async Task<int> Count(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BarCount;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    static async Task<IReadOnlyList<string>> Read(
        SqliteConnection connection,
        string sql,
        string? indexCode,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (indexCode is not null)
        {
            command.Parameters.AddWithValue("$index_code", indexCode);
        }

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    string ConnectionString => new SqliteConnectionStringBuilder { DataSource = databaseFile }.ToString();
}

// What one backfill did. Members and Owed are separate because the done
// condition is about the second: the request count matches the number of names
// lacking history, not the number of names.
public sealed record BackfillOutcome(int Members, int Owed, int Requests, int RowsWritten);
