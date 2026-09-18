using EquityBrief.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker;

// The store a verb a person runs writes to, refused where it is missing or behind this checkout
// before anything is read from it or written to it: a verb does not migrate a store.
public static class VerbStore
{
    public static string? Refusal(string databaseFile)
    {
        if (!File.Exists(databaseFile))
        {
            return $"no store at {databaseFile}. Run tools/migrate, which creates it.";
        }

        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = databaseFile,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ConnectionString);

        connection.Open();

        var at = MigrationRunner.AppliedVersion(connection);

        return at < SchemaMigrations.LatestVersion
            ? FormattableString.Invariant(
                $"the store is at schema {at} and this checkout reads schema {SchemaMigrations.LatestVersion}. ") +
              "Run tools/migrate first; a verb does not migrate a store and writes nothing to one this checkout has not migrated."
            : null;
    }
}
