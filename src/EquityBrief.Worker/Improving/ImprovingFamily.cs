using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Improving;

// What the Fundamentals family's step did on one index: the members it read, how many held a scorecard read, how many
// newly read filings it read and how many of them raised the score, what it bought and sold, how many it flagged, how many
// trades it holds after the night and how many buys wait for their next session, or the failure that stopped its part.
public sealed record ImprovingIndexOutcome(string Index, int Members, int Scored, int FirstRead, int Improved, IReadOnlyList<string> Bought, int Flagged, IReadOnlyList<string> Sold, int Held, int Waiting, string? Fault = null);

// What the step did on the night, an index at a time.
public sealed record ImprovingOutcome(DateOnly? Night, IReadOnlyList<ImprovingIndexOutcome> Indices);

// The Fundamentals family's part of the night, a step of its own after Quality at a fair price's, over every member of the
// S&P 500, 400 and 600 holding a bar on the night. It reads each member's scorecard as the night's last scoring stored it;
// for a member whose newest filing it reads for the first time, the score its previous filing held, worked through the
// core's one scorecard over the filings as first filed before that filing; its sales growth and how much faster it grew
// than the quarter before as the ledger's readings stored them; and its newest results release through the one reader of
// the release readings. It stores every member's answer with the figures that decided it and its growth carried by the
// store's closes; buys each member a newly read filing lifted to the bar at its next session's open through the core's one
// pricing function, one trade a stock; sells a trade at the close of the night its score falls under the hold floor, at
// the close of its last session and at its last close as a member; and flags a member whose release since its newest
// filing reads better, buying nothing on it. It reads no pulled history, no chart setup and no market check, makes no
// request, calls no model and writes only its own tables. Each index's part is one transaction, so a failure in it leaves
// none of its writes and is named on its row while the other indices go on.
// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
// see: A Fundamentals trade is held while its score stays at seven in nine, and judged against an equal-weighted hold of its index's members after its round trip
// see: The nightly run is arithmetic only
public sealed class ImprovingFamily : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.FundamentalScore, Touch.Read),
            new StoreTouch(Store.LedgerReading, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read),
            new StoreTouch(Store.ReleaseReading, Touch.Read),
            new StoreTouch(Store.ReadingPin, Touch.Read),
            new StoreTouch(Store.ImprovingNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.ImprovingResult, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.ImprovingTrade, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "improving";

    public const string Ok = "ok";

    public const string NotComputed = "not computed";

    // The rule the trades are kept under before a freeze registers one.
    public const string ProvisionalRule = "provisional";

    // What became of a member on the night and why a trade ended, in the rule's words.
    const string BoughtStatus = ImprovingRule.Bought;
    const string ImprovedStatus = ImprovingRule.Improved;
    const string FlaggedStatus = ImprovingRule.Flagged;
    const string HeldStatus = ImprovingRule.Held;
    const string ScoreFell = ImprovingRule.ScoreFell;
    const string TimeLimit = ImprovingRule.TimeLimit;
    const string LeftTheIndex = ImprovingRule.LeftTheIndex;

    // The indices the step reads, in the page's order of indices.
    public static IReadOnlyList<string> AllIndices { get; } = [ImprovingRule.LargeIndex, FundHoldings.MidCapIndex, FundHoldings.SmallCapIndex];

    static string Cause(Exception failure) => $"{failure.GetType().Name}: {failure.Message}";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string MembersOn = @"
        SELECT DISTINCT ticker FROM membership
        WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string Bars = "SELECT ticker, session_date, open, close, raw_close FROM bar WHERE session_date <= $night ORDER BY ticker, session_date;";

    const string ScoresOn = "SELECT ticker, quarter, financial, passed, applied, checks FROM fundamental_score WHERE index_code = $index AND session_date = $night;";

    const string ReadingsOn = "SELECT ticker, readings FROM ledger_reading WHERE index_code = $index AND session_date = $night;";

    const string ValueOn = "SELECT company_value FROM member_reading WHERE index_code = $index AND session_date = $session AND ticker = $ticker;";

    // Each ticker's filer as its newest company row files it.
    const string Filers = @"
        SELECT c.ticker, c.cik FROM company c
        WHERE c.cik IS NOT NULL AND c.fetched_at = (SELECT MAX(fetched_at) FROM company WHERE ticker = c.ticker);
    ";

    const string FactsOfFiler = "SELECT concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE cik = $cik;";

    // The newest night before this one the step read without a failure.
    const string NightBefore = "SELECT MAX(session_date) FROM improving_night WHERE rule = $rule AND index_code = $index AND session_date < $night AND fault IS NULL;";

    // Every member's carried growth on a night.
    const string CarriedOn = "SELECT ticker, carried FROM improving_result WHERE rule = $rule AND index_code = $index AND session_date = $session;";

    // A member's newest answer on or before a night, for a member the night before holds none for.
    const string NewestCarried = @"
        SELECT session_date, carried FROM improving_result
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND session_date <= $session
        ORDER BY session_date DESC LIMIT 1;
    ";

    const string ClearTheNight = @"
        DELETE FROM improving_night WHERE rule = $rule AND index_code = $index AND session_date = $night;
        DELETE FROM improving_result WHERE rule = $rule AND index_code = $index AND session_date = $night;
        DELETE FROM improving_trade WHERE rule = $rule AND index_code = $index AND chosen_on = $night;
        UPDATE improving_trade SET ended_on = NULL, sale = NULL, end_reason = 'open', result = NULL, benchmark = NULL, cost = NULL, edge = NULL
        WHERE rule = $rule AND index_code = $index AND ended_on = $night;
    ";

    const string OpenTrades = @"
        SELECT ticker, chosen_on, buy_close, bought_on, fill, gap, bench_gap, growth, through
        FROM improving_trade WHERE rule = $rule AND index_code = $index AND end_reason = 'open'
        ORDER BY chosen_on, ticker;
    ";

    const string InsertNight = @"
        INSERT INTO improving_night (rule, index_code, session_date, members, scored, first_read, improved, bought, flagged, held, settings, fault)
        VALUES ($rule, $index, $night, $members, $scored, $first_read, $improved, $bought, $flagged, $held, $settings, NULL);
    ";

    const string InsertNotComputed = @"
        INSERT OR IGNORE INTO improving_night (rule, index_code, session_date, members, scored, first_read, improved, bought, flagged, held, settings, fault)
        VALUES ($rule, $index, $night, 0, 0, 0, 0, 0, 0, 0, $settings, $fault);
    ";

    const string InsertResult = @"
        INSERT INTO improving_result (
            rule, index_code, session_date, ticker, passed_checks, applied, filed, first_read, before_passed, before_applied,
            before_checks, growth, growth_change, released, outlook, flagged, status, reason, carried)
        VALUES (
            $rule, $index, $night, $ticker, $passed_checks, $applied, $filed, $first_read, $before_passed, $before_applied,
            $before_checks, $growth, $growth_change, $released, $outlook, $flagged, $status, $reason, $carried);
    ";

    const string InsertTrade = @"
        INSERT INTO improving_trade (
            rule, index_code, ticker, chosen_on, filed, released, passed_checks, applied, before_passed, before_applied,
            buy_close, growth, through, sessions, end_reason)
        VALUES (
            $rule, $index, $ticker, $night, $filed, $released, $passed_checks, $applied, $before_passed, $before_applied,
            $buy_close, 1.0, $night, 0, 'open');
    ";

    const string CarryTrade = @"
        UPDATE improving_trade SET growth = $growth, through = $through, sessions = $sessions
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND chosen_on = $chosen_on;
    ";

    const string FillTrade = @"
        UPDATE improving_trade SET bought_on = $bought_on, fill = $fill, gap = $gap, bench_gap = $bench_gap, members = $members
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND chosen_on = $chosen_on;
    ";

    const string EndTrade = @"
        UPDATE improving_trade SET ended_on = $ended_on, sale = $sale, end_reason = $end, result = $result, benchmark = $benchmark, cost = $cost, edge = $edge
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND chosen_on = $chosen_on;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    // One stored bar as the step reads it: its open, its close and its close as traded, the first two at the store's scale.
    sealed record Daily(decimal Open, decimal Close, decimal Raw);

    // One member's answer on the night.
    sealed record Answered(
        string Ticker,
        int Passed,
        int Applied,
        DateOnly? Quarter,
        DateOnly? Filed,
        bool FirstRead,
        Scorecard? Before,
        double? Growth,
        double? GrowthChange,
        DateOnly? Released,
        string? Outlook,
        bool Flagged,
        ImprovingAnswer? Answer,
        double Carried);

    // One open trade as stored.
    sealed record Trade(string Ticker, DateOnly ChosenOn, decimal BuyClose, DateOnly? BoughtOn, decimal? Fill, double? Gap, double? BenchGap, double Growth, DateOnly Through);

    readonly IClock clock;
    readonly string databaseFile;

    public ImprovingFamily(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The step over the three indices, each index's failure named on its own row while the others go on, and a failure
    // outside every index's part named on the step's row: the family never stops the night.
    public async Task<ImprovingOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var outcomes = new List<ImprovingIndexOutcome>();
        DateOnly? read = null;
        var rows = 0;
        var settings = ImprovingRule.Starting;

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            if (await ScalarAsync(connection, null, NewestSession, [], cancellation) is not string newest)
            {
                await AppendAsync(connection, runId, startedAt, 0, Ok, "no session: the store holds no bar", cancellation);

                return new ImprovingOutcome(null, []);
            }

            var night = Date(newest);
            var bars = await BarsAsync(connection, night, cancellation);
            var filers = await FilersAsync(connection, cancellation);
            var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal);

            read = night;

            foreach (var index in AllIndices)
            {
                try
                {
                    var (outcome, written) = await IndexNightAsync(connection, index, night, bars, filers, facts, ProvisionalRule, settings, cancellation);

                    outcomes.Add(outcome);
                    rows += written;
                }
                catch (Exception failure) when (!cancellation.IsCancellationRequested)
                {
                    var fault = Cause(failure);

                    rows += await NotComputedAsync(connection, index, night, fault, ProvisionalRule, settings, cancellation);
                    outcomes.Add(new ImprovingIndexOutcome(index, 0, 0, 0, 0, [], 0, [], 0, 0, fault));
                }
            }

            await AppendAsync(connection, runId, startedAt, rows, outcomes.Any(outcome => outcome.Fault is not null) ? NotComputed : Ok, Detail(outcomes), cancellation);

            return new ImprovingOutcome(night, outcomes);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            var fault = Cause(failure);
            ImprovingIndexOutcome[] all =
            [
                .. outcomes,
                .. AllIndices.Where(index => outcomes.All(outcome => outcome.Index != index)).Select(index => new ImprovingIndexOutcome(index, 0, 0, 0, 0, [], 0, [], 0, 0, fault)),
            ];

            await AppendAfterAFailureAsync(runId, startedAt, rows, Detail(all) + "; the step stopped on " + fault, cancellation);

            return new ImprovingOutcome(read, all);
        }
    }

    // What the step's row says for each index.
    public static string Detail(IReadOnlyList<ImprovingIndexOutcome> outcomes) =>
        string.Join("; ", outcomes.Select(outcome => outcome.Fault is { } fault
            ? $"{outcome.Index}: not computed tonight, {fault}"
            : FormattableString.Invariant(
                $"{outcome.Index}: {outcome.Members} member(s) read, {outcome.Scored} scored, {outcome.FirstRead} newly read filing(s), {outcome.Improved} raising the score, {outcome.Bought.Count} bought, {outcome.Flagged} flagged, {outcome.Sold.Count} sold, {outcome.Held} held, {outcome.Waiting} waiting for their next session")));

    // The settings the night read the rule by, and the words its card states the rule in.
    public static string Settings(ImprovingSettings settings, string rule = ProvisionalRule) => JsonSerializer.Serialize(new
    {
        rule,
        buy = settings.BuyInNine,
        hold = settings.HoldInNine,
        sessions = settings.Sessions,
        words = settings.Words,
    });

    // One index's part of the night, in one transaction: every member's answer with its carried growth, the trades
    // carried, bought, sold and chosen, and the night's row.
    async Task<(ImprovingIndexOutcome Outcome, int Rows)> IndexNightAsync(
        SqliteConnection connection,
        string index,
        DateOnly night,
        IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars,
        IReadOnlyDictionary<string, string> filers,
        Dictionary<string, IReadOnlyList<FiledFactRow>> facts,
        string rule,
        ImprovingSettings settings,
        CancellationToken cancellation)
    {
        var members = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, MembersOn, [("$index", index), ("$session", Stamp(night))], cancellation))
        {
            members.Add(row.GetString(0));
        }

        var scores = new Dictionary<string, (DateOnly? Quarter, bool Financial, int Passed, int Applied)>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, ScoresOn, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            scores[row.GetString(0)] = (row.IsDBNull(1) ? null : Date(row.GetString(1)), row.GetInt64(2) == 1, row.GetInt32(3), row.GetInt32(4));
        }

        var readings = new Dictionary<string, (double? Growth, double? Change)>(StringComparer.Ordinal);
        var growthAt = LedgerReadings.IndexOf("revenue_growth");
        var changeAt = LedgerReadings.IndexOf("growth_change");

        await foreach (var row in RowsAsync(connection, null, ReadingsOn, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            if (!row.IsDBNull(1) && JsonSerializer.Deserialize<double?[]>(row.GetString(1)) is { } held)
            {
                readings[row.GetString(0)] = (held.Length > growthAt ? held[growthAt] : null, held.Length > changeAt ? held[changeAt] : null);
            }
        }

        // The filings read for the first time tonight are those filed on or after the session the family last read on the
        // index, or on its first night the session before tonight's.
        var before = await ScalarAsync(connection, null, NightBefore, [("$rule", rule), ("$index", index), ("$night", Stamp(night))], cancellation) is string stored ? Date(stored) : (DateOnly?)null;
        var lastRead = before ?? ImprovingRule.SessionBefore(night);

        // Each member's growth carried from its answer the night before, or from its own newest answer where the night
        // before holds none for it, by tonight's close over its close on that answer's night; and from one where it has none.
        var last = new Dictionary<string, (DateOnly Session, double Carried)>(StringComparer.Ordinal);

        if (before is { } previous)
        {
            await foreach (var row in RowsAsync(connection, null, CarriedOn, [("$rule", rule), ("$index", index), ("$session", Stamp(previous))], cancellation))
            {
                last[row.GetString(0)] = (previous, row.GetDouble(1));
            }

            foreach (var ticker in members.Where(ticker => !last.ContainsKey(ticker)).ToArray())
            {
                await foreach (var row in RowsAsync(connection, null, NewestCarried, [("$rule", rule), ("$index", index), ("$ticker", ticker), ("$session", Stamp(previous))], cancellation))
                {
                    last[ticker] = (Date(row.GetString(0)), row.GetDouble(1));
                }
            }
        }

        var answered = new List<Answered>();

        foreach (var ticker in members.Order(StringComparer.Ordinal))
        {
            if (Close(bars, ticker, night) is not { } close)
            {
                continue;
            }

            var carried = last.TryGetValue(ticker, out var then)
                ? Close(bars, ticker, then.Session) is { } earlier && earlier > 0m ? then.Carried * Statistic.FromRatio(close / earlier) : then.Carried
                : 1.0;
            var (growth, change) = readings.GetValueOrDefault(ticker);

            if (!scores.TryGetValue(ticker, out var score))
            {
                answered.Add(new Answered(ticker, 0, 0, null, null, false, null, growth, change, null, null, false, null, carried));

                continue;
            }

            var filer = filers.GetValueOrDefault(ticker);
            IReadOnlyList<FiledFactRow> held = [];

            if (filer is not null)
            {
                if (!facts.TryGetValue(filer, out var cached))
                {
                    cached = await FactsAsync(connection, filer, cancellation);
                    facts[filer] = cached;
                }

                held = cached;
            }

            // The newest filing the scorecard reads tonight, and where it is read for the first time the score the filing
            // before it held, through the core's one scorecard over the filings as first filed before it.
            var filed = ImprovingRule.FiledOn(held, score.Quarter, night);
            var first = filed is { } on && ImprovingRule.FirstRead(on, lastRead, night);
            Scorecard? earlierCard = null;
            ImprovingAnswer? answer = null;

            if (first)
            {
                earlierCard = FundamentalScorecard.Read(held, filed!.Value, score.Financial, Prices(bars, ticker));
                answer = ImprovingRule.Answer(score.Passed, score.Applied, earlierCard.Passed, earlierCard.Applied, change, settings);
            }

            // Its newest results release the night may read, flagged where it was accepted after the newest filing the
            // scorecard reads and reads better with its outlook raised or kept.
            DateOnly? released = null;
            string? outlook = null;
            var flagged = false;

            if (filer is not null && await ReleaseReadings.NewestAsync(connection, filer, night, cancellation) is { } release)
            {
                released = clock.SessionDateAt(release.Accepted);
                flagged = ImprovingRule.After(released.Value, filed) && ImprovingRule.Flags(release.Answer);
                outlook = flagged ? release.Answer.Outlook : null;
            }

            answered.Add(new Answered(ticker, score.Passed, score.Applied, score.Quarter, filed, first, earlierCard, growth, change, released, outlook, flagged, answer, carried));
        }

        var tonight = answered.ToDictionary(one => one.Ticker, StringComparer.Ordinal);

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheNight, [("$rule", rule), ("$index", index), ("$night", Stamp(night))], cancellation);

        var trades = new List<Trade>();

        await foreach (var row in RowsAsync(connection, transaction, OpenTrades, [("$rule", rule), ("$index", index)], cancellation))
        {
            trades.Add(new Trade(
                row.GetString(0),
                Date(row.GetString(1)),
                Money.FromStorage(row.GetString(2)),
                row.IsDBNull(3) ? null : Date(row.GetString(3)),
                row.IsDBNull(4) ? null : Money.FromStorage(row.GetString(4)),
                row.IsDBNull(5) ? null : row.GetDouble(5),
                row.IsDBNull(6) ? null : row.GetDouble(6),
                row.GetDouble(7),
                Date(row.GetString(8))));
        }

        var sold = new List<string>();
        var waiting = 0;
        var written = 0;
        var kept = new HashSet<string>(StringComparer.Ordinal);

        foreach (var open in trades)
        {
            var trade = open;

            // A stock no longer a member is carried no further: it is sold at its last close as one, the close it was
            // carried to.
            var gone = !members.Contains(trade.Ticker);

            if (!gone && trade.Through < night && Close(bars, trade.Ticker, trade.Through) is { } then && then > 0m && Close(bars, trade.Ticker, night) is { } now)
            {
                trade = trade with { Growth = trade.Growth * Statistic.FromRatio(now / then), Through = night };
            }

            // Bought at the open of the first session after its choice the store holds, through the core's one pricing
            // function; one whose next session is not stored yet waits for it.
            if (!gone && trade.BoughtOn is null)
            {
                if (await FillAsync(connection, transaction, index, rule, trade, bars, cancellation) is { } filled)
                {
                    trade = filled;
                    written++;
                }
                else
                {
                    waiting++;
                }
            }

            var sessions = trade.BoughtOn is { } bought ? SessionsHeld(bars, trade.Ticker, bought, trade.Through) : 0;

            await ExecuteAsync(connection, transaction, CarryTrade,
            [
                ("$rule", rule), ("$index", index), ("$ticker", trade.Ticker), ("$chosen_on", Stamp(trade.ChosenOn)),
                ("$growth", trade.Growth), ("$through", Stamp(trade.Through)), ("$sessions", sessions),
            ], cancellation);
            written++;

            // Sold at tonight's close where tonight's scorecard stands under the hold floor, or where tonight is its last
            // session; one with no close stored tonight was not answered and is kept.
            var fell = !gone && tonight.TryGetValue(trade.Ticker, out var answer) && answer.Applied > 0 && !ImprovingRule.Holds(answer.Passed, answer.Applied, settings);
            var ended = !gone && !fell && trade.Through == night && sessions >= settings.Sessions;

            if (gone || fell || ended)
            {
                await EndAsync(connection, transaction, index, rule, trade, gone ? trade.Through : night, gone ? LeftTheIndex : fell ? ScoreFell : TimeLimit, bars, tonight, night, cancellation);
                sold.Add(trade.Ticker);
                written++;

                continue;
            }

            kept.Add(trade.Ticker);
        }

        // Tonight's buys: each member a newly read filing lifted to the bar, one trade a stock, chosen at tonight's close.
        var boughtTonight = new List<string>();
        var statuses = new Dictionary<string, (string Status, string? Reason)>(StringComparer.Ordinal);

        foreach (var one in answered)
        {
            if (one is { FirstRead: true, Answer: { } answer, Before: { } earlier })
            {
                if (answer.Bought && kept.Contains(one.Ticker))
                {
                    statuses[one.Ticker] = (HeldStatus, null);
                }
                else if (answer.Bought && !sold.Contains(one.Ticker, StringComparer.Ordinal))
                {
                    await ExecuteAsync(connection, transaction, InsertTrade,
                    [
                        ("$rule", rule), ("$index", index), ("$ticker", one.Ticker), ("$night", Stamp(night)),
                        ("$filed", Stamp(one.Filed!.Value)), ("$released", ImprovingRule.Confirmed(one.Released, one.Quarter, one.Filed) is { } on ? Stamp(on) : DBNull.Value),
                        ("$passed_checks", one.Passed), ("$applied", one.Applied), ("$before_passed", earlier.Passed), ("$before_applied", earlier.Applied),
                        ("$buy_close", Money.ToStorage(Close(bars, one.Ticker, night)!.Value)),
                    ], cancellation);
                    boughtTonight.Add(one.Ticker);
                    statuses[one.Ticker] = (BoughtStatus, null);
                    written++;
                }
                else if (ImprovingRule.Rise(one.Passed, one.Applied, earlier.Passed, earlier.Applied) > 0)
                {
                    statuses[one.Ticker] = (ImprovedStatus, answer.Reason);
                }
            }

            if (!statuses.ContainsKey(one.Ticker) && one.Flagged)
            {
                statuses[one.Ticker] = (FlaggedStatus, null);
            }
        }

        foreach (var one in answered)
        {
            var (status, reason) = statuses.TryGetValue(one.Ticker, out var said) ? ((string?)said.Status, said.Reason) : (null, null);

            await ExecuteAsync(connection, transaction, InsertResult,
            [
                ("$rule", rule), ("$index", index), ("$night", Stamp(night)), ("$ticker", one.Ticker),
                ("$passed_checks", one.Passed), ("$applied", one.Applied),
                ("$filed", one.Filed is { } filed ? Stamp(filed) : DBNull.Value), ("$first_read", one.FirstRead ? 1 : 0),
                ("$before_passed", (object?)one.Before?.Passed ?? DBNull.Value), ("$before_applied", (object?)one.Before?.Applied ?? DBNull.Value),
                ("$before_checks", one.Before is { } card ? ScorecardText.Of(card.Checks) : DBNull.Value),
                ("$growth", (object?)one.Growth ?? DBNull.Value), ("$growth_change", (object?)one.GrowthChange ?? DBNull.Value),
                ("$released", one.Released is { } released ? Stamp(released) : DBNull.Value), ("$outlook", (object?)one.Outlook ?? DBNull.Value),
                ("$flagged", one.Flagged ? 1 : 0), ("$status", (object?)status ?? DBNull.Value), ("$reason", (object?)reason ?? DBNull.Value),
                ("$carried", one.Carried),
            ], cancellation);
        }

        var scored = answered.Count(one => one.Applied > 0);
        var firstRead = answered.Count(one => one.FirstRead);
        var improved = answered.Count(one => one.Before is { } earlier && ImprovingRule.Rise(one.Passed, one.Applied, earlier.Passed, earlier.Applied) > 0);
        var flaggedCount = statuses.Values.Count(said => said.Status == FlaggedStatus);

        await ExecuteAsync(connection, transaction, InsertNight,
        [
            ("$rule", rule), ("$index", index), ("$night", Stamp(night)), ("$members", members.Count),
            ("$scored", scored), ("$first_read", firstRead), ("$improved", improved), ("$bought", boughtTonight.Count),
            ("$flagged", flaggedCount), ("$held", kept.Count + boughtTonight.Count), ("$settings", Settings(settings, rule)),
        ], cancellation);

        await transaction.CommitAsync(cancellation);

        return (
            new ImprovingIndexOutcome(index, members.Count, scored, firstRead, improved, boughtTonight, flaggedCount, sold, kept.Count + boughtTonight.Count, waiting),
            1 + answered.Count + written);
    }

    // A stock's closes to the night as the share check reads them, at the store's scale and as traded.
    static PriceHistory Prices(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker) =>
        bars.TryGetValue(ticker, out var days)
            ? new PriceHistory(
                [.. days.Keys],
                [.. days.Values.Select(day => Statistic.FromPrice(day.Close))],
                [.. days.Values.Select(day => day.Raw > 0m ? Statistic.FromPrice(day.Raw) : 0)])
            : PriceHistory.None;

    // The sessions a trade was held from its buy to the session it was carried to, each a session the store holds a bar
    // of the stock for, the buy's own counted first.
    static int SessionsHeld(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker, DateOnly bought, DateOnly through) =>
        bars.TryGetValue(ticker, out var days) ? days.Keys.Count(day => day >= bought && day <= through) : 0;

    // A trade's buy at the open of the first session after its choice the store holds, and its benchmark's gap over the
    // members answered on the night it was chosen, each at the open of its own first session after that night, both through
    // the core's one pricing function; none where the store holds no later session of the stock's yet.
    async Task<Trade?> FillAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string rule, Trade trade, IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, CancellationToken cancellation)
    {
        if (After(bars, trade.Ticker, trade.ChosenOn) is not { } next || Close(bars, trade.Ticker, trade.ChosenOn) is not { } close)
        {
            return null;
        }

        var members = new List<FillMember>();

        await foreach (var row in RowsAsync(connection, transaction, CarriedOn, [("$rule", rule), ("$index", index), ("$session", Stamp(trade.ChosenOn))], cancellation))
        {
            var ticker = row.GetString(0);

            if (Close(bars, ticker, trade.ChosenOn) is { } then && then > 0m)
            {
                var price = Statistic.FromPrice(then);

                members.Add(new FillMember(true, price, price, After(bars, ticker, trade.ChosenOn) is { } opened ? Statistic.FromPrice(opened.Day.Open) : null));
            }
        }

        if (TradeFills.Holding(next.Session, Statistic.FromPrice(close), Statistic.FromPrice(next.Day.Open), 0.0, 0.0, members) is not { } fill)
        {
            return null;
        }

        var filled = trade with
        {
            BoughtOn = next.Session,
            Fill = next.Day.Open,
            Gap = fill.Result is { } result ? -result : null,
            BenchGap = fill.Benchmark is { } benchmark ? -benchmark : null,
        };

        await ExecuteAsync(connection, transaction, FillTrade,
        [
            ("$rule", rule), ("$index", index), ("$ticker", trade.Ticker), ("$chosen_on", Stamp(trade.ChosenOn)),
            ("$bought_on", Stamp(next.Session)), ("$fill", Money.ToStorage(next.Day.Open)),
            ("$gap", (object?)filled.Gap ?? DBNull.Value), ("$bench_gap", (object?)filled.BenchGap ?? DBNull.Value), ("$members", fill.Members),
        ], cancellation);

        return filled;
    }

    // A trade sold at a close: its result its growth less one less its gap at the open; its benchmark the mean, over the
    // members answered on the night it was chosen, of each one's carried growth on the night it was sold over its growth
    // on that night, less one less their gap; its round trip at its company's value on the night it was chosen and its
    // prices as traded; and its edge its result less its benchmark, less its round trip, in percentage points. One sold
    // before it was bought holds none of them.
    async Task EndAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        string rule,
        Trade trade,
        DateOnly on,
        string reason,
        IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars,
        IReadOnlyDictionary<string, Answered> tonight,
        DateOnly night,
        CancellationToken cancellation)
    {
        var sale = DayOf(bars, trade.Ticker, on);
        double? result = null;
        double? benchmark = null;
        double? cost = null;
        double? edge = null;

        if (trade is { BoughtOn: { } bought, Fill: { } fill, Gap: { } gap } && sale is { } sold)
        {
            result = trade.Growth - 1.0 - gap;

            var growths = new List<double>();

            await foreach (var row in RowsAsync(connection, transaction, CarriedOn, [("$rule", rule), ("$index", index), ("$session", Stamp(trade.ChosenOn))], cancellation))
            {
                var ticker = row.GetString(0);
                var start = row.GetDouble(1);
                double? end = on == night && tonight.TryGetValue(ticker, out var now) ? now.Carried : null;

                if (end is null)
                {
                    await foreach (var newest in RowsAsync(connection, transaction, NewestCarried, [("$rule", rule), ("$index", index), ("$ticker", ticker), ("$session", Stamp(on))], cancellation))
                    {
                        end = newest.GetDouble(1);
                    }
                }

                if (start > 0 && end is { } reached)
                {
                    growths.Add(reached / start);
                }
            }

            benchmark = growths.Count > 0 && trade.BenchGap is { } benchGap ? growths.Average() - 1.0 - benchGap : null;

            decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", index), ("$session", Stamp(trade.ChosenOn)), ("$ticker", trade.Ticker)], cancellation) is string stored ? Money.FromStorage(stored) : null;
            var entry = DayOf(bars, trade.Ticker, bought) is { Close: > 0m } boughtDay ? fill * boughtDay.Raw / boughtDay.Close : fill;
            var exit = sold.Raw > 0m ? sold.Raw : sold.Close;
            var trip = TradeCost.InPercent(value, entry, exit);

            cost = double.IsNaN(trip) ? null : trip;
            edge = benchmark is { } against && cost is { } paid ? ((result.Value - against) * 100.0) - paid : null;
        }

        await ExecuteAsync(connection, transaction, EndTrade,
        [
            ("$rule", rule), ("$index", index), ("$ticker", trade.Ticker), ("$chosen_on", Stamp(trade.ChosenOn)),
            ("$ended_on", Stamp(on)), ("$sale", sale is { } day ? Money.ToStorage(day.Close) : DBNull.Value), ("$end", reason),
            ("$result", (object?)result ?? DBNull.Value), ("$benchmark", (object?)benchmark ?? DBNull.Value),
            ("$cost", (object?)cost ?? DBNull.Value), ("$edge", (object?)edge ?? DBNull.Value),
        ], cancellation);
    }

    // Each stock's filer padded to ten digits, by its newest company row filing one.
    static async Task<IReadOnlyDictionary<string, string>> FilersAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var filers = new Dictionary<string, string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, Filers, [], cancellation))
        {
            filers[row.GetString(0)] = SecEdgarArchive.Padded(row.GetString(1));
        }

        return filers;
    }

    static async Task<IReadOnlyList<FiledFactRow>> FactsAsync(SqliteConnection connection, string cik, CancellationToken cancellation)
    {
        var facts = new List<FiledFactRow>();

        await foreach (var row in RowsAsync(connection, null, FactsOfFiler, [("$cik", cik)], cancellation))
        {
            facts.Add(new FiledFactRow(
                row.GetString(0),
                Date(row.GetString(1)),
                Date(row.GetString(2)),
                Money.FromStorage(row.GetString(3)),
                Date(row.GetString(4)),
                row.GetString(5),
                row.GetString(6)));
        }

        return facts;
    }

    // Every stored bar to the night, each stock's by session.
    static async Task<IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>>> BarsAsync(SqliteConnection connection, DateOnly night, CancellationToken cancellation)
    {
        var bars = new Dictionary<string, SortedDictionary<DateOnly, Daily>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, Bars, [("$night", Stamp(night))], cancellation))
        {
            var ticker = row.GetString(0);

            if (!bars.TryGetValue(ticker, out var days))
            {
                days = [];
                bars[ticker] = days;
            }

            var close = Money.FromStorage(row.GetString(3));

            days[Date(row.GetString(1))] = new Daily(Money.FromStorage(row.GetString(2)), close, row.IsDBNull(4) ? close : Money.FromStorage(row.GetString(4)));
        }

        return bars;
    }

    static Daily? DayOf(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker, DateOnly session) =>
        bars.TryGetValue(ticker, out var days) && days.TryGetValue(session, out var day) ? day : null;

    static decimal? Close(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker, DateOnly session) =>
        DayOf(bars, ticker, session)?.Close;

    // The first session after one the store holds a bar of the stock for, none where it holds no later one.
    static (DateOnly Session, Daily Day)? After(IReadOnlyDictionary<string, SortedDictionary<DateOnly, Daily>> bars, string ticker, DateOnly session)
    {
        if (!bars.TryGetValue(ticker, out var days))
        {
            return null;
        }

        foreach (var (day, bar) in days)
        {
            if (day > session)
            {
                return (day, bar);
            }
        }

        return null;
    }

    // An index's part that failed: a night row naming why where no earlier try of the night wrote one, and none where the
    // store will not take it, since recording the failure is never what stops the night.
    async Task<int> NotComputedAsync(SqliteConnection connection, string index, DateOnly night, string fault, string rule, ImprovingSettings settings, CancellationToken cancellation)
    {
        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText = InsertNotComputed;
            command.Parameters.AddWithValue("$rule", rule);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$night", Stamp(night));
            command.Parameters.AddWithValue("$settings", Settings(settings, rule));
            command.Parameters.AddWithValue("$fault", fault);

            return await command.ExecuteNonQueryAsync(cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            return 0;
        }
    }

    // The step's row after a failure outside any index's part, on a connection of its own, and none where the store will
    // not take it.
    async Task AppendAfterAFailureAsync(string runId, DateTimeOffset startedAt, int rows, string detail, CancellationToken cancellation)
    {
        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await AppendAsync(connection, runId, startedAt, rows, NotComputed, detail, cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
        }
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int rows, string outcome, string detail, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$outcome", outcome);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$rows_written", rows);
        command.Parameters.AddWithValue("$detail", detail);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellation);
    }

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            yield return reader;
        }
    }

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
