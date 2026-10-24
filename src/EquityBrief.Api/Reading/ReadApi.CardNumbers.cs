using System.Globalization;
using EquityBrief.Core.Ledger;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Api.Reading;

// What a setup card's numbers read beside the members' ledger readings: the swing filter's stored answer of each member
// on a night, whose gates' values hold the pullback's numbers on the S&P 500, and the setups the ledger stored on an index,
// which alone carry a family's own readings.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
public sealed partial class ReadApi
{
    const string SwingGatesOn = "SELECT ticker, gates FROM gate_result WHERE session_date = $night AND gates IS NOT NULL;";

    static readonly string CatalogueColumns = string.Join(", ", LedgerReadings.All.Select(reading => "setup." + reading.Column));

    // The ledger's setups on the night, one a family and stock whichever step wrote it.
    static readonly string NightSetupsOn =
        "SELECT setup.family, setup.ticker, " + CatalogueColumns + " FROM setup WHERE setup.index_code = $index AND setup.session_date = $night;";

    // Each named stock's newest setup of a family on or before the night, the stocks named by the parameters given.
    static string NewestSetupsOf(int tickers) =>
        "SELECT setup.ticker, setup.session_date, " + CatalogueColumns
        + " FROM setup JOIN (SELECT ticker, MAX(session_date) AS newest FROM setup WHERE index_code = $index AND family = $family AND session_date <= $night AND ticker IN ("
        + string.Join(", ", Enumerable.Range(0, tickers).Select(at => "$ticker" + at.ToString(CultureInfo.InvariantCulture)))
        + ") GROUP BY ticker) AS held ON held.ticker = setup.ticker AND held.newest = setup.session_date WHERE setup.index_code = $index AND setup.family = $family;";

    // Each member's stored answer from the swing filter on the night, by ticker, as its gates' text.
    public async Task<IReadOnlyDictionary<string, string>> SwingGatesAsync(DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = SwingGatesOn;
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var read = new Dictionary<string, string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            read[reader.GetString(0)] = reader.GetString(1);
        }

        return read;
    }

    // The readings of each setup the ledger stored on an index on the night, by its family and ticker, the catalogue's in
    // its order.
    public async Task<IReadOnlyDictionary<(string Family, string Ticker), IReadOnlyList<double?>>> NightSetupReadingsAsync(string index, DateOnly night)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NightSetupsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var read = new Dictionary<(string Family, string Ticker), IReadOnlyList<double?>>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            read[(reader.GetString(0), reader.GetString(1))] = SetupReadings(reader, 2);
        }

        return read;
    }

    // Each of the stocks named, its newest setup of a family the ledger stored on an index on or before the night, by
    // ticker, with the session it was read on and its readings, the catalogue's in its order; none read where no stock is
    // named.
    public async Task<IReadOnlyDictionary<string, (DateOnly Session, IReadOnlyList<double?> Readings)>> NewestSetupsAsync(string index, string family, DateOnly night, IReadOnlyList<string> tickers)
    {
        var read = new Dictionary<string, (DateOnly Session, IReadOnlyList<double?> Readings)>(StringComparer.Ordinal);

        if (tickers.Count == 0)
        {
            return read;
        }

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = NewestSetupsOf(tickers.Count);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", family);
        command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        for (var at = 0; at < tickers.Count; at++)
        {
            command.Parameters.AddWithValue("$ticker" + at.ToString(CultureInfo.InvariantCulture), tickers[at]);
        }

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            read[reader.GetString(0)] = (DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), SetupReadings(reader, 2));
        }

        return read;
    }

    // A setup row's catalogue readings from the column given on, none where the row holds none.
    static double?[] SetupReadings(SqliteDataReader reader, int from)
    {
        var readings = new double?[LedgerReadings.Count];

        for (var at = 0; at < readings.Length; at++)
        {
            readings[at] = reader.IsDBNull(at + from) ? null : reader.GetDouble(at + from);
        }

        return readings;
    }
}
