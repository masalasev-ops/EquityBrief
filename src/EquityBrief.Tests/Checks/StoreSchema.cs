using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

internal sealed record StoreColumn(string Name, string Type);

// Reads a table's columns from SCHEMA.md and from a built store, so the two can
// be compared. A table SCHEMA.md does not describe throws rather than returning
// an empty list, because a check that reports nothing about a table nobody
// declared is a check that passes over the thing it was written to catch.
internal static class StoreSchema
{
    internal static IReadOnlyList<StoreColumn> Declared(string schemaMarkdown, string table)
    {
        var heading = $"### {table}";
        var start = schemaMarkdown.IndexOf(heading, StringComparison.Ordinal);

        if (start < 0)
        {
            throw new InvalidOperationException(
                $"SCHEMA.md describes no table called {table}. The store and the document have " +
                "to be compared against each other, so a missing description fails.");
        }

        var next = schemaMarkdown.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = next < 0 ? schemaMarkdown[start..] : schemaMarkdown[start..next];

        var columns = new List<StoreColumn>();

        foreach (var line in section.Split('\n'))
        {
            var cells = line.Split('|');

            if (cells.Length < 4 || !cells[1].Contains('`'))
            {
                continue;
            }

            var type = cells[2].Trim();

            // One cell may name several columns of the same type, as
            // started_at and ended_at do.
            foreach (Match name in Regex.Matches(cells[1], "`([a-z_]+)`"))
            {
                columns.Add(new StoreColumn(name.Groups[1].Value, type));
            }
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException(
                $"SCHEMA.md has a heading for {table} but no column table under it.");
        }

        return columns;
    }

    internal static IReadOnlyList<StoreColumn> Built(TemporaryStore store, string table)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";

        var columns = new List<StoreColumn>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            columns.Add(new StoreColumn(reader.GetString(1), reader.GetString(2)));
        }

        if (columns.Count == 0)
        {
            throw new InvalidOperationException($"The store holds no table called {table}.");
        }

        return columns;
    }

    internal static IReadOnlyList<string> Tables(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name;";

        var tables = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}
