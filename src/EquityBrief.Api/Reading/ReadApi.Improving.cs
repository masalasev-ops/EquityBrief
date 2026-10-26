using System.Globalization;

namespace EquityBrief.Api.Reading;

// One night of the Fundamentals family on one index as its step stored it: the members it read, how many held a scorecard,
// how many newly read filings it read and how many raised the score, what it bought and flagged, the trades it holds after
// the night, and the failure that stopped its part where one did.
public sealed record ImprovingNightRow(string Index, DateOnly Session, int Members, int Scored, int FirstRead, int Improved, int Bought, int Flagged, int Held, string Settings, string? Fault);

// One member's answer on a night: its scorecard's checks passed and applied, the day its newest filing was first filed and
// whether the night read it first, the score its filing before held with its checks where it did, its sales growth and how
// much faster it grew than the quarter before, its newest release's day and outlook where it was flagged, its status and
// why, and its growth carried from its first answer.
public sealed record ImprovingResultRow(
    string Ticker,
    int PassedChecks,
    int Applied,
    DateOnly? Filed,
    bool FirstRead,
    int? BeforePassed,
    int? BeforeApplied,
    string? BeforeChecks,
    double? Growth,
    double? GrowthChange,
    DateOnly? Released,
    string? Outlook,
    bool Flagged,
    string? Status,
    string? Reason,
    double Carried);

// One trade as stored, read as of a night: the night it was chosen, the filing and the release behind it, its fill's gap and
// its benchmark's, and its end where it ended by the night.
public sealed record ImprovingTradeRow(string Ticker, DateOnly ChosenOn, DateOnly Filed, DateOnly? Released, DateOnly? BoughtOn, double? Gap, double? BenchGap, DateOnly? EndedOn, string EndReason);

// One member's scorecard on a night as the Fundamentals card reads it: its count, its gross profit over its assets and its
// checks as stored.
public sealed record ImprovingScoreRow(string Ticker, int Passed, int Applied, double? GrossProfitability, string Checks);

// What the read surface reads of the Fundamentals family's own rows, which its step alone writes.
// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
public sealed partial class ReadApi
{
    // The rule its trades are kept under before a freeze registers one, the night's own word.
    public const string ImprovingProvisional = "provisional";

    const string ImprovingNightOn = @"
        SELECT index_code, session_date, members, scored, first_read, improved, bought, flagged, held, settings, fault
        FROM improving_night WHERE rule = $rule AND index_code = $index AND session_date = $on;
    ";

    const string ImprovingResultsOn = @"
        SELECT ticker, passed_checks, applied, filed, first_read, before_passed, before_applied, before_checks, growth,
               growth_change, released, outlook, flagged, status, reason, carried
        FROM improving_result WHERE rule = $rule AND index_code = $index AND session_date = $on
        ORDER BY ticker;
    ";

    // Every trade chosen by the night that had not ended before it, its buy only where it was bought by the night and its
    // end only where it ended by the night.
    const string ImprovingTradesOn = @"
        SELECT ticker, chosen_on, filed, released,
               CASE WHEN bought_on <= $on THEN bought_on END, CASE WHEN bought_on <= $on THEN gap END,
               CASE WHEN bought_on <= $on THEN bench_gap END,
               CASE WHEN ended_on <= $on THEN ended_on END,
               CASE WHEN ended_on <= $on THEN end_reason ELSE 'open' END
        FROM improving_trade
        WHERE rule = $rule AND index_code = $index AND chosen_on <= $on AND (ended_on IS NULL OR ended_on >= $on)
        ORDER BY chosen_on, ticker;
    ";

    // The earnings reports the calendar holds after a night and through a later day.
    const string ImprovingReportsBetween = @"
        SELECT DISTINCT ticker, event_date FROM calendar
        WHERE kind = 'earnings' AND event_date > $after AND event_date <= $through;
    ";

    const string ImprovingScoresOn = @"
        SELECT ticker, passed, applied, gross_profitability, checks FROM fundamental_score
        WHERE index_code = $index AND session_date = $on;
    ";

    // The Fundamentals family's night on an index, none for a night its step did not read.
    public async Task<ImprovingNightRow?> ImprovingNightAsync(string index, DateOnly on, string rule = ImprovingProvisional)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ImprovingNightOn;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new ImprovingNightRow(
                reader.GetString(0),
                ImprovingDate(reader.GetString(1)),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10))
            : null;
    }

    // Every member's answer on a night, by ticker.
    public async Task<IReadOnlyList<ImprovingResultRow>> ImprovingResultsAsync(string index, DateOnly on, string rule = ImprovingProvisional)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ImprovingResultsOn;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ImprovingResultRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new ImprovingResultRow(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.IsDBNull(3) ? null : ImprovingDate(reader.GetString(3)),
                reader.GetInt64(4) == 1,
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : ImprovingDate(reader.GetString(10)),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.GetInt64(12) == 1,
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.GetDouble(15)));
        }

        return rows;
    }

    // Every trade open on a night or ended on it, read as of the night.
    public async Task<IReadOnlyList<ImprovingTradeRow>> ImprovingTradesAsync(string index, DateOnly on, string rule = ImprovingProvisional)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ImprovingTradesOn;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ImprovingTradeRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new ImprovingTradeRow(
                reader.GetString(0),
                ImprovingDate(reader.GetString(1)),
                ImprovingDate(reader.GetString(2)),
                reader.IsDBNull(3) ? null : ImprovingDate(reader.GetString(3)),
                reader.IsDBNull(4) ? null : ImprovingDate(reader.GetString(4)),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : ImprovingDate(reader.GetString(7)),
                reader.GetString(8)));
        }

        return rows;
    }

    // Every earnings report the calendar holds after a night and through a later day, by stock.
    public async Task<IReadOnlyList<(string Ticker, DateOnly Date)>> ImprovingReportsAsync(DateOnly after, DateOnly through)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ImprovingReportsBetween;
        command.Parameters.AddWithValue("$after", Day(after));
        command.Parameters.AddWithValue("$through", Day(through));

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, DateOnly)>();

        while (await reader.ReadAsync())
        {
            rows.Add((reader.GetString(0), ImprovingDate(reader.GetString(1))));
        }

        return rows;
    }

    // Every member's scorecard on an index on a night.
    public async Task<IReadOnlyList<ImprovingScoreRow>> ImprovingScoresAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ImprovingScoresOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<ImprovingScoreRow>();

        while (await reader.ReadAsync())
        {
            rows.Add(new ImprovingScoreRow(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetDouble(3), reader.GetString(4)));
        }

        return rows;
    }

    static DateOnly ImprovingDate(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
