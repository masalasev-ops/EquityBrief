using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// From the 23.10 correction of 2026-11-13 a live rule's trade kept under a plan that buys at a price, as of a night: the
// family and stock, the night that listed it and its place, the listing close and the rule's own stop, target, trail and
// limit, the plan's order, and what became of it by the night: filled at its price on a session, ended unfilled at its
// window's last session, or neither, waiting; and once filled, its end, result and sale where they came by the night.
// see: A live rule's trade under a plan is drawn on every page as its plan made it, and its waiting orders are a group of their own
public sealed record PlannedTradeRow(
    string Owner,
    string Index,
    string Family,
    string Ticker,
    DateOnly Listed,
    int Place,
    decimal Entry,
    decimal? Stop,
    decimal? Target,
    decimal? Trail,
    int Cap,
    PlanOrder Order,
    DateOnly? FilledOn,
    decimal? Fill,
    bool NotFilled,
    DateOnly? EndedOn,
    double? Result,
    decimal? Sale)
{
    // Whether the rule keeping it is its family's live rule on its index, whose trades every page draws.
    public bool Live => Owner.StartsWith(EquityBrief.Core.Candidates.FamilyRecords.LivePrefix, StringComparison.Ordinal);
}

public sealed partial class ReadApi
{
    // The S&P 500's registered rules' trades kept under a plan, their fill and sale as the fill step stored them; the S&P
    // 400's and 600's the same; and every index's dip rules', the dip's own step storing a fill and an end once the trade
    // ends. A fill or an end after the night asked for is read as not yet come.
    const string PlannedUpTo = @"
        SELECT t.candidate, 'GSPC', t.family, t.ticker, t.session_date, t.place, t.entry, t.stop, t.target, NULL, t.cap, t.plan,
               CASE WHEN f.filled_on <= $on THEN f.filled_on END, CASE WHEN f.filled_on <= $on THEN f.fill END,
               CASE WHEN f.reason IS NOT NULL AND f.filled_on IS NULL THEN 1 ELSE 0 END,
               CASE WHEN f.sold_on <= $on THEN f.sold_on END, CASE WHEN f.sold_on <= $on THEN f.result END, CASE WHEN f.sold_on <= $on THEN f.sale END
        FROM family_trade t
        LEFT JOIN trade_fill f ON f.source = 'family_trade' AND f.index_code = 'GSPC' AND f.owner = t.candidate AND f.ticker = t.ticker AND f.session_date = t.session_date
        WHERE $index = 'GSPC' AND t.plan IS NOT NULL AND t.session_date <= $on
        UNION ALL
        SELECT t.candidate, t.index_code, t.family, t.ticker, t.session_date, t.place, t.entry, t.stop, t.target, t.trail, t.cap, t.plan,
               CASE WHEN f.filled_on <= $on THEN f.filled_on END, CASE WHEN f.filled_on <= $on THEN f.fill END,
               CASE WHEN f.reason IS NOT NULL AND f.filled_on IS NULL THEN 1 ELSE 0 END,
               CASE WHEN f.sold_on <= $on THEN f.sold_on END, CASE WHEN f.sold_on <= $on THEN f.result END, CASE WHEN f.sold_on <= $on THEN f.sale END
        FROM index_rule_trade t
        LEFT JOIN trade_fill f ON f.source = 'index_rule_trade' AND f.index_code = t.index_code AND f.owner = t.candidate AND f.ticker = t.ticker AND f.session_date = t.session_date
        WHERE t.index_code = $index AND t.plan IS NOT NULL AND t.session_date <= $on
        UNION ALL
        SELECT t.rule, t.index_code, 'dip', t.ticker, t.session_date, t.place, t.entry, t.stop, NULL, NULL, t.time_limit, t.plan,
               CASE WHEN t.bought_on <= $on THEN t.bought_on END, CASE WHEN t.bought_on <= $on THEN t.fill END,
               CASE WHEN t.ended_on <= $on AND t.fill IS NULL THEN 1 ELSE 0 END,
               CASE WHEN t.ended_on <= $on AND t.fill IS NOT NULL THEN t.ended_on END, CASE WHEN t.ended_on <= $on THEN t.result END, CASE WHEN t.ended_on <= $on THEN t.sale END
        FROM dip_trade t
        WHERE t.index_code = $index AND t.plan IS NOT NULL AND t.session_date <= $on
        ORDER BY 5, 3, 6, 4;
    ";

    // Every registered rule's trade an index's plan keeps through a night, a rule's stock its family listed once a night
    // read once.
    public async Task<IReadOnlyList<PlannedTradeRow>> PlannedTradesAsync(string index, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = PlannedUpTo;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var rows = new List<PlannedTradeRow>();
        var seen = new HashSet<(string, string, string, DateOnly)>();

        await using var reader = await command.ExecuteReaderAsync();

        decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

        DateOnly? DayOf(int at) => reader.IsDBNull(at) ? null : DateOnly.ParseExact(reader.GetString(at), "yyyy-MM-dd", CultureInfo.InvariantCulture);

        while (await reader.ReadAsync())
        {
            var listed = DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            if (PlanOrder.FromJson(reader.GetString(11)) is not { } order || !seen.Add((reader.GetString(0), reader.GetString(2), reader.GetString(3), listed)))
            {
                continue;
            }

            rows.Add(new PlannedTradeRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                listed,
                reader.GetInt32(5),
                Money.FromStorage(reader.GetString(6)),
                Price(7),
                Price(8),
                Price(9),
                reader.GetInt32(10),
                order,
                DayOf(12),
                Price(13),
                reader.GetInt32(14) == 1,
                DayOf(15),
                reader.IsDBNull(16) ? null : reader.GetDouble(16),
                Price(17)));
        }

        return rows;
    }
}
