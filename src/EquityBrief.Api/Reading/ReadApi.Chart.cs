using System.Globalization;
using EquityBrief.Core.Chart;
using EquityBrief.Data;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

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
