using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// The SEC's own ticker file read whole, one request, free and keyless, by hand and by the monthly run's facts step and
// never by the night, a row a ticker it lists under a run of its own, which the quarters fetch reads for a member the
// provider files no CIK for, as it files none for a ticker changed since it last read the company. Each read keeps its
// own rows, so the newest names the filer; the run's row says how many members the provider files no CIK for the file
// names and which it does not.
// see: A member the provider files no CIK for is matched to its filer by the SEC's own ticker file
public sealed class SecTickers(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.SecTicker, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.FilingsArchive]);

    public const string Stage = "sec-tickers";

    const string InsertTicker = @"
        INSERT INTO sec_ticker (ticker, cik, title, run_id, read_at)
        VALUES ($ticker, $cik, $title, $run_id, $read_at)
        ON CONFLICT (ticker, run_id) DO NOTHING;
    ";

    // The tickers whose newest company row carries no CIK of the provider's.
    const string ProviderFilesNone = @"
        SELECT c.ticker FROM company c
        WHERE c.fetched_at = (SELECT MAX(d.fetched_at) FROM company d WHERE d.ticker = c.ticker)
          AND c.cik IS NULL
        ORDER BY c.ticker;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public sealed record Outcome(int Tickers, int Unmatched, IReadOnlyList<string> NotListed, string? Refusal);

    // Reads the file once and stores it under the run named, or under a run of its own where none is named.
    public async Task<Outcome> ReadAsync(ISecTickersFeed feed, TextWriter output, string? runId = null, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var run = runId ?? FormattableString.Invariant($"{Stage}-{started:yyyyMMddTHHmmssZ}");
        var watch = Stopwatch.StartNew();

        string? body;

        try
        {
            body = await feed.TickersAsync(cancellation);
        }
        catch (ProviderRefusal refused)
        {
            return await RefusedAsync(run, started, feed.Documents, "the archive refused the ticker file: " + refused.Message, output, cancellation);
        }

        if (body is null)
        {
            return await RefusedAsync(run, started, feed.Documents, "the archive holds no ticker file", output, cancellation);
        }

        IReadOnlyList<SecTicker> lines;

        try
        {
            lines = SecEdgarTickers.Parse(body);
        }
        catch (Exception error) when (error is FormatException or System.Text.Json.JsonException)
        {
            return await RefusedAsync(run, started, feed.Documents, "the ticker file cannot be read: " + error.Message, output, cancellation);
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var unmatched = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ProviderFilesNone;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                unmatched.Add(reader.GetString(0));
            }
        }

        var listed = lines.Select(line => line.Ticker).ToHashSet(StringComparer.Ordinal);
        var notListed = unmatched.Where(ticker => !listed.Contains(ticker)).ToArray();

        await using (var transaction = await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var line in lines)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = (SqliteTransaction)transaction;
                insert.CommandText = InsertTicker;
                insert.Parameters.AddWithValue("$ticker", line.Ticker);
                insert.Parameters.AddWithValue("$cik", line.Cik);
                insert.Parameters.AddWithValue("$title", line.Title);
                insert.Parameters.AddWithValue("$run_id", run);
                insert.Parameters.AddWithValue("$read_at", Instant(started));

                await insert.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        var detail = FormattableString.Invariant(
            $"{lines.Count} ticker(s) the SEC's file lists, kept under this run; {unmatched.Count - notListed.Length} of the {unmatched.Count} member(s) the provider files no CIK for named by it")
            + (notListed.Length > 0 ? "; not named: " + string.Join(", ", notListed) : string.Empty)
            + FormattableString.Invariant($"; {feed.Documents} request(s) in {watch.Elapsed.TotalSeconds:0} s");

        await AppendAsync(connection, run, started, "ok", lines.Count, feed.Documents, detail, cancellation);
        await output.WriteLineAsync($"{Stage}: {detail}");

        return new Outcome(lines.Count, unmatched.Count, notListed, null);
    }

    async Task<Outcome> RefusedAsync(string run, DateTimeOffset started, int requests, string why, TextWriter output, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await AppendAsync(connection, run, started, "failed", 0, requests, why, cancellation);
        await output.WriteLineAsync($"{Stage}: {why}; nothing stored");

        return new Outcome(0, 0, [], why);
    }

    async Task AppendAsync(SqliteConnection connection, string run, DateTimeOffset started, string outcome, int rows, int requests, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", run);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Instant(started));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$network_requests", requests);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
