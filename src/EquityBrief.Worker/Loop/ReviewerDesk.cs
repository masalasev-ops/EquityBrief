using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// From 24.36 the reviewer's desk, by code with no model and no request: for each pick a family listed on an index on a
// night, what history expected of it, the family's settled setups on the index from the long ledger to 2018 and the standing
// ledger from 2019 that had closed before the night matched on the readings the family's newest learned score weighs most,
// as the cards match; and the same for a seeded draw of the night's members under the same family's plan, so the family reads
// as better or worse than random by history. Each is written once, the pick's or member's own stock left out of its match.
// see: The reviewer matches each pick among the past cases like it that closed before its night, and a seeded draw of the night's members the same way
public sealed class ReviewerDesk(IClock clock, string databaseFile, string dataRoot, TextWriter output, Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.DipPick, Touch.Read),
            new StoreTouch(Store.SessionReading, Touch.Read),
            new StoreTouch(Store.UnreliableMark, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.LoopModel, Touch.Read),
            new StoreTouch(Store.Review, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "review";

    public const string Pick = "pick";

    public const string Random = "random";

    static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    const string LargePicks = "SELECT ticker, family FROM family_pick WHERE session_date = $night AND state = 'listed' ORDER BY place, ticker;";

    const string IndexPicks = "SELECT ticker, family FROM index_family_pick WHERE index_code = $index AND session_date = $night AND state = 'listed' ORDER BY place, ticker;";

    const string DipPicks = "SELECT ticker, 'dip' FROM dip_pick WHERE index_code = $index AND session_date = $night AND state = 'listed' ORDER BY place, ticker;";

    const string NightReadings = "SELECT ticker, readings FROM session_reading WHERE index_code = $index AND session_date = $night;";

    const string Held = "SELECT COUNT(*) FROM review WHERE night = $night AND index_code = $index;";

    static string Cases(string table, bool standing) =>
        "SELECT ticker, session_date, ended_on, result, \"end\", target, edge, cost, "
        + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + $" FROM {table} WHERE index_code = $index AND family = $family AND settled = 1 AND live_pass = 1 AND ended_on IS NOT NULL AND ended_on < $night{(standing ? " AND session_date >= $standing" : string.Empty)}";

    const int CasesFrom = 8;

    const string InsertReview = @"
        INSERT INTO review (night, index_code, family, kind, ticker, cases, reached, mean, matched_on, run_id)
        VALUES ($night, $index, $family, $kind, $ticker, $cases, $reached, $mean, $matched, $run);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    // A family's settled setups on the index that closed before the night, from the long ledger to 2018 and the standing ledger
    // from 2019, each with its readings, its result after its stored cost and whether it reached its take-profit.
    public static async Task<IReadOnlyList<ReviewCase>> CasesAsync(SqliteConnection connection, string index, string family, DateOnly night, CancellationToken cancellation = default)
    {
        var cases = new List<ReviewCase>();

        foreach (var standing in new[] { false, true })
        {
            await using var command = connection.CreateCommand();

            command.CommandText = Cases(standing ? "setup" : "long_setup", standing);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$night", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$standing", Core.History.LongLedger.StandingFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                double? result = reader.IsDBNull(3) ? null : reader.GetDouble(3);
                double? cost = reader.IsDBNull(7) ? null : reader.GetDouble(7);
                var form = SetupChance.FormOf(LikeSetups.IsBook(family), !reader.IsDBNull(5));
                var readings = new double?[LedgerReadings.Count];

                for (var at = 0; at < readings.Length; at++)
                {
                    readings[at] = reader.IsDBNull(CasesFrom + at) ? null : reader.GetDouble(CasesFrom + at);
                }

                cases.Add(new ReviewCase(
                    reader.GetString(0),
                    Day(reader.GetString(1)),
                    Day(reader.GetString(2)),
                    readings,
                    result is { } made ? made - (cost ?? 0) : null,
                    SetupChance.WonOf(form, reader.IsDBNull(4) ? null : reader.GetString(4), result, reader.IsDBNull(6) ? null : reader.GetDouble(6))));
            }
        }

        return cases;
    }

    // The night's reviews on an index: without the word, its picks by family and the members a draw would take printed and
    // nothing written; with it, each pick's expectation and each drawn member's written once. 0 where it printed or wrote, 2
    // where it refused to start.
    public async Task<int> RunAsync(string index, DateOnly night, bool word, CancellationToken cancellation = default)
    {
        if (!WalkForwardTester.Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        var started = clock.UtcNow;
        var runId = FormattableString.Invariant($"{Verb}-{index}-{night:yyyyMMdd}-{started.UtcDateTime:yyyyMMddTHHmmssZ}");
        var day = night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var picks = new List<(string Ticker, string Family)>();
        var readings = new Dictionary<string, double?[]>(StringComparer.Ordinal);
        var rows = new List<(string Family, string Kind, string Ticker, Expectation? Expected, string Matched)>();

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = Held;
                command.Parameters.AddWithValue("$night", day);
                command.Parameters.AddWithValue("$index", index);

                if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) > 0)
                {
                    output.WriteLine(FormattableString.Invariant($"{Verb}: the {DecisionCards.NameOf(index)}'s night of {day} is reviewed already; nothing written"));

                    return 0;
                }
            }

            foreach (var sql in index == WalkForwardTester.LargeIndex ? new[] { LargePicks } : [IndexPicks, DipPicks])
            {
                await using var command = connection.CreateCommand();

                command.CommandText = sql;
                command.Parameters.AddWithValue("$night", day);
                command.Parameters.AddWithValue("$index", index);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    picks.Add((reader.GetString(0), reader.GetString(1)));
                }
            }

            var marked = await Ledger.UnreliableMarkRows.ReadAsync(connection, cancellation);

            await using (var command = connection.CreateCommand())
            {
                command.CommandText = NightReadings;
                command.Parameters.AddWithValue("$night", day);
                command.Parameters.AddWithValue("$index", index);

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    // A stock-session the check marked unreliable or excluded is skipped.
                    // see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
                    if (!marked.Skips(reader.GetString(0), night))
                    {
                        readings[reader.GetString(0)] = [.. System.Text.Json.JsonSerializer.Deserialize<double?[]>(reader.GetString(1)) ?? []];
                    }
                }
            }

            output.WriteLine(FormattableString.Invariant($"{Verb}: the {DecisionCards.NameOf(index)}'s night of {day}: {picks.Count} pick(s) across {picks.Select(one => one.Family).Distinct().Count()} family(ies), {readings.Count} member(s) holding the night's readings"));

            if (!word)
            {
                output.WriteLine($"{Verb}: nothing written; a review over the store is written on the operator's word alone, given with '--word'");

                return 0;
            }

            foreach (var family in picks.Select(one => one.Family).Distinct(StringComparer.Ordinal))
            {
                if (await StoredRidge.NewestAsync(connection, null, index, family, cancellation) is not { } model)
                {
                    rows.AddRange(picks.Where(one => one.Family == family).Select(one => (family, Pick, one.Ticker, (Expectation?)null, string.Empty)));

                    continue;
                }

                var matched = SimilarSetups.Readings(model);
                string[] columns = [.. matched.Select(reading => LedgerReadings.All[reading].Column)];
                var cases = await CasesAsync(connection, index, family, night, cancellation);
                var form = SetupChance.FormOf(LikeSetups.IsBook(family), hasTarget: true);
                var named = string.Join(",", columns);

                Expectation? ExpectOf(string ticker) =>
                    readings.TryGetValue(ticker, out var held) && held.Length == LedgerReadings.Count ? Reviewer.Expect(cases, night, ticker, held, matched, columns, form) : null;

                rows.AddRange(picks.Where(one => one.Family == family).Select(one => (family, Pick, one.Ticker, ExpectOf(one.Ticker), named)));
                rows.AddRange(Reviewer.Draw(readings.Keys, index, night, family).Select(ticker => (family, Random, ticker, ExpectOf(ticker), named)));
            }
        }

        using (var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(Poll, cancellation), cancellation))
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await using var transaction = connection.BeginTransaction();

            foreach (var (family, kind, ticker, expected, matched) in rows)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = InsertReview;
                insert.Parameters.AddWithValue("$night", day);
                insert.Parameters.AddWithValue("$index", index);
                insert.Parameters.AddWithValue("$family", family);
                insert.Parameters.AddWithValue("$kind", kind);
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$cases", (object?)expected?.Cases ?? DBNull.Value);
                insert.Parameters.AddWithValue("$reached", (object?)expected?.Reached ?? DBNull.Value);
                insert.Parameters.AddWithValue("$mean", (object?)expected?.Mean ?? DBNull.Value);
                insert.Parameters.AddWithValue("$matched", matched);
                insert.Parameters.AddWithValue("$run", runId);
                await insert.ExecuteNonQueryAsync(cancellation);
            }

            var detail = string.Join("; ", rows.GroupBy(one => one.Family).Select(family => FormattableString.Invariant(
                $"{family.Key}: {family.Count(one => one.Kind == Pick)} pick(s), {family.Count(one => one.Kind == Pick && one.Expected is not null)} with a figure, the random draw's mean {Reviewer.RandomMean(family.Where(one => one.Kind == Random).Select(one => one.Expected))?.ToString("+0.000;-0.000", CultureInfo.InvariantCulture) ?? "none"}")));

            await using (var log = connection.CreateCommand())
            {
                log.Transaction = transaction;
                log.CommandText = AppendRun;
                log.Parameters.AddWithValue("$run_id", runId);
                log.Parameters.AddWithValue("$stage", Verb);
                log.Parameters.AddWithValue("$started_at", Instant(started));
                log.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
                log.Parameters.AddWithValue("$rows", rows.Count);
                log.Parameters.AddWithValue("$detail", detail.Length == 0 ? "no pick listed" : detail);
                await log.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
            output.WriteLine($"{Verb}: {(detail.Length == 0 ? "no pick listed" : detail)}");
        }

        return 0;
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
