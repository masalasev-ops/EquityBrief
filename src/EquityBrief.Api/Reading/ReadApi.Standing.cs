using System.Globalization;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// What a heavyweights card reads for its holdings' standing tonight and its return since its last rebalance: each
// stock's and each fund's close tonight, the close a look-back before it on its own series and the close of the
// rebalance, and the GICS sector of each member of an index, which a sector's members' mean is read over.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
public sealed partial class ReadApi
{
    const string StockStandingClosesOn = @"
        SELECT ticker, session_date, close, back FROM (
            SELECT ticker, session_date, close, ROW_NUMBER() OVER (PARTITION BY ticker ORDER BY session_date DESC) AS back
            FROM bar
            WHERE session_date <= $on AND ticker IN (SELECT value FROM json_each($names)))
        WHERE back = 1 OR back = $back OR session_date = $rebalance;";

    const string FundStandingClosesOn = @"
        SELECT series, session_date, close, back FROM (
            SELECT series, session_date, close, ROW_NUMBER() OVER (PARTITION BY series ORDER BY session_date DESC) AS back
            FROM market_bar
            WHERE session_date <= $on AND series IN (SELECT value FROM json_each($names)))
        WHERE back = 1 OR back = $back OR session_date = $rebalance;";

    const string MemberSectorsOn = @"
        SELECT m.ticker,
               (SELECT c.sector FROM company c WHERE c.ticker = m.ticker ORDER BY c.fetched_at DESC, c.rowid DESC LIMIT 1)
        FROM (SELECT DISTINCT ticker FROM membership
              WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)) m
        ORDER BY m.ticker;";

    // Each stock's closes a card reads tonight: its close on the night, the close the look-back's sessions before it on
    // its own bars, and its close on the rebalance; none where the store holds none.
    public Task<IReadOnlyDictionary<string, StandingCloses>> StockStandingClosesAsync(IReadOnlyList<string> tickers, DateOnly on, int lookBack, DateOnly? rebalance) =>
        StandingClosesAsync(StockStandingClosesOn, tickers, on, lookBack, rebalance);

    // Each fund's the same, read off the night's market series.
    public Task<IReadOnlyDictionary<string, StandingCloses>> FundStandingClosesAsync(IReadOnlyList<string> funds, DateOnly on, int lookBack, DateOnly? rebalance) =>
        StandingClosesAsync(FundStandingClosesOn, funds, on, lookBack, rebalance);

    async Task<IReadOnlyDictionary<string, StandingCloses>> StandingClosesAsync(string query, IReadOnlyList<string> names, DateOnly on, int lookBack, DateOnly? rebalance)
    {
        var day = on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var held = new Dictionary<string, (decimal? Now, decimal? Back, decimal? AtRebalance)>(StringComparer.Ordinal);

        if (names.Count > 0)
        {
            await using var connection = Open();
            await using var command = connection.CreateCommand();

            command.CommandText = query;
            command.Parameters.AddWithValue("$names", System.Text.Json.JsonSerializer.Serialize(names));
            command.Parameters.AddWithValue("$on", day);
            command.Parameters.AddWithValue("$back", lookBack + 1);
            command.Parameters.AddWithValue("$rebalance", rebalance?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var session = reader.GetString(1);
                var close = Money.FromStorage(reader.GetString(2));
                var back = reader.GetInt64(3);
                var row = held.GetValueOrDefault(name);

                held[name] = (
                    back == 1 && session == day ? close : row.Now,
                    back == lookBack + 1 ? close : row.Back,
                    rebalance is { } then && session == then.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ? close : row.AtRebalance);
            }
        }

        return names.Distinct(StringComparer.Ordinal).ToDictionary(
            name => name,
            name => held.TryGetValue(name, out var row) ? new StandingCloses(row.Now, row.Back, row.AtRebalance) : new StandingCloses(null, null, null),
            StringComparer.Ordinal);
    }

    // Each member of an index on a session with the GICS sector its newest company fetch files, none where it files none.
    // see: Every page names a member's sector by the GICS sector its company fetch files
    public async Task<IReadOnlyDictionary<string, string?>> MemberSectorsAsync(string index, DateOnly session)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = MemberSectorsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var sectors = new Dictionary<string, string?>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            sectors[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        return sectors;
    }
}
