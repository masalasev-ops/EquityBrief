using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// The column name and storage type of every column a migration declares.
internal static class ColumnDeclarations
{
    internal static IReadOnlyList<StoreColumn> In(string sql) =>
        Regex.Matches(sql, @"^\s*([a-z_]+)\s+(TEXT|INTEGER|REAL|BLOB|ANY)\b", RegexOptions.Multiline)
            .Select(match => new StoreColumn(match.Groups[1].Value, match.Groups[2].Value))
            .ToArray();
}
