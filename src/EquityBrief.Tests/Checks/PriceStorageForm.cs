using EquityBrief.Data.Migrations;

namespace EquityBrief.Tests.Checks;

// price-storage-form. No migration declares a price or money column REAL.
// The rule can be satisfied in code while a REAL column is still written, which
// is why this reads the migration text rather than the C# that uses it.
public class PriceStorageForm
{
    // Every column name in SCHEMA.md that holds a price or a money value. A
    // migration adding a money column under a new name adds it here too, which
    // is a line of review this list exists to force.
    static readonly string[] Money =
    [
        "open", "high", "low", "close", "price", "spend",
        "band_low", "band_high", "low_edge", "high_edge",
    ];

    [Fact]
    public void NoMigrationDeclaresAMoneyColumnReal()
    {
        var columns = SchemaMigrations.All
            .SelectMany(migration => ColumnDeclarations.In(migration.Sql)
                .Select(column => (Migration: migration.Name, Column: column)))
            .ToArray();

        // Scope, stated in advance. The property is carried by the money
        // columns; the total is context and has a floor set below its value.
        var money = columns.Where(entry => Money.Contains(entry.Column.Name, StringComparer.Ordinal)).ToArray();

        Assert.True(columns.Length >= 10, $"Read {columns.Length} column declarations, expected at least 10.");
        Assert.True(money.Length >= 1, $"Read {money.Length} money columns, expected at least 1.");

        Assert.DoesNotContain(money, entry => entry.Column.Type != "TEXT");
    }

    [Fact]
    public void TheCheckReportsAMoneyColumnDeclaredReal()
    {
        // The permanent proof that the assertion can fail.
        var columns = ColumnDeclarations.In(
            "CREATE TABLE t (\n  ticker TEXT,\n  spend REAL\n) STRICT;");

        Assert.Contains(new StoreColumn("spend", "REAL"), columns);
        Assert.Contains(new StoreColumn("ticker", "TEXT"), columns);
    }
}
