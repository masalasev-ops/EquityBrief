using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

public partial class ReadApi
{
    // Every row held back for a trade still open whose row names business first's trade as the one holding its stock, on
    // the S&P 500's list, on an index's list and on the dip's list, counted by the index the list is on; a pick of business
    // first's own held by its own trade is no other family's pick and is left out.
    // see: Business first stays in the night unseen until the next change that moves the S&P 400's and 600's pins, and the Run page counts the picks its open trades hold back each night an index
    const string HeldByBusinessFirstOn = @"
        SELECT 'GSPC', COUNT(*) FROM family_pick
        WHERE session_date = $on AND state = $held AND held_family = $family
        UNION ALL
        SELECT index_code, COUNT(*) FROM index_family_pick
        WHERE session_date = $on AND state = $held AND held_family = $family AND family <> $family
        GROUP BY index_code
        UNION ALL
        SELECT index_code, COUNT(*) FROM dip_pick
        WHERE session_date = $on AND state = $held AND held_family = $family
        GROUP BY index_code;
    ";

    // How many picks business first's open trades held back on a night on each of the three indices, read off the stored
    // pick rows, so every night the store holds states it and no night step moves.
    public async Task<HeldByBusinessFirstView> HeldByBusinessFirstAsync(DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = HeldByBusinessFirstOn;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$held", FamilyList.OpenTrade);
        command.Parameters.AddWithValue("$family", SetupFamilies.FundamentalsFirst.Name);

        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var index = reader.GetString(0);

            counts[index] = counts.GetValueOrDefault(index) + reader.GetInt32(1);
        }

        return new HeldByBusinessFirstView(
            on,
            counts.GetValueOrDefault(Universes.Large.Code),
            counts.GetValueOrDefault(Universes.Mid.Code),
            counts.GetValueOrDefault(Universes.Small.Code));
    }
}
