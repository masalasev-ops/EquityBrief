using System.Globalization;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Replay;

// What the replay found for one rule or book on one table: whether every row the old build's copy holds the new build's
// holds too and nothing more, and the first row that differs where one does.
public sealed record ReplayCompared(string Table, string Owner, int Old, int New, bool Same, string? First);

// The two copies' trades, holdings, waiting orders, fills and version scores compared row by row after the replay, each
// table's rows grouped by the rule or book that owns them. Every column is compared but the instants a row was written at,
// which differ between any two runs whatever the code; a waiting order is its trade's plan and is compared with it. Both
// copies are opened for reading alone.
// see: The replay compares the old build and the new over two copies of the store capped night by night, and a moved live rule keeps its record only where every trade is the same
public static class ReplayCompare
{
    // Each table compared and the columns naming the rule or book its rows belong to.
    public static IReadOnlyList<(string Table, string Owner)> Tables { get; } =
    [
        ("family_trade", "candidate"),
        ("index_rule_trade", "candidate"),
        ("index_family_trade", "index_code || ' ' || family"),
        ("dip_trade", "rule || ' | ' || index_code"),
        ("quality_holding", "rule || ' | ' || index_code"),
        ("improving_trade", "rule || ' | ' || index_code"),
        ("heavyweight_holding", "'the S&P 500 heavyweights book'"),
        ("heavyweight_rule_holding", "candidate"),
        ("index_heavyweight_holding", "'the ' || index_code || ' heavyweights book'"),
        ("index_heavyweight_rule_holding", "candidate"),
        ("rule_pick", "rule || ' | ' || index_code"),
        ("trade_fill", "owner || ' | ' || index_code"),
        ("version_score", "rule || ', ' || version"),
    ];

    // The rule an owner names: an owner naming its index after the separator names the rule before it.
    public static string RuleOf(string owner) =>
        owner.LastIndexOf(" | ", StringComparison.Ordinal) is var at and >= 0 ? owner[..at] : owner;

    // The rules whose rows differ on any table, which a registration reads as not reproduced.
    public static IReadOnlySet<string> Differing(IEnumerable<ReplayCompared> compared) =>
        compared.Where(one => !one.Same).Select(one => RuleOf(one.Owner)).ToHashSet(StringComparer.Ordinal);

    // The line a comparison's record opens with: the newest session and the newest registration the old build's copy held,
    // the store as the copies were made from it. A registration reading the record refuses where the store has moved past it.
    public const string RecordOpening = "replay-compare: over the store through ";

    public const string DiffersOpening = "replay-compare: differs: ";

    // A record read back: the session and the registration it was made through, and the rules whose rows differ; none where
    // the record does not open with its header.
    public static (string Session, long Register, IReadOnlySet<string> Differing)? Read(string record)
    {
        var lines = record.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
        var header = System.Text.RegularExpressions.Regex.Match(lines.FirstOrDefault() ?? string.Empty, "^" + System.Text.RegularExpressions.Regex.Escape(RecordOpening) + @"(\S+), its register to id (\d+)$");

        if (!header.Success)
        {
            return null;
        }

        var differing = lines
            .Where(line => line.StartsWith(DiffersOpening, StringComparison.Ordinal))
            .Select(line => line[DiffersOpening.Length..].Split('\t'))
            .Where(parts => parts.Length >= 2)
            .Select(parts => RuleOf(parts[1]))
            .ToHashSet(StringComparer.Ordinal);

        return (header.Groups[1].Value, long.Parse(header.Groups[2].Value, CultureInfo.InvariantCulture), differing);
    }

    public static string Header(string session, long register) =>
        FormattableString.Invariant($"{RecordOpening}{session}, its register to id {register}");

    public static async Task<string> HeaderOfAsync(string databaseFile, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile) + ";Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT (SELECT MAX(session_date) FROM bar_all), (SELECT MAX(id) FROM candidate_register);";

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        await reader.ReadAsync(cancellation);

        return Header(reader.IsDBNull(0) ? "none" : reader.GetString(0), reader.IsDBNull(1) ? 0 : reader.GetInt64(1));
    }

    // A column holding the instant a row was written, which no comparison reads; a window's opening instant names the window.
    public static bool Written(string column) => column.EndsWith("_at", StringComparison.Ordinal) && column != "opened_at";

    public static async Task<IReadOnlyList<ReplayCompared>> CompareAsync(string oldFile, string newFile, CancellationToken cancellation = default)
    {
        // Unpooled, so no later connection is handed this one with the other copy still attached.
        await using var connection = new SqliteConnection(StoreConnection.For(oldFile) + ";Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync(cancellation);

        await using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $file AS other;";
            // An attached store opens under the connection's own read-only mode.
            attach.Parameters.AddWithValue("$file", Path.GetFullPath(newFile));
            await attach.ExecuteNonQueryAsync(cancellation);
        }

        var found = new List<ReplayCompared>();

        foreach (var (table, owner) in Tables)
        {
            var columns = new List<string>();

            await using (var info = connection.CreateCommand())
            {
                info.CommandText = $"PRAGMA main.table_info({table});";

                await using var reader = await info.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    if (!Written(reader.GetString(1)))
                    {
                        columns.Add(reader.GetString(1));
                    }
                }
            }

            var listed = string.Join(", ", columns);
            var counts = new Dictionary<string, (int Old, int New)>(StringComparer.Ordinal);
            var first = new Dictionary<string, string>(StringComparer.Ordinal);

            async Task CountAsync(string schema, bool old)
            {
                await using var command = connection.CreateCommand();

                command.CommandText = $"SELECT {owner}, COUNT(*) FROM {schema}.{table} GROUP BY 1;";

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    var key = reader.IsDBNull(0) ? "(none)" : reader.GetString(0);
                    var was = counts.GetValueOrDefault(key);

                    counts[key] = old ? (reader.GetInt32(1), was.New) : (was.Old, reader.GetInt32(1));
                }
            }

            async Task DifferAsync(string from, string against, string side)
            {
                await using var command = connection.CreateCommand();

                command.CommandText = $"SELECT {owner}, * FROM (SELECT {listed} FROM {from}.{table} EXCEPT SELECT {listed} FROM {against}.{table}) ORDER BY 1;";

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    var key = reader.IsDBNull(0) ? "(none)" : reader.GetString(0);

                    if (!first.ContainsKey(key))
                    {
                        first[key] = side + ": " + string.Join(", ", Enumerable.Range(1, reader.FieldCount - 1).Select(column =>
                            columns[column - 1] + "=" + (reader.IsDBNull(column) ? "null" : Convert.ToString(reader.GetValue(column), CultureInfo.InvariantCulture))));
                    }
                }
            }

            await CountAsync("main", old: true);
            await CountAsync("other", old: false);
            await DifferAsync("main", "other", "only the old build's");
            await DifferAsync("other", "main", "only the new build's");

            found.AddRange(counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                new ReplayCompared(table, pair.Key, pair.Value.Old, pair.Value.New, !first.ContainsKey(pair.Key) && pair.Value.Old == pair.Value.New, first.GetValueOrDefault(pair.Key))));
        }

        return found;
    }
}
