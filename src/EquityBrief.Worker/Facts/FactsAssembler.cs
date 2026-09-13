using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Time;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Facts;

// `RowsWritten` is the files the insert wrote, of which `Replaced` took the place
// of a stored file for the same night that differed; `Unchanged` is the files the
// store already held exactly, which nothing was written for.
public sealed record FactsOutcome(int NamesExamined, int RowsWritten, int FactsWritten, int Replaced = 0, int Unchanged = 0);

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
            // The fundamentals read its catalogue row has announced since the
            // architecture was written, and which nothing could add before 6.1
            // created the table. A name with nothing stored carries none of these
            // facts rather than carrying them as nulls, because this table fills
            // on demand and most names on most nights hold nothing.
            new StoreTouch(Store.Fundamentals, Touch.Read),
            // Delete as well as Insert: a stored file for tonight that differs
            // from what the store now computes is removed and written again.
            new StoreTouch(Store.Facts, Touch.Insert | Touch.Delete),
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
    public const string FromFundamentals = "fundamental";

    // How a move's facts are named, stated once, because the claim checker reads
    // a move's span back by these names and a name spelled twice is a span that
    // stops being found without anything failing.
    public static string MovePrefix(int rank) =>
        rank == 1 ? MoveWindows.Largest : MoveWindows.Ranked + " " + rank.ToString(CultureInfo.InvariantCulture);

    public const string MeasuredFromSuffix = MoveWindows.MeasuredFrom;

    // The newest filing this name has stored, and the figures on it. Newest by
    // filing date rather than by period end, because a restatement is filed later
    // than the quarter it restates and the later filing is what is now known.
    const string LatestFilingFor = @"
        SELECT filing_date, payload
        FROM fundamentals
        WHERE ticker = $ticker
        ORDER BY filing_date DESC
        LIMIT 1;
    ";

    // The names holding a bar on the newest session the store has, being the
    // names tonight's file can be about.
    //
    // Every name with any bar until the phase 5 sign-off. The file is keyed on
    // the name's own last bar, so for a name the day's file carried nothing for,
    // or a name that has left, it was a past night's file, recomputed each night
    // over indicator, swing and move windows that drift as the store moves on,
    // and the replacement below deleted and rewrote it whenever the hash moved:
    // EQR's file of 2026-08-17 and PSTG's of 2026-04-16 would have come back
    // whole with their change lists reset on a night that was not a re-run of
    // theirs. A night writes tonight's files, so tonight's names are the ones
    // with a bar tonight; a past night's file is left as that night wrote it.
    // see: A re-run replaces a night's facts file where the store now computes a different one
    const string TickersWithBars = @"
        SELECT DISTINCT ticker FROM bar
        WHERE session_date = (SELECT MAX(session_date) FROM bar)
        ORDER BY ticker;
    ";

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

    // Every move the annotator stored, in rank order. The largest was the only one
    // until 6.6, which is where a local model first writes the cause of each of
    // them, and a cause names the move's date: a date in prose has to be one this
    // file holds, so a cause section over one stored date could only ever speak of
    // one move.
    // owes: The facts file carries the figures a local lane section quotes
    const string MovesFor = @"
        SELECT session_date, sessions, change_pct, rank
        FROM move
        WHERE ticker = $ticker
        ORDER BY rank;
    ";

    // The session a move's change was measured from: the stored session as many
    // sessions before the one it ended on as the move spans, which is the close
    // the annotator divided by. Counted in stored sessions rather than calendar
    // days, for the reason the read surface counts a move's extremes that way.
    //
    // A selection of a stored column rather than a derivation, as the extremes
    // are: which bar is chosen is set by the move row, and its date is handed back
    // unchanged. It is here because a cause of a move can only rest on a document
    // published inside the move, and a file holding only the session a move ended
    // on cannot say where that span began.
    // see: A cause of a move rests only on a document published inside that move
    const string MeasuredFrom = @"
        SELECT session_date
        FROM bar
        WHERE ticker = $ticker AND session_date < $ended
        ORDER BY session_date DESC
        LIMIT 1 OFFSET $offset;
    ";

    const string NextEventFor = @"
        SELECT event_date
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $on_or_after
        ORDER BY event_date
        LIMIT 1;
    ";

    // Insert, with the conflict ignored. `SCHEMA.md` gives this table's Insert
    // to this component and its Update to the change detector, and a table may
    // never have two owners for one operation, so an upsert here would be an
    // update by another name. `writer-ownership` reads the statement rather than
    // the declaration and said so on the first run of this checkpoint.
    const string Insert = @"
        INSERT INTO facts (ticker, session_date, payload, payload_hash)
        VALUES ($ticker, $session_date, $payload, $payload_hash)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    // A stored file for the same night that differs from the one the store now
    // computes, removed so the insert above writes tonight's in its place.
    //
    // Until the phase 5 sign-off the conflict was all there was, so the first
    // file written for a night stood whatever a second run computed: the
    // attempt of 2026-09-10 that stopped at the change detector had written
    // every name's file from band sets a refetch had doubled, the run that
    // completed after the band repair computed clean files, and every one was
    // dropped on the conflict while the run log counted it written. NVDA's file
    // for that night still named its immediate support twice. A file is what
    // every number in a report is checked against, so it has to be what the
    // store computes rather than what the first attempt computed.
    //
    // Keyed on the hash, so a night run twice over an unchanged store deletes
    // nothing and keeps the change list beside its file; a replaced file starts
    // with none, and the change detector, which runs next, writes it again. A
    // delete and an insert rather than an update, because the update is the
    // change detector's and the delete was nobody's.
    // see: A re-run replaces a night's facts file where the store now computes a different one
    const string ReplaceDiffering = @"
        DELETE FROM facts
        WHERE ticker = $ticker AND session_date = $session_date AND payload_hash != $payload_hash;
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
        var replaced = 0;
        var unchanged = 0;

        // One transaction around the whole loop rather than one per row. A row
        // that commits on its own costs a disk sync, and a sync costs the same
        // whatever the row holds, so the price is per row and not per byte. The
        // committed fixture has four names and cannot show it; the first night
        // over 503 did.
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var ticker in tickers)
        {
            var assembled = await FactsForAsync(connection, ticker, cancellation);

            if (assembled is not { Facts.Count: > 0 })
            {
                continue;
            }

            // A fact is declared once and cited by its name, so a file naming
            // one twice is refused by name rather than written. It is what two
            // band sets for one as-of produced for NVDA on 2026-09-10, and the
            // stage that left them is the defect, so the night stops on it.
            // see: Facts are declared once and cited by descriptive name
            var repeated = assembled.Facts
                .GroupBy(fact => fact.Name, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);

            if (repeated is not null)
            {
                throw new InvalidOperationException(
                    $"The facts file for {ticker} would name '{repeated.Key}' {repeated.Count()} times. " +
                    "A fact is declared once and cited by its name, so the file is refused rather than " +
                    "written, and the rows it would have been read from are what to look at.");
            }

            var payload = FactsFile.Serialise(ticker, assembled.SessionDate, assembled.Facts);
            var session = assembled.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var hash = FactsFile.Hash(payload);

            await using (var replace = connection.CreateCommand())
            {
                replace.Transaction = (SqliteTransaction)transaction;
                replace.CommandText = ReplaceDiffering;
                replace.Parameters.AddWithValue("$ticker", ticker);
                replace.Parameters.AddWithValue("$session_date", session);
                replace.Parameters.AddWithValue("$payload_hash", hash);

                replaced += await replace.ExecuteNonQueryAsync(cancellation);
            }

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Insert;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session_date", session);
            command.Parameters.AddWithValue("$payload", payload);
            command.Parameters.AddWithValue("$payload_hash", hash);

            // What the statement did rather than what was asked of it. Until the
            // phase 5 sign-off every name counted as written, and a re-run whose
            // every insert was dropped on the conflict said it had written the
            // lot.
            if (await command.ExecuteNonQueryAsync(cancellation) == 1)
            {
                rows++;
                facts += assembled.Facts.Count;
            }
            else
            {
                unchanged++;
            }
        }

        await transaction.CommitAsync(cancellation);

        var outcome = new FactsOutcome(tickers.Count, rows, facts, replaced, unchanged);

        await RecordAsync(connection, runId, startedAt, outcome, cancellation);

        return outcome;
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

        var moves = new List<(string Prefix, string Ended, int Sessions)>();

        await ReadAsync(connection, MovesFor, ticker, cancellation, reader =>
        {
            // The largest keeps the name it has carried since 5.3, so a night after
            // this change reports no material change to the one move every file
            // already held, and the others are named for their rank.
            var rank = reader.GetInt32(3);
            var prefix = MovePrefix(rank);

            facts.Add(new Fact(prefix + " session", reader.GetString(0), FromMoves));
            facts.Add(new Fact(prefix + " sessions", reader.GetInt32(1).ToString(CultureInfo.InvariantCulture), FromMoves));
            facts.Add(new Fact(prefix + " per cent", reader.GetDouble(2).ToString("0.######", CultureInfo.InvariantCulture), FromMoves));

            moves.Add((prefix, reader.GetString(0), reader.GetInt32(1)));
        });

        foreach (var (prefix, ended, sessions) in moves)
        {
            await using var command = connection.CreateCommand();

            command.CommandText = MeasuredFrom;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$ended", ended);
            command.Parameters.AddWithValue("$offset", sessions - 1);

            // A move whose first close has left the stored year carries no start
            // rather than a guessed one, and a cause of it can then rest on nothing.
            if (await command.ExecuteScalarAsync(cancellation) is string from)
            {
                facts.Add(new Fact(prefix + " " + MeasuredFromSuffix, from, FromMoves));
            }
        }

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

        await ReadAsync(connection, LatestFilingFor, ticker, cancellation, reader =>
            facts.AddRange(Filed(reader.GetString(0), reader.GetString(1))));

        return new Assembled(sessionDate, facts);
    }

    // The figures on the newest stored filing, read back out of the payload and
    // named for what they are.
    //
    // Read rather than derived, which is this component's own rule. The margin on
    // that row was worked out by the fetcher from the two figures in the same
    // filing, and here it is a stored value like any other, which is what keeps
    // one figure from being computed in two places.
    //
    // Eleven facts rather than every line of three statements. What belongs here is
    // what a written section may quote, since a number in prose has to exist in
    // this file, and section 4's numbers row is what names them.
    // see: Every number in written prose must exist in the facts file
    internal static IReadOnlyList<Fact> Filed(string filingDate, string payload)
    {
        var facts = new List<Fact> { new("latest filing date", filingDate, FromFundamentals) };

        using var document = JsonDocument.Parse(payload);

        var root = document.RootElement;

        Add(facts, "latest quarter end", Text(root, "periodEnd"));

        if (root.TryGetProperty("quarter", out var quarter))
        {
            Add(facts, "latest quarter revenue", Text(quarter, "revenue"));
            Add(facts, "latest quarter net income", Text(quarter, "netIncome"));
            Add(facts, "latest quarter gross margin", Text(quarter, "grossMargin"));
            Add(facts, "latest quarter net margin", Text(quarter, "netMargin"));
        }

        if (root.TryGetProperty("balanceSheet", out var sheet))
        {
            Add(facts, "total assets", Text(sheet, "totalAssets"));
            Add(facts, "shareholders equity", Text(sheet, "equity"));
            Add(facts, "net debt", Text(sheet, "netDebt"));
        }

        if (root.TryGetProperty("epsBases", out var bases))
        {
            Add(facts, "trailing earnings per share", Text(bases, "trailing"));
        }

        if (root.TryGetProperty("valuation", out var valuation))
        {
            Add(facts, "trailing price to earnings", Text(valuation, "trailingPe"));
            Add(facts, "forward price to earnings", Text(valuation, "forwardPe"));
        }

        facts.AddRange(Segments(root));

        return facts;
    }

    // The segment table on the newest filing, where the archive supplied one, for
    // the latest quarter it covers.
    //
    // Added at 6.6, because the segment commentary is one sentence per business
    // unit from this table and a figure in prose has to exist in this file: with the
    // table outside it, every sentence that commentary could write failed the number
    // rule and the section fell back on every name. The latest three-month period
    // only, because the commentary is about the quarter and the table's other
    // columns are the same quarter a year before and the year to date.
    //
    // Named by group, line item and period end. Two groups can share a label, which
    // the archive does for one filer the fixture holds, and a facts file names each
    // fact once, so a repeated name carries the group's position in the table.
    // owes: The facts file carries the figures a local lane section quotes
    public static IReadOnlyList<Fact> Segments(JsonElement root)
    {
        if (!root.TryGetProperty("segments", out var segments) || segments.ValueKind != JsonValueKind.Object)
        {
            return [];
        }

        var quarters = segments.GetProperty("periods").EnumerateArray()
            .Where(period => period.GetProperty("months").GetInt32() == 3)
            .Select(period => period.GetProperty("ended").GetString()!)
            .Order(StringComparer.Ordinal)
            .ToArray();

        if (quarters.Length == 0)
        {
            return [];
        }

        var ended = quarters[^1];
        var facts = new List<Fact>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        void Take(string group, JsonElement lines, int position)
        {
            foreach (var line in lines.EnumerateArray())
            {
                if (line.GetProperty("months").GetInt32() != 3
                    || line.GetProperty("ended").GetString() != ended
                    || line.GetProperty("value").ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = $"segment {group} {line.GetProperty("lineItem").GetString()} {ended}";

                if (!taken.Add(name))
                {
                    name = $"segment {group} {position.ToString(CultureInfo.InvariantCulture)} {line.GetProperty("lineItem").GetString()} {ended}";
                    taken.Add(name);
                }

                facts.Add(new Fact(name, line.GetProperty("value").GetString()!, FromFundamentals));
            }
        }

        Take("total", segments.GetProperty("consolidated"), 0);

        var index = 0;

        foreach (var group in segments.GetProperty("groups").EnumerateArray())
        {
            Take(group.GetProperty("label").GetString()!, group.GetProperty("figures"), ++index);
        }

        return facts;
    }

    // A figure the filing does not carry is left out rather than written as a
    // zero or as a blank, for the reason an absent indicator is served as null: a
    // zero is a reading and an absence is not one.
    static void Add(List<Fact> facts, string name, string? value)
    {
        if (value is not null)
        {
            facts.Add(new Fact(name, value, FromFundamentals));
        }
    }

    static string? Text(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

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
        FactsOutcome outcome,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);
        command.Parameters.AddWithValue(
            "$detail",
            $"{outcome.NamesExamined} name(s), {outcome.RowsWritten} file(s) written, {outcome.Replaced} of them " +
            $"replacing a stored file that differed, {outcome.Unchanged} unchanged, {outcome.FactsWritten} fact(s)");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
