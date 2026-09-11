using EquityBrief.Core.Prices;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Swings;
using EquityBrief.Data;
using EquityBrief.Data.Swings;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ladders;

// The trend classifier. Reads the indicators and the swings for one name as of
// one session and returns the label; it writes nothing.
//
// The catalogue's Writes cell says so in words, and it says who the label goes
// to: the ladder builder, which is the single writer of the row the label sits
// on. It said the facts assembler until 4.0, which is built at 5.3, a phase
// after the screen that draws the label.
// see: The trend classifier returns its label to the ladder builder
//
// It makes no request and calls no model.
// see: The nightly run is arithmetic only
//
// The rule is in TrendSeries. What is here is the two reads.
// see: Code owns every number
public sealed class TrendClassifier : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
        ],
        Feeds: []);

    const string AverageFor = @"
        SELECT value
        FROM indicator
        WHERE ticker = $ticker AND session_date = $as_of AND name = $name AND value IS NOT NULL;
    ";

    readonly string databaseFile;

    public TrendClassifier(string databaseFile) => this.databaseFile = databaseFile;

    // The label for one name as of one session, over a connection the caller
    // already holds. The ladder builder is inside a per-name loop when it asks,
    // so opening a second connection per name would be a connection per name for
    // a read that costs nothing.
    public static async Task<Trend> ForAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        decimal close,
        CancellationToken cancellation = default)
    {
        var shortAverage = await AverageAsync(connection, ticker, asOf, IndicatorSeries.Sma50, cancellation);
        var longAverage = await AverageAsync(connection, ticker, asOf, IndicatorSeries.Sma200, cancellation);

        // As of the date, through the reader the level builder uses, so the rule
        // that a component cannot see a swing confirmed later lives in one place.
        // A second copy of the clause here would be correct, untested and
        // invisible until something asked for an earlier date.
        var swings = StoredSwings.AsOf(connection, ticker, asOf);

        return TrendSeries.For(close, shortAverage, longAverage, swings);
    }

    // The instance path, for a caller holding only a file. Used by the suite and
    // by anything that wants one name's label without a night around it.
    public async Task<Trend> ForAsync(
        string ticker,
        DateOnly asOf,
        decimal close,
        CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        return await ForAsync(connection, ticker, asOf, close, cancellation);
    }

    // An average is REAL in storage because it is a statistic about prices, and
    // it is compared here against a close, which is a decimal. The crossing is
    // explicit and named, as the level builder's is.
    static async Task<decimal?> AverageAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        string name,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AverageFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$name", name);

        var value = await command.ExecuteScalarAsync(cancellation);

        return value is null or DBNull ? null : Statistic.ToPrice((double)value);
    }
}
