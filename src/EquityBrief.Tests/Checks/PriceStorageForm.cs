using EquityBrief.Data.Migrations;

namespace EquityBrief.Tests.Checks;

// price-storage-form. No migration declares a price or money column REAL.
// The rule can be satisfied in code while a REAL column is still written, which
// is why this reads the migration text rather than the C# that uses it.
public class PriceStorageForm
{
    // Which columns hold money is read from SCHEMA.md, not listed here.
    //
    // The obligation carried out of 0.7: this was a hand maintained array, so a
    // money column added to SCHEMA under a new name was unchecked and nothing
    // said so. The document marks one by writing "decimal" in its Notes cell, so
    // the set moves when the document does.
    internal static IReadOnlyList<string> MoneyColumns() =>
        StoreSchema.DeclaredMoney(Corpus.Read("docs/SCHEMA.md"))
            .Select(entry => entry.Column.Name)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void NoMigrationDeclaresAMoneyColumnReal()
    {
        var money = MoneyColumns();

        // The set SCHEMA declares, stated because a run that derived none would
        // assert nothing at all and pass. Eleven columns across five tables when
        // this floor was set.
        Assert.True(money.Count >= 8, $"SCHEMA declares {money.Count} money columns, expected at least 8.");

        var columns = SchemaMigrations.All
            .SelectMany(migration => ColumnDeclarations.In(migration.Sql)
                .Select(column => (Migration: migration.Name, Column: column)))
            .ToArray();

        // Two scopes. The declarations read is context; the money columns among
        // them carry the property, and that number rises as the tables holding
        // prices are built.
        var declared = columns.Where(entry => money.Contains(entry.Column.Name, StringComparer.Ordinal)).ToArray();

        Assert.True(columns.Length >= 10, $"Read {columns.Length} column declarations, expected at least 10.");
        Assert.True(declared.Length >= 5, $"Read {declared.Length} money columns in migrations, expected at least 5.");

        Assert.DoesNotContain(declared, entry => entry.Column.Type != "TEXT");
    }

    [Fact]
    public void TheMoneyColumnsAreReadFromSchemaRatherThanListedHere()
    {
        var money = MoneyColumns();

        // The five tables SCHEMA marks. bar writes "decimal in code" and the
        // others write it bare, so the marker is the word rather than the phrase.
        Assert.Contains("close", money);
        Assert.Contains("spend", money);
        Assert.Contains("low_edge", money);

        // And a column that holds a statistic is not one of them, which is what
        // stops the reader marking every column in a table carrying a price.
        Assert.DoesNotContain("volume", money);
        Assert.DoesNotContain("observed_at", money);

        // One table is described as a difference from another and has no column
        // table of its own. It is named rather than skipped quietly, so a second
        // one appearing is a failure here instead of a silent exclusion from the
        // money check.
        Assert.Equal("theme_section", Assert.Single(StoreSchema.DescribedByDelta(Corpus.Read("docs/SCHEMA.md"))));

        // The permanent proof that the reader reads the Notes cell and not the
        // column name, over a constructed document.
        var derived = StoreSchema.DeclaredMoney(
            "### t\nGrain: one row per thing.\n\n" +
            "| Column | Type | Notes |\n|---|---|---|\n" +
            "| `a` | TEXT | decimal |\n" +
            "| `b` | REAL | a statistic |\n");

        Assert.Equal("a", Assert.Single(derived).Column.Name);
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
