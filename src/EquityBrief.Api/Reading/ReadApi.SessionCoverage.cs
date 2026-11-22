using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// What the History page reads of every member's readings on every session: each index's years as counted where they were
// written, and never the readings themselves.
// see: The readings for every stock on every session are built once before the reviewer, the picker's comparison reading the same plan on every stock until then
public sealed partial class ReadApi
{
    const string SessionCoverage = @"
        SELECT index_code, year, rows, sessions, night_sessions, first_session, last_session
        FROM session_reading_year
        ORDER BY index_code, year;";

    public async Task<IReadOnlyList<SessionCoverageRow>> SessionCoverageAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SessionCoverage;

        var rows = new List<SessionCoverageRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new SessionCoverageRow(reader.GetString(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt32(3), reader.GetInt32(4), reader.GetString(5), reader.GetString(6)));
        }

        return rows;
    }
}
