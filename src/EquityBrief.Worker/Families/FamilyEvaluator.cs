using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

public sealed record FamilyEvaluation(string Family, int Members, int Passed, int OneShort);

public sealed record FamilyEvaluatorOutcome(DateOnly? Night, int RowsWritten, IReadOnlyList<FamilyEvaluation> Families);

// The family evaluator. Evaluates every member the swing filter evaluated on the night under each setup
// family but the pullback, whose answers are the filter's own rows, and stores every answer with the
// values that decided it.
//
// It runs in the swing filter's step, after the filter has stored its rows and before the family lister
// draws the page's list. The market check it hands every family is the one the filter stored, so one
// answer closes every family's list, and a series the filter excluded for a gap or as suspect is excluded
// here. It replaces its own rows for the night where the night is run again, and drops a row that did not
// pass once its session is older than the bars the store keeps. It makes no request and calls no model.
// see: The nightly run is arithmetic only
// see: The market check closes every family's list together
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
public sealed class FamilyEvaluator : IComponent
{
    // see: Every computed table's writer is its own deleter
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
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
        SELECT ticker, exclusions, gates
        FROM gate_result
        WHERE session_date = $night AND version <> '" + ReplayedResults.Version + @"'
        ORDER BY ticker;
    ";

    // Every session the store keeps, a name at a time and oldest first.
    const string EveryBar = @"
        SELECT ticker, session_date, high, low, close, volume
        FROM bar
        ORDER BY ticker, session_date;
    ";

    const string IndicatorsOn = @"
        SELECT ticker, name, value
        FROM indicator
        WHERE session_date = $night AND name IN ($typical, $volume) AND value IS NOT NULL;
    ";

    // A night run again replaces its own rows whole, and a row that did not pass goes once its session is
    // older than every bar the store keeps: nothing reads a near miss whose bars are gone, and a row that
    // passed is a trade its family's record counts.
    const string ClearTheNight = @"
        DELETE FROM family_result WHERE session_date = $night;
        DELETE FROM family_result WHERE passed = 0 AND session_date < (SELECT MIN(session_date) FROM bar);
    ";

    const string Insert = @"
        INSERT INTO family_result (
            session_date, ticker, family, passed, missed, place, entry, stop, target, order_by, exclusions, gates)
        VALUES (
            $night, $ticker, $family, $passed, $missed, $place, $entry, $stop, $target, $order_by, $exclusions, $gates);
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
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

    public async Task<FamilyEvaluatorOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        // The night is the newest session any name holds, as the swing filter reads it.
        var night = await NewestAsync(connection, cancellation);

        if (night is not { } session)
        {
            await AppendAsync(connection, null, runId, startedAt, 0, "no session is stored, so no family was evaluated", cancellation);

            return new FamilyEvaluatorOutcome(null, 0, []);
        }

        var (members, market) = await FilterRowsAsync(connection, session, cancellation);

        if (members.Count == 0)
        {
            await AppendAsync(connection, null, runId, startedAt, 0, FormattableString.Invariant($"the swing filter stored no result for {Stamp(session)}, so no family was evaluated"), cancellation);

            return new FamilyEvaluatorOutcome(null, 0, []);
        }

        var bars = await BarsAsync(connection, session, cancellation);
        var indicators = await IndicatorsAsync(connection, session, cancellation);

        var results = new List<FamilyResult>();

        foreach (var member in members)
        {
            results.Add(BreakoutRule.Evaluate(new BreakoutInputs(
                member.Ticker,
                market,
                bars.GetValueOrDefault(member.Ticker) ?? [],
                Indicator(indicators, member.Ticker, IndicatorSeries.VolAvg50),
                Indicator(indicators, member.Ticker, IndicatorSeries.Atr14),
                member.Exclusions)));
        }

        var places = SetupFamilies.Evaluated
            .SelectMany(family => FamilyRule.Ranked(results.Where(result => result.Family == family.Name)).Select((result, at) => (result.Family, result.Ticker, Place: at + 1)))
            .ToDictionary(row => (row.Family, row.Ticker), row => row.Place);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await using (var clear = connection.CreateCommand())
        {
            clear.Transaction = transaction;
            clear.CommandText = ClearTheNight;
            clear.Parameters.AddWithValue("$night", Stamp(session));

            await clear.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var result in results)
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

            await insert.ExecuteNonQueryAsync(cancellation);
        }

        var families = SetupFamilies.Evaluated
            .Select(family =>
            {
                var own = results.Where(result => result.Family == family.Name).ToArray();

                return new FamilyEvaluation(
                    family.Name,
                    own.Length,
                    own.Count(result => result.Passed),
                    own.Count(result => result.Missed == 1 && result.Exclusions.Count == 0));
            })
            .ToArray();
        var outcome = new FamilyEvaluatorOutcome(session, results.Count, families);

        await AppendAsync(connection, transaction, runId, startedAt, results.Count, Detail(outcome), cancellation);
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

            members.Add(new Member(reader.GetString(0), [.. exclusions.Where(exclusion => SeriesExclusions.Contains(exclusion, StringComparer.Ordinal))]));

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

    static async Task<IReadOnlyDictionary<(string, string), double>> IndicatorsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = IndicatorsOn;
        command.Parameters.AddWithValue("$night", Stamp(night));
        command.Parameters.AddWithValue("$typical", IndicatorSeries.Atr14);
        command.Parameters.AddWithValue("$volume", IndicatorSeries.VolAvg50);

        var values = new Dictionary<(string, string), double>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            values[(reader.GetString(0), reader.GetString(1))] = reader.GetDouble(2);
        }

        return values;
    }

    static double? Indicator(IReadOnlyDictionary<(string, string), double> indicators, string ticker, string name) =>
        indicators.TryGetValue((ticker, name), out var value) ? value : null;

    static async Task<DateOnly?> NewestAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = NewestSession;

        return await command.ExecuteScalarAsync(cancellation) is string newest ? Date(newest) : null;
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction? transaction, string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
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
