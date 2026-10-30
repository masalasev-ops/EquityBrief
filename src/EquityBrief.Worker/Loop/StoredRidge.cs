using EquityBrief.Core.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The newest learned score fitted on all finished data for a family on an index, which a rule stating the ranking by
// the learned score orders its candidates by: of the ridge models stored for the family on the index, the one with the
// newest cut, which is the newest monthly run's model over every finished setup, since a fold's cut lies inside the
// history and the whole's lies the day after its last session; none where no run has fitted one. Read once a night where
// a standing rule asks, and never where none does.
// see: The engines' hooks are eight forms a registration states and all default off, and every source a rule pins moves only in a checkpoint that registers every rule reading it again
public static class StoredRidge
{
    const string Newest = "SELECT parameters FROM loop_model WHERE index_code = $index AND family = $family AND kind = 'ridge' ORDER BY learned_before DESC, year DESC LIMIT 1;";

    public static async Task<RidgeModel?> NewestAsync(SqliteConnection connection, SqliteTransaction? transaction, string index, string family, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = Newest;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", family);

        return await command.ExecuteScalarAsync(cancellation) is string text ? RidgeModel.Parse(text) : null;
    }

    // The same on a connection of its own, for a step handed the reader by the night rather than reading the store itself.
    public static async Task<IScoreModel?> NewestAsync(string databaseFile, string index, string family, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(EquityBrief.Data.StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        return await NewestAsync(connection, null, index, family, cancellation);
    }
}
