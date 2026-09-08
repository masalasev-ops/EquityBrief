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
//
// Anywhere in the value, not at position zero.
//
// The obligation carried out of the 0.7 review. A path reaches a store row in
// two ways: written as the whole value, which position zero caught, and
// embedded in a sentence, which it did not. `run_log.detail` is where an
// exception's text lands, and exception text carries the path in the middle of
// a message: "could not open E:\...\equitybrief.db" is the case the check was
// built for and the one shape it could not see.
//
// Position zero is not simply relaxed to a scan, because a bare separator is
// far too common to search for. A relative path, a date, a ratio and a URL all
// carry one, and a check that reported them would be turned off. What is looked
// for is the shape of a rooted path: a separator that begins a token, or a
// drive letter followed by a colon and a separator. A URL's separators are
// preceded by a colon and another separator, so a URL does not match, and it is
// not a portability fault in any case: it reads the same on the other machine.
internal static class AbsolutePaths
{
    const char WindowsSeparator = (char)92;

    static bool IsSeparator(char character) =>
        character == '/' || character == WindowsSeparator;

    // A separator or a drive letter only roots a path where it begins a token.
    // In the middle of one it is a relative path, a date or a ratio.
    static bool BeginsAToken(string value, int index) =>
        index == 0 || char.IsWhiteSpace(value[index - 1]) || value[index - 1] is '\'' or '"' or '(' or '[' or '<';

    internal static bool LooksAbsolute(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (!BeginsAToken(value, index))
            {
                continue;
            }

            if (IsSeparator(value[index]))
            {
                // The whole run of separators, so a UNC path beginning with two
                // of them is rooted rather than skipped, and something has to
                // follow it: a separator with nothing after it is not a path.
                var after = index;

                while (after < value.Length && IsSeparator(value[after]))
                {
                    after++;
                }

                if (after < value.Length && !char.IsWhiteSpace(value[after]))
                {
                    return true;
                }

                continue;
            }

            // The drive form, which needs the separator after the colon. A
            // letter and a colon alone is drive-relative rather than rooted, and
            // is also what every timestamp in this store looks like at the hour.
            if (index + 2 < value.Length
                && char.IsAsciiLetter(value[index])
                && value[index + 1] == ':'
                && IsSeparator(value[index + 2]))
            {
                return true;
            }
        }

        return false;
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
