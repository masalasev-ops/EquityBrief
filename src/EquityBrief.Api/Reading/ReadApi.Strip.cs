using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// One series' close on its newest session on or before the night and the close of the session before it, none where
// the store holds no session before.
public sealed record StripClose(string Series, DateOnly Session, decimal Close, decimal? Before);

// The Treasury's newest stored session on or before the night, its 10-year and 3-month, and the same of the session
// before it.
public sealed record StripYields(DateOnly Session, double TenYear, double? ThreeMonth, double? TenYearBefore, double? ThreeMonthBefore);

// What the market strip reads of the night's store: each tile's series' two newest closes, the VIX's closes over the year
// before its newest, which its median is read over, and the Treasury's two newest rows.
public sealed record StripStored(IReadOnlyDictionary<string, StripClose> Closes, IReadOnlyList<decimal> VixYear, StripYields? Yields);

// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public sealed partial class ReadApi
{
    const string StripClosesOn = @"
        SELECT series, session_date, close, back FROM (
            SELECT series, session_date, close, ROW_NUMBER() OVER (PARTITION BY series ORDER BY session_date DESC) AS back
            FROM market_bar
            WHERE session_date <= $on AND series IN (SELECT value FROM json_each($names)))
        WHERE back <= 2;";

    // The VIX's closes before its newest, newest first, as many as its median is read over.
    const string VixYearOn = @"
        SELECT close FROM market_bar
        WHERE series = $vix AND session_date <= $on
        ORDER BY session_date DESC
        LIMIT $count OFFSET 1;";

    const string YieldsOn = "SELECT session_date, ten_year, three_month FROM treasury_yield WHERE session_date <= $on ORDER BY session_date DESC LIMIT 2;";

    public async Task<StripStored> StripAsync(DateOnly on, IReadOnlyList<string> series, int vixSessions)
    {
        var day = on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var newest = new Dictionary<string, (DateOnly Session, decimal Close)>(StringComparer.Ordinal);
        var before = new Dictionary<string, decimal>(StringComparer.Ordinal);

        await using var connection = Open();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = StripClosesOn;
            command.Parameters.AddWithValue("$names", System.Text.Json.JsonSerializer.Serialize(series));
            command.Parameters.AddWithValue("$on", day);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var name = reader.GetString(0);
                var close = Money.FromStorage(reader.GetString(2));

                if (reader.GetInt64(3) == 1)
                {
                    newest[name] = (DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture), close);
                }
                else
                {
                    before[name] = close;
                }
            }
        }

        var vix = new List<decimal>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = VixYearOn;
            command.Parameters.AddWithValue("$vix", MarketCloses.Vix);
            command.Parameters.AddWithValue("$on", day);
            command.Parameters.AddWithValue("$count", vixSessions);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                vix.Add(Money.FromStorage(reader.GetString(0)));
            }
        }

        var yields = new List<(DateOnly Session, double TenYear, double? ThreeMonth)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = YieldsOn;
            command.Parameters.AddWithValue("$on", day);

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                yields.Add((
                    DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                    reader.GetDouble(1),
                    reader.IsDBNull(2) ? null : reader.GetDouble(2)));
            }
        }

        return new StripStored(
            newest.ToDictionary(pair => pair.Key, pair => new StripClose(pair.Key, pair.Value.Session, pair.Value.Close, before.TryGetValue(pair.Key, out var then) ? then : null), StringComparer.Ordinal),
            vix,
            yields.Count == 0
                ? null
                : new StripYields(yields[0].Session, yields[0].TenYear, yields[0].ThreeMonth, yields.Count > 1 ? yields[1].TenYear : null, yields.Count > 1 ? yields[1].ThreeMonth : null));
    }
}
