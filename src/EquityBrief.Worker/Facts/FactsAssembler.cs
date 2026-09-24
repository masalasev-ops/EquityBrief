using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Components;
using EquityBrief.Core.Facts;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Fundamentals;
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
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Swing, Touch.Read),
            new StoreTouch(Store.VolumeProfile, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Read),
            new StoreTouch(Store.Move, Touch.Read),
            // Each print's reaction, which a written section may quote beside the moves.
            new StoreTouch(Store.EarningsReaction, Touch.Read),
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
    public const string FromReactions = "earnings reaction";
    public const string FromCalendar = "calendar";
    public const string FromFundamentals = "fundamental";
    public const string FromMembership = "membership";

    // The fact carrying the company's name, which every prompt lists with the other facts and a
    // research pass reads to rank a document whose title names the company.
    // see: The membership row carries the company's name the index feed states
    public const string CompanyName = "company name";

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

    // The company's name the membership row holds, from its newest span that states one.
    const string NameFor = @"
        SELECT name
        FROM membership
        WHERE ticker = $ticker AND name IS NOT NULL
        ORDER BY observed_at DESC
        LIMIT 1;
    ";

    // Every move the annotator stored, in rank order. The largest was the only one
    // until 6.6, which is where a local model first writes the cause of each of
    // them, and a cause names the move's date: a date in prose has to be one this
    // file holds, so a cause section over one stored date could only ever speak of
    // one move.
    // owes: The facts file carries the figures a local lane section quotes
    const string MovesFor = @"
        SELECT session_date, sessions, change_pct, rank, group_kind, group_name, group_members, group_counted, group_median
        FROM move
        WHERE ticker = $ticker
        ORDER BY rank;
    ";

    // Every print's reaction the annotator stored for the name, newest first, which a section
    // on the earnings may quote.
    // see: Each print's reaction is read from the nightly calendar and the stored bars, and reaches no reason, gate or plan
    const string ReactionsFor = @"
        SELECT report_date, timing, reaction_session, estimate, actual, surprise_pct, move_pct
        FROM earnings_reaction
        WHERE ticker = $ticker
        ORDER BY report_date DESC;
    ";

    // How a print's facts are named: by its place counting back from the newest, as a move is
    // named by its rank, and never by its date. The claim checker reads a number in a fact's
    // name as a window the file carries, so a date written into a name would admit its year,
    // its month and its day as windows; the date is a value instead, where a date belongs.
    public static string ReactionPrefix(int place) =>
        place == 1 ? "latest earnings reaction" : "earnings reaction " + place.ToString(CultureInfo.InvariantCulture);

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

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
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

        await ReadAsync(connection, NameFor, ticker, cancellation, reader =>
            facts.Add(new Fact(CompanyName, reader.GetString(0), FromMembership)));

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
        var grouped = false;

        await ReadAsync(connection, MovesFor, ticker, cancellation, reader =>
        {
            // The largest keeps the name it has carried since 5.3, so a night after
            // this change reports no material change to the one move every file
            // already held, and the others are named for their rank.
            var rank = reader.GetInt32(3);
            var prefix = MovePrefix(rank);
            var median = !reader.IsDBNull(4);

            // The name's group, once and before its moves, being the one the annotator read
            // every move against, then each move's median over its own sessions with how many
            // members it counted, as the annotator stored them. A move stored before the median
            // was carries none.
            // see: A large move is shown beside its group's median move over the same sessions
            if (median && !grouped)
            {
                grouped = true;

                if (!reader.IsDBNull(5))
                {
                    facts.Add(new Fact("group", reader.GetString(5), FromMoves));
                }

                facts.Add(new Fact("group kind", reader.GetString(4), FromMoves));
                facts.Add(new Fact("group members", reader.GetInt32(6).ToString(CultureInfo.InvariantCulture), FromMoves));
            }

            facts.Add(new Fact(prefix + " session", reader.GetString(0), FromMoves));
            facts.Add(new Fact(prefix + " sessions", reader.GetInt32(1).ToString(CultureInfo.InvariantCulture), FromMoves));
            facts.Add(new Fact(prefix + " per cent", reader.GetDouble(2).ToString("0.######", CultureInfo.InvariantCulture), FromMoves));

            if (median)
            {
                facts.Add(new Fact(prefix + " group members counted", reader.GetInt32(7).ToString(CultureInfo.InvariantCulture), FromMoves));
                facts.Add(new Fact(
                    prefix + " group median per cent",
                    reader.IsDBNull(8) ? "not available" : reader.GetDouble(8).ToString("0.######", CultureInfo.InvariantCulture),
                    FromMoves));
            }

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

        // Each print's reaction, newest first, with its timing as the calendar stated it and a
        // figure the provider did not file saying so rather than standing as a zero.
        var place = 0;

        await ReadAsync(connection, ReactionsFor, ticker, cancellation, reader =>
        {
            var print = ReactionPrefix(++place);

            facts.Add(new Fact(print + " report date", reader.GetString(0), FromReactions));
            facts.Add(new Fact(print + " timing", reader.GetString(1), FromReactions));
            facts.Add(new Fact(print + " session", reader.GetString(2), FromReactions));
            facts.Add(new Fact(print + " estimate", reader.IsDBNull(3) ? "none filed" : reader.GetString(3), FromReactions));
            facts.Add(new Fact(print + " actual", reader.IsDBNull(4) ? "none filed" : reader.GetString(4), FromReactions));
            facts.Add(new Fact(
                print + " surprise per cent",
                reader.IsDBNull(5) ? "none filed" : reader.GetDouble(5).ToString("0.######", CultureInfo.InvariantCulture),
                FromReactions));
            facts.Add(new Fact(print + " move per cent", reader.GetDouble(6).ToString("0.######", CultureInfo.InvariantCulture), FromReactions));
        });

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

        // The quarter's earnings per share against the estimate for it, and its growth on the
        // same quarter a year before and on the one before it, as the fetcher computed it.
        // see: The facts file carries a quarter's growth, its earnings against the estimate and the filing's own tables with their year-earlier columns
        if (root.TryGetProperty("earnings", out var earnings) && earnings.ValueKind == JsonValueKind.Object)
        {
            Add(facts, "latest quarter earnings per share", Text(earnings, "epsActual"));
            Add(facts, "latest quarter earnings per share estimate", Text(earnings, "epsEstimate"));
        }

        if (root.TryGetProperty("growth", out var growth) && growth.ValueKind == JsonValueKind.Object)
        {
            Add(facts, "latest quarter revenue growth on a year earlier", Text(growth, "revenue"));
            Add(facts, "latest quarter net income growth on a year earlier", Text(growth, "netIncome"));
            Add(facts, "latest quarter earnings per share growth on a year earlier", Text(growth, "epsActual"));
            Add(facts, "latest quarter revenue growth on the quarter before", Text(growth, "revenueOnTheQuarterBefore"));
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

        // The dividend the provider files, on the newest filing's row alone.
        // see: The numbers section draws the dividend the provider files from the newest filing alone, and nothing for a company paying none
        if (root.TryGetProperty("dividend", out var dividend) && dividend.ValueKind == JsonValueKind.Object)
        {
            Add(facts, "dividend forward annual rate", Text(dividend, "forwardAnnualRate"));
            Add(facts, "dividend forward yield", Text(dividend, "forwardYield"));
            Add(facts, "dividend payout ratio", Text(dividend, "payoutRatio"));
            Add(facts, "ex-dividend date", Text(dividend, "exDividendDate"));
            Add(facts, "dividend pay date", Text(dividend, "payDate"));
        }

        facts.AddRange(Segments(root));
        facts.AddRange(RevenueTables(root));
        facts.AddRange(TableGrowth(root));
        facts.AddRange(Guided(root));

        return facts;
    }

    // The segment table on the newest filing, where the archive supplied one, for
    // the newest period it covers: a quarter where it files one, and otherwise its
    // newest longer period, which after an annual report is twelve months.
    //
    // Named by group, line item and period end. Two groups can share a label, which
    // the archive does for one filer the fixture holds, and a facts file names each
    // fact once, so a repeated name carries the group's position in the table.
    // owes: The facts file carries the figures a local lane section quotes
    public static IReadOnlyList<Fact> Segments(JsonElement root) =>
        root.TryGetProperty("segments", out var segments) && segments.ValueKind == JsonValueKind.Object
            ? Table(segments, SegmentPeriods.Prefix)
            : [];

    // The filing's other tables of revenue by a grouping, each named by what its title says it
    // groups by, so a figure by market platform is not read as a segment's.
    // see: The facts file carries a quarter's growth, its earnings against the estimate and the filing's own tables with their year-earlier columns
    public static IReadOnlyList<Fact> RevenueTables(JsonElement root) =>
        root.TryGetProperty("revenueTables", out var tables) && tables.ValueKind == JsonValueKind.Array
            ? [.. tables.EnumerateArray().SelectMany(table => Table(table, Prefix(table)))]
            : [];

    // Each group's growth on the same months a year before, in the segment table and the other
    // tables, as the fetcher computed it from each table's own columns, named as the table's own
    // figures are, a repeated label by its position.
    public static IReadOnlyList<Fact> TableGrowth(JsonElement root)
    {
        if (!root.TryGetProperty("tableGrowth", out var grown) || grown.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var prefixes = new Dictionary<string, string>(StringComparer.Ordinal);

        if (root.TryGetProperty("revenueTables", out var tables) && tables.ValueKind == JsonValueKind.Array)
        {
            foreach (var table in tables.EnumerateArray())
            {
                prefixes[table.GetProperty("report").GetString()!] = Prefix(table);
            }
        }

        var facts = new List<Fact>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        foreach (var figure in grown.EnumerateArray())
        {
            var prefix = prefixes.GetValueOrDefault(figure.GetProperty("report").GetString()!, SegmentPeriods.Prefix);
            var label = figure.GetProperty("label").GetString();
            var line = figure.GetProperty("lineItem").GetString() + " growth on a year earlier "
                + SegmentPeriods.Name(figure.GetProperty("months").GetInt32(), figure.GetProperty("ended").GetString()!);
            var name = $"{prefix}{label} {line}";

            if (!taken.Add(name))
            {
                name = $"{prefix}{label} {figure.GetProperty("group").GetInt32().ToString(CultureInfo.InvariantCulture)} {line}";

                if (!taken.Add(name))
                {
                    continue;
                }
            }

            facts.Add(new Fact(name, figure.GetProperty("value").GetString()!, FromFundamentals));
        }

        return facts;
    }

    // How a table of revenue by a grouping names its figures: the segment prefix and what its title
    // says it groups by.
    static string Prefix(JsonElement table) =>
        SegmentPeriods.Prefix + Grouping(table.GetProperty("title").GetString() ?? string.Empty) + " ";

    // What a table of revenue groups by, read off its title: "revenue by market platform" from a
    // title reading "Segment Information - Schedule of Revenue by Market Platform (Details)".
    static string Grouping(string title)
    {
        var named = Regex.Match(title, @"(?i)\b(?:revenue|sales)\s+by\s+[^()|$]+");

        return (named.Success ? named.Value : "other table").Trim().ToLowerInvariant();
    }

    // The figures of one table: the latest period it files, being the latest quarter where it files
    // one and otherwise its newest period, and the same months ending within a week of a year
    // before it, each named by group, line item and period end.
    static IReadOnlyList<Fact> Table(JsonElement segments, string prefix)
    {
        // see: A facts file carries the latest period of the segment table, and says which period it is
        var periods = segments.GetProperty("periods").EnumerateArray()
            .Select(period => (Months: period.GetProperty("months").GetInt32(), Ended: period.GetProperty("ended").GetString()!))
            .ToArray();

        var quarters = periods.Where(period => period.Months == SegmentPeriods.QuarterMonths).ToArray();
        var latest = (quarters.Length > 0 ? quarters : periods)
            .OrderBy(period => period.Ended, StringComparer.Ordinal)
            .ThenBy(period => period.Months)
            .ToArray();

        if (latest.Length == 0)
        {
            return [];
        }

        var (months, ended) = latest[^1];
        var facts = new List<Fact>();
        var taken = new HashSet<string>(StringComparer.Ordinal);

        // The same months a year before, which the table states beside the latest.
        var yearBefore = DateOnly.ParseExact(ended, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddYears(-1);
        var earlier = periods
            .Where(held => held.Months == months
                && Math.Abs(DateOnly.ParseExact(held.Ended, "yyyy-MM-dd", CultureInfo.InvariantCulture).DayNumber - yearBefore.DayNumber) <= FundamentalsFetcher.PeriodEndDays)
            .Select(held => held.Ended)
            .FirstOrDefault();

        void Take(string group, JsonElement lines, int position, string at)
        {
            // A quarter keeps the name every stored facts file carries; a longer period
            // names its months, which the prompt and the claim checker read back.
            var period = SegmentPeriods.Name(months, at);

            foreach (var line in lines.EnumerateArray())
            {
                if (line.GetProperty("months").GetInt32() != months
                    || line.GetProperty("ended").GetString() != at
                    || line.GetProperty("value").ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var name = $"{prefix}{group} {line.GetProperty("lineItem").GetString()} {period}";

                // A label repeated in one table is told apart by its position, and a line repeated
                // under one group for one period is the same figure twice, which is kept once, since
                // a file naming a fact twice cannot be written.
                if (!taken.Add(name))
                {
                    name = $"{prefix}{group} {position.ToString(CultureInfo.InvariantCulture)} {line.GetProperty("lineItem").GetString()} {period}";

                    if (!taken.Add(name))
                    {
                        continue;
                    }
                }

                facts.Add(new Fact(name, line.GetProperty("value").GetString()!, FromFundamentals));
            }
        }

        foreach (var at in earlier is null ? [ended] : new[] { ended, earlier })
        {
            Take(FundamentalsFetcher.CompanyRows, segments.GetProperty("consolidated"), 0, at);

            var index = 0;

            foreach (var group in segments.GetProperty("groups").EnumerateArray())
            {
                Take(group.GetProperty("label").GetString()!, group.GetProperty("figures"), ++index, at);
            }
        }

        return facts;
    }

    // Each figure the claim checker reads in management's located guidance passage, as the checker
    // reads it, named by its place in the passage and never by what it guides. A passage no
    // heading located carries none, because nothing says which paragraph is the guidance.
    // see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
    internal static IReadOnlyList<Fact> Guided(JsonElement root)
    {
        if (!root.TryGetProperty("guidance", out var guidance)
            || guidance.ValueKind != JsonValueKind.Object
            || !guidance.TryGetProperty("located", out var located)
            || located.ValueKind != JsonValueKind.True
            || Text(guidance, "passage") is not { Length: > 0 } passage)
        {
            return [];
        }

        return
        [
            .. ClaimRules.Figures(passage)
                .Where(figure => figure.Kind == FigureKind.Figure)
                .Select((figure, at) => new Fact(
                    $"guidance figure {(at + 1).ToString(CultureInfo.InvariantCulture)}",
                    (figure.Magnitude * figure.Scale).ToString("0.############", CultureInfo.InvariantCulture),
                    FromFundamentals)),
        ];
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
