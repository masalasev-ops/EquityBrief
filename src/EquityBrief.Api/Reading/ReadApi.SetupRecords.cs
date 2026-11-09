using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// Each setup's record over the long history as a pick's card, Home and Tonight's tiles draw it: a price family's traded plan
// from its newest run of the test of the buy and sell points on an index, newest by the order its rows were written, and each
// filing-based family's year its filings reach back to on every index.
// see: A pick's card draws its setup's record over the long history under its entry check, the rule's record and never the stock's
public sealed partial class ReadApi
{
    const string TradedRecords = @"
        SELECT index_code, family, through, traded_later_edge_per_pick, traded_later_picks
        FROM plan_point WHERE rowid IN (SELECT MAX(rowid) FROM plan_point GROUP BY index_code, family)
        ORDER BY index_code, family;";

    public async Task<SetupRecords> SetupRecordsAsync()
    {
        var records = new List<SetupLongRecord>();

        await using (var connection = Open())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = TradedRecords;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                records.Add(new SetupLongRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.IsDBNull(3) ? null : reader.GetDouble(3),
                    reader.IsDBNull(4) ? null : reader.GetInt32(4),
                    EquityBrief.Core.History.LongLedger.StandingFrom.Year,
                    int.Parse(reader.GetString(2).AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture)));
            }
        }

        records.AddRange(Universes.Offered
            .SelectMany(choice => SetupLongRecord.FilingFamilies.Select(family => SetupLongRecord.Filings(choice.Code, family)))
            .OfType<SetupLongRecord>());

        return new SetupRecords(records);
    }
}
