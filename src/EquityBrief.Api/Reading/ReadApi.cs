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

// One listing row, as the store holds it.
//
// `Reasons` and `PlanAtListing` arrive as the JSON the builder wrote, because
// the read surface hands back stored values unchanged and parsing one into a
// shape would be deriving.
public sealed record ListingRow(
    string Ticker,
    DateOnly SessionDate,
    string Reasons,
    int FiredCount,
    string PlanAtListing);

// One stage of one run, as the run log holds it.
//
// The two instants arrive parsed rather than as text, because the only thing
// this row is read for is a page that states how long each stage took, and a
// surface that parsed them would be the second place the log's time format is
// stated.
public sealed record RunStageRow(
    string RunId,
    string Stage,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Outcome,
    int RowsWritten,
    int ModelCalls,
    int NetworkRequests,
    string Spend,
    string Detail);

// One filled forward return, as the store holds it.
//
// `Outcome` is null while the horizon has not matured, and it is null rather
// than a word for it, because an unresolved setup is never a win and a word
// would put it in the same column as one. `BaseRate` sits on the row rather
// than beside the horizon, which is the grain the store already chose so the
// row a page reads carries the figure it must be read against.
// see: An unresolved setup is never a win
// see: Every forward-return figure is shown against the universe base rate
public sealed record ForwardReturnRow(
    string Ticker,
    DateOnly SessionDate,
    string Horizon,
    string? Outcome,
    DateOnly? ResolvedOn,
    double? ReturnPct,
    double? BaseRate);

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

    // The newest night the listings hold, so the front page resolves to it
    // without a date being asked for.
    const string NewestNight = "SELECT MAX(session_date) FROM listing;";

    // Every listing for one night, fired and quiet alike, because the page's own
    // header states the true fired count over the whole index and the twenty
    // drawn rows cannot tell you it.
    const string ListingsForNight = @"
        SELECT ticker, session_date, reasons, fired_count, plan_at_listing
        FROM listing
        WHERE session_date = $session_date
        ORDER BY ticker;
    ";

    // Every listing the store holds, which is what a reason's record is counted
    // over.
    //
    // Every night rather than tonight, because a record is a property of the
    // reason across every name it ever fired for, and a record over one evening
    // would be a statement about that evening wearing the clothes of a verdict.
    // It is a scan of the table, and it is one scan an evening on a page nobody
    // reloads: the reasons are JSON on the row, so a count per reason cannot be
    // asked of the store.
    const string EveryListing = @"
        SELECT ticker, session_date, reasons, fired_count, plan_at_listing
        FROM listing
        ORDER BY session_date, ticker;
    ";

    // A name's own listing history, which is what the universe screen's two
    // right-hand columns count and what the listing strip draws.
    const string ListingsForName = @"
        SELECT ticker, session_date, reasons, fired_count, plan_at_listing
        FROM listing
        WHERE ticker = $ticker
        ORDER BY session_date DESC
        LIMIT $sessions;
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

    // Every stage of every run that started inside a window of UTC days, which
    // is what the run page's operational header draws.
    //
    // The window is wide and the night is decided afterwards, by the clock. The
    // log has no session column, and it cannot have one it would agree with:
    // the run starts after the close in New York, so the UTC date it carries is
    // the session's on some evenings and the next day's on others, and which it
    // is depends on the offset that evening. The clock is the one thing allowed
    // to answer that question, and it answers it here rather than in SQL.
    // see: Nothing is written against one operating system
    const string RunLogInWindow = @"
        SELECT run_id, stage, started_at, ended_at, outcome,
               rows_written, model_calls, network_requests, spend, detail
        FROM run_log
        WHERE started_at >= $from AND started_at < $to
        ORDER BY started_at, stage;
    ";

    // Every filled forward return, which is what the run page's reason records
    // count over and where the base rate is read from.
    const string ForwardReturns = @"
        SELECT ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate
        FROM forward_return
        ORDER BY session_date, ticker, horizon;
    ";

    // The current members whose stored series does not end on the newest session
    // anyone has, which is the stale region's population. The same query the
    // night's closing stage counts over, returning the names rather than the
    // count, because a page that says four names are stale and does not say
    // which is a page nobody can act on.
    const string StaleNames = @"
        SELECT m.ticker
        FROM membership m
        WHERE m.index_code = $index AND m.""left"" IS NULL
          AND IFNULL((SELECT MAX(b.session_date) FROM bar b WHERE b.ticker = m.ticker), '')
              < (SELECT MAX(session_date) FROM bar)
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

    // Every stage of the runs that belong to one night, in the order they ran.
    //
    // The night is decided by the clock over each row's own start, for the
    // reason the query states: a run that starts at half past eight in New York
    // carries tomorrow's UTC date and belongs to tonight. The window handed to
    // SQL is a day either side, so the filter has something to filter and the
    // whole log is not read to draw one evening.
    public async Task<IReadOnlyList<RunStageRow>> RunLogAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = RunLogInWindow;
        command.Parameters.AddWithValue("$from", night.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$to", night.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<RunStageRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var started = DateTimeOffset.Parse(reader.GetString(2), CultureInfo.InvariantCulture);

            if (clock.SessionDateAt(started) != night)
            {
                continue;
            }

            rows.Add(new RunStageRow(
                reader.GetString(0),
                reader.GetString(1),
                started,
                DateTimeOffset.Parse(reader.GetString(3), CultureInfo.InvariantCulture),
                reader.GetString(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? string.Empty : reader.GetString(9)));
        }

        return rows;
    }

    // How long the night took, read from the run log rather than measured here.
    // A night the log does not carry says so rather than showing nothing, which
    // is the same rule the fact strip follows for a date not on file.
    //
    // The night is what selects the rows, which it did not until 5.6. The query
    // took every run carrying a listings stage and spanned the lot, so the date
    // in the parameter changed nothing and two stored nights reported one
    // duration covering both. The span is over the run that wrote that night's
    // list, so a re-run of an earlier evening is its own duration rather than a
    // widening of tonight's.
    public async Task<string?> NightDurationAsync(DateOnly night)
    {
        var rows = await RunLogAsync(night);

        var runs = rows
            .Where(row => row.Stage == "listings")
            .Select(row => row.RunId)
            .ToHashSet(StringComparer.Ordinal);

        var stages = rows.Where(row => runs.Contains(row.RunId)).ToArray();

        if (stages.Length == 0)
        {
            return null;
        }

        var started = stages.Min(stage => stage.StartedAt);
        var ended = stages.Max(stage => stage.EndedAt);

        return (ended - started).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture);
    }

    public async Task<IReadOnlyList<ForwardReturnRow>> ForwardReturnsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ForwardReturns;

        var rows = new List<ForwardReturnRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ForwardReturnRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4)
                    ? null
                    : DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<string>> StaleNamesAsync(string indexCode)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = StaleNames;
        command.Parameters.AddWithValue("$index", indexCode);

        var names = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async Task<DateOnly?> NewestNightAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestNight;

        return await command.ExecuteScalarAsync() is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    public async Task<IReadOnlyList<ListingRow>> ListingsAsync(DateOnly sessionDate)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ListingsForNight;
        command.Parameters.AddWithValue("$session_date", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await ListingsAsync(command);
    }

    public async Task<IReadOnlyList<ListingRow>> ListingsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = EveryListing;

        return await ListingsAsync(command);
    }

    public async Task<IReadOnlyList<ListingRow>> ListingsAsync(string ticker, int sessions)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ListingsForName;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$sessions", sessions);

        return await ListingsAsync(command);
    }

    static async Task<IReadOnlyList<ListingRow>> ListingsAsync(SqliteCommand command)
    {
        var rows = new List<ListingRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ListingRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetString(4)));
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
