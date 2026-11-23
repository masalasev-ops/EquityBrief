using System.Globalization;
using EquityBrief.Core.Loop;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The survivors as the Improvement loop page reads them for one index: each with its line, the night it passed, its newest
// state and why, the name it registered under, and the result of each pick it was walked to while it queued, from the
// sessions the bar store holds after each.
// see: A survivor that has to queue is walked each night as a record drawn among no buys and carries it when a slot opens
public sealed partial class ReadApi
{
    const string SurvivorRows = @"
        SELECT s.id, s.line, s.passed_on, t.state, t.candidate, t.why, t.at
        FROM survivor s
        JOIN survivor_state t ON t.survivor_id = s.id
        WHERE s.index_code = $index
          AND t.rowid = (SELECT u.rowid FROM survivor_state u WHERE u.survivor_id = s.id ORDER BY u.at DESC, u.rowid DESC LIMIT 1)
        ORDER BY s.passed_on, s.id;";

    const string SurvivorPicks = "SELECT session_date, ticker, typical_move FROM survivor_pick WHERE survivor_id = $survivor_id ORDER BY session_date, ticker;";

    const string SessionsFrom = "SELECT open, close FROM bar WHERE ticker = $ticker AND session_date >= $from ORDER BY session_date LIMIT $count;";

    const string RegisteredOn = "SELECT MIN(at) FROM survivor_state WHERE survivor_id = $survivor_id AND state = 'registered';";

    public async Task<IReadOnlyList<SurvivorRow>> SurvivorsAsync(string index, DateOnly? night)
    {
        var rows = new List<SurvivorRow>();

        await using var connection = Open();

        var read = new List<(long Id, string Line, DateOnly Passed, string State, string? Candidate, string Why)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SurvivorRows;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                read.Add((reader.GetInt64(0), reader.GetString(1), Day(reader.GetString(2)), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5)));
            }
        }

        foreach (var (id, text, passed, state, candidate, why) in read)
        {
            if (IdeaGrammar.Parse(text).Line is not { } line)
            {
                continue;
            }

            var results = new List<double?>();
            var nights = new HashSet<DateOnly>();

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = SurvivorPicks;
                command.Parameters.AddWithValue("$survivor_id", id);

                await using var reader = await command.ExecuteReaderAsync();
                var picks = new List<(DateOnly Session, string Ticker, double Typical)>();

                while (await reader.ReadAsync())
                {
                    picks.Add((Day(reader.GetString(0)), reader.GetString(1), reader.GetDouble(2)));
                }

                foreach (var (session, ticker, typical) in picks)
                {
                    nights.Add(session);

                    await using var bars = connection.CreateCommand();

                    bars.CommandText = SessionsFrom;
                    bars.Parameters.AddWithValue("$ticker", ticker);
                    bars.Parameters.AddWithValue("$from", Stamp(session));
                    bars.Parameters.AddWithValue("$count", line.Sessions + 2);

                    await using var walked = await bars.ExecuteReaderAsync();
                    var sessions = new List<(double, double)>();

                    while (await walked.ReadAsync())
                    {
                        sessions.Add((Core.Prices.Statistic.FromPrice(Data.Money.FromStorage(walked.GetString(0))), Core.Prices.Statistic.FromPrice(Data.Money.FromStorage(walked.GetString(1)))));
                    }

                    results.Add(Survivors.PickResult(line, typical, sessions));
                }
            }

            DateOnly? registeredOn = null;

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = RegisteredOn;
                command.Parameters.AddWithValue("$survivor_id", id);

                if (await command.ExecuteScalarAsync() is string at)
                {
                    registeredOn = DateOnly.FromDateTime(DateTime.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal));
                }
            }

            rows.Add(new SurvivorRow(id, line.Words, line.Canonical, passed, state, candidate, why, results, registeredOn,
                night is { } through ? NightsFrom(passed, through) : 0));
        }

        return rows;
    }

    // The nights a survivor has been walked: the night it passed and every session after it to the newest night.
    public static int NightsFrom(DateOnly passed, DateOnly through) =>
        through < passed ? 0 : through == passed ? 1 : Core.Bars.ExchangeClosures.SessionsBetween(passed, through).Count + 2;
}
