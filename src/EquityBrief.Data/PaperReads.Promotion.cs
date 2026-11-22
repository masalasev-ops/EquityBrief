using System.Globalization;
using EquityBrief.Core.Paper;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Data;

// From 24.23 the presses on the case for promotion and the night's answers to them, oldest first, which the accounts read the
// carries of and the case page reads each press's state from; none where the store was migrated before their tables.
// see: A promotion swaps a running revision and its variant at the start of the next night that runs, each carrying its own record
public sealed partial class PaperReads
{
    const string PressesRead = "SELECT id, index_code, family, variant, press, undoes, month, pressed_at FROM promotion_press ORDER BY id;";

    const string AnswersRead = "SELECT press_id, index_code, family, applied, words, revision, promoted, running, replaced, carries, night, swapped_at FROM promotion_swap ORDER BY press_id;";

    public async Task<(IReadOnlyList<PromotionPressRow> Presses, IReadOnlyList<PromotionSwapRow> Answers)> PromotionSwapsAsync()
    {
        var presses = new List<PromotionPressRow>();
        var answers = new List<PromotionSwapRow>();

        await using var connection = Open();

        await using (var held = connection.CreateCommand())
        {
            held.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'promotion_swap';";

            if (Convert.ToInt64(await held.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0)
            {
                return (presses, answers);
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = PressesRead;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                presses.Add(new PromotionPressRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt64(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AnswersRead;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                answers.Add(new PromotionSwapRow(
                    reader.GetInt64(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetInt64(3) == 1,
                    reader.GetString(4),
                    reader.IsDBNull(5) ? null : reader.GetInt32(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6),
                    reader.IsDBNull(7) ? null : reader.GetString(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8),
                    PromotionSwaps.ReadCarries(reader.IsDBNull(9) ? null : reader.GetString(9)),
                    Day(reader.GetString(10)),
                    DateTimeOffset.Parse(reader.GetString(11), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)));
            }
        }

        return (presses, answers);
    }
}
