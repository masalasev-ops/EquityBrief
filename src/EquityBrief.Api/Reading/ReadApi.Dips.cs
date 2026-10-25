using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// One night of the short-term dip on one index as its step stored it: the members it read, its market setting and
// whether it left the list open, the settings it ran on, how many passed and how many it listed, and the failure that
// stopped its part where one did.
public sealed record DipNightRow(string Index, DateOnly Session, int Members, string Market, bool MarketOpen, string Settings, int Passed, int Listed, string? Fault);

// One stock on the dip's list on a night: listed with its place, or held back, for a trade still open naming the family,
// the night and the index that holds it, or for a stock another family listed that night naming that family.
public sealed record DipPickRow(DateOnly Session, string Ticker, string State, int? Place, string? ListedUnder, string? HeldIndex, string? HeldFamily, DateOnly? HeldNight);

// What the read surface reads of the dip's own rows, which its step alone writes.
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
public sealed partial class ReadApi
{
    const string DipNightOn = @"
        SELECT index_code, session_date, members, market, market_open, settings, passed, listed, fault
        FROM dip_night WHERE index_code = $index AND session_date = $on;
    ";

    const string DipResultsOn = @"
        SELECT ticker, passed, place, entry, stop, time_limit, rsi, reason, gates
        FROM dip_result WHERE index_code = $index AND session_date = $on
        ORDER BY place IS NULL, place, ticker;
    ";

    const string DipPicksOn = @"
        SELECT session_date, ticker, state, place, listed_under, held_index, held_family, held_night
        FROM dip_pick WHERE index_code = $index AND session_date = $on
        ORDER BY place IS NULL, place, ticker;
    ";

    // Every trade the dip's page list kept on an index on or before a night, its fill read where it was bought by the
    // night and its end, its result and its cost only where it ended by the night; from 22.3 each under the rule its
    // night's card was drawn by, the provisional rule's before a freeze and the live rule's after it.
    // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
    const string DipTradesUpTo = @"
        SELECT t.index_code, t.ticker, t.session_date, t.place, t.entry, t.stop, t.time_limit,
               CASE WHEN t.ended_on <= $on THEN t.ended_on END,
               CASE WHEN t.ended_on <= $on THEN t.result END,
               CASE WHEN t.ended_on <= $on THEN t.cost END,
               CASE WHEN t.bought_on <= $on THEN t.fill END,
               CASE WHEN t.ended_on <= $on THEN t.end_reason END
        FROM dip_trade t
        WHERE t.index_code = $index AND t.session_date <= $on
          AND t.rule = COALESCE((SELECT json_extract(n.settings, '$.rule') FROM dip_night n WHERE n.index_code = t.index_code AND n.session_date = t.session_date), 'provisional')
        ORDER BY t.session_date DESC, t.place, t.ticker;
    ";

    // From 22.3 each trade a dip version kept on an index on a night, as a variant's picks are read when its tab is
    // chosen: its rule, its stock, its place, its buy and stop at the listing close and no target.
    const string DipVersionPicksOn = @"
        SELECT t.rule, t.ticker, t.session_date, t.place, t.entry, t.stop
        FROM dip_trade t
        WHERE t.index_code = $index AND t.session_date = $on
          AND t.rule <> COALESCE((SELECT json_extract(n.settings, '$.rule') FROM dip_night n WHERE n.index_code = t.index_code AND n.session_date = t.session_date), 'provisional')
        ORDER BY t.rule, t.place, t.ticker;
    ";

    // From 22.3 the picks each dip version kept on an index's night, which its tab draws when chosen.
    // see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
    public async Task<IReadOnlyList<RulePickRow>> DipVersionPicksAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = DipVersionPicksOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<RulePickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new RulePickRow(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(3),
                Money.FromStorage(reader.GetString(4)),
                Money.FromStorage(reader.GetString(5)),
                null,
                null,
                "Every step of this version passed tonight."));
        }

        return rows;
    }

    // The dip's night on an index, none for a night its step did not read.
    public async Task<DipNightRow?> DipNightAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = DipNightOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync();

        return await reader.ReadAsync()
            ? new DipNightRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetInt64(4) == 1,
                reader.GetString(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetString(8))
            : null;
    }

    // Every member's answer under the dip on an index's night, in the shape an index family's answer is read in: its
    // place among those passing, its buy and stop at the listing close, its time limit as its cap, its 2-session RSI as
    // the figure it is ordered by, the first gate it failed and every gate's answer.
    public async Task<IReadOnlyList<IndexResultRow>> DipResultsAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = DipResultsOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<IndexResultRow>();

        await using var reader = await command.ExecuteReaderAsync();

        decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

        while (await reader.ReadAsync())
        {
            rows.Add(new IndexResultRow(
                reader.GetString(0),
                DipRule.Name,
                reader.GetInt64(1) == 1,
                reader.IsDBNull(2) ? null : reader.GetInt32(2),
                Price(3),
                Price(4),
                null,
                null,
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.GetString(8)));
        }

        return rows;
    }

    // The dip's list on an index's night: every stock it passed, listed or held back.
    public async Task<IReadOnlyList<DipPickRow>> DipPicksAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = DipPicksOn;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<DipPickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new DipPickRow(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1),
                reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetInt32(3),
                reader.IsDBNull(4) ? null : reader.GetString(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : DateOnly.ParseExact(reader.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture)));
        }

        return rows;
    }

    // Every trade the dip's page list kept on an index on or before a night, newest first, in the shape an index family's
    // trade is read in: bought at its fill where its next session is stored, its result at the open and its cost each in
    // the risk its listing close set.
    public async Task<IReadOnlyList<IndexTradeRow>> DipTradesAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = DipTradesUpTo;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<IndexTradeRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new IndexTradeRow(
                reader.GetString(0),
                DipRule.Name,
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetInt32(3),
                Money.FromStorage(reader.GetString(4)),
                Money.FromStorage(reader.GetString(5)),
                null,
                null,
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : DateOnly.ParseExact(reader.GetString(7), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : Money.FromStorage(reader.GetString(10)),
                reader.IsDBNull(10) ? EquityBrief.Core.Ledger.TradeFills.Waiting : null));
        }

        return rows;
    }
}
