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

    // From the operator's order of 2026-11-24, item 5, each live rule's long test over its years to 2018, from the newest per-rule
    // run over the long history on its index started after the first build of that index's long ledger on or after the day
    // the 24.19 corrections landed: its trades and their total result per $1 risked; none where no such run stands.
    // see: A price family's record box draws the long test's years to 2018 beside its record from 2019, the long box saying it is being rebuilt until the long ledger is
    const string LongTestFigures = @"
        SELECT r.index_code, r.family, SUM(y.units), SUM(y.total)
        FROM loop_rule r JOIN loop_rule_year y ON y.run_id = r.run_id AND y.index_code = r.index_code AND y.family = r.family AND y.rule = r.rule
        WHERE y.year <= $through AND r.rowid IN (
            SELECT MAX(l.rowid) FROM loop_rule l
            WHERE l.history = 'long' AND l.standing = 'live'
            AND substr(l.run_id, length(l.run_id) - 15) > (
                SELECT MIN(substr(b.run_id, length(b.run_id) - 15)) FROM run_log b
                WHERE b.stage = 'ledger-build-long' AND b.outcome = 'ok' AND b.run_id LIKE 'ledger-build-long-' || l.index_code || '-%' AND b.started_at >= $rebuilt_on)
            GROUP BY l.index_code, l.family)
        GROUP BY r.index_code, r.family;";

    public async Task<SetupRecords> SetupRecordsAsync()
    {
        var records = new List<SetupLongRecord>();
        var longTest = new Dictionary<(string Index, string Family), (double Edge, int Picks)>();

        await using (var connection = Open())
        {
            await using var command = connection.CreateCommand();
            command.CommandText = LongTestFigures;
            command.Parameters.AddWithValue("$through", EquityBrief.Core.History.LongLedger.Through.Year);
            command.Parameters.AddWithValue("$rebuilt_on", SetupLongRecord.LongRebuiltOn.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                if (!reader.IsDBNull(2) && reader.GetInt32(2) > 0 && !reader.IsDBNull(3))
                {
                    longTest[(reader.GetString(0), reader.GetString(1))] = (reader.GetDouble(3) / reader.GetInt32(2), reader.GetInt32(2));
                }
            }
        }

        // On the operator's decision of 2026-11-18, for a month after the newest correction to the history, the correction on each
        // record whose newest run started on or after its day, which the box says beneath its figure.
        // see: A record the history was corrected under says for a month how many of its rule's trades the correction added and removed, or that none changed
        var correction = MarkRenderer.RecordCorrections[^1];
        var today = clock.SessionDateAt(clock.UtcNow);
        var shown = today >= correction.On && today < correction.On.AddMonths(1);
        var before = correction.On.ToString("yyyyMMdd", System.Globalization.CultureInfo.InvariantCulture) + "T000000Z";

        SetupLongRecord Corrected(SetupLongRecord record, string started) =>
            shown && string.CompareOrdinal(started, before) >= 0 ? record with { Correction = correction } : record;

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
                        LongEdge = longTest.TryGetValue((reader.GetString(0), reader.GetString(1)), out var tested) ? tested.Edge : null,
                        LongPicks = longTest.TryGetValue((reader.GetString(0), reader.GetString(1)), out var counted) ? counted.Picks : null,
                    },
                    reader.GetString(8)));
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
                    reader.GetString(6)));
            }
        }

        records.AddRange(Universes.Offered
            .SelectMany(choice => SetupLongRecord.FilingFamilies.Select(family => SetupLongRecord.Filings(choice.Code, family)))
            .OfType<SetupLongRecord>());

        return new SetupRecords(records);
    }
}
