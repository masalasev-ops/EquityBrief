using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

public sealed record LateBarAnswer(string Ticker, string Outcome, DateTimeOffset? AskedAt, string Why);

public sealed record LateBarsOutcome(DateOnly? Session, IReadOnlyList<LateBarAnswer> Answers, int Requests, string? NotAsked = null)
{
    public int Arrived => Answers.Count(answer => answer.Outcome == LateBars.Arrived);
}

// The second ask for a member the night's day file carried nothing for: a request of its own at the end of the night's
// fetch, and once more in the first off-peak run after the night finished, at most `LateBars.Most` asks a night across the
// two, each stated on a row of its own. A bar the provider now holds is written as the day file's would have been, the
// ordinary append under a source of its own that moves no stored bar. One written at the fetch is read by the night's own
// steps, its rules among them; one written in the off-peak run has its readings worked by `LateBarReadings` and moves no
// pick, list, trade or ledger row. A member still missing is left to the corporate action check's suspect path.
// see: A member missing from the night's day file is asked for with its own request at the end of the fetch and once more in the off-peak run, at most 20 asks a night
public sealed class LateBarAsker : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read | Touch.Insert),
            new StoreTouch(Store.LateBarAsk, Touch.Read | Touch.Insert),
            new StoreTouch(Store.SeriesState, Touch.Read | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.HistoricalPrice]);

    public const string Stage = "late-bars";

    const string StoredBefore = @"
        SELECT session_date, close, volume FROM bar
        WHERE ticker = $ticker AND session_date < $session AND session_date >= $from
        ORDER BY session_date;
    ";

    const string StoreBar = @"
        INSERT INTO bar (
            ticker, session_date, open, high, low, close,
            volume, source, observed_at, raw_close)
        VALUES (
            $ticker, $session_date, $open, $high, $low, $close,
            $volume, $source, $observed_at, $raw_close)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string StoreAsk = @"
        INSERT INTO late_bar_ask (ticker, session_date, stage, asked_at, outcome, detail, run_id)
        VALUES ($ticker, $session_date, $stage, $asked_at, $outcome, $detail, $run_id)
        ON CONFLICT (ticker, session_date, stage) DO UPDATE SET
            asked_at = excluded.asked_at,
            outcome = excluded.outcome,
            detail = excluded.detail,
            run_id = excluded.run_id;
    ";

    const string NewestNight = "SELECT MAX(session_date) FROM bar WHERE source = 'bulk';";

    // The night's last step's row at or after the fetch's asks, which says the night has finished writing.
    const string NightFinished = @"
        SELECT EXISTS (
            SELECT 1 FROM run_log l
            WHERE l.stage = 'backup' AND l.started_at >= (SELECT MIN(a.asked_at) FROM late_bar_ask a WHERE a.session_date = $session AND a.stage = 'fetch'));
    ";

    const string AskedAtTheFetch = @"
        SELECT a.ticker FROM late_bar_ask a
        WHERE a.session_date = $session AND a.stage = 'fetch'
          AND NOT EXISTS (SELECT 1 FROM bar b WHERE b.ticker = a.ticker AND b.session_date = a.session_date)
          AND NOT EXISTS (SELECT 1 FROM late_bar_ask o WHERE o.ticker = a.ticker AND o.session_date = a.session_date AND o.stage = 'off-peak')
        ORDER BY a.ticker;
    ";

    const string AsksSoFar = "SELECT COUNT(*) FROM late_bar_ask WHERE session_date = $session AND outcome <> 'past the limit';";

    const string StateOf = "SELECT reason FROM series_state WHERE ticker = $ticker AND state = 'suspect';";

    const string SetBackToOk = @"
        UPDATE series_state SET state = 'ok', reason = NULL, checked_at = $checked_at, retries = 0
        WHERE ticker = $ticker AND state = 'suspect';
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    readonly IHistoricalBarFeed history;
    readonly IClock clock;
    readonly string databaseFile;

    public LateBarAsker(IHistoricalBarFeed history, IClock clock, string databaseFile)
    {
        this.history = history;
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The members the night's own file carried nothing for, once that file is stored.
    public async Task<LateBarsOutcome> AtTheFetchAsync(string runId, DateOnly session, IReadOnlyList<string> missing, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var outcome = await AskAsync(connection, runId, session, LateBars.AtTheFetch, missing, LateBars.Most, cancellation);

        await AppendAsync(connection, runId, started, outcome, string.Empty, cancellation);

        return outcome;
    }

    // Once more for those still missing, in the first off-peak run after the night has finished; a run while the night is
    // still writing, or after this night's second ask, asks nothing and says so.
    public async Task<LateBarsOutcome> OffPeakAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await ScalarAsync(connection, NewestNight, cancellation) is not string newest)
        {
            return await NothingAsync(connection, runId, started, null, "the store holds no night's bars", cancellation);
        }

        var session = DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var named = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AskedAtTheFetch;
            command.Parameters.AddWithValue("$session", newest);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                named.Add(reader.GetString(0));
            }
        }

        if (named.Count == 0)
        {
            return await NothingAsync(connection, runId, started, session, $"no member of the night of {newest} is left to ask for", cancellation);
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NightFinished;
            command.Parameters.AddWithValue("$session", newest);

            if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture) == 0)
            {
                return await NothingAsync(connection, runId, started, session, $"the night of {newest} has not finished, so a later off-peak run asks", cancellation);
            }
        }

        long asked;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = AsksSoFar;
            command.Parameters.AddWithValue("$session", newest);
            asked = Convert.ToInt64(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
        }

        var outcome = await AskAsync(connection, runId, session, LateBars.OffPeak, named, LateBars.Most - (int)asked, cancellation);
        var arrived = outcome.Answers.Where(answer => answer.Outcome == LateBars.Arrived).Select(answer => answer.Ticker).ToArray();

        await ClearAnsweredAsync(connection, session, arrived, cancellation);

        var worked = string.Empty;

        if (arrived.Length > 0)
        {
            worked = await new LateBarReadings(clock, databaseFile).WorkAsync(runId, session, arrived, cancellation);
        }

        await AppendAsync(connection, runId, started, outcome, worked, cancellation);

        return outcome;
    }

    async Task<LateBarsOutcome> AskAsync(SqliteConnection connection, string runId, DateOnly session, string stage, IReadOnlyList<string> named, int most, CancellationToken cancellation)
    {
        var answers = new List<LateBarAnswer>();
        var from = history.Requests;
        var asked = 0;

        foreach (var ticker in named.OrderBy(name => name, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();

            if (asked >= most)
            {
                var why = FormattableString.Invariant($"past the {LateBars.Most} asks a night makes");

                answers.Add(new LateBarAnswer(ticker, LateBars.PastTheLimit, null, why));
                await StoreAskAsync(connection, runId, ticker, session, stage, null, LateBars.PastTheLimit, why, cancellation);

                continue;
            }

            asked++;

            var at = clock.UtcNow;
            ProviderBar? bar = null;
            string outcome;
            string said;

            try
            {
                var stored = await StoredBeforeAsync(connection, ticker, session, cancellation);
                var answer = await history.BarsAsync(ticker, session.AddDays(-LateBars.ReachDays), session, cancellation);

                (bar, outcome, said) = LateBars.Judge(session, answer, stored);
            }
            catch (Exception failure) when (!cancellation.IsCancellationRequested)
            {
                (outcome, said) = (LateBars.Failed, "the request failed: " + failure.Message);
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            if (bar is not null)
            {
                await StoreBarAsync(connection, (SqliteTransaction)transaction, ticker, bar, at, cancellation);
            }

            await StoreAskAsync(connection, runId, ticker, session, stage, at, outcome, said, cancellation, (SqliteTransaction)transaction);
            await transaction.CommitAsync(cancellation);

            answers.Add(new LateBarAnswer(ticker, outcome, at, said));
        }

        return new LateBarsOutcome(session, answers, history.Requests - from);
    }

    // A name marked suspect only because its answer ended short, which this bar now answers, is set back to ok; one holding
    // copied candles, or still behind by a session before the night, stays as the check left it.
    async Task ClearAnsweredAsync(SqliteConnection connection, DateOnly session, IReadOnlyList<string> arrived, CancellationToken cancellation)
    {
        foreach (var ticker in arrived)
        {
            await using var read = connection.CreateCommand();

            read.CommandText = StateOf;
            read.Parameters.AddWithValue("$ticker", ticker);

            if (await read.ExecuteScalarAsync(cancellation) is not string reason || SuspectReason.Read(reason) is not { Copied: false } fault)
            {
                continue;
            }

            var before = await StoredBeforeAsync(connection, ticker, session, cancellation);

            if (before.Count == 0 || before.Keys.Max() != fault.Newest)
            {
                continue;
            }

            await using var clear = connection.CreateCommand();

            clear.CommandText = SetBackToOk;
            clear.Parameters.AddWithValue("$ticker", ticker);
            clear.Parameters.AddWithValue("$checked_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));

            await clear.ExecuteNonQueryAsync(cancellation);
        }
    }

    static async Task<IReadOnlyDictionary<DateOnly, (decimal Close, long Volume)>> StoredBeforeAsync(SqliteConnection connection, string ticker, DateOnly session, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = StoredBefore;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$from", session.AddDays(-LateBars.ReachDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var stored = new Dictionary<DateOnly, (decimal Close, long Volume)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            stored[DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture)] = (Money.FromStorage(reader.GetString(1)), reader.GetInt64(2));
        }

        return stored;
    }

    static async Task StoreBarAsync(SqliteConnection connection, SqliteTransaction transaction, string ticker, ProviderBar bar, DateTimeOffset at, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = StoreBar;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$open", Money.ToStorage(bar.Open));
        command.Parameters.AddWithValue("$high", Money.ToStorage(bar.High));
        command.Parameters.AddWithValue("$low", Money.ToStorage(bar.Low));
        command.Parameters.AddWithValue("$close", Money.ToStorage(bar.Close));
        command.Parameters.AddWithValue("$volume", bar.Volume);
        command.Parameters.AddWithValue("$source", LateBars.Source);
        command.Parameters.AddWithValue("$observed_at", at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$raw_close", Money.ToStorage(bar.RawClose));

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task StoreAskAsync(SqliteConnection connection, string runId, string ticker, DateOnly session, string stage, DateTimeOffset? at, string outcome, string detail, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = StoreAsk;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$asked_at", at is { } instant ? instant.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$detail", detail);
        command.Parameters.AddWithValue("$run_id", runId);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // What the step's row and the night's fetch line say of the asks.
    public static string Detail(LateBarsOutcome outcome) =>
        outcome.NotAsked ?? (outcome.Answers.Count == 0
            ? "no member to ask for"
            : string.Join("; ", outcome.Answers.Select(answer => $"{answer.Ticker} {answer.Outcome}: {answer.Why}")));

    async Task<LateBarsOutcome> NothingAsync(SqliteConnection connection, string runId, DateTimeOffset started, DateOnly? session, string why, CancellationToken cancellation)
    {
        var outcome = new LateBarsOutcome(session, [], 0, why);

        await AppendAsync(connection, runId, started, outcome, string.Empty, cancellation);

        return outcome;
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset started, LateBarsOutcome outcome, string worked, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.Arrived);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue("$detail", Detail(outcome) + worked);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        return await command.ExecuteScalarAsync(cancellation);
    }
}
