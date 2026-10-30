using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Shorts;

// What a read of FINRA's short interest came to: the settlements read, the member asks made, the reports stored, the
// members FINRA answered nothing for at a settlement, the requests, and the refusal that stopped it, none where none did.
public sealed record ShortInterestOutcome(int Settlements, int Asked, int Stored, int Unanswered, int Requests, string? Refused, int Restated = 0);

// The short interest step. A process of its own the off-peak run starts after the labeller, never the night, and by hand
// with its history: it asks FINRA for the settlements its reference symbol's reports carry from the newest settlement
// the store holds, or from FINRA's first with the history, and for each asks the records of every stock the three
// indices held on or after that settlement, a request a page of symbols, storing each stock's report with the day FINRA
// published it, the seventh business day after the settlement. A report stored already is kept as first read, so the
// newest settlement held is asked again each run and stores only what it lacked. A stock FINRA answers nothing for at a
// settlement stores nothing and is counted. A refusal stops the run with what it stored kept, named on its row. With no
// settlement stored and no history asked for, it reads the settlements of the last 45 days. Writes one row of its own and
// calls no model.
// see: FINRA's short interest is read once a settlement after the labeller, and its history once by hand
public sealed class ShortInterestReader(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.ShortInterest, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.ShortInterest]);

    public const string Stage = "short-interest";

    public const string RunPrefix = Stage + "-";

    public const string HistoryPrefix = Stage + "-history-";

    // The days before today a run reads the settlements of where the store holds none and no history is asked for.
    public const int FirstLookBackDays = 45;

    const string NewestSettlement = "SELECT MAX(settlement_date) FROM short_interest;";

    const string HeldSettlements = "SELECT DISTINCT settlement_date FROM short_interest;";

    // A settlement's reports moved to the day the rule gives, where the day stored is another.
    const string Restate = "UPDATE short_interest SET published_on = $published_on WHERE settlement_date = $settlement_date AND published_on <> $published_on;";

    // Every stock the three indices held on or after a settlement, each once, a member announced to join among them, as
    // the stages that store what a member needs read it.
    const string HeldOnOrAfter = @"
        SELECT DISTINCT ticker FROM membership
        WHERE ""left"" IS NULL OR ""left"" > $on
        ORDER BY ticker;
    ";

    const string Insert = @"
        INSERT INTO short_interest (ticker, settlement_date, published_on, shares_short, average_volume, days_to_cover, revision, observed_at, run_id)
        VALUES ($ticker, $settlement_date, $published_on, $shares_short, $average_volume, $days_to_cover, $revision, $observed_at, $run_id)
        ON CONFLICT (ticker, settlement_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<ShortInterestOutcome> RunAsync(IShortInterestFeed feed, bool history, TextWriter? output = null, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var runId = (history ? HistoryPrefix : RunPrefix) + started.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var today = clock.SessionDateAt(started);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // A report is kept as first read, and the day it was published is the rule's and not FINRA's, so each run first
        // moves every stored settlement to the day the rule gives where it stands at another.
        var restated = await RestateAsync(connection, cancellation);
        var newest = await NewestAsync(connection, cancellation);
        var from = history ? ShortInterests.FirstSettlement : newest ?? today.AddDays(-FirstLookBackDays);
        var before = feed.Requests;
        var (read, asked, stored, unanswered) = (0, 0, 0, 0);
        string? refused = null;

        try
        {
            var settlements = await feed.SettlementsAsync(ShortInterests.ReferenceSymbol, from, today, cancellation);

            foreach (var settlement in settlements)
            {
                var tickers = await HeldAsync(connection, settlement, cancellation);
                var bySymbol = tickers
                    .GroupBy(ShortInterests.SymbolOf, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
                var answered = new HashSet<string>(StringComparer.Ordinal);

                foreach (var page in bySymbol.Keys.Order(StringComparer.Ordinal).Chunk(ShortInterests.SymbolsARequest))
                {
                    var reports = await feed.SettlementAsync(settlement, page, cancellation);

                    stored += await StoreAsync(connection, reports, bySymbol, answered, runId, cancellation);
                }

                read++;
                asked += tickers.Count;
                unanswered += tickers.Count(ticker => !answered.Contains(ticker));
                output?.WriteLine(FormattableString.Invariant($"short-interest: the settlement of {Day(settlement)}, published {Day(ShortInterests.PublishedOn(settlement))}, {tickers.Count} stock(s) asked, {tickers.Count(answered.Contains)} answered"));
            }
        }
        catch (Exception failure) when (failure is ProviderRefusal or FormatException || (failure is OperationCanceledException && !cancellation.IsCancellationRequested))
        {
            refused = failure is OperationCanceledException ? "FINRA did not answer in time on any try" : failure.Message;
        }

        var outcome = new ShortInterestOutcome(read, asked, stored, unanswered, feed.Requests - before, refused, restated);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", Stage);
            command.Parameters.AddWithValue("$started_at", Instant(started));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", refused is null ? "ok" : "partial");
            command.Parameters.AddWithValue("$rows_written", stored + restated);
            command.Parameters.AddWithValue("$network_requests", outcome.Requests);
            command.Parameters.AddWithValue("$detail", Detail(outcome, from, today, history));
            await command.ExecuteNonQueryAsync(cancellation);
        }

        return outcome;
    }

    // The row's own sentence.
    public static string Detail(ShortInterestOutcome outcome, DateOnly from, DateOnly through, bool history) =>
        FormattableString.Invariant($"{(history ? "the history" : "the newest settlements")} from {Day(from)} through {Day(through)}: ")
        + FormattableString.Invariant($"{outcome.Settlements} settlement(s) read, {outcome.Asked} stock ask(s), {outcome.Stored} report(s) stored, {outcome.Unanswered} answered nothing, {outcome.Requests} request(s) to FINRA")
        + (outcome.Restated > 0 ? FormattableString.Invariant($"; {outcome.Restated} stored report(s) moved to the day FINRA's publication is counted on") : string.Empty)
        + (outcome.Refused is { } refusal ? "; stopped, " + refusal : string.Empty);

    async Task<int> RestateAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var settlements = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = HeldSettlements;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                settlements.Add(reader.GetString(0));
            }
        }

        var restated = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var settlement in settlements)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = Restate;
            command.Parameters.AddWithValue("$settlement_date", settlement);
            command.Parameters.AddWithValue("$published_on", Day(ShortInterests.PublishedOn(DateOnly.ParseExact(settlement, "yyyy-MM-dd", CultureInfo.InvariantCulture))));
            restated += await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return restated;
    }

    async Task<int> StoreAsync(
        SqliteConnection connection,
        IReadOnlyList<ShortInterestReport> reports,
        IReadOnlyDictionary<string, string[]> bySymbol,
        HashSet<string> answered,
        string runId,
        CancellationToken cancellation)
    {
        var stored = 0;
        var observed = Instant(clock.UtcNow);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var report in reports)
        {
            foreach (var ticker in bySymbol.GetValueOrDefault(report.Symbol) ?? [])
            {
                answered.Add(ticker);

                await using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = Insert;
                command.Parameters.AddWithValue("$ticker", ticker);
                command.Parameters.AddWithValue("$settlement_date", Day(report.Settlement));
                command.Parameters.AddWithValue("$published_on", Day(ShortInterests.PublishedOn(report.Settlement)));
                command.Parameters.AddWithValue("$shares_short", report.SharesShort);
                command.Parameters.AddWithValue("$average_volume", report.AverageVolume is { } volume ? volume : DBNull.Value);
                command.Parameters.AddWithValue("$days_to_cover", report.DaysToCover is { } days ? days : DBNull.Value);
                command.Parameters.AddWithValue("$revision", report.Revision is { } revision ? revision : DBNull.Value);
                command.Parameters.AddWithValue("$observed_at", observed);
                command.Parameters.AddWithValue("$run_id", runId);
                stored += await command.ExecuteNonQueryAsync(cancellation);
            }
        }

        await transaction.CommitAsync(cancellation);

        return stored;
    }

    static async Task<IReadOnlyList<string>> HeldAsync(SqliteConnection connection, DateOnly settlement, CancellationToken cancellation)
    {
        var tickers = new List<string>();

        await using var command = connection.CreateCommand();

        command.CommandText = HeldOnOrAfter;
        command.Parameters.AddWithValue("$on", Day(settlement));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestSettlement;

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
