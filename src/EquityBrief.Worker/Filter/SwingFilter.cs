using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

public sealed record SwingFilterOutcome(int Members, int RowsWritten, IReadOnlyList<int> Funnel, int Excluded, int Passing, string Version, int Evaluated = 0, IReadOnlyList<string>? Faults = null);

// The swing filter. Evaluates every member of the index on the night through the five gates, the
// trigger and the exclusions, ranks the names passing, and stores every answer, so what each gate
// removed can be counted, and a gate's near misses read later from rows nothing rewrites.
//
// It runs after the listings, so the ladder's first tranche it reads is the one tonight's listing
// kept, and it changes nothing the listings wrote. It makes no request and calls no model. Each
// standing swing family candidate is evaluated in its shadow over the same inputs by the shadow the
// night hands in, and stored on the member's row, a missing or moved evaluator failing the stage as it
// fails the listings.
// see: The nightly run is arithmetic only
// see: The swing filter's starting settings are ruled from shape counts before tonight's list switches to it
public sealed class SwingFilter : IComponent
{
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.SeriesState, Touch.Read),
            new StoreTouch(Store.SwingReading, Touch.Read),
            new StoreTouch(Store.MarketReading, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "swing-filter";

    // What a row's version says where no filter version is open and it ran on section 17's values.
    public const string NoVersionOpen = "none";

    // The pin of every source the filter's answers run through, less the line below; a check derives
    // the list from the compiled code and holds this to it.
    public const string CodeVersionDeclaration = "public const string CodeVersion =";

    public const string CodeVersion = "9c34a8d191d1";

    public static IReadOnlyList<string> CodeVersionSources { get; } =
    [
        "src/EquityBrief.Core/Bars/ExchangeClosures.cs",
        "src/EquityBrief.Core/Bars/TradingCalendar.cs",
        "src/EquityBrief.Core/Filter/FilterSettings.cs",
        "src/EquityBrief.Core/Filter/SwingFunnel.cs",
        "src/EquityBrief.Core/Filter/SwingGates.cs",
        "src/EquityBrief.Core/Filter/SwingReadings.cs",
        "src/EquityBrief.Core/Ladders/LadderSeries.cs",
        "src/EquityBrief.Core/Levels/LevelSeries.cs",
        "src/EquityBrief.Core/Prices/Statistic.cs",
        "src/EquityBrief.Core/Time/IClock.cs",
        "src/EquityBrief.Data/Money.cs",
        "src/EquityBrief.Data/StoreConnection.cs",
        "src/EquityBrief.Worker/Bars/SeriesGapStop.cs",
        "src/EquityBrief.Worker/Filter/ListedTranche.cs",
        "src/EquityBrief.Worker/Filter/SwingFilter.cs",
        "src/EquityBrief.Worker/Filter/SwingReader.cs",
    ];

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string OpenVersion = @"
        SELECT version, settings
        FROM filter_version
        WHERE closed_at IS NULL
        ORDER BY opened_at DESC
        LIMIT 1;
    ";

    const string MarketOn = "SELECT members, counted, above, breadth FROM market_reading WHERE session_date = $session;";

    const string ReadingsOn = @"
        SELECT ticker, bars, return_short, return_long, strength, recent_high, high_session,
               pullback_sessions, depth, dry_up, tightness, note
        FROM swing_reading
        WHERE session_date = $session;
    ";

    // The last sessions each name holds, newest first: tonight's close and volume, the session before's
    // close and high, and as many before that as the arrival window reads.
    const string LastTwoBars = @"
        SELECT ticker, session_date, high, close, volume
        FROM (
            SELECT ticker, session_date, high, close, volume,
                   ROW_NUMBER() OVER (PARTITION BY ticker ORDER BY session_date DESC) AS back
            FROM bar)
        WHERE back <= $keep
        ORDER BY ticker, session_date DESC;
    ";

    const string IndicatorsOn = @"
        SELECT ticker, name, value
        FROM indicator
        WHERE session_date = $session AND name IN ($typical, $volume) AND value IS NOT NULL;
    ";

    const string LaddersOn = "SELECT ticker, trend_state FROM ladder WHERE as_of = $session;";

    const string BandsOn = @"
        SELECT ticker, low_edge, high_edge, role, strength, has_non_average_anchor
        FROM level
        WHERE as_of = $session;
    ";

    const string ListingsOn = "SELECT ticker, plan_at_listing FROM listing WHERE session_date = $session;";

    // The next print on file on or after the night, for every name at once.
    const string NextPrints = @"
        SELECT ticker, MIN(event_date)
        FROM calendar
        WHERE kind = 'earnings' AND event_date >= $session
        GROUP BY ticker;
    ";

    const string Suspects = "SELECT ticker FROM series_state WHERE state = 'suspect';";

    // The trigger events one session stored, one per name it evaluated, and none where it read none.
    const string EventsOn = "SELECT ticker, trigger_event FROM gate_result WHERE session_date = $session;";

    // A night run again replaces its own set whole; no other night's rows are touched, because a gate's
    // near misses are read over years.
    const string ClearTheNight = "DELETE FROM gate_result WHERE session_date = $session;";

    const string Insert = @"
        INSERT INTO gate_result (
            ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade,
            ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, swing_reward_to_risk,
            swing_stop_moves, exclusions, passed, rank, strength, band_strength, gates, shadow)
        VALUES (
            $ticker, $session_date, $version, $code, $market, $trend, $setup, $family, $trigger_pass, $trigger_event, $trade,
            $ladder_reward_to_risk, $ladder_stop_moves, $swing_entry, $swing_stop, $swing_target, $swing_reward_to_risk,
            $swing_stop_moves, $exclusions, $passed, $rank, $strength, $band_strength, $gates, $shadow);
    ";


    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public SwingFilter(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The family's shadow is the night's, read off the register as it stood when the night started, and
    // none where no night handed one.
    public async Task<SwingFilterOutcome> RunAsync(string indexCode, string runId, IMemberShadow? family = null, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The night is the newest session any name holds, read off the store for the reason the
        // listings stage reads it there: a replay run on a weekend has a clock a session ahead of every bar.
        var night = await NewestAsync(connection, cancellation) ?? clock.SessionDateAt(clock.UtcNow);
        var session = Stamp(night);

        var (version, settings) = await VersionAsync(connection, cancellation);
        var members = await MembersAsync(connection, indexCode, Stamp(clock.SessionDateAt(clock.UtcNow)), cancellation);
        var market = await MarketAsync(connection, session, cancellation);
        var readings = await ReadingsAsync(connection, session, night, cancellation);
        var bars = await LastTwoAsync(connection, Math.Max(2, Math.Max(settings.ArrivalSessions, family?.ArrivalReach ?? 0) + 1), cancellation);
        var indicators = await IndicatorsAsync(connection, session, cancellation);
        var trends = await TextsAsync(connection, LaddersOn, session, cancellation);
        var bands = await BandsAsync(connection, session, cancellation);
        var plans = await TextsAsync(connection, ListingsOn, session, cancellation);
        var prints = await TextsAsync(connection, NextPrints, session, cancellation);
        var suspects = await SuspectsAsync(connection, cancellation);

        // Each name's sessions before are its own, the ones it holds behind tonight's, and the events
        // stored for them are read once for each distinct session.
        var befores = new Dictionary<DateOnly, IReadOnlyDictionary<string, bool?>>();

        foreach (var before in bars.Values.Where(pair => pair.Count > 1 && pair[0].Session == night).SelectMany(pair => pair.Skip(1).Select(bar => bar.Session)).Distinct())
        {
            befores[before] = await EventsAsync(connection, Stamp(before), cancellation);
        }

        bool? FiredOn(string ticker, DateOnly session) =>
            befores.TryGetValue(session, out var stored) && stored.TryGetValue(ticker, out var happened) ? happened : null;

        var results = new List<GateResult>();
        var shadows = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var ticker in members)
        {
            var read = readings.GetValueOrDefault(ticker);
            var pair = bars.GetValueOrDefault(ticker) ?? [];
            var tonight = pair.Count > 0 && pair[0].Session == night ? pair[0] : null;
            var before = tonight is not null && pair.Count > 1 ? pair[1] : null;
            var next = prints.TryGetValue(ticker, out var dated) ? Date(dated) : (DateOnly?)null;

            bool? fired = before is not null ? FiredOn(ticker, before.Session) : null;
            IReadOnlyList<SessionEvent> earlier = tonight is not null
                ? [.. pair.Skip(2).Select(bar => new SessionEvent(bar.Session, FiredOn(ticker, bar.Session)))]
                : [];

            var inputs = new GateInputs(
                    ticker,
                    market,
                    trends.GetValueOrDefault(ticker),
                    read?.Strength,
                    read?.Reading,
                    read?.Note ?? (read is null ? "no swing readings are stored for the night" : null),
                    tonight?.Close,
                    before?.Close,
                    before?.High,
                    tonight?.Volume,
                    Indicator(indicators, ticker, IndicatorSeries.VolAvg50),
                    Indicator(indicators, ticker, IndicatorSeries.Atr14),
                    bands.GetValueOrDefault(ticker) ?? [],
                    plans.TryGetValue(ticker, out var plan) ? ListedTranche.Of(plan) : null,
                    next,
                    next is { } date ? ExchangeClosures.SessionsUntil(night, date) : null,
                    suspects.Contains(ticker),
                    SwingReader.GapIn(read?.Note),
                    before?.Session,
                    fired,
                    earlier);

            results.Add(SwingGates.Evaluate(inputs, settings));

            // A member with no bar for the night, or one held across a gap, is skipped by every candidate
            // with the reason, as the listings stage skips it, rather than scored over nothing.
            if (family is not null)
            {
                shadows[ticker] = family.Evaluate(inputs, tonight is null, SwingReader.GapIn(read?.Note));
            }
        }

        var ranked = SwingGates.Ranked(results, settings);
        var rank = ranked
            .Select((result, at) => (result.Ticker, At: at + 1))
            .ToDictionary(pair => pair.Ticker, pair => pair.At, StringComparer.Ordinal);
        var funnel = SwingFunnel.Of(results);
        var excluded = results.Count(result => result.Gates.All(gate => gate.Passed) && result.Exclusions.Count > 0);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = ClearTheNight;
            clear.Parameters.AddWithValue("$session", session);

            await clear.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var result in results)
        {
            await WriteAsync(connection, (SqliteTransaction)transaction, session, version, result, rank.TryGetValue(result.Ticker, out var at) ? at : null, shadows.GetValueOrDefault(result.Ticker), cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$rows_written", results.Count);
            command.Parameters.AddWithValue("$outcome", family is { Faults.Count: > 0 } ? "failed" : "ok");
            command.Parameters.AddWithValue("$detail", SwingFunnel.Line(funnel, excluded, ranked.Count, version) + (family?.Said ?? string.Empty));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return new SwingFilterOutcome(members.Count, results.Count, funnel, excluded, ranked.Count, version, family?.Evaluated ?? 0, family?.Faults ?? []);
    }

    // The five gates' answers, their reasons and values, and the notes, as the row stores them.
    public static string GatesJson(GateResult result) =>
        JsonSerializer.Serialize(new
        {
            gates = result.Gates.Select(gate => new { gate = gate.Name, passed = gate.Passed, reason = gate.Reason, values = gate.Values }),
            notes = result.Notes,
        });

    static async Task WriteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string session,
        string version,
        GateResult result,
        int? rank,
        string? shadow,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = Insert;
        command.Parameters.AddWithValue("$ticker", result.Ticker);
        command.Parameters.AddWithValue("$session_date", session);
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$code", CodeVersion);
        command.Parameters.AddWithValue("$market", Flag(result.Gates[0].Passed));
        command.Parameters.AddWithValue("$trend", Flag(result.Gates[1].Passed));
        command.Parameters.AddWithValue("$setup", Flag(result.Gates[2].Passed));
        command.Parameters.AddWithValue("$family", (object?)result.Family ?? DBNull.Value);
        command.Parameters.AddWithValue("$trigger_pass", Flag(result.Gates[3].Passed));
        command.Parameters.AddWithValue("$trigger_event", result.TriggerEvent is { } happened ? Flag(happened) : DBNull.Value);
        command.Parameters.AddWithValue("$trade", Flag(result.Gates[4].Passed));
        command.Parameters.AddWithValue("$ladder_reward_to_risk", Ratio(result.LadderTrade.RewardToRisk));
        command.Parameters.AddWithValue("$ladder_stop_moves", Nullable(result.LadderTrade.StopInMoves));
        command.Parameters.AddWithValue("$swing_entry", Price(result.SwingTrade.Entry));
        command.Parameters.AddWithValue("$swing_stop", Price(result.SwingTrade.Stop));
        command.Parameters.AddWithValue("$swing_target", Price(result.SwingTrade.Target));
        command.Parameters.AddWithValue("$swing_reward_to_risk", Ratio(result.SwingTrade.RewardToRisk));
        command.Parameters.AddWithValue("$swing_stop_moves", Nullable(result.SwingTrade.StopInMoves));
        command.Parameters.AddWithValue("$exclusions", JsonSerializer.Serialize(result.Exclusions));
        command.Parameters.AddWithValue("$passed", Flag(result.Passed));
        command.Parameters.AddWithValue("$rank", rank is { } at ? at : DBNull.Value);
        command.Parameters.AddWithValue("$strength", Nullable(result.Strength));
        command.Parameters.AddWithValue("$band_strength", result.BandStrength is { } strength ? strength : DBNull.Value);
        command.Parameters.AddWithValue("$gates", GatesJson(result));
        command.Parameters.AddWithValue("$shadow", shadow is null ? DBNull.Value : shadow);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static int Flag(bool value) => value ? 1 : 0;

    static object Nullable(double? value) => value is { } present ? present : DBNull.Value;

    static object Ratio(decimal? value) => value is { } present ? Statistic.FromRatio(present) : DBNull.Value;

    static object Price(decimal? value) => value is { } present ? Money.ToStorage(present) : DBNull.Value;

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static double? Indicator(IReadOnlyDictionary<(string, string), double> indicators, string ticker, string name) =>
        indicators.TryGetValue((ticker, name), out var value) ? value : null;

    sealed record Bar(DateOnly Session, decimal High, decimal Close, long Volume);

    sealed record Read(SwingReading? Reading, double? Strength, string? Note);

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
    }

    // The open version and its settings, or none and section 17's proposed values.
    static async Task<(string Version, FilterSettings Settings)> VersionAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = OpenVersion;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (reader.GetString(0), FilterSettings.Read(reader.GetString(1)))
            : (NoVersionOpen, FilterSettings.Proposed);
    }

    static async Task<IReadOnlyList<string>> MembersAsync(SqliteConnection connection, string indexCode, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session);

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<Breadth?> MarketAsync(SqliteConnection connection, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MarketOn;
        command.Parameters.AddWithValue("$session", session);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? new Breadth(reader.GetInt32(0), reader.GetInt32(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetDouble(3))
            : null;
    }

    static async Task<IReadOnlyDictionary<string, Read>> ReadingsAsync(SqliteConnection connection, string session, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReadingsOn;
        command.Parameters.AddWithValue("$session", session);

        var readings = new Dictionary<string, Read>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            double? Real(int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);

            var note = reader.IsDBNull(11) ? null : reader.GetString(11);

            readings[reader.GetString(0)] = new Read(
                note is null
                    ? new SwingReading(
                        night,
                        reader.GetInt32(1),
                        Real(2),
                        Real(3),
                        reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                        reader.IsDBNull(6) ? null : Date(reader.GetString(6)),
                        reader.IsDBNull(7) ? null : reader.GetInt32(7),
                        Real(8),
                        Real(9),
                        Real(10))
                    : null,
                Real(4),
                note);
        }

        return readings;
    }

    static async Task<IReadOnlyDictionary<string, IReadOnlyList<Bar>>> LastTwoAsync(SqliteConnection connection, int keep, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = LastTwoBars;
        command.Parameters.AddWithValue("$keep", keep);

        var bars = new Dictionary<string, List<Bar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!bars.TryGetValue(ticker, out var pair))
            {
                bars[ticker] = pair = [];
            }

            pair.Add(new Bar(Date(reader.GetString(1)), Money.FromStorage(reader.GetString(2)), Money.FromStorage(reader.GetString(3)), reader.GetInt64(4)));
        }

        return bars.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<Bar>)entry.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<(string, string), double>> IndicatorsAsync(SqliteConnection connection, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsOn;
        command.Parameters.AddWithValue("$session", session);
        command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);
        command.Parameters.AddWithValue("$volume", IndicatorSeries.VolAvg50);

        var values = new Dictionary<(string, string), double>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            values[(reader.GetString(0), reader.GetString(1))] = reader.GetDouble(2);
        }

        return values;
    }

    // One text per name off a query keyed on the night, the first of the row's two columns being the ticker.
    static async Task<IReadOnlyDictionary<string, string>> TextsAsync(SqliteConnection connection, string query, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = query;
        command.Parameters.AddWithValue("$session", session);

        var texts = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (!reader.IsDBNull(1))
            {
                texts[reader.GetString(0)] = reader.GetString(1);
            }
        }

        return texts;
    }

    // Tonight's bands for every name, with the role the level builder set against tonight's close.
    static async Task<IReadOnlyDictionary<string, IReadOnlyList<FilterBand>>> BandsAsync(SqliteConnection connection, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BandsOn;
        command.Parameters.AddWithValue("$session", session);

        var bands = new Dictionary<string, List<FilterBand>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!bands.TryGetValue(ticker, out var held))
            {
                bands[ticker] = held = [];
            }

            held.Add(new FilterBand(
                Money.FromStorage(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5) == 1));
        }

        return bands.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<FilterBand>)entry.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlySet<string>> SuspectsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = Suspects;

        var suspects = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            suspects.Add(reader.GetString(0));
        }

        return suspects;
    }

    static async Task<IReadOnlyDictionary<string, bool?>> EventsAsync(SqliteConnection connection, string session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = EventsOn;
        command.Parameters.AddWithValue("$session", session);

        var events = new Dictionary<string, bool?>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            events[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetInt32(1) == 1;
        }

        return events;
    }
}
