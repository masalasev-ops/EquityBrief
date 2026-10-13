using EquityBrief.Core.Time;

namespace EquityBrief.Api.Reading;

// The newest nights' own run ids, each a night's first try named for the instant it started, which the date line reads
// the night's usual start off.
// see: The date line names tonight's night by the start the most of the newest seven nights share
public sealed partial class ReadApi
{
    const string NightRunIds =
        "SELECT DISTINCT run_id FROM run_log " +
        "WHERE run_id GLOB 'night-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]T[0-9][0-9][0-9][0-9][0-9][0-9]Z' " +
        "ORDER BY run_id DESC LIMIT $nights;";

    public async Task<IReadOnlyList<string>> NightRunIdsAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NightRunIds;
        command.Parameters.AddWithValue("$nights", NightTime.NightsRead);

        var runs = new List<string>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            runs.Add(reader.GetString(0));
        }

        return runs;
    }
}
