using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Analysts;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Analysts;

// What a run of the ratings refresh came to: the members it read, the asks it made and what each came to, and why it
// stopped.
public sealed record RefreshOutcome(int Members, int Asked, int Answered, int NoAnalysts, int Failed, string Stopped, int Requests);

// The ratings refresh. A process of its own the off-peak run starts after the labeller, never the night: it reads the
// members of the three indices on the newest session the store holds, those Yahoo was never asked about first and then
// those asked longest ago, each by its newest ask that reached Yahoo whatever it came to, and asks the analyst job for
// each in turn while the New York day's asks, the chart page's among them, stand under the refresh's cap, each answer
// stored by the job as a chart page's is under the job's own row. It stops after three failed asks in a row, since Yahoo
// refusing one is Yahoo refusing the run. It writes one row of its own with the members it read, its asks and why it
// stopped, and calls no model.
// see: The ratings refresh asks Yahoo for the members whose stored answer is oldest or none after the labeller, under the chart page's daily cap less the page's reserve
public sealed class RatingsRefresh(
    IClock clock,
    string databaseFile,
    Func<IAnalystFeed> feeds,
    Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.AnalystView, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = AnalystLimits.RefreshStage;

    public const string RunPrefix = Stage + "-";

    // The run log's words for why a run stopped.
    public const string AtTheCap = "cap reached";

    public const string AllAsked = "every member asked";

    public const string Refused = "Yahoo refused";

    public const string NoSession = "no session stored";

    // The failed asks in a row a run stops at.
    public const int FailuresInARow = 3;

    // The wait between two asks, so a run does not ask Yahoo in a burst.
    public static TimeSpan Between { get; } = TimeSpan.FromSeconds(2);

    readonly Func<TimeSpan, CancellationToken, Task> wait = pause ?? ((span, token) => Task.Delay(span, token));

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // The members the three indices hold on a session, each once.
    const string MembersOn = @"
        SELECT DISTINCT ticker FROM membership
        WHERE (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    // Each stock's newest ask that reached Yahoo, read off the analyst job's own rows, and its newest stored answer, the
    // later of the two its turn.
    const string NewestAsks = @"
        SELECT json_extract(detail, '$.ticker'), MAX(started_at) FROM run_log
        WHERE stage = $stage AND network_requests > 0
        GROUP BY json_extract(detail, '$.ticker');
    ";

    const string NewestAnswers = "SELECT ticker, MAX(asked_at) FROM analyst_view GROUP BY ticker;";

    // The day's asks are the analyst job's runs of the day that asked Yahoo anything, the chart page's and the refresh's.
    const string AskedInTheDay = "SELECT COUNT(*) FROM run_log WHERE stage = $stage AND started_at >= $from AND started_at < $to AND network_requests > 0;";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    public static string RunIdFor(DateTimeOffset started) =>
        RunPrefix + started.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);

    public async Task<RefreshOutcome> RunAsync(CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await NewestAsync(connection, cancellation) is not { } session)
        {
            return await EndAsync(connection, started, new RefreshOutcome(0, 0, 0, 0, 0, NoSession, 0), [], cancellation);
        }

        var members = await InTurnAsync(connection, session, cancellation);
        var asked = new List<string>();
        var (answered, none, failed, inARow, requests) = (0, 0, 0, 0, 0);
        var stopped = AllAsked;

        foreach (var ticker in members)
        {
            if (await AskedTodayAsync(connection, cancellation) >= AnalystLimits.RefreshCap)
            {
                stopped = AtTheCap;

                break;
            }

            if (asked.Count > 0)
            {
                await wait(Between, cancellation);
            }

            var feed = feeds();
            var outcome = await new AnalystJob(clock, databaseFile, feed).RunAsync(ticker, clock.UtcNow, cancellation);

            requests += feed.Requests;

            if (outcome.Outcome == AnalystJob.AtTheCap)
            {
                stopped = AtTheCap;

                break;
            }

            asked.Add($"{ticker} {outcome.Outcome}");

            switch (outcome.Outcome)
            {
                case AnalystJob.Answered:
                    (answered, inARow) = (answered + 1, 0);

                    break;
                case AnalystJob.NoAnalysts:
                    (none, inARow) = (none + 1, 0);

                    break;
                default:
                    (failed, inARow) = (failed + 1, inARow + 1);

                    break;
            }

            if (inARow >= FailuresInARow)
            {
                stopped = Refused;

                break;
            }
        }

        return await EndAsync(connection, started, new RefreshOutcome(members.Count, asked.Count, answered, none, failed, stopped, requests), asked, cancellation);
    }

    // The members in the order the refresh asks them: those never asked first, by ticker, then those whose newest ask is
    // oldest.
    public static IReadOnlyList<string> InTurn(IReadOnlyList<string> members, IReadOnlyDictionary<string, string> newest) =>
        [
            .. members
                .OrderBy(ticker => newest.ContainsKey(ticker) ? 1 : 0)
                .ThenBy(ticker => newest.GetValueOrDefault(ticker) ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(ticker => ticker, StringComparer.Ordinal),
        ];

    async Task<IReadOnlyList<string>> InTurnAsync(SqliteConnection connection, DateOnly session, CancellationToken cancellation)
    {
        var members = new List<string>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MembersOn;
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                members.Add(reader.GetString(0));
            }
        }

        var newest = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (query, staged) in new[] { (NewestAsks, true), (NewestAnswers, false) })
        {
            await using var command = connection.CreateCommand();

            command.CommandText = query;

            if (staged)
            {
                command.Parameters.AddWithValue("$stage", AnalystJob.Stage);
            }

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (reader.IsDBNull(0) || reader.IsDBNull(1))
                {
                    continue;
                }

                var (ticker, at) = (reader.GetString(0), reader.GetString(1));

                if (!newest.TryGetValue(ticker, out var held) || string.CompareOrdinal(at, held) > 0)
                {
                    newest[ticker] = at;
                }
            }
        }

        return InTurn(members, newest);
    }

    async Task<int> AskedTodayAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var (from, to) = AnalystLimits.DayOf(clock.UtcNow, clock.SessionZone);

        await using var command = connection.CreateCommand();

        command.CommandText = AskedInTheDay;
        command.Parameters.AddWithValue("$stage", AnalystJob.Stage);
        command.Parameters.AddWithValue("$from", Instant(from));
        command.Parameters.AddWithValue("$to", Instant(to));

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellation), CultureInfo.InvariantCulture);
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    async Task<RefreshOutcome> EndAsync(SqliteConnection connection, DateTimeOffset started, RefreshOutcome outcome, IReadOnlyList<string> asked, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", RunIdFor(started));
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", Instant(started));
        command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        command.Parameters.AddWithValue("$outcome", outcome.Stopped == Refused ? "stopped" : "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.Answered);
        command.Parameters.AddWithValue("$network_requests", outcome.Requests);
        command.Parameters.AddWithValue("$detail", JsonSerializer.Serialize(new
        {
            members = outcome.Members,
            asked = outcome.Asked,
            answered = outcome.Answered,
            noAnalysts = outcome.NoAnalysts,
            failed = outcome.Failed,
            cap = AnalystLimits.RefreshCap,
            stopped = outcome.Stopped,
            each = asked,
        }));
        await command.ExecuteNonQueryAsync(cancellation);

        return outcome;
    }

    static string Instant(DateTimeOffset instant) => instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
