using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// One index's part of the ledger's night: the setups appended, those the live rule's own setting passes, the windows
// closed, the time it took, or the failure that left the index unwritten.
public sealed record LedgerIndex(string Index, int Setups, int LivePasses, int Closed, double Seconds, string? Fault = null);

public sealed record LedgerOutcome(DateOnly? Session, IReadOnlyList<LedgerIndex> Indices)
{
    public int Setups => Indices.Sum(index => index.Setups);

    public int Closed => Indices.Sum(index => index.Closed);
}

// The setup ledger. After the families have drawn each index's list, the night appends every member-session a family's
// loose gates pass on each index, with the live rule's own pass beside it, its plan as prices, its readings as they
// stood and its path still open, and closes the windows of the setups stored before whose paths ended on the night's
// close or whose benchmark, the same plan on every member of that session, has every member's path ended; the same
// component builds the history by hand, session by session over the pulled bars merged with the store's, each row
// replayed to its end where the history reaches it. A reading the inputs do not reach is stored as none. A failure in
// one index's part leaves that index's rows of the night unwritten and named on the stage's row, and the step goes on;
// the stage makes no request and calls no model. A night run again replaces its own rows.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
// see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
// see: The nightly run is arithmetic only
public sealed class SetupLedger : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.PulledEarnings, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
            new StoreTouch(Store.PulledMarketBar, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.PulledIncome, Touch.Read),
            new StoreTouch(Store.PulledMember, Touch.Read),
            new StoreTouch(Store.PulledSnapshot, Touch.Read),
            new StoreTouch(Store.PulledHolding, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.SetupNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "ledger";

    public const string BuildStage = "ledger-build";

    public const string Ok = DecisionCards.Ok;

    public const string NotComputed = DecisionCards.NotComputed;

    public const string NightSource = "night";

    public const string HistorySource = "history";

    // The sessions a history chunk is written in one transaction over, a quarter's.
    public const int ChunkSessions = 63;

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string MarketBars = "SELECT series, session_date, close FROM market_bar WHERE series = $series AND session_date <= $night ORDER BY session_date;";

    const string PulledMarketBars = "SELECT series, session_date, close FROM pulled_market_bar WHERE series = $series AND session_date <= $night ORDER BY session_date;";

    // The newest sector the quarters step filed for each ticker, and the one the companies pull filed where the night
    // filed none.
    const string Sectors = "SELECT ticker, sector FROM company WHERE sector IS NOT NULL ORDER BY fetched_at;";

    const string PulledSectors = "SELECT ticker, sector FROM pulled_company WHERE sector IS NOT NULL;";

    // Each ticker's filer: the newest the quarters step filed for it, and the companies pull's beneath it for the
    // history, read in that order so the night's own replaces the pull's.
    const string Filers = "SELECT ticker, cik FROM company WHERE cik IS NOT NULL ORDER BY fetched_at;";

    const string PulledFilers = "SELECT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;";

    const string FactsOfFiler = "SELECT concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE cik = $cik;";

    const string BusinessAgain = @"
        UPDATE setup
        SET revenue_growth = $revenue_growth, growth_change = $growth_change, gross_margin_change = $gross_margin_change,
            operating_margin_change = $operating_margin_change, cash_over_income = $cash_over_income
        WHERE ticker = $ticker AND session_date = $session AND source = $source;
    ";

    // The round trip a member's trade pays, in per cent of its close, as the member reader stored it for the night.
    const string Costs = "SELECT ticker, cost FROM member_reading WHERE index_code = $index AND session_date = $night AND cost IS NOT NULL;";

    const string LargePicks = "SELECT ticker, family FROM family_pick WHERE session_date = $night AND state = 'listed';";

    const string IndexPicks = "SELECT ticker, family FROM index_family_pick WHERE index_code = $index AND session_date = $night AND state = 'listed';";

    const string ClearTheNight = "DELETE FROM setup WHERE index_code = $index AND session_date = $night AND source = $source;";

    const string ClearTheNightsRows = "DELETE FROM setup_night WHERE index_code = $index AND session_date = $night AND source = $source;";

    const string StoredHistory = "SELECT DISTINCT session_date FROM setup_night WHERE index_code = $index AND source = $source;";

    const string OpenSetups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, risk_moves, cost, end
        FROM setup WHERE index_code = $index AND settled = 0 AND session_date < $night ORDER BY session_date, family, ticker;";

    const string Settle = @"
        UPDATE setup SET result = $result, benchmark = $benchmark, edge = $edge, edge_after_cost = $edge_after_cost, sessions = $sessions,
            end = $end, ended_on = $ended_on, settled = $settled
        WHERE index_code = $index AND family = $family AND ticker = $ticker AND session_date = $session;";

    const string InsertNight = @"
        INSERT INTO setup_night (index_code, family, session_date, members, setups, live_passes, source)
        VALUES ($index, $family, $session, $members, $setups, $live_passes, $source);";

    // No model call, no request and nothing spent, stated on the row as every stage states them, since the spend cap
    // reads every row's spend.
    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, $outcome, $rows_written, 0, 0, '0', $detail);";

    static readonly string InsertSetup =
        "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, risk_moves, "
        + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + ", result, benchmark, cost, edge, edge_after_cost, sessions, end, ended_on, settled, source, pin) VALUES ("
        + "$index, $family, $ticker, $session, $rule, $live_pass, $picked, $entry, $stop, $target, $trail, $cap, $risk_moves, "
        + string.Join(", ", LedgerReadings.All.Select((_, at) => FormattableString.Invariant($"$r{at}")))
        + ", $result, $benchmark, $cost, $edge, $edge_after_cost, $sessions, $end, $ended_on, $settled, $source, $pin);";

    readonly IClock clock;
    readonly string databaseFile;

    public SetupLedger(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The night's step: tonight's setups appended on each index and the windows closed, index by index.
    public async Task<LedgerOutcome> NightAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var written = new List<LedgerIndex>();

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
            {
                await AppendAsync(connection, runId, Stage, startedAt, 0, Ok, "no session: the store holds no bar", cancellation);

                return new LedgerOutcome(null, []);
            }

            var night = Date(newest);

            foreach (var index in DecisionCards.Indices)
            {
                var watch = Stopwatch.StartNew();

                try
                {
                    written.Add(await IndexNightAsync(connection, index, night, watch, cancellation));
                }
                catch (Exception failure) when (!cancellation.IsCancellationRequested)
                {
                    written.Add(new LedgerIndex(index, 0, 0, 0, watch.Elapsed.TotalSeconds, IndexFamilies.Cause(failure)));
                }
            }

            await AppendAsync(connection, runId, Stage, startedAt, written.Sum(index => index.Setups + index.Closed), written.Any(index => index.Fault is not null) ? NotComputed : Ok, Detail(written), cancellation);

            return new LedgerOutcome(night, written);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            LedgerIndex[] all =
            [
                .. written,
                .. DecisionCards.Indices.Where(index => written.All(one => one.Index != index)).Select(index => new LedgerIndex(index, 0, 0, 0, 0, IndexFamilies.Cause(failure))),
            ];

            await AppendAfterAFailureAsync(runId, startedAt, all.Sum(index => index.Setups + index.Closed), Detail(all) + "; the step stopped on " + IndexFamilies.Cause(failure), cancellation);

            return new LedgerOutcome(null, all);
        }
    }

    // The row states counts and no timing, so two nights over one fixture write the same row; the step's
    // time is the row's own started_at to ended_at, and an index's seconds stay on the outcome alone.
    public static string Detail(IReadOnlyList<LedgerIndex> indices) =>
        string.Join("; ", indices.Select(index => index.Fault is null
            ? FormattableString.Invariant($"{index.Setups} setup(s), {index.LivePasses} passing the live rule, {index.Closed} window(s) closed on the {DecisionCards.NameOf(index.Index)}")
            : $"the {DecisionCards.NameOf(index.Index)}'s setups not computed tonight: {index.Fault}"));

    async Task<LedgerIndex> IndexNightAsync(SqliteConnection connection, string index, DateOnly night, Stopwatch watch, CancellationToken cancellation)
    {
        // Every read before the write, since the inputs are read on the connection the transaction will hold.
        var (names, income) = await IndexFamilies.InputsAsync(connection, index, night, cancellation);
        var inputs = IndexNightRead.Prepare(index, night, names, income);
        var market = await MarketAsync(connection, MarketBars, night, cancellation);
        var sectors = await SectorsAsync(connection, Sectors, names, cancellation);
        var costs = await CostsAsync(connection, index, night, cancellation);
        var picks = await PicksAsync(connection, index, night, cancellation);
        var open = await OpenAsync(connection, index, night, cancellation);
        var facts = await FactsAsync(connection, [Filers], names.Select(name => name.Ticker), cancellation);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        try
        {
            foreach (var clear in new[] { ClearTheNight, ClearTheNightsRows })
            {
                await ExecuteAsync(connection, transaction, clear, [("$index", index), ("$night", Stamp(night)), ("$source", NightSource)], cancellation);
            }

            var setups = 0;
            var passes = 0;
            var closed = 0;

            if (inputs is not null)
            {
                var closes = inputs.Series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();
                var (highs, lows) = SweepIdeas.HighsAndLows(inputs.Series, inputs.Members, inputs.Calendar.Length);
                var families = LedgerSetups.On(index, inputs.Series, inputs.Sessions, inputs.Members, inputs.Calendar, inputs.At, market, new LedgerContext(income, sectors, highs, lows, facts));

                foreach (var family in families)
                {
                    var listed = picks.TryGetValue(family.Family, out var held) ? held : [];

                    foreach (var row in family.Rows)
                    {
                        var name = NameOf(inputs.Series, row.Ticker);
                        var at = IndexNightRead.BarOf(inputs.Series[name], inputs.At);
                        var outcome = Outcome(closes, inputs, name, at, row.Anchor, row.Plan);
                        var cost = costs.TryGetValue(row.Ticker, out var percent) ? CostInRisks(percent, row.Anchor) : null;

                        await InsertAsync(connection, transaction, row, listed.Contains(row.Ticker), cost, outcome, inputs.Calendar, inputs.At, NightSource, cancellation);
                    }

                    await ExecuteAsync(connection, transaction, InsertNight,
                    [
                        ("$index", index), ("$family", family.Family), ("$session", Stamp(night)), ("$members", family.Members),
                        ("$setups", family.Rows.Count), ("$live_passes", family.LivePasses), ("$source", NightSource),
                    ], cancellation);

                    setups += family.Rows.Count;
                    passes += family.LivePasses;
                }

                closed = await SettleAsync(connection, transaction, index, open, inputs, closes, cancellation);
            }

            await transaction.CommitAsync(cancellation);

            return new LedgerIndex(index, setups, passes, closed, watch.Elapsed.TotalSeconds);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);

            throw;
        }
    }

    // A setup's path from its anchor over the closes the series holds, and the same plan on every member of its
    // session: the benchmark settled only once every member's path has ended.
    static (SetupOutcome Outcome, BenchmarkReading Benchmark) Outcome(double[][] closes, IndexNightInputs inputs, int name, int bar, SetupAnchor anchor, SetupPlan plan)
    {
        var outcome = SetupReplay.Replay(closes[name], bar, anchor);
        var at = inputs.Series[name].SessionAt[bar];
        var members = new List<(double[] Closes, int Bar, double Move)>();

        for (var place = 0; place < inputs.Members.Names[at].Length; place++)
        {
            var member = inputs.Members.Names[at][place];
            var memberBar = inputs.Members.Bars[at][place];

            members.Add((closes[member], memberBar, inputs.Series[member].Atr[memberBar]));
        }

        return (outcome, SetupBenchmark.Of(members, anchor.Session, plan));
    }

    // The stored setups of the index not yet settled, each replayed again over the closes to the night and its benchmark
    // read again, and written where its path ended or its benchmark settled.
    async Task<int> SettleAsync(SqliteConnection connection, SqliteTransaction transaction, string index, IReadOnlyList<OpenSetup> open, IndexNightInputs inputs, double[][] closes, CancellationToken cancellation)
    {
        var closed = 0;
        var names = inputs.Series.Select((one, name) => (one.Name.Ticker, name)).ToDictionary(pair => pair.Ticker, pair => pair.name, StringComparer.Ordinal);

        foreach (var setup in open)
        {
            var at = Array.BinarySearch(inputs.Calendar, setup.Session);

            if (at < 0 || !names.TryGetValue(setup.Ticker, out var name))
            {
                continue;
            }

            var bar = IndexNightRead.BarOf(inputs.Series[name], at);

            if (bar < 0)
            {
                continue;
            }

            var move = inputs.Series[name].Atr[bar];
            var anchor = setup.Anchor;
            var plan = PlanOf(setup, move);
            var (outcome, benchmark) = Outcome(closes, inputs, name, bar, anchor, plan);
            if (outcome.Result is null && !benchmark.Settled)
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, Settle, SettleParameters(index, setup.Family, setup.Ticker, setup.Session, setup.Cost, outcome, benchmark, inputs.Calendar, at), cancellation);

            // A window closes once, on the night its path ends; a row waiting on its benchmark alone is written again
            // and counted again under neither.
            closed += setup.End == SetupEnds.Open && outcome.Result is not null ? 1 : 0;
        }

        return closed;
    }

    // How often the build looks again while it waits for the night or the drain, and the hour a weekday's night
    // window opens, as the sweep's own waits read them.
    public static readonly TimeSpan Poll = SweepRunner.Poll;

    // Whether a chunk about to take the given time must wait: while a night holds its lock, and inside a weekday's
    // night window, which the chunk would run into.
    public static bool MustWait(DateTimeOffset now, bool nightHeld, TimeSpan chunk) => nightHeld || SweepRunner.InTheNightsWindow(now, chunk);

    // The sessions of the span the build writes: those between the span's ends the stored history does not hold, so a
    // build stopped part way goes on from where it was, and a span written again is asked for by name.
    public static IReadOnlyList<int> SessionsToWrite(IReadOnlyList<DateOnly> calendar, int first, int last, IReadOnlySet<DateOnly> stored, bool again)
    {
        var sessions = new List<int>();

        for (var at = first; at <= last; at++)
        {
            if (again || !stored.Contains(calendar[at]))
            {
                sessions.Add(at);
            }
        }

        return sessions;
    }

    // The history, built by hand from a clean copy of main's commit: every session of the span on one index the
    // store does not hold a history row for, over the pulled bars merged with the store's on membership as it stood,
    // each setup replayed to its end where the history reaches it, written a chunk of sessions at a time under a run
    // of its own. The build waits for the night and holds the drain's lock, which the store's copy holds while it
    // copies, for each chunk it writes and for no longer. Asked to write the span again, it replaces what an earlier
    // build wrote for it.
    public async Task<int> BuildAsync(string dataRoot, string index, DateOnly from, DateOnly through, TextWriter output, bool again = false, Func<TimeSpan, CancellationToken, Task>? wait = null, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var runId = FormattableString.Invariant($"{BuildStage}-{startedAt:yyyyMMddTHHmmssZ}");
        var history = new SweepHistory(databaseFile);
        var pause = wait ?? Task.Delay;

        await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

        output.WriteLine($"ledger-build: reading the {DecisionCards.NameOf(index)}'s history through {Stamp(through)}");

        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation, index: index == IndexFamilies.LargeIndex ? null : index, asItStood: index != IndexFamilies.LargeIndex);
        var income = await history.IncomeAsync(through, cancellation);
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Length);
        var closes = series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var market = await MarketAsync(connection, PulledMarketBars, through, cancellation);
        var sectors = await SectorsAsync(connection, PulledSectors, inputs.Names, cancellation);
        var facts = await FactsAsync(connection, [PulledFilers, Filers], inputs.Names.Select(name => name.Ticker), cancellation);
        var context = new LedgerContext(income, sectors, highs, lows, facts);
        var first = Array.FindIndex(calendar, session => session >= from);
        var last = Array.FindLastIndex(calendar, session => session <= through);

        if (first < 0 || last < first)
        {
            output.WriteLine("ledger-build: the history holds no session in the span");

            return 1;
        }

        var stored = await StoredHistorySessionsAsync(connection, index, cancellation);
        var sessionsToWrite = SessionsToWrite(calendar, first, last, stored, again);

        if (sessionsToWrite.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"ledger-build: every session of {Stamp(calendar[first])} to {Stamp(calendar[last])} is written already; --again writes the span again"));

            return 0;
        }

        output.WriteLine(FormattableString.Invariant($"ledger-build: {sessionsToWrite.Count} session(s) to write of the {last - first + 1} in the span, {last - first + 1 - sessionsToWrite.Count} written already"));

        var written = 0;
        var passes = 0;

        for (var taken = 0; taken < sessionsToWrite.Count; taken += ChunkSessions)
        {
            var chunkSessions = sessionsToWrite.Skip(taken).Take(ChunkSessions).ToArray();
            var (start, end) = (chunkSessions[0], chunkSessions[^1]);

            await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

            var watch = Stopwatch.StartNew();

            using var held = await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            foreach (var at in chunkSessions)
            {
                foreach (var clear in new[] { ClearTheNight, ClearTheNightsRows })
                {
                    await ExecuteAsync(connection, transaction, clear, [("$index", index), ("$night", Stamp(calendar[at])), ("$source", HistorySource)], cancellation);
                }
            }

            var chunk = 0;

            foreach (var at in chunkSessions)
            {
                var night = calendar[at];
                var inputsOnTheNight = new IndexNightInputs(index, night, calendar, at, series, sessions, members, members.Names[at], income);

                foreach (var family in LedgerSetups.On(index, series, sessions, members, calendar, at, market, context))
                {
                    foreach (var row in family.Rows)
                    {
                        var name = NameOf(series, row.Ticker);
                        var bar = IndexNightRead.BarOf(series[name], at);
                        var outcome = Outcome(closes, inputsOnTheNight, name, bar, row.Anchor, row.Plan);

                        await InsertAsync(connection, transaction, row, null, null, outcome, calendar, at, HistorySource, cancellation);
                    }

                    await ExecuteAsync(connection, transaction, InsertNight,
                    [
                        ("$index", index), ("$family", family.Family), ("$session", Stamp(night)), ("$members", family.Members),
                        ("$setups", family.Rows.Count), ("$live_passes", family.LivePasses), ("$source", HistorySource),
                    ], cancellation);

                    chunk += family.Rows.Count;
                    passes += family.LivePasses;
                }
            }

            await transaction.CommitAsync(cancellation);

            written += chunk;
            output.WriteLine(FormattableString.Invariant($"ledger-build: {Stamp(calendar[start])} to {Stamp(calendar[end])}, {chunkSessions.Length} session(s), {chunk} setup(s) in {watch.Elapsed.TotalSeconds:0} s, {written} so far"));
        }

        await AppendAsync(connection, runId, BuildStage, startedAt, written, Ok,
            FormattableString.Invariant($"{written} setup(s) on the {DecisionCards.NameOf(index)} over {Stamp(calendar[first])} to {Stamp(calendar[last])}, {passes} passing the live rule, {sessionsToWrite.Count} session(s) written of the {last - first + 1} in the span"), cancellation);

        output.WriteLine(FormattableString.Invariant($"ledger-build: {written} setup(s) written for the {DecisionCards.NameOf(index)} over {sessionsToWrite.Count} session(s), {passes} passing the live rule"));

        return 0;
    }

    // Waiting for the night: while a night holds its lock under the data root, and inside a weekday's night window,
    // looking again each poll and saying so once.
    async Task WaitForTheNightAsync(string dataRoot, TextWriter output, Func<TimeSpan, CancellationToken, Task> pause, CancellationToken cancellation)
    {
        var said = false;

        while (MustWait(clock.UtcNow, NightLock.Holder(dataRoot) is not null, TimeSpan.FromMinutes(10)))
        {
            if (!said)
            {
                output.WriteLine(NightLock.Holder(dataRoot) is { } holder ? $"ledger-build: waiting while {holder} holds the night's lock" : "ledger-build: pausing for the night's window");
                said = true;
            }

            await pause(Poll, cancellation);
        }
    }

    static async Task<IReadOnlySet<DateOnly>> StoredHistorySessionsAsync(SqliteConnection connection, string index, CancellationToken cancellation)
    {
        var stored = new HashSet<DateOnly>();

        await using var command = Command(connection, null, StoredHistory, [("$index", index), ("$source", HistorySource)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            stored.Add(Date(reader.GetString(0)));
        }

        return stored;
    }

    static int NameOf(IReadOnlyList<SweepSeries> series, string ticker)
    {
        for (var name = 0; name < series.Count; name++)
        {
            if (string.Equals(series[name].Name.Ticker, ticker, StringComparison.Ordinal))
            {
                return name;
            }
        }

        throw new InvalidOperationException($"The series hold no {ticker}.");
    }

    // The round trip in risks from the member reader's per cent of the close.
    static double? CostInRisks(double percent, SetupAnchor anchor) =>
        anchor.Risk > 0 ? percent / 100 * anchor.Entry / anchor.Risk : null;

    sealed record OpenSetup(string Family, string Ticker, DateOnly Session, SetupAnchor Anchor, double? Cost, string End);

    // A stored setup's plan in typical moves, read back off its prices and the member's own move on its session.
    static SetupPlan PlanOf(OpenSetup setup, double move)
    {
        var anchor = setup.Anchor;

        return anchor.Trails
            ? new SetupPlan(anchor.RiskMoves ?? anchor.Risk / move, null, move > 0 ? anchor.Trail!.Value / move : anchor.Trail!.Value, anchor.Cap)
            : new SetupPlan(anchor.RiskMoves ?? anchor.Risk / move, (anchor.Target!.Value - anchor.Entry) / anchor.Risk, null, anchor.Cap);
    }

    async Task InsertAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        SetupRow row,
        bool? picked,
        double? cost,
        (SetupOutcome Outcome, BenchmarkReading Benchmark) outcome,
        IReadOnlyList<DateOnly> calendar,
        int at,
        string source,
        CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, InsertSetup,
        [
            ("$index", row.Index), ("$family", row.Family), ("$ticker", row.Ticker), ("$session", Stamp(row.Session)), ("$rule", row.Rule),
            ("$live_pass", row.LivePass ? 1 : 0), ("$picked", picked is { } held ? (held ? 1 : 0) : DBNull.Value),
            ("$cap", row.Anchor.Cap), ("$risk_moves", row.Anchor.RiskMoves is { } moves ? moves : DBNull.Value),
            ("$source", source), ("$pin", LedgerReadings.Version),
            .. SettleParameters(row.Index, row.Family, row.Ticker, row.Session, cost, outcome.Outcome, outcome.Benchmark, calendar, at).Where(parameter => parameter.Name is "$result" or "$benchmark" or "$edge" or "$edge_after_cost" or "$sessions" or "$end" or "$ended_on" or "$settled"),
            ("$cost", cost is { } paid ? paid : DBNull.Value),
        ]);

        Money.Bind(command, "$entry", Statistic.ToPrice(row.Anchor.Entry));
        Money.Bind(command, "$stop", Statistic.ToPrice(row.Anchor.Stop));

        if (row.Anchor.Target is { } target)
        {
            Money.Bind(command, "$target", Statistic.ToPrice(target));
        }
        else
        {
            command.Parameters.AddWithValue("$target", DBNull.Value);
        }

        if (row.Anchor.Trail is { } trail)
        {
            Money.Bind(command, "$trail", Statistic.ToPrice(trail));
        }
        else
        {
            command.Parameters.AddWithValue("$trail", DBNull.Value);
        }

        for (var reading = 0; reading < LedgerReadings.Count; reading++)
        {
            command.Parameters.AddWithValue(FormattableString.Invariant($"$r{reading}"), row.Readings[reading] is { } value ? value : DBNull.Value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // The result half of a row: its result where its path ended, its benchmark where settled, the edge where both are,
    // the edge after its cost where that is read too, the sessions held, how and when it ended, and whether it is settled.
    static List<(string Name, object Value)> SettleParameters(string index, string family, string ticker, DateOnly session, double? cost, SetupOutcome outcome, BenchmarkReading benchmark, IReadOnlyList<DateOnly> calendar, int at)
    {
        var mean = benchmark.Settled ? benchmark.Mean : null;
        double? edge = outcome.Result is { } result && mean is { } against ? result - against : null;
        double? afterCost = edge is { } held && cost is { } paid ? held - paid : null;
        var endedOn = outcome.Result is not null && at + outcome.Sessions < calendar.Count ? Stamp(calendar[at + outcome.Sessions]) : null;

        return
        [
            ("$index", index), ("$family", family), ("$ticker", ticker), ("$session", Stamp(session)),
            ("$result", outcome.Result is { } figure ? figure : DBNull.Value),
            ("$benchmark", mean is { } settledMean ? settledMean : DBNull.Value),
            ("$edge", edge is { } edgeFigure ? edgeFigure : DBNull.Value),
            ("$edge_after_cost", afterCost is { } afterFigure ? afterFigure : DBNull.Value),
            ("$sessions", outcome.Sessions),
            ("$end", outcome.End),
            ("$ended_on", endedOn is { } day ? day : DBNull.Value),
            ("$settled", outcome.Result is not null && benchmark.Settled ? 1 : 0),
        ];
    }

    async Task<IReadOnlyList<OpenSetup>> OpenAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        var open = new List<OpenSetup>();

        await using var command = Command(connection, null, OpenSetups, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var anchor = new SetupAnchor(
                Date(reader.GetString(2)),
                Statistic.FromPrice(Money.FromStorage(reader.GetString(3))),
                Statistic.FromPrice(Money.FromStorage(reader.GetString(4))),
                reader.IsDBNull(5) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(5))),
                reader.IsDBNull(6) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(6))),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8));

            open.Add(new OpenSetup(reader.GetString(0), reader.GetString(1), anchor.Session, anchor, reader.IsDBNull(9) ? null : reader.GetDouble(9), reader.GetString(10)));
        }

        return open;
    }

    static async Task<LedgerMarket> MarketAsync(SqliteConnection connection, string sql, DateOnly night, CancellationToken cancellation)
    {
        var series = new Dictionary<string, IReadOnlyList<(DateOnly Session, double Close)>>(StringComparer.Ordinal);

        foreach (var name in LedgerMarket.Named)
        {
            var closes = new List<(DateOnly, double)>();

            await using var command = Command(connection, null, sql, [("$series", name), ("$night", Stamp(night))]);
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                closes.Add((Date(reader.GetString(1)), Statistic.FromPrice(Money.FromStorage(reader.GetString(2)))));
            }

            if (closes.Count > 0)
            {
                series[name] = closes;
            }
        }

        return new LedgerMarket(series);
    }

    // Each name's sector: the one its history carries, else the newest the store files for it.
    static async Task<IReadOnlyDictionary<string, string?>> SectorsAsync(SqliteConnection connection, string sql, IReadOnlyList<SweepName> names, CancellationToken cancellation)
    {
        var sectors = new Dictionary<string, string?>(StringComparer.Ordinal);

        await using var command = Command(connection, null, sql, []);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            sectors[reader.GetString(0)] = reader.GetString(1);
        }

        foreach (var name in names)
        {
            if (name.Sector is { } sector)
            {
                sectors[name.Ticker] = sector;
            }
        }

        return sectors;
    }

    // Each ticker's filer's facts as first filed, the filer read off the companies the query names, a later row of one
    // ticker's filer replacing an earlier one.
    static async Task<IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>>> FactsAsync(
        SqliteConnection connection,
        IReadOnlyList<string> filerQueries,
        IEnumerable<string> tickers,
        CancellationToken cancellation)
    {
        var filerOf = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var sql in filerQueries)
        {
            await using var command = Command(connection, null, sql, []);
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                filerOf[reader.GetString(0)] = SecEdgarArchive.Padded(reader.GetString(1));
            }
        }

        var byFiler = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal);
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal);

        foreach (var ticker in tickers.Distinct(StringComparer.Ordinal))
        {
            if (!filerOf.TryGetValue(ticker, out var cik))
            {
                continue;
            }

            if (!byFiler.TryGetValue(cik, out var rows))
            {
                var read = new List<FiledFactRow>();

                await using var command = Command(connection, null, FactsOfFiler, [("$cik", cik)]);
                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    read.Add(new FiledFactRow(
                        reader.GetString(0),
                        DateOnly.ParseExact(reader.GetString(1), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Money.FromStorage(reader.GetString(3)),
                        DateOnly.ParseExact(reader.GetString(4), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        reader.GetString(5),
                        reader.GetString(6)));
                }

                rows = read;
                byFiler[cik] = rows;
            }

            facts[ticker] = rows;
        }

        return facts;
    }

    // The business readings of the night's setups read again for the tickers given, once the filings refresh has
    // stored what the archive posted before the session; the night's rows alone, each from the facts filed before
    // the setup's session. It returns how many rows it read again.
    public async Task<int> BusinessAgainAsync(DateOnly session, IReadOnlyList<string> tickers, CancellationToken cancellation = default)
    {
        if (tickers.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var facts = await FactsAsync(connection, [Filers], tickers, cancellation);
        var read = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var ticker in tickers.Distinct(StringComparer.Ordinal))
        {
            var business = FiledFacts.Read(facts.GetValueOrDefault(ticker) ?? [], session);

            await using var command = Command(connection, transaction, BusinessAgain,
            [
                ("$ticker", ticker),
                ("$session", Stamp(session)),
                ("$source", NightSource),
                ("$revenue_growth", (object?)business.RevenueGrowth ?? DBNull.Value),
                ("$growth_change", (object?)business.GrowthChange ?? DBNull.Value),
                ("$gross_margin_change", (object?)business.GrossMarginChange ?? DBNull.Value),
                ("$operating_margin_change", (object?)business.OperatingMarginChange ?? DBNull.Value),
                ("$cash_over_income", (object?)business.CashOverIncome ?? DBNull.Value),
            ]);

            read += await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return read;
    }

    static async Task<IReadOnlyDictionary<string, double>> CostsAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        var costs = new Dictionary<string, double>(StringComparer.Ordinal);

        await using var command = Command(connection, null, Costs, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            costs[reader.GetString(0)] = reader.GetDouble(1);
        }

        return costs;
    }

    // The stocks each family's list held on the night as the index's own stage stored it, keyed by family; a family that
    // listed nothing is absent, so every stock reads as not picked under it.
    static async Task<IReadOnlyDictionary<string, HashSet<string>>> PicksAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        var picks = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var large = index == IndexFamilies.LargeIndex;

        await using var command = Command(connection, null, large ? LargePicks : IndexPicks, large ? [("$night", Stamp(night))] : [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var family = reader.GetString(1);

            if (!picks.TryGetValue(family, out var held))
            {
                picks[family] = held = new HashSet<string>(StringComparer.Ordinal);
            }

            held.Add(reader.GetString(0));
        }

        return picks;
    }

    async Task AppendAsync(SqliteConnection connection, string runId, string stage, DateTimeOffset startedAt, int rows, string outcome, string detail, CancellationToken cancellation) =>
        await ExecuteAsync(connection, null, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", stage),
            ("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$outcome", outcome),
            ("$rows_written", rows),
            ("$detail", detail),
        ], cancellation);

    // The stage's row after a failure outside any index's part, on a connection of its own, and none where the store
    // will not take it: recording the failure is never what stops the night.
    async Task AppendAfterAFailureAsync(string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await AppendAsync(connection, runId, Stage, startedAt, rows, NotComputed, detail, cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
        }
    }

    static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = Command(connection, null, sql, parameters);
        var value = await command.ExecuteScalarAsync(cancellation);

        return value is DBNull ? null : value;
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
