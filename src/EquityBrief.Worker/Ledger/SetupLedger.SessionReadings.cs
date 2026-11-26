using System.Diagnostics;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// Every member's readings on every session from 2005, a row a member an index a session with the members as they stood. The
// build reads the long history and works each member's readings as the night works them; a session a night stored is copied
// from the night's own rows instead. Nothing is written without the word, and no night reads the table.
// see: The readings for every stock on every session are built once before the reviewer, the picker's comparison reading the same plan on every stock until then
public sealed partial class SetupLedger
{
    public const string SessionReadingsStage = "session-readings";

    public const string SessionCheckStage = "session-readings-check";

    // A row the build worked from the long history, beside the night's own source.
    public const string BuildSource = "build";

    // The sessions the build reads and times before it states its count and its time: the first year it would write.
    public const int MeasuredSessions = 252;

    // The days before a night whose own rows each night copies again, the night's later steps rewriting the insider, business
    // and release readings of the nights before it in place.
    public const int NightCopyDays = 14;

    // The sessions a year the check samples, and the seed it samples by.
    public const int SessionCheckPerYear = 5;

    public const int SessionCheckSeed = 19;

    // The readings a setup's row holds of its family alone, which a member's row holds none of and the check compares no
    // setup's row on.
    public static IReadOnlyList<string> FamilyOwnReadings { get; } = ["reward_to_risk", "freshness", "band_strength", "volume_multiple", "range_ratio", "reaction_moves"];

    static readonly string LongSetupsOn =
        "SELECT family, ticker, " + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM long_setup WHERE index_code = $index AND session_date = $session ORDER BY family, ticker;";

    static readonly string StandingSetupsOn =
        "SELECT family, ticker, " + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM setup WHERE index_code = $index AND session_date = $session ORDER BY family, ticker;";

    // The build by hand, from a clean copy of main's commit: every session from 2005 on one index the table holds no row of
    // under the catalogue's version now, its count and its time stated first and nothing written without `word`; each chunk
    // waiting for the night and holding the drain's lock while it writes, then each year of the index counted. From the 24.19
    // correction, with `again`, every session written once more from the session given or the first, each cleared whole and
    // written in its chunk's transaction, a session the nights stored copied again from their rows, and every row held on a
    // session outside the calendar over that span removed first; a build stopped part way is run again from the session its
    // last chunk line names. Every build then holds the rows the table holds on each session it wrote to the rows it wrote
    // there, and a session outside the calendar to none, and stops short of an ok outcome where either differs.
    public async Task<int> SessionReadingsAsync(string dataRoot, string index, bool word, TextWriter output, Func<TimeSpan, CancellationToken, Task>? wait = null, CancellationToken cancellation = default, Func<long>? storeSize = null, bool again = false, DateOnly? againFrom = null)
    {
        var startedAt = clock.UtcNow;
        var pause = wait ?? Task.Delay;
        long SizeNow() => storeSize?.Invoke() ?? new FileInfo(databaseFile).Length;

        await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var longHistory = new LongHistoryReader(databaseFile);
        var through = await longHistory.NewestSessionAsync(cancellation);
        var prepared = await PrepareAsync(connection, index, through, output, cancellation, heavyweightsRead: false, live: await LiveRulesAsync(index, output, cancellation), longHistory: longHistory);

        prepared.ReportFilings = await ReportFilingsAsync(connection, cancellation);

        var calendar = prepared.Calendar;
        var first = Array.FindIndex(calendar, session => session >= LongLedger.From);
        var last = Array.FindLastIndex(calendar, session => session <= through);

        if (first < 0 || last < first)
        {
            output.WriteLine("session-readings: the long history holds no session from 2005");

            return 1;
        }

        var stored = await SessionsAsync(connection, SessionsAtPin, [("$index", index), ("$pin", LedgerReadings.Version)], cancellation);
        var nights = await SessionsAsync(connection, NightSessionsOf, [("$index", index)], cancellation);
        var toWrite = again
            ? Enumerable.Range(first, last - first + 1).Where(at => againFrom is not { } from || calendar[at] >= from).ToArray()
            : Enumerable.Range(first, last - first + 1).Where(at => !stored.Contains(calendar[at])).ToArray();
        var toWork = toWrite.Where(at => !nights.Contains(calendar[at])).ToArray();
        var sizeBefore = SizeNow();

        output.WriteLine(FormattableString.Invariant($"session-readings: {toWrite.Length} session(s) of the {DecisionCards.NameOf(index)} to write of the {last - first + 1} from {Stamp(calendar[first])} to {Stamp(calendar[last])}, {toWork.Length} worked from the long history and {toWrite.Length - toWork.Length} copied from the nights' own rows; the store holds {sizeBefore:N0} bytes"));

        if (toWrite.Length == 0)
        {
            output.WriteLine("session-readings: every session is written under the catalogue's version now");

            return 0;
        }

        // The count and the time, measured on the first year the build would work, before anything is written.
        var measured = toWork.Take(MeasuredSessions).ToArray();
        var watch = Stopwatch.StartNew();
        var measuredRows = Computed(measured.Length, item => prepared.MemberReadings(measured[item]));
        var seconds = watch.Elapsed.TotalSeconds;
        var worked = measured.Select((at, item) => (at, item)).ToDictionary(pair => pair.at, pair => measuredRows[pair.item]);

        output.WriteLine(SessionEstimate(measured.Length, measuredRows.Sum(rows => rows.Count), measuredRows.Sum(rows => rows.Sum(row => (long)row.Readings.Length + row.Ticker.Length + RowOverheadBytes)), seconds, toWork.Length, Cores, measured.Length == 0 ? null : calendar[measured[0]]));

        if (!word)
        {
            output.WriteLine("session-readings: the build writes only on the operator's word, given as '--word'; nothing was written");

            return 2;
        }

        var runId = FormattableString.Invariant($"{SessionReadingsStage}-{index}-{startedAt:yyyyMMddTHHmmssZ}");
        var timed = Stopwatch.StartNew();
        var written = 0L;
        var copied = 0;
        var writtenOn = new Dictionary<DateOnly, long>();
        var onCalendar = calendar[first..(last + 1)].ToHashSet();
        var spanFrom = again && againFrom is { } given ? given : DateOnly.MinValue;
        var removed = 0;

        if (again)
        {
            using var drain = await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            foreach (var stray in (await HeldBySessionAsync(connection, transaction, index, spanFrom, calendar[last], cancellation)).Keys.Where(session => !onCalendar.Contains(session)))
            {
                await using var clear = Command(connection, transaction, ClearSession, [("$index", index), ("$session", Stamp(stray))]);

                removed += await clear.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
        }

        for (var taken = 0; taken < toWrite.Length; taken += ChunkSessions)
        {
            var chunk = toWrite.Skip(taken).Take(ChunkSessions).ToArray();

            await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

            var chunkWatch = Stopwatch.StartNew();
            var rows = Computed<IReadOnlyList<(string Ticker, string Readings)>>(chunk.Length, item => nights.Contains(calendar[chunk[item]])
                ? []
                : worked.TryGetValue(chunk[item], out var held) ? held : prepared.MemberReadings(chunk[item]));

            using var drain = await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            var chunkRows = 0L;

            for (var item = 0; item < chunk.Length; item++)
            {
                var session = Stamp(calendar[chunk[item]]);

                if (nights.Contains(calendar[chunk[item]]))
                {
                    var night = await CopyNightSessionsAsync(connection, transaction, index, calendar[chunk[item]], calendar[chunk[item]], cancellation);

                    writtenOn[calendar[chunk[item]]] = night;
                    chunkRows += night;
                    copied++;

                    continue;
                }

                await ExecuteAsync(connection, transaction, ClearSession, [("$index", index), ("$session", session)], cancellation);

                foreach (var (ticker, readings) in rows[item])
                {
                    await ExecuteAsync(connection, transaction, InsertSessionReading,
                        [("$index", index), ("$session", session), ("$ticker", ticker), ("$readings", readings), ("$pin", LedgerReadings.Version), ("$source", BuildSource)], cancellation);
                }

                writtenOn[calendar[chunk[item]]] = rows[item].Count;
                chunkRows += rows[item].Count;
            }

            await transaction.CommitAsync(cancellation);

            foreach (var at in chunk)
            {
                worked.Remove(at);
            }

            written += chunkRows;
            output.WriteLine(FormattableString.Invariant($"session-readings: {Stamp(calendar[chunk[0]])} to {Stamp(calendar[chunk[^1]])}, {chunk.Length} session(s), {chunkRows} row(s) in {chunkWatch.Elapsed.TotalSeconds:0} s, {written} so far; the store holds {SizeNow():N0} bytes"));
        }

        IReadOnlyList<SessionYear> years;

        using (await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation))
        {
            years = await CountSessionYearsAsync(connection, null, index, calendar[first].Year, calendar[last].Year, cancellation);
        }

        var apart = HeldAgainstWritten(await HeldBySessionAsync(connection, null, index, spanFrom, calendar[last], cancellation), writtenOn, onCalendar);
        var detail = FormattableString.Invariant($"{written} row(s) of every member's readings on the {DecisionCards.NameOf(index)} over {Stamp(calendar[first])} to {Stamp(calendar[last])}, {toWrite.Length - copied} session(s) worked from the long history and {copied} copied from the nights' own rows; ")
            + (again ? FormattableString.Invariant($"{removed} row(s) held on no session of the calendar removed; ") : "")
            + (apart ?? FormattableString.Invariant($"the table holds {written} row(s) on the sessions written, as written; "))
            + FormattableString.Invariant($"{years.Sum(year => year.Rows)} row(s) over {years.Count} year(s) in all")
            + SizeDetail(sizeBefore, SizeNow(), (int)Math.Min(int.MaxValue, written), toWrite.Length, timed.Elapsed).Replace("a setup", "a row", StringComparison.Ordinal);

        await AppendAsync(connection, runId, SessionReadingsStage, startedAt, (int)Math.Min(int.MaxValue, written), apart is null ? Ok : NotComputed, detail, cancellation);
        output.WriteLine("session-readings: " + detail);

        return apart is null ? 0 : 3;
    }

    static async Task<Dictionary<DateOnly, long>> HeldBySessionAsync(SqliteConnection connection, SqliteTransaction? transaction, string index, DateOnly from, DateOnly through, CancellationToken cancellation)
    {
        var held = new Dictionary<DateOnly, long>();

        await using var command = Command(connection, transaction, HeldBySession, [("$index", index), ("$from", Stamp(from)), ("$through", Stamp(through))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            held[Date(reader.GetString(0))] = reader.GetInt64(1);
        }

        return held;
    }

    // The rows the table holds over a build's span held to what the build wrote: each session it wrote holding the rows it
    // wrote there, and no session outside the calendar holding any. Nothing where both hold, else what differs, named.
    public static string? HeldAgainstWritten(IReadOnlyDictionary<DateOnly, long> held, IReadOnlyDictionary<DateOnly, long> written, IReadOnlySet<DateOnly> calendar)
    {
        var differing = written.Where(session => held.GetValueOrDefault(session.Key) != session.Value).OrderBy(session => session.Key).ToArray();
        var strays = held.Where(session => !calendar.Contains(session.Key)).OrderBy(session => session.Key).ToArray();

        if (differing.Length == 0 && strays.Length == 0)
        {
            return null;
        }

        var parts = new List<string>();

        if (differing.Length > 0)
        {
            parts.Add(FormattableString.Invariant($"{differing.Length} session(s) written hold other than the rows written, {Stamp(differing[0].Key)} first with {held.GetValueOrDefault(differing[0].Key)} held of {differing[0].Value} written, {written.Keys.Sum(session => held.GetValueOrDefault(session))} row(s) held on the sessions written of {written.Values.Sum()} written"));
        }

        if (strays.Length > 0)
        {
            parts.Add(FormattableString.Invariant($"{strays.Sum(session => session.Value)} row(s) held on {strays.Length} session(s) outside the calendar, {Stamp(strays[0].Key)} first"));
        }

        return "the table does not hold what the build wrote: " + string.Join("; ", parts) + "; ";
    }

    // The sessions a night stored over a span written again whole from the night's own rows, and the rows copied.
    static async Task<int> CopyNightSessionsAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly from, DateOnly through, CancellationToken cancellation)
    {
        IReadOnlyList<(string Name, object Value)> span = [("$index", index), ("$from", Stamp(from)), ("$through", Stamp(through))];

        await ExecuteAsync(connection, transaction, ClearNightSessions, span, cancellation);

        await using var copy = Command(connection, transaction, CopyNightReadings, span);

        return await copy.ExecuteNonQueryAsync(cancellation);
    }

    // The bytes a row takes beside its readings' text and its ticker: its index, session, pin and source, and the key's entry.
    public const int RowOverheadBytes = 64;

    // What the build states before it writes: the sessions it measured, their rows and their time, and the rows, hours and
    // growth it expects for the sessions it would work at that rate.
    public static string SessionEstimate(int sessions, long rows, long bytes, double seconds, int toWork, int cores, DateOnly? from)
    {
        if (sessions == 0)
        {
            return "session-readings: no session to work from the long history, every one to write copied from the nights' own rows";
        }

        var rowsASession = 1.0 * rows / sessions;
        var secondsASession = seconds / sessions;
        var bytesARow = rows == 0 ? 0 : 1.0 * bytes / rows;
        var expected = rowsASession * toWork;

        return FormattableString.Invariant($"session-readings: measured on {sessions} session(s) from {Stamp(from!.Value)}, {rows:N0} row(s) in {seconds:0.0} s across {cores} core(s), {rowsASession:0.0} a session at {secondsASession:0.00} s a session; ")
            + FormattableString.Invariant($"about {expected:N0} row(s) over the {toWork} session(s) to work, about {secondsASession * toWork / 60:0.0} minute(s) of reading before the writes, and about {expected * bytesARow:N0} bytes of growth at {bytesARow:0} a row");
    }

    // A year of an index's rows as counted: its rows, its sessions, the sessions a night stored its own rows of, and its first
    // and last session.
    public sealed record SessionYear(int Year, long Rows, int Sessions, int NightSessions, DateOnly First, DateOnly Last);

    // Each year of an index from the first given through the last counted again and written whole, the years before standing.
    public async Task<IReadOnlyList<SessionYear>> CountSessionYearsAsync(SqliteConnection connection, SqliteTransaction? transaction, string index, int from, int through, CancellationToken cancellation)
    {
        var years = new List<SessionYear>();
        var nightSessions = new Dictionary<int, int>();
        IReadOnlyList<(string Name, object Value)> span = [("$index", index), ("$from", FormattableString.Invariant($"{from:0000}-01-01")), ("$before", FormattableString.Invariant($"{through + 1:0000}-01-01"))];

        await using (var command = Command(connection, transaction, CountNightSessions, span))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                nightSessions[reader.GetInt32(0)] = reader.GetInt32(1);
            }
        }

        await using (var command = Command(connection, transaction, CountSessionYears, span))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                years.Add(new SessionYear(reader.GetInt32(0), reader.GetInt64(1), reader.GetInt32(2), nightSessions.GetValueOrDefault(reader.GetInt32(0)), Date(reader.GetString(3)), Date(reader.GetString(4))));
            }
        }

        await ExecuteAsync(connection, transaction, ClearSessionYears, [("$index", index), ("$from", from), ("$through", through)], cancellation);

        foreach (var year in years)
        {
            await ExecuteAsync(connection, transaction, InsertSessionYear,
            [
                ("$index", index), ("$year", year.Year), ("$rows", year.Rows), ("$sessions", year.Sessions), ("$night_sessions", year.NightSessions),
                ("$first", Stamp(year.First)), ("$last", Stamp(year.Last)), ("$counted_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture)),
            ], cancellation);
        }

        return years;
    }

    // The night's own rows on an index copied into the table for the night and the days before it, inside the night's own
    // transaction, each such session written again whole, and the years they fall in counted again.
    async Task<int> CopyTheNightsReadingsAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        var from = night.AddDays(-NightCopyDays);
        var copied = await CopyNightSessionsAsync(connection, transaction, index, from, night, cancellation);

        await CountSessionYearsAsync(connection, transaction, index, from.Year, night.Year, cancellation);

        return copied;
    }

    static async Task<HashSet<DateOnly>> SessionsAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        var sessions = new HashSet<DateOnly>();

        await using var command = Command(connection, null, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            sessions.Add(Date(reader.GetString(0)));
        }

        return sessions;
    }

    // The check by hand: a seeded sample of each year's sessions on the index. On each, one row the build worked is rebuilt
    // from the long history cut at its session and held to the row stored; and every setup the ledger holds of the session,
    // the long ledger's to 2018 and the standing ledger's after, is held to its stock's row on every reading but its family's
    // own; a session the night stored is held to the night's own rows. Every reading that differs is named. It reads the
    // store and writes its run log row alone.
    public async Task<int> SessionCheckAsync(string index, TextWriter output, int perYear = SessionCheckPerYear, int seed = SessionCheckSeed, CancellationToken cancellation = default, bool overLedgerMembers = false)
    {
        var startedAt = clock.UtcNow;
        var runId = FormattableString.Invariant($"{SessionCheckStage}-{index}-{startedAt:yyyyMMddTHHmmssZ}");

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var longHistory = new LongHistoryReader(databaseFile);
        var through = await longHistory.NewestSessionAsync(cancellation);
        var inputs = await longHistory.ReadAsync(index, through, output.WriteLine, cancellation);
        var history = new SweepHistory(databaseFile);
        string[] filers = [LongFilers, PulledFilers, Filers];
        var income = await history.IncomeAsync(through, cancellation);
        var market = await MarketAsync(connection, LongMarketBars, through, cancellation);
        var sectors = await SectorsAsync(connection, PulledSectors, inputs.Names, cancellation);
        var facts = await FactsAsync(connection, filers, inputs.Names.Select(name => name.Ticker), cancellation, LongFactsOfFiler);
        var insiders = await InsidersAsync(connection, filers, inputs.Names.Select(name => name.Ticker), FirstInsiderDay(inputs.Sessions), cancellation);
        var releases = await ReleasesAsync(connection, filers, inputs.Names.Select(name => name.Ticker), cancellation);
        var oneOffs = await OneOffsAsync(connection, filers, inputs.Names.Select(name => name.Ticker), cancellation);
        var actions = await ActionsAsync(connection, inputs.Names.Select(name => name.Ticker), cancellation);
        var trends = await TrendsAsync(connection, inputs.Names.Select(name => name.Ticker), cancellation);
        var shortInterest = await ShortInterestAsync(connection, inputs.Names.Select(name => name.Ticker), cancellation);
        var settlements = await ShortInterestSettlementsAsync(connection, cancellation);
        var largeBreadth = index == IndexFamilies.LargeIndex
            ? null
            : LargeBreadthOver(inputs.Sessions, SweepSwitches.LargeBreadth(await longHistory.ReadAsync(IndexFamilies.LargeIndex, through, null, cancellation), inputs.Sessions));
        var gics = await GicsAsync(connection, history: true, cancellation);
        var nights = await SessionsAsync(connection, NightSessionsOf, [("$index", index)], cancellation);
        var reportFilings = await ReportFilingsAsync(connection, cancellation);

        var random = new Random(seed);
        var sample = inputs.Sessions
            .Where(session => session >= LongLedger.From)
            .GroupBy(session => session.Year)
            .OrderBy(year => year.Key)
            .SelectMany(year => year.Select(session => (Session: session, Order: random.Next())).OrderBy(pair => pair.Order).Take(perYear).Select(pair => pair.Session).Order())
            .ToArray();
        var findings = new List<string>();
        var memberFindings = new List<string>();
        var expected = new List<string>();
        var rebuilt = 0;
        var setups = 0;
        var nightRows = 0;
        var memberSessions = 0;
        var own = FamilyOwnReadings.Select(LedgerReadings.IndexOf).ToHashSet();
        var standingSampled = sample.Where(session => session > LongLedger.Through && !nights.Contains(session)).ToArray();
        var standing = await StandingMembersAsync(
            index,
            through,
            overLedgerMembers ? standingSampled.SelectMany(session => inputs.Sessions.Where(day => day <= session).TakeLast(MemberWindow)).Distinct() : standingSampled,
            cancellation);
        var overMembers = new SortedDictionary<string, (int Compared, int Table, int Over, int OverTraded)>(StringComparer.Ordinal);
        var overExamples = new List<string>();
        var unrowed = new List<(string Finding, string Cause)>();

        foreach (var session in sample)
        {
            var rows = new List<(string Ticker, double?[] Readings, string Source)>();

            await using (var command = Command(connection, null, SessionRowsOn, [("$index", index), ("$session", Stamp(session))]))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    rows.Add((reader.GetString(0), ReadingsOf(reader.GetString(1)), reader.GetString(2)));
                }
            }

            if (rows.Count == 0)
            {
                findings.Add($"{Stamp(session)}: the table holds no row of the session");

                continue;
            }

            var byTicker = rows.ToDictionary(row => row.Ticker, row => row.Readings, StringComparer.Ordinal);

            if (nights.Contains(session))
            {
                // A session the night stored is the night's own rows, held to them.
                await using var command = Command(connection, null, "SELECT ticker, readings FROM ledger_reading WHERE index_code = $index AND session_date = $session;", [("$index", index), ("$session", Stamp(session))]);
                await using var reader = await command.ExecuteReaderAsync(cancellation);
                var nightsTickers = new HashSet<string>(StringComparer.Ordinal);

                while (await reader.ReadAsync(cancellation))
                {
                    nightRows++;
                    nightsTickers.Add(reader.GetString(0));

                    if (!byTicker.TryGetValue(reader.GetString(0), out var held))
                    {
                        findings.Add($"{reader.GetString(0)} {Stamp(session)}: the night's own row is not in the table");
                    }
                    else
                    {
                        findings.AddRange(LedgerPointInTime.Differences(ReadingsOf(reader.GetString(1)), held).Select(differ =>
                            FormattableString.Invariant($"{reader.GetString(0)} {Stamp(session)}: {differ.Reading} the night's {Shown(differ.Stored)}, the table's {Shown(differ.Rebuilt)}")));
                    }
                }

                findings.AddRange(byTicker.Keys.Where(ticker => !nightsTickers.Contains(ticker)).Order(StringComparer.Ordinal).Select(ticker => $"{ticker} {Stamp(session)}: the table holds it and the night's own rows do not"));

                continue;
            }

            // The standing ledger's members on a session after the long years held to the table's, both ways.
            if (standing.TryGetValue(session, out var ledgerMembers))
            {
                memberSessions++;
                memberFindings.AddRange(MemberSetDifferences(ledgerMembers, byTicker.Keys, session));
            }

            // One row the build worked, rebuilt from the long history cut at its session.
            var picked = rows[random.Next(rows.Count)];
            var cut = LedgerPointInTime.Cut(inputs, session);
            var readingsThen = LedgerPointInTime.Rebuilt(index, cut, LedgerPointInTime.Cut(market, session), income, sectors, facts, picked.Ticker, insiders, releases, largeBreadth, gics, actions, trends, shortInterest, oneOffs, table: true, reportFilings: reportFilings, settlements: settlements);

            if (readingsThen is null)
            {
                findings.Add($"{picked.Ticker} {Stamp(session)}: the cut history holds no bar of it on its session");
            }
            else
            {
                rebuilt++;

                foreach (var differ in LedgerPointInTime.Differences(picked.Readings, readingsThen))
                {
                    (TableOwnReadings.UntilThePinMove.Contains(differ.Reading, StringComparer.Ordinal) ? expected : findings).Add(
                        FormattableString.Invariant($"{picked.Ticker} {Stamp(session)}: {differ.Reading} stored {Shown(differ.Stored)}, rebuilt at its session {Shown(differ.Rebuilt)}"));
                }
            }

            // Every setup the ledger holds of the session, held to its stock's row on every reading but its family's own.
            var heldSetups = new List<(string Family, string Ticker, double?[] Stored, double?[] Held)>();

            await using (var command = Command(connection, null, session <= LongLedger.Through ? LongSetupsOn : StandingSetupsOn, [("$index", index), ("$session", Stamp(session))]))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    setups++;

                    var family = reader.GetString(0);
                    var ticker = reader.GetString(1);
                    var stored = new double?[LedgerReadings.Count];

                    for (var at = 0; at < stored.Length; at++)
                    {
                        stored[at] = reader.IsDBNull(2 + at) ? null : reader.GetDouble(2 + at);
                    }

                    if (!byTicker.TryGetValue(ticker, out var held))
                    {
                        var finding = $"{family} {ticker} {Stamp(session)}: the ledger's setup has no row of its stock in the table";

                        findings.Add(finding);

                        if (overLedgerMembers)
                        {
                            unrowed.Add((finding, UnrowedCause(cut, ticker, session)));
                        }

                        continue;
                    }

                    heldSetups.Add((family, ticker, stored, held));

                    foreach (var differ in SetupDifferences(stored, held, own))
                    {
                        (TableOwnReadings.Expected(differ.Reading) ? expected : findings).Add(
                            FormattableString.Invariant($"{family} {ticker} {Stamp(session)}: {differ.Reading} the ledger's {Shown(differ.Stored)}, the table's {Shown(differ.Rebuilt)}"));
                    }
                }
            }

            // From the operator's order of 2026-11-24, items 2 and 4: on a session the standing ledger's members are read for,
            // the rows rebuilt over those members as its own build reads them, each across-members reading and the third and
            // fourth alphas held to the ledger's beside the table's.
            if (overLedgerMembers && standing.ContainsKey(session) && heldSetups.Count > 0)
            {
                var over = LedgerPointInTime.RebuiltMany(index, LedgerPointInTime.OverMembers(cut, standing, MemberWindow), LedgerPointInTime.Cut(market, session), income, sectors, facts, heldSetups.Select(setup => setup.Ticker), insiders, releases, largeBreadth, gics, actions, trends, shortInterest, oneOffs, settlements);

                foreach (var (family, ticker, stored, held) in heldSetups)
                {
                    if (!over.TryGetValue(ticker, out var rebuiltOver))
                    {
                        continue;
                    }

                    foreach (var column in AcrossMembers)
                    {
                        var at = LedgerReadings.IndexOf(column);
                        var traded = column switch { "alpha_three" => rebuiltOver.Alpha3Traded, "alpha_four" => rebuiltOver.Alpha4Traded, _ => rebuiltOver.Readings[at] };
                        var was = overMembers.GetValueOrDefault(column);
                        var overDiffers = !SameReading(stored[at], rebuiltOver.Readings[at]);

                        overMembers[column] = (was.Compared + 1, was.Table + (SameReading(stored[at], held[at]) ? 0 : 1), was.Over + (overDiffers ? 1 : 0), was.OverTraded + (SameReading(stored[at], traded) ? 0 : 1));

                        if (overDiffers && overExamples.Count(example => example.StartsWith(column + " ", StringComparison.Ordinal)) < 3)
                        {
                            overExamples.Add(FormattableString.Invariant($"{column} {family} {ticker} {Stamp(session)}: the ledger's {Shown(stored[at])}, the table's {Shown(held[at])}, over the ledger's members {Shown(rebuiltOver.Readings[at])}"));
                        }
                    }
                }
            }
        }

        foreach (var (column, counts) in overMembers)
        {
            output.WriteLine(FormattableString.Invariant($"session-readings-members: {column}: {counts.Compared} reading(s) on the standing ledger's sampled sessions differ from the ledger's: the table's {counts.Table}, rebuilt over the ledger's members at its prices {counts.Over}, over its members at the traded price {counts.OverTraded}"));
        }

        foreach (var example in overExamples)
        {
            output.WriteLine("session-readings-members: still differs, " + example);
        }

        foreach (var group in unrowed.GroupBy(one => one.Cause).OrderByDescending(group => group.Count()))
        {
            output.WriteLine(FormattableString.Invariant($"session-readings-unrowed: {group.Count()} setup(s): {group.Key}; e.g. {group.First().Finding}"));
        }

        // From the operator's order of 2026-11-25, item 5: the differences still to trace first, then the member lists held both
        // ways, then the seven readings the table works its own way apart and marked expected; any difference in any part still
        // fails the check.
        output.WriteLine(FormattableString.Invariant($"session-readings-check: to trace, {findings.Count} difference(s)"));

        foreach (var finding in findings)
        {
            output.WriteLine("session-readings-check: " + finding);
        }

        output.WriteLine(FormattableString.Invariant($"session-readings-member-lists: {memberFindings.Count} difference(s) between the standing ledger's members and the table's"));

        foreach (var finding in memberFindings)
        {
            output.WriteLine("session-readings-check: " + finding);
        }

        output.WriteLine(FormattableString.Invariant($"session-readings-expected: expected, by design, {expected.Count} difference(s) on the seven readings the table works its own way and the short interest a night reads with no cap until the pin move: ")
            + string.Join(", ", TableOwnReadings.Columns.Concat(TableOwnReadings.UntilThePinMove).Select(column => FormattableString.Invariant($"{column} {expected.Count(finding => finding.Contains($": {column} ", StringComparison.Ordinal))}"))));

        foreach (var finding in expected)
        {
            output.WriteLine("session-readings-expected: " + finding);
        }

        var differences = findings.Count + memberFindings.Count + expected.Count;
        var detail = FormattableString.Invariant($"{sample.Length} sampled session(s) on the {DecisionCards.NameOf(index)}, {perYear} a year by seed {seed}: {rebuilt} row(s) rebuilt from the long history cut at their session, {setups} of the ledger's setup(s) held to their stock's row, {nightRows} of the nights' own row(s) held to the table, {memberSessions} session(s) of the standing ledger's members held to the table's both ways; ")
            + (differences == 0
                ? "every reading the same"
                : FormattableString.Invariant($"{differences} difference(s), each named on the command's output: {findings.Count} to trace, {memberFindings.Count} between the member lists and {expected.Count} expected by design on the seven readings the table works its own way and the short interest until the pin move"));

        await AppendAsync(connection, runId, SessionCheckStage, startedAt, 0, differences == 0 ? Ok : NotComputed, detail, cancellation);
        output.WriteLine("session-readings-check: " + detail);

        return differences == 0 ? 0 : 1;
    }

    // The sessions to a sampled one whose members a rebuild over the ledger's members reads as the ledger's: the longest window
    // a ranked reading places a member over, Alpha#3's ten, with room.
    const int MemberWindow = 12;

    // The readings ranked or counted across a session's members, and the third and fourth alphas, which a rebuild over the
    // ledger's members holds to the ledger's.
    static readonly string[] AcrossMembers =
    [
        "breadth", "strength", "strength_twelve_less_one", "alpha_one", "alpha_two", "highs_less_lows", "drop_against_sector",
        "sector_month", "sector_quarter", "large_breadth", "alpha_three", "alpha_four",
    ];

    // Two readings the same: both none, or two figures a millionth of the larger apart or less.
    static bool SameReading(double? one, double? other) =>
        (one, other) switch
        {
            (null, null) => true,
            ({ } a, { } b) => Math.Abs(a - b) <= LedgerPointInTime.Tolerance * Math.Max(Math.Abs(a), Math.Abs(b)),
            _ => false,
        };

    // Why the table holds no row of a ledger setup's stock on its session, as the long history the table reads holds it.
    static string UnrowedCause(SweepHistoryInputs cut, string ticker, DateOnly session)
    {
        var named = cut.Names.Where(name => string.Equals(name.Ticker, ticker, StringComparison.Ordinal)).ToArray();

        return named.Length == 0 ? "the long history holds no code of that ticker"
            : !named.Any(name => name.MemberOn(session)) ? "the long history holds the code and no span of the index on the session"
            : !named.Any(name => name.Bars.Any(bar => bar.Session == session)) ? "a member on the session with no bar there"
            : "a member with a bar on the session, read as a gap";
    }

    // A session's members the standing ledger reads and the table holds, each held to the other: a member of either the other
    // lacks named, the ledger's first.
    public static IEnumerable<string> MemberSetDifferences(IReadOnlySet<string> ledgerMembers, IEnumerable<string> tableMembers, DateOnly session)
    {
        var table = tableMembers.ToHashSet(StringComparer.Ordinal);

        return ledgerMembers.Where(ticker => !table.Contains(ticker)).Order(StringComparer.Ordinal).Select(ticker => $"{ticker} {Stamp(session)}: the ledger's members hold it and the table does not")
            .Concat(table.Where(ticker => !ledgerMembers.Contains(ticker)).Order(StringComparer.Ordinal).Select(ticker => $"{ticker} {Stamp(session)}: the table holds it and the ledger's members do not"));
    }

    // The members the standing ledger's build reads on each session given, its history read as the build reads it, by ticker.
    async Task<Dictionary<DateOnly, HashSet<string>>> StandingMembersAsync(string index, DateOnly through, IEnumerable<DateOnly> sessions, CancellationToken cancellation)
    {
        var wanted = sessions.ToArray();

        if (wanted.Length == 0)
        {
            return [];
        }

        var inputs = await new SweepHistory(databaseFile).ReadAsync(through, null, cancellation, index: index == IndexFamilies.LargeIndex ? null : index, asItStood: index != IndexFamilies.LargeIndex);
        var sessionAt = inputs.Sessions.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var members = SweepBenchmark.On(series, sessionAt.Count);

        return wanted.Where(sessionAt.ContainsKey).ToDictionary(session => session, session => members.Names[sessionAt[session]].Select(name => series[name].Name.Ticker).ToHashSet(StringComparer.Ordinal));
    }

    // The readings a setup's row and its stock's row differ on, a setup's family's own readings and a reading the long build
    // holds none of by design passed over.
    public static IReadOnlyList<(string Reading, double? Stored, double? Rebuilt)> SetupDifferences(IReadOnlyList<double?> setup, IReadOnlyList<double?> member, IReadOnlySet<int> own)
    {
        var kept = new double?[setup.Count];
        var held = new double?[member.Count];

        for (var at = 0; at < setup.Count; at++)
        {
            var passed = own.Contains(at) || (LedgerPointInTime.HeldNoneByTheLongBuild.Contains(LedgerReadings.All[at].Column, StringComparer.Ordinal) && (setup[at] is null || member[at] is null));

            kept[at] = passed ? null : setup[at];
            held[at] = passed ? null : at < member.Count ? member[at] : null;
        }

        return LedgerPointInTime.Differences(kept, held);
    }

    sealed partial class PreparedHistory
    {
        // Every member's readings on one session, the members as they stood with a bar and no gap, each as the night works
        // them over its own bars, a ticker held twice read once, in the members' order.
        public IReadOnlyList<(string Ticker, string Readings)> MemberReadings(int at)
        {
            var rows = new List<(string Ticker, string Readings)>(members.Names[at].Length);
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var name in members.Names[at])
            {
                var one = series[name];

                if (!seen.Add(one.Name.Ticker))
                {
                    continue;
                }

                var bar = IndexNightRead.BarOf(one, at);
                var readings = LedgerSetups.Readings(index, name, one, ownCloses.Value[name], bar, sessions[at], members, calendar, at, market, context, series);

                CapShortInterest(readings, context.ShortInterest, Settlements, one.Name.Ticker, calendar[at]);
                TableOwnReadings.Apply(readings, tradedRanks.Value, name, one, bar, ReportFilings is { } filings ? ReportDaysOf(filings, one) : null);
                rows.Add((one.Name.Ticker, ReadingsText(readings)));
            }

            return rows;
        }

        // Each code's results 8-Ks and quarterly and annual reports, which the table's weekdays to the next report read.
        public IReadOnlyDictionary<string, ReportFiled>? ReportFilings { get; set; }

        // The members' places at the traded price, which the table's third and fourth alphas read.
        readonly Lazy<TradedRanks> tradedRanks = new(() => new TradedRanks(series, members));
    }

    // A code's results 8-Ks, those carrying item 2.02, each by the day it reports and the day it was filed, and its 10-Qs and
    // 10-Ks by the day each was filed.
    public sealed record ReportFiled(List<(DateOnly Reported, DateOnly Filed)> Results, List<DateOnly> Periodic);

    const string ReportFilingsRead = "SELECT ticker, filed, form, items, reported FROM long_filing WHERE form IN ('8-K', '10-Q', '10-K');";

    // Every code's report filings as the long filings hold them.
    // see: The every-session table counts the weekdays to the next report from the results 8-K each quarter's 10-Q or 10-K closes
    static async Task<IReadOnlyDictionary<string, ReportFiled>> ReportFilingsAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var filings = new Dictionary<string, ReportFiled>(StringComparer.Ordinal);

        await using var command = Command(connection, null, ReportFilingsRead, []);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var form = reader.GetString(2);
            var results = form == "8-K";

            if (results && (reader.IsDBNull(3) || !reader.GetString(3).Split(',').Contains("2.02", StringComparer.Ordinal)))
            {
                continue;
            }

            if (!filings.TryGetValue(reader.GetString(0), out var held))
            {
                filings[reader.GetString(0)] = held = new ReportFiled([], []);
            }

            var filedOn = DateOnly.ParseExact(reader.GetString(1)[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

            // From the operator's ruling of 2026-11-25 a results 8-K counts on the day it reports, its filing's day where it
            // names none or one after it.
            if (results)
            {
                var reportedOn = !reader.IsDBNull(4) && reader.GetString(4) is { Length: >= 10 } reported
                    && DateOnly.TryParseExact(reported[..10], "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var day) && day <= filedOn
                    ? day
                    : filedOn;

                held.Results.Add((reportedOn, filedOn));
            }
            else
            {
                held.Periodic.Add(filedOn);
            }
        }

        return filings;
    }

    // A code's report days: off its filings where they hold any, and the earnings calendars' days around them.
    internal static IReadOnlyList<DateOnly> ReportDaysOf(IReadOnlyDictionary<string, ReportFiled> filings, SweepSeries one) =>
        filings.TryGetValue(one.Name.Ticker, out var filed)
            ? TableReadings.ReportDays(filed.Results, filed.Periodic, one.Name.Prints)
            : one.Name.Prints;
}
