using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

// The estimates fetcher. In the swing filter's stage, for the members a rule reading analysts' estimates passes on
// everything else, it reads back each one's estimate the store already holds for the night and asks the provider's
// fundamentals for each of the rest, once a member, storing each answer with its run. A member the provider does not
// serve, or whose ask would take the night past the day's allowance, is read as none and named on the row, and stores
// nothing, so a run of the rest of the night asks for it again; a night run again for an earlier session asks for
// none, since the estimates it would store are today's and not that night's. It writes one row of its own each night
// the stage runs, with the members it read and asked for and the requests it made, and calls no model.
// see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night
// see: A member's estimates are raised where its current fiscal year's consensus earnings estimate stands above its level 30 days before
// see: The nightly run is arithmetic only
public sealed class EstimatesFetcher(IFundamentalsFeed? feed, Func<int> weightedSoFar, IClock clock, string databaseFile) : IComponent, IEstimateSource
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.EstimateReading, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.CompanyFinancials]);

    public const string Stage = "estimates";

    const string StoredOn = @"
        SELECT ticker, year_end, current_estimate, days_ago_estimate, not_read
        FROM estimate_reading
        WHERE session_date = $night;
    ";

    const string Insert = @"
        INSERT INTO estimate_reading (ticker, session_date, year_end, current_estimate, days_ago_estimate, not_read, run_id)
        VALUES ($ticker, $night, $year_end, $current_estimate, $days_ago_estimate, $not_read, $run_id)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly DateTimeOffset startedAt = clock.UtcNow;
    readonly List<string> read = [];
    readonly List<string> unserved = [];
    int requests;
    int written;

    public async Task<IReadOnlyDictionary<string, EstimateReading>> ReadAsync(DateOnly night, IReadOnlyList<string> tickers, string runId, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var held = await StoredAsync(connection, night, cancellation);
        var readings = new Dictionary<string, EstimateReading>(StringComparer.Ordinal);

        foreach (var ticker in tickers.Distinct(StringComparer.Ordinal))
        {
            if (held.TryGetValue(ticker, out var kept))
            {
                readings[ticker] = kept;
                read.Add($"{ticker} read from the store, {SwingWords(kept)}");

                continue;
            }

            var refused = feed is null
                ? "none asked for, since this night was run again for an earlier session"
                : weightedSoFar() + ProviderWeights.Fundamentals > ProviderWeights.DailyAllowance
                    ? FormattableString.Invariant($"none asked for, since its ask would take the night past the day's allowance of {ProviderWeights.DailyAllowance}")
                    : null;

            if (refused is not null)
            {
                readings[ticker] = EstimateReading.Unread(refused);
                unserved.Add($"{ticker}: {refused}");

                continue;
            }

            var before = feed!.Requests;

            try
            {
                var answer = await feed.FundamentalsAsync(ticker, cancellation);
                var reading = answer.Estimates ?? EstimateReading.Unread("the answer files no earnings at all");

                written += await InsertAsync(connection, ticker, night, reading, runId, cancellation);
                readings[ticker] = reading;
                read.Add($"{ticker} asked for, {SwingWords(reading)}");
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                readings[ticker] = EstimateReading.Unread("the provider did not answer in time on any try");
                unserved.Add($"{ticker}: the provider did not answer in time on any try, so nothing was stored for it");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // Every failure of the ask but the night's own cancellation is a member not served, whatever its shape,
                // since only the rule reading it reads it and reads a member with none as not raised.
                readings[ticker] = EstimateReading.Unread("the provider did not serve it: " + failure.Message);
                unserved.Add($"{ticker}: nothing was stored for it, {failure.Message}");
            }
            finally
            {
                requests += feed.Requests - before;
            }
        }

        return readings;
    }

    // The row's own sentence, which the swing filter's row repeats after the members it asked about.
    public string Said =>
        ": " + (read.Count == 0 ? "none read" : string.Join("; ", read))
        + (unserved.Count == 0 ? string.Empty : "; not read: " + string.Join("; ", unserved))
        + FormattableString.Invariant($"; {requests} request(s)");

    // The fetcher's row for the night, written whether or not a member was asked about, so the stage's rows say the
    // whole night ran.
    public async Task RecordAsync(string runId, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", written);
        command.Parameters.AddWithValue("$network_requests", requests);
        command.Parameters.AddWithValue("$detail", read.Count + unserved.Count == 0
            ? "no member was passed by a rule reading analysts' estimates, so none was asked for"
            : FormattableString.Invariant($"{read.Count + unserved.Count} member(s) a rule reading analysts' estimates passed on everything else") + Said);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string SwingWords(EstimateReading reading) => Core.Candidates.SwingFilterRule.Words(reading);

    static async Task<IReadOnlyDictionary<string, EstimateReading>> StoredAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = StoredOn;
        command.Parameters.AddWithValue("$night", Stamp(night));

        var held = new Dictionary<string, EstimateReading>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held[reader.GetString(0)] = new EstimateReading(
                reader.IsDBNull(1) ? null : DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(2) ? null : Money.FromStorage(reader.GetString(2)),
                reader.IsDBNull(3) ? null : Money.FromStorage(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetString(4));
        }

        return held;
    }

    static async Task<int> InsertAsync(SqliteConnection connection, string ticker, DateOnly night, EstimateReading reading, string runId, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Insert;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$night", Stamp(night));
        command.Parameters.AddWithValue("$year_end", reading.YearEnd is { } end ? Stamp(end) : DBNull.Value);
        command.Parameters.AddWithValue("$current_estimate", reading.Current is { } now ? Money.ToStorage(now) : DBNull.Value);
        command.Parameters.AddWithValue("$days_ago_estimate", reading.DaysAgo is { } then ? Money.ToStorage(then) : DBNull.Value);
        command.Parameters.AddWithValue("$not_read", (object?)reading.NotRead ?? DBNull.Value);
        command.Parameters.AddWithValue("$run_id", runId);

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
