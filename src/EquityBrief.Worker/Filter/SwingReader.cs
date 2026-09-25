using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

public sealed record SwingReadOutcome(int Members, int RowsWritten, int Read, int Stale, int Gapped, int NoBars, int RowsDropped, Breadth Breadth);

// The swing reader. Writes, for every member of the index on the night, the readings the swing
// filter's gates are measured against, and for the night the breadth of the whole index.
//
// A row for every member, a name the night could read nothing for among them with the reason,
// for the reason the listings row is written for every member: a gate whose reading is absent
// has to say why rather than find no row. It makes no request and calls no model: everything it
// reads is already in the store.
// see: The nightly run is arithmetic only
//
// The arithmetic is in `SwingReadings`, as pure functions of a session-ordered series and of the
// night's members. What is here is reading, writing and the run log.
// see: Code owns every number
public sealed class SwingReader : IComponent
{
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.SwingReading, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.MarketReading, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "swing-readings";

    // The members on the night's session, joined by it where the join date is known and not left
    // by it.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // Every stored bar at once, because a place among the members' returns reads every member's.
    const string AllBars = @"
        SELECT ticker, session_date, high, low, close, volume
        FROM bar
        ORDER BY ticker, session_date;
    ";

    // The four readings the night's indicators give on its own session: the typical daily move,
    // the fifty-day average volume, and the two averages breadth is read against.
    const string IndicatorsOn = @"
        SELECT ticker, name, value
        FROM indicator
        WHERE session_date = $session AND name IN ($typical, $volume, $long, $short);
    ";

    // A night run again replaces its own set whole, so a name no longer a member keeps no row on it.
    const string ClearTheNight = "DELETE FROM swing_reading WHERE session_date = $session;";

    const string Insert = @"
        INSERT INTO swing_reading (
            ticker, session_date, bars, return_short, return_long, place_short, place_long, strength,
            recent_high, high_session, pullback_sessions, depth, dry_up, tightness, note)
        VALUES (
            $ticker, $session_date, $bars, $return_short, $return_long, $place_short, $place_long, $strength,
            $recent_high, $high_session, $pullback_sessions, $depth, $dry_up, $tightness, $note);
    ";

    const string UpsertNight = @"
        INSERT INTO market_reading (
            session_date, members, counted, above, breadth, counted_context, above_context, breadth_context,
            volume_counted, median_volume_ratio)
        VALUES (
            $session_date, $members, $counted, $above, $breadth, $counted_context, $above_context, $breadth_context,
            $volume_counted, $median_volume_ratio)
        ON CONFLICT (session_date) DO UPDATE SET
            members = excluded.members,
            counted = excluded.counted,
            above = excluded.above,
            breadth = excluded.breadth,
            counted_context = excluded.counted_context,
            above_context = excluded.above_context,
            breadth_context = excluded.breadth_context,
            volume_counted = excluded.volume_counted,
            median_volume_ratio = excluded.median_volume_ratio;
    ";

    // The retention, one year back from the newest stored session, which is the boundary every
    // computed table drops at.
    const string DropOlderThan = @"
        DELETE FROM swing_reading WHERE session_date < $oldest;
        DELETE FROM market_reading WHERE session_date < $oldest;
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

    public SwingReader(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<SwingReadOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The night is the newest session any name holds, read off the store rather than the
        // clock, for the reason the listings stage reads it there: a replay run on a weekend has a
        // clock a session ahead of every bar.
        var night = await NewestAsync(connection, cancellation) ?? clock.SessionDateAt(clock.UtcNow);
        var members = await MembersAsync(connection, indexCode, clock.SessionDateAt(clock.UtcNow), cancellation);
        var series = await AllBarsAsync(connection, cancellation);
        var indicators = await IndicatorsAsync(connection, night, cancellation);
        var stop = new SeriesGapStop();

        var readings = new Dictionary<string, SwingReading>(StringComparer.Ordinal);
        var notes = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var ticker in members)
        {
            var bars = series.TryGetValue(ticker, out var held) ? held : [];

            if (bars.Count == 0)
            {
                notes[ticker] = NoBarStored;
                continue;
            }

            if (bars[^1].SessionDate < night)
            {
                notes[ticker] = NoBarThisSession(bars[^1].SessionDate);
                continue;
            }

            // see: A gap is a session the exchange traded and the store does not hold
            if (stop.Stops(ticker, [.. bars.Select(bar => bar.SessionDate)]))
            {
                notes[ticker] = GapAt(stop.Gaps[^1].SessionDate);
                continue;
            }

            readings[ticker] = SwingReadings.Of(
                bars,
                Indicator(indicators, ticker, IndicatorSeries.Atr14),
                Indicator(indicators, ticker, IndicatorSeries.VolAvg50));
        }

        var placesShort = SwingReadings.Places(Returns(readings, reading => reading.ReturnShort));
        var placesLong = SwingReadings.Places(Returns(readings, reading => reading.ReturnLong));

        var breadth = SwingReadings.BreadthOf(members.Count, Held(readings, series, indicators, SwingReadings.AverageNamed(SwingReadings.BreadthAverageSessions)));
        var breadth50 = SwingReadings.BreadthOf(members.Count, Held(readings, series, indicators, SwingReadings.AverageNamed(SwingReadings.ContextAverageSessions)));
        var ratios = VolumeRatios(readings, series, indicators);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = ClearTheNight;
            clear.Parameters.AddWithValue("$session", Stamp(night));

            await clear.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var ticker in members)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session_date", Stamp(night));

            var reading = readings.GetValueOrDefault(ticker);
            double? placeShort = placesShort.TryGetValue(ticker, out var shortPlace) ? shortPlace : null;
            double? placeLong = placesLong.TryGetValue(ticker, out var longPlace) ? longPlace : null;

            command.Parameters.AddWithValue("$bars", reading?.Bars ?? (series.TryGetValue(ticker, out var bars) ? bars.Count : 0));
            command.Parameters.AddWithValue("$return_short", Nullable(reading?.ReturnShort));
            command.Parameters.AddWithValue("$return_long", Nullable(reading?.ReturnLong));
            command.Parameters.AddWithValue("$place_short", Nullable(placeShort));
            command.Parameters.AddWithValue("$place_long", Nullable(placeLong));
            command.Parameters.AddWithValue("$strength", Nullable(placeShort is { } one && placeLong is { } two ? (one + two) / 2 : null));
            command.Parameters.AddWithValue("$recent_high", reading?.High is { } high ? Money.ToStorage(high) : DBNull.Value);
            command.Parameters.AddWithValue("$high_session", reading?.HighSession is { } made ? Stamp(made) : DBNull.Value);
            command.Parameters.AddWithValue("$pullback_sessions", reading?.PullbackSessions is { } since ? since : DBNull.Value);
            command.Parameters.AddWithValue("$depth", Nullable(reading?.Depth));
            command.Parameters.AddWithValue("$dry_up", Nullable(reading?.DryUp));
            command.Parameters.AddWithValue("$tightness", Nullable(reading?.Tightness));
            command.Parameters.AddWithValue("$note", notes.TryGetValue(ticker, out var why) ? why : DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = UpsertNight;
            command.Parameters.AddWithValue("$session_date", Stamp(night));
            command.Parameters.AddWithValue("$members", members.Count);
            command.Parameters.AddWithValue("$counted", breadth.Counted);
            command.Parameters.AddWithValue("$above", breadth.Above);
            command.Parameters.AddWithValue("$breadth", Nullable(breadth.Share));
            command.Parameters.AddWithValue("$counted_context", breadth50.Counted);
            command.Parameters.AddWithValue("$above_context", breadth50.Above);
            command.Parameters.AddWithValue("$breadth_context", Nullable(breadth50.Share));
            command.Parameters.AddWithValue("$volume_counted", ratios.Count);
            command.Parameters.AddWithValue("$median_volume_ratio", Nullable(SwingReadings.Median(ratios)));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        var dropped = 0;

        await using (var drop = connection.CreateCommand())
        {
            drop.Transaction = (SqliteTransaction)transaction;
            drop.CommandText = DropOlderThan;
            drop.Parameters.AddWithValue("$oldest", Stamp(night.AddYears(-BarFetcher.RetentionYears)));

            dropped = await drop.ExecuteNonQueryAsync(cancellation);
        }

        var stale = notes.Values.Count(note => note.StartsWith(NoBarThisSessionOpening, StringComparison.Ordinal));
        var gapped = notes.Values.Count(note => note.StartsWith(GapOpening, StringComparison.Ordinal));
        var none = notes.Values.Count(note => note == NoBarStored);

        await RecordAsync(connection, (SqliteTransaction)transaction, runId, startedAt, members.Count, readings.Count, stale, gapped, none, dropped, stop.Report(), breadth, cancellation);

        await transaction.CommitAsync(cancellation);

        return new SwingReadOutcome(members.Count, members.Count, readings.Count, stale, gapped, none, dropped, breadth);
    }

    public const string NoBarStored = "no bar is stored for the name";

    const string NoBarThisSessionOpening = "no bar for this session";

    const string GapOpening = "the stored series has a gap at";

    static string NoBarThisSession(DateOnly last) =>
        NoBarThisSessionOpening + "; the last session stored for the name is " + Stamp(last);

    static string GapAt(DateOnly gap) => GapOpening + " " + Stamp(gap) + ", so nothing is read across it";

    // The gap a row's note names, and none where the note names none.
    public static DateOnly? GapIn(string? note) =>
        note is not null
            && note.StartsWith(GapOpening + " ", StringComparison.Ordinal)
            && note.Length >= GapOpening.Length + 11
            && DateOnly.TryParseExact(note.AsSpan(GapOpening.Length + 1, 10), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var gap)
                ? gap
                : null;

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static object Nullable(double? value) => value is { } present ? present : DBNull.Value;

    static double? Indicator(IReadOnlyDictionary<(string, string), double> indicators, string ticker, string name) =>
        indicators.TryGetValue((ticker, name), out var value) ? value : null;

    static IReadOnlyDictionary<string, double> Returns(
        IReadOnlyDictionary<string, SwingReading> readings,
        Func<SwingReading, double?> pick) =>
        readings
            .Where(pair => pick(pair.Value) is not null)
            .ToDictionary(pair => pair.Key, pair => pick(pair.Value)!.Value, StringComparer.Ordinal);

    // The members read tonight that hold both a close and the average, which is what breadth is
    // counted over.
    static IReadOnlyList<(decimal Close, double Average)> Held(
        IReadOnlyDictionary<string, SwingReading> readings,
        IReadOnlyDictionary<string, IReadOnlyList<ReadingBar>> series,
        IReadOnlyDictionary<(string, string), double> indicators,
        string average) =>
    [
        .. readings.Keys
            .Where(ticker => indicators.ContainsKey((ticker, average)))
            .Select(ticker => (series[ticker][^1].Close, indicators[(ticker, average)])),
    ];

    // Tonight's volume against the fifty-day average for every member read tonight trading some
    // volume and holding an average above nought.
    static IReadOnlyList<double> VolumeRatios(
        IReadOnlyDictionary<string, SwingReading> readings,
        IReadOnlyDictionary<string, IReadOnlyList<ReadingBar>> series,
        IReadOnlyDictionary<(string, string), double> indicators) =>
    [
        .. readings.Keys
            .Where(ticker => series[ticker][^1].Volume > 0
                && indicators.TryGetValue((ticker, IndicatorSeries.VolAvg50), out var average) && average > 0)
            .Select(ticker => Core.Prices.Statistic.FromVolume(series[ticker][^1].Volume) / indicators[(ticker, IndicatorSeries.VolAvg50)]),
    ];

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static async Task<IReadOnlyList<string>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", Stamp(session));

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<IReadOnlyDictionary<string, IReadOnlyList<ReadingBar>>> AllBarsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AllBars;

        var series = new Dictionary<string, List<ReadingBar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!series.TryGetValue(ticker, out var bars))
            {
                series[ticker] = bars = [];
            }

            bars.Add(new ReadingBar(
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.GetInt64(5)));
        }

        return series.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<ReadingBar>)pair.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<(string, string), double>> IndicatorsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsOn;
        command.Parameters.AddWithValue("$session", Stamp(night));
        command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);
        command.Parameters.AddWithValue("$volume", IndicatorSeries.VolAvg50);
        command.Parameters.AddWithValue("$long", SwingReadings.AverageNamed(SwingReadings.BreadthAverageSessions));
        command.Parameters.AddWithValue("$short", SwingReadings.AverageNamed(SwingReadings.ContextAverageSessions));

        var values = new Dictionary<(string, string), double>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            // A null value is a reading not available, and leaves the name out of what reads it.
            if (!reader.IsDBNull(2))
            {
                values[(reader.GetString(0), reader.GetString(1))] = reader.GetDouble(2);
            }
        }

        return values;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string runId,
        DateTimeOffset startedAt,
        int members,
        int read,
        int stale,
        int gapped,
        int none,
        int dropped,
        string gaps,
        Breadth breadth,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", members);
        command.Parameters.AddWithValue(
            "$detail",
            FormattableString.Invariant($"{members} member(s), {read} read, {stale} with no bar for the session, {gapped} gapped, {none} holding no bar, {dropped} dropped") + gaps + "; breadth " +
            (breadth.Share is { } share
                ? FormattableString.Invariant($"{share * 100:0.0}% over {breadth.Counted} of {breadth.Members}")
                : FormattableString.Invariant($"not available, {breadth.Counted} of {breadth.Members} members holding a close and a 200-day average")));

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
