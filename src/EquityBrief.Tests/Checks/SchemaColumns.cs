using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// schema-columns. Every table a migration creates has the columns and storage
// types SCHEMA.md declares for it, in the order it declares them, and no others.
// The document is the source of truth and the store is asserted against it.
public class SchemaColumns
{
    // What this check reaches, declared here rather than beside it, so a
    // placement or a verdict naming schema-columns is reconciled against what
    // schema-columns actually opens. Two catalogue and matrix rows rest on it:
    // both are single rows and neither needs this check to read the
    // architecture, because what it asserts is the store against SCHEMA.md.
    internal static CheckReach Reach => new(
        "schema-columns",
        ["docs/SCHEMA.md"],
        [
            // 5.5, the forward returns and the news pulse.
            CheckReach.Key(Scope.StoresTable, "Forward returns"),
            CheckReach.Key(Scope.StoresTable, "News pulse"),

            // 6.1, the fundamentals store: migration 19's columns and types against
            // SCHEMA's own declaration of them.
            CheckReach.Key(Scope.StoresTable, "Fundamentals"),

            // 6.3, the source documents store: migration 20's columns and types,
            // and the two columns that admit null because a refusal is kept as a
            // row. Nothing else in the suite reads a column's nullability, and
            // this table is the first where it carries a property rather than
            // being a detail of the declaration.
            CheckReach.Key(Scope.StoresTable, "Source documents"),

            // 6.4, the research store and the theme store: migration 21's columns
            // and types, and the status the store itself refuses outside the four
            // SCHEMA declares.
            CheckReach.Key(Scope.StoresTable, "Research store"),
            CheckReach.Key(Scope.StoresTable, "Theme store"),

            // 5.4, tonight's list.
            CheckReach.Key(Scope.StoresTable, "Listings"),

            // 5.3, the facts file.
            CheckReach.Key(Scope.StoresTable, "Facts"),

            // 5.2, the move annotator.
            CheckReach.Key(Scope.StoresTable, "Indicators, swings, volume profile, levels, ladders, moves"),

            CheckReach.Key(Scope.CatalogueTable, "Migration runner"),
            CheckReach.Key(Scope.StoresTable, "Run log"),
            CheckReach.Key(Scope.StoresTable, "Membership"),
            CheckReach.Key(Scope.StoresTable, "Bar store"),
            CheckReach.Key(Scope.StoresTable, "Series state"),
            CheckReach.Key(Scope.StoresTable, "Calendar"),
        ]);

    const string Sample =
        "### sample\n" +
        "Grain: one row per thing.\n\n" +
        "| Column | Type | Notes |\n" +
        "|---|---|---|\n" +
        "| `a` | TEXT | |\n" +
        "| `b`, `c` | INTEGER | two names, one type |\n\n" +
        "Primary key: `a`.\n";

    [Fact]
    public void BarMatchesWhatSchemaDeclares()
    {
        // The third table, and the first carrying money columns, so this is also
        // where the declared storage type of a price is compared against what
        // the store actually built.
        var schema = Corpus.Read("docs/SCHEMA.md");
        var declared = StoreSchema.Declared(schema, "bar");

        Assert.Equal(10, declared.Count);

        using var store = new TemporaryStore().Migrated();
        var built = StoreSchema.Built(store, "bar");

        Assert.Equal(
            declared.Select(column => (column.Name, column.Type)),
            built.Select(column => (column.Name, column.Type)));

        // Every money column is TEXT in the built store, not only in the
        // migration text price-storage-form reads.
        var money = PriceStorageForm.MoneyColumns();

        Assert.All(
            built.Where(column => money.Contains(column.Name, StringComparer.Ordinal)),
            column => Assert.Equal("TEXT", column.Type));
    }

    [Fact]
    public void MembershipMatchesWhatSchemaDeclares()
    {
        // The second table, and the second store claim resting on this check.
        // Same assertion as run_log's below: the columns the store has are the
        // columns SCHEMA declares, in that order, with the declared types.
        var schema = Corpus.Read("docs/SCHEMA.md");
        var declared = StoreSchema.Declared(schema, "membership");

        // Seven from 6.9, where the industry was added for a theme to be read
        // under, and six from 5.1, where the sector was added for the universe
        // screen to filter on. Stated exactly rather than as a floor, so a column
        // added to the store without being declared fails here as much as one
        // declared without being built.
        Assert.Equal(7, declared.Count);

        using var store = new TemporaryStore().Migrated();
        var built = StoreSchema.Built(store, "membership");

        Assert.Equal(
            declared.Select(column => (column.Name, column.Type)),
            built.Select(column => (column.Name, column.Type)));
    }

    [Fact]
    public void RunLogMatchesWhatSchemaDeclares()
    {
        var declared = StoreSchema.Declared(File.ReadAllText(Repository.Schema), "run_log");

        // Ten, and stated in advance: run_log is the only table 0.2 creates and
        // this is the scope carrying the property.
        Assert.Equal(10, declared.Count);

        using var store = new TemporaryStore().Migrated();

        Assert.Equal(declared, StoreSchema.Built(store, "run_log"));
    }

    [Fact]
    public void SourceDocumentMatchesWhatSchemaDeclaresAndAdmitsNullOnlyWhereARefusalNeedsIt()
    {
        var schema = Corpus.Read("docs/SCHEMA.md");
        var declared = StoreSchema.Declared(schema, "source_document");

        // Seven, stated exactly. The test above compares every table in the
        // store against the file, so what this adds is the count and the
        // nullability, and the nullability is the half nothing else reads.
        Assert.Equal(7, declared.Count);

        using var store = new TemporaryStore().Migrated();

        Assert.Equal(declared, StoreSchema.Built(store, "source_document"));

        // A refusal is kept as a row with its reason and no body, and one of the
        // things it is refused for is carrying no publish date. So exactly two
        // columns admit null, and which two is the property: a not null date
        // column makes the most common refusal in the measured set unrecordable,
        // and a not null body makes every refusal unrecordable.
        var admitsNull = AdmitsNull(store, "source_document");

        Assert.Equal(["published_on", "body"], admitsNull);

        // The other direction, because a migration that left every column
        // nullable would satisfy the line above by containing those two.
        Assert.All(
            new[] { "id", "url", "title", "fetched_at", "admissibility" },
            column => Assert.DoesNotContain(column, admitsNull));

        // And the file says the same thing in prose, which is the document the
        // store is asserted against everywhere else here. SCHEMA carries no
        // nullability column, so the statement is a sentence and this is what
        // holds the sentence to the store.
        Assert.Contains(
            "Two columns admit null and neither is an absence of data",
            schema,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheResearchAndThemeTablesMatchWhatSchemaDeclaresAndRefuseAStatusItDoesNot()
    {
        var schema = Corpus.Read("docs/SCHEMA.md");

        using var store = new TemporaryStore().Migrated();

        // Nine and ten, stated exactly. The theme table is the research table's
        // columns with its subject renamed and one more, and asserting both
        // counts is what says the file wrote the difference out rather than
        // leaving a reader to apply it.
        var research = StoreSchema.Declared(schema, "research_section");
        var theme = StoreSchema.Declared(schema, "theme_section");

        Assert.Equal(9, research.Count);
        Assert.Equal(10, theme.Count);

        Assert.Equal(research, StoreSchema.Built(store, "research_section"));
        Assert.Equal(theme, StoreSchema.Built(store, "theme_section"));

        Assert.Equal(
            research.Skip(1).Select(column => column.Name),
            theme.Skip(1).Take(8).Select(column => column.Name));

        Assert.Equal("theme", theme[0].Name);
        Assert.Equal("industries", theme[^1].Name);

        // One column admits null in each, the reason, which a pending and an
        // accepted section have none of. A section with no admissible source is a
        // row with empty prose rather than a null one, because the row is what
        // records that it was left out.
        Assert.Equal(["reject_reason"], AdmitsNull(store, "research_section"));
        Assert.Equal(["reject_reason"], AdmitsNull(store, "theme_section"));

        // The four statuses are accepted and a fifth is refused by the store, in
        // both tables, so a writer that invented one fails at the write.
        string[] statuses = ["pending", "accepted", "rejected", "fallback"];

        for (var version = 1; version <= statuses.Length; version++)
        {
            store.Execute(
                "INSERT INTO research_section VALUES ('AAPL', 'The two cases', " +
                $"{version}, '2026-09-08', 'a model', '{statuses[version - 1]}', '', '[]', NULL);");
        }

        var refused = Assert.Throws<SqliteException>(() => store.Execute(
            "INSERT INTO research_section VALUES ('AAPL', 'The two cases', 9, '2026-09-08', 'a model', 'omitted', '', '[]', NULL);"));

        // 19 is SQLITE_CONSTRAINT and 275 its CHECK extension, read as codes
        // rather than as a message for the reason the membership refusals are.
        Assert.Equal(19, refused.SqliteErrorCode);
        Assert.Equal(275, refused.SqliteExtendedErrorCode);

        Assert.Throws<SqliteException>(() => store.Execute(
            "INSERT INTO theme_section VALUES ('memory', 'The industry cycle', 1, '2026-09-08', 'a model', 'omitted', '', '[]', NULL, '[]');"));

        store.Execute(
            "INSERT INTO theme_section VALUES ('memory', 'The industry cycle', 1, '2026-09-08', 'a model', 'pending', '', '[]', NULL, '[]');");
    }

    // Which of a built table's columns admit null, read off the store rather
    // than off the migration text, because what admits a row is the table.
    static IReadOnlyList<string> AdmitsNull(TemporaryStore store, string table)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({table});";

        var columns = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            if (reader.GetInt32(3) == 0)
            {
                columns.Add(reader.GetString(1));
            }
        }

        return columns;
    }

    [Fact]
    public void EveryTableInTheStoreIsOneSchemaDeclares()
    {
        // The other direction. A migration that creates a table nobody declared
        // is the failure this half catches.
        using var store = new TemporaryStore().Migrated();
        var schema = File.ReadAllText(Repository.Schema);

        using var connection = store.Open();
        var tables = StoreSchema.Tables(connection);

        Assert.True(tables.Count >= 1, $"Read {tables.Count} tables, expected at least 1.");
        Assert.All(tables, table => Assert.Equal(
            StoreSchema.Declared(schema, table), StoreSchema.Built(store, table)));
    }

    // ---- membership's uniqueness, exercised rather than read ----
    //
    // Migration 6 dropped the primary key and replaced it with
    // UNIQUE (index_code, ticker, IFNULL(joined, '')), because a constituent
    // the provider carries no join date for cannot sit in a key whose column is
    // NOT NULL. The fold is what makes it the non-naive form: a plain index on
    // the three columns treats two NULLs as distinct and lets a ticker hold two
    // unknown-join spans.
    //
    // The phase 2 sign-off proved three refusals and the control by hand, from
    // outside the repository, and recorded that nothing in the suite asserted
    // any of them: schema-columns reads tables and columns and does not read
    // indexes. A key changed under time pressure and examined once by hand is
    // what a permanent test replaces.
    //
    // These insert directly rather than through MembershipLoader, because the
    // loader's statement carries ON CONFLICT ... DO UPDATE against the same
    // expression, so a legitimate re-run upserts and never reaches the refusal.
    // What is under test is the constraint, not the upsert.

    const string Insert =
        @"INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at) VALUES ";

    static SqliteException Refused(TemporaryStore store, string values) =>
        Assert.Throws<SqliteException>(() => store.Execute(Insert + values + ";"));

    static void Refuses(SqliteException refusal)
    {
        // 19 is SQLITE_CONSTRAINT and 2067 is its UNIQUE extension. The codes
        // and not the message: SQLite names the index in the message for an
        // expression index and names the columns for a plain one, so asserting
        // the text here would make every refusal test fail the moment the index
        // changed form, whether or not the row was still refused. The form is
        // asserted once, on its own, below. Three refusal tests that all go red
        // for one reason are three tests saying one thing.
        Assert.Equal(19, refusal.SqliteErrorCode);
        Assert.Equal(2067, refusal.SqliteExtendedErrorCode);
    }

    [Fact]
    public void TheSpanIndexIsUniqueAndFoldsTheUnknownJoinDate()
    {
        // The form, read off the built store rather than off the migration text,
        // because what enforces anything is the index the store holds. This is
        // the assertion the three refusals below deliberately do not make: they
        // say a duplicate is refused, and this says by what.
        using var store = new TemporaryStore().Migrated();
        using var connection = store.Open();

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT sql FROM sqlite_master WHERE type = 'index' AND name = 'membership_span';";

        var sql = command.ExecuteScalar() as string;

        Assert.NotNull(sql);
        Assert.Contains("UNIQUE", sql!, StringComparison.OrdinalIgnoreCase);

        // The fold is the whole reason this is an expression index rather than
        // the primary key it replaced. Without it two NULLs are distinct and a
        // ticker can hold two unknown-join spans.
        Assert.Contains("IFNULL(joined, '')", sql!, StringComparison.Ordinal);

        // And SCHEMA says the same thing in prose, which is the document this
        // check asserts the store against everywhere else.
        Assert.Contains(
            "with the unknown folded to a value, which is an expression index rather than a primary key",
            Corpus.Read("docs/SCHEMA.md"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ASecondSpanForAConstituentWithNoJoinDateIsRefused()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(Insert + "('GSPC', 'WAB', NULL, NULL, '2026-09-05T21:10:00Z');");

        Refuses(Refused(store, "('GSPC', 'WAB', NULL, NULL, '2026-09-05T21:10:00Z')"));
    }

    [Fact]
    public void ASecondSpanForADatedConstituentIsRefused()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(Insert + "('GSPC', 'AAPL', '1982-11-30', NULL, '2026-09-05T21:10:00Z');");

        Refuses(Refused(store, "('GSPC', 'AAPL', '1982-11-30', NULL, '2026-09-05T21:10:00Z')"));
    }

    [Fact]
    public void TwoRowsForOneTickerBothCarryingNoJoinDateCollide()
    {
        // The case a naive expression index lets through, and the reason the
        // fold is there. The two rows differ in `left`, so nothing but the fold
        // makes them the same key: without it both NULLs are distinct and the
        // ticker holds two unknown-join spans, which is the state the primary
        // key used to make impossible.
        using var store = new TemporaryStore().Migrated();

        store.Execute(Insert + "('GSPC', 'WAB', NULL, NULL, '2026-09-05T21:10:00Z');");

        Refuses(Refused(store, "('GSPC', 'WAB', NULL, '2026-09-07', '2026-09-05T21:10:00Z')"));
    }

    [Fact]
    public void ThreeDistinctSpansForOneTickerAreAccepted()
    {
        // The control. A refusal that refuses everything proves nothing, so the
        // index is shown to admit the domain it is supposed to admit: one span
        // whose join date is unknown and two dated ones for the same ticker.
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            Insert +
            "('GSPC', 'WAB', NULL, '1990-01-02', '2026-09-05T21:10:00Z')," +
            "('GSPC', 'WAB', '1995-06-01', '2001-03-04', '2026-09-05T21:10:00Z')," +
            "('GSPC', 'WAB', '2010-07-08', NULL, '2026-09-05T21:10:00Z');");

        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM membership WHERE ticker = 'WAB';";

        Assert.Equal(3L, (long)command.ExecuteScalar()!);
    }

    [Fact]
    public void TheEmptyStringEdgeIsUnreachableThroughTheWritingPathRatherThanGuarded()
    {
        // The sentinel's own risk, stated rather than guarded. A literal empty
        // string in `joined` collides with the unknown, because the fold maps
        // both to the same key, and it was confirmed to at the phase 2 sign-off.
        //
        // Nothing guards it and nothing should. MembershipLoader is the sole
        // declared writer of this table, and what it binds comes from
        // IndexConstituent.Joined, which is a DateOnly? Every present value
        // either parses as yyyy-MM-dd or throws, so the bound value is DBNull or
        // exactly ten characters and there is no path that produces "". A guard
        // would be code for a case the writer cannot reach.
        //
        // This is asserted rather than left as a comment, because the reasoning
        // rests entirely on that type. The day it becomes a string this test
        // fails, and the reader is told to reconsider the guard rather than
        // discovering the collision from a store.
        var joined = typeof(IndexConstituent).GetProperty(nameof(IndexConstituent.Joined));

        Assert.NotNull(joined);
        Assert.Equal(typeof(DateOnly?), joined!.PropertyType);

        // And the other half of "sole declared writer": SCHEMA gives every
        // operation on this table to one component, which is what makes the
        // exposure a future writer rather than this one.
        Assert.Contains(
            "| `membership` | MembershipLoader | MembershipLoader | none |",
            Corpus.Read("docs/SCHEMA.md"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void TheReaderTakesSeveralNamesFromOneCell()
    {
        Assert.Equal(
            [new StoreColumn("a", "TEXT"), new StoreColumn("b", "INTEGER"), new StoreColumn("c", "INTEGER")],
            StoreSchema.Declared(Sample, "sample"));
    }

    [Fact]
    public void ATableTheDocumentDoesNotDescribeFailsRatherThanReturningNothing()
    {
        // The parse guard. Reporting zero columns for a table nobody declared
        // would pass over exactly what this check exists to find.
        Assert.Throws<InvalidOperationException>(
            () => StoreSchema.Declared(File.ReadAllText(Repository.Schema), "not_a_table"));

        using var store = new TemporaryStore().Migrated();
        Assert.Throws<InvalidOperationException>(() => StoreSchema.Built(store, "not_a_table"));
    }
}
