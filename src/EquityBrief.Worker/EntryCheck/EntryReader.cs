using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.EntryCheck;

public sealed record EntryReadOutcome(DateOnly? Night, int Read, int WithoutABar);

// The entry check's readings of every member holding a bar on the night, so a stock no rule picked is read too. Arithmetic over
// the stored bars and the history pulled before them; it asks nothing and no rule, plan, list or gate reads what it writes.
// see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
public sealed class EntryReader : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.EntryReading, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "entry-readings";

    const string Newest = "SELECT MAX(session_date) FROM bar;";

    const string Members = @"
        SELECT DISTINCT ticker FROM membership
        WHERE (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string BarsOf = "SELECT session_date, high, low, close, volume, open, raw_close FROM bar WHERE ticker = $ticker AND session_date <= $session ORDER BY session_date;";

    const string IndicatorsOf = "SELECT name, value FROM indicator WHERE ticker = $ticker AND session_date = $session;";

    const string ClearTheNight = "DELETE FROM entry_reading WHERE session_date = $session;";

    const string StoreReading = @"
        INSERT INTO entry_reading (
            ticker, session_date, close, typical_day, stretch_percent, stretch_days, stretch_share, gain_twenty, rsi, largest_gain,
            volume_ratio, gap_high, gap_low, highest_since, history_start, older_low, older_high, older_on, older_days, run_id)
        VALUES (
            $ticker, $session_date, $close, $typical_day, $stretch_percent, $stretch_days, $stretch_share, $gain_twenty, $rsi, $largest_gain,
            $volume_ratio, $gap_high, $gap_low, $highest_since, $history_start, $older_low, $older_high, $older_on, $older_days, $run_id);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    // Every session the pulled history holds before the stored year, whatever a reading needs of it.
    const int AllBefore = 100_000;

    readonly IClock clock;
    readonly string databaseFile;

    public EntryReader(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<EntryReadOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        string? newest;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Newest;
            newest = await command.ExecuteScalarAsync(cancellation) as string;
        }

        if (newest is null)
        {
            await AppendAsync(connection, runId, started, 0, "no session: the store holds no bar", cancellation);

            return new EntryReadOutcome(null, 0, 0);
        }

        var night = DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var members = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Members;
            command.Parameters.AddWithValue("$session", newest);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                members.Add(reader.GetString(0));
            }
        }

        var readings = new List<EntryReading>();
        var without = 0;

        foreach (var ticker in members)
        {
            cancellation.ThrowIfCancellationRequested();

            var bars = await BarsAsync(connection, ticker, newest, cancellation);

            if (bars.Count == 0 || bars[^1].Session != night)
            {
                without++;

                continue;
            }

            var (typical, rsi, volume) = await IndicatorsAsync(connection, ticker, newest, cancellation);
            var first = bars[0];
            var before = await IndexFamilies.BeforeTheYearAsync(connection, null, ticker, first, AllBefore, cancellation);
            EntryBar Read(SweepBar bar) => new(bar.Session, bar.High, bar.Low, bar.Close, bar.Volume);

            if (EntryReadings.Of(ticker, [.. bars.Select(Read)], [.. before.Select(Read)], typical, rsi, volume) is { } reading)
            {
                readings.Add(reading);
            }
        }

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            await using (var clear = connection.CreateCommand())
            {
                clear.Transaction = transaction;
                clear.CommandText = ClearTheNight;
                clear.Parameters.AddWithValue("$session", newest);

                await clear.ExecuteNonQueryAsync(cancellation);
            }

            foreach (var reading in readings)
            {
                await StoreAsync(connection, transaction, reading, runId, cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        await AppendAsync(connection, runId, started, readings.Count, FormattableString.Invariant($"{readings.Count} member(s) read for {night:yyyy-MM-dd}, {without} holding no bar on it"), cancellation);

        return new EntryReadOutcome(night, readings.Count, without);
    }

    static async Task<IReadOnlyList<SweepBar>> BarsAsync(SqliteConnection connection, string ticker, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BarsOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session", session);

        var bars = new List<SweepBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new SweepBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                reader.GetInt64(4),
                Money.FromStorage(reader.GetString(5)),
                Money.FromStorage(reader.GetString(6))));
        }

        return bars;
    }

    static async Task<(double? Typical, double? Rsi, double? Volume)> IndicatorsAsync(SqliteConnection connection, string ticker, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsOf;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session", session);

        (double? Typical, double? Rsi, double? Volume) read = (null, null, null);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (reader.IsDBNull(1))
            {
                continue;
            }

            var value = reader.GetDouble(1);

            read = reader.GetString(0) switch
            {
                IndicatorSeries.Atr14 => read with { Typical = value },
                IndicatorSeries.Rsi14 => read with { Rsi = value },
                IndicatorSeries.VolAvg50 => read with { Volume = value },
                _ => read,
            };
        }

        return read;
    }

    static async Task StoreAsync(SqliteConnection connection, SqliteTransaction transaction, EntryReading reading, string runId, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        static object Or(object? value) => value ?? DBNull.Value;
        static object Day(DateOnly? day) => day is { } on ? on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value;
        static object Price(decimal? price) => price is { } value ? Money.ToStorage(value) : DBNull.Value;

        command.Transaction = transaction;
        command.CommandText = StoreReading;
        command.Parameters.AddWithValue("$ticker", reading.Ticker);
        command.Parameters.AddWithValue("$session_date", Day(reading.Session));
        command.Parameters.AddWithValue("$close", Money.ToStorage(reading.Close));
        command.Parameters.AddWithValue("$typical_day", Or(reading.TypicalDay));
        command.Parameters.AddWithValue("$stretch_percent", Or(reading.StretchPercent));
        command.Parameters.AddWithValue("$stretch_days", Or(reading.StretchDays));
        command.Parameters.AddWithValue("$stretch_share", Or(reading.StretchShare));
        command.Parameters.AddWithValue("$gain_twenty", Or(reading.GainTwenty));
        command.Parameters.AddWithValue("$rsi", Or(reading.Rsi));
        command.Parameters.AddWithValue("$largest_gain", reading.LargestGain is { } largest ? largest ? 1 : 0 : DBNull.Value);
        command.Parameters.AddWithValue("$volume_ratio", Or(reading.VolumeRatio));
        command.Parameters.AddWithValue("$gap_high", Price(reading.GapHigh));
        command.Parameters.AddWithValue("$gap_low", Price(reading.GapLow));
        command.Parameters.AddWithValue("$highest_since", Day(reading.HighestSince));
        command.Parameters.AddWithValue("$history_start", Day(reading.HistoryStart));
        command.Parameters.AddWithValue("$older_low", Price(reading.OlderLow));
        command.Parameters.AddWithValue("$older_high", Price(reading.OlderHigh));
        command.Parameters.AddWithValue("$older_on", Day(reading.OlderOn));
        command.Parameters.AddWithValue("$older_days", Or(reading.OlderDays));
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset started, int rows, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
