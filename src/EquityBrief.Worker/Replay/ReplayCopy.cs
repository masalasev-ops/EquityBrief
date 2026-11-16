using System.Globalization;
using System.Reflection;
using EquityBrief.Core.Providers;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Replay;

// A copy of the store the replay runs nights over. Prepared once for the first night replayed: every table a deciding step
// reads and never writes is renamed and read through a view keeping its rows at or before the ceiling, a session or the
// instant the night that session ran ended; and every table a deciding step writes is stood as it stood on the evening before
// the first night replayed, by the steps' own undo of a night run again applied to every night from it. A store holding the
// replay's ceiling table is a copy; the operator's store holds none, and the preparation refuses any store whose folder holds
// no mark the copying left beside it.
// see: The replay compares the old build and the new over two copies of the store capped night by night, and a moved live rule keeps its record only where every trade is the same
public static class ReplayCopy
{
    public const string CeilingTable = "replay_ceiling";

    // The file the copying writes beside a copy, without which nothing here writes to a store.
    public const string CopyMark = "replay.copy";

    // A table a deciding step reads and never writes, and the column its rows are capped on: a session or date compared with
    // the ceiling's session, or an instant compared with the instant the ceiling's night ended.
    public sealed record Capped(string Table, string Column, bool Instant);

    // From the deciding components' own declarations: each table they read and never write that holds rows dated after a
    // night. The rest they read and never write hold history before the store's year, settings or registrations read at
    // the night's own instant, or a state per name the steps read by its dates, and are read whole.
    public static IReadOnlyList<Capped> Caps { get; } =
    [
        new("bar", "session_date", false),
        new("kept_bar", "session_date", false),
        new("market_bar", "session_date", false),
        new("indicator", "session_date", false),
        new("level", "as_of", false),
        new("ladder", "as_of", false),
        new("swing", "session_date", false),
        new("swing_reading", "session_date", false),
        new("listing", "session_date", false),
        new("member_reading", "session_date", false),
        new("market_reading", "session_date", false),
        new("switch_reading", "session_date", false),
        new("session_mark", "session_date", false),
        new("data_check", "session_date", false),
        new("fundamental_reading", "session_date", false),
        new("fundamental_score", "session_date", false),
        new("estimate_trend_night", "session_date", false),
        new("earnings_reaction", "reaction_session", false),
        new("filed_fact", "filed", false),
        new("insider_trade", "filed", false),
        new("calendar", "observed_at", true),
        new("reported_quarter", "fetched_at", true),
        new("release_document", "accepted_at", true),
        new("release_reading", "read_at", true),
        new("analyst_view", "answered_at", true),
        new("estimate_trend", "fetched_at", true),
        new("insider_read", "read_at", true),
        new("short_interest", "observed_at", true),
    ];

    // The steps' own undo of a night run again, each statement over every night from the first replayed, $first that night:
    // the nights' rows gone, each holding and trade bought from it gone, and each ended, half sold or given its exit from it
    // open again as before. Figures carried night by night, a book's growth and the session it was carried to, stand as the
    // newest night left them in both copies alike.
    public static IReadOnlyList<string> Rewind { get; } =
    [
        "DELETE FROM gate_result WHERE session_date >= $first;",
        "DELETE FROM list_rule WHERE session_date >= $first;",
        "DELETE FROM family_result WHERE session_date >= $first;",
        "DELETE FROM family_pick WHERE session_date >= $first;",
        "DELETE FROM family_night WHERE session_date >= $first;",
        "DELETE FROM family_trade WHERE session_date >= $first;",
        "UPDATE family_trade SET ended_on = NULL, result = NULL, benchmark = NULL, members = NULL WHERE ended_on >= $first;",
        "DELETE FROM heavyweight_night WHERE session_date >= $first;",
        "DELETE FROM heavyweight_holding WHERE entered_on >= $first;",
        "UPDATE heavyweight_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL WHERE ended_on >= $first;",
        "UPDATE heavyweight_holding SET half_sold_on = NULL WHERE half_sold_on >= $first;",
        "UPDATE heavyweight_holding SET stop = NULL, take_profit = NULL, take_profit_from = NULL, exit_added_on = NULL WHERE exit_added_on >= $first;",
        "DELETE FROM heavyweight_rule_night WHERE session_date >= $first;",
        "DELETE FROM heavyweight_rule_holding WHERE entered_on >= $first;",
        "UPDATE heavyweight_rule_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL WHERE ended_on >= $first;",
        "UPDATE heavyweight_rule_holding SET half_sold_on = NULL WHERE half_sold_on >= $first;",
        "UPDATE heavyweight_holding SET through = $before WHERE through >= $first;",
        "UPDATE heavyweight_rule_holding SET through = $before WHERE through >= $first;",
        "DELETE FROM index_family_night WHERE session_date >= $first;",
        "DELETE FROM index_family_result WHERE session_date >= $first;",
        "DELETE FROM index_family_pick WHERE session_date >= $first;",
        "DELETE FROM index_family_trade WHERE session_date >= $first;",
        "UPDATE index_family_trade SET ended_on = NULL, result = NULL, cost = NULL, benchmark = NULL, members = NULL WHERE ended_on >= $first;",
        "DELETE FROM index_rule_trade WHERE session_date >= $first;",
        "UPDATE index_rule_trade SET ended_on = NULL, result = NULL, cost = NULL, benchmark = NULL, members = NULL WHERE ended_on >= $first;",
        "DELETE FROM index_heavyweight_holding WHERE entered_on >= $first;",
        "UPDATE index_heavyweight_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL, cost = NULL WHERE ended_on >= $first;",
        "UPDATE index_heavyweight_holding SET stop = NULL, take_profit = NULL, take_profit_from = NULL, exit_added_on = NULL WHERE exit_added_on >= $first;",
        "UPDATE index_heavyweight_holding SET through = $before WHERE through >= $first;",
        "DELETE FROM index_heavyweight_rule_holding WHERE entered_on >= $first;",
        "UPDATE index_heavyweight_rule_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL, cost = NULL WHERE ended_on >= $first;",
        "UPDATE index_heavyweight_rule_holding SET through = $before WHERE through >= $first;",
        "DELETE FROM index_heavyweight_rule_night WHERE session_date >= $first;",
        "DELETE FROM dip_night WHERE session_date >= $first;",
        "DELETE FROM dip_result WHERE session_date >= $first;",
        "DELETE FROM dip_pick WHERE session_date >= $first;",
        "DELETE FROM dip_trade WHERE session_date >= $first;",
        "UPDATE dip_trade SET ended_on = NULL, sale = NULL, end_reason = 'open', sessions = NULL, result = NULL, cost = NULL WHERE ended_on >= $first;",
        "UPDATE dip_trade SET bought_on = NULL, fill = NULL, past = NULL WHERE bought_on >= $first;",
        "DELETE FROM rule_night WHERE session_date >= $first AND source = 'night';",
        "DELETE FROM rule_pick WHERE session_date >= $first;",
        "UPDATE rule_pick SET ended_on = NULL, result = NULL WHERE ended_on >= $first;",
        "DELETE FROM forming_row WHERE session_date >= $first;",
        "DELETE FROM quality_night WHERE session_date >= $first;",
        "DELETE FROM quality_result WHERE session_date >= $first;",
        "DELETE FROM quality_holding WHERE chosen_on >= $first;",
        "UPDATE quality_holding SET ended_on = NULL, sale = NULL, end_reason = 'open', result = NULL, benchmark = NULL, cost = NULL, edge = NULL WHERE ended_on >= $first;",
        "UPDATE quality_holding SET half_sold_on = NULL WHERE half_sold_on >= $first;",
        "UPDATE quality_holding SET stop = NULL, take_profit = NULL, take_profit_from = NULL, exit_added_on = NULL WHERE exit_added_on >= $first;",
        "UPDATE quality_holding SET through = $before WHERE through >= $first;",
        "DELETE FROM improving_night WHERE session_date >= $first;",
        "DELETE FROM improving_result WHERE session_date >= $first;",
        "DELETE FROM improving_trade WHERE chosen_on >= $first;",
        "UPDATE improving_trade SET ended_on = NULL, sale = NULL, end_reason = 'open', result = NULL, benchmark = NULL, cost = NULL, edge = NULL WHERE ended_on >= $first;",
        "UPDATE improving_trade SET half_sold_on = NULL WHERE half_sold_on >= $first;",
        "UPDATE improving_trade SET stop = NULL, take_profit = NULL, take_profit_from = NULL, exit_added_on = NULL WHERE exit_added_on >= $first;",
        "UPDATE improving_trade SET through = $before WHERE through >= $first;",
        "DELETE FROM setup WHERE session_date >= $first;",
        "DELETE FROM setup_night WHERE session_date >= $first;",
        "DELETE FROM ledger_reading WHERE session_date >= $first;",
        "DELETE FROM forward_return WHERE session_date >= $first;",
        "DELETE FROM trade_fill WHERE session_date >= $first;",
        "DELETE FROM version_score WHERE session_date >= $first;",
    ];

    // The session the copy is capped at, or none where the store is not a replay's copy.
    public static DateOnly? CeilingOf(string databaseFile)
    {
        if (!File.Exists(databaseFile))
        {
            return null;
        }

        using var connection = new SqliteConnection(StoreConnection.For(databaseFile) + ";Mode=ReadOnly");
        connection.Open();

        using var command = connection.CreateCommand();

        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", CeilingTable);

        if (command.ExecuteScalar() is null)
        {
            return null;
        }

        command.CommandText = "SELECT session_date FROM " + CeilingTable + ";";
        command.Parameters.Clear();

        return command.ExecuteScalar() is string session
            ? DateOnly.ParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    // Whether the store's folder carries the copying's mark.
    public static bool IsACopy(string databaseFile) =>
        Path.GetDirectoryName(Path.GetFullPath(databaseFile)) is { } folder && File.Exists(Path.Combine(folder, CopyMark));

    // The copy prepared for its first replayed night: the caps put in place over every table a deciding step reads and never
    // writes, the ceiling at the night before the first, and the deciding steps' own tables stood as they stood that evening.
    // A store already prepared is refused, as is one carrying no copy's mark.
    public static async Task<string> PrepareAsync(string databaseFile, DateOnly first, CancellationToken cancellation = default)
    {
        if (!IsACopy(databaseFile))
        {
            throw new InvalidOperationException($"'{databaseFile}' carries no '{CopyMark}' beside it, so it is not a replay's copy and nothing was written.");
        }

        if (CeilingOf(databaseFile) is not null)
        {
            throw new InvalidOperationException($"'{databaseFile}' is already prepared.");
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        async Task<object?> ScalarAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            return await command.ExecuteScalarAsync(cancellation);
        }

        var stamp = Stamp(first);
        var before = await ScalarAsync("SELECT MAX(session_date) FROM bar WHERE session_date < $first;", ("$first", stamp)) as string
            ?? throw new InvalidOperationException($"the copy holds no session before {stamp}, so there is no evening to stand it at.");
        var rows = 0;

        foreach (var statement in Rewind)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = statement;
            command.Parameters.AddWithValue("$first", stamp);

            if (statement.Contains("$before", StringComparison.Ordinal))
            {
                command.Parameters.AddWithValue("$before", before);
            }

            rows += await command.ExecuteNonQueryAsync(cancellation);
        }

        await ScalarAsync("CREATE TABLE " + CeilingTable + " (session_date TEXT NOT NULL, instant TEXT NOT NULL);");
        await ScalarAsync("INSERT INTO " + CeilingTable + " (session_date, instant) VALUES ($session, $instant);", ("$session", before), ("$instant", await NightEndedAsync(connection, transaction, before, cancellation)));

        foreach (var cap in Caps)
        {
            var bound = cap.Instant ? "instant" : "session_date";

            await ScalarAsync($"ALTER TABLE {cap.Table} RENAME TO {cap.Table}_all;");
            await ScalarAsync($"CREATE VIEW {cap.Table} AS SELECT * FROM {cap.Table}_all WHERE {cap.Column} <= (SELECT {bound} FROM {CeilingTable});");
        }

        await transaction.CommitAsync(cancellation);

        return FormattableString.Invariant($"prepared for {stamp}: {rows} row(s) of the deciding steps' tables stood as on the evening of {before}, {Caps.Count} table(s) capped");
    }

    // The new build's copy alone: each open ladder window's hash and code version written as this build applies its
    // parameters, so the night replayed under the new build scores the windows the night under the old scored. It is here
    // and not in the scorer, whose source the ladder rules' code version pins.
    public static async Task<int> HashWindowsAsTheCodeIsAsync(string databaseFile, CancellationToken cancellation = default)
    {
        if (!IsACopy(databaseFile))
        {
            throw new InvalidOperationException($"'{databaseFile}' carries no '{CopyMark}' beside it, so it is not a replay's copy and no window was moved.");
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        var open = new List<(string Rule, string Version, string Parameters, string OpenedAt)>();

        await using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT rule, version, parameters, opened_at FROM rule_version WHERE closed_at IS NULL;";

            await using var reader = await read.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                open.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        var code = EquityBrief.Worker.Rules.RuleVersionScorer.CodeVersion;

        foreach (var (rule, version, parameters, openedAt) in open)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = "UPDATE rule_version SET parameters_hash = $parameters_hash, code_version = $code_version WHERE rule = $rule AND version = $version AND opened_at = $opened_at;";
            command.Parameters.AddWithValue("$parameters_hash", EquityBrief.Core.Rules.RuleVersions.Hash(EquityBrief.Core.Rules.RuleVersions.Read(parameters), code));
            command.Parameters.AddWithValue("$code_version", code);
            command.Parameters.AddWithValue("$rule", rule);
            command.Parameters.AddWithValue("$version", version);
            command.Parameters.AddWithValue("$opened_at", openedAt);
            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return open.Count;
    }

    // The ceiling moved to a night: its session and the instant that night's run ended.
    public static async Task CeilAsync(string databaseFile, DateOnly night, CancellationToken cancellation = default)
    {
        if (!IsACopy(databaseFile) || CeilingOf(databaseFile) is null)
        {
            throw new InvalidOperationException($"'{databaseFile}' is not a prepared replay copy, so no ceiling was moved.");
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "UPDATE " + CeilingTable + " SET session_date = $session, instant = $instant;";
        command.Parameters.AddWithValue("$session", Stamp(night));
        command.Parameters.AddWithValue("$instant", await NightEndedAsync(connection, transaction, Stamp(night), cancellation));
        await command.ExecuteNonQueryAsync(cancellation);
        await transaction.CommitAsync(cancellation);
    }

    // The instant the night of a session ended, read off the run log: the latest end of a night's run started that evening,
    // New York's, or the next morning in UTC; six in the morning UTC after it where the log holds none.
    static async Task<string> NightEndedAsync(SqliteConnection connection, SqliteTransaction transaction, string session, CancellationToken cancellation)
    {
        var day = DateOnly.ParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var next = Stamp(day.AddDays(1));

        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = "SELECT MAX(ended_at) FROM run_log WHERE run_id LIKE 'night-%' AND started_at >= $from AND started_at < $to;";
        command.Parameters.AddWithValue("$from", session + "T20:00:00Z");
        command.Parameters.AddWithValue("$to", next + "T18:00:00Z");

        return await command.ExecuteScalarAsync(cancellation) as string ?? next + "T06:00:00Z";
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// The feeds a replayed night is handed: each counts no request and refuses every ask, so a deciding step that asked a
// feed would stop the replay rather than reach a network.
public class ReplayFeed : DispatchProxy
{
    public const string Refusal = "a replayed night asks nothing of a feed";

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
        targetMethod?.Name switch
        {
            "get_Requests" or "get_Symbols" => 0,
            "get_Indices" => EquityBrief.Worker.Indices.IndexFamilies.Indices,
            _ => throw new InvalidOperationException($"{Refusal}: {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}"),
        };

    static T One<T>() where T : class => Create<T, ReplayFeed>();

    public static NightFeeds None() =>
        new(One<IIndexMembershipFeed>(), One<IHistoricalBarFeed>(), One<IBulkPriceFeed>(), One<ICorporateActionFeed>(), One<IEarningsCalendarFeed>(), One<INewsFeed>(), One<IFundamentalsFeed>(), One<IMarketSeriesFeed>())
        {
            Funds = One<IFundHoldingsFeed>(),
            Dividends = One<IDividendCalendarFeed>(),
        };
}
