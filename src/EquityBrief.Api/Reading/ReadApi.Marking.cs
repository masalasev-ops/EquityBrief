using System.Globalization;
using EquityBrief.Api.Passes;
using EquityBrief.Core.History;
using EquityBrief.Data;
using EquityBrief.Web.App;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// The marking page: every unexplained jump the data check found and every end no filing settles, each with the sessions
// around it, its volume against its median and the filings its filer made beside it, the mark the page proposes and any mark
// the operator gave it already; the long ledger's setups counted as no result beside them; and the operator's confirm, one
// row of the marks a mark under the page's own header, a second word on a session changing nothing. Nothing is marked
// without a confirm.
// see: The marking page proposes a mark for each unexplained jump and unsettled end from its evidence, and writes none without the operator's confirm
public sealed partial class ReadApi
{
    const string UnexplainedJumpsToMark = "SELECT ticker, session_date, era, says FROM data_check WHERE class = 'jump' AND kind = 'unexplained' ORDER BY ticker, session_date;";

    const string MarksHeld = "SELECT ticker, session_date, kind FROM session_mark;";

    const string BarsAroundAMark = @"
        SELECT session_date, close, volume FROM long_bar WHERE ticker = $ticker AND session_date BETWEEN $from AND $to
        UNION
        SELECT session_date, close, volume FROM bar WHERE ticker = $ticker AND session_date BETWEEN $from AND $to
        ORDER BY session_date;";

    const string FilingsAroundAMark = "SELECT filed, form, items FROM long_filing WHERE ticker = $ticker AND filed BETWEEN $from AND $to ORDER BY filed;";

    // From the 24.19 correction what a history-ends mark's evidence reads: the code's spans on every index, its traded sessions
    // under its other marks, its filer's filings and the S&P 500's sessions; and each ruling given.
    const string HistoryEndsToSettle = "SELECT ticker, MIN(session_date) FROM session_mark WHERE kind = 'history ends' GROUP BY ticker ORDER BY ticker;";

    const string SpansOfACode = "SELECT index_code, joined, \"left\" FROM long_span WHERE ticker = $ticker;";

    static readonly string TradedOfACode = $@"
        SELECT session_date, close, volume FROM long_bar WHERE ticker = $ticker AND volume > 0 AND {Core.Chart.SessionMarking.AsMarked}
        UNION ALL
        SELECT session_date, close, volume FROM bar WHERE ticker = $ticker AND volume > 0 AND {Core.Chart.SessionMarking.AsMarked}
            AND session_date NOT IN (SELECT held.session_date FROM long_bar AS held WHERE held.ticker = $ticker)
        ORDER BY session_date;";

    const string FilingsOfACode = "SELECT filed, form FROM long_filing WHERE ticker = $ticker;";

    const string LongSessions = "SELECT session_date FROM long_market_bar WHERE series = 'GSPC' ORDER BY session_date;";

    const string MarksRuled = "SELECT ticker, session_date, ruling FROM mark_ruling;";

    const string SetupsCountedAsNoResult = "SELECT index_code, family, ticker, session_date, edge FROM long_outlier ORDER BY index_code, family, session_date, ticker;";

    public async Task<MarkingView> MarkingAsync()
    {
        var ends = (await LongPricesAsync()).Unsettled;

        await using var connection = Open();

        var held = new Dictionary<(string, string), string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MarksHeld;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                held[(reader.GetString(0), reader.GetString(1))] = reader.GetString(2);
            }
        }

        var jumps = new List<(string Ticker, DateOnly Session, string? Era, string? Says)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = UnexplainedJumpsToMark;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                jumps.Add((reader.GetString(0), Day(reader.GetString(1)), reader.IsDBNull(2) ? null : reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
            }
        }

        var jumpRows = new List<MarkingRow>();

        foreach (var (ticker, session, era, says) in jumps)
        {
            var evidence = await EvidenceAsync(connection, ticker, session);
            var (kind, proposed) = MarkProposals.ForJump(session, evidence);

            jumpRows.Add(new MarkingRow(MarkingRow.Jump, ticker, session, era, says, evidence, kind, proposed, held.GetValueOrDefault((ticker, Text(session)))));
        }

        var endRows = new List<MarkingRow>();

        foreach (var line in ends)
        {
            var parts = line.Split(' ', 2);

            if (parts.Length < 2 || !DateOnly.TryParseExact(parts[1].Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last))
            {
                continue;
            }

            var evidence = await EvidenceAsync(connection, parts[0], last);
            var (kind, proposed) = MarkProposals.ForEnd(last);

            endRows.Add(new MarkingRow(MarkingRow.End, parts[0], last, null, null, evidence, kind, proposed, held.GetValueOrDefault((parts[0], Text(last)))));
        }

        var outlying = new List<OutlyingRow>();

        if (await TableHeldAsync(connection, "long_outlier"))
        {
            await using var command = connection.CreateCommand();
            command.CommandText = SetupsCountedAsNoResult;

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                outlying.Add(new OutlyingRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), Day(reader.GetString(3)), reader.GetDouble(4)));
            }
        }

        return new MarkingView(jumpRows, endRows, outlying) { ToRule = await MarksToRuleAsync(connection) };
    }

    // From the 24.19 correction every odd history-ends mark the evidence leaves to the operator, its filer's reports and its
    // traded prices read through the long reader's own tests, with any ruling given; a mark the evidence settles is not
    // listed.
    // see: A history-ends mark ends only the member span it falls in, and a mark inside a span the stock trades on past is settled by its filer and its prices
    async Task<IReadOnlyList<MarkToRule>> MarksToRuleAsync(SqliteConnection connection)
    {
        if (!await TableHeldAsync(connection, "mark_ruling"))
        {
            return [];
        }

        var ends = new List<(string Ticker, DateOnly Session)>();
        var calendar = new List<DateOnly>();
        var ruled = new Dictionary<(string, DateOnly), string>();

        await using (var command = MarkRead(connection,HistoryEndsToSettle, []))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ends.Add((reader.GetString(0), Day(reader.GetString(1))));
            }
        }

        await using (var command = MarkRead(connection,LongSessions, []))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                calendar.Add(Day(reader.GetString(0)));
            }
        }

        await using (var command = MarkRead(connection,MarksRuled, []))
        await using (var reader = await command.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                ruled[(reader.GetString(0), Day(reader.GetString(1)))] = reader.GetString(2);
            }
        }

        var rows = new List<MarkToRule>();

        foreach (var (ticker, session) in ends)
        {
            var spans = new List<(string Index, DateOnly Joined, DateOnly? Left)>();
            var traded = new List<Core.Sweep.SweepBar>();
            var filings = new List<(DateOnly, string)>();

            await using (var command = MarkRead(connection,SpansOfACode, [("$ticker", ticker)]))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    spans.Add((reader.GetString(0), Day(reader.GetString(1)), reader.IsDBNull(2) ? null : Day(reader.GetString(2))));
                }
            }

            await using (var command = MarkRead(connection,TradedOfACode, [("$ticker", ticker)]))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var close = Money.FromStorage(reader.GetString(1));

                    traded.Add(new Core.Sweep.SweepBar(Day(reader.GetString(0)), close, close, close, reader.GetInt64(2), close));
                }
            }

            if (!LongMarks.Odd(session, spans, [.. traded.Select(bar => bar.Session)]))
            {
                continue;
            }

            await using (var command = MarkRead(connection,FilingsOfACode, [("$ticker", ticker)]))
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    filings.Add((Day(reader.GetString(0)), reader.GetString(1)));
                }
            }

            var filer = LongMarks.FilerOf(session, filings);
            var price = LongMarks.PriceOf(traded, session, calendar);

            if (LongMarks.Settle(filer, price) != LongMarks.ToOperator)
            {
                continue;
            }

            rows.Add(new MarkToRule(
                ticker,
                session,
                [.. spans.Select(span => span.Index).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                filer,
                price,
                await EvidenceAsync(connection, ticker, session),
                ruled.GetValueOrDefault((ticker, session))));
        }

        return rows;
    }

    static SqliteCommand MarkRead(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    // From the 24.19 correction the operator's rulings on the marks the evidence leaves to them, each written once for a mark
    // the page lists and a ruling the reader reads, with what the two tests said beside it; a mark already ruled keeps its
    // first ruling; what was written and what was refused is said back.
    public async Task<(bool Written, string Line)> RuleMarksAsync(IReadOnlyList<(string Ticker, string Session, string Ruling)> chosen)
    {
        if (chosen.Count == 0)
        {
            return (false, "Nothing was ruled: the confirm named no mark.");
        }

        await using var connection = Open();

        var listed = (await MarksToRuleAsync(connection)).ToDictionary(row => (row.Ticker, Text(row.Session)));
        var at = clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture);
        var written = 0;
        var refused = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        foreach (var (ticker, session, ruling) in chosen)
        {
            if (!listed.TryGetValue((ticker, session), out var row) || ruling is not (LongMarks.ReadThrough or LongMarks.Kept))
            {
                refused++;

                continue;
            }

            await using var command = Command(connection, transaction, InsertRuling,
                [("$ticker", ticker), ("$session", session), ("$ruling", ruling), ("$filer", row.Filer), ("$price", row.Price), ("$ruled_at", at)]);

            written += await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        var kept = chosen.Count - refused - written;

        return (written > 0, FormattableString.Invariant($"{written} mark(s) ruled{(kept > 0 ? FormattableString.Invariant($", {kept} ruled already and left as they were") : string.Empty)}{(refused > 0 ? FormattableString.Invariant($", {refused} refused: not on the page's list or not read through or kept") : string.Empty)}."));
    }

    // The operator's confirm: each mark one row, written only for a session the page lists and a kind the marks hold, with
    // the proposal's words where the mark is the one proposed and words naming both where the operator chose another; a
    // session already marked keeps its first word; what was written and what was refused is said back.
    public async Task<(bool Written, string Line)> ConfirmMarksAsync(IReadOnlyList<(string Ticker, string Session, string Kind)> chosen)
    {
        if (chosen.Count == 0)
        {
            return (false, "Nothing was marked: the confirm named no mark.");
        }

        var listed = await MarkingAsync();
        var onThePage = listed.Jumps.Concat(listed.Ends).GroupBy(row => (row.Ticker, Text(row.Session))).ToDictionary(group => group.Key, group => group.First());
        var marks = chosen
            .Select(mark => (mark.Ticker, mark.Session, mark.Kind, Says: onThePage.TryGetValue((mark.Ticker, mark.Session), out var row)
                ? mark.Kind == row.Proposed ? row.ProposedSays : FormattableString.Invariant($"{mark.Kind}, on the operator's word on the marking page, where {row.Proposed} was proposed")
                : string.Empty))
            .ToArray();
        var refused = marks.Count(mark => !onThePage.ContainsKey((mark.Ticker, mark.Session)) || !MarkProposals.Kinds.Contains(mark.Kind, StringComparer.Ordinal) || string.IsNullOrWhiteSpace(mark.Says));
        var ruled = clock.UtcNow.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture);
        var runId = "marking-page-" + clock.UtcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var written = 0;

        await using var connection = Open();
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync();

        foreach (var mark in marks.Where(mark => onThePage.ContainsKey((mark.Ticker, mark.Session)) && MarkProposals.Kinds.Contains(mark.Kind, StringComparer.Ordinal) && !string.IsNullOrWhiteSpace(mark.Says)))
        {
            await using var command = Command(connection, transaction, InsertMark,
            [
                ("$ticker", mark.Ticker), ("$session", mark.Session), ("$kind", mark.Kind), ("$says", mark.Says.Trim()), ("$ruled", ruled), ("$run_id", runId),
            ]);

            written += await command.ExecuteNonQueryAsync();
        }

        await transaction.CommitAsync();

        return (written > 0, FormattableString.Invariant($"{written} mark(s) written{(marks.Length - refused - written > 0 ? FormattableString.Invariant($", {marks.Length - refused - written} already marked and left as they were") : string.Empty)}{(refused > 0 ? FormattableString.Invariant($", {refused} refused: not on the page's lists or not a kind the marks hold") : string.Empty)}."));
    }

    // A session's evidence: the five sessions each side with each close and volume, its median volume over the 50 sessions
    // before it, and its filer's filings within three days either side.
    static async Task<MarkEvidence> EvidenceAsync(SqliteConnection connection, string ticker, DateOnly session)
    {
        var bars = new List<(DateOnly Session, double Close, long Volume)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = BarsAroundAMark;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$from", Text(session.AddDays(-(MarkProposals.MedianSessions * 7 / 5) - 14)));
            command.Parameters.AddWithValue("$to", Text(session.AddDays(MarkProposals.AroundSessions * 7 / 5 + 7)));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                bars.Add((Day(reader.GetString(0)), Core.Prices.Statistic.FromPrice(Money.FromStorage(reader.GetString(1))), reader.GetInt64(2)));
            }
        }

        var at = bars.FindIndex(bar => bar.Session >= session);
        var place = at < 0 ? bars.Count : at;
        var before = bars.Take(place).TakeLast(MarkProposals.MedianSessions).Select(bar => bar.Volume * 1.0).Order().ToArray();
        double? median = before.Length == 0 ? null : before.Length % 2 == 1 ? before[before.Length / 2] : (before[(before.Length / 2) - 1] + before[before.Length / 2]) / 2;
        var around = bars.Skip(Math.Max(0, place - MarkProposals.AroundSessions)).Take((2 * MarkProposals.AroundSessions) + 1).ToArray();
        var filings = new List<(DateOnly Filed, string Form, string? Items)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = FilingsAroundAMark;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$from", Text(session.AddDays(-MarkProposals.FilingDays)));
            command.Parameters.AddWithValue("$to", Text(session.AddDays(MarkProposals.FilingDays)));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                filings.Add((Day(reader.GetString(0)), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        return new MarkEvidence(around, median, filings);
    }

    static async Task<bool> TableHeldAsync(SqliteConnection connection, string table)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name;";
        command.Parameters.AddWithValue("$name", table);

        return Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) > 0;
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
