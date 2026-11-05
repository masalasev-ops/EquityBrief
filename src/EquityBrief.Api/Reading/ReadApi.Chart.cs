using System.Globalization;
using EquityBrief.Core.Chart;
using EquityBrief.Data;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// The bars the chart page's weekly and monthly charts read before the store's year: the newest pull holding the stock's
// first stored session, that pull's sessions before it, and the pull's close on that session, which with the store's own sets
// the scale; or why none is read.
public sealed record PulledSeries(string? Pull, decimal? PulledClose, IReadOnlyList<SessionBar> Sessions, string? Reason);

// The chart page's read of the history pulled before the store's year, one stock's rows by the table's key, made only
// when the page draws a weekly or monthly chart, so the daily chart reads no pulled bar.
// see: The history pulled before the store's year sits apart from its bars under the pull that wrote it, read for the name and chart pages' figures alone and removed whole by that pull
public sealed partial class ReadApi
{
    public const string NothingPulled = "no history was pulled for it before its first stored session";
    public const string NoCloseToScaleBy = "the pull holds no close on its first stored session to set the scale by";

    // The newest pull holding the stock's first stored session, since from the 22.11 ruling a pull keeps its whole span
    // beside an earlier pull's rows.
    // see: A pull keeps its whole span beside an earlier pull's rows, and every reader reads the newest pull holding what it reads
    const string PulledHolding = "SELECT pull, close FROM pulled_bar WHERE ticker = $ticker AND session_date = $first ORDER BY pull DESC LIMIT 1;";

    const string AnyPulled = "SELECT 1 FROM pulled_bar WHERE ticker = $ticker LIMIT 1;";

    const string PulledBefore = @"
        SELECT session_date, open, high, low, close, volume FROM pulled_bar
        WHERE ticker = $ticker AND pull = $pull AND session_date < $first AND " + SessionMarking.FromItsStart + @"
        ORDER BY session_date;";

    // The newest night's notes of one stock, each a real event the chart check confirmed, and the operator's word on its sessions.
    // see: A real event the chart check confirms is a line beneath the chart saying what happened on that date
    // see: The operator's word on a session of the pull is a mark beside it and no pulled row moves, and only the chart's reads and the chart check read it until phase 23's restart
    const string NotesOfStock = @"
        SELECT session_date, kind, says FROM chart_check
        WHERE ticker = $ticker AND flag = 'note' AND night = (SELECT MAX(night) FROM chart_check)
        UNION ALL
        SELECT session_date, kind, says FROM session_mark
        WHERE ticker = $ticker
        ORDER BY session_date DESC;";

    const string WaitsOfStock = "SELECT ex_date, amount, asked_on FROM dividend_wait WHERE ticker = $ticker ORDER BY ex_date;";

    // see: A dividend the provider's answer does not yet carry is asked for again on the five nights after and weekly, and the stock's pages and the run page say so while it waits
    public async Task<IReadOnlyList<DividendWait>> DividendWaitsAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = WaitsOfStock;
        command.Parameters.AddWithValue("$ticker", ticker);

        var waits = new List<DividendWait>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            waits.Add(new DividendWait(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1)),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return waits;
    }

    const string EntryColumns = "ticker, session_date, close, typical_day, stretch_percent, stretch_days, stretch_share, gain_twenty, rsi, largest_gain, volume_ratio, gap_high, gap_low, highest_since, history_start, older_low, older_high, older_on, older_days, release_on, one_off_sentence, one_off_amount, one_off_tied, one_off_direction, beat";

    static EquityBrief.Core.EntryCheck.EntryReading EntryRow(SqliteDataReader reader)
    {
        static double? Figure(SqliteDataReader row, int at) => row.IsDBNull(at) ? null : row.GetDouble(at);
        static decimal? Price(SqliteDataReader row, int at) => row.IsDBNull(at) ? null : Money.FromStorage(row.GetString(at));
        static DateOnly? On(SqliteDataReader row, int at) => row.IsDBNull(at) ? null : DateOnly.ParseExact(row.GetString(at), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new(
            reader.GetString(0),
            DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            Money.FromStorage(reader.GetString(2)),
            Figure(reader, 3),
            Figure(reader, 4),
            Figure(reader, 5),
            Figure(reader, 6),
            Figure(reader, 7),
            Figure(reader, 8),
            reader.IsDBNull(9) ? null : reader.GetInt64(9) == 1,
            Figure(reader, 10),
            Price(reader, 11),
            Price(reader, 12),
            On(reader, 13),
            On(reader, 14),
            Price(reader, 15),
            Price(reader, 16),
            On(reader, 17),
            Figure(reader, 18),
            On(reader, 19),
            reader.IsDBNull(20) ? null : reader.GetString(20),
            Price(reader, 21),
            reader.IsDBNull(22) ? null : reader.GetInt64(22) == 1,
            reader.IsDBNull(23) ? null : (int)reader.GetInt64(23),
            Price(reader, 24));
    }

    // The entry check's reading of a stock on a night, and of every member on one for the cards.
    // see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
    public async Task<EquityBrief.Core.EntryCheck.EntryReading?> EntryReadingOfAsync(string ticker, DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT " + EntryColumns + " FROM entry_reading WHERE ticker = $ticker AND session_date = $session;";
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? EntryRow(reader) : null;
    }

    public async Task<IReadOnlyDictionary<string, EquityBrief.Core.EntryCheck.EntryReading>> EntryReadingsOnAsync(DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT " + EntryColumns + " FROM entry_reading WHERE session_date = $session;";
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var readings = new Dictionary<string, EquityBrief.Core.EntryCheck.EntryReading>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var reading = EntryRow(reader);

            readings[reading.Ticker] = reading;
        }

        return readings;
    }

    const string LateOfStock = @"
        SELECT ticker, session_date, stage, asked_at, outcome, detail, qualified FROM late_bar_ask
        WHERE ticker = $ticker AND session_date = $session AND outcome = 'arrived'
        ORDER BY stage;";

    const string LateOnNight = @"
        SELECT ticker, session_date, stage, asked_at, outcome, detail, qualified FROM late_bar_ask
        WHERE session_date = $session
        ORDER BY ticker, stage DESC;";

    // The names split for the first time on a night, none of those the first read kept as what stood already.
    // see: A name split for the first time is listed on the run page the night it first appears
    public async Task<IReadOnlyList<SplitNameRow>> SplitNamesOnAsync(DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT name, spaced, ticker, source FROM split_name WHERE first_night = $session AND baseline = 0 ORDER BY name;";
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var names = new List<SplitNameRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            names.Add(new SplitNameRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        }

        return names;
    }

    static LateBarRow LateRow(SqliteDataReader reader) => new(
        reader.GetString(0),
        DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
        reader.GetString(2),
        reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.GetString(4),
        reader.GetString(5),
        EquityBrief.Core.Bars.LateBars.Qualified(reader.IsDBNull(6) ? null : reader.GetString(6)));

    // The stock's bar of the night where one of the asks wrote it, which the stock's page says.
    public async Task<LateBarRow?> LateBarOfAsync(string ticker, DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LateOfStock;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync() ? LateRow(reader) : null;
    }

    // Every ask of the night, which the run page states one by one.
    public async Task<IReadOnlyList<LateBarRow>> LateBarsOnAsync(DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = LateOnNight;
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var asks = new List<LateBarRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            asks.Add(LateRow(reader));
        }

        return asks;
    }

    public async Task<IReadOnlyList<ChartNote>> ChartNotesAsync(string ticker)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NotesOfStock;
        command.Parameters.AddWithValue("$ticker", ticker);

        var notes = new List<ChartNote>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            notes.Add(new ChartNote(DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture), reader.GetString(1), reader.GetString(2)));
        }

        return notes;
    }

    // Every finding of the newest night and the one before it, which the proof page reads.
    // see: The chart check reads every member's chart after the bars land and asks nothing of the network, and lists what it finds the morning it appears
    const string ChecksOfNights = @"
        SELECT night, ticker, flag, kind, session_date, says FROM chart_check
        WHERE flag <> 'note' AND night IN (SELECT DISTINCT night FROM chart_check ORDER BY night DESC LIMIT 2)
        ORDER BY night DESC, ticker, flag, session_date DESC;";

    public async Task<IReadOnlyList<ChartProofRow>> ChartChecksAsync()
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ChecksOfNights;

        var rows = new List<ChartProofRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new ChartProofRow(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5)));
        }

        return rows;
    }

    public async Task<PulledSeries> PulledSeriesAsync(string ticker, DateOnly first)
    {
        await using var connection = Open();
        var stamp = first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        string? pull = null;
        decimal? close = null;

        await using (var holding = connection.CreateCommand())
        {
            holding.CommandText = PulledHolding;
            holding.Parameters.AddWithValue("$ticker", ticker);
            holding.Parameters.AddWithValue("$first", stamp);

            await using var reader = await holding.ExecuteReaderAsync();

            if (await reader.ReadAsync())
            {
                pull = reader.GetString(0);
                close = Money.FromStorage(reader.GetString(1));
            }
        }

        if (pull is null || close is not > 0)
        {
            await using var any = connection.CreateCommand();

            any.CommandText = AnyPulled;
            any.Parameters.AddWithValue("$ticker", ticker);

            return new PulledSeries(null, null, [], await any.ExecuteScalarAsync() is null ? NothingPulled : NoCloseToScaleBy);
        }

        await using var before = connection.CreateCommand();

        before.CommandText = PulledBefore;
        before.Parameters.AddWithValue("$ticker", ticker);
        before.Parameters.AddWithValue("$pull", pull);
        before.Parameters.AddWithValue("$first", stamp);

        var sessions = new List<SessionBar>();

        await using (var reader = await before.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                sessions.Add(new SessionBar(
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    Money.FromStorage(reader.GetString(1)),
                    Money.FromStorage(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3)),
                    Money.FromStorage(reader.GetString(4)),
                    reader.GetInt64(5)));
            }
        }

        return new PulledSeries(pull, close, sessions, sessions.Count == 0 ? NothingPulled : null);
    }
}
