using System.Globalization;
using EquityBrief.Core.Paper;

namespace EquityBrief.Data;

public sealed partial class PaperReads
{
    // Every trade the live list recommended up to a night: each name the swing filter passed on a night
    // it listed, on the plan that night's trade gate read, with the forward return it was scored under,
    // newest first in the list's own order. The names passing and no other, so a member failing one gate
    // is not a trade the list recommended whatever plan its row carries; and a night the reasons listed
    // holds no filter row to read. The plan is chosen as the near misses choose it. On a night the families
    // drew the page's list, the pullback's trades are the stocks the page listed under it, in the page's
    // order, each with the other families it qualified under, and every other family's trades are the
    // stocks the page listed under it, each on the trade its family's stored answer holds and the outcome
    // scored under its family's horizon.
    // see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
    // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    // A pullback pick an approved setting named an exit of the menu for is read under the menu's own horizon.
    // see: The S&P 500's pullback is proposed for and changed through the Loop, the shape clock's moves among its sources
    static readonly string Picks = @"
        SELECT g.ticker, g.session_date,
               CASE WHEN (SELECT fx.exit FROM family_pick fx WHERE fx.session_date = g.session_date AND fx.ticker = g.ticker AND fx.family = $pullback) IS NOT NULL
                    THEN '" + Core.Returns.ForwardReturnSeries.MenuExit + @"'
                    ELSE CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN $clear ELSE $swing END END,
               g.swing_entry,
               CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN g.clear_stop ELSE g.swing_stop END,
               CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN g.clear_target ELSE g.swing_target END,
               f.horizon IS NOT NULL, f.outcome, f.resolved_on, f.return_pct, f.break_even,
               COALESCE((SELECT l.name FROM listed_name l WHERE l.ticker = g.ticker), titled((SELECT m.name FROM membership m WHERE m.ticker = g.ticker
                ORDER BY m.observed_at DESC, m.rowid DESC LIMIT 1))),
               (SELECT b.close FROM bar b WHERE b.ticker = g.ticker AND b.session_date = g.session_date),
               (SELECT b.session_date FROM bar b WHERE b.ticker = g.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               (SELECT b.close FROM bar b WHERE b.ticker = g.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               s.state,
               $pullback,
               (SELECT fp.also FROM family_pick fp
                WHERE fp.session_date = g.session_date AND fp.ticker = g.ticker AND fp.family = $pullback AND fp.state = $listed),
               f.planned_risk,
               IFNULL(" + Core.Families.FamilyList.PlaceOn("g.ticker", "g.session_date") + @", 0),
               " + Core.Quarters.FundamentalState.PlaceIn("s.state") + @",
               g.rank,
               tf.result, tf.fill, tf.filled_on, tf.past, tf.reason, tf.sold_on, tf.sale
        FROM gate_result g
        JOIN list_rule r ON r.session_date = g.session_date AND r.rule = $filter
        LEFT JOIN filter_version v ON v.version = g.version
        LEFT JOIN forward_return f
            ON f.ticker = g.ticker AND f.session_date = g.session_date
            AND f.horizon = CASE WHEN (SELECT fx.exit FROM family_pick fx WHERE fx.session_date = g.session_date AND fx.ticker = g.ticker AND fx.family = $pullback) IS NOT NULL
                                 THEN '" + Core.Returns.ForwardReturnSeries.MenuExit + @"'
                                 ELSE CASE json_extract(v.settings, '$.trade') WHEN $clearPlan THEN $clear ELSE $swing END END
        LEFT JOIN fundamental_reading s ON s.ticker = g.ticker AND s.session_date = g.session_date
        LEFT JOIN trade_fill tf ON tf.source = 'forward_return' AND tf.index_code = 'GSPC' AND tf.owner = f.horizon AND tf.ticker = g.ticker AND tf.session_date = g.session_date
        WHERE g.session_date <= $on AND ($ticker IS NULL OR g.ticker = $ticker)
          AND CASE WHEN " + Core.Families.FamilyList.HasPicks("g.session_date") + @"
                   THEN EXISTS (SELECT 1 FROM family_pick fp
                                WHERE fp.session_date = g.session_date AND fp.ticker = g.ticker AND fp.family = $pullback AND fp.state = $listed)
                   ELSE g.passed = 1 END
        UNION ALL
        SELECT p.ticker, p.session_date,
               " + Core.Families.SetupFamilies.HorizonIn("p.family", "pv.settings") + @",
               pr.entry, pr.stop, pr.target,
               pf.horizon IS NOT NULL, pf.outcome, pf.resolved_on, pf.return_pct, pf.break_even,
               COALESCE((SELECT l.name FROM listed_name l WHERE l.ticker = p.ticker), titled((SELECT m.name FROM membership m WHERE m.ticker = p.ticker
                ORDER BY m.observed_at DESC, m.rowid DESC LIMIT 1))),
               (SELECT b.close FROM bar b WHERE b.ticker = p.ticker AND b.session_date = p.session_date),
               (SELECT b.session_date FROM bar b WHERE b.ticker = p.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               (SELECT b.close FROM bar b WHERE b.ticker = p.ticker AND b.session_date <= $on
                ORDER BY b.session_date DESC LIMIT 1),
               ps.state,
               p.family,
               p.also,
               pf.planned_risk,
               IFNULL(p.place, 0),
               0,
               0,
               ptf.result, ptf.fill, ptf.filled_on, ptf.past, ptf.reason, ptf.sold_on, ptf.sale
        FROM family_pick p
        JOIN family_result pr ON pr.session_date = p.session_date AND pr.ticker = p.ticker AND pr.family = p.family
        LEFT JOIN gate_result pg ON pg.ticker = p.ticker AND pg.session_date = p.session_date
        LEFT JOIN filter_version pv ON pv.version = pg.version
        LEFT JOIN forward_return pf
            ON pf.ticker = p.ticker AND pf.session_date = p.session_date
            AND pf.horizon = " + Core.Families.SetupFamilies.HorizonIn("p.family", "pv.settings") + @"
        LEFT JOIN fundamental_reading ps ON ps.ticker = p.ticker AND ps.session_date = p.session_date
        LEFT JOIN trade_fill ptf ON ptf.source = 'forward_return' AND ptf.index_code = 'GSPC' AND ptf.owner = pf.horizon AND ptf.ticker = p.ticker AND ptf.session_date = p.session_date
        WHERE p.state = $listed AND p.family <> $pullback
          AND p.session_date <= $on AND ($ticker IS NULL OR p.ticker = $ticker)
        ORDER BY 2 DESC, 20, 21, 22, 1;
    ";

    // Every trade the live list recommended on a night up to the one given, newest first, or one name's
    // alone where a ticker is given.
    // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
    public async Task<IReadOnlyList<PickRow>> PicksAsync(DateOnly on, string? ticker = null)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = Picks;
        command.Parameters.AddWithValue("$on", on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ticker", (object?)ticker ?? DBNull.Value);
        command.Parameters.AddWithValue("$filter", Core.Shortlist.ListRules.Filter);
        command.Parameters.AddWithValue("$clearPlan", Core.Filter.FilterSettings.ClearWord);
        command.Parameters.AddWithValue("$clear", Core.Returns.ForwardReturnSeries.Clear);
        command.Parameters.AddWithValue("$swing", Core.Returns.ForwardReturnSeries.Swing);
        command.Parameters.AddWithValue("$pullback", Core.Families.SetupFamilies.Pullback);
        command.Parameters.AddWithValue("$listed", Core.Families.FamilyList.Listed);

        var rows = new List<PickRow>();

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            decimal? Price(int at) => reader.IsDBNull(at) ? null : Money.FromStorage(reader.GetString(at));

            DateOnly? Day(int at) => reader.IsDBNull(at) ? null : DateOnly.ParseExact(reader.GetString(at), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            rows.Add(new PickRow(
                reader.GetString(0),
                DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(2),
                Price(3),
                Price(4),
                Price(5),
                reader.GetInt32(6) == 1,
                reader.IsDBNull(7) ? null : reader.GetString(7),
                Day(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9),
                reader.IsDBNull(10) ? null : reader.GetDouble(10),
                reader.IsDBNull(11) ? null : reader.GetString(11),
                Price(12),
                Day(13),
                Price(14),
                reader.IsDBNull(15) ? null : reader.GetString(15),
                reader.GetString(16),
                reader.IsDBNull(17) ? [] : System.Text.Json.JsonSerializer.Deserialize<string[]>(reader.GetString(17)) ?? [],
                reader.IsDBNull(18) ? null : reader.GetDouble(18),
                reader.IsDBNull(22) ? null : reader.GetDouble(22),
                Price(23),
                Day(24),
                reader.IsDBNull(25) ? null : reader.GetString(25),
                reader.IsDBNull(26) ? null : reader.GetString(26),
                Day(27),
                Price(28)));
        }

        return rows;
    }
}
