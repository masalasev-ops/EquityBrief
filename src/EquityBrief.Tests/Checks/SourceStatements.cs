using System.Text;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// One write a statement makes: the operation, the table, and the statement it
// was read from, so a failure can print the thing a person has to go and look at.
internal sealed record SourceWrite(string Operation, string Table, string Statement);

// Reads the writes out of source and out of migration SQL.
//
// Two things this must not do, both of which the first version did.
//
// It must not read prose. The first version matched a verb and a table name
// anywhere in the same semicolon-delimited span, over raw file text, so a
// comment saying "Insert, Update and Delete are the three operations SCHEMA
// declares" beside the word membership read as three writes against membership.
// A source scan that finds a pattern in a sentence is not evidence of behaviour.
//
// It must not read identifiers either, and that is why stripping comments is not
// enough on its own. The Store enum has members named Bar and Membership and the
// Touch enum has Insert, Update and Delete, and an enum body carries no
// semicolon, so the whole declaration read as one statement naming every table
// and every operation at once.
//
// So the match is on the shape of SQL rather than on words that appear in it.
// INSERT INTO a table, UPDATE a table SET, DELETE FROM a table, DROP TABLE a
// table. None of those shapes occurs in prose or in an identifier list, and each
// names its own table, so a statement touching two tables is read as touching
// both rather than as touching whichever the regex reached first.
internal static class SourceStatements
{
    // The operations SCHEMA declares ownership for, plus drop, which only
    // bar-append-only asks about and only of migrations.
    internal const string Insert = "Insert";
    internal const string Update = "Update";
    internal const string Delete = "Delete";
    internal const string Drop = "Drop";

    // A table name, optionally quoted, as SQLite accepts it.
    const string Name = @"[""`\[]?([A-Za-z_][A-Za-z0-9_]*)[""`\]]?";

    static readonly (string Operation, Regex Pattern)[] Shapes =
    [
        (Insert, new Regex(@"\binsert\s+(?:or\s+\w+\s+)?into\s+" + Name, Options)),
        (Update, new Regex(@"\bupdate\s+" + Name + @"\s+set\b", Options)),
        (Delete, new Regex(@"\bdelete\s+from\s+" + Name, Options)),
        (Drop, new Regex(@"\bdrop\s+table\s+(?:if\s+exists\s+)?" + Name, Options)),
    ];

    // An upsert names its table once, at the top, and the update it performs has
    // no table beside it. Without this the declared Update behind an upsert
    // looks like a declaration with no code behind it.
    static readonly Regex UpsertUpdate = new(@"\bon\s+conflict\b[^;]*?\bdo\s+update\s+set\b", Options);

    const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled;

    internal static IReadOnlyList<SourceWrite> In(string source)
    {
        var code = WithoutComments(source);
        var writes = new List<SourceWrite>();

        foreach (var statement in code.Split(';'))
        {
            var trimmed = Regex.Replace(statement, @"\s+", " ").Trim();

            if (trimmed.Length == 0)
            {
                continue;
            }

            foreach (var (operation, pattern) in Shapes)
            {
                writes.AddRange(pattern
                    .Matches(statement)
                    .Select(match => new SourceWrite(operation, match.Groups[1].Value, trimmed)));
            }

            if (!UpsertUpdate.IsMatch(statement))
            {
                continue;
            }

            // The update belongs to whatever the insert targeted.
            writes.AddRange(writes
                .Where(write => write.Operation == Insert && write.Statement == trimmed)
                .Select(write => new SourceWrite(Update, write.Table, trimmed))
                .ToArray());
        }

        return writes
            .DistinctBy(write => (write.Operation, write.Table, write.Statement))
            .ToArray();
    }

    // Comments out, string contents kept, because the SQL lives in the strings.
    // Written as a scanner rather than a regex because a regex that removes
    // comments will remove a "//" inside a string literal, and a url in a
    // constant is exactly where that bites.
    internal static string WithoutComments(string source)
    {
        var kept = new StringBuilder(source.Length);
        var index = 0;

        while (index < source.Length)
        {
            var here = source[index];
            var next = index + 1 < source.Length ? source[index + 1] : '\0';

            if (here == '/' && next == '/')
            {
                while (index < source.Length && source[index] != '\n')
                {
                    index++;
                }

                continue;
            }

            if (here == '/' && next == '*')
            {
                var close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                index = close < 0 ? source.Length : close + 2;

                continue;
            }

            if (here == '@' && next == '"')
            {
                index = KeepVerbatim(source, index, kept);

                continue;
            }

            if (here == '"')
            {
                index = KeepQuoted(source, index, kept);

                continue;
            }

            kept.Append(here);
            index++;
        }

        return kept.ToString();
    }

    // A verbatim string ends at a quote that is not doubled. The doubled quote
    // matters here rather than being a nicety: every SQL constant in this
    // repository quotes the column named left that way.
    static int KeepVerbatim(string source, int index, StringBuilder kept)
    {
        kept.Append(source[index]).Append(source[index + 1]);
        index += 2;

        while (index < source.Length)
        {
            if (source[index] == '"')
            {
                if (index + 1 < source.Length && source[index + 1] == '"')
                {
                    kept.Append('"').Append('"');
                    index += 2;

                    continue;
                }

                kept.Append('"');

                return index + 1;
            }

            kept.Append(source[index]);
            index++;
        }

        return index;
    }

    static int KeepQuoted(string source, int index, StringBuilder kept)
    {
        kept.Append(source[index]);
        index++;

        while (index < source.Length)
        {
            if (source[index] == '\\' && index + 1 < source.Length)
            {
                kept.Append(source[index]).Append(source[index + 1]);
                index += 2;

                continue;
            }

            if (source[index] == '"')
            {
                kept.Append('"');

                return index + 1;
            }

            if (source[index] == '\n')
            {
                return index;
            }

            kept.Append(source[index]);
            index++;
        }

        return index;
    }
}
