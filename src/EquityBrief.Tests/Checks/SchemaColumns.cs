using EquityBrief.Tests.Harness;

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
            CheckReach.Key(Scope.CatalogueTable, "Migration runner"),
            CheckReach.Key(Scope.StoresTable, "Run log"),
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
