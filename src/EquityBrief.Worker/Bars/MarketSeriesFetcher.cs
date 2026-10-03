using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// What the market series fetcher did on a night: the night's session it asked to, each series stored with the
// sessions it added, each series it stored nothing for and why, the rows written and the requests made.
public sealed record MarketSeriesOutcome(DateOnly? Through, IReadOnlyList<string> Stored, IReadOnlyList<string> Refused, int RowsWritten, int Requests)
{
    public string Detail =>
        Through is not { } night
            ? "no session is stored, so no market series was asked for"
            : FormattableString.Invariant($"through {night:yyyy-MM-dd}: ")
                + string.Join("; ", Stored.Concat(Refused))
                + FormattableString.Invariant($"; {RowsWritten} session(s) stored, {Requests} request(s)");
}

// The market series fetcher. Asks the provider for the index's and the VIX's daily series over the days before the
// night's session that the market switches read back over, one request a series whatever the index's size, and
// stores each session no night has stored yet, apart from the members' bars. Insert only: a session already held
// keeps the row the night that first stored it wrote. A series the provider refuses, does not answer in time, sends
// nothing for or answers in a form that cannot be read stores nothing and is named on the stage's own row, and it
// stops nothing, since only the switches read it and each reads a night it holds no close for as closed. It runs in
// the fetch step after the bar fetcher, reading the night's session off the bars that step stored, and calls no
// model.
// see: The night asks for the index's and the VIX's daily closes once a series, and keeps them apart from the members' bars
public sealed class MarketSeriesFetcher(IMarketSeriesFeed feed, IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.HistoricalPrice]);

    public const string Stage = "market-series";

    // The series it asks for, in this order: the index itself and the VIX.
    public static IReadOnlyList<string> Series { get; } = [MarketCloses.Index, MarketCloses.Vix];

    // The calendar days before the night's session it asks for: more than a year, so the 200 sessions the
    // index's average reads and the ten the VIX is read back over are held with the exchange's holidays among them.
    public const int WindowDays = 400;

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string Insert = @"
        INSERT INTO market_bar (series, session_date, open, high, low, close, run_id)
        VALUES ($series, $session_date, $open, $high, $low, $close, $run_id)
        ON CONFLICT (series, session_date) DO NOTHING;
    ";

    const string HeldOf = "SELECT COUNT(*) FROM market_bar WHERE series = $series;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<MarketSeriesOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var night = await NewestAsync(connection, cancellation);

        if (night is not { } through)
        {
            var nothing = new MarketSeriesOutcome(null, [], [], 0, 0);

            await AppendAsync(connection, null, runId, startedAt, nothing, cancellation);

            return nothing;
        }

        var requestsBefore = feed.Requests;
        var answered = new List<(string Series, IReadOnlyList<ProviderBar> Bars)>();
        var refused = new List<string>();

        foreach (var series in Series)
        {
            try
            {
                var bars = await feed.SeriesAsync(series, through.AddDays(-WindowDays), through, cancellation);

                if (bars.Count == 0)
                {
                    refused.Add($"{series}: the provider sent no session, so nothing was stored for it");
                }
                else
                {
                    answered.Add((series, bars));
                }
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                refused.Add($"{series}: the provider did not answer in time on any try, so nothing was stored for it");
            }
            catch (Exception failure) when (failure is not OperationCanceledException)
            {
                // Every failure of the request but the night's own cancellation is a series not served, whatever
                // its shape: an answer that arrived and cannot be read stops nothing, as a refusal stops nothing.
                refused.Add(failure is ProviderRefusal or FormatException
                    ? $"{series}: nothing was stored for it, {failure.Message}"
                    : $"{series}: nothing was stored for it, its answer could not be read: {failure.Message}");
            }
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var stored = new List<string>();
        var written = 0;

        foreach (var (series, bars) in answered)
        {
            var added = 0;

            foreach (var bar in bars)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = Insert;
                insert.Parameters.AddWithValue("$series", series);
                insert.Parameters.AddWithValue("$session_date", Stamp(bar.SessionDate));
                Money.Bind(insert, "$open", bar.Open);
                Money.Bind(insert, "$high", bar.High);
                Money.Bind(insert, "$low", bar.Low);
                Money.Bind(insert, "$close", bar.Close);
                insert.Parameters.AddWithValue("$run_id", runId);

                added += await insert.ExecuteNonQueryAsync(cancellation);
            }

            await using var held = connection.CreateCommand();

            held.Transaction = transaction;
            held.CommandText = HeldOf;
            held.Parameters.AddWithValue("$series", series);

            var holds = Convert.ToInt64(await held.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
            var newest = bars.Max(bar => bar.SessionDate);

            written += added;
            stored.Add(
                FormattableString.Invariant($"{series}: {added} new of the {bars.Count} session(s) sent, {holds} held")
                + (newest < through ? FormattableString.Invariant($", the newest sent {newest:yyyy-MM-dd} and none for the night") : string.Empty));
        }

        var outcome = new MarketSeriesOutcome(through, stored, refused, written, feed.Requests - requestsBefore);

        await AppendAsync(connection, transaction, runId, startedAt, outcome, cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction? transaction, string runId, DateTimeOffset startedAt, MarketSeriesOutcome outcome, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue("$detail", outcome.Detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
