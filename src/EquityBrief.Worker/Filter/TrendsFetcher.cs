using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Filter;

// What the trends fetcher did on a night: the members it read, those it asked for, those whose current fiscal year it
// stored, those an answer held no current year for, those it did not ask for and why, and its requests and the symbols
// they asked for.
public sealed record TrendsOutcome(int Members, int Asked, int Stored, int Unanswered, int NotAsked, int Requests, int Symbols, string? Refused);

// The trends fetcher. In the swing filter's stage, after the estimates fetcher, it asks the provider's earnings trends for
// every member the three indices hold on the night that holds no trend stored for the night, a page of symbols a
// request at one weighted call a symbol, and stores each member's current fiscal year from the answer under the night's
// session, as first read. A page whose ask would take the night past the day's allowance is not asked, nor any after
// it; a page the provider refuses or does not answer in time stores nothing and the pages after it are still asked. A
// member whose answer holds no current year is counted and stores nothing, and a night run again for an earlier
// session asks for none, since a trend is the day's it is asked and not that night's. It writes one row of its own each
// night the stage runs, and calls no model.
// see: The night asks every member's estimate trend once a night in requests of a page of symbols
// see: The nightly run is arithmetic only
public sealed class TrendsFetcher(IEstimateTrendsFeed? feed, Func<int> weightedSoFar, IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.EstimateTrendNight, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.EstimateTrends]);

    public const string Stage = "trends";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // The members the three indices hold on a session, each once.
    const string MembersOn = @"
        SELECT DISTINCT ticker FROM membership
        WHERE (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string StoredOn = "SELECT ticker FROM estimate_trend_night WHERE session_date = $session;";

    const string Insert = @"
        INSERT INTO estimate_trend_night (
            ticker, session_date, period_end, eps_now, eps_seven_days_ago, eps_thirty_days_ago, eps_sixty_days_ago,
            eps_ninety_days_ago, up_last_seven_days, up_last_thirty_days, down_last_seven_days, down_last_thirty_days, run_id)
        VALUES (
            $ticker, $session_date, $period_end, $eps_now, $eps_seven_days_ago, $eps_thirty_days_ago, $eps_sixty_days_ago,
            $eps_ninety_days_ago, $up_last_seven_days, $up_last_thirty_days, $down_last_seven_days, $down_last_thirty_days, $run_id)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public async Task<TrendsOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await NewestAsync(connection, cancellation) is not { } session)
        {
            return await EndAsync(connection, runId, started, new TrendsOutcome(0, 0, 0, 0, 0, 0, 0, null), "no session is stored, so no member was read", cancellation);
        }

        var members = await MembersAsync(connection, session, cancellation);
        var held = await HeldAsync(connection, session, cancellation);
        var wanted = members.Where(ticker => !held.Contains(ticker)).ToArray();

        if (feed is null)
        {
            return await EndAsync(
                connection,
                runId,
                started,
                new TrendsOutcome(members.Count, 0, 0, 0, wanted.Length, 0, 0, null),
                FormattableString.Invariant($"{members.Count} member(s), {held.Count} held already; none asked for, since this night was run again for an earlier session or given no trends feed"),
                cancellation);
        }

        var (requests, symbols) = (feed.Requests, feed.Symbols);
        var (asked, stored, unanswered, notAsked) = (0, 0, 0, 0);
        var refusals = new List<string>();
        var stoppedAtTheAllowance = false;

        foreach (var page in wanted.Chunk(EstimateTrends.PageSymbols))
        {
            if (stoppedAtTheAllowance || weightedSoFar() + (page.Length * ProviderWeights.EstimateTrends) > ProviderWeights.DailyAllowance)
            {
                stoppedAtTheAllowance = true;
                notAsked += page.Length;

                continue;
            }

            asked += page.Length;

            IReadOnlyList<NightTrend> trends;

            try
            {
                trends = await feed.TrendsAsync(page, cancellation);
            }
            catch (OperationCanceledException) when (!cancellation.IsCancellationRequested)
            {
                refusals.Add(FormattableString.Invariant($"a page of {page.Length} from {page[0]} not answered in time on any try"));
                unanswered += page.Length;

                continue;
            }
            catch (Exception failure) when (failure is ProviderRefusal or FormatException or System.Text.Json.JsonException)
            {
                refusals.Add(FormattableString.Invariant($"a page of {page.Length} from {page[0]} refused: {failure.Message}"));
                unanswered += page.Length;

                continue;
            }

            var answered = trends.Where(trend => page.Contains(trend.Ticker, StringComparer.Ordinal)).ToArray();

            stored += await StoreAsync(connection, session, answered, runId, cancellation);
            unanswered += page.Length - answered.Select(trend => trend.Ticker).Distinct(StringComparer.Ordinal).Count();
        }

        var outcome = new TrendsOutcome(members.Count, asked, stored, unanswered, notAsked, feed.Requests - requests, feed.Symbols - symbols, refusals.Count == 0 ? null : string.Join("; ", refusals));

        return await EndAsync(
            connection,
            runId,
            started,
            outcome,
            FormattableString.Invariant($"{members.Count} member(s), {held.Count} held already, {asked} asked for in {outcome.Requests} request(s) at {outcome.Symbols * ProviderWeights.EstimateTrends} weighted call(s), {stored} stored, {unanswered} answered no current year")
                + (notAsked == 0 ? string.Empty : FormattableString.Invariant($", {notAsked} not asked since their ask would take the night past the day's allowance of {ProviderWeights.DailyAllowance}"))
                + (outcome.Refused is { } refused ? "; " + refused : string.Empty),
            cancellation);
    }

    async Task<int> StoreAsync(SqliteConnection connection, DateOnly session, IReadOnlyList<NightTrend> trends, string runId, CancellationToken cancellation)
    {
        var stored = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var trend in trends)
        {
            await using var command = connection.CreateCommand();

            command.Transaction = transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$ticker", trend.Ticker);
            command.Parameters.AddWithValue("$session_date", Day(session));
            command.Parameters.AddWithValue("$period_end", Day(trend.PeriodEnd));
            command.Parameters.AddWithValue("$eps_now", Stored(trend.Now));
            command.Parameters.AddWithValue("$eps_seven_days_ago", Stored(trend.WeekBefore));
            command.Parameters.AddWithValue("$eps_thirty_days_ago", Stored(trend.MonthBefore));
            command.Parameters.AddWithValue("$eps_sixty_days_ago", Stored(trend.TwoMonthsBefore));
            command.Parameters.AddWithValue("$eps_ninety_days_ago", Stored(trend.QuarterBefore));
            command.Parameters.AddWithValue("$up_last_seven_days", Count(trend.UpWeek));
            command.Parameters.AddWithValue("$up_last_thirty_days", Count(trend.UpMonth));
            command.Parameters.AddWithValue("$down_last_seven_days", Count(trend.DownWeek));
            command.Parameters.AddWithValue("$down_last_thirty_days", Count(trend.DownMonth));
            command.Parameters.AddWithValue("$run_id", runId);
            stored += await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return stored;
    }

    static object Stored(decimal? figure) => figure is { } value ? Money.ToStorage(value) : DBNull.Value;

    static object Count(int? count) => count is { } value ? value : DBNull.Value;

    static async Task<IReadOnlyList<string>> MembersAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        var members = new List<string>();

        await using var command = connection.CreateCommand();

        command.CommandText = MembersOn;
        command.Parameters.AddWithValue("$session", Day(session));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<HashSet<string>> HeldAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        var held = new HashSet<string>(StringComparer.Ordinal);

        await using var command = connection.CreateCommand();

        command.CommandText = StoredOn;
        command.Parameters.AddWithValue("$session", Day(session));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held.Add(reader.GetString(0));
        }

        return held;
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    async Task<TrendsOutcome> EndAsync(SqliteConnection connection, string runId, DateTimeOffset started, TrendsOutcome outcome, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Instant(started));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome.Refused is null ? "ok" : "partial");
        command.Parameters.AddWithValue("$rows_written", outcome.Stored);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue("$detail", detail);
        await command.ExecuteNonQueryAsync(cancellation);

        return outcome;
    }

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
