using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

public sealed record FilterHistoryOutcome(string Version, IReadOnlyList<DateOnly> Replayed, IReadOnlyList<DateOnly> Unreadable, int RowsWritten);

// The swing filter's results for sessions before its first stored night, replayed and stored so a later
// night can read whether a trigger had fired on the sessions before it.
//
// Each session is replayed by the filter counts, from the bars, bands and plans the store holds as of that
// session, under the open version's settings, and every member's result is stored under the replayed
// version, which no clock, no scored setup and no page reads. A session that already holds results, or
// that the night can still run again, is refused whole, and nothing is written: a session's results are
// written by the night that drew it wherever a night can.
// see: The swing filter's results are replayed for the sessions before its first stored night, for the trigger's arrival alone
public sealed class FilterHistory : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string RunPrefix = "filter-history-";

    public const string Stage = "filter history";

    const string OpenVersion = "SELECT version, settings FROM filter_version WHERE closed_at IS NULL ORDER BY opened_at DESC LIMIT 1;";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string HeldSessions = "SELECT DISTINCT session_date FROM gate_result;";

    const string Insert = @"
        INSERT INTO gate_result (
            ticker, session_date, version, code, market, trend, setup, family, trigger_pass, trigger_event, trade,
            ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, swing_reward_to_risk,
            swing_stop_moves, exclusions, passed, rank, strength, band_strength, gates, shadow)
        VALUES (
            $ticker, $session_date, $version, $code, $market, $trend, $setup, $family, $trigger_pass, $trigger_event, $trade,
            $ladder_reward_to_risk, $ladder_stop_moves, $swing_entry, $swing_stop, $swing_target, $swing_reward_to_risk,
            $swing_stop_moves, $exclusions, $passed, NULL, $strength, $band_strength, $gates, NULL);
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

    public FilterHistory(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public static string RunIdAt(DateTimeOffset at) => FormattableString.Invariant($"{RunPrefix}{at:yyyyMMddTHHmmss.fffffffZ}");

    // The verb a person runs: `filter-history --from <yyyy-MM-dd> --through <yyyy-MM-dd>`.
    public static async Task<int> RunAsync(string[] args, IClock clock, string databaseFile, TextWriter output, TextWriter error)
    {
        DateOnly? Day(string name) =>
            VerbArguments.Value(args, name) is { } given
                && DateOnly.TryParseExact(given, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
                ? day
                : null;

        if (Day("--from") is not { } from || Day("--through") is not { } through || through < from)
        {
            error.WriteLine("filter-history: name the sessions to replay with '--from <yyyy-MM-dd> --through <yyyy-MM-dd>', the first on or before the last.");

            return 2;
        }

        try
        {
            var outcome = await new FilterHistory(clock, databaseFile).ReplayAsync(VerbArguments.Value(args, "--index") ?? "GSPC", from, through, RunIdAt(clock.UtcNow));

            output.WriteLine(Said(outcome));

            return 0;
        }
        catch (InvalidOperationException refused)
        {
            error.WriteLine($"filter-history: {refused.Message}");

            return 1;
        }
    }

    public async Task<FilterHistoryOutcome> ReplayAsync(string indexCode, DateOnly from, DateOnly through, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var sessions = Enumerable.Range(0, through.DayNumber - from.DayNumber + 1)
            .Select(offset => from.AddDays(offset))
            .Where(ExchangeClosures.IsSession)
            .ToArray();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var (version, settings) = await VersionAsync(connection, cancellation)
            ?? throw new InvalidOperationException("no filter version is open, so there are no settings to replay the sessions under.");

        var newest = await ScalarAsync(connection, NewestSession, cancellation) is string stamp ? Date(stamp) : (DateOnly?)null;
        var held = await HeldAsync(connection, cancellation);

        if (sessions.Length == 0)
        {
            throw new InvalidOperationException(FormattableString.Invariant($"no exchange session falls between {from:yyyy-MM-dd} and {through:yyyy-MM-dd}."));
        }

        if (newest is not { } last || sessions.Any(session => session >= last))
        {
            throw new InvalidOperationException(FormattableString.Invariant($"a session on or after {newest:yyyy-MM-dd}, the newest the store holds, is the night's to draw: run the night again for it instead."));
        }

        if (sessions.Where(held.Contains).ToArray() is { Length: > 0 } drawn)
        {
            throw new InvalidOperationException(
                $"{string.Join(", ", drawn.Select(Stamp))} already hold the filter's results, which are not written over.");
        }

        // Replayed under the open version's settings alone, each session's results handed back as they are read.
        var replayed = new SortedDictionary<DateOnly, IReadOnlyList<GateResult>>();

        await new FilterCounts(databaseFile).CountAsync(
            indexCode,
            true,
            cancellation: cancellation,
            evaluated: (session, _, results) => replayed[session] = results,
            under: new Dictionary<string, FilterSettings>(StringComparer.Ordinal) { [version] = settings },
            only: sessions);

        var unreadable = sessions.Where(session => !replayed.ContainsKey(session)).ToArray();
        var rows = 0;

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            foreach (var (session, results) in replayed)
            {
                foreach (var result in results)
                {
                    await WriteAsync(connection, transaction, Stamp(session), result, cancellation);
                    rows++;
                }
            }

            var outcome = new FilterHistoryOutcome(version, [.. replayed.Keys], unreadable, rows);

            await using var log = connection.CreateCommand();

            log.Transaction = transaction;
            log.CommandText = AppendRun;
            log.Parameters.AddWithValue("$run_id", runId);
            log.Parameters.AddWithValue("$stage", Stage);
            log.Parameters.AddWithValue("$started_at", Instant(startedAt));
            log.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            log.Parameters.AddWithValue("$outcome", "ok");
            log.Parameters.AddWithValue("$rows_written", rows);
            log.Parameters.AddWithValue("$detail", Said(outcome));

            await log.ExecuteNonQueryAsync(cancellation);
            await transaction.CommitAsync(cancellation);

            return outcome;
        }
    }

    public static string Said(FilterHistoryOutcome outcome) =>
        $"{outcome.Replayed.Count} session(s) replayed under version {outcome.Version}'s settings, {outcome.RowsWritten} result(s) stored as {ReplayedResults.Version}"
        + (outcome.Replayed.Count > 0 ? $": {string.Join(", ", outcome.Replayed.Select(Stamp))}" : string.Empty)
        + (outcome.Unreadable.Count > 0 ? $"; not readable, so not stored: {string.Join(", ", outcome.Unreadable.Select(Stamp))}" : string.Empty);

    static async Task WriteAsync(SqliteConnection connection, SqliteTransaction transaction, string session, GateResult result, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = Insert;
        command.Parameters.AddWithValue("$ticker", result.Ticker);
        command.Parameters.AddWithValue("$session_date", session);
        command.Parameters.AddWithValue("$version", ReplayedResults.Version);
        command.Parameters.AddWithValue("$code", SwingFilter.CodeVersion);
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
        command.Parameters.AddWithValue("$strength", Nullable(result.Strength));
        command.Parameters.AddWithValue("$band_strength", result.BandStrength is { } strength ? strength : DBNull.Value);
        command.Parameters.AddWithValue("$gates", SwingFilter.GatesJson(result));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<(string Version, FilterSettings Settings)?> VersionAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = OpenVersion;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) ? (reader.GetString(0), FilterSettings.Read(reader.GetString(1))) : null;
    }

    static async Task<IReadOnlySet<DateOnly>> HeldAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = HeldSessions;

        var held = new HashSet<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held.Add(Date(reader.GetString(0)));
        }

        return held;
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string query, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = query;

        return await command.ExecuteScalarAsync(cancellation);
    }

    static int Flag(bool value) => value ? 1 : 0;

    static object Nullable(double? value) => value is { } present ? present : DBNull.Value;

    static object Ratio(decimal? value) => value is { } present ? Statistic.FromRatio(present) : DBNull.Value;

    static object Price(decimal? value) => value is { } present ? Money.ToStorage(present) : DBNull.Value;

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
