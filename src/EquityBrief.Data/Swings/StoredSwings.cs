using System.Globalization;
using EquityBrief.Core.Swings;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Data.Swings;

// Reading swings as of a date.
//
// This exists as its own thing, before anything reads it, because the reading is
// where the mistake lives. Writing a swing is arithmetic over seven bars and
// there is one way to get it wrong; reading one is a filter, and the obvious
// filter is the wrong one.
//
// A swing dated the tenth is not knowable on the tenth. It becomes knowable on
// the thirteenth, when the third session after it closes and the lookback
// completes. So a component asking what the chart looked like on the tenth, a
// backtest walking a year forward, or a forward return measured from a listing,
// has to filter on `confirmed_on` and not on `session_date`. Filtering on
// `session_date` returns a peak nobody could have seen, and the result is a
// backtest that finds an edge which existed only in hindsight. Nothing about the
// stored row makes that error visible afterwards: the query returns rows, the
// arithmetic runs, the number looks like a number.
//
// One filter and not two. `confirmed_on` is never before `session_date`, so a
// row confirmed by a date is a row whose session is at or before it as well, and
// adding the second condition would suggest the two are independent.
public static class StoredSwings
{
    const string ConfirmedBy = @"
        SELECT session_date, direction, price, confirmed_on
        FROM swing
        WHERE ticker = $ticker AND confirmed_on <= $as_of
        ORDER BY session_date, direction;
    ";

    public static IReadOnlyList<Swing> AsOf(SqliteConnection connection, string ticker, DateOnly asOf)
    {
        using var command = connection.CreateCommand();

        command.CommandText = ConfirmedBy;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var swings = new List<Swing>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            swings.Add(new Swing(
                Date(reader.GetString(0)),
                reader.GetString(1),
                Money.FromStorage(reader.GetString(2)),
                Date(reader.GetString(3))));
        }

        return swings;
    }

    static DateOnly Date(string stored) =>
        DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
