using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Indicators;

public sealed record ChartAveragesOutcome(int NamesWarmed, int NamesWithout, int RowsWritten, int Stopped);

// The chart's averages over the sessions before the store's year.
//
// The indicator engine reads the bar store, which holds one year, so its 20-, 50- and 200-day averages have no value on
// the year's first 19, 49 and 199 sessions, and the chart drew the 200-day line from two thirds of the way across. This
// reads each name's sessions before its first stored bar from the pulled history, at most the longest window less one,
// brought to the bar store's scale by the two closes of the first session both hold, and computes the three averages over
// the joined series with the indicator arithmetic as it stands, keeping only the values the indicator rows leave empty.
// Only the chart draws them; no rule, level, listing, gate or card reads them, and the indicator engine is not edited, so
// no rule's version moves.
// see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step only the chart reads
public sealed class ChartAverager : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.ChartAverage, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "chart-averages";

    // The averages the chart draws, and the sessions before the year the longest of them needs.
    public static IReadOnlyList<string> Averages { get; } = [IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200];

    public const int WarmUp = 199;

    // Why a name holds no warm-up, as the chart's key says it.
    public const string NoPull = "no pulled history before its first stored session";
    public const string NoSharedSession = "no pulled close on its first stored session to set the scale by";
    public const string BeforeTheCalendar = "no pulled session the exchange's calendar covers before its first stored session";
    public const string MissingSession = "the pull is missing the session before its first stored session";

    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    const string BarsFor = "SELECT session_date, high, low, close, volume FROM bar WHERE ticker = $ticker ORDER BY session_date;";

    // The newest pull holding the name's first stored session, which is the pull the scale is read from and the
    // warm-up taken from.
    const string PullHolding = @"
        SELECT pull, close FROM pulled_bar
        WHERE ticker = $ticker AND session_date = $first
        ORDER BY pull DESC
        LIMIT 1;
    ";

    const string WarmUpFrom = @"
        SELECT session_date, high, low, close, volume FROM pulled_bar
        WHERE ticker = $ticker AND pull = $pull AND session_date < $first AND session_date >= $covered
        ORDER BY session_date DESC
        LIMIT $count;
    ";

    const string AnyPulledBefore = "SELECT 1 FROM pulled_bar WHERE ticker = $ticker AND session_date < $first LIMIT 1;";

    const string StoredClose = "SELECT close FROM bar WHERE ticker = $ticker AND session_date = $session;";

    const string Clear = "DELETE FROM chart_average WHERE ticker = $ticker;";

    // A name the bar store no longer holds draws no chart, so nothing of its own is kept.
    const string ClearUnheld = "DELETE FROM chart_average WHERE ticker NOT IN (SELECT DISTINCT ticker FROM bar);";

    const string Insert = @"
        INSERT INTO chart_average (ticker, name, night, sessions, pull, reason)
        VALUES ($ticker, $name, $night, $sessions, $pull, $reason);
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

    public ChartAverager(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<ChartAveragesOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var night = clock.SessionDateAt(startedAt);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var (warmed, without, rows) = (0, 0, 0);
        var stop = new SeriesGapStop();

        foreach (var ticker in await TickersAsync(connection, cancellation))
        {
            var bars = await BarsAsync(connection, ticker, cancellation);

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            await ExecuteAsync(connection, transaction, Clear, [("$ticker", ticker)], cancellation);

            // A name whose stored series holds a hole is computed for nothing, as the indicator engine computes it.
            if (bars.Count == 0 || stop.Stops(ticker, [.. bars.Select(bar => bar.SessionDate)]))
            {
                await transaction.CommitAsync(cancellation);

                continue;
            }

            var (pull, reason, warmUp) = await WarmUpAsync(connection, ticker, bars[0], cancellation);

            var points = IndicatorSeries.For([.. warmUp, .. bars]);
            var stored = bars.Select(bar => bar.SessionDate).ToHashSet();
            var bare = IndicatorSeries.For(bars)
                .Where(point => point.Value is null)
                .Select(point => (point.SessionDate, point.Name))
                .ToHashSet();

            foreach (var name in Averages)
            {
                // The values the indicator rows leave empty, on the stored sessions alone.
                var filled = points
                    .Where(point => point.Name == name && point.Value is not null && stored.Contains(point.SessionDate) && bare.Contains((point.SessionDate, name)))
                    .Select(point => new object[] { point.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), point.Value!.Value })
                    .ToArray();

                await ExecuteAsync(
                    connection,
                    transaction,
                    Insert,
                    [
                        ("$ticker", ticker),
                        ("$name", name),
                        ("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
                        ("$sessions", JsonSerializer.Serialize(filled)),
                        ("$pull", pull),
                        ("$reason", reason),
                    ],
                    cancellation);

                rows++;
            }

            await transaction.CommitAsync(cancellation);

            if (pull is null)
            {
                without++;
            }
            else
            {
                warmed++;
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = ClearUnheld;

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await RecordAsync(connection, runId, startedAt, warmed, without, rows, stop.Report(), cancellation);

        return new ChartAveragesOutcome(warmed, without, rows, stop.Stopped);
    }

    // The sessions before a name's first stored bar from the newest pull holding that session, newest first and no
    // further back than the exchange's calendar is covered or a session the pull is missing, brought to the bar
    // store's scale by the ratio of the two closes on the first stored session, taken as prices.
    static async Task<(string? Pull, string? Reason, IReadOnlyList<SeriesBar> WarmUp)> WarmUpAsync(
        SqliteConnection connection,
        string ticker,
        SeriesBar first,
        CancellationToken cancellation)
    {
        var day = first.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        string? pull = null;
        decimal? pulledClose = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PullHolding;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$first", day);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                pull = reader.GetString(0);
                pulledClose = Money.FromStorage(reader.GetString(1));
            }
        }

        if (pull is null || pulledClose is not > 0m)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = AnyPulledBefore;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$first", day);

            return (null, await command.ExecuteScalarAsync(cancellation) is not null ? NoSharedSession : NoPull, []);
        }

        decimal ratio;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = StoredClose;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", day);

            ratio = Money.FromStorage((string)(await command.ExecuteScalarAsync(cancellation))!) / pulledClose.Value;
        }

        var rows = new List<(DateOnly Session, decimal High, decimal Low, decimal Close, long Volume)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = WarmUpFrom;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$pull", pull);
            command.Parameters.AddWithValue("$first", day);
            command.Parameters.AddWithValue("$covered", ExchangeClosures.CoveredFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$count", WarmUp);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                rows.Add((
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Money.FromStorage(reader.GetString(1)),
                    Money.FromStorage(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3)),
                    reader.GetInt64(4)));
            }
        }

        // Newest first, kept while each is the exchange's session before the one after it, so the warm-up stops at the
        // first session the pull is missing rather than averaging across it.
        var kept = new List<SeriesBar>();
        var after = first.SessionDate;

        foreach (var row in rows)
        {
            if (!ExchangeClosures.IsSession(row.Session) || ExchangeClosures.SessionsBetween(row.Session, after).Count != 0)
            {
                break;
            }

            kept.Add(new SeriesBar(
                row.Session,
                Statistic.FromPrice(row.High * ratio),
                Statistic.FromPrice(row.Low * ratio),
                Statistic.FromPrice(row.Close * ratio),
                Statistic.FromVolume(row.Volume)));

            after = row.Session;
        }

        kept.Reverse();

        return rows.Count == 0 ? (null, BeforeTheCalendar, [])
            : kept.Count == 0 ? (null, MissingSession, [])
            : (pull, null, kept);
    }

    static async Task<IReadOnlyList<SeriesBar>> BarsAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
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

    static async Task<IReadOnlyList<string>> TickersAsync(SqliteConnection connection, CancellationToken cancellation)
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

    static async Task ExecuteAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string sql, (string Name, object? Value)[] parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task RecordAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int warmed, int without, int rows, string gaps, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", FormattableString.Invariant($"{warmed} name(s) read from a pull, {without} with none, {rows} row(s){gaps}"));

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
