using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// The Ledger page's reads: the summary the ledger's writers refresh, a family's newest settled setups and one setup's
// closes. The page draws them and computes none of them.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public sealed partial class ReadApi
{
    // The calendar days a chosen setup's closes start before its session, about ten sessions.
    public const int LedgerLeadDays = 14;

    const string LedgerYearsOf = @"
        SELECT index_code, family, year, setups, live_passes, night_rows, picked, settled, result_mean, edge_mean, result_deciles, edge_deciles
        FROM ledger_summary WHERE index_code = $index ORDER BY family, year;";

    const string LedgerSettledOf = @"
        SELECT family, ticker, session_date, live_pass, end, ended_on, sessions, result, edge, entry, stop, target, trail
        FROM setup WHERE index_code = $index AND family = $family AND settled = 1
        ORDER BY session_date DESC, ticker LIMIT $limit;";

    const string LedgerSetupOf = @"
        SELECT family, ticker, session_date, live_pass, end, ended_on, sessions, result, edge, entry, stop, target, trail
        FROM setup WHERE index_code = $index AND family = $family AND ticker = $ticker AND session_date = $session;";

    const string LedgerClosesOf = @"
        SELECT session_date, close FROM bar WHERE ticker = $ticker AND session_date >= $from AND session_date <= $to
        UNION ALL
        SELECT k.session_date, k.close FROM kept_bar k
        WHERE k.ticker = $ticker AND k.session_date >= $from AND k.session_date <= $to
          AND NOT EXISTS (SELECT 1 FROM bar b WHERE b.ticker = k.ticker AND b.session_date = k.session_date)
        ORDER BY 1;";

    public async Task<IReadOnlyList<LedgerYear>> LedgerYearsAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LedgerYearsOf;
        command.Parameters.AddWithValue("$index", index);

        var years = new List<LedgerYear>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            years.Add(new LedgerYear(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : LedgerSummaries.Read(reader.GetString(10)),
                reader.IsDBNull(11) ? null : LedgerSummaries.Read(reader.GetString(11))));
        }

        return years;
    }

    public async Task<IReadOnlyList<LedgerSetupRow>> LedgerSettledAsync(string index, string family, int limit)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LedgerSettledOf;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", family);
        command.Parameters.AddWithValue("$limit", limit);

        return await LedgerRowsAsync(command);
    }

    public async Task<LedgerPath?> LedgerPathAsync(string index, string family, string ticker, DateOnly session, DateOnly newest)
    {
        await using var connection = Open();
        LedgerSetupRow? setup;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LedgerSetupOf;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            setup = (await LedgerRowsAsync(command)).SingleOrDefault();
        }

        if (setup is null)
        {
            return null;
        }

        var closes = new List<(DateOnly, decimal)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LedgerClosesOf;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$from", session.AddDays(-LedgerLeadDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$to", (setup.EndedOn ?? newest).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                closes.Add((DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), Money.FromStorage(reader.GetString(1))));
            }
        }

        return new LedgerPath(setup, closes);
    }

    static async Task<IReadOnlyList<LedgerSetupRow>> LedgerRowsAsync(SqliteCommand command)
    {
        var rows = new List<LedgerSetupRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new LedgerSetupRow(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt64(3) == 1,
                reader.GetString(4),
                reader.IsDBNull(5) ? null : DateOnly.ParseExact(reader.GetString(5), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                Money.FromStorage(reader.GetString(9)),
                Money.FromStorage(reader.GetString(10)),
                reader.IsDBNull(11) ? null : Money.FromStorage(reader.GetString(11)),
                reader.IsDBNull(12) ? null : Money.FromStorage(reader.GetString(12))));
        }

        return rows;
    }
}
