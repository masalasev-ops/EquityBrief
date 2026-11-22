using System.Globalization;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The local model's words over each stored case, read for the case for promotion: for each variant the words the newest
// answer wrote with the day and the model, and the earlier answers as its history; a refused answer is no words, so the page
// draws code's sentences in its place.
// see: The monthly run stores each case standing from Paper trading's accounts in Core, and the local run writes its words
public sealed partial class ReadApi
{
    const string AnsweredCaseWords = @"
        SELECT index_code, family, variant, for_words, against_words, model, asked_at
        FROM promotion_case_words
        WHERE outcome = 'answered'
        ORDER BY asked_at DESC, rowid DESC;
    ";

    public async Task<IReadOnlyDictionary<string, CaseWordsView>> CaseWordsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = AnsweredCaseWords;

        var all = new Dictionary<string, List<CaseWordsLine>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var key = SinglePageApp.CaseKey(reader.GetString(0), reader.GetString(1), reader.GetString(2));
            var written = DateOnly.FromDateTime(DateTime.ParseExact(reader.GetString(6), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal));

            if (!all.TryGetValue(key, out var lines))
            {
                all[key] = lines = [];
            }

            lines.Add(new CaseWordsLine(reader.GetString(3), reader.GetString(4), reader.GetString(5), written));
        }

        return all.ToDictionary(pair => pair.Key, pair => new CaseWordsView(pair.Value[0], [.. pair.Value.Skip(1)]), StringComparer.Ordinal);
    }
}
