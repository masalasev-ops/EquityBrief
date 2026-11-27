using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The reading recipes as the Improvement loop page reads them for one index: every recipe entered, newest first, each with
// its run on the index where one has worked it; and every piece of data a recipe asked for that the store lacks.
// see: A reading recipe is a closed set of operations over stored fields and is kept apart from the pinned catalogue
public sealed partial class ReadApi
{
    const string RecipeRows = @"
        SELECT r.id, r.line, r.words, r.source, substr(r.entered_at, 1, 10), u.figures, u.members, u.first_session, u.last_session, u.checked
        FROM recipe r LEFT JOIN recipe_run u ON u.recipe_id = r.id AND u.index_code = $index
        ORDER BY r.id DESC;";

    const string DataRequests = "SELECT need, line, source, substr(entered_at, 1, 10) FROM data_request ORDER BY id DESC;";

    public async Task<RecipesView> RecipesAsync(string index)
    {
        var recipes = new List<RecipeRow>();
        var requests = new List<DataRequestRow>();

        await using var connection = Open();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = RecipeRows;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                recipes.Add(new RecipeRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    reader.IsDBNull(9) ? null : reader.GetInt32(9)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = DataRequests;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                requests.Add(new DataRequestRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        return new RecipesView(recipes, requests);
    }
}
