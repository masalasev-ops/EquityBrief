using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Fundamentals;

public sealed record ScorerOutcome(DateOnly Night, int Members, int Scored, int Worded);

// The fundamentals scorecard. Writes, for every member of the S&P 500, 400 and 600 holding a bar on the night, its nine
// checks over its filer's facts as first filed before the session, through the core's one scorecard, with each check's
// figures, the word, the newest quarter it read and a newer one the calendar says was reported and no filing yet states,
// one ending more than NewerAfter days after it;
// its gross profit over its total assets with its place among its index's members; and the price over its four quarters'
// earnings as the valuation reading works its value, with the median of its GICS industry's members on its index. The
// closes it reads are the store's own, so a split before the store's first session is not seen. It makes no request and
// calls no model, and a night run again replaces its own session's rows and no other's.
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
// see: The nightly run is arithmetic only
public sealed class FundamentalScorer(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.FundamentalScore, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "fundamental-scores";

    // The night scores every member again after its filings refresh, under a row of its own, so a filing the refresh
    // stored tonight is read on tonight's scorecard as it is on tonight's setups.
    public const string AgainStage = "fundamental-scores-again";

    // The fewest members of an industry on an index with earnings above nothing whose median price over earnings is read.
    public const int IndustryFewest = 3;

    // A quarter the calendar says was reported is newer than the one scored only where it ends more than this many days
    // after it: the calendar dates a quarter by its month's end where a filer's fiscal quarter ends on a set weekday, up
    // to three weeks apart, and the shortest fiscal quarter runs twelve weeks.
    public const int NewerAfter = 45;

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string MembersOn = @"
        SELECT DISTINCT index_code, ticker
        FROM membership
        WHERE " + IndexScope.Condition + @"
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY index_code, ticker;
    ";

    const string ClosesTo = "SELECT ticker, session_date, close, raw_close FROM bar WHERE session_date <= $session ORDER BY ticker, session_date;";

    // Each ticker's newest company row: its filer, its sector and its industry.
    const string Companies = @"
        SELECT c.ticker, c.cik, c.sector, c.industry
        FROM company c
        WHERE c.fetched_at = (SELECT MAX(fetched_at) FROM company WHERE ticker = c.ticker);
    ";

    const string FactsOfFiler = "SELECT concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE cik = $cik;";

    const string ReportsTo = "SELECT ticker, event_date, detail FROM calendar WHERE kind = 'earnings' AND event_date <= $session;";

    const string Clear = "DELETE FROM fundamental_score WHERE session_date = $session;";

    const string Insert = @"
        INSERT INTO fundamental_score (
            index_code, session_date, ticker, quarter, year_before, awaited, financial, passed, applied, word, checks,
            gross_profitability, gross_rank, gross_ranked, multiple, industry, industry_multiple)
        VALUES (
            $index_code, $session_date, $ticker, $quarter, $year_before, $awaited, $financial, $passed, $applied, $word, $checks,
            $gross_profitability, $gross_rank, $gross_ranked, $multiple, $industry, $industry_multiple);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            $rows_written, 0, 0, '0', $detail);
    ";

    sealed record Scored(string Index, string Ticker, Scorecard Card, DateOnly? Awaited, double? Multiple, string? Industry);

    public async Task<ScorerOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default, IReadOnlyList<string>? wider = null, string stage = Stage)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var night = await NewestAsync(connection, cancellation) ?? clock.SessionDateAt(clock.UtcNow);
        var members = await MembersAsync(connection, indexCode, night, wider, cancellation);
        var closes = await ClosesAsync(connection, night, cancellation);
        var companies = await CompaniesAsync(connection, cancellation);
        var reports = await ReportsAsync(connection, night, cancellation);
        var byFiler = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal);
        var scored = new List<Scored>();

        foreach (var (index, ticker) in members)
        {
            if (closes.GetValueOrDefault(ticker) is not { Sessions: [.., var newest] } prices || newest != night)
            {
                continue;
            }

            var company = companies.GetValueOrDefault(ticker);
            IReadOnlyList<FiledFactRow> facts = [];

            if (company.Cik is { } cik)
            {
                if (!byFiler.TryGetValue(cik, out var held))
                {
                    held = await FactsAsync(connection, cik, cancellation);
                    byFiler[cik] = held;
                }

                facts = held;
            }

            var financial = FundamentalScorecard.IsFinancial(GicsSectors.On(ticker, company.Sector, night));
            var card = FundamentalScorecard.Read(facts, night, financial, prices);
            var parts = ValuationReading.PartsOf(facts, night);
            var yield = parts.Earnings.Count > 0 ? ValuationReading.OverValue(parts.Earnings[^1].Earnings, parts, prices, night) : null;
            DateOnly? awaited = reports.TryGetValue(ticker, out var reported) && card.Quarter is { } read && reported.DayNumber - read.DayNumber > NewerAfter
                ? reported
                : null;

            scored.Add(new Scored(index, ticker, card, awaited, yield is { } earning && earning > 0 ? 1 / earning : null, company.Industry));
        }

        // Each index's members placed by gross profit over total assets, the highest first, and each industry's median
        // price over earnings among its members on the index with earnings above nothing.
        var ranks = new Dictionary<(string Index, string Ticker), (int Rank, int Of)>();

        foreach (var index in scored.GroupBy(one => one.Index))
        {
            var placed = index.Where(one => one.Card.GrossProfitability is not null).OrderByDescending(one => one.Card.GrossProfitability).ThenBy(one => one.Ticker, StringComparer.Ordinal).ToArray();

            for (var at = 0; at < placed.Length; at++)
            {
                ranks[(index.Key, placed[at].Ticker)] = (at + 1, placed.Length);
            }
        }

        var medians = scored
            .Where(one => one.Industry is not null && one.Multiple is not null)
            .GroupBy(one => (one.Index, one.Industry!))
            .Where(group => group.Count() >= IndustryFewest)
            .ToDictionary(group => group.Key, group => Median([.. group.Select(one => one.Multiple!.Value)]));

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = (SqliteTransaction)transaction;
            clear.CommandText = Clear;
            clear.Parameters.AddWithValue("$session", Stamp(night));
            await clear.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var one in scored)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$index_code", one.Index);
            command.Parameters.AddWithValue("$session_date", Stamp(night));
            command.Parameters.AddWithValue("$ticker", one.Ticker);
            command.Parameters.AddWithValue("$quarter", one.Card.Quarter is { } quarter ? Stamp(quarter) : DBNull.Value);
            command.Parameters.AddWithValue("$year_before", one.Card.YearBefore is { } before ? Stamp(before) : DBNull.Value);
            command.Parameters.AddWithValue("$awaited", one.Awaited is { } awaited ? Stamp(awaited) : DBNull.Value);
            command.Parameters.AddWithValue("$financial", one.Card.Financial ? 1 : 0);
            command.Parameters.AddWithValue("$passed", one.Card.Passed);
            command.Parameters.AddWithValue("$applied", one.Card.Applied);
            command.Parameters.AddWithValue("$word", one.Card.Word is { } word ? ScorecardText.WordOf(word) : DBNull.Value);
            command.Parameters.AddWithValue("$checks", ScorecardText.Of(one.Card.Checks));
            command.Parameters.AddWithValue("$gross_profitability", one.Card.GrossProfitability is { } gross ? gross : DBNull.Value);
            command.Parameters.AddWithValue("$gross_rank", ranks.TryGetValue((one.Index, one.Ticker), out var rank) ? rank.Rank : DBNull.Value);
            command.Parameters.AddWithValue("$gross_ranked", ranks.TryGetValue((one.Index, one.Ticker), out var ranked) ? ranked.Of : DBNull.Value);
            command.Parameters.AddWithValue("$multiple", one.Multiple is { } multiple ? multiple : DBNull.Value);
            command.Parameters.AddWithValue("$industry", one.Industry is { } industry ? industry : DBNull.Value);
            command.Parameters.AddWithValue("$industry_multiple", one.Industry is { } named && medians.TryGetValue((one.Index, named), out var median) ? median : DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellation);
        }

        var worded = scored.Count(one => one.Card.Word is not null);

        await using (var log = connection.CreateCommand())
        {
            log.Transaction = (SqliteTransaction)transaction;
            log.CommandText = AppendRun;
            log.Parameters.AddWithValue("$run_id", runId);
            log.Parameters.AddWithValue("$stage", stage);
            log.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            log.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            log.Parameters.AddWithValue("$rows_written", scored.Count);
            log.Parameters.AddWithValue(
                "$detail",
                FormattableString.Invariant($"{members.Count} member(s) read for {Stamp(night)}, {scored.Count} holding a bar on it scored, {worded} given a word, {scored.Count(one => one.Card.Financial)} financial, {scored.Count(one => one.Awaited is not null)} with a newer quarter reported and not yet filed"));
            await log.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return new ScorerOutcome(night, members.Count, scored.Count, worded);
    }

    static double Median(double[] values)
    {
        Array.Sort(values);

        return values.Length % 2 == 1 ? values[values.Length / 2] : (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2;
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
    }

    static async Task<IReadOnlyList<(string Index, string Ticker)>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session, IReadOnlyList<string>? wider, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        IndexScope.Bind(command, indexCode, wider);
        command.Parameters.AddWithValue("$session", Stamp(session));

        var members = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            // A name two indices hold on one session is read once, in the first.
            if (seen.Add(reader.GetString(1)))
            {
                members.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        return members;
    }

    // Each name's closes to the session at the store's scale and as traded, a close as traded the store does not hold
    // being nought.
    static async Task<IReadOnlyDictionary<string, PriceHistory>> ClosesAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ClosesTo;
        command.Parameters.AddWithValue("$session", Stamp(session));

        var bars = new Dictionary<string, List<SweepBar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!bars.TryGetValue(ticker, out var series))
            {
                series = [];
                bars[ticker] = series;
            }

            series.Add(new SweepBar(Date(reader.GetString(1)), 0m, 0m, Money.FromStorage(reader.GetString(2)), 0, RawClose: reader.IsDBNull(3) ? 0m : Money.FromStorage(reader.GetString(3))));
        }

        return bars.ToDictionary(pair => pair.Key, pair => PriceHistory.Of(pair.Value), StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<string, (string? Cik, string? Sector, string? Industry)>> CompaniesAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Companies;

        var companies = new Dictionary<string, (string?, string?, string?)>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            companies[reader.GetString(0)] = (
                reader.IsDBNull(1) ? null : SecEdgarArchive.Padded(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3));
        }

        return companies;
    }

    static async Task<IReadOnlyList<FiledFactRow>> FactsAsync(SqliteConnection connection, string cik, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = FactsOfFiler;
        command.Parameters.AddWithValue("$cik", cik);

        var facts = new List<FiledFactRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            facts.Add(new FiledFactRow(
                reader.GetString(0),
                Date(reader.GetString(1)),
                Date(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Date(reader.GetString(4)),
                reader.GetString(5),
                reader.GetString(6)));
        }

        return facts;
    }

    // Each ticker's newest quarter the calendar says was reported on or before the session.
    static async Task<IReadOnlyDictionary<string, DateOnly>> ReportsAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReportsTo;
        command.Parameters.AddWithValue("$session", Stamp(session));

        var newest = new Dictionary<string, DateOnly>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (reader.IsDBNull(2))
            {
                continue;
            }

            using var detail = JsonDocument.Parse(reader.GetString(2));

            if (detail.RootElement.ValueKind is JsonValueKind.Object
                && detail.RootElement.TryGetProperty("periodEnd", out var end) && end.ValueKind is JsonValueKind.String
                && DateOnly.TryParseExact(end.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var quarter)
                && (!newest.TryGetValue(reader.GetString(0), out var held) || quarter > held))
            {
                newest[reader.GetString(0)] = quarter;
            }
        }

        return newest;
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
