using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Core.Volume;
using EquityBrief.Data;
using EquityBrief.Data.Swings;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Levels;

public sealed record LevelOutcome(int NamesBanded, int NamesSkipped, int BandsWritten);

// The level builder. Reads swings, indicators, the volume profile and the bar
// store, and writes the bands figure 9.1 describes: collect the four candidate
// sources, merge what is closer than half a typical day's move, add the sessions
// that reached each band, assign roles, and score.
//
// It makes no request and calls no model.
// see: The nightly run is arithmetic only
//
// The arithmetic is in LevelSeries. What is here is the four reads, the money
// crossing the averages need, and the write.
// see: Code owns every number
public sealed class LevelBuilder : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Level, Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "levels";

    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    // The window, newest first, so the query can be bounded and the caller
    // reverses it. Reading the whole series and slicing in code would work and
    // would read sixty times more rows than it uses on a name with a year.
    const string WindowFor = @"
        SELECT session_date, high, low, close
        FROM (SELECT session_date, high, low, close FROM bar WHERE ticker = $ticker
              ORDER BY session_date DESC LIMIT $window)
        ORDER BY session_date;
    ";

    const string AveragesFor = @"
        SELECT name, value
        FROM indicator
        WHERE ticker = $ticker AND session_date = $as_of AND name IN ($names)
              AND value IS NOT NULL;
    ";

    const string ShelvesFor = @"
        SELECT band_low, band_high, share_of_period
        FROM volume_profile
        WHERE ticker = $ticker AND as_of = $as_of AND share_of_period >= $threshold
        ORDER BY band_low;
    ";

    const string Upsert = @"
        INSERT INTO level (
            ticker, as_of, low_edge, high_edge, role,
            immediate, strength, has_non_average_anchor, members)
        VALUES (
            $ticker, $as_of, $low_edge, $high_edge, $role,
            $immediate, $strength, $has_non_average_anchor, $members)
        ON CONFLICT (ticker, as_of, low_edge) DO UPDATE SET
            high_edge = excluded.high_edge,
            role = excluded.role,
            immediate = excluded.immediate,
            strength = excluded.strength,
            has_non_average_anchor = excluded.has_non_average_anchor,
            members = excluded.members;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    // The three averages that are levels. The momentum readings are not: an RSI
    // is not a price and has no place on a price axis, which is why this names
    // three of the ten rather than reading them all.
    static readonly string[] AverageNames = [IndicatorSeries.Sma20, IndicatorSeries.Sma50, IndicatorSeries.Sma200];

    // A shelf is a band at or above twice its even share of the period's volume,
    // which is section 17's threshold read against the band count.
    // see: The volume profile is twenty bands across the window's own range
    public static double ShelfThreshold => 2d / VolumeProfileSeries.Bands;

    readonly IClock clock;
    readonly string databaseFile;

    public LevelBuilder(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<LevelOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var tickers = await TickersAsync(connection, cancellation);

        var banded = 0;
        var skipped = 0;
        var bands = 0;

        foreach (var ticker in tickers)
        {
            var window = await WindowAsync(connection, ticker, cancellation);

            // The same population rule the profile has, and for the same reason:
            // the level window is sixty sessions and a band built over twenty
            // would be a band over a different period with nothing on the row to
            // say so.
            // see: A name with fewer than sixty sessions gets no volume profile
            if (window.Count < VolumeProfileSeries.Window)
            {
                skipped++;

                continue;
            }

            var asOf = window[^1].SessionDate;
            var levels = await LevelsAsync(connection, ticker, window, asOf, cancellation);

            if (levels.Count == 0)
            {
                skipped++;

                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            foreach (var level in levels)
            {
                await using var command = connection.CreateCommand();

                command.Transaction = (SqliteTransaction)transaction;
                command.CommandText = Upsert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd"));
                Money.Bind(command, "$low_edge", level.LowEdge);
                Money.Bind(command, "$high_edge", level.HighEdge);
                command.Parameters.AddWithValue("$role", level.Role);
                command.Parameters.AddWithValue("$immediate", level.Immediate ? 1 : 0);
                command.Parameters.AddWithValue("$strength", level.Strength);
                command.Parameters.AddWithValue("$has_non_average_anchor", level.HasNonAverageAnchor ? 1 : 0);
                command.Parameters.AddWithValue("$members", Serialised(level.Members));

                await command.ExecuteNonQueryAsync(cancellation);

                bands++;
            }

            await transaction.CommitAsync(cancellation);

            banded++;
        }

        await RecordAsync(connection, runId, startedAt, banded, skipped, bands, cancellation);

        return new LevelOutcome(banded, skipped, bands);
    }

    // The members, as SCHEMA's column describes them: each member's kind, price
    // and date. The price is written in the storage form rather than as a JSON
    // number, because a JSON number is a double and a price is not.
    static string Serialised(IReadOnlyList<LevelMember> members) =>
        JsonSerializer.Serialize(members.Select(member => new
        {
            kind = member.Kind,
            price = Money.ToStorage(member.Price),
            date = member.Date.ToString("yyyy-MM-dd"),
        }));

    async Task<IReadOnlyList<Level>> LevelsAsync(
        SqliteConnection connection,
        string ticker,
        IReadOnlyList<LevelBar> window,
        DateOnly asOf,
        CancellationToken cancellation)
    {
        var swings = Swings(connection, ticker, asOf, window[0].SessionDate);
        var averages = await AveragesAsync(connection, ticker, asOf, cancellation);
        var shelves = await ShelvesAsync(connection, ticker, asOf, cancellation);
        var typicalMove = await TypicalMoveAsync(connection, ticker, asOf, cancellation);

        // Without a typical day's move there is no merge distance, and a merge
        // distance guessed is a band set nobody can read. A name with fewer than
        // fifteen bars has no ATR, and this cannot be reached from a window of
        // sixty, but the absence is answered rather than assumed away.
        if (typicalMove is not { } move)
        {
            return [];
        }

        var candidates = new List<LevelMember>();

        candidates.AddRange(swings);
        candidates.AddRange(averages);
        candidates.AddRange(shelves);
        candidates.AddRange(LevelSeries.RetracementsBetween(
            Last(swings, SwingSeries.High),
            Last(swings, SwingSeries.Low)));

        return LevelSeries.For(window, candidates, window[^1].Close, move / 2, asOf);
    }

    // The window's most recent swing of one kind, or nothing.
    static (decimal Price, DateOnly Date)? Last(IReadOnlyList<LevelMember> swings, string direction)
    {
        var last = swings
            .Where(member => member.Kind == $"swing {direction}")
            .OrderBy(member => member.Date)
            .LastOrDefault();

        return last.Kind is null ? null : (last.Price, last.Date);
    }

    async Task<IReadOnlyList<string>> TickersAsync(SqliteConnection connection, CancellationToken cancellation)
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

    async Task<IReadOnlyList<LevelBar>> WindowAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = WindowFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$window", VolumeProfileSeries.Window);

        var bars = new List<LevelBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new LevelBar(
                Date(reader.GetString(0)),
                Money.FromStorage(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3))));
        }

        return bars;
    }

    // The swings, through the reader 3.2 built rather than through a second
    // query with the same WHERE clause in it.
    //
    // That matters more than it looks. The filter is on confirmed_on and not on
    // session_date, and a second copy of it here would be a second place the
    // rule lives with no test behind it: while the as-of date is the name's own
    // last session the filter can exclude nothing, because a swing exists only
    // where three sessions follow it, so a copy here would be a rule that is
    // correct, untested and invisible until something passes an earlier date.
    // Reading it through StoredSwings puts the rule where 3.2's test can fail on
    // it.
    static IReadOnlyList<LevelMember> Swings(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        DateOnly from) =>
    [
        .. StoredSwings.AsOf(connection, ticker, asOf)
            .Where(swing => swing.SessionDate >= from)
            .Select(swing => new LevelMember(
                MemberSource.Swing,
                $"swing {swing.Direction}",
                swing.Price,
                swing.SessionDate)),
    ];

    // The money crossing, and the one this component exists to make. A moving
    // average is stored REAL because it is a statistic about prices, and it is
    // drawn on the chart as a price, so the level it becomes is a decimal
    // rounded to the store's own precision.
    async Task<IReadOnlyList<LevelMember>> AveragesAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        var slots = AverageNames.Select((_, index) => $"$name{index}").ToArray();

        command.CommandText = AveragesFor.Replace("$names", string.Join(", ", slots), StringComparison.Ordinal);
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd"));

        for (var index = 0; index < AverageNames.Length; index++)
        {
            command.Parameters.AddWithValue(slots[index], AverageNames[index]);
        }

        var averages = new List<LevelMember>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            averages.Add(new LevelMember(
                MemberSource.Average,
                reader.GetString(0),
                Statistic.ToPrice(reader.GetDouble(1)),
                asOf));
        }

        return averages;
    }

    // A shelf enters the merge as one price, the midpoint of its band.
    // see: A volume shelf enters the merge as one price, the midpoint of its band
    async Task<IReadOnlyList<LevelMember>> ShelvesAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ShelvesFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$threshold", ShelfThreshold);

        var shelves = new List<LevelMember>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var low = Money.FromStorage(reader.GetString(0));
            var high = Money.FromStorage(reader.GetString(1));

            shelves.Add(new LevelMember(
                MemberSource.Shelf,
                "shelf",
                PriceForm.Round((low + high) / 2, Statistic.Places),
                asOf));
        }

        return shelves;
    }

    // A typical day's move, which is the ATR at the as-of session. Half of it is
    // the merge distance.
    async Task<decimal?> TypicalMoveAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText =
            "SELECT value FROM indicator WHERE ticker = $ticker AND session_date = $as_of " +
            "AND name = $name AND value IS NOT NULL;";
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd"));
        command.Parameters.AddWithValue("$name", IndicatorSeries.Atr14);

        var value = await command.ExecuteScalarAsync(cancellation);

        return value is null or DBNull ? null : Statistic.ToPrice((double)value);
    }

    static DateOnly Date(string stored) =>
        DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int banded,
        int skipped,
        int bands,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", bands);

        command.Parameters.AddWithValue(
            "$detail",
            $"{banded} name(s) banded, {skipped} skipped, {bands} band(s)");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
