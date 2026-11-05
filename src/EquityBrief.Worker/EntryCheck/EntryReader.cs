using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.EntryCheck;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Releases;
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
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read),
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

    const string NewestRelease = @"
        SELECT d.accepted_at, d.text_gzip FROM release_document d
        WHERE d.cik = (SELECT c.cik FROM company c WHERE c.ticker = $ticker AND c.cik IS NOT NULL ORDER BY c.fetched_at DESC LIMIT 1)
          AND d.release = 1 AND substr(d.accepted_at, 1, 10) <= $session
        ORDER BY d.accepted_at DESC
        LIMIT 1;
    ";

    const string NewestReport = @"
        SELECT event_date, detail FROM calendar
        WHERE ticker = $ticker AND kind = 'earnings' AND event_date <= $session
        ORDER BY event_date DESC
        LIMIT 1;
    ";

    const string ClearTheNight = "DELETE FROM entry_reading WHERE session_date = $session;";

    const string StoreReading = @"
        INSERT INTO entry_reading (
            ticker, session_date, close, typical_day, stretch_percent, stretch_days, stretch_share, gain_twenty, rsi, largest_gain,
            volume_ratio, gap_high, gap_low, highest_since, history_start, older_low, older_high, older_on, older_days, run_id,
            release_on, one_off_sentence, one_off_amount, one_off_tied, one_off_direction, beat)
        VALUES (
            $ticker, $session_date, $close, $typical_day, $stretch_percent, $stretch_days, $stretch_share, $gain_twenty, $rsi, $largest_gain,
            $volume_ratio, $gap_high, $gap_low, $highest_since, $history_start, $older_low, $older_high, $older_on, $older_days, $run_id,
            $release_on, $one_off_sentence, $one_off_amount, $one_off_tied, $one_off_direction, $beat);
    ";

    // How far a calendar report may stand from a release's day and still be the report it speaks of.
    const int ReportReachDays = 7;

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
                readings.Add(await WithReleaseAsync(connection, reading, newest, cancellation));
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

    // The one-off reading of the member's newest stored results release, and the beat the calendar stores for the report it speaks
    // of, none where the calendar holds no report near the release's day or no estimate and actual for it.
    // see: The entry check's one-off reading reads a stored release by fixed phrases, and says it can miss an item
    static async Task<EntryReading> WithReleaseAsync(SqliteConnection connection, EntryReading reading, string session, CancellationToken cancellation)
    {
        DateOnly on;
        OneOffItem? item;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestRelease;
            command.Parameters.AddWithValue("$ticker", reading.Ticker);
            command.Parameters.AddWithValue("$session", session);

            await using var row = await command.ExecuteReaderAsync(cancellation);

            if (!await row.ReadAsync(cancellation))
            {
                return reading;
            }

            on = DateOnly.ParseExact(row.GetString(0)[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture);
            item = OneOffItems.Of(ResultsReleases.Decompressed((byte[])row[1]));
        }

        decimal? beat = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NewestReport;
            command.Parameters.AddWithValue("$ticker", reading.Ticker);
            command.Parameters.AddWithValue("$session", session);

            await using var row = await command.ExecuteReaderAsync(cancellation);

            if (await row.ReadAsync(cancellation)
                && Math.Abs(DateOnly.ParseExact(row.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - on.DayNumber) <= ReportReachDays)
            {
                beat = BeatOf(row.GetString(1));
            }
        }

        return reading with
        {
            ReleaseOn = on,
            OneOffSentence = item?.Sentence,
            OneOffAmount = item?.Amount,
            OneOffTied = item?.TiedToEarnings,
            OneOffDirection = item?.Direction,
            Beat = beat,
        };
    }

    static decimal? BeatOf(string detail)
    {
        using var document = JsonDocument.Parse(detail);

        decimal? Read(string name) =>
            document.RootElement.TryGetProperty(name, out var said) && said.ValueKind == JsonValueKind.String && decimal.TryParse(said.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var value) ? value : null;

        return Read("actual") is { } actual && Read("estimate") is { } estimate ? actual - estimate : null;
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
        command.Parameters.AddWithValue("$release_on", Day(reading.ReleaseOn));
        command.Parameters.AddWithValue("$one_off_sentence", Or(reading.OneOffSentence));
        command.Parameters.AddWithValue("$one_off_amount", Price(reading.OneOffAmount));
        command.Parameters.AddWithValue("$one_off_tied", reading.OneOffTied is { } tied ? tied ? 1 : 0 : DBNull.Value);
        command.Parameters.AddWithValue("$one_off_direction", Or(reading.OneOffDirection));
        command.Parameters.AddWithValue("$beat", Price(reading.Beat));

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
