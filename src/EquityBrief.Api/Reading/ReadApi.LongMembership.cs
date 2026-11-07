using EquityBrief.Core.History;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// What the History page reads of the long history's membership: the run that wrote it, each index's year of holdings by how
// they were matched or why they were left out, and the companies left out by name.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
// see: The holdings the long history leaves out are split by why they left with their share of value each year
public sealed partial class ReadApi
{
    const string LongMembershipRun = "SELECT run_id FROM long_snapshot ORDER BY run_id DESC LIMIT 1;";

    const string LongMembershipYears = @"
        SELECT index_code, substr(period, 1, 4), COUNT(DISTINCT period), COUNT(*),
               SUM(ticker IS NOT NULL), SUM(CASE WHEN matched_by = 'hand' THEN 1 ELSE 0 END), SUM(ticker IS NULL),
               COALESCE(left_as, ''), group_concat(value_usd, '|')
        FROM long_holding
        GROUP BY index_code, substr(period, 1, 4), COALESCE(left_as, '')
        ORDER BY index_code, substr(period, 1, 4);";

    const string LongMembershipLeftOut = "SELECT DISTINCT index_code, name FROM long_holding WHERE ticker IS NULL ORDER BY index_code, name;";

    public async Task<LongMembershipView> LongMembershipAsync()
    {
        await using var connection = Open();
        string? run;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LongMembershipRun;
            run = await command.ExecuteScalarAsync() as string;
        }

        if (run is null)
        {
            return new LongMembershipView(null, [], []);
        }

        var parts = new List<(string Index, int Year, int QuarterEnds, int Holdings, int Matched, int ByHand, int LeftOut, string LeftAs, decimal Value)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LongMembershipYears;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                // Each value as the schedule filed it, summed in decimal rather than by the store, which keeps them as text.
                var value = reader.IsDBNull(8) ? 0m : reader.GetString(8).Split('|', StringSplitOptions.RemoveEmptyEntries).Sum(text => decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture));

                parts.Add((reader.GetString(0), int.Parse(reader.GetString(1), System.Globalization.CultureInfo.InvariantCulture), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetString(7), value));
            }
        }

        var years = parts
            .GroupBy(part => (part.Index, part.Year))
            .Select(group => new LongMembershipYear(
                group.Key.Index,
                group.Key.Year,
                group.Max(part => part.QuarterEnds),
                group.Sum(part => part.Holdings),
                group.Sum(part => part.Matched),
                group.Sum(part => part.ByHand),
                group.Sum(part => part.LeftOut),
                group.Sum(part => part.Value),
                group.Where(part => part.LeftAs.Length > 0).ToDictionary(part => part.LeftAs, part => part.Value, StringComparer.Ordinal)))
            .ToArray();

        var named = new List<LongNamedLeftOut>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LongMembershipLeftOut;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (LongFloors.LeftOutAs(reader.GetString(1)) is { } company)
                {
                    named.Add(new LongNamedLeftOut(reader.GetString(0), company.Name, company.NamedAs));
                }
            }
        }

        return new LongMembershipView(run, years, named);
    }
}
