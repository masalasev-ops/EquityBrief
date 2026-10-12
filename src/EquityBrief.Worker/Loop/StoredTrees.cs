using EquityBrief.Core.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The tree models the walk-forward tester stored, found by the key a rule's hooks name one by, the first twelve
// hexadecimal characters of its hash; each read back from its text the first time a key asks for it, and none for a key
// no stored model carries.
// see: The learned score's challenger is a tree ensemble fitted by FastTree and scored by code from its stored trees, put only where it passes and earns more than the ridge score
public static class StoredTrees
{
    const string Models = "SELECT hash, parameters FROM loop_model WHERE kind = 'trees' ORDER BY rowid;";

    public static async Task<Func<long, TreeModel?>> ReadAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken cancellation)
    {
        var texts = new Dictionary<long, string>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = Models;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                texts[TreeModel.KeyOf(reader.GetString(0))] = reader.GetString(1);
            }
        }

        var read = new Dictionary<long, TreeModel?>();

        return key =>
        {
            if (!read.TryGetValue(key, out var model))
            {
                model = texts.TryGetValue(key, out var text) ? TreeModel.Parse(text) : null;
                read[key] = model;
            }

            return model;
        };
    }
}
