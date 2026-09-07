using EquityBrief.Core.Configuration;
using EquityBrief.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests;

// A store in a throwaway directory.
//
// Nothing in the suite reaches data/. A check that reads the live store is a
// check whose result depends on last night, which is a different instrument
// from the one this corpus builds.
internal sealed class TemporaryStore : IDisposable
{
    internal TemporaryStore()
    {
        Root = Path.Combine(Path.GetTempPath(), "equitybrief-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(Root);
    }

    internal string Root { get; }

    internal string DatabaseFile => Path.Combine(Root, StoreLocation.DatabaseFileName);

    internal TemporaryStore Migrated()
    {
        MigrationRunner.Standard().Apply(DatabaseFile);
        return this;
    }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(MigrationRunner.ConnectionStringFor(DatabaseFile));
        connection.Open();
        return connection;
    }

    internal void Execute(string sql)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // A leftover temporary directory is not a reason to fail a run.
        }
    }
}
