using System.Diagnostics;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// From 23.6 the long ledger: the history build over the long history's reader, writing `long_setup` apart from the standing
// ledger, so no card, hook or night reads it. Each setup carries two readings, the benchmark and the trade as ruled, and each
// member and trade whose stock's prices stop ended at its last price, the second the same walks over the same history with
// each stopped series held at its last price. A session once written is frozen: a build writes only the sessions of the span
// it holds no setup of, and moves no row an earlier build wrote. At its end it writes the standing ledger's setups whose
// stock's prices stopped while they were open at their last price into `departed_setup`, and each family's years on the
// index under both readings into `long_reading`, the long years and the standing ones, which the History page draws.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public sealed partial class SetupLedger
{
    public const string LongBuildStage = "ledger-build-long";

    public const string LongCheckStage = "ledger-check-long";

    // The long ledger's reads: the market series from the long pull, each long code's filer off its filings, read before the
    // pull's and the night's so theirs replace it, and a filer's facts in the standing table and the long one together,
    // whose periods meet and do not overlap.
    const string LongMarketBars = "SELECT series, session_date, close FROM long_market_bar WHERE series = $series AND session_date <= $night ORDER BY session_date;";

    const string LongFilers = "SELECT ticker, MAX(cik) FROM long_filing GROUP BY ticker;";

    const string LongFactsOfFiler = @"
        SELECT concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE cik = $cik
        UNION ALL
        SELECT concept, period_start, period_end, dollars, filed, form, accession FROM long_fact WHERE cik = $cik;";

    const string LongSessions = "SELECT DISTINCT session_date FROM long_setup WHERE index_code = $index;";

    const string LongReadThrough = "SELECT MAX(read_through) FROM long_reading WHERE index_code = $index;";

    static readonly string LongReadings =
        "SELECT family, ticker, session_date, " + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM long_setup WHERE index_code = $index ORDER BY session_date, family, ticker;";

    const string LongTrades = @"
        SELECT family, ticker, session_date, entry, stop, fill, company_value,
               result, benchmark, edge, end, ended_on, settled,
               last_result, last_benchmark, last_edge, last_end, last_ended_on, last_settled
        FROM long_setup WHERE index_code = $index;";

    const string StandingTrades = @"
        SELECT family, ticker, session_date, entry, stop, fill, company_value, result, benchmark, edge, end, ended_on, settled, source
        FROM setup WHERE index_code = $index;";

    const string StandingDeparted = "SELECT family, ticker, session_date, last_result, benchmark, last_edge, last_session FROM departed_setup WHERE index_code = $index;";

    const string UnexplainedJumps = "SELECT ticker, session_date FROM data_check WHERE class = 'jump' AND kind = 'unexplained';";

    const string LongSpansOf = "SELECT ticker, joined, \"left\" FROM long_span WHERE index_code = $index;";

    // The long ledger by hand, from a clean copy of main's commit: every session of 2005 to 2018 on one index the long
    // ledger holds no setup of, over the long history read through the newest session it holds, each setup replayed to its
    // end under both readings, written a chunk of sessions at a time under the night's wait and the drain's lock as the
    // standing build writes; then the standing setups whose prices stopped and each family's years.
    public async Task<int> LongBuildAsync(string dataRoot, string index, TextWriter output, Func<TimeSpan, CancellationToken, Task>? wait = null, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var pause = wait ?? Task.Delay;

        await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var longHistory = new LongHistoryReader(databaseFile);
        var through = await longHistory.NewestSessionAsync(cancellation);
        var live = await LiveRulesAsync(index, output, cancellation);
        var prepared = await PrepareAsync(connection, index, through, output, cancellation, live: live, longHistory: longHistory);
        var first = Array.FindIndex(prepared.Calendar, session => session >= LongLedger.From);
        var last = Array.FindLastIndex(prepared.Calendar, session => session <= LongLedger.Through);

        if (first < 0 || last < first)
        {
            output.WriteLine("ledger-build: the long history holds no session in 2005 to 2018");

            return 1;
        }

        var stored = await LongSessionsAsync(connection, index, cancellation);
        var sessionsToWrite = Enumerable.Range(first, last - first + 1).Where(at => !stored.Contains(prepared.Calendar[at])).ToArray();
        var runId = FormattableString.Invariant($"{LongBuildStage}-{index}-{startedAt:yyyyMMddTHHmmssZ}");
        var atLastPrice = prepared.HeldAtLastPrice();
        var written = 0;
        var passes = 0;

        if (sessionsToWrite.Length > 0)
        {
            output.WriteLine(FormattableString.Invariant($"ledger-build: the store holds {new FileInfo(databaseFile).Length:N0} bytes of the {LongLedger.CeilingBytes:N0} its size before the long history and the long tables' budget come to"));
        }

        output.WriteLine(sessionsToWrite.Length == 0
            ? FormattableString.Invariant($"ledger-build: every session of {Stamp(prepared.Calendar[first])} to {Stamp(prepared.Calendar[last])} holding a setup is in the long ledger already, which stays as written until the restart")
            : FormattableString.Invariant($"ledger-build: {sessionsToWrite.Length} session(s) of the long ledger to read of the {last - first + 1} in 2005 to 2018, under both readings"));

        for (var taken = 0; taken < sessionsToWrite.Length; taken += ChunkSessions)
        {
            var chunkSessions = sessionsToWrite.Skip(taken).Take(ChunkSessions).ToArray();

            await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

            var watch = Stopwatch.StartNew();

            if (new FileInfo(databaseFile).Length is var storeBytes && storeBytes > LongLedger.CeilingBytes)
            {
                return await LongStopAsync(connection, runId, startedAt, written, FormattableString.Invariant($"the store holds {storeBytes:N0} bytes, past the {LongLedger.CeilingBytes:N0} its size before the long history and the long tables' budget come to, before {Stamp(prepared.Calendar[chunkSessions[0]])}"), output, cancellation);
            }

            (int At, BuiltSession AsRuled, BuiltSession Last)[] built;

            try
            {
                built = [.. chunkSessions.Select(at => (at, prepared.Compute(at), atLastPrice.Compute(at)))];
            }
            catch (Exception failure) when (!cancellation.IsCancellationRequested)
            {
                return await LongStopAsync(connection, runId, startedAt, written, FormattableString.Invariant($"the sessions from {Stamp(prepared.Calendar[chunkSessions[0]])} could not be read: {IndexFamilies.Cause(failure)}"), output, cancellation);
            }

            using var held = await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            var chunk = 0;

            foreach (var (at, asRuled, lastPrice) in built)
            {
                for (var place = 0; place < asRuled.Families.Count; place++)
                {
                    var (family, reads) = asRuled.Families[place];
                    var lastReads = lastPrice.Families[place].Reads;

                    if (lastPrice.Families[place].Family.Family != family.Family || lastReads.Count != reads.Count)
                    {
                        throw new InvalidOperationException(FormattableString.Invariant($"the two readings of {family.Family} on {Stamp(prepared.Calendar[at])} hold different setups"));
                    }

                    for (var row = 0; row < family.Rows.Count; row++)
                    {
                        await InsertAsync(connection, transaction, family.Rows[row], null, null, reads[row], prepared.Replayed, at, HistorySource, cancellation, InsertLongSetup,
                        [
                            .. LastPriceParameters(SettleParameters(family.Rows[row].Index, family.Family, family.Rows[row].Ticker, family.Rows[row].Session, null, lastReads[row].Outcome, lastReads[row].Benchmark, prepared.Replayed, at)),
                            ("$run_id", runId),
                        ]);
                    }

                    chunk += family.Rows.Count;
                    passes += family.LivePasses;
                }
            }

            await transaction.CommitAsync(cancellation);

            written += chunk;
            output.WriteLine(FormattableString.Invariant($"ledger-build: {Stamp(prepared.Calendar[chunkSessions[0]])} to {Stamp(prepared.Calendar[chunkSessions[^1]])}, {chunkSessions.Length} session(s), {chunk} setup(s) in {watch.Elapsed.TotalSeconds:0} s, {written} so far"));
        }

        int departed;
        int years;

        using (await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation))
        {
            departed = await WriteDepartedAsync(connection, prepared, runId, cancellation);
            years = await WriteLongYearsAsync(connection, index, through, runId, cancellation);
        }

        await AppendAsync(connection, runId, LongBuildStage, startedAt, written, Ok,
            FormattableString.Invariant($"{written} setup(s) of the long ledger on the {DecisionCards.NameOf(index)} over {Stamp(prepared.Calendar[first])} to {Stamp(prepared.Calendar[last])}, each under both readings, {passes} passing the live rule")
                + (prepared.LiveWords is { } words ? ", " + words : string.Empty)
                + FormattableString.Invariant($", {sessionsToWrite.Length} session(s) read of the {last - first + 1}, the long history read through {Stamp(through)}; {departed} standing setup(s) whose prices stopped while open at their last price; {years} family year(s) under the two readings"), cancellation);

        output.WriteLine(FormattableString.Invariant($"ledger-build: {written} setup(s) of the long ledger written for the {DecisionCards.NameOf(index)}, {passes} passing the live rule; {departed} standing setup(s) at their last price; {years} family year(s)"));

        return 0;
    }

    // A build that stops: its row on the run log naming the session it stopped before and why, the chunks it wrote standing
    // and the next build going on from where it stopped.
    async Task<int> LongStopAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int written, string why, TextWriter output, CancellationToken cancellation)
    {
        var detail = FormattableString.Invariant($"{written} setup(s) of the long ledger written, then stopped: {why}; the sessions written stand and the next build goes on from them");

        await AppendAsync(connection, runId, LongBuildStage, startedAt, written, NotComputed, detail, cancellation);
        output.WriteLine("ledger-build: " + detail);

        return 1;
    }

    // The result half of a setup at its last price, under the long table's names for it.
    static IEnumerable<(string Name, object Value)> LastPriceParameters(IEnumerable<(string Name, object Value)> settled) =>
        settled
            .Where(parameter => parameter.Name is "$result" or "$benchmark" or "$edge" or "$edge_after_cost" or "$sessions" or "$end" or "$ended_on" or "$settled")
            .Select(parameter => ("$last_" + parameter.Name[1..], parameter.Value));

    static async Task<IReadOnlySet<DateOnly>> LongSessionsAsync(SqliteConnection connection, string index, CancellationToken cancellation)
    {
        var stored = new HashSet<DateOnly>();

        await using var command = Command(connection, null, LongSessions, [("$index", index)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            stored.Add(Date(reader.GetString(0)));
        }

        return stored;
    }

    // The standing ledger's setups on the index left open with no result whose stock's prices stop after it was bought,
    // each at its last price, written whole for the index; the benchmark as the standing ledger stored it.
    async Task<int> WriteDepartedAsync(SqliteConnection connection, PreparedHistory prepared, string runId, CancellationToken cancellation)
    {
        var rows = new List<IReadOnlyList<(string Name, object Value)>>();

        await using (var command = Command(connection, null, StandingTrades, [("$index", prepared.Index)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                if (!reader.IsDBNull(7) || reader.GetInt64(12) != 0 || !prepared.Places.TryGetValue(reader.GetString(1), out var name) || !prepared.Stopped[name])
                {
                    continue;
                }

                var bars = prepared.Series[name].Name.Bars;
                var session = Date(reader.GetString(2));

                if (bars.Length == 0 || bars[^1].Session <= session)
                {
                    continue;
                }

                var family = reader.GetString(0);
                var entry = Statistic.FromPrice(Money.FromStorage(reader.GetString(3)));
                var stop = Statistic.FromPrice(Money.FromStorage(reader.GetString(4)));
                var bought = reader.IsDBNull(5) ? entry : Statistic.FromPrice(Money.FromStorage(reader.GetString(5)));
                var lastClose = Statistic.FromPrice(bars[^1].Close);
                var result = LongLedger.AtLastPrice(LikeSetups.IsBook(family), bought, entry, stop, lastClose);
                double? benchmark = reader.IsDBNull(8) ? null : reader.GetDouble(8);

                rows.Add(
                [
                    ("$index", prepared.Index), ("$family", family), ("$ticker", reader.GetString(1)), ("$session", reader.GetString(2)), ("$source", reader.GetString(13)),
                    ("$bought_at", Money.ToStorage(Statistic.ToPrice(bought))), ("$last_session", Stamp(bars[^1].Session)), ("$last_close", Money.ToStorage(bars[^1].Close)),
                    ("$last_result", result is { } made ? made : DBNull.Value), ("$benchmark", benchmark is { } mean ? mean : DBNull.Value),
                    ("$last_edge", result is { } a && benchmark is { } b ? a - b : DBNull.Value), ("$run_id", runId),
                ]);
            }
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearDeparted, [("$index", prepared.Index)], cancellation);

        foreach (var row in rows)
        {
            await ExecuteAsync(connection, transaction, InsertDeparted, row, cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return rows.Count;
    }

    // Each family's years on the index under the two readings, written whole for the index: the long years from the long
    // ledger, and the standing years from the standing ledger as ruled and with its departed setups at their last price.
    async Task<int> WriteLongYearsAsync(SqliteConnection connection, string index, DateOnly through, string runId, CancellationToken cancellation)
    {
        var jumps = new Dictionary<string, List<DateOnly>>(StringComparer.Ordinal);

        await using (var command = Command(connection, null, UnexplainedJumps, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                if (!jumps.TryGetValue(reader.GetString(0), out var held))
                {
                    jumps[reader.GetString(0)] = held = [];
                }

                held.Add(Date(reader.GetString(1)));
            }
        }

        // The jumps standing inside a member's span of the index: a trade's benchmark holds every member of its session, so a
        // hold crossing any of them is set apart with the trade's own.
        var spans = new Dictionary<string, List<(DateOnly Joined, DateOnly? Left)>>(StringComparer.Ordinal);

        await using (var command = Command(connection, null, LongSpansOf, [("$index", index)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                if (!spans.TryGetValue(reader.GetString(0), out var held))
                {
                    spans[reader.GetString(0)] = held = [];
                }

                held.Add((Date(reader.GetString(1)), reader.IsDBNull(2) ? null : Date(reader.GetString(2))));
            }
        }

        DateOnly[] memberJumps =
        [
            .. jumps
                .Where(pair => spans.ContainsKey(pair.Key))
                .SelectMany(pair => pair.Value.Where(day => LongLedger.MemberOn(spans[pair.Key], day)))
                .Distinct()
                .Order(),
        ];

        var asRuled = new List<LongTrade>();
        var lastPrice = new List<LongTrade>();

        LongTrade Trade(string family, string ticker, DateOnly session, double entry, double stop, double? companyValue, double? result, double? edge, string end, DateOnly? endedOn, bool settled)
        {
            var afterCost = LikeSetups.AfterCost(LikeSetups.IsBook(family) ? Core.Loop.ChanceForm.Book : Core.Loop.ChanceForm.Trailing, result, null, entry, stop, companyValue);
            double? cost = result is { } made && afterCost is { } after ? (LikeSetups.IsBook(family) ? ((made * 100) - after) / 100 : made - after) : null;

            return new LongTrade(
                family,
                session.Year,
                settled,
                end == SetupEnds.Target,
                edge,
                edge is { } held && cost is { } paid ? held - paid : null,
                LongLedger.Crosses(jumps.GetValueOrDefault(ticker) ?? [], session, endedOn) || LongLedger.Crosses(memberJumps, session, endedOn));
        }

        static double? Real(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : reader.GetDouble(at);

        static DateOnly? Day(SqliteDataReader reader, int at) => reader.IsDBNull(at) ? null : Date(reader.GetString(at));

        await using (var command = Command(connection, null, LongTrades, [("$index", index)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                var family = reader.GetString(0);
                var ticker = reader.GetString(1);
                var session = Date(reader.GetString(2));
                var entry = Statistic.FromPrice(Money.FromStorage(reader.GetString(3)));
                var stop = Statistic.FromPrice(Money.FromStorage(reader.GetString(4)));

                asRuled.Add(Trade(family, ticker, session, entry, stop, Real(reader, 6), Real(reader, 7), Real(reader, 9), reader.GetString(10), Day(reader, 11), reader.GetInt64(12) == 1));
                lastPrice.Add(Trade(family, ticker, session, entry, stop, Real(reader, 6), Real(reader, 13), Real(reader, 15), reader.GetString(16), Day(reader, 17), reader.GetInt64(18) == 1));
            }
        }

        var departed = new Dictionary<(string, string, DateOnly), (double? Result, double? Edge, DateOnly Last)>();

        await using (var command = Command(connection, null, StandingDeparted, [("$index", index)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                departed[(reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)))] = (Real(reader, 3), Real(reader, 5), Date(reader.GetString(6)));
            }
        }

        await using (var command = Command(connection, null, StandingTrades, [("$index", index)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                var family = reader.GetString(0);
                var ticker = reader.GetString(1);
                var session = Date(reader.GetString(2));

                if (session < LongLedger.StandingFrom)
                {
                    continue;
                }

                var entry = Statistic.FromPrice(Money.FromStorage(reader.GetString(3)));
                var stop = Statistic.FromPrice(Money.FromStorage(reader.GetString(4)));
                var trade = Trade(family, ticker, session, entry, stop, Real(reader, 6), Real(reader, 7), Real(reader, 9), reader.GetString(10), Day(reader, 11), reader.GetInt64(12) == 1);

                asRuled.Add(trade);
                lastPrice.Add(departed.TryGetValue((family, ticker, session), out var gone) && gone.Edge is not null
                    ? Trade(family, ticker, session, entry, stop, Real(reader, 6), gone.Result, gone.Edge, string.Empty, gone.Last, true)
                    : trade);
            }
        }

        IReadOnlyList<LongYear> years = [.. LongLedger.Years(asRuled, LongLedger.AsRuled), .. LongLedger.Years(lastPrice, LongLedger.LastPrice)];

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearLongReading, [("$index", index)], cancellation);

        foreach (var year in years)
        {
            await ExecuteAsync(connection, transaction, InsertLongReading,
            [
                ("$index", index), ("$family", year.Family), ("$year", year.Year), ("$reading", year.Reading), ("$setups", year.Setups),
                ("$settled", year.Settled), ("$targets", year.Targets),
                ("$edge_mean", year.EdgeMean is { } edge ? edge : DBNull.Value),
                ("$edge_after_cost_mean", year.EdgeAfterCostMean is { } after ? after : DBNull.Value),
                ("$crossing", year.Crossing),
                ("$edge_after_cost_without", year.EdgeAfterCostWithout is { } without ? without : DBNull.Value),
                ("$read_through", Stamp(through)), ("$run_id", runId),
            ], cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return years.Count;
    }

    // The long ledger's point-in-time check by hand: a seeded sample of each year's long setups on the index, each one's
    // readings rebuilt from the long history read through the build's own end and cut at its own session, and held to the
    // readings stored. It reads the store and writes its run log row alone.
    public async Task<int> LongCheckAsync(string index, TextWriter output, int perYear = CheckPerYear, int seed = CheckSeed, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var through = await ScalarAsync(connection, LongReadThrough, [("$index", index)], cancellation) as string;

        return await CheckAsync(index, output, perYear, seed, cancellation, new LongHistoryReader(databaseFile), through is null ? null : Date(through));
    }
}
