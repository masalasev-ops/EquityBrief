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

        // Six from 5.1, where the sector was added for the universe screen to
        // filter on. Stated exactly rather than as a floor, so a column added
        // to the store without being declared fails here as much as one
        // declared without being built.
        Assert.Equal(6, declared.Count);

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
