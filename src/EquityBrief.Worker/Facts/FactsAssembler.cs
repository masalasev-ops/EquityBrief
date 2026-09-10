using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Facts;

public sealed record FactsOutcome(int NamesExamined, int RowsWritten, int FactsWritten);

// The facts assembler. Writes tonight's facts file for each name, with every
// number the computed sections may use, the source of each, and the hash.
//
// This is the file a written section is checked against: every number in prose
// must exist here, which is what lets a reader trust a paragraph a model wrote.
// So nothing here is derived. Every value is a stored column read back and put
// beside the name of the stage that computed it, and a figure the store does not
// hold is a figure no section may use.
// see: Code owns every number
// see: Every number in written prose must exist in the facts file
// see: Facts are declared once and cited by descriptive name
//
// It owns `payload` and `payload_hash`. `material_changes` is the change
// detector's, on the same row and on a disjoint column, which is what permits a
// table with an inserter and a different updater.
public sealed class FactsAssembler : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Move, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "facts";

    // The sources a fact can name, which are the stages that computed the value.
    // Named once here so the file and the check agree, rather than as a string
    // at each site.
    public const string FromBars = "bar";
    public const string FromIndicators = "indicator";
    public const string FromSwings = "swing";
    public const string FromLevels = "level";
    public const string FromLadder = "ladder";
    public const string FromMoves = "move";
    public const string FromCalendar = "calendar";

    const string TickersWithBars = "SELECT DISTINCT ticker FROM bar ORDER BY ticker;";

    const string LastSessionFor = @"
        SELECT session_date, close, high, low, volume
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    const string IndicatorsFor = @"
        SELECT name, value
        FROM indicator
        WHERE ticker = $ticker
              AND session_date = (SELECT MAX(session_date) FROM indicator WHERE ticker = $ticker)
        ORDER BY name;
    ";

    const string BandsFor = @"
        SELECT role, low_edge, high_edge, strength
        FROM level
        WHERE ticker = $ticker
              AND immediate = 1
              AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = $ticker)
        ORDER BY role;
    ";

    const string LadderFor = @"
        SELECT trend_state
        FROM ladder
        WHERE ticker = $ticker
        ORDER BY as_of DESC
        LIMIT 1;
    ";

    const string SwingCountFor = "SELECT COUNT(*) FROM swing WHERE ticker = $ticker;";

    const string LargestMoveFor = @"
        SELECT session_date, sessions, change_pct
        FROM move
        WHERE ticker = $ticker
        ORDER BY rank
        LIMIT 1;
    ";

    const string NextEventFor = @"
        SELECT event_date
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $on_or_after
        ORDER BY event_date
        LIMIT 1;
    ";

    // Insert, and nothing else. `SCHEMA.md` gives this table's Insert to this
    // component and its Update to the change detector, and a table may never
    // have two owners for one operation, so an upsert here would be an update by
    // another name. `writer-ownership` reads the statement rather than the
    // declaration and said so on the first run of this checkpoint.
    //
    // A night run twice conflicts on every row it already wrote and the conflict
    // is ignored rather than replacing the row, which keeps the night idempotent
    // in what it records about the market and keeps the change list beside it
    // untouched. That is the same shape `news_pulse` takes and it is what makes
    // the per-operation split a property rather than a coincidence of ordering.
    const string Insert = @"
        INSERT INTO facts (ticker, session_date, payload, payload_hash)
        VALUES ($ticker, $session_date, $payload, $payload_hash)
        ON CONFLICT (ticker, session_date) DO NOTHING;
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

    public FactsAssembler(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<FactsOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var tickers = await TickersAsync(connection, cancellation);
        var rows = 0;
        var facts = 0;

        foreach (var ticker in tickers)
        {
            var assembled = await FactsForAsync(connection, ticker, cancellation);

            if (assembled is not { Facts.Count: > 0 })
            {
                continue;
            }

            var payload = FactsFile.Serialise(ticker, assembled.SessionDate, assembled.Facts);

            await using var command = connection.CreateCommand();

            command.CommandText = Insert;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session_date", assembled.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$payload_hash", FactsFile.Hash(payload));

            await command.ExecuteNonQueryAsync(cancellation);

            rows++;
            facts += assembled.Facts.Count;
        }

        await RecordAsync(connection, runId, startedAt, tickers.Count, rows, facts, cancellation);

        return new FactsOutcome(tickers.Count, rows, facts);
    }

    static async Task<IReadOnlyList<string>> TickersAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = TickersWithBars;

        var tickers = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            tickers.Add(reader.GetString(0));
        }

        return tickers;
    }

    sealed record Assembled(DateOnly SessionDate, IReadOnlyList<Fact> Facts);

    // Every fact for one name, each read from a stored column and named for what
    // it is. A name whose night computed nothing has no last session and gets no
    // row, which is an absence rather than a file of nulls.
    async Task<Assembled?> FactsForAsync(SqliteConnection connection, string ticker, CancellationToken cancellation)
    {
        var facts = new List<Fact>();
        DateOnly sessionDate;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = LastSessionFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (!await reader.ReadAsync(cancellation))
            {
                return null;
            }

            sessionDate = DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture);

            facts.Add(new Fact("close", reader.GetString(1), FromBars));
            facts.Add(new Fact("session high", reader.GetString(2), FromBars));
            facts.Add(new Fact("session low", reader.GetString(3), FromBars));
            facts.Add(new Fact("session volume", reader.GetInt64(4).ToString(CultureInfo.InvariantCulture), FromBars));
        }

        await ReadAsync(connection, IndicatorsFor, ticker, cancellation, reader =>
            facts.Add(new Fact(
                reader.GetString(0),
                reader.IsDBNull(1) ? "not available" : reader.GetDouble(1).ToString("0.######", CultureInfo.InvariantCulture),
                FromIndicators)));

        await ReadAsync(connection, BandsFor, ticker, cancellation, reader =>
        {
            var role = reader.GetString(0);

            facts.Add(new Fact($"immediate {role} low edge", reader.GetString(1), FromLevels));
            facts.Add(new Fact($"immediate {role} high edge", reader.GetString(2), FromLevels));
            facts.Add(new Fact($"immediate {role} strength", reader.GetInt32(3).ToString(CultureInfo.InvariantCulture), FromLevels));
        });

        await ReadAsync(connection, LadderFor, ticker, cancellation, reader =>
            facts.Add(new Fact("trend state", reader.GetString(0), FromLadder)));

        await ReadAsync(connection, LargestMoveFor, ticker, cancellation, reader =>
        {
            facts.Add(new Fact("largest move session", reader.GetString(0), FromMoves));
            facts.Add(new Fact("largest move sessions", reader.GetInt32(1).ToString(CultureInfo.InvariantCulture), FromMoves));
            facts.Add(new Fact("largest move per cent", reader.GetDouble(2).ToString("0.######", CultureInfo.InvariantCulture), FromMoves));
        });

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SwingCountFor;
            command.Parameters.AddWithValue("$ticker", ticker);

            facts.Add(new Fact(
                "swings marked",
                Convert.ToInt64(await command.ExecuteScalarAsync(cancellation)).ToString(CultureInfo.InvariantCulture),
                FromSwings));
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = NextEventFor;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$on_or_after", sessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

            // A name with no dated event on file says so rather than carrying a
            // blank, because a guessed date is a wrong date and a blank is a
            // date a reader supplies themselves.
            var next = await command.ExecuteScalarAsync(cancellation);

            facts.Add(new Fact(
                "next dated event",
                next is null or DBNull ? "not on file" : (string)next,
                FromCalendar));
        }

        return new Assembled(sessionDate, facts);
    }

    static async Task ReadAsync(
        SqliteConnection connection,
        string sql,
        string ticker,
        CancellationToken cancellation,
        Action<SqliteDataReader> read)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            read(reader);
        }
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int names,
        int rows,
        int facts,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", $"{names} name(s), {rows} file(s), {facts} fact(s)");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
