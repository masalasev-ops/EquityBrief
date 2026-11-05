using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Report;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Names;

public sealed record SplitNameOutcome(int Read, int New, bool Baseline);

// The names a page splits at their capitals, each kept once with the night it first appeared, so the run page can list a new one
// that night. It reads the segment names of the filings fetched since it last read and the insiders' titles. Its first read writes
// what stands already as the baseline, which no run page lists. No rule reads what it writes.
// see: A name split for the first time is listed on the run page the night it first appears
public sealed class SplitNameReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Fundamentals, Touch.Read),
            new StoreTouch(Store.InsiderTrade, Touch.Read),
            new StoreTouch(Store.SplitName, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "split-names";

    public const string FromFiling = "filing";
    public const string FromInsider = "insider";

    const string Since = "SELECT MAX(read_at) FROM split_name;";

    const string Filed = @"
        SELECT ticker, json_extract(payload, '$.segments') FROM fundamentals
        WHERE fetched_at > $since AND json_type(payload, '$.segments') = 'object';
    ";

    const string Titles = "SELECT title, MIN(ticker) FROM insider_trade WHERE title IS NOT NULL GROUP BY title;";

    const string Keep = @"
        INSERT INTO split_name (name, spaced, ticker, source, first_night, baseline, read_at, run_id)
        VALUES ($name, $spaced, $ticker, $source, $first_night, $baseline, $read_at, $run_id)
        ON CONFLICT (name) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public SplitNameReader(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<SplitNameOutcome> RunAsync(string runId, DateOnly night, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var at = started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        string? since;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Since;
            since = await command.ExecuteScalarAsync(cancellation) as string;
        }

        var found = new Dictionary<string, (string Ticker, string Source)>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Filed;
            command.Parameters.AddWithValue("$since", since ?? string.Empty);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                using var table = JsonDocument.Parse(reader.GetString(1));

                foreach (var group in SegmentTable.FromStored(table.RootElement).Groups)
                {
                    if (SegmentTable.NameOf(group.Label) is { } name && JoinedNames.IsSplit(name))
                    {
                        found.TryAdd(name, (reader.GetString(0), FromFiling));
                    }
                }
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Titles;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (JoinedNames.IsSplit(reader.GetString(0)))
                {
                    found.TryAdd(reader.GetString(0), (reader.GetString(1), FromInsider));
                }
            }
        }

        var baseline = since is null;
        var added = 0;

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var (name, from) in found.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                await using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = Keep;
                command.Parameters.AddWithValue("$name", name);
                command.Parameters.AddWithValue("$spaced", JoinedNames.Spaced(name));
                command.Parameters.AddWithValue("$ticker", from.Ticker);
                command.Parameters.AddWithValue("$source", from.Source);
                command.Parameters.AddWithValue("$first_night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$baseline", baseline ? 1 : 0);
                command.Parameters.AddWithValue("$read_at", at);
                command.Parameters.AddWithValue("$run_id", runId);

                added += await command.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", at);
            command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$rows_written", added);
            command.Parameters.AddWithValue("$detail", baseline ? FormattableString.Invariant($"{added} split name(s) kept as the baseline") : FormattableString.Invariant($"{added} name(s) split for the first time"));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        return new SplitNameOutcome(found.Count, added, baseline);
    }
}
