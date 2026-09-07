using Microsoft.Data.Sqlite;

namespace EquityBrief.Data.Migrations;

// Applies pending migrations in version order, each in its own transaction with
// the version stamp inside it, so a store is never at a version it has not
// fully applied.
public sealed class MigrationRunner
{
    readonly IReadOnlyList<Migration> migrations;

    public MigrationRunner(IReadOnlyList<Migration> migrations)
    {
        if (migrations.Count == 0)
        {
            throw new ArgumentException(
                "No migrations were given. Applying nothing and reporting success is the " +
                "under-reporting this runner must not do.",
                nameof(migrations));
        }

        this.migrations = migrations;
    }

    public static MigrationRunner Standard() => new(SchemaMigrations.All);

    public static string ConnectionStringFor(string databaseFile) =>
        new SqliteConnectionStringBuilder { DataSource = databaseFile }.ConnectionString;

    public MigrationOutcome Apply(string databaseFile)
    {
        var directory = Path.GetDirectoryName(databaseFile);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(ConnectionStringFor(databaseFile));
        connection.Open();

        var applied = AppliedVersion(connection);
        var latest = migrations.Max(migration => migration.Version);

        if (applied > latest)
        {
            throw new InvalidOperationException(
                $"The store is at schema version {applied} and this checkout knows {latest}. " +
                "A store written by a newer checkout is not migrated backwards, because the " +
                "older code cannot know what the newer schema meant.");
        }

        var pending = migrations
            .Where(migration => migration.Version > applied)
            .OrderBy(migration => migration.Version)
            .ToArray();

        foreach (var migration in pending)
        {
            using var transaction = connection.BeginTransaction();

            using (var change = connection.CreateCommand())
            {
                change.Transaction = transaction;
                change.CommandText = migration.Sql;
                change.ExecuteNonQuery();
            }

            using (var stamp = connection.CreateCommand())
            {
                // A pragma takes no parameter, and the value is an integer from
                // the list above rather than anything a caller supplied.
                stamp.Transaction = transaction;
                stamp.CommandText = $"PRAGMA user_version = {migration.Version};";
                stamp.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        return new MigrationOutcome(
            applied,
            AppliedVersion(connection),
            pending.Select(migration => migration.Name).ToArray());
    }

    public static int AppliedVersion(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA user_version;";

        return Convert.ToInt32(command.ExecuteScalar());
    }
}

// What one run of the runner did. Applied is empty on a store that was already
// current, which is the second half of 0.2's done condition.
public sealed record MigrationOutcome(int From, int To, IReadOnlyList<string> Applied);
