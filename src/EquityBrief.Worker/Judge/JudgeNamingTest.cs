using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Local;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Judge;

// What one round of a naming test came to: the members asked, those answered, and those whose company the answer named.
public sealed record JudgeNamingRound(string Round, int Asked, int Answered, int Named)
{
    public bool Passed => JudgeNaming.Passed(Named, Answered) && Answered * JudgeNaming.Hundred >= (JudgeNaming.Hundred - JudgeNamingTest.MostRefused) * Asked;
}

// What a naming test came to: its run, the night and the field list's version it read, each round, and why it stopped
// where it stopped short.
public sealed record JudgeNamingOutcome(string Run, DateOnly Session, string Version, IReadOnlyList<JudgeNamingRound> Rounds, string? Stopped)
{
    public bool Passed => Stopped is null && Rounds.Count == 2 && Rounds.All(round => round.Passed);

    // From 24.24 the index whose members it drew.
    public string? Index { get; init; }
}

// The naming test, by hand on the operator's word: a seeded sample of 100 members of a night's stored facts, from 24.24 of
// one index's members, each asked alone of the local model under the local model's lock, its short row and then its full
// sheet, every answer stored with whether it named the company; a round passes where at most 2 in 100 of its answers named
// it and at most 5 in 100 were refused, and the picker picks in an index only under a test of that index passed. It asks
// nothing of a network, takes the lock or asks nothing, and writes one run log row of its own.
// see: The AI stock picker starts only where the local model names no more than 2 in 100 of the companies behind a sample of its facts
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
public sealed class JudgeNamingTest(IReadingModelFeed feed, LocalModelSettings settings, IClock clock, string databaseFile, string dataRoot, Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.JudgeFact, Touch.Read),
            new StoreTouch(Store.ListedName, Touch.Read),
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.JudgeNaming, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.LocalModel]);

    public const string Stage = "judge-naming";

    // The share of a round's members the model may refuse, in 100, before the round fails for answering too few.
    public const int MostRefused = 5;

    public const string LockHeld = "another local process held the local model's lock until the instant given";

    const string Facts = @"
        SELECT f.index_code, f.ticker, f.row_text, f.sheet_text,
            COALESCE((SELECT l.name FROM listed_name l WHERE l.ticker = f.ticker),
                     (SELECT m.name FROM membership m WHERE m.ticker = f.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1))
        FROM judge_fact f WHERE f.session_date = $session AND f.version = $version AND f.index_code = $index ORDER BY f.ticker;";

    const string Insert = @"
        INSERT INTO judge_naming (run_id, session_date, version, round, index_code, ticker, seed, outcome, answer, named, model, asked_at)
        VALUES ($run_id, $session, $version, $round, $index, $ticker, $seed, $outcome, $answer, $named, $model, $asked_at);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows, $calls, 0, '0', $detail);";

    // The newest test under a version of one index's members, each round's asked, answered and named.
    const string Newest = @"
        SELECT round, COUNT(*), SUM(outcome = 'answered'), SUM(named) FROM judge_naming
        WHERE run_id = (SELECT run_id FROM judge_naming WHERE version = $version AND index_code = $index ORDER BY asked_at DESC, rowid DESC LIMIT 1)
            AND index_code = $index
        GROUP BY round ORDER BY round;";

    // The seeded draw of the members asked: every member of the night's facts shuffled by the seed, the first 100 kept.
    public static IReadOnlyList<T> Draw<T>(IReadOnlyList<T> members, int seed)
    {
        var shuffled = members.ToArray();
        var random = new Random(seed);

        for (var at = shuffled.Length - 1; at > 0; at--)
        {
            var other = random.Next(at + 1);

            (shuffled[at], shuffled[other]) = (shuffled[other], shuffled[at]);
        }

        return [.. shuffled.Take(JudgeNaming.SampleSize)];
    }

    public async Task<JudgeNamingOutcome> RunAsync(DateOnly session, int seed, DateTimeOffset? until, string runId, string index, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var members = new List<(string Index, string Ticker, string Row, string Sheet, string? Name)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Facts;
            command.Parameters.AddWithValue("$session", day);
            command.Parameters.AddWithValue("$version", JudgeFacts.Version);
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                members.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetString(4)));
            }
        }

        var drawn = Draw(members, seed);
        var tallies = new Dictionary<string, (int Asked, int Answered, int Named)>(StringComparer.Ordinal) { [JudgeNaming.Row] = (0, 0, 0), [JudgeNaming.Sheet] = (0, 0, 0) };
        string? stopped = drawn.Count < JudgeNaming.SampleSize
            ? FormattableString.Invariant($"the night's facts on the {Core.Candidates.OwnStepCandidate.NameOf(index)} under field list {JudgeFacts.Version} hold {members.Count} member(s), fewer than the {JudgeNaming.SampleSize} drawn")
            : null;
        var calls = 0;

        await using (var held = stopped is null ? await LocalLocks.TakeAsync(dataRoot, until, clock, pause ?? Task.Delay, cancellation) : null)
        {
            if (stopped is null && held is null)
            {
                stopped = LockHeld;
            }

            if (stopped is null)
            {
                var loaded = await feed.PrepareAsync(cancellation);

                try
                {
                    foreach (var round in new[] { JudgeNaming.Row, JudgeNaming.Sheet })
                    {
                        foreach (var member in drawn)
                        {
                            if (until is { } last && clock.UtcNow >= last)
                            {
                                stopped ??= FormattableString.Invariant($"the instant given, {last:yyyy-MM-ddTHH:mm}Z, was reached");

                                break;
                            }

                            var request = new ReadingRequest(settings.Model, JudgeNaming.System, JudgeNaming.Prompt(round == JudgeNaming.Row ? member.Row : member.Sheet), seed, JudgeNaming.AnswerTokens, JudgeNaming.Schema());
                            string outcome;
                            string? answer = null;
                            string? model = null;
                            var named = false;

                            calls++;

                            try
                            {
                                var reply = await feed.AskAsync(request, cancellation);

                                (outcome, answer, model) = ("answered", reply.Content, reply.Model);
                                (var company, var ticker) = Answer(reply.Content);
                                named = JudgeNaming.Named(company, ticker, member.Ticker, member.Name);
                            }
                            catch (ReadingRefused refused)
                            {
                                (outcome, answer) = ("refused", $"{refused.Cause}: {refused.Message}");
                            }

                            var (asked, answered, count) = tallies[round];

                            tallies[round] = (asked + 1, answered + (outcome == "answered" ? 1 : 0), count + (named ? 1 : 0));

                            await using var insert = connection.CreateCommand();

                            insert.CommandText = Insert;

                            foreach (var (name, value) in new (string, object?)[]
                            {
                                ("$run_id", runId), ("$session", day), ("$version", JudgeFacts.Version), ("$round", round), ("$index", member.Index),
                                ("$ticker", member.Ticker), ("$seed", seed), ("$outcome", outcome), ("$answer", answer), ("$named", named ? 1 : 0),
                                ("$model", model), ("$asked_at", Stamp(clock.UtcNow)),
                            })
                            {
                                insert.Parameters.AddWithValue(name, value ?? DBNull.Value);
                            }

                            await insert.ExecuteNonQueryAsync(cancellation);
                        }
                    }
                }
                finally
                {
                    await feed.ReleaseAsync(loaded, cancellation);
                }
            }
        }

        var rounds = tallies.Select(pair => new JudgeNamingRound(pair.Key, pair.Value.Asked, pair.Value.Answered, pair.Value.Named)).ToArray();
        var result = new JudgeNamingOutcome(runId, session, JudgeFacts.Version, rounds, stopped) { Index = index };

        await using (var log = connection.CreateCommand())
        {
            log.CommandText = AppendRun;

            foreach (var (name, value) in new (string, object)[]
            {
                ("$run_id", runId), ("$stage", Stage), ("$started_at", Stamp(started)), ("$ended_at", Stamp(clock.UtcNow)),
                ("$outcome", result.Passed ? "passed" : stopped is null ? "failed" : "stopped"), ("$rows", rounds.Sum(round => round.Asked)), ("$calls", calls),
                ("$detail", Words(result)),
            })
            {
                log.Parameters.AddWithValue(name, value);
            }

            await log.ExecuteNonQueryAsync(cancellation);
        }

        return result;
    }

    // Whether the picker may pick in an index under the field list's version: the newest naming test under it of that
    // index's members answered both rounds and passed each, and why not where it may not.
    public static async Task<(bool May, string Why)> MayStartAsync(SqliteConnection connection, string version, string index, CancellationToken cancellation = default)
    {
        var rounds = new List<JudgeNamingRound>();
        var name = Core.Candidates.OwnStepCandidate.NameOf(index);

        await using var command = connection.CreateCommand();

        command.CommandText = Newest;
        command.Parameters.AddWithValue("$version", version);
        command.Parameters.AddWithValue("$index", index);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rounds.Add(new JudgeNamingRound(reader.GetString(0), reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3)));
        }

        if (rounds.Count == 0)
        {
            return (false, $"no naming test of the {name}'s members has run under field list {version}");
        }

        var failed = rounds.Where(round => round.Asked < JudgeNaming.SampleSize || !round.Passed).ToArray();

        return rounds.Count == 2 && failed.Length == 0
            ? (true, $"the newest naming test of the {name}'s members under field list {version} passed both rounds")
            : (false, $"the newest naming test of the {name}'s members under field list {version} did not pass: {string.Join("; ", rounds.Select(RoundWords))}");
    }

    public static string Words(JudgeNamingOutcome outcome) =>
        FormattableString.Invariant($"the naming test of {outcome.Session:yyyy-MM-dd}{(outcome.Index is { } index ? " on the " + Core.Candidates.OwnStepCandidate.NameOf(index) : string.Empty)} under field list {outcome.Version}: ")
        + string.Join("; ", outcome.Rounds.Select(RoundWords))
        + (outcome.Stopped is { } why ? "; stopped: " + why : outcome.Passed ? "; passed" : "; failed");

    static string RoundWords(JudgeNamingRound round) =>
        FormattableString.Invariant($"{round.Round}s {round.Named} named of {round.Answered} answered of {round.Asked} asked");

    // The company and the ticker an answer gives, none where it does not read as the schema holds it.
    static (string? Company, string? Ticker) Answer(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return (null, null);
        }

        try
        {
            using var document = JsonDocument.Parse(content);

            string? Text(string name) => document.RootElement.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

            return (Text("company"), Text("ticker"));
        }
        catch (JsonException)
        {
            return (content, null);
        }
    }

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
