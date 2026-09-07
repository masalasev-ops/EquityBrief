using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

internal sealed record PathOffence(string Table, string Column, string Value);

internal sealed record PortabilityScan(
    int TablesScanned,
    int RowsScanned,
    IReadOnlyList<PathOffence> Offenders);

// Finds absolute paths in stored text.
//
// The Windows separator is written by code point rather than as an escape so
// that the two forms are both recognised wherever this runs. Path.IsPathRooted
// would not do: on macOS it does not see a Windows path, and a store is copied
// between the two machines, which is the whole reason this check exists.
internal static class AbsolutePaths
{
    const char WindowsSeparator = (char)92;

    internal static bool LooksAbsolute(string value)
    {
        if (value.Length < 2)
        {
            return false;
        }

        if (value[0] == '/' || value[0] == WindowsSeparator)
        {
            return true;
        }

        return char.IsAsciiLetter(value[0]) && value[1] == ':';
    }

    internal static PortabilityScan Scan(TemporaryStore store)
    {
        using var connection = store.Open();
        var tables = StoreSchema.Tables(connection);
        var offenders = new List<PathOffence>();
        var rows = 0;

        foreach (var table in tables)
        {
            using var command = connection.CreateCommand();
            command.CommandText = $"SELECT * FROM {table};";
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows++;

                for (var field = 0; field < reader.FieldCount; field++)
                {
                    if (reader.IsDBNull(field) || reader.GetFieldType(field) != typeof(string))
                    {
                        continue;
                    }

                    var value = reader.GetString(field);

                    if (LooksAbsolute(value))
                    {
                        offenders.Add(new PathOffence(table, reader.GetName(field), value));
                    }
                }
            }
        }

        return new PortabilityScan(tables.Count, rows, offenders);
    }
}
