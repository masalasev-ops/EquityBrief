using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// From 24.30 the reading recipes' desk: a recipe's line entered once, refused outside the closed grammar, a field or a
// piece of data the store lacks listed once for the operator and blocking nothing; and, on the operator's word alone, each
// recipe not yet run on an index worked over the readings of every member on every session the every-session table holds,
// its figures written only where the figures of evenly spaced sessions, worked again with every later session removed, are
// the same. No rule's version reads a recipe.
// see: A reading recipe is a closed set of operations over stored fields and is kept apart from the pinned catalogue
public sealed class RecipeDesk(IClock clock, string databaseFile, string dataRoot, TextWriter output, Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.SessionReading, Touch.Read),
            new StoreTouch(Store.Recipe, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RecipeRun, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RecipeReading, Touch.Insert),
            new StoreTouch(Store.DataRequest, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "recipe";

    public const string WordFlag = "--word";

    // The sessions a run works each recipe's figures again on with every later session removed, spread evenly over the
    // index's calendar from its first to its last.
    public const int CheckedSessions = 20;

    // The figures a run writes in one transaction.
    public const int Chunk = 50_000;

    public const string OperatorSource = "the operator";

    static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    const string InsertRecipe = "INSERT INTO recipe (line, words, source, entered_at, run_id) VALUES ($line, $words, $source, $at, $run) RETURNING id;";

    const string RecipeOfLine = "SELECT id FROM recipe WHERE line = $line;";

    const string Listed = "SELECT need FROM data_request;";

    const string InsertRequest = "INSERT INTO data_request (need, line, words, source, entered_at, run_id) VALUES ($need, $line, $words, $source, $at, $run);";

    const string Recipes = "SELECT id, line FROM recipe WHERE id NOT IN (SELECT recipe_id FROM recipe_run WHERE index_code = $index) ORDER BY id;";

    const string Calendar = "SELECT DISTINCT session_date FROM session_reading WHERE index_code = $index ORDER BY session_date;";

    const string InsertReading = "INSERT INTO recipe_reading (recipe_id, index_code, session_date, ticker, value, run_id) VALUES ($recipe, $index, $session, $ticker, $value, $run);";

    const string InsertRun = @"
        INSERT INTO recipe_run (recipe_id, index_code, figures, members, first_session, last_session, checked, ran_at, run_id)
        VALUES ($recipe, $index, $figures, $members, $first, $last, $checked, $at, $run);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows, 0, 0, '0', $detail);";

    // A recipe's line entered with its words and its source: refused outside the grammar with why and nothing written; a
    // field it names or data it needs that the store lacks listed once each for the operator, blocking nothing, the recipe
    // entered beside them where its line reads; a line entered before said so. 0 where the line was entered, listed or held
    // already, 2 where the grammar refused it.
    public async Task<int> EnterAsync(string text, string words, string source, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)))[..8].ToLowerInvariant();
        var runId = FormattableString.Invariant($"{Verb}-{hash}-{started.UtcDateTime:yyyyMMddTHHmmssZ}");
        var read = Core.Loop.Recipes.Parse(text);

        if (read.Term is null && read.Lacking.Count == 0)
        {
            output.WriteLine($"{Verb}: refused, {read.Why}; nothing written");

            return 2;
        }

        using var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(Poll, cancellation), cancellation);
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = connection.BeginTransaction();

        var line = read.Term?.Canonical ?? text.Trim();
        var needs = read.Lacking.Select(field => $"the field {field}").Concat(read.Term is { } term ? Core.Loop.Recipes.Needs(term, holdsIndustries: false) : []).ToArray();
        var listed = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = Command(connection, transaction, Listed))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                listed.Add(reader.GetString(0));
            }
        }

        var said = new List<string>();
        var rows = 0;

        foreach (var need in needs)
        {
            if (listed.Contains(need))
            {
                said.Add($"{need} is listed for the operator already");

                continue;
            }

            await using var insert = Command(connection, transaction, InsertRequest, ("$need", need), ("$line", line), ("$words", words), ("$source", source), ("$at", Instant(started)), ("$run", runId));
            await insert.ExecuteNonQueryAsync(cancellation);
            rows++;
            said.Add($"{need} listed for the operator, blocking nothing");
        }

        if (read.Term is { } entered)
        {
            await using var existing = Command(connection, transaction, RecipeOfLine, ("$line", entered.Canonical));

            if (await existing.ExecuteScalarAsync(cancellation) is long id)
            {
                said.Add(FormattableString.Invariant($"'{entered.Canonical}' is entered already as recipe {id}"));
            }
            else
            {
                await using var insert = Command(connection, transaction, InsertRecipe, ("$line", entered.Canonical), ("$words", words), ("$source", source), ("$at", Instant(started)), ("$run", runId));
                var recipe = (long)(await insert.ExecuteScalarAsync(cancellation))!;

                rows++;
                said.Add(FormattableString.Invariant($"'{entered.Canonical}' entered as recipe {recipe}, on test and read by no rule"));
            }
        }
        else
        {
            said.Add($"the recipe is not entered: {read.Why}");
        }

        await using (var log = Command(connection, transaction, AppendRun, ("$run_id", runId), ("$stage", Verb), ("$started_at", Instant(started)), ("$ended_at", Instant(clock.UtcNow)), ("$outcome", "ok"), ("$rows", rows), ("$detail", string.Join("; ", said))))
        {
            await log.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);
        output.WriteLine($"{Verb}: {string.Join("; ", said)}");

        return 0;
    }

    // Each recipe not yet run on the index, worked over the every-session table's readings of its members: without the word,
    // the recipes, their fields and the table's sessions printed and nothing run; with it, each recipe needing nothing the
    // store lacks worked whole, checked on evenly spaced sessions against its figures worked again with every later session
    // removed, and its figures written where they are the same, a recipe reading a later session refused with nothing of it
    // written. 0 where it ran or printed, 2 where it refused to start.
    public async Task<int> RunAsync(string index, bool word, CancellationToken cancellation = default, Func<RecipeTerm, RecipePanel, IReadOnlyDictionary<string, double?[]>>? compute = null)
    {
        var all = compute ?? Core.Loop.Recipes.All;

        if (!WalkForwardTester.Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        var started = clock.UtcNow;
        var runId = FormattableString.Invariant($"{Verb}-{index}-{started.UtcDateTime:yyyyMMddTHHmmssZ}");
        var pending = new List<(long Id, RecipeTerm Term)>();
        var calendar = new List<DateOnly>();

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            await using (var command = Command(connection, null, Recipes, ("$index", index)))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    if (Core.Loop.Recipes.Parse(reader.GetString(1)).Term is { } term)
                    {
                        pending.Add((reader.GetInt64(0), term));
                    }
                }
            }

            await using (var command = Command(connection, null, Calendar, ("$index", index)))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    calendar.Add(Day(reader.GetString(0)));
                }
            }
        }

        var name = DecisionCards.NameOf(index);

        output.WriteLine(FormattableString.Invariant($"{Verb}: {pending.Count} recipe(s) not yet run on the {name}, over the every-session table's {calendar.Count} session(s) of it{(calendar.Count > 0 ? $", {calendar[0]:yyyy-MM-dd} to {calendar[^1]:yyyy-MM-dd}" : string.Empty)}"));

        foreach (var (id, term) in pending)
        {
            var needs = Core.Loop.Recipes.Needs(term, holdsIndustries: false);

            output.WriteLine(FormattableString.Invariant($"{Verb}: recipe {id}, '{term.Canonical}', reading {string.Join(", ", term.Fields)}{(needs.Count > 0 ? $"; waits for {string.Join(", ", needs)}" : string.Empty)}"));
        }

        if (!word)
        {
            output.WriteLine($"{Verb}: nothing run; a run over the store starts on the operator's word alone, given with '{WordFlag}'");

            return 0;
        }

        if (calendar.Count == 0)
        {
            output.WriteLine($"{Verb}: the every-session table holds no session of the {name}; nothing run");

            return 2;
        }

        var said = new List<string>();
        var written = 0;

        foreach (var (id, term) in pending)
        {
            if (Core.Loop.Recipes.Needs(term, holdsIndustries: false) is { Count: > 0 } needs)
            {
                said.Add(FormattableString.Invariant($"recipe {id} waits for {string.Join(", ", needs)}"));

                continue;
            }

            var panel = await PanelAsync(index, calendar, term, cancellation);
            var figures = all(term, panel);
            var checkedAt = CheckedPlaces(calendar.Count);

            if (checkedAt.Where(at => !SameThrough(all, term, panel, figures, at)).Select(at => (int?)at).FirstOrDefault() is { } later)
            {
                said.Add(FormattableString.Invariant($"recipe {id} refused: its figures on {calendar[later]:yyyy-MM-dd} read a session after it, and nothing of it is written"));

                continue;
            }

            var (count, members, first, last) = await WriteAsync(id, index, calendar, figures, runId, checkedAt.Count, cancellation);

            written += count;
            said.Add(FormattableString.Invariant($"recipe {id}, {count} figure(s) of {members} member(s){(first is { } from ? $", {from:yyyy-MM-dd} to {last:yyyy-MM-dd}" : string.Empty)}, its figures on {checkedAt.Count} session(s) the same with every later session removed"));
        }

        await using (var connection = new SqliteConnection(StoreConnection.For(databaseFile)))
        {
            await connection.OpenAsync(cancellation);

            await using var log = Command(connection, null, AppendRun, ("$run_id", runId), ("$stage", Verb), ("$started_at", Instant(started)), ("$ended_at", Instant(clock.UtcNow)), ("$outcome", "ok"), ("$rows", written), ("$detail", string.Join("; ", said)));
            await log.ExecuteNonQueryAsync(cancellation);
        }

        output.WriteLine(FormattableString.Invariant($"{Verb}: {(said.Count == 0 ? "no recipe to run" : string.Join("; ", said))}, in {(clock.UtcNow - started).TotalMinutes:0} min"));

        return 0;
    }

    // The places in a calendar of so many sessions a run checks, spread evenly from the first to the last.
    public static IReadOnlyList<int> CheckedPlaces(int sessions) =>
        sessions <= CheckedSessions
            ? [.. Enumerable.Range(0, sessions)]
            : [.. Enumerable.Range(0, CheckedSessions).Select(at => (int)((long)at * (sessions - 1) / (CheckedSessions - 1))).Distinct()];

    static bool SameThrough(Func<RecipeTerm, RecipePanel, IReadOnlyDictionary<string, double?[]>> all, RecipeTerm term, RecipePanel panel, IReadOnlyDictionary<string, double?[]> figures, int session)
    {
        var cut = all(term, panel.Through(session));

        return figures.All(pair => (pair.Value[session], cut.TryGetValue(pair.Key, out var again) && session < again.Length ? again[session] : null) switch
        {
            (null, null) => true,
            ({ } full, { } through) => full.Equals(through),
            _ => false,
        });
    }

    // The panel a recipe is worked over: each member's standing on each session of the index the every-session table
    // holds, and its figure of each field the recipe reads, read from the table's lists alone.
    async Task<RecipePanel> PanelAsync(string index, IReadOnlyList<DateOnly> calendar, RecipeTerm term, CancellationToken cancellation)
    {
        int[] places = [.. term.Fields.Select(LedgerReadings.IndexOf)];
        var at = calendar.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);
        var members = new Dictionary<string, bool[]>(StringComparer.Ordinal);
        var fields = places.ToDictionary(place => place, _ => new Dictionary<string, double?[]>(StringComparer.Ordinal));

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT ticker, session_date"
            + string.Concat(places.Select((place, column) => $", json_extract(readings, $p{column})"))
            + " FROM session_reading WHERE index_code = $index;";
        command.Parameters.AddWithValue("$index", index);

        for (var column = 0; column < places.Length; column++)
        {
            command.Parameters.AddWithValue($"$p{column}", FormattableString.Invariant($"$[{places[column]}]"));
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var ticker = reader.GetString(0);

            if (!at.TryGetValue(Day(reader.GetString(1)), out var session))
            {
                continue;
            }

            if (!members.TryGetValue(ticker, out var on))
            {
                members[ticker] = on = new bool[calendar.Count];

                foreach (var place in places)
                {
                    fields[place][ticker] = new double?[calendar.Count];
                }
            }

            on[session] = true;

            for (var column = 0; column < places.Length; column++)
            {
                fields[places[column]][ticker][session] = reader.IsDBNull(2 + column) ? null : reader.GetDouble(2 + column);
            }
        }

        return new RecipePanel(calendar, members, fields.ToDictionary(pair => pair.Key, pair => (IReadOnlyDictionary<string, double?[]>)pair.Value));
    }

    // A recipe's figures on an index written a chunk at a time under the drain's lock, its run's row last.
    async Task<(int Figures, int Members, DateOnly? First, DateOnly? Last)> WriteAsync(long recipe, string index, IReadOnlyList<DateOnly> calendar, IReadOnlyDictionary<string, double?[]> figures, string runId, int checkedCount, CancellationToken cancellation)
    {
        var count = 0;
        var members = new HashSet<string>(StringComparer.Ordinal);
        DateOnly? first = null;
        DateOnly? last = null;
        var rows = figures
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .SelectMany(pair => pair.Value.Select((value, at) => (Ticker: pair.Key, At: at, Value: value)))
            .Where(one => one.Value is not null)
            .ToArray();

        for (var from = 0; from <= rows.Length; from += Chunk)
        {
            using var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(Poll, cancellation), cancellation);
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await using var transaction = connection.BeginTransaction();
            await using var insert = Command(connection, transaction, InsertReading, ("$recipe", recipe), ("$index", index), ("$session", string.Empty), ("$ticker", string.Empty), ("$value", 0.0), ("$run", runId));

            foreach (var (ticker, at, value) in rows.Skip(from).Take(Chunk))
            {
                insert.Parameters["$session"].Value = calendar[at].ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                insert.Parameters["$ticker"].Value = ticker;
                insert.Parameters["$value"].Value = value!.Value;
                await insert.ExecuteNonQueryAsync(cancellation);
                count++;
                members.Add(ticker);
                first = first is { } earliest && earliest <= calendar[at] ? earliest : calendar[at];
                last = last is { } latest && latest >= calendar[at] ? latest : calendar[at];
            }

            if (from + Chunk > rows.Length)
            {
                await using var run = Command(connection, transaction, InsertRun, ("$recipe", recipe), ("$index", index), ("$figures", count), ("$members", members.Count), ("$first", first?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$last", last?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)), ("$checked", checkedCount), ("$at", Instant(clock.UtcNow)), ("$run", runId));
                await run.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        return (count, members.Count, first, last);
    }

    static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, params (string Name, object? Value)[] parameters)
    {
        var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Transaction = transaction;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        return command;
    }

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
