using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

public sealed record FamilyEvaluation(string Family, int Members, int Passed, int OneShort);

public sealed record FamilyEvaluatorOutcome(DateOnly? Night, int RowsWritten, IReadOnlyList<FamilyEvaluation> Families, int Evaluated = 0, IReadOnlyList<string>? Faults = null);

// The family evaluator. Evaluates every member the swing filter evaluated on the night under each setup
// family but the pullback, whose answers are the filter's own rows, and stores every answer with the
// values that decided it.
//
// It runs in the swing filter's step, after the filter has stored its rows and before the family lister
// draws the page's list. The market check it hands every family is the one the filter stored, so one
// answer closes every family's list, and a series the filter excluded for a gap or as suspect is excluded
// here. The earnings drift reads each name's newest print as the move annotator stored it and the night's
// bands, so nothing is asked of a provider. Every family candidate standing when the night started is
// evaluated over the same inputs at its own settings, by the shadow the night hands in, and its verdict
// stored on the member's row of its family, a missing or moved evaluator failing the stage. It replaces its
// own rows for the night where the night is run again, and drops a row that did not pass once its session is
// older than the bars the store keeps. It makes no request and calls no model.
// see: The nightly run is arithmetic only
// see: The market check closes every family's list together
// see: Each print's reaction is read from the nightly calendar and the stored bars, and the earnings drift is the one rule that reads it
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
public sealed class FamilyEvaluator : IComponent
{
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FamilyResult, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "family-rules";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // Every member the swing filter evaluated on the night, with the exclusions and the gates it stored. A
    // replayed result is none of the night's.
    const string FilterRowsOn = @"
        SELECT g.ticker, g.exclusions, g.gates
        FROM gate_result g
        WHERE g.session_date = $night AND g.version <> '" + ReplayedResults.Version + @"'
        ORDER BY g.ticker;
    ";

    // Every session the store keeps, a name at a time and oldest first.
    const string EveryBar = @"
        SELECT ticker, session_date, high, low, close, volume
        FROM bar
        ORDER BY ticker, session_date;
    ";

    // The typical move and the average volume stored for the night and for the sessions before it a family
    // reads a reaction in.
    const string IndicatorsFrom = @"
        SELECT ticker, session_date, name, value
        FROM indicator
        WHERE session_date >= $from AND session_date <= $night AND name IN ($typical, $volume) AND value IS NOT NULL;
    ";

    // Every print whose reaction session is the night's or an earlier one, a name at a time and oldest
    // first, so the last a name holds is its newest.
    const string PrintsTo = @"
        SELECT ticker, report_date, reaction_session, actual IS NOT NULL, surprise_pct
        FROM earnings_reaction
        WHERE reaction_session <= $night
        ORDER BY ticker, reaction_session;
    ";

    // The low edge of every band the night stored.
    const string BandsOn = "SELECT ticker, low_edge FROM level WHERE as_of = $night;";

    // A night run again replaces its own rows whole, and a row that did not pass goes once its session is
    // older than every bar the store keeps: nothing reads a near miss whose bars are gone, and a row that
    // passed is a trade its family's record counts.
    const string ClearTheNight = @"
        DELETE FROM family_result WHERE session_date = $night;
        DELETE FROM family_result WHERE passed = 0 AND session_date < (SELECT MIN(session_date) FROM bar);
    ";

    const string Insert = @"
        INSERT INTO family_result (
            session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates, shadow)
        VALUES (
            $night, $ticker, $family, $passed, $missed, $place, $entry, $stop, $target, $order_by, $exclusions, $gates, $shadow);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public FamilyEvaluator(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    sealed record Member(string Ticker, IReadOnlyList<string> Exclusions);

    public Task<FamilyEvaluatorOutcome> RunAsync(string runId, CancellationToken cancellation = default) =>
        RunAsync(runId, FamilyRuleShadow.None, cancellation);

    public async Task<FamilyEvaluatorOutcome> RunAsync(string runId, FamilyRuleShadow shadow, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The night is the newest session any name holds, as the swing filter reads it.
        var night = await NewestAsync(connection, cancellation);

        if (night is not { } session)
        {
            await AppendAsync(connection, null, runId, startedAt, 0, "no session is stored, so no family was evaluated", false, cancellation);

            return new FamilyEvaluatorOutcome(null, 0, []);
        }

        var (members, market) = await FilterRowsAsync(connection, session, cancellation);

        if (members.Count == 0)
        {
            await AppendAsync(connection, null, runId, startedAt, 0, FormattableString.Invariant($"the swing filter stored no result for {Stamp(session)}, so no family was evaluated"), false, cancellation);

            return new FamilyEvaluatorOutcome(null, 0, []);
        }

        var bars = await BarsAsync(connection, session, cancellation);

        // The indicators are read from the oldest session a reaction inside the widest drift window a standing
        // rule admits can follow.
        var from = bars.Values
            .Select(held => held[Math.Max(0, held.Count - 1 - shadow.DriftWindowReach)].Session)
            .DefaultIfEmpty(session)
            .Min();
        var indicators = await IndicatorsAsync(connection, from, session, cancellation);
        var prints = await PrintsAsync(connection, session, cancellation);
        var bands = await BandsAsync(connection, session, cancellation);

        var results = new List<(FamilyResult Result, string? Shadow)>();

        foreach (var member in members)
        {
            var held = bars.GetValueOrDefault(member.Ticker) ?? [];
            var typical = Indicator(indicators, member.Ticker, session, IndicatorSeries.Atr14);

            // The reaction's own session among the member's bars, which the two readings beside it are stored for.
            var print = prints.GetValueOrDefault(member.Ticker);
            var at = print is null ? -1 : held.Select((bar, index) => (bar, index)).Where(pair => pair.bar.Session == print.ReactionSession).Select(pair => pair.index).DefaultIfEmpty(-1).First();

            var inputs = new FamilyMember(
                new BreakoutInputs(
                    member.Ticker,
                    market,
                    held,
                    Indicator(indicators, member.Ticker, session, IndicatorSeries.VolAvg50),
                    typical,
                    member.Exclusions),
                new DriftInputs(
                    member.Ticker,
                    market,
                    held,
                    print,
                    at >= 1 ? Indicator(indicators, member.Ticker, held[at - 1].Session, IndicatorSeries.Atr14) : null,
                    at >= 0 ? Indicator(indicators, member.Ticker, held[at].Session, IndicatorSeries.VolAvg50) : null,
                    typical,
                    bands.GetValueOrDefault(member.Ticker) ?? [],
                    member.Exclusions));

            // A member the night holds no bar for, or holds across a gap, is a counted skip of every family candidate.
            var withheld = FamilyRuleShadow.Withheld(held.Count == 0, member.Exclusions.Contains(SwingGates.GapExclusion, StringComparer.Ordinal));

            results.Add((BreakoutRule.Evaluate(inputs.Breakout), shadow.Evaluate(BreakoutRule.Name, inputs, withheld)));
            results.Add((DriftRule.Evaluate(inputs.Drift), shadow.Evaluate(DriftRule.Name, inputs, withheld)));
        }

        var places = SetupFamilies.Evaluated
            .SelectMany(family => FamilyRule.Ranked(results.Select(pair => pair.Result).Where(result => result.Family == family.Name)).Select((result, at) => (result.Family, result.Ticker, Place: at + 1)))
            .ToDictionary(row => (row.Family, row.Ticker), row => row.Place);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = ClearTheNight;
            clear.Parameters.AddWithValue("$night", Stamp(session));

            await clear.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var (result, verdicts) in results)
        {
            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = Insert;
            insert.Parameters.AddWithValue("$night", Stamp(session));
            insert.Parameters.AddWithValue("$ticker", result.Ticker);
            insert.Parameters.AddWithValue("$family", result.Family);
            insert.Parameters.AddWithValue("$passed", result.Passed ? 1 : 0);
            insert.Parameters.AddWithValue("$missed", result.Missed);
            insert.Parameters.AddWithValue("$place", places.TryGetValue((result.Family, result.Ticker), out var place) ? place : DBNull.Value);
            insert.Parameters.AddWithValue("$entry", Price(result.Entry));
            insert.Parameters.AddWithValue("$stop", Price(result.Stop));
            insert.Parameters.AddWithValue("$target", Price(result.Target));
            insert.Parameters.AddWithValue("$order_by", result.OrderBy is { } figure ? figure : DBNull.Value);
            insert.Parameters.AddWithValue("$exclusions", JsonSerializer.Serialize(result.Exclusions));
            insert.Parameters.AddWithValue("$gates", FamilyRule.GatesJson(result.Gates));
            insert.Parameters.AddWithValue("$shadow", verdicts is null ? DBNull.Value : verdicts);

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var families = SetupFamilies.Evaluated
            .Select(family =>
            {
                var own = results.Select(pair => pair.Result).Where(result => result.Family == family.Name).ToArray();

                return new FamilyEvaluation(
                    family.Name,
                    own.Length,
                    own.Count(result => result.Passed),
                    own.Count(result => result.Missed == 1 && result.Exclusions.Count == 0));
            })
            .ToArray();
        var outcome = new FamilyEvaluatorOutcome(session, results.Count, families, shadow.Evaluated, shadow.Faults);

        await AppendAsync(connection, transaction, runId, startedAt, results.Count, Detail(outcome) + shadow.Said, shadow.Faults.Count > 0, cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // What the stage's row says: for each family, the members it passed of the ones it evaluated and the
    // ones a single gate short.
    public static string Detail(FamilyEvaluatorOutcome outcome) =>
        FormattableString.Invariant($"for {outcome.Night:yyyy-MM-dd}: ")
        + string.Join("; ", outcome.Families.Select(family => FormattableString.Invariant($"the {family.Family} family passed {family.Passed} of {family.Members} members, {family.OneShort} one gate short")));

    // The series exclusions a family carries over from the filter's row: a gap, and a series marked suspect.
    // The filter's other exclusions are its own rule's.
    static readonly string[] SeriesExclusions = [SwingGates.GapExclusion, SwingGates.SuspectExclusion];

    static async Task<(IReadOnlyList<Member> Members, Gate Market)> FilterRowsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = FilterRowsOn;
        command.Parameters.AddWithValue("$night", Stamp(night));

        var members = new List<Member>();
        Gate? market = null;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var exclusions = JsonSerializer.Deserialize<string[]>(reader.GetString(1)) ?? [];

            members.Add(new Member(
                reader.GetString(0),
                [.. exclusions.Where(exclusion => SeriesExclusions.Contains(exclusion, StringComparer.Ordinal))]));

            // The market gate is one answer for the night, so the first row's is every row's.
            market ??= FamilyRule.GatesOf(reader.GetString(2)).FirstOrDefault(gate => gate.Name == FamilyRule.Market);
        }

        return (members, market ?? FamilyRule.NoMarketCheck);
    }

    // Every name's sessions ending on the night, oldest first. A name whose newest session is not the
    // night's holds none, since nothing is read for a night a name has no bar on.
    static async Task<IReadOnlyDictionary<string, IReadOnlyList<FamilyBar>>> BarsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = EveryBar;

        var bars = new Dictionary<string, List<FamilyBar>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!bars.TryGetValue(ticker, out var held))
            {
                bars[ticker] = held = [];
            }

            held.Add(new FamilyBar(
                Date(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.GetInt64(5)));
        }

        return bars
            .Where(entry => entry.Value[^1].Session == night)
            .ToDictionary(entry => entry.Key, entry => (IReadOnlyList<FamilyBar>)entry.Value, StringComparer.Ordinal);
    }

    static async Task<IReadOnlyDictionary<(string, DateOnly, string), double>> IndicatorsAsync(SqliteConnection connection, DateOnly from, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsFrom;
        command.Parameters.AddWithValue("$from", Stamp(from));
        command.Parameters.AddWithValue("$night", Stamp(night));
        command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);
        command.Parameters.AddWithValue("$volume", IndicatorSeries.VolAvg50);

        var values = new Dictionary<(string, DateOnly, string), double>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            values[(reader.GetString(0), Date(reader.GetString(1)), reader.GetString(2))] = reader.GetDouble(3);
        }

        return values;
    }

    static double? Indicator(IReadOnlyDictionary<(string, DateOnly, string), double> indicators, string ticker, DateOnly session, string name) =>
        indicators.TryGetValue((ticker, session, name), out var value) ? value : null;

    // Each name's newest print whose reaction session is the night's or an earlier one.
    static async Task<IReadOnlyDictionary<string, DriftPrint>> PrintsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = PrintsTo;
        command.Parameters.AddWithValue("$night", Stamp(night));

        var prints = new Dictionary<string, DriftPrint>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            prints[reader.GetString(0)] = new DriftPrint(
                Date(reader.GetString(1)),
                Date(reader.GetString(2)),
                reader.GetInt64(3) == 1,
                reader.IsDBNull(4) ? null : reader.GetDouble(4));
        }

        return prints;
    }

    static async Task<IReadOnlyDictionary<string, IReadOnlyList<decimal>>> BandsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BandsOn;
        command.Parameters.AddWithValue("$night", Stamp(night));

        var bands = new Dictionary<string, List<decimal>>(StringComparer.Ordinal);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!bands.TryGetValue(ticker, out var held))
            {
                bands[ticker] = held = [];
            }

            held.Add(Money.FromStorage(reader.GetString(1)));
        }

        return bands.ToDictionary(entry => entry.Key, entry => (IReadOnlyList<decimal>)entry.Value, StringComparer.Ordinal);
    }

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction? transaction, string runId, DateTimeOffset startedAt, int rows, string detail, bool failed, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$outcome", failed ? "failed" : "ok");
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static object Price(decimal? value) => value is { } present ? Money.ToStorage(present) : DBNull.Value;

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
