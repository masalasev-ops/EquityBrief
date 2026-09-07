using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Harness;

internal sealed record ArchitectureTable(string Heading, IReadOnlyList<IReadOnlyList<string>> Rows)
{
    // The first row is the header. A table with only a header makes no claims,
    // which is a fact the report states rather than passes over.
    internal IReadOnlyList<IReadOnlyList<string>> Body => Rows.Count > 0 ? Rows.Skip(1).ToArray() : [];
}

// Reads ARCHITECTURE.html's tables and the heading each one sits under.
//
// Every table is found, not only the ones the harness knows what to do with,
// because a table nobody placed is a table that can go unread and that is the
// failure architecture-conformance exists to catch.
internal static class ArchitectureTables
{
    internal static IReadOnlyList<ArchitectureTable> In(string document)
    {
        var tables = new List<ArchitectureTable>();
        var heading = "(before any heading)";

        foreach (Match match in Regex.Matches(
                     document,
                     @"<(h[1-4])[^>]*>(.*?)</\1>|<table[^>]*>(.*?)</table>",
                     RegexOptions.Singleline))
        {
            if (match.Groups[1].Success)
            {
                heading = Text(match.Groups[2].Value);
                continue;
            }

            tables.Add(new ArchitectureTable(heading, RowsIn(match.Groups[3].Value)));
        }

        if (tables.Count == 0)
        {
            throw new InvalidOperationException(
                "No tables were found in the architecture. Reporting zero claims from a document " +
                "that could not be parsed is the failure this guard exists to prevent, so this " +
                "fails instead.");
        }

        return tables;
    }

    static IReadOnlyList<IReadOnlyList<string>> RowsIn(string table) =>
        Regex.Matches(table, @"<tr[^>]*>(.*?)</tr>", RegexOptions.Singleline)
            .Select(row => (IReadOnlyList<string>)Regex
                .Matches(row.Groups[1].Value, @"<t[hd][^>]*>(.*?)</t[hd]>", RegexOptions.Singleline)
                .Select(cell => Text(cell.Groups[1].Value))
                .ToArray())
            .ToArray();

    static string Text(string markup) =>
        WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(markup, "<br[^>]*>", " "), "<[^>]+>", string.Empty))
            .Replace('\n', ' ')
            .Trim() is var text && text.Length > 0
            ? Regex.Replace(text, @"\s+", " ")
            : string.Empty;
}
