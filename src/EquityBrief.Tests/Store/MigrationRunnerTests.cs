using EquityBrief.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Store;

// 0.2's done condition, as two tests: applies against an empty directory, and
// again against an applied store without error.
public class MigrationRunnerTests
{
    [Fact]
    public void ItAppliesAgainstAnEmptyDirectory()
    {
        using var store = new TemporaryStore();

        var outcome = MigrationRunner.Standard().Apply(store.DatabaseFile);

        Assert.Equal(0, outcome.From);
        Assert.Equal(SchemaMigrations.LatestVersion, outcome.To);
        Assert.Equal(SchemaMigrations.All.Count, outcome.Applied.Count);
        Assert.True(File.Exists(store.DatabaseFile), $"No store at {store.DatabaseFile}.");
    }

    [Fact]
    public void ItAppliesNothingASecondTime()
    {
        using var store = new TemporaryStore().Migrated();

        var second = MigrationRunner.Standard().Apply(store.DatabaseFile);

        Assert.Empty(second.Applied);
        Assert.Equal(second.From, second.To);
        Assert.Equal(SchemaMigrations.LatestVersion, second.To);
    }

    [Fact]
    public void ItRefusesAStoreWrittenByANewerCheckout()
    {
        using var store = new TemporaryStore().Migrated();
        store.Execute($"PRAGMA user_version = {SchemaMigrations.LatestVersion + 1};");

        var refusal = Assert.Throws<InvalidOperationException>(
            () => MigrationRunner.Standard().Apply(store.DatabaseFile));

        Assert.Contains("not migrated backwards", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheVersionsAreDistinctAndStartAtOne()
    {
        var versions = SchemaMigrations.All.Select(migration => migration.Version).ToArray();

        Assert.Equal(versions.Length, versions.Distinct().Count());
        Assert.Equal(Enumerable.Range(1, versions.Length), versions.Order());
    }

    [Fact]
    public void TheStoreRefusesAValueItCannotLosslesslyConvert()
    {
        // The behavioural proof that STRICT is on. A scan of the migration text
        // finds the word; only an insert proves the store enforces anything.
        using var store = new TemporaryStore().Migrated();

        var refusal = Assert.Throws<SqliteException>(() => store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, rows_written) " +
            "VALUES ('r', 's', 'i', 'many');"));

        Assert.Contains(
            "cannot store TEXT value in INTEGER column",
            refusal.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AMoneyColumnCoercesADoubleRatherThanRefusingIt()
    {
        // The limit of what the store enforces, pinned so a later session does
        // not assume STRICT covers the money rule. SQLite renders the double as
        // text and stores that, because the conversion is lossless. Nothing at
        // the storage layer catches it, which is why price-storage-form reads
        // the migration text and why prices are decimal in code.
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, spend) VALUES ('r', 's', 'i', 1.25);");

        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT typeof(spend), spend FROM run_log;";
        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal("text", reader.GetString(0));
        Assert.Equal("1.25", reader.GetString(1));
    }

    [Fact]
    public void TheStoreAcceptsTheSameMoneyValueAsText()
    {
        // Without this, the test above would pass just as well over a column
        // that refuses everything.
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, spend) VALUES ('r', 's', 'i', '1.25');");
    }
}
