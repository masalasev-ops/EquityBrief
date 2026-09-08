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
    // Every table SCHEMA describes, from its own section headings. The Tables
    // reader below asks a store what it holds; this asks the document what it
    // declares, and component-access compares the two vocabularies against it.
    internal static IReadOnlyList<string> DeclaredTables(string schemaMarkdown) =>
        System.Text.RegularExpressions.Regex
            .Matches(schemaMarkdown, @"^### ([a-z_]+)\s*$", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    // Every money column SCHEMA declares, read from the Notes cell that marks
    // one rather than from a list kept beside the check.
    //
    // The obligation carried out of 0.7: price-storage-form's set of money
    // column names was hand maintained, so a money column added under a new name
    // was unchecked and nothing said so. SCHEMA marks one by saying "decimal" in
    // its Notes, which `bar` writes as "decimal in code" and the other four
    // tables write bare, so the marker is matched on the word.
    internal static IReadOnlyList<(string Table, StoreColumn Column)> DeclaredMoney(string schemaMarkdown) =>
        DeclaredTables(schemaMarkdown)
            .Except(DescribedByDelta(schemaMarkdown), StringComparer.Ordinal)
            .SelectMany(table => Money(schemaMarkdown, table).Select(column => (Table: table, Column: column)))
            .ToArray();

    // Tables SCHEMA describes as a difference from another rather than with a
    // column table of their own. There is one, `theme_section`, which says it
    // has research_section's columns with theme in place of ticker.
    //
    // Named rather than skipped quietly. A sweep over every table has to do
    // something about this one, and swallowing it would mean a second such table
    // was excluded from the money check with nothing saying so.
    internal static IReadOnlyList<string> DescribedByDelta(string schemaMarkdown) =>
        DeclaredTables(schemaMarkdown)
            .Where(table => !HasAColumnTable(schemaMarkdown, table))
            .ToArray();

    static bool HasAColumnTable(string schemaMarkdown, string table)
    {
        var heading = $"### {table}";
        var start = schemaMarkdown.IndexOf(heading, StringComparison.Ordinal);
        var next = schemaMarkdown.IndexOf("\n### ", start + heading.Length, StringComparison.Ordinal);
        var section = next < 0 ? schemaMarkdown[start..] : schemaMarkdown[start..next];

        return section.Contains("| Column |", StringComparison.Ordinal);
    }

    static IReadOnlyList<StoreColumn> Money(string schemaMarkdown, string table) =>
        Columns(schemaMarkdown, table)
            .Where(entry => entry.Notes.Contains("decimal", StringComparison.OrdinalIgnoreCase))
            .Select(entry => entry.Column)
            .ToArray();

    internal static IReadOnlyList<StoreColumn> Declared(string schemaMarkdown, string table) =>
        Columns(schemaMarkdown, table).Select(entry => entry.Column).ToArray();

    static IReadOnlyList<(StoreColumn Column, string Notes)> Columns(string schemaMarkdown, string table)
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

        var columns = new List<(StoreColumn Column, string Notes)>();

        foreach (var line in section.Split('\n'))
        {
            var cells = line.Split('|');

            if (cells.Length < 4 || !cells[1].Contains('`'))
            {
                continue;
            }

            var type = cells[2].Trim();

            // The Notes cell is carried because it is where SCHEMA marks a
            // column as money, and price-storage-form reads that rather than
            // keeping its own list of names.
            var notes = cells[3].Trim();

            // One cell may name several columns of the same type, as
            // started_at and ended_at do.
            foreach (Match name in Regex.Matches(cells[1], "`([a-z_]+)`"))
            {
                columns.Add((new StoreColumn(name.Groups[1].Value, type), notes));
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
