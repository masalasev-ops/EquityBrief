using EquityBrief.Core.Indicators;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// One stored bar, handed over exactly as the store holds it.
//
// The prices are decimal because the money rule is decimal in code and TEXT in
// storage, and they cross that boundary through Money and nowhere else. Volume
// is a long because the column is INTEGER.
public sealed record BarRow(
    string Ticker,
    DateOnly SessionDate,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    long Volume);

// One stored indicator, handed over exactly as the store holds it.
//
// Value is a nullable double because the column is REAL and nullable, and an
// indicator whose window is longer than the history behind its session has no
// value. BarCount is what makes that null legible, and it is served rather than
// dropped: a reader who sees no long average is owed the count that explains it.
public sealed record IndicatorRow(
    string Ticker,
    DateOnly SessionDate,
    string Name,
    double? Value,
    int BarCount);

// One stored level band, handed over exactly as the store holds it.
//
// The members arrive as the JSON string the column holds rather than parsed
// into a shape of this project's own. Parsing here would put a second reading of
// that column beside the level builder's writing of it, and the surface that
// draws the summary table is the one that has to understand it.
public sealed record LevelRow(
    string Ticker,
    DateOnly AsOf,
    decimal LowEdge,
    decimal HighEdge,
    string Role,
    bool Immediate,
    int Strength,
    bool HasNonAverageAnchor,
    string Members);

// One band of a name's volume profile, as stored.
public sealed record ProfileRow(
    string Ticker,
    DateOnly AsOf,
    decimal BandLow,
    decimal BandHigh,
    long Shares,
    double ShareOfPeriod);

// One dated event the calendar holds for a name, as stored.
public sealed record CalendarRow(string Ticker, DateOnly EventDate, string Kind, string Timing, string Detail);

// A name's ladder row for one night: the trend state and the plan as stored.
// The plan is handed over as the stored JSON rather than parsed, because
// parsing it here would make this surface the second place the plan's shape is
// stated.
public sealed record LadderRow(string Ticker, DateOnly AsOf, string TrendState, string Plan);

// One of a name's biggest moves, as the store holds it.
//
// No cause. It is a researched claim and lives in `research_section` with its
// source, so the how-it-got-here table's cause column is explicitly absent until
// phase 6 rather than blank.
public sealed record MoveRow(string Ticker, DateOnly SessionDate, int Sessions, double ChangePct, int Rank);

// One row of the universe screen, and every field is a stored column.
//
// Nothing here is derived, which is what keeps the read surface's own claim
// true. The distance the screen sorts on is worked out from these values by the
// projection, in the seam `NameScreen` names, and not here and not in the page.
//
// Every nullable field is a name the night computed nothing for: a joiner with
// no bars has no close, a name with no ladder row has no trend state, and a name
// whose chart has no band on one side has no edge there. Each is drawn as an
// absence rather than as a zero.
public sealed record UniverseRow(
    string Ticker,
    string? Sector,
    decimal? Close,
    string? TrendState,
    decimal? NearestSupport,
    decimal? NearestResistance,
    double? TypicalMove);

// The read surface. Serves what the nightly run stored, and nothing else.
//
// It computes nothing and fetches nothing, which section 7's row states and
// this checkpoint's done condition requires be proved rather than asserted.
// Both halves are meant literally. No value leaving here is derived from
// another: every field is the stored column, converted between the storage form
// and the code form and not otherwise touched. And the project has no feed, no
// client and no reference to the worker, which api-isolation reads from the
// compiled dependency file rather than from the project file.
//
// The seam matters more than it looks. A read surface that computes is a second
// place the arithmetic lives, and the day it disagrees with the nightly run
// nothing says which one is the system.
// see: Code owns every number
// see: A screen reads and renders, and computes nothing
public sealed class ReadApi : IComponent
{
    // Reads every store and appends to the run log, which is section 7's row
    // for this component and the eleven R cells plus one W in its matrix row.
    //
    // Every store means every store the matrix has a column for. The candidate
    // register has no column and is not in the catalogue's phrase, so it is not
    // declared here either; it arrives with the registrar in phase 7.
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Move, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.Fundamentals, Touch.Read),
            new StoreTouch(Store.NewsPulse, Touch.Read),
            new StoreTouch(Store.ResearchSection, Touch.Read),
            new StoreTouch(Store.ThemeSection, Touch.Read),
            new StoreTouch(Store.SourceDocument, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "read-api";

    readonly string databaseFile;
    readonly IClock clock;

    public ReadApi(string databaseFile, IClock clock)
    {
        this.databaseFile = databaseFile;
        this.clock = clock;
    }

    // Ordered by session so the caller does not have to sort, which is the one
    // thing this does beyond selecting. Ordering is not computation: it changes
    // which row comes first and never what a row says.
    const string BarsForName = @"
        SELECT ticker, session_date, open, high, low, close, volume
        FROM bar
        WHERE ticker = $ticker AND session_date >= $from AND session_date <= $to
        ORDER BY session_date;
    ";

    // Ordered by session and then by name, for the reason the bars query is
    // ordered: the caller does not have to sort and ordering says nothing about
    // what a row holds. The null value is served as a null and never as a zero,
    // because a zero is a reading and an absent indicator is not one.
    // Every band for one name at its latest as-of date. The latest rather than
    // all of them, because the level table on the name page is tonight's map and
    // a page holding two nights of bands would be a page holding two maps.
    const string LevelsForName = @"
        SELECT ticker, as_of, low_edge, high_edge, role, immediate, strength,
               has_non_average_anchor, members
        FROM level
        WHERE ticker = $ticker
              AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = $ticker)
        ORDER BY low_edge;
    ";

    // The latest night alone, for the same reason the level query binds as_of to
    // the maximum: a page holding two nights of profile bands is a page holding
    // two histograms of different periods.
    const string ProfileForName = @"
        SELECT ticker, as_of, band_low, band_high, share_count, share_of_period
        FROM volume_profile
        WHERE ticker = $ticker
              AND as_of = (SELECT MAX(as_of) FROM volume_profile WHERE ticker = $ticker)
        ORDER BY band_low;
    ";

    // The next event on or after a date, which is what the fact strip states and
    // what the earnings-soon condition reads. A name with no row answers with
    // nothing, and nothing is what the strip says rather than a guessed date.
    const string NextEventForName = @"
        SELECT ticker, event_date, kind, timing, detail
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $on_or_after
        ORDER BY event_date
        LIMIT 1;
    ";

    const string LadderForName = @"
        SELECT ticker, as_of, trend_state, plan
        FROM ladder
        WHERE ticker = $ticker
        ORDER BY as_of DESC
        LIMIT 1;
    ";

    const string IndicatorsForName = @"
        SELECT ticker, session_date, name, value, bar_count
        FROM indicator
        WHERE ticker = $ticker AND session_date >= $from AND session_date <= $to
        ORDER BY session_date, name;
    ";

    // A name's biggest moves, largest first, which is the order the
    // how-it-got-here table is read down.
    const string MovesForName = @"
        SELECT ticker, session_date, sessions, change_pct, rank
        FROM move
        WHERE ticker = $ticker
        ORDER BY rank;
    ";

    // Every current member of the index, with what the night computed for it.
    //
    // The population is the index rather than the names with bars, which is the
    // same population the ladder builder writes over and the same reason: a name
    // the night computed nothing for is a row saying so, and a name quietly
    // absent would make a count wrong in the direction nobody looks.
    //
    // Left joins throughout, and the bands are the immediate ones the level
    // builder already marked, so the nearest on each side is read rather than
    // searched for. `as_of` binds to the maximum for the same reason the level
    // query binds it: a screen holding two nights of bands is a screen holding
    // two charts.
    const string Universe = @"
        SELECT m.ticker,
               m.sector,
               (SELECT b.close FROM bar b WHERE b.ticker = m.ticker
                ORDER BY b.session_date DESC LIMIT 1),
               (SELECT l.trend_state FROM ladder l WHERE l.ticker = m.ticker
                ORDER BY l.as_of DESC LIMIT 1),
               (SELECT MAX(v.high_edge) FROM level v WHERE v.ticker = m.ticker
                AND v.immediate = 1 AND v.role = 'support'
                AND v.as_of = (SELECT MAX(a.as_of) FROM level a WHERE a.ticker = m.ticker)),
               (SELECT MIN(v.low_edge) FROM level v WHERE v.ticker = m.ticker
                AND v.immediate = 1 AND v.role = 'resistance'
                AND v.as_of = (SELECT MAX(a.as_of) FROM level a WHERE a.ticker = m.ticker)),
               (SELECT i.value FROM indicator i WHERE i.ticker = m.ticker AND i.name = $typical
                ORDER BY i.session_date DESC LIMIT 1)
        FROM membership m
        WHERE m.index_code = $index_code AND m.""left"" IS NULL
        ORDER BY m.ticker;
    ";

    // One row per process start, stage read-api, which is the grain SCHEMA
    // declares for the run log: one row per run per stage. A row per served
    // request would break that key and would grow the operational record by
    // something that is not an operation.
    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $started_at, 'started',
            0, 0, 0, '0', $detail);
    ";

    SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={databaseFile}");
        connection.Open();

        return connection;
    }

    public async Task<IReadOnlyList<BarRow>> BarsAsync(string ticker, DateOnly from, DateOnly to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = BarsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var bars = new List<BarRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            bars.Add(new BarRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                Money.FromStorage(reader.GetString(5)),
                reader.GetInt64(6)));
        }

        return bars;
    }

    public async Task<IReadOnlyList<IndicatorRow>> IndicatorsAsync(string ticker, DateOnly from, DateOnly to)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$to", to.ToString("yyyy-MM-dd"));

        var rows = new List<IndicatorRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new IndicatorRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                await reader.IsDBNullAsync(3) ? null : reader.GetDouble(3),
                reader.GetInt32(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<LevelRow>> LevelsAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LevelsForName;
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<LevelRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LevelRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                reader.GetString(4),
                reader.GetInt32(5) == 1,
                reader.GetInt32(6),
                reader.GetInt32(7) == 1,
                reader.GetString(8)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<ProfileRow>> ProfileAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ProfileForName;
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<ProfileRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ProfileRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                reader.GetInt64(4),
                reader.GetDouble(5)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<MoveRow>> MovesAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = MovesForName;
        command.Parameters.AddWithValue("$ticker", ticker);

        var rows = new List<MoveRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new MoveRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(2),
                reader.GetDouble(3),
                reader.GetInt32(4)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<UniverseRow>> UniverseAsync(string indexCode)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Universe;
        command.Parameters.AddWithValue("$index_code", indexCode);
        command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);

        var rows = new List<UniverseRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new UniverseRow(
                reader.GetString(0),
                reader.IsDBNull(1) ? null : reader.GetString(1),
                reader.IsDBNull(2) ? null : Money.FromStorage(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : Money.FromStorage(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                reader.IsDBNull(6) ? null : reader.GetDouble(6)));
        }

        return rows;
    }

    public async Task<CalendarRow?> NextEventAsync(string ticker, DateOnly onOrAfter)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NextEventForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$on_or_after", onOrAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new CalendarRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4))
            : null;
    }

    public async Task<LadderRow?> LadderAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LadderForName;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new LadderRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetString(3))
            : null;
    }

    // The operational record of the read surface coming up, which section
    // 15.10's run page reads. Appended rather than updated, because the run log
    // has no updater declared and every component appends to it.
    public async Task RecordStartAsync(string runId, string detail)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync();
    }
}
