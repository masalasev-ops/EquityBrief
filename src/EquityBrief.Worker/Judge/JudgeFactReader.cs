using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Ledger;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Judge;

// The judge's facts of a night: for every member of the three indices the night's ledger read, its fields as the judge is
// shown them, read from the night's stored rows and nothing after the night. The sector is the newest the company rows
// file by the next morning, the size the night's company value as a band, the readings the night's ledger row's, the
// business score the night's own, the news the stories published in the 30 days to the next morning as the labeller read
// them by then, and the three fields the ledger's night step leaves empty worked from the bars to the night and the
// night's ladder plan; from 24.24 each figure placed in the tenth it stands in among its own index's members that night.
// It reads the store alone, opened for reading, and writes nothing.
// see: The AI stock picker sees the fields the operator approved and nothing that names a company, a date or a price
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
public static class JudgeFactReader
{
    // The hour of the next morning, in UTC, before which everything read was stored: ahead of the next open.
    public const int MorningHourUtc = 13;

    const string Ledger = "SELECT index_code, ticker, readings FROM ledger_reading WHERE session_date = $session AND ($ticker IS NULL OR ticker = $ticker);";

    const string Values = "SELECT index_code, ticker, company_value FROM member_reading WHERE session_date = $session AND ($ticker IS NULL OR ticker = $ticker);";

    const string Scores = "SELECT index_code, ticker, passed, applied FROM fundamental_score WHERE session_date = $session AND ($ticker IS NULL OR ticker = $ticker);";

    const string Sectors = @"
        SELECT c.ticker, c.sector FROM company c
        WHERE c.fetched_at = (SELECT MAX(d.fetched_at) FROM company d WHERE d.ticker = c.ticker AND d.fetched_at <= $through)
            AND ($ticker IS NULL OR c.ticker = $ticker);";

    const string Bars = @"
        SELECT ticker, close, high FROM bar
        WHERE session_date <= $session AND session_date > date($session, '-400 day') AND ($ticker IS NULL OR ticker = $ticker)
        ORDER BY ticker, session_date;";

    const string Ladders = "SELECT ticker, plan FROM ladder WHERE as_of = $session AND ($ticker IS NULL OR ticker = $ticker);";

    // Each story published in the window counted once, by the label of the newest instruction it was labelled under by
    // the next morning.
    const string News = @"
        WITH stories AS (
            SELECT ticker, article_id FROM news_article
            WHERE published_at > $from AND published_at <= $through AND ($ticker IS NULL OR ticker = $ticker)),
        labels AS (
            SELECT l.ticker, l.article_id, l.direction,
                ROW_NUMBER() OVER (PARTITION BY l.ticker, l.article_id ORDER BY l.instruction_version DESC, l.labelled_at DESC) AS n
            FROM news_label l JOIN stories s ON s.ticker = l.ticker AND s.article_id = l.article_id
            WHERE l.outcome = 'labelled' AND l.labelled_at <= $through)
        SELECT ticker, SUM(direction = 'positive'), SUM(direction = 'negative'), COUNT(*) FROM labels WHERE n = 1 GROUP BY ticker;";

    // The read-only connection the reader opens on a store file.
    public static string ReadOnly(string databaseFile)
    {
        var builder = StoreConnection.Builder(databaseFile);

        builder.Mode = SqliteOpenMode.ReadOnly;

        return builder.ConnectionString;
    }

    public static async Task<IReadOnlyList<JudgeFact>> ReadAsync(string databaseFile, DateOnly session, string? ticker = null, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(ReadOnly(databaseFile));
        await connection.OpenAsync(cancellation);

        return await ReadAsync(connection, session, ticker, cancellation);
    }

    public static async Task<IReadOnlyList<JudgeFact>> ReadAsync(SqliteConnection connection, DateOnly session, string? ticker = null, CancellationToken cancellation = default)
    {
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var through = MorningOf(session).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        var from = MorningOf(session).AddDays(-JudgeFacts.NewsDays).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        object named = (object?)ticker ?? DBNull.Value;

        var values = new Dictionary<(string, string), decimal?>();
        var scores = new Dictionary<(string, string), (int Passed, int Applied)>();
        var sectors = new Dictionary<string, string?>(StringComparer.Ordinal);
        var bars = new Dictionary<string, (List<decimal> Closes, List<decimal> Highs)>(StringComparer.Ordinal);
        var plans = new Dictionary<string, double?>(StringComparer.Ordinal);
        var news = new Dictionary<string, (int Good, int Bad)>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, Values, [("$session", day), ("$ticker", named)], cancellation))
        {
            values[(row.GetString(0), row.GetString(1))] = row.IsDBNull(2) ? null : decimal.Parse(row.GetString(2), CultureInfo.InvariantCulture);
        }

        await foreach (var row in RowsAsync(connection, Scores, [("$session", day), ("$ticker", named)], cancellation))
        {
            scores[(row.GetString(0), row.GetString(1))] = (row.GetInt32(2), row.GetInt32(3));
        }

        await foreach (var row in RowsAsync(connection, Sectors, [("$through", through), ("$ticker", named)], cancellation))
        {
            sectors[row.GetString(0)] = row.IsDBNull(1) ? null : row.GetString(1);
        }

        await foreach (var row in RowsAsync(connection, Bars, [("$session", day), ("$ticker", named)], cancellation))
        {
            if (!bars.TryGetValue(row.GetString(0), out var held))
            {
                held = ([], []);
                bars[row.GetString(0)] = held;
            }

            held.Closes.Add(decimal.Parse(row.GetString(1), CultureInfo.InvariantCulture));
            held.Highs.Add(decimal.Parse(row.GetString(2), CultureInfo.InvariantCulture));
        }

        await foreach (var row in RowsAsync(connection, Ladders, [("$session", day), ("$ticker", named)], cancellation))
        {
            plans[row.GetString(0)] = PlanRewardToRisk(row.IsDBNull(1) ? null : row.GetString(1));
        }

        await foreach (var row in RowsAsync(connection, News, [("$from", from), ("$through", through), ("$ticker", named)], cancellation))
        {
            news[row.GetString(0)] = (row.GetInt32(1), row.GetInt32(2));
        }

        var facts = new List<(JudgeFact, IReadOnlyDictionary<string, double?>)>();

        await foreach (var row in RowsAsync(connection, Ledger, [("$session", day), ("$ticker", named)], cancellation))
        {
            var (index, member) = (row.GetString(0), row.GetString(1));
            var readings = row.IsDBNull(2) ? [] : JsonSerializer.Deserialize<double?[]>(row.GetString(2)) ?? [];
            var (closes, highs) = bars.TryGetValue(member, out var held) ? held : ([], []);
            (int Passed, int Applied)? score = scores.TryGetValue((index, member), out var scored) ? scored : null;

            facts.Add((new JudgeFact(index, member, session, Shown(
                readings,
                sectors.GetValueOrDefault(member),
                values.GetValueOrDefault((index, member)),
                score,
                closes,
                highs,
                plans.GetValueOrDefault(member),
                news.TryGetValue(member, out var told) ? told : (0, 0))),
                Figures(readings, score, closes, highs, plans.GetValueOrDefault(member))));
        }

        // From 24.24 each figure is placed among its own index's members that night, and a member named alone among none.
        return JudgeFacts.PlacedAmongTheirIndex(facts);
    }

    // A member's figures the picker places among its index's, by key, from the same readings its shown text is worked
    // from: each placed field's figure, none where it holds none.
    public static IReadOnlyDictionary<string, double?> Figures(
        IReadOnlyList<double?> readings,
        (int Passed, int Applied)? score,
        IReadOnlyList<decimal> closes,
        IReadOnlyList<decimal> highs,
        double? rewardToRisk)
    {
        double? At(string column) => LedgerReadings.IndexOf(column) is var at && at < readings.Count ? readings[at] : null;

        var figures = new Dictionary<string, double?>(StringComparer.Ordinal)
        {
            ["return_twelve_less_one"] = JudgeFacts.ReturnTwelveLessOne(closes),
            ["high_ratio"] = JudgeFacts.HighRatio(closes, highs),
            ["business_score"] = score is { Applied: > 0 } held ? held.Passed * 1.0 / held.Applied : null,
            ["surprise"] = At("surprise_percent") is { } beat && At("surprise_sessions") is not null ? beat : null,
            ["reward_to_risk"] = rewardToRisk,
        };

        foreach (var key in JudgeFacts.Placed.Where(key => !figures.ContainsKey(key)))
        {
            figures[key] = At(key);
        }

        return figures;
    }

    // The instant on the morning after a night before which everything the judge reads was stored.
    public static DateTimeOffset MorningOf(DateOnly session) =>
        new(session.AddDays(1).ToDateTime(new TimeOnly(MorningHourUtc, 0)), TimeSpan.Zero);

    // A member's fields as shown, from the night's rows.
    public static IReadOnlyDictionary<string, string> Shown(
        IReadOnlyList<double?> readings,
        string? sector,
        decimal? companyValue,
        (int Passed, int Applied)? score,
        IReadOnlyList<decimal> closes,
        IReadOnlyList<decimal> highs,
        double? rewardToRisk,
        (int Good, int Bad) news)
    {
        double? At(string column) => LedgerReadings.IndexOf(column) is var at && at < readings.Count ? readings[at] : null;

        var surprise = At("surprise_percent") is { } beat && At("surprise_sessions") is { } since
            ? $"{JudgeFacts.PercentAsStored(beat)}, {JudgeFacts.Count(since, "trading day", "trading days")} ago"
            : JudgeFacts.NotOnFile;

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sector"] = string.IsNullOrWhiteSpace(sector) ? JudgeFacts.NotOnFile : sector,
            ["size"] = JudgeFacts.SizeBand(companyValue),
            ["return_month"] = JudgeFacts.Percent(At("return_month")),
            ["return_quarter"] = JudgeFacts.PercentAsStored(At("return_quarter")),
            ["return_twelve_less_one"] = JudgeFacts.Percent(JudgeFacts.ReturnTwelveLessOne(closes)),
            ["strength"] = JudgeFacts.Place(At("strength"), "its index's stocks"),
            ["high_ratio"] = JudgeFacts.Percent(JudgeFacts.HighRatio(closes, highs)),
            ["close_over_fifty"] = JudgeFacts.Against(At("close_over_fifty")),
            ["close_over_long"] = JudgeFacts.Against(At("close_over_long")),
            ["rsi"] = JudgeFacts.Rsi(At("rsi")),
            ["volume_ratio"] = JudgeFacts.Times(At("volume_ratio")),
            ["volatility_month"] = At("volatility_month") is { } move && double.IsFinite(move)
                ? Math.Round(move * 100, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + "%"
                : JudgeFacts.NotOnFile,
            ["business_score"] = score is { Applied: > 0 } held
                ? FormattableString.Invariant($"{held.Passed} of the {held.Applied} checks that apply")
                : JudgeFacts.NotOnFile,
            ["surprise"] = surprise,
            ["estimate_revision_month"] = JudgeFacts.Percent(At("estimate_revision_month")),

            ["depth"] = JudgeFacts.Typical(At("depth")),
            ["since_high"] = JudgeFacts.Count(At("since_high"), "trading day", "trading days"),
            ["tightness"] = At("tightness") is { } tight ? JudgeFacts.Times(tight, 2) + " the daily range of the 20 days before" : JudgeFacts.NotOnFile,
            ["reward_to_risk"] = rewardToRisk is { } reward ? JudgeFacts.Times(reward) + " the risk" : JudgeFacts.NotOnFile,
            ["stretch_days"] = JudgeFacts.Typical(At("stretch_days")),
            ["gap_days"] = JudgeFacts.Typical(At("gap_days")),
            ["revenue_growth"] = JudgeFacts.Percent(At("revenue_growth")),
            ["growth_change"] = JudgeFacts.Points(At("growth_change")),
            ["gross_margin_change"] = JudgeFacts.Points(At("gross_margin_change")),
            ["operating_margin_change"] = JudgeFacts.Points(At("operating_margin_change")),
            ["cash_over_income"] = JudgeFacts.Times(At("cash_over_income")),
            ["profit"] = JudgeFacts.YesNo(At("profit")),
            ["coverage"] = JudgeFacts.YesNo(At("coverage")),
            ["return_on_assets"] = JudgeFacts.Percent(At("return_on_assets")),
            ["return_on_assets_change"] = JudgeFacts.Points(At("return_on_assets_change")),
            ["leverage_fall"] = JudgeFacts.Fell(At("leverage_fall"), "debt"),
            ["share_fall"] = JudgeFacts.Percent(At("share_fall")),
            ["value_place"] = JudgeFacts.Place(At("value_place"), "its own last 12 quarters on earnings for its price"),
            ["release_results"] = JudgeFacts.Answer(At("release_results"), "better than expected", "in line", "worse than expected"),
            ["release_outlook"] = JudgeFacts.Answer(At("release_outlook"), "raised", "kept", "lowered or withdrawn"),
            ["release_tone"] = JudgeFacts.Answer(At("release_tone"), "confident", "neutral", "cautious"),
            ["release_demand"] = JudgeFacts.Answer(At("release_demand"), "stronger", "steady", "weaker"),
            ["insider_net_buy"] = JudgeFacts.Answer(At("insider_net_buy"), "net buying", "none", "net selling"),
            ["insider_buyers"] = JudgeFacts.Count(At("insider_buyers"), "insider", "insiders"),
            ["insider_officer_buy"] = JudgeFacts.YesNo(At("insider_officer_buy")),
            ["insider_unplanned_sale"] = JudgeFacts.YesNo(At("insider_unplanned_sale")),
            ["short_interest_share"] = At("short_interest_share") is { } shorted && double.IsFinite(shorted)
                ? Math.Round(shorted * 100, 1, MidpointRounding.AwayFromZero).ToString("0.0", CultureInfo.InvariantCulture) + "%"
                : JudgeFacts.NotOnFile,
            ["news"] = FormattableString.Invariant($"{news.Good} good and {news.Bad} bad"),
            ["sector_month"] = JudgeFacts.Percent(At("sector_month")),
            ["sector_quarter"] = JudgeFacts.Percent(At("sector_quarter")),
            ["breadth"] = JudgeFacts.Share(At("breadth")),
            ["vix_over_median"] = JudgeFacts.Times(At("vix_over_median"), 2),
        };
    }

    // The ladder plan's first tranche's reward over its risk, none where the night's ladder holds no plan for the member.
    public static double? PlanRewardToRisk(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
        {
            return null;
        }

        using var document = JsonDocument.Parse(plan);

        if (!document.RootElement.TryGetProperty("arithmetic", out var arithmetic) || arithmetic.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return JudgeFacts.RewardToRisk(Price(arithmetic, "firstReward"), Price(arithmetic, "firstRisk"));
    }

    static decimal? Price(JsonElement arithmetic, string name) =>
        arithmetic.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(
        SqliteConnection connection,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            yield return reader;
        }
    }
}
