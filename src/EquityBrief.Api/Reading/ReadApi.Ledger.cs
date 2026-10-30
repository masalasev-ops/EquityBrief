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

    // Which readings go with winners for a family on an index: the rows of the newest run the walk-forward tester stored
    // for the index, in the catalogue's order, and none where no run is stored; the page works none of it.
    // see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
    const string NewestLinksOf = @"
        SELECT run_id, month, reading, setups, years, correlation, same_way, shuffled, words
        FROM loop_link
        WHERE index_code = $index AND family = $family
          AND run_id = (SELECT run_id FROM loop_link WHERE index_code = $index ORDER BY rowid DESC LIMIT 1)
        ORDER BY rowid;";

    // From 20.8 what each release answer came to for a family on an index: the rows of the newest run the walk-forward
    // tester stored them under, in the order it wrote them, and none where no run stored any; the page works none of it.
    // see: The release readings join the ledger's catalogue once their history read is complete, and their use is judged on the years after the model's cutoff
    const string NewestAnswersOf = @"
        SELECT run_id, month, cutoff, question, answer, side, setups, winners, mean_result, mean_edge
        FROM release_answer_edge
        WHERE index_code = $index AND family = $family
          AND run_id = (SELECT run_id FROM release_answer_edge WHERE index_code = $index ORDER BY rowid DESC LIMIT 1)
        ORDER BY rowid;";

    // From 20.8 the local model's cutoff the newest reading version whose history read is complete states, none before.
    const string NewestCutoff = "SELECT cutoff FROM reading_pin WHERE history_completed_at IS NOT NULL ORDER BY id DESC LIMIT 1;";

    public async Task<AnswerTable?> LedgerAnswersAsync(string index, string family)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestAnswersOf;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", family);

        (string Run, string Month, DateOnly Cutoff)? held = null;
        var rows = new List<AnswerEdge>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            held ??= (reader.GetString(0), reader.GetString(1), DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture));
            rows.Add(new AnswerEdge(
                family,
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetDouble(8),
                reader.GetDouble(9)));
        }

        return held is { } run ? new AnswerTable(run.Run, run.Month, run.Cutoff, rows) : null;
    }

    public async Task<DateOnly?> ModelCutoffAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestCutoff;

        return await command.ExecuteScalarAsync() is string day ? DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;
    }

    // From 21.6 where the readings whose sources keep no history of their own start: the first quarters fetch filing the
    // current year's trend, the first night the night asked every member's trend, the first settlement of FINRA's short
    // interest held, and the stocks holding an answer from Yahoo.
    // see: A new reading joins the ledger with its history where its source keeps one, and from the night it is first stored where it keeps none
    const string ReadingSourcesHeld = @"
        SELECT (SELECT MIN(fetched_at) FROM estimate_trend WHERE period = '0y'),
               (SELECT MIN(session_date) FROM estimate_trend_night),
               (SELECT MIN(settlement_date) FROM short_interest),
               (SELECT COUNT(DISTINCT ticker) FROM analyst_view);
    ";

    public async Task<EquityBrief.Web.Marks.ReadingSources> LedgerSourcesAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ReadingSourcesHeld;

        await using var reader = await command.ExecuteReaderAsync();

        await reader.ReadAsync();

        static DateOnly? Day(string? text) =>
            text is { Length: >= 10 } ? DateOnly.ParseExact(text[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture) : null;

        return new EquityBrief.Web.Marks.ReadingSources(
            Day(reader.IsDBNull(0) ? null : reader.GetString(0)),
            Day(reader.IsDBNull(1) ? null : reader.GetString(1)),
            Day(reader.IsDBNull(2) ? null : reader.GetString(2)),
            reader.IsDBNull(3) ? 0 : reader.GetInt32(3));
    }

    public async Task<LinkTable?> LedgerLinksAsync(string index, string family)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestLinksOf;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", family);

        string? run = null;
        string? month = null;
        var rows = new List<ReadingLink>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            run ??= reader.GetString(0);
            month ??= reader.GetString(1);
            rows.Add(new ReadingLink(
                reader.GetString(2),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetDouble(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.GetString(8)));
        }

        return run is null || month is null ? null : new LinkTable(run, month, rows);
    }

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
