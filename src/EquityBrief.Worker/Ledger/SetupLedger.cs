using System.Diagnostics;
using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Insiders;
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
// stood and its path still open, and closes the windows of the setups the night stored before whose paths ended on the
// night's close or whose benchmark, the same plan on every member of that session the night reads, has every member's
// path ended; the same component builds the history by hand, session by session over the pulled bars merged with the store's,
// each row and its benchmark replayed to their end where the history and the store's own closes after it reach, and the
// history's rows are settled by that build alone. A reading the inputs do not reach is stored as none. A failure in one
// index's part leaves that index's rows of the night unwritten and named on the stage's row, and the step goes on; the
// stage makes no request and calls no model. A night run again replaces its own rows.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
// see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
// see: The history's setups are settled by the build alone, which replays them over the store's own prices after its span
// see: The nightly run is arithmetic only
public sealed partial class SetupLedger : IComponent
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
            new StoreTouch(Store.InsiderTrade, Touch.Read),
            new StoreTouch(Store.InsiderRead, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.SetupNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LedgerSummary, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.LedgerReading, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
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

    // From 19.13 the insiders' transactions the four readings read, filed on or after a day: every open-market purchase
    // and sale, and every row of an amendment, which may stand in for an original's; and the spans the insider history
    // holds.
    // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
    const string InsiderRowsFrom = @"
        SELECT cik, accession, filed, form, amends, original_filed, owner_ciks, officer, sequence, traded, code, shares, price, acquired, planned, direct
        FROM insider_trade
        WHERE filed >= $from AND (code IN ('P', 'S') OR form = '4/A');
    ";

    const string InsiderSpansRead = "SELECT first_day, last_day FROM insider_read;";

    const string BusinessAgain = @"
        UPDATE setup
        SET revenue_growth = $revenue_growth, growth_change = $growth_change, gross_margin_change = $gross_margin_change,
            operating_margin_change = $operating_margin_change, cash_over_income = $cash_over_income, value_place = $value_place
        WHERE ticker = $ticker AND session_date = $session AND source = $source;
    ";

    // From 19.13 the four insider readings of the night's setups and its members' readings, read again once the filings
    // refresh has stored the session before's filings.
    const string InsidersAgain = @"
        UPDATE setup
        SET insider_net_buy = $insider_net_buy, insider_buyers = $insider_buyers, insider_officer_buy = $insider_officer_buy,
            insider_unplanned_sale = $insider_unplanned_sale
        WHERE ticker = $ticker AND session_date = $session AND source = $source;
    ";

    const string NightsSetupTickers = "SELECT DISTINCT ticker FROM setup WHERE session_date = $session AND source = $source;";

    const string NightsReadings = "SELECT index_code, ticker, readings FROM ledger_reading WHERE session_date = $session;";

    const string ReadingsAgain = "UPDATE ledger_reading SET readings = $readings WHERE index_code = $index AND ticker = $ticker AND session_date = $session;";

    // A member's closes on the night, which the valuation reading reads again once the filings refresh has stored its facts.
    const string StoredCloses = "SELECT session_date, close, raw_close FROM bar WHERE ticker = $ticker AND session_date <= $night ORDER BY session_date;";

    // The newest pull holding a member's first stored session, and its close there, which the scale is read from.
    const string PullHoldingFirst = "SELECT pull, close FROM pulled_bar WHERE ticker = $ticker AND session_date = $first ORDER BY pull DESC LIMIT 1;";

    // That pull's sessions before the first stored session, from the newest on or before the oldest day the reading reaches.
    const string PulledBeforeTheYear = @"
        SELECT session_date, close, raw_close FROM pulled_bar
        WHERE ticker = $ticker AND pull = $pull AND session_date < $first
            AND session_date >= COALESCE((SELECT MAX(session_date) FROM pulled_bar WHERE ticker = $ticker AND pull = $pull AND session_date <= $oldest), $oldest)
        ORDER BY session_date;";

    // The round trip a member's trade pays, in per cent of its close, as the member reader stored it for the night.
    const string Costs = "SELECT ticker, cost FROM member_reading WHERE index_code = $index AND session_date = $night AND cost IS NOT NULL;";

    // The S&P 500's own families' list, and the fundamentals-first family's, which the index families' step draws for it.
    const string LargePicks = @"
        SELECT ticker, family FROM family_pick WHERE session_date = $night AND state = 'listed'
        UNION ALL
        SELECT ticker, family FROM index_family_pick WHERE index_code = 'GSPC' AND session_date = $night AND state = 'listed';";

    const string IndexPicks = "SELECT ticker, family FROM index_family_pick WHERE index_code = $index AND session_date = $night AND state = 'listed';";

    const string ClearTheNight = "DELETE FROM setup WHERE index_code = $index AND session_date = $night AND source = $source;";

    const string ClearTheNightsRows = "DELETE FROM setup_night WHERE index_code = $index AND session_date = $night AND source = $source;";

    const string StoredHistory = "SELECT DISTINCT session_date FROM setup_night WHERE index_code = $index AND source = $source;";

    const string ClearTheNightsReadings = "DELETE FROM ledger_reading WHERE index_code = $index AND session_date = $night;";

    const string InsertReadings = "INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ($index, $session, $ticker, $readings, $pin);";

    // A member's readings as stored: the catalogue's in its order as a list in the invariant form, null where none.
    public static string ReadingsText(IReadOnlyList<double?> readings) =>
        "[" + string.Join(",", readings.Select(reading => reading is { } value && double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "null")) + "]";

    // The night's own setups alone: a history setup is settled by the build that wrote it, which reads its members as
    // they stood, where the night reads the members on the night and the bar store's year.
    // see: The history's setups are settled by the build alone, which replays them over the store's own prices after its span
    const string OpenSetups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, risk_moves, cost, end
        FROM setup WHERE index_code = $index AND source = $source AND settled = 0 AND session_date < $night ORDER BY session_date, family, ticker;";

    const string Settle = @"
        UPDATE setup SET result = $result, benchmark = $benchmark, edge = $edge, edge_after_cost = $edge_after_cost, sessions = $sessions,
            end = $end, ended_on = $ended_on, settled = $settled, fill = $fill, past = $past
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
        + ", result, benchmark, cost, edge, edge_after_cost, sessions, end, ended_on, settled, source, pin, fill, past) VALUES ("
        + "$index, $family, $ticker, $session, $rule, $live_pass, $picked, $entry, $stop, $target, $trail, $cap, $risk_moves, "
        + string.Join(", ", LedgerReadings.All.Select((_, at) => FormattableString.Invariant($"$r{at}")))
        + ", $result, $benchmark, $cost, $edge, $edge_after_cost, $sessions, $end, $ended_on, $settled, $source, $pin, $fill, $past);";

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

    // The catalogue's shared readings of an index's members on the newest night, for a rule whose hooks read them: each
    // member's read as the night's own setups read theirs, from the same inputs, and none for a member the night holds no
    // bar for. Read only where a standing rule's hooks ask, so a night with none reads nothing.
    // see: Every engine's settings hooks land together and all default off, so the families' pins move once
    public async Task<Func<string, IReadOnlyList<double?>?>> ReadingsTonightAsync(string index, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
        {
            return _ => null;
        }

        var night = Date(newest);
        var (names, income) = await IndexFamilies.InputsAsync(connection, index, night, cancellation);

        if (IndexNightRead.Prepare(index, night, names, income) is not { } inputs)
        {
            return _ => null;
        }

        var market = await MarketAsync(connection, MarketBars, night, cancellation);
        var sectors = await SectorsAsync(connection, Sectors, names, cancellation);
        var facts = await FactsAsync(connection, [Filers], names.Select(name => name.Ticker), cancellation);
        var before = await BeforeTheYearAsync(connection, FirstSessions(names), facts, night, cancellation);
        var insiders = await InsidersAsync(connection, [Filers], names.Select(name => name.Ticker), night.AddDays(-InsiderReadings.WindowDays), cancellation);
        var (highs, lows) = SweepIdeas.HighsAndLows(inputs.Series, inputs.Members, inputs.Calendar.Length);
        var context = new LedgerContext(income, sectors, highs, lows, facts, before)
        {
            Ranks = new FactorRanks(inputs.Series, inputs.Members),
            Insiders = insiders.Rows,
            InsiderSpans = insiders.Spans,
        };
        var read = new Dictionary<string, IReadOnlyList<double?>?>(StringComparer.Ordinal);

        IReadOnlyList<double?>? Of(string ticker)
        {
            var name = Array.FindIndex(inputs.Series, one => string.Equals(one.Name.Ticker, ticker, StringComparison.Ordinal));

            if (name < 0 || IndexNightRead.BarOf(inputs.Series[name], inputs.At) is var bar && bar < 0)
            {
                return null;
            }

            var closes = inputs.Series[name].Bars.Select(one => Statistic.FromPrice(one.Close)).ToArray();

            return LedgerSetups.Readings(index, name, inputs.Series[name], closes, bar, inputs.Sessions[inputs.At], inputs.Members, inputs.Calendar, inputs.At, market, context);
        }

        return ticker => read.TryGetValue(ticker, out var held) ? held : read[ticker] = Of(ticker);
    }

    // What a history's readings read beside its series, as the build reads it: the pulled market series to the history's
    // end, each name's sector and its filer's facts as first filed, and the members at a new high and a new low on each
    // session; so the tester reads every listing's readings through the catalogue's own function.
    // see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
    public static async Task<(LedgerMarket Market, LedgerContext Context)> HistoryReadingsAsync(
        string databaseFile,
        SweepHistoryInputs inputs,
        IReadOnlyList<SweepSeries> series,
        SweepBenchmark.Members members,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        DateOnly through,
        CancellationToken cancellation = default)
    {
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, inputs.Sessions.Length);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var market = await MarketAsync(connection, PulledMarketBars, through, cancellation);
        var sectors = await SectorsAsync(connection, PulledSectors, inputs.Names, cancellation);
        var facts = await FactsAsync(connection, [PulledFilers, Filers], inputs.Names.Select(name => name.Ticker), cancellation);
        var insiders = await InsidersAsync(connection, [PulledFilers, Filers], inputs.Names.Select(name => name.Ticker), FirstInsiderDay(inputs.Sessions), cancellation);

        return (market, new LedgerContext(income, sectors, highs, lows, facts)
        {
            Ranks = new FactorRanks(series, members),
            Insiders = insiders.Rows,
            InsiderSpans = insiders.Spans,
        });
    }

    // The first day a history's insider readings read a filing on: the window before its first session.
    static DateOnly FirstInsiderDay(IReadOnlyList<DateOnly> sessions) =>
        sessions.Count == 0 ? DateOnly.MinValue : sessions[0].AddDays(-InsiderReadings.WindowDays);

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
        var before = await BeforeTheYearAsync(connection, FirstSessions(names), facts, night, cancellation);
        var insiders = await InsidersAsync(connection, [Filers], names.Select(name => name.Ticker), night.AddDays(-InsiderReadings.WindowDays), cancellation);

        // The S&P 500's heavyweights' rebalances the book stored from the oldest open heavyweights' setup to the night:
        // tonight's size cut is tonight's setups, and each later rebalance's buys end the setups the rule did not buy.
        var since = open.Where(setup => setup.Family == HeavyweightRule.Name).Select(setup => setup.Session).DefaultIfEmpty(night).Min();
        var rebalances = index == IndexFamilies.LargeIndex
            ? await HeavyweightCutsAsync(connection, since, night, cancellation)
            : new Dictionary<DateOnly, IReadOnlyList<HeavyweightCut>>();

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
                var opens = inputs.Series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Open)).ToArray()).ToArray();
                var (highs, lows) = SweepIdeas.HighsAndLows(inputs.Series, inputs.Members, inputs.Calendar.Length);
                var context = new LedgerContext(income, sectors, highs, lows, facts, before)
                {
                    Ranks = new FactorRanks(inputs.Series, inputs.Members),
                    Insiders = insiders.Rows,
                    InsiderSpans = insiders.Spans,
                };
                var families = LedgerSetups.On(index, inputs.Series, inputs.Sessions, inputs.Members, inputs.Calendar, inputs.At, market, context);

                foreach (var family in families)
                {
                    var listed = picks.TryGetValue(family.Family, out var held) ? held : [];

                    foreach (var row in family.Rows)
                    {
                        var name = NameOf(inputs.Series, row.Ticker);
                        var at = IndexNightRead.BarOf(inputs.Series[name], inputs.At);
                        var outcome = Outcome(closes, opens, inputs, name, at, row.Anchor, row.Plan);
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

                // A rebalance night of the S&P 500's book: each member of each sector's size cut, its pick whether the
                // book bought it, its cost a fraction of the buy, its path open and its benchmark the size cut's.
                if (rebalances.TryGetValue(night, out var tonight))
                {
                    var heavyweights = LedgerSetups.Heavyweights(index, inputs.Series, closes, inputs.Sessions, inputs.Members, inputs.Calendar, inputs.At, market, context, tonight);

                    foreach (var row in heavyweights.Rows)
                    {
                        double? cost = costs.TryGetValue(row.Ticker, out var percent) ? percent / 100 : null;

                        await InsertAsync(connection, transaction, row, row.LivePass, cost, new SetupRead(new SetupOutcome(null, 0, SetupEnds.Open), new BenchmarkReading(null, 0, 0)), inputs.Calendar, inputs.At, NightSource, cancellation);
                    }

                    await ExecuteAsync(connection, transaction, InsertNight,
                    [
                        ("$index", index), ("$family", heavyweights.Family), ("$session", Stamp(night)), ("$members", heavyweights.Members),
                        ("$setups", heavyweights.Rows.Count), ("$live_passes", heavyweights.LivePasses), ("$source", NightSource),
                    ], cancellation);

                    setups += heavyweights.Rows.Count;
                    passes += heavyweights.LivePasses;
                }

                closed = await SettleAsync(connection, transaction, index, open, inputs, closes, opens, rebalances, cancellation);

                // Every member's readings on the night, read as its setups read theirs, which the chart of what is behind a
                // stock reads for its percentiles among the members and its push on the learned score.
                // see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
                await ExecuteAsync(connection, transaction, ClearTheNightsReadings, [("$index", index), ("$night", Stamp(night))], cancellation);

                for (var name = 0; name < inputs.Series.Length; name++)
                {
                    var bar = IndexNightRead.BarOf(inputs.Series[name], inputs.At);

                    if (bar < 0)
                    {
                        continue;
                    }

                    var readings = LedgerSetups.Readings(index, name, inputs.Series[name], closes[name], bar, inputs.Sessions[inputs.At], inputs.Members, inputs.Calendar, inputs.At, market, context);

                    await ExecuteAsync(connection, transaction, InsertReadings,
                    [
                        ("$index", index), ("$session", Stamp(night)), ("$ticker", inputs.Series[name].Name.Ticker),
                        ("$readings", ReadingsText(readings)), ("$pin", LedgerReadings.Version),
                    ], cancellation);
                }
            }

            await transaction.CommitAsync(cancellation);
            await RefreshSummaryAsync(connection, index, cancellation);

            return new LedgerIndex(index, setups, passes, closed, watch.Elapsed.TotalSeconds);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);

            throw;
        }
    }

    // A setup's path from its anchor over the closes the series holds, and the same plan on every member of its
    // session: the benchmark settled once every member's path has ended, a member whose prices stopped before its path
    // ended left out. The build names the members whose prices stopped; the night names none, since every member it reads
    // is a member on the night and one the index let go before it is not read at all.
    // see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
    // From 19.10 the path is bought at the open of the session after the setup's night and the benchmark the same way on
    // every member, each read through the core's one pricing function at the open.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    static SetupRead Outcome(double[][] closes, double[][] opens, IndexNightInputs inputs, int name, int bar, SetupAnchor anchor, SetupPlan plan, bool[]? stopped = null)
    {
        var outcome = SetupReplay.Replay(closes[name], bar, anchor);
        var at = inputs.Series[name].SessionAt[bar];
        var members = new List<(double[] Closes, double[] Opens, int Bar, double Move, bool Stopped)>();

        for (var place = 0; place < inputs.Members.Names[at].Length; place++)
        {
            var member = inputs.Members.Names[at][place];
            var memberBar = inputs.Members.Bars[at][place];

            members.Add((closes[member], opens[member], memberBar, inputs.Series[member].Atr[memberBar], stopped is not null && stopped[member]));
        }

        var (benchmark, listed) = SetupBenchmark.WithMembers(members, anchor.Session, plan);

        return AtOpen(anchor, outcome, benchmark, listed, OpenAfter(closes[name], opens[name], bar), book: false);
    }

    // A setup read at its next session's open: its result, its benchmark and where that open stood against its plan, the
    // close's figures moved by the gaps; a setup whose path ended with no open stored after its night places no trade.
    static SetupRead AtOpen(SetupAnchor anchor, SetupOutcome outcome, BenchmarkReading benchmark, IReadOnlyList<FillMember> members, double? open, bool book)
    {
        var moved = benchmark with { Mean = TradeFills.Benchmark(benchmark.Mean, members).Mean };

        if (open is not { } price || !(price > 0))
        {
            return new SetupRead(outcome.Result is null ? outcome : new SetupOutcome(null, outcome.Sessions, SetupEnds.None), moved);
        }

        var fill = book
            ? TradeFills.Holding(anchor.Session, anchor.Entry, price, outcome.Result, benchmark.Mean, members)
            : TradeFills.Swing(anchor.Session, anchor.Entry, anchor.Stop, anchor.Trails ? null : anchor.Target, price, outcome.Result, benchmark.Mean, members);

        return fill is null
            ? new SetupRead(outcome.Result is null ? outcome : new SetupOutcome(null, outcome.Sessions, SetupEnds.None), moved)
            : new SetupRead(outcome with { Result = fill.Result }, moved, fill.Fill, fill.Past);
    }

    // The open of the session after a bar, none where the series holds none.
    static double? OpenAfter(double[] closes, double[] opens, int bar) =>
        bar >= 0 && bar + 1 < closes.Length && bar + 1 < opens.Length && opens[bar + 1] > 0 ? opens[bar + 1] : null;

    // A setup's path and its benchmark as the ledger stores them from 19.10, at the open, with the open it was bought at
    // and where that open stood against its plan; none bought where the store holds no open after its night.
    sealed record SetupRead(SetupOutcome Outcome, BenchmarkReading Benchmark, double? Fill = null, string? Past = null);

    // The night's stored setups not yet settled, each replayed again over the closes to the night and its benchmark read
    // again, and written where its path ended or its benchmark settled.
    async Task<int> SettleAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        IReadOnlyList<OpenSetup> open,
        IndexNightInputs inputs,
        double[][] closes,
        double[][] opens,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<HeavyweightCut>> rebalances,
        CancellationToken cancellation)
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

            SetupRead read;

            if (setup.Family == HeavyweightRule.Name)
            {
                if (HeavyweightOutcome(setup, rebalances, inputs, closes, opens, names, name, bar, at) is not { } heavyweight)
                {
                    continue;
                }

                read = heavyweight;
            }
            else
            {
                read = Outcome(closes, opens, inputs, name, bar, setup.Anchor, PlanOf(setup, inputs.Series[name].Atr[bar]));
            }

            var (outcome, benchmark) = (read.Outcome, read.Benchmark);

            if (outcome.Result is null && !benchmark.Settled)
            {
                continue;
            }

            await ExecuteAsync(connection, transaction, Settle, [.. SettleParameters(index, setup.Family, setup.Ticker, setup.Session, setup.Cost, outcome, benchmark, inputs.Calendar, at), .. FillParameters(read)], cancellation);

            // A window closes once, on the night its path ends; a row waiting on its benchmark alone is written again
            // and counted again under neither.
            closed += setup.End == SetupEnds.Open && outcome.Result is not null ? 1 : 0;
        }

        return closed;
    }

    // A heavyweights' setup's path to the night, read off the rebalances the book stored: sold at the first later one
    // whose buys leave the stock out, the benchmark its sector's size cut on its own session over the same sessions;
    // none where the book's rows of its own session are not held.
    static SetupRead? HeavyweightOutcome(
        OpenSetup setup,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<HeavyweightCut>> rebalances,
        IndexNightInputs inputs,
        double[][] closes,
        double[][] opens,
        IReadOnlyDictionary<string, int> names,
        int name,
        int bar,
        int at)
    {
        if (!rebalances.TryGetValue(setup.Session, out var entered)
            || entered.FirstOrDefault(cut => cut.Largest.Contains(setup.Ticker, StringComparer.Ordinal)) is not { } sector)
        {
            return null;
        }

        var notBought = rebalances
            .Where(pair => pair.Key > setup.Session && !pair.Value.Any(cut => cut.Leaders.Contains(setup.Ticker)))
            .Select(pair => Array.BinarySearch(inputs.Calendar, pair.Key))
            .Where(session => session >= 0)
            .Select(session => IndexNightRead.BarOf(inputs.Series[name], session))
            .Where(held => held >= 0)
            .ToArray();
        var outcome = HeavyweightPaths.Replay(closes[name], bar, notBought);
        var cut = sector.Largest
            .Where(names.ContainsKey)
            .Select(ticker => (closes[names[ticker]], opens[names[ticker]], IndexNightRead.BarOf(inputs.Series[names[ticker]], at)))
            .ToArray();

        return HeavyweightRead(setup.Anchor, outcome, [.. cut.Select(member => (member.Item1, member.Item3, false))], cut, closes[name], opens[name], bar);
    }

    // A heavyweights' setup read at its next session's open against its size cut bought the same way.
    static SetupRead HeavyweightRead(SetupAnchor anchor, SetupOutcome outcome, IReadOnlyList<(double[] Closes, int From, bool Stopped)> cut, IReadOnlyList<(double[] Closes, double[] Opens, int From)> members, double[] closes, double[] opens, int bar)
    {
        var benchmark = outcome.Result is null ? new BenchmarkReading(null, 0, cut.Count) : HeavyweightPaths.Cut(cut, outcome.Sessions);

        return AtOpen(anchor, outcome, benchmark, HeavyweightPaths.CutMembers(members, outcome.Sessions), OpenAfter(closes, opens, bar), book: true);
    }

    // The fill half of a row: the open it was bought at, at the setup's own scale, and where that open stood.
    static List<(string Name, object Value)> FillParameters(SetupRead read) =>
    [
        ("$fill", read.Fill is { } fill ? Money.ToStorage(Statistic.ToPrice(fill)) : DBNull.Value),
        ("$past", read.Past is { } past ? past : DBNull.Value),
    ];

    // The S&P 500's heavyweights' rebalances over the history, read by the sweep's own replay at the live setting: the
    // first scored session of each month, each sector's size cut by value as it stood with the leaders the rule buys.
    static async Task<IReadOnlyDictionary<int, IReadOnlyList<HeavyweightCut>>> HeavyweightHistoryAsync(
        SweepHistory history,
        SweepHistoryInputs inputs,
        DateOnly through,
        TextWriter output,
        CancellationToken cancellation)
    {
        var calendar = inputs.Sessions;
        var first = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);

        if (first < 0)
        {
            return new Dictionary<int, IReadOnlyList<HeavyweightCut>>();
        }

        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);
        var pulled = await history.HeavyweightAsync(through, cancellation);
        var market = await history.MarketAsync(through, cancellation);

        output.WriteLine(FormattableString.Invariant($"ledger-build: reading the heavyweights' {months.Count} rebalance(s) by the sweep's own replay"));

        var (_, laid) = HeavyweightSweep.Lay(inputs, pulled, market.FirstOrDefault(one => one.Series == LedgerMarket.Index), months.ToHashSet(), first);

        return months
            .Where(laid.ContainsKey)
            .ToDictionary(
                session => session,
                session => (IReadOnlyList<HeavyweightCut>)
                [
                    .. HeavyweightSweep.Sectors(laid[session], HeavyweightSweep.Frozen).Select(sector => new HeavyweightCut(
                        sector.Sector,
                        [.. sector.Largest.Select(ranked => ranked.Ticker)],
                        sector.Leaders.ToHashSet(StringComparer.Ordinal))),
                ]);
    }

    const string ClearTheSummary = "DELETE FROM ledger_summary WHERE index_code = $index;";

    const string SummaryCounts = @"
        SELECT family, CAST(substr(session_date, 1, 4) AS INTEGER) AS year, COUNT(*), SUM(live_pass),
            SUM(CASE WHEN picked IS NOT NULL THEN 1 ELSE 0 END), SUM(CASE WHEN picked = 1 THEN 1 ELSE 0 END), SUM(settled),
            AVG(CASE WHEN settled = 1 THEN result END), AVG(CASE WHEN settled = 1 THEN edge END)
        FROM setup WHERE index_code = $index GROUP BY family, year ORDER BY family, year;";

    const string SettledFigures = "SELECT family, substr(session_date, 1, 4), result, edge FROM setup WHERE index_code = $index AND settled = 1;";

    const string InsertSummary = @"
        INSERT INTO ledger_summary (index_code, family, year, setups, live_passes, night_rows, picked, settled, result_mean, edge_mean, result_deciles, edge_deciles, refreshed_at)
        VALUES ($index, $family, $year, $setups, $live_passes, $night_rows, $picked, $settled, $result_mean, $edge_mean, $result_deciles, $edge_deciles, $refreshed_at);";

    // The Ledger page's summary of one index rewritten whole from its setups: each family's rows a year, those the live
    // rule passes, those the night wrote and of them those its list picked, those settled, their mean result and edge
    // and the cut points between the deciles of each.
    async Task RefreshSummaryAsync(SqliteConnection connection, string index, CancellationToken cancellation)
    {
        var counts = new List<(string Family, int Year, int Setups, int Passes, int NightRows, int Picked, int Settled, double? Result, double? Edge)>();

        await using (var command = Command(connection, null, SummaryCounts, [("$index", index)]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                counts.Add((
                    reader.GetString(0),
                    reader.GetInt32(1),
                    reader.GetInt32(2),
                    reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetInt32(6),
                    reader.IsDBNull(7) ? null : reader.GetDouble(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8)));
            }
        }

        var figures = new Dictionary<(string Family, int Year), (List<double> Results, List<double> Edges)>();

        await using (var command = Command(connection, null, SettledFigures, [("$index", index)]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var key = (reader.GetString(0), int.Parse(reader.GetString(1), CultureInfo.InvariantCulture));

                if (!figures.TryGetValue(key, out var held))
                {
                    held = ([], []);
                    figures[key] = held;
                }

                if (!reader.IsDBNull(2))
                {
                    held.Results.Add(reader.GetDouble(2));
                }

                if (!reader.IsDBNull(3))
                {
                    held.Edges.Add(reader.GetDouble(3));
                }
            }
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheSummary, [("$index", index)], cancellation);

        foreach (var row in counts)
        {
            var settled = figures.TryGetValue((row.Family, row.Year), out var held) ? held : ([], []);
            var results = LedgerSummaries.Deciles(settled.Results);
            var edges = LedgerSummaries.Deciles(settled.Edges);

            await ExecuteAsync(connection, transaction, InsertSummary,
            [
                ("$index", index), ("$family", row.Family), ("$year", row.Year), ("$setups", row.Setups), ("$live_passes", row.Passes),
                ("$night_rows", row.NightRows), ("$picked", row.Picked), ("$settled", row.Settled),
                ("$result_mean", (object?)row.Result ?? DBNull.Value), ("$edge_mean", (object?)row.Edge ?? DBNull.Value),
                ("$result_deciles", results is null ? DBNull.Value : LedgerSummaries.Stored(results)),
                ("$edge_deciles", edges is null ? DBNull.Value : LedgerSummaries.Stored(edges)),
                ("$refreshed_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture)),
            ], cancellation);
        }

        await transaction.CommitAsync(cancellation);
    }

    // The rebalances the S&P 500's heavyweights' book stored between two sessions, each sector's size cut in order of
    // value with the leaders it bought.
    const string HeavyweightRebalances = @"
        SELECT session_date, sector, ticker, leader FROM heavyweight_night
        WHERE session_date >= $since AND session_date <= $night
        ORDER BY session_date, sector, place;";

    static async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<HeavyweightCut>>> HeavyweightCutsAsync(SqliteConnection connection, DateOnly since, DateOnly night, CancellationToken cancellation)
    {
        var rows = new List<(DateOnly Session, string Sector, string Ticker, bool Leader)>();

        await using (var command = Command(connection, null, HeavyweightRebalances, [("$since", Stamp(since)), ("$night", Stamp(night))]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                rows.Add((Date(reader.GetString(0)), reader.GetString(1), reader.GetString(2), reader.GetInt64(3) == 1));
            }
        }

        return rows
            .GroupBy(row => row.Session)
            .ToDictionary(
                session => session.Key,
                session => (IReadOnlyList<HeavyweightCut>)
                [
                    .. session
                        .GroupBy(row => row.Sector, StringComparer.Ordinal)
                        .Select(sector => new HeavyweightCut(
                            sector.Key,
                            [.. sector.Select(row => row.Ticker)],
                            sector.Where(row => row.Leader).Select(row => row.Ticker).ToHashSet(StringComparer.Ordinal))),
                ]);
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
    // each setup and its benchmark replayed to their end where the history and the bar store's closes after it reach,
    // written a chunk of sessions at a time under a run of its own. The build waits for the night and holds the drain's
    // lock, which the store's copy holds while it copies, for each chunk it writes and for no longer. Asked to write the
    // span again, it replaces what an earlier build wrote for it.
    // see: The history's setups are settled by the build alone, which replays them over the store's own prices after its span
    public async Task<int> BuildAsync(string dataRoot, string index, DateOnly from, DateOnly through, TextWriter output, bool again = false, Func<TimeSpan, CancellationToken, Task>? wait = null, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var pause = wait ?? Task.Delay;

        await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var prepared = await PrepareAsync(connection, index, through, output, cancellation);
        var first = Array.FindIndex(prepared.Calendar, session => session >= from);
        var last = Array.FindLastIndex(prepared.Calendar, session => session <= through);

        if (first < 0 || last < first)
        {
            output.WriteLine("ledger-build: the history holds no session in the span");

            return 1;
        }

        var stored = await StoredHistorySessionsAsync(connection, index, cancellation);
        var sessionsToWrite = SessionsToWrite(prepared.Calendar, first, last, stored, again);

        if (sessionsToWrite.Count == 0)
        {
            output.WriteLine(FormattableString.Invariant($"ledger-build: every session of {Stamp(prepared.Calendar[first])} to {Stamp(prepared.Calendar[last])} is written already; --again writes the span again"));

            return 0;
        }

        output.WriteLine(FormattableString.Invariant($"ledger-build: {sessionsToWrite.Count} session(s) to write of the {last - first + 1} in the span, {last - first + 1 - sessionsToWrite.Count} written already"));

        await WriteAsync(dataRoot, connection, prepared, first, last, sessionsToWrite, prepared.Compute, startedAt, output, pause, cancellation);

        return 0;
    }

    // The tolerance the monthly rebuild holds each family on each index to: the setups the standing build gives a
    // benchmark, less the larger of half a per cent of them and ten setups.
    // see: The monthly run writes the history again only where no family on any index loses more than half a per cent of its benchmarks or ten setups
    public const double RebuildShortfallShare = 0.005;

    public const int RebuildShortfallFloor = 10;

    public static int RebuildAllowed(int standing) => Math.Max(RebuildShortfallFloor, (int)Math.Ceiling(standing * RebuildShortfallShare));

    public static bool RebuildHolds(int standing, int rebuilt) => standing - rebuilt <= RebuildAllowed(standing);

    const string StandingBenchmarks = @"
        SELECT family, SUM(CASE WHEN settled = 1 AND benchmark IS NOT NULL THEN 1 ELSE 0 END)
        FROM setup WHERE index_code = $index AND source = $source GROUP BY family;";

    // The monthly rebuild: each index's history computed again over the span the standing build wrote, beside it and
    // before any of it is written, and written for every index only where no family on any index gives fewer setups a
    // benchmark than the standing build beyond the tolerance. Otherwise nothing is written, the standing history stands,
    // and the words name each family short with both counts.
    // see: The monthly run writes the history again only where no family on any index loses more than half a per cent of its benchmarks or ten setups
    public async Task<(bool Held, string Words)> RebuildAsync(string dataRoot, IReadOnlyList<string> indices, TextWriter output, Func<TimeSpan, CancellationToken, Task>? wait = null, CancellationToken cancellation = default)
    {
        var pause = wait ?? Task.Delay;
        var rebuilt = new List<(PreparedHistory Prepared, int First, int Last, IReadOnlyList<int> Sessions, IReadOnlyDictionary<int, BuiltSession> Built, DateTimeOffset StartedAt)>();
        var shortOf = new List<string>();
        var counted = new List<string>();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        foreach (var index in indices)
        {
            if (await HistorySpanAsync(index, cancellation) is not { } span)
            {
                return (false, $"the {DecisionCards.NameOf(index)} holds no history to write again; build it by hand first");
            }

            await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

            var startedAt = clock.UtcNow;
            var prepared = await PrepareAsync(connection, index, span.Through, output, cancellation);
            var first = Array.FindIndex(prepared.Calendar, session => session >= span.From);
            var last = Array.FindLastIndex(prepared.Calendar, session => session <= span.Through);

            if (first < 0 || last < first)
            {
                return (false, $"the {DecisionCards.NameOf(index)}'s history holds no session of its span");
            }

            var sessions = SessionsToWrite(prepared.Calendar, first, last, new HashSet<DateOnly>(), again: true);
            var built = sessions.ToDictionary(at => at, prepared.Compute);
            var benchmarks = built.Values
                .SelectMany(session => session.Families)
                .GroupBy(family => family.Family.Family, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Sum(family => family.Reads.Count(read => read.Outcome.Result is not null && read.Benchmark.Settled)), StringComparer.Ordinal);
            var standing = new Dictionary<string, int>(StringComparer.Ordinal);

            await using (var command = Command(connection, null, StandingBenchmarks, [("$index", index), ("$source", HistorySource)]))
            await using (var reader = await command.ExecuteReaderAsync(cancellation))
            {
                while (await reader.ReadAsync(cancellation))
                {
                    standing[reader.GetString(0)] = reader.GetInt32(1);
                }
            }

            foreach (var family in standing.Keys.Union(benchmarks.Keys).Order(StringComparer.Ordinal))
            {
                var was = standing.GetValueOrDefault(family);
                var now = benchmarks.GetValueOrDefault(family);

                if (!RebuildHolds(was, now))
                {
                    shortOf.Add(FormattableString.Invariant($"the {DecisionCards.NameOf(index)}'s {family} {now} against the standing {was}, {was - now} fewer where {RebuildAllowed(was)} are allowed"));
                }
            }

            counted.Add(FormattableString.Invariant($"the {DecisionCards.NameOf(index)} {benchmarks.Values.Sum()} with a benchmark against the standing {standing.Values.Sum()}"));
            output.WriteLine($"ledger-build: the {DecisionCards.NameOf(index)}'s history computed again beside the standing one, {counted[^1]}");
            rebuilt.Add((prepared, first, last, sessions, built, startedAt));
        }

        if (shortOf.Count > 0)
        {
            return (false, "the standing history is kept and nothing is written, since " + string.Join("; ", shortOf));
        }

        foreach (var (prepared, first, last, sessions, built, startedAt) in rebuilt)
        {
            await WriteAsync(dataRoot, connection, prepared, first, last, sessions, at => built[at], startedAt, output, pause, cancellation);
        }

        return (true, "every index's history written again: " + string.Join("; ", counted));
    }

    // An index's history read for the build through the span's end: the calendar, the members' series as they stood,
    // the closes each path replays over with the bar store's after the span's end, the members whose prices stopped,
    // the sessions each row's end is dated by, the market's, the context's and the heavyweights' inputs, and what the
    // build reads past the span's end. Once the bar store no longer holds the span's end, no path is replayed past it,
    // and the words say so.
    async Task<PreparedHistory> PrepareAsync(SqliteConnection connection, string index, DateOnly through, TextWriter output, CancellationToken cancellation, bool heavyweightsRead = true)
    {
        var history = new SweepHistory(databaseFile);

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
        var (closes, opens, stopped, after, extended) = await ClosesAsync(connection, series, through, cancellation);
        var past = after.Length == 0
            ? $"the bar store holding no close after {Stamp(through)}"
            : FormattableString.Invariant($"{extended} name(s) replayed past {Stamp(through)} over the bar store's closes through {Stamp(after[^1])}");

        output.WriteLine($"ledger-build: {past}");

        var market = await MarketAsync(connection, PulledMarketBars, through, cancellation);
        var sectors = await SectorsAsync(connection, PulledSectors, inputs.Names, cancellation);
        var facts = await FactsAsync(connection, [PulledFilers, Filers], inputs.Names.Select(name => name.Ticker), cancellation);
        var heavyweights = index == IndexFamilies.LargeIndex && heavyweightsRead
            ? await HeavyweightHistoryAsync(history, inputs, through, output, cancellation)
            : new Dictionary<int, IReadOnlyList<HeavyweightCut>>();
        var insiders = await InsidersAsync(connection, [PulledFilers, Filers], inputs.Names.Select(name => name.Ticker), FirstInsiderDay(calendar), cancellation);
        var context = new LedgerContext(income, sectors, highs, lows, facts)
        {
            Ranks = new FactorRanks(series, members),
            Insiders = insiders.Rows,
            InsiderSpans = insiders.Spans,
        };

        return new PreparedHistory(index, calendar, series, sessions, members, income, closes, opens, stopped, [.. calendar, .. after], past, market, context, heavyweights);
    }

    // One session the history build computed: each family's setups with each one's path and benchmark beside it.
    sealed record BuiltSession(IReadOnlyList<(FamilySetups Family, IReadOnlyList<SetupRead> Reads)> Families);

    sealed partial class PreparedHistory(
        string index,
        DateOnly[] calendar,
        SweepSeries[] series,
        SweepColumns.Session[] sessions,
        SweepBenchmark.Members members,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        double[][] closes,
        double[][] opens,
        bool[] stopped,
        DateOnly[] replayed,
        string past,
        LedgerMarket market,
        LedgerContext context,
        IReadOnlyDictionary<int, IReadOnlyList<HeavyweightCut>> heavyweights)
    {
        readonly Dictionary<string, int> nameOf = series
            .Select((one, name) => (one.Name.Ticker, name))
            .DistinctBy(pair => pair.Ticker, StringComparer.Ordinal)
            .ToDictionary(pair => pair.Ticker, pair => pair.name, StringComparer.Ordinal);

        public string Index => index;

        public DateOnly[] Calendar => calendar;

        public DateOnly[] Replayed => replayed;

        public string Past => past;

        public SweepSeries[] Series => series;

        public SweepBenchmark.Members Members => members;

        public double[][] Closes => closes;

        public double[][] Opens => opens;

        public bool[] Stopped => stopped;

        public IReadOnlyDictionary<int, IReadOnlyList<HeavyweightCut>> Heavyweights => heavyweights;

        public IReadOnlyDictionary<string, int> Places => nameOf;

        // One session's setups on every family, each replayed to its end with its benchmark; on a rebalance of the S&P
        // 500's heavyweights, each member of each sector's size cut held as the rule holds a buy, to the first later
        // rebalance whose buys leave it out or its cap, against its size cut.
        public BuiltSession Compute(int at)
        {
            var families = new List<(FamilySetups, IReadOnlyList<SetupRead>)>();
            var inputsOnTheNight = new IndexNightInputs(index, calendar[at], calendar, at, series, sessions, members, members.Names[at], income);

            foreach (var family in LedgerSetups.On(index, series, sessions, members, calendar, at, market, context))
            {
                families.Add((family, [.. family.Rows.Select(row =>
                {
                    var name = NameOf(series, row.Ticker);

                    return Outcome(closes, opens, inputsOnTheNight, name, IndexNightRead.BarOf(series[name], at), row.Anchor, row.Plan, stopped);
                })]));
            }

            if (heavyweights.TryGetValue(at, out var cuts))
            {
                var family = LedgerSetups.Heavyweights(index, series, closes, sessions, members, calendar, at, market, context, cuts);

                families.Add((family, [.. family.Rows.Select(row =>
                {
                    var name = nameOf[row.Ticker];
                    var bar = IndexNightRead.BarOf(series[name], at);
                    var notBought = heavyweights
                        .Where(pair => pair.Key > at && !pair.Value.Any(cut => cut.Leaders.Contains(row.Ticker)))
                        .Select(pair => IndexNightRead.BarOf(series[name], pair.Key))
                        .Where(held => held >= 0)
                        .ToArray();
                    var outcome = HeavyweightPaths.Replay(closes[name], bar, notBought);
                    var cut = cuts
                        .First(one => one.Largest.Contains(row.Ticker, StringComparer.Ordinal)).Largest
                        .Where(nameOf.ContainsKey)
                        .Select(ticker => nameOf[ticker])
                        .ToArray();

                    return HeavyweightRead(
                        row.Anchor,
                        outcome,
                        [.. cut.Select(member => (closes[member], IndexNightRead.BarOf(series[member], at), stopped[member]))],
                        [.. cut.Select(member => (closes[member], opens[member], IndexNightRead.BarOf(series[member], at)))],
                        closes[name],
                        opens[name],
                        bar);
                })]));
            }

            return new BuiltSession(families);
        }
    }

    // An index's history written over the sessions given, a chunk of sessions at a time, each chunk waiting for the
    // night and holding the drain's lock while it replaces those sessions' history rows, then the index's summary and
    // the build's row on the run log.
    async Task WriteAsync(string dataRoot, SqliteConnection connection, PreparedHistory prepared, int first, int last, IReadOnlyList<int> sessionsToWrite, Func<int, BuiltSession> session, DateTimeOffset startedAt, TextWriter output, Func<TimeSpan, CancellationToken, Task> pause, CancellationToken cancellation)
    {
        var index = prepared.Index;
        var calendar = prepared.Calendar;
        var runId = FormattableString.Invariant($"{BuildStage}-{index}-{startedAt:yyyyMMddTHHmmssZ}");
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
                foreach (var (family, reads) in session(at).Families)
                {
                    for (var place = 0; place < family.Rows.Count; place++)
                    {
                        await InsertAsync(connection, transaction, family.Rows[place], null, null, reads[place], prepared.Replayed, at, HistorySource, cancellation);
                    }

                    await ExecuteAsync(connection, transaction, InsertNight,
                    [
                        ("$index", index), ("$family", family.Family), ("$session", Stamp(calendar[at])), ("$members", family.Members),
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

        using (await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation))
        {
            await RefreshSummaryAsync(connection, index, cancellation);
        }

        await AppendAsync(connection, runId, BuildStage, startedAt, written, Ok,
            FormattableString.Invariant($"{written} setup(s) on the {DecisionCards.NameOf(index)} over {Stamp(calendar[first])} to {Stamp(calendar[last])}, {passes} passing the live rule, {sessionsToWrite.Count} session(s) written of the {last - first + 1} in the span, {prepared.Past}"), cancellation);

        output.WriteLine(FormattableString.Invariant($"ledger-build: {written} setup(s) written for the {DecisionCards.NameOf(index)} over {sessionsToWrite.Count} session(s), {passes} passing the live rule"));
    }

    public const string CheckStage = "ledger-check";

    // The history setups a year the point-in-time check samples, and the seed it samples by, so a check run again
    // over the same ledger reads the same setups.
    public const int CheckPerYear = 25;

    public const int CheckSeed = 17;

    static readonly string HistoryReadings =
        "SELECT family, ticker, session_date, " + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM setup WHERE index_code = $index AND source = $source ORDER BY session_date, family, ticker;";

    // The newest session the history build read on the index, which the check reads the history through.
    const string HistoryEnd = "SELECT MAX(session_date) FROM setup_night WHERE index_code = $index AND source = $source;";

    // The first and the newest sessions the history build wrote on the index.
    const string HistorySpan = "SELECT MIN(session_date), MAX(session_date) FROM setup_night WHERE index_code = $index AND source = $source;";

    // The span the history build wrote on the index, which writing it again covers, none where it wrote none.
    public async Task<(DateOnly From, DateOnly Through)?> HistorySpanAsync(string index, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using var command = Command(connection, null, HistorySpan, [("$index", index), ("$source", HistorySource)]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation) && !reader.IsDBNull(0) && !reader.IsDBNull(1)
            ? (Date(reader.GetString(0)), Date(reader.GetString(1)))
            : null;
    }

    // The point-in-time check by hand: a seeded sample of each year's history setups on the index, each one's readings
    // rebuilt from the history read through the build's own end and cut at its own session, a swing setup's own readings
    // by its family's gates there, and held to the readings stored, every reading that differs named. It reads the store
    // and writes its run log row alone.
    public async Task<int> CheckAsync(string index, TextWriter output, int perYear = CheckPerYear, int seed = CheckSeed, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var runId = FormattableString.Invariant($"{CheckStage}-{index}-{startedAt:yyyyMMddTHHmmssZ}");

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var stored = new List<(string Family, string Ticker, DateOnly Session, double?[] Readings)>();

        await using (var command = Command(connection, null, HistoryReadings, [("$index", index), ("$source", HistorySource)]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var readings = new double?[LedgerReadings.Count];

                for (var at = 0; at < readings.Length; at++)
                {
                    readings[at] = reader.IsDBNull(3 + at) ? null : reader.GetDouble(3 + at);
                }

                stored.Add((reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)), readings));
            }
        }

        if (stored.Count == 0)
        {
            output.WriteLine($"ledger-check: the ledger holds no history setup on the {DecisionCards.NameOf(index)}, so nothing was checked");
            await AppendAsync(connection, runId, CheckStage, startedAt, 0, Ok, "no history setup to check", cancellation);

            return 0;
        }

        var random = new Random(seed);
        var sample = stored
            .GroupBy(row => row.Session.Year)
            .OrderBy(year => year.Key)
            .SelectMany(year => year.Select(row => (Row: row, Order: random.Next())).OrderBy(pair => pair.Order).Take(perYear).Select(pair => pair.Row))
            .ToArray();
        var through = HistoryThrough(await ScalarAsync(connection, HistoryEnd, [("$index", index), ("$source", HistorySource)], cancellation) as string, sample.Max(row => row.Session));
        var history = new SweepHistory(databaseFile);
        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation, index: index == IndexFamilies.LargeIndex ? null : index, asItStood: index != IndexFamilies.LargeIndex);
        var income = await history.IncomeAsync(through, cancellation);
        var market = await MarketAsync(connection, PulledMarketBars, through, cancellation);
        var sectors = await SectorsAsync(connection, PulledSectors, inputs.Names, cancellation);
        var facts = await FactsAsync(connection, [PulledFilers, Filers], inputs.Names.Select(name => name.Ticker), cancellation);
        var insiders = await InsidersAsync(connection, [PulledFilers, Filers], inputs.Names.Select(name => name.Ticker), FirstInsiderDay(inputs.Sessions), cancellation);
        var findings = new List<string>();
        var read = 0;

        foreach (var session in sample.GroupBy(row => row.Session).OrderBy(group => group.Key))
        {
            var cut = LedgerPointInTime.Cut(inputs, session.Key);
            var cutMarket = LedgerPointInTime.Cut(market, session.Key);

            foreach (var row in session)
            {
                var (held, rebuilt) = LedgerPointInTime.Setup(index, cut, cutMarket, income, sectors, facts, row.Family, row.Ticker, insiders);

                if (!held)
                {
                    findings.Add($"{row.Family} {row.Ticker} {Stamp(row.Session)}: the cut history holds no bar of it on its session");

                    continue;
                }

                if (rebuilt is null)
                {
                    findings.Add($"{row.Family} {row.Ticker} {Stamp(row.Session)}: the family's loose gates over the cut history pass no setup of it on its session");

                    continue;
                }

                read++;
                findings.AddRange(LedgerPointInTime.Differences(row.Readings, rebuilt).Select(differ =>
                    FormattableString.Invariant($"{row.Family} {row.Ticker} {Stamp(row.Session)}: {differ.Reading} stored {Shown(differ.Stored)}, rebuilt {Shown(differ.Rebuilt)}")));
            }
        }

        foreach (var finding in findings)
        {
            output.WriteLine("ledger-check: " + finding);
        }

        var detail = FormattableString.Invariant($"{read} of {sample.Length} sampled history setup(s) on the {DecisionCards.NameOf(index)} rebuilt from the history read through {Stamp(through)} and cut at each one's session, {perYear} a year by seed {seed}; ")
            + (findings.Count == 0 ? "every reading the same" : FormattableString.Invariant($"{findings.Count} difference(s), each named on the command's output"));

        await AppendAsync(connection, runId, CheckStage, startedAt, 0, findings.Count == 0 ? Ok : NotComputed, detail, cancellation);
        output.WriteLine("ledger-check: " + detail);

        return findings.Count == 0 ? 0 : 1;
    }

    static string Shown(double? reading) => reading is { } held ? held.ToString("R", CultureInfo.InvariantCulture) : "none";

    // The end the check reads the history to: the newest session the history build read on the index, so the pulled
    // years are scaled onto the stored closes as the build scaled them, and the newest setup sampled where the store
    // records no build reaching it.
    public static DateOnly HistoryThrough(string? built, DateOnly sampled) =>
        built is not null && Date(built) is var end && end >= sampled ? end : sampled;

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

    const string StoredSpans = "SELECT ticker, MIN(session_date), MAX(session_date) FROM bar GROUP BY ticker;";

    const string StoredAfter = "SELECT ticker, session_date, close, open FROM bar WHERE session_date > $through ORDER BY ticker, session_date;";

    // Each name's closes over the history, then the bar store's own after the span's end through its newest session,
    // where the history's closes end in the store's own bars and so on its scale, with the sessions those closes stand
    // on; and the names whose prices stopped before that newest session, a takeover's, a delisting's or a member no index
    // the night reads holds any longer, whose path still open the benchmark leaves out. A name the store holds a later
    // close for has not stopped, whatever the history reads of it.
    // see: The history's setups are settled by the build alone, which replays them over the store's own prices after its span
    // see: A setup's benchmark leaves out a member whose prices stop before its trade ends, as the sweeps do
    static async Task<(double[][] Closes, double[][] Opens, bool[] Stopped, DateOnly[] After, int Extended)> ClosesAsync(SqliteConnection connection, IReadOnlyList<SweepSeries> series, DateOnly through, CancellationToken cancellation)
    {
        var spans = new Dictionary<string, (DateOnly First, DateOnly Last)>(StringComparer.Ordinal);
        var after = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var afterOpens = new Dictionary<string, List<double>>(StringComparer.Ordinal);
        var sessions = new SortedSet<DateOnly>();

        await using (var command = Command(connection, null, StoredSpans, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                spans[reader.GetString(0)] = (Date(reader.GetString(1)), Date(reader.GetString(2)));
            }
        }

        await using (var command = Command(connection, null, StoredAfter, [("$through", Stamp(through))]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                var ticker = reader.GetString(0);

                if (!after.TryGetValue(ticker, out var held))
                {
                    after[ticker] = held = [];
                    afterOpens[ticker] = [];
                }

                sessions.Add(Date(reader.GetString(1)));
                held.Add(Statistic.FromPrice(Money.FromStorage(reader.GetString(2))));
                afterOpens[ticker].Add(Statistic.FromPrice(Money.FromStorage(reader.GetString(3))));
            }
        }

        var newest = spans.Values.Select(span => span.Last).Append(through).Max();
        var closes = new double[series.Count][];
        var opens = new double[series.Count][];
        var stopped = new bool[series.Count];
        var extended = 0;

        for (var name = 0; name < series.Count; name++)
        {
            var ticker = series[name].Name.Ticker;
            var history = series[name].Bars.Select(bar => Statistic.FromPrice(bar.Close));
            var stored = spans.TryGetValue(ticker, out var span) ? span : ((DateOnly First, DateOnly Last)?)null;
            var extends = stored is { } held && held.First <= through && after.ContainsKey(ticker);

            closes[name] = extends ? [.. history, .. after[ticker]] : [.. history];
            opens[name] = extends ? [.. series[name].Bars.Select(bar => Statistic.FromPrice(bar.Open)), .. afterOpens[ticker]] : [.. series[name].Bars.Select(bar => Statistic.FromPrice(bar.Open))];
            extended += extends ? 1 : 0;

            var last = series[name].Bars.Length > 0 ? series[name].Bars[^1].Session : DateOnly.MinValue;

            stopped[name] = (stored is { } kept && kept.Last > last ? kept.Last : last) < newest;
        }

        return (closes, opens, stopped, [.. sessions], extended);
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
        SetupRead outcome,
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
            .. FillParameters(outcome),
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

        await using var command = Command(connection, null, OpenSetups, [("$index", index), ("$source", NightSource), ("$night", Stamp(night))]);
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

    // Each ticker's insiders' transactions the four readings read, through its filer as the facts are read and filed on or
    // after the day given, the columns the readings read alone; and the spans the store's insider history holds.
    // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
    static async Task<LedgerInsiders> InsidersAsync(
        SqliteConnection connection,
        IReadOnlyList<string> filerQueries,
        IEnumerable<string> tickers,
        DateOnly from,
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

        var wanted = tickers.Distinct(StringComparer.Ordinal).Where(filerOf.ContainsKey).ToArray();
        var ciks = wanted.Select(ticker => filerOf[ticker]).ToHashSet(StringComparer.Ordinal);
        var byFiler = new Dictionary<string, List<InsiderRow>>(StringComparer.Ordinal);

        await using (var command = Command(connection, null, InsiderRowsFrom, [("$from", Stamp(from))]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var cik = reader.GetString(0);

                if (!ciks.Contains(cik))
                {
                    continue;
                }

                if (!byFiler.TryGetValue(cik, out var rows))
                {
                    byFiler[cik] = rows = [];
                }

                rows.Add(new InsiderRow(
                    reader.GetString(1),
                    Date(reader.GetString(2)),
                    reader.GetString(3),
                    reader.IsDBNull(4) ? null : reader.GetString(4),
                    reader.IsDBNull(5) ? null : Date(reader.GetString(5)),
                    reader.GetString(6),
                    string.Empty,
                    false,
                    reader.GetInt64(7) == 1,
                    null,
                    false,
                    reader.GetInt32(8),
                    Date(reader.GetString(9)),
                    reader.GetString(10),
                    Money.FromStorage(reader.GetString(11)),
                    reader.IsDBNull(12) ? null : Money.FromStorage(reader.GetString(12)),
                    reader.GetInt64(13) == 1,
                    reader.IsDBNull(14) ? null : reader.GetInt64(14) == 1,
                    reader.GetInt64(15) == 1));
            }
        }

        var spans = new List<(DateOnly First, DateOnly Last)>();

        await using (var command = Command(connection, null, InsiderSpansRead, []))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                spans.Add((Date(reader.GetString(0)), Date(reader.GetString(1))));
            }
        }

        return new LedgerInsiders(
            wanted.ToDictionary(ticker => ticker, ticker => (IReadOnlyList<InsiderRow>)(byFiler.GetValueOrDefault(filerOf[ticker]) ?? []), StringComparer.Ordinal),
            new InsiderCoverage(spans));
    }

    // The business readings and the valuation reading of the night's setups read again for the tickers given, once the
    // filings refresh has stored what the archive posted before the session; the night's rows alone, each from the facts
    // filed before the setup's session and the valuation reading over the closes the night's step read. It returns how
    // many rows it read again.
    public async Task<int> BusinessAgainAsync(DateOnly session, IReadOnlyList<string> tickers, CancellationToken cancellation = default)
    {
        if (tickers.Count == 0)
        {
            return 0;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var facts = await FactsAsync(connection, [Filers], tickers, cancellation);
        var places = new Dictionary<string, double?>(StringComparer.Ordinal);

        foreach (var ticker in tickers.Distinct(StringComparer.Ordinal))
        {
            places[ticker] = await ValuePlaceAsync(connection, ticker, facts.GetValueOrDefault(ticker) ?? [], session, cancellation);
        }

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
                ("$value_place", (object?)places[ticker] ?? DBNull.Value),
            ]);

            read += await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return read;
    }

    // A member's valuation reading on the night over its stored closes and those before its year, as the night's step
    // reads it.
    static async Task<double?> ValuePlaceAsync(SqliteConnection connection, string ticker, IReadOnlyList<FiledFactRow> facts, DateOnly night, CancellationToken cancellation) =>
        await PricesOnAsync(connection, ticker, facts, night, cancellation) is { } prices
            ? ValuationReading.Place(ValuationReading.PartsOf(facts, night), prices, night)
            : null;

    // The four insider readings of the night's setups and of every member's readings on the night read again, once the
    // filings refresh has stored the session before's filings and recorded its day: each from the filings filed before
    // the session over the company's value as the night's step reads it, from its stored closes and those before its year.
    // It returns how many rows it read again.
    // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
    public async Task<int> InsidersAgainAsync(DateOnly session, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var tickers = new SortedSet<string>(StringComparer.Ordinal);
        var memberRows = new List<(string Index, string Ticker, string Readings)>();

        await using (var command = Command(connection, null, NightsSetupTickers, [("$session", Stamp(session)), ("$source", NightSource)]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                tickers.Add(reader.GetString(0));
            }
        }

        await using (var command = Command(connection, null, NightsReadings, [("$session", Stamp(session))]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                memberRows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2)));
                tickers.Add(reader.GetString(1));
            }
        }

        if (tickers.Count == 0)
        {
            return 0;
        }

        var facts = await FactsAsync(connection, [Filers], tickers, cancellation);
        var insiders = await InsidersAsync(connection, [Filers], tickers, session.AddDays(-InsiderReadings.WindowDays), cancellation);
        var four = new Dictionary<string, InsiderFour>(StringComparer.Ordinal);

        foreach (var ticker in tickers)
        {
            var filed = facts.GetValueOrDefault(ticker) ?? [];
            var prices = await PricesOnAsync(connection, ticker, filed, session, cancellation);
            var parts = ValuationReading.PartsOf(filed, session);

            // A member the store matches no filer for holds no entry and reads none, where a filer filing nothing reads nought.
            four[ticker] = insiders.Rows.GetValueOrDefault(ticker) is { } rows
                ? InsiderReadings.Read(rows, insiders.Spans, session, dollars => prices is null ? null : ValuationReading.OverValue(dollars, parts, prices, session))
                : InsiderFour.None;
        }

        var columns = new[] { "insider_net_buy", "insider_buyers", "insider_officer_buy", "insider_unplanned_sale" }.Select(LedgerReadings.IndexOf).ToArray();
        var read = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var (ticker, readings) in four)
        {
            await using var command = Command(connection, transaction, InsidersAgain,
            [
                ("$ticker", ticker),
                ("$session", Stamp(session)),
                ("$source", NightSource),
                ("$insider_net_buy", (object?)readings.NetBuy ?? DBNull.Value),
                ("$insider_buyers", (object?)readings.Buyers ?? DBNull.Value),
                ("$insider_officer_buy", (object?)readings.OfficerBuy ?? DBNull.Value),
                ("$insider_unplanned_sale", (object?)readings.UnplannedSale ?? DBNull.Value),
            ]);

            read += await command.ExecuteNonQueryAsync(cancellation);
        }

        foreach (var (index, ticker, stored) in memberRows)
        {
            if (System.Text.Json.JsonSerializer.Deserialize<double?[]>(stored) is not { Length: var length } values || length != LedgerReadings.Count)
            {
                continue;
            }

            var readings = four[ticker];

            (values[columns[0]], values[columns[1]], values[columns[2]], values[columns[3]]) = (readings.NetBuy, readings.Buyers, readings.OfficerBuy, readings.UnplannedSale);

            await using var command = Command(connection, transaction, ReadingsAgain,
            [
                ("$index", index),
                ("$ticker", ticker),
                ("$session", Stamp(session)),
                ("$readings", System.Text.Json.JsonSerializer.Serialize(values)),
            ]);

            read += await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return read;
    }

    // A member's closes on the night and those before its year that its valuation reading reaches, as the night's step
    // reads them; none where the store holds no bar of it.
    static async Task<PriceHistory?> PricesOnAsync(SqliteConnection connection, string ticker, IReadOnlyList<FiledFactRow> facts, DateOnly night, CancellationToken cancellation)
    {
        var bars = new List<SweepBar>();

        await using (var command = Command(connection, null, StoredCloses, [("$ticker", ticker), ("$night", Stamp(night))]))
        {
            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                bars.Add(new SweepBar(Date(reader.GetString(0)), 0m, 0m, Money.FromStorage(reader.GetString(1)), 0, RawClose: reader.IsDBNull(2) ? 0m : Money.FromStorage(reader.GetString(2))));
            }
        }

        if (bars.Count == 0)
        {
            return null;
        }

        var before = await BeforeTheYearAsync(connection, [(ticker, bars[0].Session, bars[0].Close)], new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal) { [ticker] = facts }, night, cancellation);

        return (before.GetValueOrDefault(ticker) ?? PriceHistory.None).Then(PriceHistory.Of(bars));
    }

    // Each name's first stored session and its close there.
    static IEnumerable<(string Ticker, DateOnly First, decimal Close)> FirstSessions(IReadOnlyList<SweepName> names) =>
        names.Where(name => name.Bars.Length > 0).Select(name => (name.Ticker, name.Bars[0].Session, name.Bars[0].Close));

    // Each member's closes before its first stored session that its valuation reading reaches on the night: the sessions
    // of the newest pull holding that first session, from the newest on or before the end of the oldest of the reading's
    // twelve quarters, each close brought to the store's scale by the ratio of the stored close to the pulled close on
    // the first session as the chart averages step brings them, and each close as traded as the pull holds it. A member
    // whose reading reaches no session before its year, or whose first session no pull holds, reads none; no other
    // reading reads them.
    // see: The valuation reading reads the prices before the store's year at the store's scale from the history pulled for those years
    static async Task<IReadOnlyDictionary<string, PriceHistory>> BeforeTheYearAsync(
        SqliteConnection connection,
        IEnumerable<(string Ticker, DateOnly First, decimal Close)> names,
        IReadOnlyDictionary<string, IReadOnlyList<FiledFactRow>> facts,
        DateOnly night,
        CancellationToken cancellation)
    {
        var before = new Dictionary<string, PriceHistory>(StringComparer.Ordinal);

        foreach (var (ticker, first, firstClose) in names)
        {
            if (ValuationReading.OldestDay(ValuationReading.PartsOf(facts.GetValueOrDefault(ticker) ?? [], night)) is not { } oldest || oldest >= first)
            {
                continue;
            }

            string? pull = null;
            var pulledClose = 0m;

            await using (var command = Command(connection, null, PullHoldingFirst, [("$ticker", ticker), ("$first", Stamp(first))]))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellation);

                if (await reader.ReadAsync(cancellation))
                {
                    pull = reader.GetString(0);
                    pulledClose = Money.FromStorage(reader.GetString(1));
                }
            }

            if (pull is null || pulledClose <= 0m)
            {
                continue;
            }

            var ratio = firstClose / pulledClose;
            var (sessions, closes, traded) = (new List<DateOnly>(), new List<double>(), new List<double>());

            await using (var command = Command(connection, null, PulledBeforeTheYear, [("$ticker", ticker), ("$pull", pull), ("$first", Stamp(first)), ("$oldest", Stamp(oldest))]))
            {
                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    sessions.Add(Date(reader.GetString(0)));
                    closes.Add(Statistic.FromPrice(Money.FromStorage(reader.GetString(1)) * ratio));
                    traded.Add(reader.IsDBNull(2) ? 0 : Statistic.FromPrice(Money.FromStorage(reader.GetString(2))));
                }
            }

            before[ticker] = new PriceHistory([.. sessions], [.. closes], [.. traded]);
        }

        return before;
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
