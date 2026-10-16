using System.Globalization;
using EquityBrief.Core.Chart;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// The bars the chart page's weekly and monthly charts read before the store's year: the pull holding the stock's first
// stored session, that pull's sessions before it, and the pull's close on that session, which with the store's own sets
// the scale; or why none is read.
public sealed record PulledSeries(string? Pull, decimal? PulledClose, IReadOnlyList<SessionBar> Sessions, string? Reason);

// The chart page's read of the history pulled before the store's year, one stock's rows by the table's key, made only
// when the page draws a weekly or monthly chart, so the daily chart reads no pulled bar.
// see: The history pulled before the store's year sits apart from its bars under the pull that wrote it, read for the name and chart pages' figures alone and removed whole by that pull
public sealed partial class ReadApi
{
    public const string NothingPulled = "no history was pulled for it before its first stored session";
    public const string NoCloseToScaleBy = "the pull holds no close on its first stored session to set the scale by";

    const string PulledHolding = "SELECT pull, close FROM pulled_bar WHERE ticker = $ticker AND session_date = $first;";

    const string AnyPulled = "SELECT 1 FROM pulled_bar WHERE ticker = $ticker LIMIT 1;";

    const string PulledBefore = @"
        SELECT session_date, open, high, low, close, volume FROM pulled_bar
        WHERE ticker = $ticker AND pull = $pull AND session_date < $first
        ORDER BY session_date;";

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
