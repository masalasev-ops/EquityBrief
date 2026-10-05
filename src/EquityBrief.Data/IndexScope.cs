using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Data;

// The indices a stage that stores what every member needs reads its members from: the night's own index and the
// wider ones the night reads beside it, one list a query reads through a single parameter. A stage handed no wider
// index reads its own alone, as it did before the S&P 400 and 600 joined.
// see: The universe is the S&P 1500's three indices with each member tagged by its index, and membership is fetched
public static class IndexScope
{
    // The condition a members query reads its indices with, the list bound as one JSON array.
    public const string Condition = "index_code IN (SELECT value FROM json_each($indices))";

    public static IReadOnlyList<string> Of(string indexCode, IReadOnlyList<string>? wider) =>
        [indexCode, .. (wider ?? []).Where(code => !string.Equals(code, indexCode, StringComparison.Ordinal))];

    public static void Bind(SqliteCommand command, string indexCode, IReadOnlyList<string>? wider) =>
        command.Parameters.AddWithValue("$indices", JsonSerializer.Serialize(Of(indexCode, wider)));
}
