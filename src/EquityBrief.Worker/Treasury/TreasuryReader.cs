using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Treasury;

// What one read of the Treasury's table did: the sessions of the year it published up to the night's, the rows new to
// the store, the night's own 10-year where the table published one, and why nothing was read where nothing was.
public sealed record TreasuryRead(int Published, int Kept, double? TenYear, string? NotRead = null);

// The Treasury's 10-year par yield, read once a night after the close: one request for the session's calendar year,
// free, keyless and the Treasury's own, every session of it up to the night's kept that the store does not hold and
// none after the night. A refused or unreadable answer keeps nothing and stops no step, its row saying why, and a night
// handed no feed reads none and says so. The name page reads a dividend's yield against it.
// see: The Treasury's 10-year par yield is read once a night after the close and kept a session a row
public sealed class TreasuryReader(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.TreasuryYield, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.TreasuryYield]);

    public const string Stage = "treasury";

    const string Insert = @"
        INSERT INTO treasury_yield (session_date, ten_year, run_id)
        VALUES ($session_date, $ten_year, $run_id)
        ON CONFLICT (session_date) DO NOTHING;
    ";

    const string Held = "SELECT COUNT(*) FROM treasury_yield;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<TreasuryRead> RunAsync(ITreasuryYieldFeed? feed, DateOnly session, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (feed is null)
        {
            var none = new TreasuryRead(0, 0, null, "the night was given no Treasury feed");

            await AppendAsync(connection, runId, startedAt, "ok", 0, 0, Detail(none, session), cancellation);

            return none;
        }

        var requestsBefore = feed.Requests;
        IReadOnlyList<TreasuryYield> published;

        try
        {
            published = [.. (await feed.YearAsync(session.Year, cancellation)).Where(yield => yield.Session <= session)];
        }
        catch (Exception failure) when (failure is ProviderRefusal or FormatException || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            var refused = new TreasuryRead(0, 0, null, failure is OperationCanceledException ? "the Treasury did not answer in time on any try" : failure.Message);

            await AppendAsync(connection, runId, startedAt, "partial", 0, feed.Requests - requestsBefore, Detail(refused, session), cancellation);

            return refused;
        }

        var before = await CountAsync(connection, cancellation);

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var yield in published)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = Insert;
                insert.Parameters.AddWithValue("$session_date", yield.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$ten_year", yield.TenYear);
                insert.Parameters.AddWithValue("$run_id", runId);

                await insert.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        var read = new TreasuryRead(
            published.Count,
            await CountAsync(connection, cancellation) - before,
            published.FirstOrDefault(yield => yield.Session == session)?.TenYear);

        await AppendAsync(connection, runId, startedAt, "ok", read.Kept, feed.Requests - requestsBefore, Detail(read, session), cancellation);

        return read;
    }

    // What the run page reads of the step.
    public static string Detail(TreasuryRead read, DateOnly session) =>
        read.NotRead is { } why
            ? "no 10-year was read: " + why
            : FormattableString.Invariant($"{read.Published} session(s) of {session.Year} the Treasury published to {session:yyyy-MM-dd}, {read.Kept} new to the store; ")
                + (read.TenYear is { } tenYear
                    ? FormattableString.Invariant($"the 10-year {tenYear:0.00} on {session:yyyy-MM-dd}")
                    : FormattableString.Invariant($"none published for {session:yyyy-MM-dd} yet"));

    static async Task<int> CountAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Held;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, string outcome, int rows, int requests, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$network_requests", requests);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
