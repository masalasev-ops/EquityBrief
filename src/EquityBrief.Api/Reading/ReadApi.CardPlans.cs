using System.Globalization;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// Each price pick's plan its kind's test recommends, as the night priced it on the pick's card, by family and stock, for one
// index's night; none for a card the night priced nothing on.
// see: A watched pick on Home draws the plan the long test recommends for its kind of pick, priced on its own levels and not traded
public sealed partial class ReadApi
{
    const string CardAlternatives = @"
        SELECT family, ticker, alternative FROM decision_card
        WHERE index_code = $index AND session_date = $night AND alternative IS NOT NULL;";

    public async Task<IReadOnlyDictionary<(string Family, string Ticker), AlternativePlan>> CardAlternativesAsync(string index, DateOnly night)
    {
        var plans = new Dictionary<(string Family, string Ticker), AlternativePlan>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();
        command.CommandText = CardAlternatives;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (AlternativePlan.Parse(reader.GetString(2)) is { } plan)
            {
                plans[(reader.GetString(0), reader.GetString(1))] = plan;
            }
        }

        return plans;
    }
}
