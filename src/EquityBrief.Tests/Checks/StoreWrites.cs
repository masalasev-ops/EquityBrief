using System.Text.RegularExpressions;
using EquityBrief.Data.Migrations;

namespace EquityBrief.Tests.Checks;

// bar-append-only and writer-ownership.
//
// Both read what the shipped source and the migrations actually say, because a
// rule about writes that is checked by reading the components' own claims about
// themselves is a rule checked against an opinion.
//
// writer-ownership runs in both directions from 1.1. Until the first component
// landed, one direction had a population of zero and the other could not be
// asserted at all, and the two tests standing in for them were placeholders that
// said so. The first component is what turns them into the property the roster
// row has always claimed.
public class StoreWrites
{
    static IReadOnlyList<string> ShippedSource() =>
        Repository.SourceFiles()
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
            .ToArray();

    // The type that carries a statement, taken from the file name. A coarse
    // attribution and stated as one: a file holding two types would credit both
    // writes to the first, and nothing in the shipped source does that today.
    static string TypeIn(string file) => Path.GetFileNameWithoutExtension(file);

    internal static IReadOnlyList<SourceWrite> WritesIn(string text) => SourceStatements.In(text);

    [Fact]
    public void NothingDeletesOrUpdatesABarTable()
    {
        // The scope is the whole shipped source and every migration, and it is
        // stated because the population that carries the property, statements
        // touching a bar table, is currently zero: the bar table arrives at 1.2.
        var sources = ShippedSource();
        var migrations = SchemaMigrations.All;

        Assert.True(sources.Count >= 5, $"Scanned {sources.Count} shipped source files, expected at least 5.");
        Assert.True(migrations.Count >= 2, $"Scanned {migrations.Count} migrations, expected at least 2.");

        var offences = sources
            .SelectMany(file => Offences(WritesIn(File.ReadAllText(file)), TypeIn(file))
                .Select(write => $"{Path.GetFileName(file)}: {write.Operation} on {write.Table}: {write.Statement}"))
            .Concat(migrations
                // A migration has no owner. The exemption is for a component
                // SCHEMA names as a deleter, and a migration is not one: a
                // migration that dropped or rewrote a bar table would take the
                // series out from under every component at once.
                .SelectMany(migration => WritesIn(migration.Sql)
                    .Where(write => IsBar(write.Table) && write.Operation is not SourceStatements.Insert)
                    .Select(write => $"{migration.Name}: {write.Operation} on {write.Table}: {write.Statement}")))
            .ToArray();

        Assert.Empty(offences);

        // The exemption exists, so the scope it narrows is stated. Two owners
        // are declared and both are exercised: a run that found none would mean
        // the exemption had swallowed the property rather than narrowed it.
        var owners = BarDeleters();

        Assert.Equal(2, owners.Count);
        Assert.Contains("BarFetcher", owners);
        Assert.Contains("CorporateActionChecker", owners);
    }

    // Who SCHEMA declares may delete a bar, read from the ownership table
    // rather than listed here. A list beside the check is a second statement of
    // one fact, and this is the fact SCHEMA is the only place for.
    internal static IReadOnlyList<string> BarDeleters() =>
    [
        .. Declared()
            .Where(row => IsBar(row.Table) && row.Operation == SourceStatements.Delete)
            .Select(row => row.Owner)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(owner => owner, StringComparer.Ordinal),
    ];

    // A delete against a bar table is permitted only in the file of a component
    // SCHEMA declares as a deleter of it. An update is permitted nowhere, by
    // anybody: SCHEMA's Update cell for `bar` is "none", and the two sanctioned
    // removals both take whole sessions rather than editing one in place.
    internal static IReadOnlyList<SourceWrite> Offences(IReadOnlyList<SourceWrite> writes, string type)
    {
        var owners = BarDeleters();

        return
        [
            .. writes.Where(write => IsBar(write.Table)
                && write.Operation != SourceStatements.Insert
                && !(write.Operation == SourceStatements.Delete
                    && owners.Contains(type, StringComparer.Ordinal))),
        ];
    }

    static bool IsBar(string table) => table.Equals("bar", StringComparison.OrdinalIgnoreCase);

    [Fact]
    public void ADeleteInAFileTheSchemaDoesNotNameStillFails()
    {
        // The permanent negative proof under the exemption. The same statement
        // passes in the owner's file and fails in one that is not named, so the
        // exemption is a property of the declaration rather than of the text.
        var delete = WritesIn("DELETE FROM bar WHERE session_date < $oldest;");

        Assert.Empty(Offences(delete, "BarFetcher"));
        Assert.Empty(Offences(delete, "CorporateActionChecker"));

        var found = Assert.Single(Offences(delete, "Backfill"));

        Assert.Equal(SourceStatements.Delete, found.Operation);
        Assert.Single(Offences(delete, "ReadApi"));
        Assert.Single(Offences(delete, "SomeNewComponent"));

        // And the exemption is for deletes alone. An owner may drop the
        // sessions that fell out of the window; nobody may edit a stored bar.
        Assert.Single(Offences(WritesIn("UPDATE bar SET close = '1';"), "BarFetcher"));
        Assert.Single(Offences(WritesIn("DROP TABLE bar;"), "CorporateActionChecker"));
    }

    [Fact]
    public void TheCheckReportsADeleteAgainstABarTable()
    {
        // The permanent proof that the reader can find one, written before there
        // was a bar table for it to find.
        Assert.Contains(WritesIn("DELETE FROM bar WHERE ticker = 'AAPL';"), write => write is { Operation: "Delete", Table: "bar" });
        Assert.Contains(WritesIn("UPDATE bar SET close = '1';"), write => write is { Operation: "Update", Table: "bar" });
        Assert.Contains(WritesIn("DROP TABLE bar;"), write => write is { Operation: "Drop", Table: "bar" });

        var insert = WritesIn("INSERT INTO bar VALUES ('AAPL');");

        Assert.Contains(insert, write => write is { Operation: "Insert", Table: "bar" });
        Assert.DoesNotContain(insert, write => write.Operation == "Delete");
    }

    [Fact]
    public void TheReaderReadsSqlRatherThanProseOrIdentifiers()
    {
        // The two false positives the first version of this reader produced,
        // both from shipped source that writes nothing. They are kept as cases
        // because the repair is a shape match, and a shape match is the kind of
        // thing a later widening quietly loosens back into a word match.
        Assert.Empty(WritesIn(
            "// Insert, Update and Delete are the three operations SCHEMA.md declares\n" +
            "// ownership for, and membership and bar are two of the tables.\n"));

        Assert.Empty(WritesIn("public enum Store { Membership, Bar, Level }"));
        Assert.Empty(WritesIn("public enum Touch { None, Read, Insert, Update, Delete }"));

        // A url inside a string keeps its slashes rather than being read as a
        // comment, which is what the scanner exists for.
        Assert.Contains("//example", SourceStatements.WithoutComments("var x = \"https://example\"; // gone"));
        Assert.DoesNotContain("gone", SourceStatements.WithoutComments("var x = \"https://example\"; // gone"));

        // And an upsert is one statement carrying two operations on one table.
        var upsert = WritesIn(@"@""
            INSERT INTO membership (ticker) VALUES ($ticker)
            ON CONFLICT (ticker) DO UPDATE SET observed_at = excluded.observed_at;""");

        Assert.Contains(upsert, write => write is { Operation: "Insert", Table: "membership" });
        Assert.Contains(upsert, write => write is { Operation: "Update", Table: "membership" });
    }

    // The ownership table, read once for both directions.
    internal static IReadOnlyList<(string Table, string Operation, string Owner)> Declared()
    {
        var schema = Corpus.Read("docs/SCHEMA.md");

        var rows = Regex.Matches(
            schema,
            @"^\| `([a-z_]+)` \| ([^|]*) \| ([^|]*) \| ([^|]*) \|",
            RegexOptions.Multiline);

        string[] operations = [SourceStatements.Insert, SourceStatements.Update, SourceStatements.Delete];

        return rows
            .SelectMany(row => operations
                .Select((operation, index) => (Table: row.Groups[1].Value, Operation: operation, Cell: row.Groups[index + 2].Value))
                .SelectMany(entry => entry.Cell
                    .Split(',')
                    .Select(name => name.Trim())
                    // A component name. "none" is not one, and neither is the
                    // run log's "every component appends", which is prose the
                    // file uses deliberately and which the next test handles.
                    .Where(name => Regex.IsMatch(name, "^[A-Z][A-Za-z]+$"))
                    .Select(name => (entry.Table, entry.Operation, Owner: name))))
            .ToArray();
    }

    // The one cell in the ownership table that names no component. SCHEMA gives
    // run_log's Insert to "every component appends", deliberately, because the
    // run log is the one store every stage writes its own row to. Read from the
    // document rather than hardcoded, so the exemption disappears the day the
    // cell is changed to name an owner.
    // A migration that rebuilds a table rather than dropping one.
    //
    // SQLite cannot change a primary key in place, so a column that has to admit
    // null is changed by creating a replacement, copying into it, dropping the
    // original and renaming. The drop is half of a rename and not a removal, and
    // the signature that says so is the rename back to the dropped name in the
    // same source. Without this the only way to make `membership.joined` admit
    // the unknown the provider carries for 145 of its 822 spans would have been
    // to stop asserting that nothing drops a declared table.
    //
    // Narrow on purpose, and in three ways: only the migration runner, only a
    // drop, and only where the same source renames something back to the name it
    // dropped. A drop with no rename is still reported, which the proof below
    // exercises rather than describes.
    internal static bool RebuildsInPlace((string Type, string Operation, string Table, string Statement) write) =>
        write.Operation == SourceStatements.Drop
        && write.Type == nameof(EquityBrief.Data.Migrations.SchemaMigrations)
        && Regex.IsMatch(write.Statement, @"\bDROP\s+TABLE\b", RegexOptions.IgnoreCase)
        && RenamesBackTo(write.Table);

    internal static bool RenamesBackTo(string table) =>
        EquityBrief.Data.Migrations.SchemaMigrations.All.Any(migration =>
            Regex.IsMatch(
                migration.Sql,
                @"\bDROP\s+TABLE\s+" + Regex.Escape(table) + @"\b",
                RegexOptions.IgnoreCase)
            && Regex.IsMatch(
                migration.Sql,
                @"\bALTER\s+TABLE\s+\w+\s+RENAME\s+TO\s+" + Regex.Escape(table) + @"\b",
                RegexOptions.IgnoreCase));

    [Fact]
    public void ADropIsForgivenOnlyWhereTheSameMigrationRenamesSomethingBackToIt()
    {
        // The permanent proof. A rebuild is permitted and a removal is not, and
        // the difference is read out of the migration rather than taken on the
        // word "rebuild" appearing in a comment.
        Assert.True(RenamesBackTo("membership"));

        // Every other declared table, none of which any migration drops. This is
        // the direction that would quietly widen: a helper that answered yes to
        // everything would pass the assertion above and forgive every drop.
        foreach (var table in new[] { "bar", "run_log", "series_state" })
        {
            Assert.False(RenamesBackTo(table), $"Nothing renames a table back to {table}, so a drop of it is a removal.");
        }
    }

    internal static bool AnyComponentMay(string table, string operation)
    {
        var schema = Corpus.Read("docs/SCHEMA.md");

        var row = Regex.Match(
            schema,
            @"^\| `" + Regex.Escape(table) + @"` \| ([^|]*) \| ([^|]*) \| ([^|]*) \|",
            RegexOptions.Multiline);

        if (!row.Success)
        {
            return false;
        }

        var cell = operation switch
        {
            SourceStatements.Insert => row.Groups[1].Value,
            SourceStatements.Update => row.Groups[2].Value,
            _ => row.Groups[3].Value,
        };

        return cell.Contains("every component appends", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TheRunLogIsTheOneStoreEveryComponentMayAppendTo()
    {
        // Asserted rather than assumed, and both ways, so the exemption cannot
        // quietly widen to a second table or a second operation.
        Assert.True(AnyComponentMay("run_log", SourceStatements.Insert));
        Assert.False(AnyComponentMay("run_log", SourceStatements.Update));
        Assert.False(AnyComponentMay("run_log", SourceStatements.Delete));
        Assert.False(AnyComponentMay("membership", SourceStatements.Insert));
        Assert.False(AnyComponentMay("bar", SourceStatements.Insert));
    }

    [Fact]
    public void EveryWriteInTheCodeIsDeclaredInSchema()
    {
        var declared = Declared();
        var tables = declared.Select(row => row.Table).Distinct(StringComparer.Ordinal).ToArray();

        Assert.True(declared.Count >= 15, $"Read {declared.Count} declared owner entries, expected at least 15.");

        var found = ShippedSource()
            .SelectMany(file => WritesIn(File.ReadAllText(file))
                .Where(write => tables.Contains(write.Table, StringComparer.OrdinalIgnoreCase))
                .Select(write => (Type: TypeIn(file), write.Operation, write.Table, write.Statement)))
            .ToArray();

        // Stated because the property is over the writes found, and a run that
        // found none has asserted nothing. It was zero until 1.1.
        Assert.True(found.Length >= 2, $"Found {found.Length} writes against a declared table, expected at least 2.");

        var undeclared = found
            .Where(write => !AnyComponentMay(write.Table, write.Operation))
            .Where(write => !RebuildsInPlace(write))
            .Where(write => !declared.Any(row =>
                row.Table.Equals(write.Table, StringComparison.OrdinalIgnoreCase)
                && row.Operation == write.Operation
                && row.Owner == write.Type))
            .Select(write => $"{write.Type} performs {write.Operation} on {write.Table}, which SCHEMA does not declare it owns: {write.Statement}")
            .ToArray();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void EveryDeclaredWriterThatExistsHasCodeBehindIt()
    {
        // The direction that could not hold until a component existed, and the
        // reason the roster row read wider than the check for two phases. The
        // ones that do not exist yet are still counted rather than asserted, and
        // that count shrinks by one per component from here.
        var declared = Declared();
        var sources = ShippedSource();

        // Grouped rather than keyed, because two projects each carry a
        // Program.cs and a dictionary throws on the second one.
        var writesByType = sources
            .GroupBy(TypeIn, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<SourceWrite>)[.. group.SelectMany(file => WritesIn(File.ReadAllText(file)))],
                StringComparer.Ordinal);

        var built = declared
            .Where(row => writesByType.ContainsKey(row.Owner))
            .ToArray();

        var absent = declared
            .Select(row => row.Owner)
            .Distinct(StringComparer.Ordinal)
            .Count(owner => !writesByType.ContainsKey(owner));

        // Two: MembershipLoader against membership for Insert and for Update.
        // At the measured value rather than below it, deliberately, because this
        // population only grows: it was zero before 1.1 and rises by one per
        // declared operation as each component lands.
        Assert.True(
            built.Length >= 2,
            $"{built.Length} declared owner entries have a class in the shipped source, expected at " +
            "least 2. This direction of writer-ownership is asserted over those and counted over " +
            "the rest, so a run finding none has asserted nothing.");

        var empty = built
            .Where(row => !writesByType[row.Owner].Any(write =>
                write.Operation == row.Operation
                && write.Table.Equals(row.Table, StringComparison.OrdinalIgnoreCase)))
            .Select(row => $"{row.Owner} is declared to {row.Operation} {row.Table} and carries no statement that does")
            .ToArray();

        Assert.Empty(empty);

        // Context, not the property. The number of declared writers with no
        // class yet is a fact about how much of the system is unbuilt.
        Assert.True(absent > 0, $"{absent} declared writers have no class yet, which is context and not a pass.");
    }

    [Fact]
    public void TheCheckReportsAWriteNobodyDeclared()
    {
        // Both directions proved against constructed input, so neither rests on
        // the corpus happening to be correct today.
        var declared = Declared();

        Assert.Contains(declared, row => row is { Table: "membership", Operation: "Insert", Owner: "MembershipLoader" });
        Assert.Contains(declared, row => row is { Table: "membership", Operation: "Update", Owner: "MembershipLoader" });
        Assert.DoesNotContain(declared, row => row is { Table: "membership", Operation: "Delete" });

        // A write against a table the type does not own.
        var rogue = WritesIn("INSERT INTO facts (ticker) VALUES ('AAPL');");

        Assert.Contains(rogue, write => write is { Operation: "Insert", Table: "facts" });
        Assert.DoesNotContain(declared, row =>
            row.Table == "facts" && row.Operation == "Insert" && row.Owner == "MembershipLoader");
    }
}
