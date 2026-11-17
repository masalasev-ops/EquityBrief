using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// Each setup's record over the long history as a pick's card, Home and Tonight's tiles draw it: a price family's traded plan
// from its newest run of the test of the buy and sell points on an index, newest by the order its rows were written, and each
// filing-based family's year its filings reach back to on every index.
// see: A book's record box reads its months at the exit it trades in points a month from the year after its exit was chosen
public sealed partial class ReadApi
{
    // A run's start, the stamp its id ends on, as yyyyMMddTHHmmssZ.
    const string RunStarted = "substr(run_id, length(run_id) - 15)";

    const string TradedRecords = @"
        SELECT index_code, family, through, traded_later_edge_per_pick, traded_later_picks, live_later_move, live_later_up, live_later_fills, " + RunStarted + @"
        FROM plan_point WHERE rowid IN (SELECT MAX(rowid) FROM plan_point GROUP BY index_code, family)
        ORDER BY index_code, family;";

    const string BookRecords = @"
        SELECT index_code, family, from_year, through, months, points, " + RunStarted + @"
        FROM book_record WHERE rowid IN (SELECT MAX(rowid) FROM book_record GROUP BY index_code, family)
        ORDER BY index_code, family;";

    // The figure each record stood at before a correction: its newest row from a run started before the correction's day.
    const string TradedBefore = @"
        SELECT index_code, family, traded_later_edge_per_pick
        FROM plan_point WHERE rowid IN (SELECT MAX(rowid) FROM plan_point WHERE " + RunStarted + @" < $before GROUP BY index_code, family);";

    const string BooksBefore = @"
        SELECT index_code, family, points
        FROM book_record WHERE rowid IN (SELECT MAX(rowid) FROM book_record WHERE " + RunStarted + @" < $before GROUP BY index_code, family);";

    public async Task<SetupRecords> SetupRecordsAsync()
    {
        var records = new List<SetupLongRecord>();

        // On the operator's ruling of 2026-11-17, for a month after the newest correction to the history, the figure each record
        // stood at before it, drawn where the record's newest run started on or after the correction's day.
        // see: A record corrected after a fault in the history says so beneath its figure for a month, with the figure it stood at before
        var corrected = MarkRenderer.RecordCorrections[^1];
        var today = clock.SessionDateAt(clock.UtcNow);
        var shown = today >= corrected && today < corrected.AddMonths(1);
        var before = corrected.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "T000000Z";
        var tradedWas = shown ? await WasAsync(TradedBefore, before) : new Dictionary<(string, string), double>();
        var booksWas = shown ? await WasAsync(BooksBefore, before) : new Dictionary<(string, string), double>();

        SetupLongRecord Corrected(SetupLongRecord record, string started, IReadOnlyDictionary<(string, string), double> was) =>
            string.CompareOrdinal(started, before) >= 0 && was.TryGetValue((record.Index, record.Family), out var figure)
                ? record with { CorrectedOn = corrected, Was = figure }
                : record;

        await using (var connection = Open())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = TradedRecords;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                records.Add(Corrected(
                    new SetupLongRecord(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(3) ? null : reader.GetDouble(3),
                        reader.IsDBNull(4) ? null : reader.GetInt32(4),
                        EquityBrief.Core.History.LongLedger.StandingFrom.Year,
                        int.Parse(reader.GetString(2).AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture))
                    {
                        // From the 23.10 correction of 2026-11-13 the live plan's moves from 2019, none before a run writes them.
                        // see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
                        LiveMove = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        LiveUp = reader.IsDBNull(6) ? null : reader.GetDouble(6),
                        LiveFills = reader.IsDBNull(7) ? null : reader.GetInt32(7),
                    },
                    reader.GetString(8),
                    tradedWas));
            }
        }

        // On the operator's ruling of 2026-11-15 on the shapes each live book's record in points a month from the year after its
        // exit was chosen, newest by the order its rows were written, ahead of the year its filings reach back to.
        // see: A book's record box reads its months at the exit it trades in points a month from the year after its exit was chosen
        await using (var connection = Open())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = BookRecords;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                records.Add(Corrected(
                    new SetupLongRecord(
                        reader.GetString(0),
                        reader.GetString(1),
                        null,
                        null,
                        reader.GetInt32(2),
                        int.Parse(reader.GetString(3).AsSpan(0, 4), System.Globalization.CultureInfo.InvariantCulture))
                    {
                        Points = reader.IsDBNull(5) ? null : reader.GetDouble(5),
                        Months = reader.GetInt32(4),
                    },
                    reader.GetString(6),
                    booksWas));
            }
        }

        records.AddRange(Universes.Offered
            .SelectMany(choice => SetupLongRecord.FilingFamilies.Select(family => SetupLongRecord.Filings(choice.Code, family)))
            .OfType<SetupLongRecord>());

        return new SetupRecords(records);
    }

    async Task<IReadOnlyDictionary<(string, string), double>> WasAsync(string query, string before)
    {
        var was = new Dictionary<(string, string), double>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = query;
        command.Parameters.AddWithValue("$before", before);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(2))
            {
                was[(reader.GetString(0), reader.GetString(1))] = reader.GetDouble(2);
            }
        }

        return was;
    }
}
