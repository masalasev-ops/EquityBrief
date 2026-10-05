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

// The market series fetcher. Asks the provider for the index's, the VIX's and the eleven sector funds' daily series
// over the days before the night's session that the market switches and the sector heavyweights read back over, one
// request a series whatever the index's size, and stores each session no night has stored yet, apart from the
// members' bars. A session already held keeps the row the night that first stored it wrote, but a fund's: the
// provider adjusts a fund's closes for each dividend it pays, so a fund's held session the answer states at another
// price is written again from the answer, and every fund's return is read over closes on one basis. A series the
// provider refuses, does not answer in time, sends nothing for or answers in a form that cannot be read stores nothing
// and is named on the stage's own row, and it stops nothing, since a switch reads a night it holds no close for as
// closed and a heavyweights' rebalance waits for a night the store holds one. It runs in the fetch step after the bar
// fetcher, reading the night's session off the bars that step stored, and calls no model.
// see: The night asks for the market series' daily closes once a series, and keeps them apart from the members' bars
public sealed class MarketSeriesFetcher(IMarketSeriesFeed feed, IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.HistoricalPrice]);

    public const string Stage = "market-series";

    // The funds each index's readings compare, from 15.1: the S&P 500's, 400's and 600's own funds, read against each
    // other for small companies beating large, and the high-yield bond fund, read for credit.
    // see: The universe is the S&P 1500's three indices with each member tagged by its index, and membership is fetched
    public static IReadOnlyList<string> IndexAndCreditFunds { get; } = ["SPY", "IJH", "IJR", "HYG"];

    // The series it asks for, in this order: the index itself, the VIX, each sector's fund by its sector's name, and
    // the index and credit funds.
    public static IReadOnlyList<string> Series { get; } =
        [MarketCloses.Index, MarketCloses.Vix, .. GicsSectors.Eleven.Select(sector => GicsSectors.Funds[sector]), .. IndexAndCreditFunds];

    // The calendar days before the night's session it asks for: more than a year, so the 200 sessions the
    // index's average reads, the ten the VIX is read back over and the 252 a fund's year and a beta read are held with
    // the exchange's holidays among them.
    public const int WindowDays = 400;

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string Insert = @"
        INSERT INTO market_bar (series, session_date, open, high, low, close, run_id)
        VALUES ($series, $session_date, $open, $high, $low, $close, $run_id)
        ON CONFLICT (series, session_date) DO NOTHING;
    ";

    // A fund's session held at another price than the answer states, written again from the answer.
    const string Restate = @"
        UPDATE market_bar
        SET open = $open, high = $high, low = $low, close = $close, run_id = $run_id
        WHERE series = $series AND session_date = $session_date;
    ";

    const string HeldCloses = "SELECT session_date, close FROM market_bar WHERE series = $series;";

    // Whether a series is a fund, a sector's or an index and credit fund, whose held sessions the answer may state again.
    public static bool IsFund(string series) =>
        GicsSectors.Funds.Values.Contains(series, StringComparer.Ordinal) || IndexAndCreditFunds.Contains(series, StringComparer.Ordinal);

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
            var restated = 0;
            var closesHeld = new Dictionary<string, decimal>(StringComparer.Ordinal);

            if (IsFund(series))
            {
                await using var closes = connection.CreateCommand();

                closes.Transaction = transaction;
                closes.CommandText = HeldCloses;
                closes.Parameters.AddWithValue("$series", series);

                await using var reader = await closes.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    closesHeld[reader.GetString(0)] = Money.FromStorage(reader.GetString(1));
                }
            }

            foreach (var bar in bars)
            {
                // A fund's held session the answer states at another close is written again; any other held session
                // keeps its first row.
                var again = closesHeld.TryGetValue(Stamp(bar.SessionDate), out var close) && close != bar.Close;

                await using var write = connection.CreateCommand();

                write.Transaction = transaction;
                write.CommandText = again ? Restate : Insert;
                write.Parameters.AddWithValue("$series", series);
                write.Parameters.AddWithValue("$session_date", Stamp(bar.SessionDate));
                Money.Bind(write, "$open", bar.Open);
                Money.Bind(write, "$high", bar.High);
                Money.Bind(write, "$low", bar.Low);
                Money.Bind(write, "$close", bar.Close);
                write.Parameters.AddWithValue("$run_id", runId);

                var changed = await write.ExecuteNonQueryAsync(cancellation);

                if (again)
                {
                    restated += changed;
                }
                else
                {
                    added += changed;
                }
            }

            await using var held = connection.CreateCommand();

            held.Transaction = transaction;
            held.CommandText = HeldOf;
            held.Parameters.AddWithValue("$series", series);

            var holds = Convert.ToInt64(await held.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
            var newest = bars.Max(bar => bar.SessionDate);

            written += added + restated;
            stored.Add(
                FormattableString.Invariant($"{series}: {added} new of the {bars.Count} session(s) sent, {holds} held")
                + (restated > 0 ? FormattableString.Invariant($", {restated} held session(s) written again at the closes the answer states") : string.Empty)
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
