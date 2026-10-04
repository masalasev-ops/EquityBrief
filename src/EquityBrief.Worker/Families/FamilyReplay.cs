using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

// What a replay found for one rule: the version it was replayed under, the session it replayed from, the nights
// it read, the trades the record stored and the replay kept, whether every one is the same, and what differed
// first where one was not.
public sealed record FamilyReplayed(string Candidate, string Version, DateOnly From, int Nights, int Stored, int Replayed, bool Reproduced, string Said);

// The family replay. Before a family rule is registered again, at a change of code that moves its evaluator or to
// add a rule to its family, replays it at its settings under the code as it stands over every night the store
// holds since its record began, through the night's own inputs, shadow and list, and compares each trade it would
// have kept with the one its record stored: the stock, the night, the place on the list, the stop and the target
// as distances from the buy, the session it ended and its result. Where every trade is the same the rule's record
// carries on from where it began; where one is not, its record restarts from the registration, and this row says
// what differed first. One row on the run log a rule, under a run of its own.
//
// It reads the store and writes its rows alone, makes no request and calls no model.
// see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
// see: The nightly run is arithmetic only
public sealed class FamilyReplay : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FamilyResult, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: []);

    public const string RunPrefix = "replay-";

    // A stop's or a target's distance from the buy is the same where the two agree to a millionth: a split or a
    // dividend since the night rescales the store's closes, and a distance does not move with the scale.
    const decimal Distance = 0.000001m;

    // A result is the same where the two agree to a thousand-millionth, both read off the same closes.
    const double Result = 1e-9;

    const string ReadRegister = @"
        SELECT id, candidate, rule, test, evaluator, parameters, evaluator_version,
               event, retires, registered_at, evidence
        FROM candidate_register
        ORDER BY id;
    ";

    const string Replays = "SELECT outcome, detail, started_at FROM run_log WHERE stage LIKE $stages ORDER BY rowid;";

    // The nights the family's evaluator stored its rows for, from a session on.
    const string NightsFrom = "SELECT DISTINCT session_date FROM family_result WHERE family = $family AND session_date >= $from ORDER BY session_date;";

    // Every trade a rule's record stored.
    const string Stored = @"
        SELECT ticker, session_date, place, entry, stop, target, ended_on, result
        FROM family_trade
        WHERE candidate = $candidate
        ORDER BY session_date, place;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            0, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public FamilyReplay(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // A trade as the record stored it or the replay kept it.
    sealed record Trade(string Ticker, DateOnly Session, int Place, decimal Entry, decimal Stop, decimal? Target, DateOnly? EndedOn, double? Result);

    // The run id, to the ten-millionth of a second, so two replays a second apart never share one.
    public static string RunIdAt(DateTimeOffset at) => FormattableString.Invariant($"{RunPrefix}{at:yyyyMMddTHHmmss.fffffffZ}");

    // Every standing rule of one family, which the family registered again registers again.
    public Task<IReadOnlyList<FamilyReplayed>> FamilyAsync(string family, CancellationToken cancellation = default) =>
        RunAsync(rule => FamilyOf(rule) == family, cancellation);

    // Every standing family rule whose evaluator a change of code has moved, which the moved rules registered again
    // register again.
    public Task<IReadOnlyList<FamilyReplayed>> MovedAsync(CancellationToken cancellation = default) =>
        RunAsync(rule => CandidateEvaluators.Find(rule.Evaluator) is { } evaluator && !string.Equals(evaluator.Version, rule.EvaluatorVersion, StringComparison.Ordinal), cancellation);

    async Task<IReadOnlyList<FamilyReplayed>> RunAsync(Func<RegisterRow, bool> which, CancellationToken cancellation)
    {
        var startedAt = clock.UtcNow;
        var runId = RunIdAt(startedAt);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var register = await RegisterAsync(connection, cancellation);
        var replays = await ReplaysAsync(connection, cancellation);
        var standing = CandidateFamily.Standing(register, startedAt).Where(which).ToArray();
        var rules = standing.Where(rule => CandidateEvaluators.Find(rule.Evaluator) is FamilyRuleEvaluator).ToArray();
        var found = new List<FamilyReplayed>();

        foreach (var family in rules.GroupBy(FamilyOf))
        {
            found.AddRange(await FamilyAsync(connection, family.Key, [.. family], register, replays, cancellation));
        }

        // A rule the heavyweights' book keeps is not replayed, so its record restarts at the registration and its row says so.
        // see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
        found.AddRange(standing
            .Where(rule => CandidateEvaluators.Find(rule.Evaluator) is BookEvaluator)
            .Select(rule => new FamilyReplayed(
                rule.Candidate,
                CandidateEvaluators.Find(rule.Evaluator)!.Version,
                FamilyRecords.ReplayFrom(register, rule, replays),
                0,
                0,
                0,
                false,
                NotReplayed)));

        foreach (var one in found)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = AppendRun;
            command.Parameters.AddWithValue("$run_id", runId);
            command.Parameters.AddWithValue("$stage", FamilyRecords.ReplayStageOf(one.Candidate));
            command.Parameters.AddWithValue("$started_at", Instant(startedAt));
            command.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            command.Parameters.AddWithValue("$outcome", one.Reproduced ? FamilyRecords.ReplayReproduced : FamilyRecords.ReplayDiffers);
            command.Parameters.AddWithValue("$detail", FamilyRecords.ReplayDetail(one.Candidate, one.Version, one.From, one.Nights, one.Stored, one.Replayed, one.Said));

            await command.ExecuteNonQueryAsync(cancellation);
        }

        return found;
    }

    // One family's rules replayed together over the nights since the earliest of their records began, each from its own.
    static async Task<IReadOnlyList<FamilyReplayed>> FamilyAsync(
        SqliteConnection connection,
        string family,
        IReadOnlyList<RegisterRow> rules,
        IReadOnlyList<RegisterRow> register,
        IReadOnlyList<FamilyReplayRow> replays,
        CancellationToken cancellation)
    {
        var cap = SetupFamilies.Named(family)?.CapSessions ?? 0;
        var froms = rules.ToDictionary(rule => rule.Candidate, rule => FamilyRecords.ReplayFrom(register, rule, replays), StringComparer.Ordinal);
        var earliest = froms.Values.Min();
        var nights = await NightsAsync(connection, family, earliest, cancellation);

        // Each rule at the version the code carries now, so the night's shadow evaluates it as a rule standing under it.
        var asTheCodeIs = rules.Select(rule => rule with { EvaluatorVersion = CandidateEvaluators.Find(rule.Evaluator)!.Version }).ToArray();
        var shadow = FamilyRuleShadow.For(asTheCodeIs, DateTimeOffset.MaxValue);
        var stored = new Dictionary<string, IReadOnlyList<Trade>>(StringComparer.Ordinal);

        foreach (var rule in rules)
        {
            stored[rule.Candidate] = await StoredAsync(connection, rule.Candidate, cancellation);
        }

        var calendar = await FamilyRecorder.SessionsAsync(connection, earliest, cancellation);
        var closes = await FamilyRecorder.ClosesAsync(connection, earliest, cancellation);
        var at = calendar.Select((session, index) => (session, index)).ToDictionary(pair => pair.session, pair => pair.index);
        var kept = rules.ToDictionary(rule => rule.Candidate, _ => new List<Trade>(), StringComparer.Ordinal);
        var read = rules.ToDictionary(rule => rule.Candidate, _ => 0, StringComparer.Ordinal);

        foreach (var night in nights)
        {
            if (await FamilyEvaluator.InputsAsync(connection, night, shadow.DriftWindowReach, cancellation) is not { } inputs)
            {
                continue;
            }

            var verdicts = new List<(string Ticker, ShadowOutcome Outcome)>();

            foreach (var member in inputs.Members)
            {
                verdicts.AddRange(FamilyRuleShadow.Read(shadow.Evaluate(family, member.Inputs, member.Withheld)).Select(outcome => (member.Ticker, outcome)));
            }

            foreach (var rule in rules.Where(rule => froms[rule.Candidate] <= night))
            {
                read[rule.Candidate]++;

                // A stock is held by a trade still open on the night: one this replay kept, or one the record kept
                // before the replay's first night.
                var held = kept[rule.Candidate]
                    .Concat(stored[rule.Candidate].Where(trade => trade.Session < froms[rule.Candidate]))
                    .Where(trade => trade.Session < night && (trade.EndedOn is null || trade.EndedOn >= night))
                    .Select(trade => trade.Ticker)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var (ticker, place, plan) in FamilyRecorder.Keep(verdicts.Where(verdict => verdict.Outcome.Candidate == rule.Candidate), held))
                {
                    var ended = FamilyRecorder.Walk(night, plan.Entry, plan.Stop, plan.Target, cap, closes.GetValueOrDefault(ticker), calendar, at);

                    kept[rule.Candidate].Add(new Trade(ticker, night, place, plan.Entry, plan.Stop, plan.Target, ended?.On, ended?.Result));
                }
            }
        }

        return
        [
            .. rules.Select(rule =>
            {
                var from = froms[rule.Candidate];
                var record = stored[rule.Candidate].Where(trade => trade.Session >= from).ToArray();
                var replayed = kept[rule.Candidate];
                var differs = FirstDifference(record, replayed);

                return new FamilyReplayed(
                    rule.Candidate,
                    CandidateEvaluators.Find(rule.Evaluator)!.Version,
                    from,
                    read[rule.Candidate],
                    record.Length,
                    replayed.Count,
                    differs is null,
                    differs ?? FormattableString.Invariant($"reproduced its {record.Length} trade(s) over {read[rule.Candidate]} night(s) from {Stamp(from)}"));
            }),
        ];
    }

    // The first trade the record and the replay do not share, in words, or none where they share every one.
    static string? FirstDifference(IReadOnlyList<Trade> record, IReadOnlyList<Trade> replayed)
    {
        foreach (var trade in record.Concat(replayed).OrderBy(trade => trade.Session).ThenBy(trade => trade.Place).ThenBy(trade => trade.Ticker, StringComparer.Ordinal))
        {
            var stored = record.FirstOrDefault(one => one.Ticker == trade.Ticker && one.Session == trade.Session);
            var again = replayed.FirstOrDefault(one => one.Ticker == trade.Ticker && one.Session == trade.Session);
            var on = FormattableString.Invariant($"{trade.Ticker} on {Stamp(trade.Session)}");

            if (stored is null)
            {
                return $"the replay keeps {on} where the record keeps none";
            }

            if (again is null)
            {
                return $"the record keeps {on} where the replay keeps none";
            }

            if (stored.Place != again.Place)
            {
                return FormattableString.Invariant($"{on} is at place {again.Place} in the replay and {stored.Place} in the record");
            }

            if (!Same(Away(stored.Stop, stored.Entry), Away(again.Stop, again.Entry)))
            {
                return $"{on} has its stop at another distance from its buy in the replay";
            }

            if (!Same(stored.Target is { } target ? Away(target, stored.Entry) : null, again.Target is { } aimed ? Away(aimed, again.Entry) : null))
            {
                return $"{on} has its target at another distance from its buy in the replay";
            }

            if (stored.EndedOn != again.EndedOn || !Same(stored.Result, again.Result, Result))
            {
                return $"{on} ends {Ending(again)} in the replay and {Ending(stored)} in the record";
            }
        }

        return null;
    }

    // A price's distance from the buy, as a share of the buy.
    static decimal Away(decimal price, decimal entry) => (price - entry) / entry;

    static bool Same(decimal? one, decimal? other) =>
        (one, other) switch
        {
            (null, null) => true,
            ({ } a, { } b) => Math.Abs(a - b) <= Distance * Math.Max(1m, Math.Abs(a)),
            _ => false,
        };

    static bool Same(double? one, double? other, double within) =>
        (one, other) switch
        {
            (null, null) => true,
            ({ } a, { } b) => Math.Abs(a - b) <= within * Math.Max(1, Math.Abs(a)),
            _ => false,
        };

    static string Ending(Trade trade) =>
        trade.EndedOn is not { } on
            ? "still open"
            : trade.Result is { } made
                ? FormattableString.Invariant($"on {Stamp(on)} at {made:0.000}")
                : FormattableString.Invariant($"on {Stamp(on)} with no result");

    static string FamilyOf(RegisterRow rule) => CandidateEvaluators.Find(rule.Evaluator) switch
    {
        FamilyRuleEvaluator evaluator => evaluator.Family,
        BookEvaluator book => book.Family,
        _ => string.Empty,
    };

    // What a heavyweights rule's row says, which the run page states beside it.
    public const string NotReplayed = "the sector heavyweights' books are not replayed, so its record restarts at this registration";

    static async Task<IReadOnlyList<RegisterRow>> RegisterAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ReadRegister;

        var rows = new List<RegisterRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new RegisterRow(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8),
                ParseInstant(reader.GetString(9)),
                reader.IsDBNull(10) ? null : reader.GetString(10)));
        }

        return rows;
    }

    static async Task<IReadOnlyList<FamilyReplayRow>> ReplaysAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Replays;
        command.Parameters.AddWithValue("$stages", FamilyRecords.ReplayStages);

        var rows = new List<FamilyReplayRow>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            if (FamilyRecords.ReplayOf(reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), ParseInstant(reader.GetString(2))) is { } row)
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    static async Task<IReadOnlyList<DateOnly>> NightsAsync(SqliteConnection connection, string family, DateOnly from, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NightsFrom;
        command.Parameters.AddWithValue("$family", family);
        command.Parameters.AddWithValue("$from", Stamp(from));

        var nights = new List<DateOnly>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            nights.Add(Date(reader.GetString(0)));
        }

        return nights;
    }

    static async Task<IReadOnlyList<Trade>> StoredAsync(SqliteConnection connection, string candidate, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = Stored;
        command.Parameters.AddWithValue("$candidate", candidate);

        var trades = new List<Trade>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            trades.Add(new Trade(
                reader.GetString(0),
                Date(reader.GetString(1)),
                reader.GetInt32(2),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                reader.IsDBNull(6) ? null : Date(reader.GetString(6)),
                reader.IsDBNull(7) ? null : reader.GetDouble(7)));
        }

        return trades;
    }

    static string Instant(DateTimeOffset at) => at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static DateTimeOffset ParseInstant(string stamp) =>
        DateTimeOffset.ParseExact(stamp, "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
