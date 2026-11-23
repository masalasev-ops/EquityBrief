using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.History;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The one tester's run: each grammar line given is parsed, refused with its reason where the grammar cannot read it and
// skipped where the notebook already holds its line; each other is walked over its index's setups from 2005, its tenths'
// cut points read from 2005 to 2018 alone, each pick bought, stopped and sold as the line says through the plan test's own
// walk; every line of the run is judged together from 2019 at the weekly bar; and each is written to the notebook with its
// figures, its verdict and why. It moves no pick, plan, rule or version.
// see: Every idea from any source is tested by the one tester and kept in a notebook nothing is deleted from
// see: An idea is one line of a closed grammar that code parses into one canonical line and tests with no build session
public sealed class IdeaTestRun(IClock clock, string databaseFile, string dataRoot, TextWriter output)
{
    public const string Verb = "idea-test";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.Notebook, Touch.Read),
            new StoreTouch(Store.Notebook, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string BuiltOn = "the long history from 2005, its levels chosen on 2005 to 2018 and judged from 2019";

    // The sources a line given by hand may name.
    public static IReadOnlyList<string> Sources { get; } = ["the operator", "the researcher", "the idea box", "the picker"];

    const string Tested = "SELECT line, MIN(tested_on) FROM notebook WHERE line IS NOT NULL GROUP BY line;";

    const string Insert = @"
        INSERT INTO notebook (entered_at, source, source_key, index_code, family, words, line, built_on, early_picks, early_edge, late_picks, late_edge, blocks, adjusted, bar, verdict, why, tested_on, run_id)
        VALUES ($entered_at, $source, NULL, $index_code, $family, $words, $line, $built_on, $early_picks, $early_edge, $late_picks, $late_edge, $blocks, $adjusted, $bar, $verdict, $why, $tested_on, $run_id) RETURNING id;";

    const string InsertRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    // The family's setups an idea's event offers on its index: every one its loose gates passed or those its live setting
    // passed, from the long ledger to 2018 and the standing ledger after, each with the readings its conditions and order read.
    public static string SetupsSql(IdeaLine idea) =>
        string.Join(" UNION ALL ", new[] { ("long_setup", string.Empty), ("setup", " AND session_date >= $standing") }.Select(table =>
            $"SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value{string.Concat(ReadingsOf(idea).Select(reading => ", " + reading))} "
            + $"FROM {table.Item1} WHERE index_code = $index AND family = $family{(idea.Loose ? string.Empty : " AND live_pass = 1")}{table.Item2}"))
        + " ORDER BY session_date, ticker;";

    public static IReadOnlyList<string> ReadingsOf(IdeaLine idea) =>
        [.. idea.Conditions.Select(condition => condition.Reading).Concat(idea.OrderBy is { } order ? [order] : []).Distinct(StringComparer.Ordinal).Where(IdeaGrammar.Readings.Contains)];

    // The plan a line walks: its buy, its stop in typical days under the buy, sold on the time limit, and taking profit on the
    // whole or on half at its multiple of the risk.
    public static PlanCombination CombinationOf(IdeaLine idea)
    {
        var plan = new PlanCombination(idea.Entry, idea.Window, PlanPoints.TypicalStop(idea.StopDays), PlanPoints.TimeLimit);

        return idea.Shape switch
        {
            IdeaGrammar.Whole => plan.TakingAtRisk(idea.Multiple!.Value),
            IdeaGrammar.Half => plan.Halving(idea.Multiple!.Value),
            _ => plan,
        };
    }

    // The notebook's row each line this run tested was written to, by its canonical line.
    public IReadOnlyDictionary<string, long> Written { get; private set; } = new Dictionary<string, long>(StringComparer.Ordinal);

    // The lines a run given a deadline started no walk for once it had passed, by their canonical line.
    public IReadOnlySet<string> Unstarted { get; private set; } = new HashSet<string>(StringComparer.Ordinal);

    // Each line tested, and where the run is given an instant to stop starting by, each line not yet walked at that instant
    // left untested; the lines walked are judged together as one run.
    public async Task<int> RunAsync(IReadOnlyList<string> lines, string source, CancellationToken cancellation = default, DateTimeOffset? stopStartingAt = null)
    {
        if (!Sources.Contains(source, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name '--source' as one of {string.Join(", ", Sources.Select(one => $"'{one}'"))}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        var started = clock.UtcNow;
        var runId = FormattableString.Invariant($"{Verb}-{started.UtcDateTime:yyyyMMddTHHmmssZ}");
        var tested = new Dictionary<string, string?>(StringComparer.Ordinal);

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);
            await using var command = connection.CreateCommand();

            command.CommandText = Tested;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                tested[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
            }
        }

        var ideas = new List<IdeaLine>();

        foreach (var text in lines)
        {
            var (idea, refused) = IdeaGrammar.Parse(text);

            if (idea is null)
            {
                output.WriteLine($"{Verb}: refused, {refused}");
            }
            else if (tested.TryGetValue(idea.Canonical, out var on) || ideas.Any(one => one.Canonical == idea.Canonical))
            {
                output.WriteLine($"{Verb}: the notebook already holds this idea{(on is null ? string.Empty : ", tested " + on)}: {idea.Canonical}");
            }
            else
            {
                ideas.Add(idea);
            }
        }

        if (ideas.Count == 0)
        {
            output.WriteLine($"{Verb}: no line to test");

            return 2;
        }

        var figures = new IdeaFigures[ideas.Count];
        var throughs = new DateOnly[ideas.Count];
        var chosenOn = new IReadOnlyDictionary<string, double[]>[ideas.Count];
        var rows = new long[ideas.Count];

        foreach (var index in ideas.Select(idea => idea.Index).Distinct(StringComparer.Ordinal))
        {
            var (walker, through, calendar) = await PlanPointTest.WalkerAsync(databaseFile, index, output.WriteLine, cancellation);
            var opens = calendar.ToList().FindIndex(session => session.Year >= IdeaTester.LateFrom);
            int SessionOf(DateOnly day) => calendar.ToList().BinarySearch(day) is var at && at >= 0 ? at - opens : (~at) - opens;
            var newest = SessionOf(through);

            for (var at = 0; at < ideas.Count; at++)
            {
                if (ideas[at].Index != index)
                {
                    continue;
                }

                if (stopStartingAt is { } deadline && clock.UtcNow >= deadline)
                {
                    continue;
                }

                var idea = ideas[at];
                var setups = await SetupsAsync(idea, cancellation);
                var cuts = IdeaTester.CutsOf(setups.Select(one => one.Setup), ReadingsOf(idea));
                chosenOn[at] = cuts;
                var chosen =IdeaTester.Selected(idea, setups.Select(one => one.Setup), cuts).ToHashSet();
                var combination = CombinationOf(idea);
                var walked = setups.Where(one => chosen.Contains(one.Setup)).ToArray();
                var picks = new IdeaPick[walked.Length];

                Parallel.For(0, walked.Length, new ParallelOptions { CancellationToken = cancellation }, one =>
                {
                    var pick = walker.PicksUnder(walked[one].Flagged with { Cap = idea.Sessions }, [combination]) is { Length: > 0 } read ? read[0] : null;

                    picks[one] = new IdeaPick(walked[one].Setup.Session, pick?.Edge);
                });

                figures[at] = IdeaTester.FiguresOf(picks, SessionOf, newest, idea.Sessions);
                throughs[at] = through;
                output.WriteLine(FormattableString.Invariant($"{Verb}: {idea.Canonical}: {setups.Count} setup(s) offered, {walked.Length} bought, {figures[at].EarlyPicks} with a result to 2018 and {figures[at].LatePicks} from 2019"));
            }
        }

        // The lines no walk started for, once the deadline had passed, are left untested and judged in no run.
        var kept = Enumerable.Range(0, ideas.Count).Where(at => figures[at] is not null).ToArray();

        Unstarted = ideas.Where((_, at) => figures[at] is null).Select(idea => idea.Canonical).ToHashSet(StringComparer.Ordinal);

        if (kept.Length < ideas.Count)
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: {ideas.Count - kept.Length} line(s) not started before the run's deadline, left for the next run"));
            (ideas, figures, throughs, chosenOn, rows) = ([.. kept.Select(at => ideas[at])], [.. kept.Select(at => figures[at])], [.. kept.Select(at => throughs[at])], [.. kept.Select(at => chosenOn[at])], new long[kept.Length]);
        }

        if (ideas.Count == 0)
        {
            return 2;
        }

        var verdicts = IdeaTester.Verdicts(figures);

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);
            var entered = Stamp(clock.UtcNow);

            for (var at = 0; at < ideas.Count; at++)
            {
                var (idea, figure, verdict) = (ideas[at], figures[at], verdicts[at]);

                await using var command = connection.CreateCommand();

                command.Transaction = transaction;
                command.CommandText = Insert;
                command.Parameters.AddWithValue("$entered_at", entered);
                command.Parameters.AddWithValue("$source", source);
                command.Parameters.AddWithValue("$index_code", idea.Index);
                command.Parameters.AddWithValue("$family", idea.Family);
                command.Parameters.AddWithValue("$words", idea.Words);
                command.Parameters.AddWithValue("$line", idea.Canonical);
                command.Parameters.AddWithValue("$built_on", BuiltOn);
                command.Parameters.AddWithValue("$early_picks", figure.EarlyPicks);
                command.Parameters.AddWithValue("$early_edge", (object?)figure.EarlyEdge ?? DBNull.Value);
                command.Parameters.AddWithValue("$late_picks", figure.LatePicks);
                command.Parameters.AddWithValue("$late_edge", (object?)figure.LateEdge ?? DBNull.Value);
                command.Parameters.AddWithValue("$blocks", figure.Blocks.Count);
                command.Parameters.AddWithValue("$adjusted", (object?)verdict.Adjusted ?? DBNull.Value);
                command.Parameters.AddWithValue("$bar", verdict.Bar);
                command.Parameters.AddWithValue("$verdict", verdict.Verdict);
                command.Parameters.AddWithValue("$why", verdict.Why);
                command.Parameters.AddWithValue("$tested_on", throughs[at].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$run_id", runId);
                rows[at] = (long)(await command.ExecuteScalarAsync(cancellation))!;

                output.WriteLine(FormattableString.Invariant($"{Verb}: {verdict.Verdict}: {verdict.Why} {idea.Canonical}"));
            }

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = InsertRun;
                command.Parameters.AddWithValue("$run_id", runId);
                command.Parameters.AddWithValue("$stage", Verb);
                command.Parameters.AddWithValue("$started_at", Stamp(started));
                command.Parameters.AddWithValue("$ended_at", Stamp(clock.UtcNow));
                command.Parameters.AddWithValue("$rows", ideas.Count);
                command.Parameters.AddWithValue("$detail", FormattableString.Invariant($"{ideas.Count} idea(s) tested together at the bar of 1 in {Math.Round(1 / IdeaTester.Bar):0}: {verdicts.Count(one => one.Verdict == IdeaTester.Passed)} passed, {verdicts.Count(one => one.Verdict == IdeaTester.NearMiss)} near misses, {verdicts.Count(one => one.Verdict == IdeaTester.Failed)} failed"));
                await command.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        Written = ideas.Select((idea, at) => (idea.Canonical, rows[at])).ToDictionary(one => one.Canonical, one => one.Item2, StringComparer.Ordinal);

        // Each idea that passed runs by itself from that night: registered as its setup's variant or queued.
        // see: A passed idea registers by itself as its setup's variant where it has room or a variant past its minimum makes room, and queues where none can go
        var desk = new SurvivorDesk(clock, databaseFile, dataRoot);

        for (var at = 0; at < ideas.Count; at++)
        {
            if (verdicts[at].Verdict == IdeaTester.Passed)
            {
                var admitted = await desk.AdmitAsync(rows[at], ideas[at], chosenOn[at], throughs[at], runId, cancellation);

                output.WriteLine($"{Verb}: survivor {admitted.Outcome}: {admitted.Why} {ideas[at].Canonical}");
            }
        }

        return 0;
    }

    async Task<List<(PlanPointTest.FlaggedSetup Flagged, IdeaSetup Setup)>> SetupsAsync(IdeaLine idea, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = SetupsSql(idea);
        command.Parameters.AddWithValue("$index", idea.Index);
        command.Parameters.AddWithValue("$family", idea.Family);
        command.Parameters.AddWithValue("$standing", LongLedger.StandingFrom.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var readings = ReadingsOf(idea);
        var rows = new List<(PlanPointTest.FlaggedSetup, IdeaSetup)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            double? Price(int column) => reader.IsDBNull(column) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(column)));

            var session = DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var values = new Dictionary<string, double>(StringComparer.Ordinal);

            for (var at = 0; at < readings.Count; at++)
            {
                if (!reader.IsDBNull(9 + at))
                {
                    values[readings[at]] = reader.GetDouble(9 + at);
                }
            }

            rows.Add((
                new PlanPointTest.FlaggedSetup(reader.GetString(0), reader.GetString(1), session, Price(3) ?? 0, Price(4) ?? 0, Price(5), Price(6), reader.GetInt32(7), reader.IsDBNull(8) ? null : reader.GetDouble(8)),
                new IdeaSetup(reader.GetString(1), session, values)));
        }

        return rows;
    }

    static string Stamp(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
