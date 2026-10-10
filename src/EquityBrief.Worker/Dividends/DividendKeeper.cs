using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Dividends;

// What one keeping wrote: the dividends handed or answered, the rows new to the store, the members asked and those not
// served with why, and the requests made, none on a night.
public sealed record DividendsKept(int Handed, int Kept, int Asked = 0, IReadOnlyList<string>? NotServed = null, int Requests = 0);

// The dividends a member paid, kept for the name page's dividend history and its safety.
//
// Two ways in, one writer. On the night, the dividends the night's bulk answer carried for the names the night stores,
// which the corporate action check has already asked for, so keeping them asks nothing. And on the operator's command,
// the dividends history run: each member an index holds today asked once for its dividends from a date, one request a
// member at the historical weight, stated before the first request and held at the stop a pull is held at. One row a
// member and ex-dividend date, the first kept standing, so a date the night and the history both name is written once and
// never rewritten. Each amount is as the provider restated it for splits on the day it was read, beside the amount paid.
// see: Each dividend a member paid is kept from the night's bulk answer and from one history run, and read as the provider restated it on the day it was read
public sealed class DividendKeeper(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.DividendEvent, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.SplitsAndDividends]);

    public const string Stage = "dividends";
    public const string HistoryStage = "dividends-history";

    // The run ids the history run is written under, which the run page reads as a run by hand.
    public const string HistoryPrefix = "dividends-history-";

    public const string Night = "night";
    public const string History = "history";

    // The members an index holds on a session, each once.
    const string MembersOn = @"
        SELECT DISTINCT ticker FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string Insert = @"
        INSERT INTO dividend_event (ticker, ex_date, amount, unadjusted, declared_on, record_on, paid_on, period, currency, source, run_id)
        VALUES ($ticker, $ex_date, $amount, $unadjusted, $declared_on, $record_on, $paid_on, $period, $currency, $source, $run_id)
        ON CONFLICT (ticker, ex_date) DO NOTHING;
    ";

    const string Held = "SELECT COUNT(*) FROM dividend_event;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // The night's dividends, handed by the corporate action check from the answer it asked for, each a name the night
    // stores. Asks nothing and writes its own row.
    public async Task<DividendsKept> KeepTheNightAsync(IReadOnlyList<DividendPaid> paid, string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var kept = await KeepAsync(connection, paid, Night, runId, cancellation);
        var outcome = new DividendsKept(paid.Count, kept);

        await AppendAsync(
            connection,
            runId,
            Stage,
            startedAt,
            "ok",
            kept,
            0,
            FormattableString.Invariant($"{paid.Count} dividend(s) the night's answer carried for the names it stores, {kept} new to the store"),
            cancellation);

        return outcome;
    }

    // The verb a person runs: `dividends --history --from <yyyy-MM-dd>`, with `--index` naming the index whose members
    // today are asked, the S&P 500's where none is named, `--names` with codes between commas asking only those members,
    // and the stop's word to go past it. States its asks and their weighted calls before the first request.
    public static async Task<int> RunAsync(
        string[] args,
        Func<IDividendHistoryFeed> feed,
        IClock clock,
        string databaseFile,
        TextWriter output,
        TextWriter error)
    {
        if (!VerbArguments.Has(args, "--history"))
        {
            error.WriteLine("dividends: the one run by hand is '--history --from <yyyy-MM-dd>', which asks each member today for its dividends from that date.");

            return 2;
        }

        if (VerbArguments.Value(args, "--from") is not { } given
            || !DateOnly.TryParseExact(given, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var from))
        {
            error.WriteLine("dividends: name the first ex-dividend date to ask for with '--from <yyyy-MM-dd>'.");

            return 2;
        }

        IReadOnlyCollection<string>? only = VerbArguments.Value(args, "--names") is { } listed
            ? [.. listed.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)]
            : null;

        IDividendHistoryFeed resolved;

        try
        {
            resolved = feed();
        }
        catch (Exception refusal) when (refusal is InvalidOperationException or DirectoryNotFoundException)
        {
            error.WriteLine("dividends: " + refusal.Message);

            return 1;
        }

        var runId = FormattableString.Invariant($"{HistoryPrefix}{clock.UtcNow:yyyyMMddTHHmmss.fffffffZ}");

        try
        {
            var outcome = await new DividendKeeper(clock, databaseFile).HistoryAsync(
                resolved,
                VerbArguments.Value(args, "--index") ?? "GSPC",
                from,
                runId,
                line => output.WriteLine("dividends: " + line),
                only: only,
                pastTheStop: VerbArguments.Has(args, ProviderStop.PastTheStop));

            output.WriteLine("dividends: " + runId);
            output.WriteLine("dividends: " + HistoryDetail(outcome));

            return 0;
        }
        catch (ArgumentException refusal)
        {
            error.WriteLine("dividends: " + refusal.Message);

            return 1;
        }
    }

    // Each member the index holds on today's session asked once for its dividends from a date, those not served named
    // and the rest kept, in one write after the last answer.
    public async Task<DividendsKept> HistoryAsync(
        IDividendHistoryFeed feed,
        string indexCode,
        DateOnly from,
        string runId,
        Action<string>? stating = null,
        CancellationToken cancellation = default,
        IReadOnlyCollection<string>? only = null,
        bool pastTheStop = false)
    {
        var startedAt = clock.UtcNow;
        var session = clock.SessionDateAt(startedAt);

        if (from > session)
        {
            throw new ArgumentException(FormattableString.Invariant($"The first date asked for, {from:yyyy-MM-dd}, is after today's session, {session:yyyy-MM-dd}, so nothing could be answered."), nameof(from));
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, session, cancellation);
        var asked = only is null ? members : [.. members.Where(ticker => only.Contains(ticker, StringComparer.Ordinal))];
        var weighted = asked.Count * ProviderWeights.HistoricalPerTicker;

        // Stated before the first request, and held at the stop a pull is held at.
        // see: Every pull states its provider calls before it runs and stops for the operator at the day's cap
        var what = FormattableString.Invariant($"{asked.Count:N0} member(s) of {indexCode} today, one request each, from {from:yyyy-MM-dd}");

        stating?.Invoke(ProviderStop.Stated(what, weighted));
        ProviderStop.Hold(what, weighted, pastTheStop);

        var requestsBefore = feed.Requests;
        var answered = new List<DividendPaid>();
        var notServed = new List<string>();

        foreach (var ticker in asked)
        {
            try
            {
                answered.AddRange((await feed.DividendsAsync(ticker, from, cancellation)).Where(paid => paid.ExDate >= from && paid.ExDate <= session));
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                notServed.Add($"{ticker}: the provider did not answer in time on any try");
            }
            catch (Exception failure) when (failure is ProviderRefusal or FormatException or JsonException)
            {
                notServed.Add($"{ticker}: {failure.Message}");
            }
        }

        var kept = await KeepAsync(connection, answered, History, runId, cancellation);
        var outcome = new DividendsKept(answered.Count, kept, asked.Count, notServed, feed.Requests - requestsBefore);

        await AppendAsync(
            connection,
            runId,
            HistoryStage,
            startedAt,
            notServed.Count == 0 ? "ok" : "partial",
            kept,
            outcome.Requests,
            JsonSerializer.Serialize(new { index = indexCode, from = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), asked = asked.Count, answered = answered.Count, kept, notServed }),
            cancellation);

        return outcome;
    }

    public static string HistoryDetail(DividendsKept outcome) =>
        FormattableString.Invariant($"{outcome.Asked} member(s) asked, {(outcome.NotServed ?? []).Count} not served, {outcome.Handed} dividend(s) answered, {outcome.Kept} new to the store, {outcome.Requests} request(s)");

    async Task<int> KeepAsync(SqliteConnection connection, IReadOnlyList<DividendPaid> paid, string source, string runId, CancellationToken cancellation)
    {
        var before = await CountAsync(connection, cancellation);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var dividend in paid)
        {
            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = Insert;
            insert.Parameters.AddWithValue("$ticker", dividend.Ticker);
            insert.Parameters.AddWithValue("$ex_date", Day(dividend.ExDate));
            insert.Parameters.AddWithValue("$amount", Money.ToStorage(dividend.Amount));
            insert.Parameters.AddWithValue("$unadjusted", dividend.Unadjusted is { } paidAs ? Money.ToStorage(paidAs) : DBNull.Value);
            insert.Parameters.AddWithValue("$declared_on", dividend.DeclaredOn is { } declared ? Day(declared) : DBNull.Value);
            insert.Parameters.AddWithValue("$record_on", dividend.RecordOn is { } recorded ? Day(recorded) : DBNull.Value);
            insert.Parameters.AddWithValue("$paid_on", dividend.PaidOn is { } paidOn ? Day(paidOn) : DBNull.Value);
            insert.Parameters.AddWithValue("$period", (object?)dividend.Period ?? DBNull.Value);
            insert.Parameters.AddWithValue("$currency", (object?)dividend.Currency ?? DBNull.Value);
            insert.Parameters.AddWithValue("$source", source);
            insert.Parameters.AddWithValue("$run_id", runId);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return await CountAsync(connection, cancellation) - before;
    }

    static async Task<IReadOnlyList<string>> MembersAsync(SqliteConnection connection, string indexCode, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", Day(session));

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<int> CountAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Held;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    async Task AppendAsync(SqliteConnection connection, string runId, string stage, DateTimeOffset startedAt, string outcome, int rows, int requests, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$started_at", Instant(startedAt));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$network_requests", requests);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
}
