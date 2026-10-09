using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Indices;

// What the index families did for one index on the night: the members read, the breadth and the market check, how many
// each family passed, how many of the index's list were listed and how many held back by a trade still open, and the
// trades kept tonight and ended tonight; or, for an index whose part of the night failed, the failure.
public sealed record IndexNightOutcome(string Index, int Members, double? Breadth, bool MarketOpen, IReadOnlyDictionary<string, int> Passed, int Listed, int HeldByATrade, int TradesKept, int TradesEnded, IndexHeavyweightsOutcome Heavyweights, string? Fault = null)
{
    public static IndexNightOutcome NotComputed(string index, string fault) =>
        new(index, 0, null, false, new Dictionary<string, int>(StringComparer.Ordinal), 0, 0, 0, 0, new IndexHeavyweightsOutcome(false, 0, 0, 0), fault);

    // The index's registered rules read on the night and what their own lists did, and what the registered heavyweights
    // rules' own books did.
    public int Rules { get; init; }

    public IndexRuleTrades.Kept RuleTrades { get; init; } = new(0, 0, 0);

    public IndexHeavyweightsOutcome HeavyweightRules { get; init; } = new(false, 0, 0, 0);
}

// The night the index families read, none where the store holds no bar, and each index's outcome.
public sealed record IndexFamiliesOutcome(DateOnly? Session, IReadOnlyList<IndexNightOutcome> Nights);

// The index families. After the S&P 500's families have drawn their list, they read the S&P 400's and 600's
// provisional swing rules for the night with the sweep's own code over the members' year of bars, each member's answer
// stored under each family whether it passed or not, and draw each index's list by the S&P 500's own rule: the
// families in the page's order, five a family, a stock listed once, and a stock whose trade on any card of any index is
// still open listed by none. They walk each index's open trades over the closes since and keep tonight's listed rows as
// trades. A night run again replaces its own rows; they evaluate no rule of the S&P 500's, make no request and call
// no model. A failure in one index's part undoes that index's writes of the night, writes a night row naming it and
// reads the next index; one outside any index's part ends the step with every index not yet read named the same way,
// so the S&P 500's night goes on to its facts and its reports whatever became of theirs. The night's own cancellation
// is the one failure passed on.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
// see: A failure in the S&P 400's or 600's part of the night is caught and named, and the S&P 500's night is built regardless
// see: The nightly run is arithmetic only
// see: Every computed table's writer is its own deleter
public sealed class IndexFamilies : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.ListRule, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.IndexFamilyNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.IndexFamilyResult, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.IndexFamilyTrade, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.SwitchReading, Touch.Read),
            new StoreTouch(Store.MarketReading, Touch.Read),
            new StoreTouch(Store.IndexRuleTrade, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.IndexHeavyweightRuleNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.IndexHeavyweightRuleHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "index-families";

    // The run ids the verb writes, which no page reads as a night.
    public const string ByHandPrefix = "index-families-by-hand-";

    // The indices read, in the order the page offers them after the S&P 500.
    public static IReadOnlyList<string> Indices { get; } = [FundHoldings.MidCapIndex, FundHoldings.SmallCapIndex];

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    // An index's members on the night with every span the index held each for.
    const string MembersOf = @"
        SELECT ticker, joined, ""left"" FROM membership
        WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string SpansOf = @"SELECT joined, ""left"" FROM membership WHERE index_code = $index AND ticker = $ticker;";

    const string BarsOf = @"
        SELECT session_date, high, low, close, volume, open, raw_close FROM bar
        WHERE ticker = $ticker AND session_date <= $night ORDER BY session_date;
    ";

    const string PrintsOf = "SELECT event_date, timing, detail FROM calendar WHERE ticker = $ticker AND kind = 'earnings' ORDER BY event_date;";

    // Each quarter as filed, its net and operating income, the profit gate's reading.
    const string QuartersOf = @"
        SELECT period_end, filing_date, net_income, operating_income FROM reported_quarter
        WHERE ticker = $ticker AND filing_date IS NOT NULL ORDER BY period_end;
    ";

    // Every trade an index's list kept, all indices together, which the open trade rule reads.
    const string IndexTrades = @"
        SELECT index_code, family, ticker, session_date, ended_on, cap FROM index_family_trade
        WHERE session_date < $night;
    ";

    const string OpenIndexTrades = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap FROM index_family_trade
        WHERE index_code = $index AND ended_on IS NULL AND session_date < $night;
    ";

    const string ClearTheNight = @"
        DELETE FROM index_family_night WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_result WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_pick WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_trade WHERE index_code = $index AND session_date = $night;
        DELETE FROM index_family_result WHERE index_code = $index AND passed = 0 AND session_date < (SELECT MIN(session_date) FROM bar);
    ";

    const string InsertNight = @"
        INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced)
        VALUES ($index, $night, $members, $breadth, $open, $settings, $rebalanced);
    ";

    const string InsertResult = @"
        INSERT INTO index_family_result (index_code, session_date, ticker, family, passed, place, entry, stop, target, trail, cap, order_by, reason)
        VALUES ($index, $night, $ticker, $family, $passed, $place, $entry, $stop, $target, $trail, $cap, $order_by, $reason);
    ";

    const string InsertPick = @"
        INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also, held_index, held_family, held_night)
        VALUES ($index, $night, $ticker, $family, $state, $place, $also, $held_index, $held_family, $held_night);
    ";

    const string InsertTrade = @"
        INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap)
        VALUES ($index, $family, $ticker, $night, $place, $entry, $stop, $target, $trail, $cap);
    ";

    const string EndTrade = @"
        UPDATE index_family_trade SET ended_on = $ended_on, result = $result, cost = $cost
        WHERE index_code = $index AND family = $family AND ticker = $ticker AND session_date = $session;
    ";

    // A company's value on a trade's own night as the member readings read it under the index, none where they read none.
    const string ValueOn = "SELECT company_value FROM member_reading WHERE index_code = $index AND session_date = $session AND ticker = $ticker;";

    // An index's night that failed, where no earlier try of the night computed one.
    const string InsertNotComputed = @"
        INSERT INTO index_family_night (index_code, session_date, members, breadth, market_open, settings, rebalanced, fault)
        VALUES ($index, $night, 0, NULL, 0, $settings, 0, $fault)
        ON CONFLICT (index_code, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    // The stage row's outcome where every index was read, and where one or more was not: a word the run page draws
    // among the stages a person has to look at, and one no resume reads as a stop.
    public const string Ok = "ok";

    public const string NotComputed = "not computed";

    readonly IClock clock;
    readonly string databaseFile;

    public IndexFamilies(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The catalogue's readings of an index's members on the night, read only for an index whose standing rule's hooks
    // read them.
    // see: Every engine's settings hooks land together and all default off, so the families' pins move once
    public async Task<IndexFamiliesOutcome> RunAsync(
        string runId,
        CancellationToken cancellation = default,
        IReadOnlyList<Core.Candidates.RegisterRow>? register = null,
        DateTimeOffset? nightStartedAt = null,
        Func<string, CancellationToken, Task<Func<string, IReadOnlyList<double?>?>>>? readingsOf = null)
    {
        var startedAt = clock.UtcNow;
        var outcomes = new List<IndexNightOutcome>();
        DateOnly? read = null;
        var rows = 0;

        // The index rules standing registered when the night started, each index's own read on its night.
        // see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
        var (standing, skipped) = StandingRules(register ?? [], nightStartedAt ?? startedAt);

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            var night = await ScalarAsync(connection, NewestSession, [], cancellation) is string newest ? Date(newest) : (DateOnly?)null;

            if (night is not { } session)
            {
                await AppendAsync(connection, runId, startedAt, 0, Ok, "no session: the store holds no bar", cancellation);

                return new IndexFamiliesOutcome(null, []);
            }

            read = session;

            foreach (var index in (IReadOnlyList<string>)[LargeIndex, .. Indices])
            {
                try
                {
                    var (outcome, written) = await IndexNightAsync(connection, index, session, [.. standing.Where(rule => rule.Index == index)], cancellation, readingsOf);

                    outcomes.Add(outcome);
                    rows += written;
                }
                catch (Exception failure) when (!cancellation.IsCancellationRequested)
                {
                    var fault = Cause(failure);

                    rows += await NotComputedAsync(connection, index, session, fault, cancellation);
                    outcomes.Add(IndexNightOutcome.NotComputed(index, fault));
                }
            }

            await AppendAsync(connection, runId, startedAt, rows, outcomes.Any(outcome => outcome.Fault is not null) ? NotComputed : Ok, Detail(outcomes) + Skipped(skipped), cancellation);

            return new IndexFamiliesOutcome(session, outcomes);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            var fault = Cause(failure);
            IndexNightOutcome[] all =
            [
                .. outcomes,
                .. ((IReadOnlyList<string>)[LargeIndex, .. Indices]).Where(index => outcomes.All(outcome => outcome.Index != index)).Select(index => IndexNightOutcome.NotComputed(index, fault)),
            ];

            await AppendAfterAFailureAsync(runId, startedAt, rows, Detail(all) + "; the step stopped on " + fault, cancellation);

            return new IndexFamiliesOutcome(read, all);
        }
    }

    // Each holding the S&P 400's and 600's books sold on leaving the index, sold again by hand at its stock's close on the
    // session it was sold as the bar table holds it, with its round trip from that close; one already sold there, or one
    // whose close the bar table does not hold, is left as it was. The step's own row under the run names each holding
    // written with its close before and after, and the words are returned.
    // see: A heavyweight leaving the index is sold at its last session's close as a member
    public async Task<string> SellLeaversAgainAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        IReadOnlyList<string> sold;

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation))
        {
            sold = await IndexHeavyweights.SellLeaversAgainAsync(connection, transaction, cancellation);
            await transaction.CommitAsync(cancellation);
        }

        var detail = sold.Count == 0
            ? "no holding sold on leaving the index stands at a close other than its stock's on the session it was sold"
            : "sold again at its stock's close on the session it was sold: " + string.Join("; ", sold);

        await AppendAsync(connection, runId, startedAt, sold.Count, Ok, detail, cancellation);

        return detail;
    }

    // The failure an index's part of the night stopped on, as its row and the stage's name it.
    public static string Cause(Exception failure) => $"{failure.GetType().Name}: {failure.Message}";

    // An index whose part of the night failed: its writes of the night undone with its transaction, and a night row
    // naming the failure where no earlier try of the night computed one. A row that cannot be written either leaves the
    // failure on the stage's row alone.
    async Task<int> NotComputedAsync(SqliteConnection connection, string index, DateOnly night, string fault, CancellationToken cancellation)
    {
        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText = InsertNotComputed;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$night", Stamp(night));
            command.Parameters.AddWithValue("$settings", Settings(index));
            command.Parameters.AddWithValue("$fault", fault);

            return await command.ExecuteNonQueryAsync(cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            return 0;
        }
    }

    // The stage's row after a failure outside any index's part, on a connection of its own, and none where the store
    // will not take it: recording the failure is never what stops the night.
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

    // One index's part of the night: its members' answers under each family, its list, its trades walked and kept and
    // its sector heavyweights, written in one transaction, so a failure anywhere in it leaves none of its writes.
    async Task<(IndexNightOutcome Outcome, int Rows)> IndexNightAsync(
        SqliteConnection connection,
        string index,
        DateOnly session,
        IReadOnlyList<IndexRule> rules,
        CancellationToken cancellation,
        Func<string, CancellationToken, Task<Func<string, IReadOnlyList<double?>?>>>? readingsOf = null)
    {
        var (names, income) = await InputsAsync(connection, index, session, cancellation);
        var inputs = IndexNightRead.Prepare(index, session, names, income);
        var levels = inputs is null || rules.Count == 0 ? IndexLevels.None : await LevelsAsync(connection, inputs, cancellation);

        // The S&P 500 is read here for the fundamentals-first family alone, its other families being the S&P 500's own and
        // its book the S&P 500's sector heavyweights'.
        // see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
        var large = index == LargeIndex;
        var families = large ? IndexNightRead.LargeFamilies : IndexNightRead.Families;

        // The catalogue's readings of the members on the night, which the fundamentals-first family reads every night and a
        // standing rule's hooks read where they state one.
        var tonight = inputs is not null && readingsOf is not null ? await readingsOf(index, cancellation) : null;

        // A family whose live rule stands draws the index's page on that rule's settings, and every other on its
        // provisional rule.
        // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
        IndexFamilyRead ReadOf(IndexNightInputs prepared, string family) =>
            rules.FirstOrDefault(rule => rule.Live && rule.Family == family) is { } live ? IndexRules.Read(prepared, live, levels) : IndexNightRead.Provisional(prepared, family, tonight);

        var read = inputs is null
            ? new IndexNight(index, session, 0, null, false, [])
            : new IndexNight(index, session, inputs.Held.Length, inputs.Breadth, inputs.Open, [.. families.SelectMany(family => IndexNightRead.Answers(inputs, family, ReadOf(inputs, family)))]);
        IReadOnlyList<(IndexRule Rule, IReadOnlyList<IndexAnswer> Answers)> ruleAnswers = inputs is null
            ? []
            : [.. rules.Where(rule => rule.Evaluator is not Core.Candidates.IndexHeavyweightCandidate).Select(rule => (rule, IndexNightRead.Answers(inputs, rule.Family, IndexRules.Read(inputs, rule, levels))))];
        var qualifying = families
            .Select(family => new FamilyQualifiers(family, [.. read.Answers.Where(answer => answer.Family == family && answer.Passed).OrderBy(answer => answer.Place).Select(answer => answer.Ticker)]))
            .ToArray();
        var passing = qualifying.SelectMany(family => family.Tickers).Distinct(StringComparer.Ordinal).ToArray();
        var (open, heldIn) = await OpenAsync(connection, session, passing, cancellation, large);
        var picks = FamilyList.Draw(qualifying, open);
        var readings = ruleAnswers.Any(pair => RuleHooks.Of(pair.Rule.Parameters).ReadsReadings) ? tonight : null;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheNight, [("$index", index), ("$night", Stamp(session))], cancellation);

        var ended = await WalkAsync(connection, transaction, index, session, cancellation);
        var heavyweights = large
            ? new IndexHeavyweightsOutcome(false, 0, 0, 0)
            : await IndexHeavyweights.RunAsync(connection, transaction, index, session, names, income, cancellation);
        var heavyweightRules = large
            ? new IndexHeavyweightsOutcome(false, 0, 0, 0)
            : await IndexHeavyweights.RulesAsync(connection, transaction, index, session, names, income, levels, [.. rules.Where(rule => rule.Evaluator is Core.Candidates.IndexHeavyweightCandidate)], cancellation);
        var ruleTrades = await IndexRuleTrades.KeepAsync(connection, transaction, index, session, inputs, ruleAnswers, cancellation, readings);

        await ExecuteAsync(connection, transaction, InsertNight, [("$index", index), ("$night", Stamp(session)), ("$members", read.Members), ("$breadth", (object?)read.Breadth ?? DBNull.Value), ("$open", read.MarketOpen ? 1 : 0), ("$settings", Settings(index, rules)), ("$rebalanced", heavyweights.Rebalanced ? 1 : 0)], cancellation);

        foreach (var answer in read.Answers)
        {
            await ExecuteAsync(connection, transaction, InsertResult,
            [
                ("$index", index), ("$night", Stamp(session)), ("$ticker", answer.Ticker), ("$family", answer.Family), ("$passed", answer.Passed ? 1 : 0),
                ("$place", (object?)answer.Place ?? DBNull.Value), ("$entry", Stored(answer.Entry)), ("$stop", Stored(answer.Stop)), ("$target", Stored(answer.Target)), ("$trail", Stored(answer.Trail)),
                ("$cap", (object?)answer.Cap ?? DBNull.Value), ("$order_by", (object?)answer.OrderBy ?? DBNull.Value), ("$reason", (object?)answer.Reason ?? DBNull.Value),
            ], cancellation);
        }

        var answers = read.Answers.Where(answer => answer.Passed).ToDictionary(answer => (answer.Ticker, answer.Family));
        var kept = 0;

        foreach (var pick in picks)
        {
            await ExecuteAsync(connection, transaction, InsertPick,
            [
                ("$index", index), ("$night", Stamp(session)), ("$ticker", pick.Ticker), ("$family", pick.Family), ("$state", pick.State),
                ("$place", (object?)pick.Place ?? DBNull.Value), ("$also", JsonSerializer.Serialize(pick.Also)),
                ("$held_index", pick.HeldBy is not null && heldIn.TryGetValue(pick.Ticker, out var heldIndex) ? heldIndex : DBNull.Value),
                ("$held_family", (object?)pick.HeldBy?.Family ?? DBNull.Value),
                ("$held_night", pick.HeldBy is { } held ? Stamp(held.Listed) : DBNull.Value),
            ], cancellation);

            if (pick.State == FamilyList.Listed && answers.TryGetValue((pick.Ticker, pick.Family), out var trade))
            {
                kept++;
                await ExecuteAsync(connection, transaction, InsertTrade,
                [
                    ("$index", index), ("$family", pick.Family), ("$ticker", pick.Ticker), ("$night", Stamp(session)), ("$place", pick.Place!.Value),
                    ("$entry", Stored(trade.Entry)), ("$stop", Stored(trade.Stop)), ("$target", Stored(trade.Target)), ("$trail", Stored(trade.Trail)), ("$cap", trade.Cap!.Value),
                ], cancellation);
            }
        }

        await transaction.CommitAsync(cancellation);

        var outcome = new IndexNightOutcome(
            index,
            read.Members,
            read.Breadth,
            read.MarketOpen,
            families.ToDictionary(family => family, family => read.Answers.Count(answer => answer.Family == family && answer.Passed), StringComparer.Ordinal),
            picks.Count(pick => pick.State == FamilyList.Listed),
            picks.Count(pick => pick.State == FamilyList.OpenTrade),
            kept,
            ended,
            heavyweights)
        {
            Rules = rules.Count,
            RuleTrades = ruleTrades,
            HeavyweightRules = heavyweightRules,
        };

        return (outcome, 1 + read.Answers.Count + picks.Count + kept + ruleTrades.Trades);
    }

    // What the stage's row says for each index: what it read, or the failure that stopped it.
    public static string Detail(IReadOnlyList<IndexNightOutcome> outcomes) =>
        string.Join("; ", outcomes.Select(outcome => outcome.Fault is { } fault
            ? $"{outcome.Index}: not computed tonight, {fault}"
            : FormattableString.Invariant(
                $"{outcome.Index}: {outcome.Members} member(s) read, breadth {(outcome.Breadth is { } breadth ? breadth.ToString("0.00", CultureInfo.InvariantCulture) : "not read")}, the market check {(outcome.MarketOpen ? "open" : "closed")}, {string.Join(", ", outcome.Passed.Select(pair => $"{pair.Value} passed by the {pair.Key}"))}, {outcome.Listed} listed, {outcome.HeldByATrade} held back by a trade still open, {outcome.TradesKept} trade(s) kept, {outcome.TradesEnded} ended; the sector heavyweights {(outcome.Heavyweights.Rebalanced ? "rebalanced" : "carried")}{(outcome.Heavyweights.Waits is { } waits ? ", " + waits : string.Empty)}, {outcome.Heavyweights.Entered} bought, {outcome.Heavyweights.Ended} sold, {outcome.Heavyweights.Held} held")
                + (outcome.Rules > 0 ? FormattableString.Invariant($"; {outcome.Rules} registered rule(s) kept {outcome.RuleTrades.Trades} trade(s) on their own lists, {outcome.RuleTrades.Ended} ended and {outcome.RuleTrades.Benchmarked} benchmarked") : string.Empty)
                + (outcome.HeavyweightRules is { Held: > 0 } or { Entered: > 0 } or { Ended: > 0 } or { Rebalanced: true } or { Waits: not null } ? FormattableString.Invariant($"; the registered heavyweights rules' books {(outcome.HeavyweightRules.Rebalanced ? "rebalanced" : "carried")}, {outcome.HeavyweightRules.Entered} bought, {outcome.HeavyweightRules.Ended} sold, {outcome.HeavyweightRules.Held} held{(outcome.HeavyweightRules.Waits is { } ruleWaits ? "; " + ruleWaits : string.Empty)}") : string.Empty)));

    // The registered rules a night left unread, each with why, after the indices' own words.
    static string Skipped(IReadOnlyList<string> skipped) =>
        skipped.Count == 0 ? string.Empty : "; not read: " + string.Join("; ", skipped);

    // The rule each family runs on in the index, the words a card's description is written from: its settings, its
    // floors, its gate and the cost its trades pay; and for each family whose live rule stands, that rule with the day it
    // was registered and its settings in words, the rule the night read the family's list by in the provisional rule's place.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    public static string Settings(string index, IReadOnlyList<IndexRule>? rules = null) => JsonSerializer.Serialize(new
    {
        floors = new { price = MemberReadings.LowestPrice, dollarVolume = MemberReadings.DollarVolumeFloor(index), sessions = MemberReadings.DollarVolumeSessions },
        profitGate = new { quarters = MemberReadings.Quarters },
        costs = CostsPaid,
        marketFloor = FamilySweep.MarketFloor,
        pullback = new { setting = SweepIdeas.BaseRule.Setting.Describe(SweepGrid.Extended), cap = IndexNightRead.PullbackCap },
        breakout = BreakoutSweep.Grid.Key(IndexNightRead.BreakoutAsFrozen),
        drift = DriftSweep.Grid.Key(IndexNightRead.DriftAsFrozen),
        heavyweights = IndexHeavyweights.Provisional.Key,
        fundamentals = new { setting = FundamentalsRule.Provisional.Key, words = FundamentalsRule.Provisional.Words, cap = IndexNightRead.PullbackCap },
        live = (rules ?? [])
            .Where(rule => rule.Live)
            .Select(rule => new
            {
                family = rule.Family,
                candidate = rule.Candidate,
                since = rule.RegisteredAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                words = IndexRules.Words(rule.Evaluator, rule.Parameters),
            })
            .ToArray(),
    });

    // The rules of the index's swing families standing registered at the night's start, each read at its registration's
    // settings; a rule whose evaluator moved since it was registered, or whose settings its sweep's grid does not hold, is
    // not read and is named with why.
    // see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
    public static (IReadOnlyList<IndexRule> Rules, IReadOnlyList<string> Skipped) StandingRules(IReadOnlyList<Core.Candidates.RegisterRow> register, DateTimeOffset at)
    {
        var rules = new List<IndexRule>();
        var skipped = new List<string>();

        foreach (var row in Core.Candidates.CandidateFamily.Standing(register, at))
        {
            if (Core.Candidates.CandidateEvaluators.Find(row.Evaluator) is not Core.Candidates.IndexRuleCandidate evaluator)
            {
                continue;
            }

            var parameters = Core.Candidates.CandidateEvaluator.Read(row.Parameters);

            if (evaluator.Version != row.EvaluatorVersion)
            {
                skipped.Add($"'{row.Candidate}', its evaluator moved from {row.EvaluatorVersion} to {evaluator.Version}");
            }
            else if (IndexRules.Refusal(evaluator, parameters) is { } refusal)
            {
                skipped.Add($"'{row.Candidate}', {refusal}");
            }
            else
            {
                rules.Add(new IndexRule(row.Candidate, evaluator, parameters, row.RegisteredAt));
            }
        }

        return (rules, skipped);
    }

    // The readings a night's index rules read: each member's industry figures and coverage and its peers' surprise over
    // the sessions a drift's reaction can reach, the switches and the S&P 500's breadth, as the night stored them.
    public static async Task<IndexLevels> LevelsAsync(SqliteConnection connection, IndexNightInputs inputs, CancellationToken cancellation)
    {
        var industry = new Dictionary<string, (double?, double?)>(StringComparer.Ordinal);
        var coverage = new Dictionary<string, bool?>(StringComparer.Ordinal);
        var peers = new Dictionary<(string, DateOnly), double?>();
        var from = inputs.Calendar[Math.Max(0, inputs.At - ReactionReach)];

        static double? Figure(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = MemberLevels;
            command.Parameters.AddWithValue("$index", inputs.Index);
            command.Parameters.AddWithValue("$from", Stamp(from));
            command.Parameters.AddWithValue("$night", Stamp(inputs.Night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (ticker, session) = (reader.GetString(0), Date(reader.GetString(1)));

                peers[(ticker, session)] = Figure(reader, 5);

                if (session == inputs.Night)
                {
                    industry[ticker] = (Figure(reader, 2), Figure(reader, 3));
                    coverage[ticker] = reader.IsDBNull(4) ? null : reader.GetInt64(4) == 1;
                }
            }
        }

        double?[] switches = [null, null, null, null];

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = inputs.Index == Core.Providers.FundHoldings.MidCapIndex ? MidSwitches : SmallSwitches;
            command.Parameters.AddWithValue("$night", Stamp(inputs.Night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            if (await reader.ReadAsync(cancellation))
            {
                switches = [Figure(reader, 0), Figure(reader, 1), Figure(reader, 2), Figure(reader, 3)];
            }
        }

        double? large = await ScalarAsync(connection, LargeBreadth, [("$night", Stamp(inputs.Night))], cancellation) is double breadth ? breadth : null;

        return new IndexLevels(industry, coverage, peers, switches[0], switches[1], switches[2], switches[3], large);
    }

    // The sessions back a drift's reaction can sit, its widest window and a margin.
    const int ReactionReach = IndexSweepRunner.DriftWideWindow + 5;

    const string MemberLevels = @"
        SELECT ticker, session_date, industry_month, industry_quarter, coverage, peer_surprise FROM member_reading
        WHERE index_code = $index AND session_date >= $from AND session_date <= $night;
    ";

    const string MidSwitches = "SELECT ijh_half_year, ijh_year, hyg_average, hyg_change FROM switch_reading WHERE session_date = $night;";

    const string SmallSwitches = "SELECT ijr_half_year, ijr_year, hyg_average, hyg_change FROM switch_reading WHERE session_date = $night;";

    const string LargeBreadth = "SELECT breadth FROM market_reading WHERE session_date = $night;";

    // The cost a trade of the index pays, which its stored result is read after.
    // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
    public const string CostsPaid = "the published spread";

    // Each member's year of bars, every span the index held it for, its prints and surprises off the calendar, and its
    // quarters as filed.
    public static async Task<(IReadOnlyList<SweepName> Names, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income)> InputsAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        var tickers = new List<string>();

        await foreach (var row in RowsAsync(connection, MembersOf, [("$index", index), ("$session", Stamp(night))], cancellation))
        {
            if (!tickers.Contains(row.GetString(0), StringComparer.Ordinal))
            {
                tickers.Add(row.GetString(0));
            }
        }

        var names = new List<SweepName>();
        var income = new Dictionary<string, IReadOnlyList<FiledIncome>>(StringComparer.Ordinal);

        foreach (var ticker in tickers)
        {
            var spans = new List<(DateOnly?, DateOnly?)>();
            var bars = new List<SweepBar>();
            var prints = new List<DateOnly>();
            var surprises = new List<SweepSurprise>();
            var quarters = new List<FiledIncome>();

            await foreach (var row in RowsAsync(connection, SpansOf, [("$index", index), ("$ticker", ticker)], cancellation))
            {
                spans.Add((row.IsDBNull(0) ? null : Date(row.GetString(0)), row.IsDBNull(1) ? null : Date(row.GetString(1))));
            }

            await foreach (var row in RowsAsync(connection, BarsOf, [("$ticker", ticker), ("$night", Stamp(night))], cancellation))
            {
                bars.Add(new SweepBar(
                    Date(row.GetString(0)),
                    Money.FromStorage(row.GetString(1)),
                    Money.FromStorage(row.GetString(2)),
                    Money.FromStorage(row.GetString(3)),
                    row.GetInt64(4),
                    Money.FromStorage(row.GetString(5)),
                    row.IsDBNull(6) ? 0m : Money.FromStorage(row.GetString(6))));
            }

            await foreach (var row in RowsAsync(connection, PrintsOf, [("$ticker", ticker)], cancellation))
            {
                var date = Date(row.GetString(0));

                prints.Add(date);

                if (SweepHistory.SurpriseIn(row.GetString(2)) is { } percent)
                {
                    surprises.Add(new SweepSurprise(date, row.GetString(1) == "after", percent));
                }
            }

            await foreach (var row in RowsAsync(connection, QuartersOf, [("$ticker", ticker)], cancellation))
            {
                quarters.Add(new FiledIncome(
                    Date(row.GetString(0)),
                    Date(row.GetString(1)),
                    row.IsDBNull(2) ? null : Money.FromStorage(row.GetString(2)),
                    row.IsDBNull(3) ? null : Money.FromStorage(row.GetString(3)),
                    null));
            }

            names.Add(new SweepName(ticker, [.. bars], spans, [.. prints], null, surprises));
            income[ticker] = quarters;
        }

        return (names, income);
    }

    // The trade each of the names still holds on the night on any card of any index, with the index it is held in: the
    // S&P 500's by its own list's rule, and the S&P 400's and 600's while not ended or ended on the night itself.
    static async Task<(IReadOnlyDictionary<string, HeldTrade> Open, IReadOnlyDictionary<string, string> Index)> OpenAsync(SqliteConnection connection, DateOnly night, IReadOnlyList<string> tickers, CancellationToken cancellation, bool large = false)
    {
        var open = new Dictionary<string, HeldTrade>(StringComparer.Ordinal);
        var index = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var (ticker, held) in await FamilyLister.OpenTradesAsync(connection, night, tickers, cancellation))
        {
            open[ticker] = held;
            index[ticker] = LargeIndex;
        }

        var (elsewhere, heldIn) = await OpenIndexTradesAsync(connection, night, tickers, cancellation);

        foreach (var (ticker, held) in elsewhere)
        {
            if (open.TryAdd(ticker, held))
            {
                index[ticker] = heldIn[ticker];
            }
        }

        // On the S&P 500, a stock its own families listed tonight is held as by that trade, so it is listed once on the page.
        if (large)
        {
            var wanted = tickers.ToHashSet(StringComparer.Ordinal);

            await foreach (var row in RowsAsync(connection, ListedTonight, [("$night", Stamp(night))], cancellation))
            {
                if (wanted.Contains(row.GetString(0)) && open.TryAdd(row.GetString(0), new HeldTrade(row.GetString(1), night)))
                {
                    index[row.GetString(0)] = LargeIndex;
                }
            }
        }

        return (open, index);
    }

    // The stocks the S&P 500's own families listed on the night.
    const string ListedTonight = "SELECT ticker, family FROM family_pick WHERE session_date = $night AND state = 'listed' ORDER BY ticker;";

    // The code an S&P 500 card's trade is named by where a list of the S&P 400 or 600 holds a stock back for it.
    public const string LargeIndex = "GSPC";

    // The trade each of the names still holds on the night on an S&P 400 or 600 list, with that list's index: a trade
    // listed before the night that has not ended, or ended on the night itself, since a stock is free the night after
    // its trade ends. The S&P 500's list reads it to hold such a stock back too.
    // see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
    public static async Task<(IReadOnlyDictionary<string, HeldTrade> Open, IReadOnlyDictionary<string, string> Index)> OpenIndexTradesAsync(SqliteConnection connection, DateOnly night, IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        var open = new Dictionary<string, HeldTrade>(StringComparer.Ordinal);
        var index = new Dictionary<string, string>(StringComparer.Ordinal);
        var wanted = tickers.ToHashSet(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, IndexTrades, [("$night", Stamp(night))], cancellation))
        {
            var ticker = row.GetString(2);
            var ended = row.IsDBNull(4) ? (DateOnly?)null : Date(row.GetString(4));

            if (!wanted.Contains(ticker) || open.ContainsKey(ticker) || ended is { } on && on < night)
            {
                continue;
            }

            open[ticker] = new HeldTrade(row.GetString(1), Date(row.GetString(3)));
            index[ticker] = row.GetString(0);
        }

        return (open, index);
    }

    // Each of the index's trades not yet ended, walked over the member's closes since its night at the scale the
    // series has now, written where it ended with its result and its cost, its company valued as the member readings
    // read it under the index on the trade's night; the count ended.
    // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
    async Task<int> WalkAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        var open = new List<(string Family, string Ticker, DateOnly Session, decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap)>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = OpenIndexTrades;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$night", Stamp(night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                open.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    Date(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3)),
                    Money.FromStorage(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : Money.FromStorage(reader.GetString(6)),
                    reader.GetInt32(7)));
            }
        }

        var ended = 0;

        foreach (var trade in open)
        {
            var bars = new List<(DateOnly Session, decimal Close, decimal Raw)>();

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT session_date, close, raw_close FROM bar WHERE ticker = $ticker AND session_date >= $from AND session_date <= $night ORDER BY session_date;";
                command.Parameters.AddWithValue("$ticker", trade.Ticker);
                command.Parameters.AddWithValue("$from", Stamp(trade.Session));
                command.Parameters.AddWithValue("$night", Stamp(night));

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    bars.Add((Date(reader.GetString(0)), Money.FromStorage(reader.GetString(1)), reader.IsDBNull(2) ? 0m : Money.FromStorage(reader.GetString(2))));
                }
            }

            if (bars.Count == 0 || bars[0].Session != trade.Session || bars[0].Close <= 0m)
            {
                continue;
            }

            // The series' scale now over the scale its night stored the trade at.
            var scale = Statistic.FromRatio(bars[0].Close / trade.Entry);
            var closes = bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray();
            var entry = Statistic.FromPrice(trade.Entry) * scale;
            var stop = Statistic.FromPrice(trade.Stop) * scale;
            int sessions;
            double? result;

            if (trade.Trail is { } trail)
            {
                result = FamilyWalks.Trailing(closes, 0, entry, stop, Statistic.FromPrice(trail) * scale, trade.Cap, out sessions);
            }
            else
            {
                result = FamilyWalks.Fixed(closes, 0, entry, stop, Statistic.FromPrice(trade.Target ?? trade.Entry) * scale, trade.Cap, out sessions);
            }

            if (result is not { } multiple)
            {
                continue;
            }

            var sale = bars[sessions];
            decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", index), ("$session", Stamp(trade.Session)), ("$ticker", trade.Ticker)], cancellation) is string stored ? Money.FromStorage(stored) : null;
            var cost = TradeCost.InPercent(value, bars[0].Raw > 0m ? bars[0].Raw : bars[0].Close, sale.Raw > 0m ? sale.Raw : sale.Close) / 100.0 / ((entry - stop) / entry);

            await ExecuteAsync(connection, transaction, EndTrade,
            [
                ("$index", index), ("$family", trade.Family), ("$ticker", trade.Ticker), ("$session", Stamp(trade.Session)),
                ("$ended_on", Stamp(sale.Session)), ("$result", multiple), ("$cost", cost),
            ], cancellation);
            ended++;
        }

        return ended;
    }

    static object Stored(decimal? value) => value is { } price ? Money.ToStorage(price) : DBNull.Value;

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

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteScalarAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
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

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

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

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// What an index's sector heavyweights did on the night: whether the book rebalanced, the holdings bought, ended and
// held at the close, and why a rebalance waits where one waits.
public sealed record IndexHeavyweightsOutcome(bool Rebalanced, int Entered, int Ended, int Held, string? Waits = null);

// One member of the index as the heavyweights' design (b) reads it on a rebalance: its ticker, its industry and its
// sector as filed, its return over the rule's window, and whether it clears the floors and the quality.
public sealed record IndexFollower(string Ticker, string? Industry, string? Sector, double? Return, bool Clears);

// The S&P 400's and 600's sector heavyweights on their provisional settings, design (a) read within each index with the
// sweep's own code, kept by the index families in their step: on the first night of a month the book reads, each
// sector's ten largest members of the index by value as it stood, its return its members' mean and a leader's beta at
// least one against the index's fund, the two leading their sector above nothing bought among the members clearing the
// floors and the profit gate, each bought with its lead, and a holding the rule would no longer buy sold at the close;
// every night each holding and its size cut carried by the night's closes, and a holding whose stock left the index sold
// at its last close as a member, read from the bar table whatever the night's members. A holding's result is its growth
// less one, beside its size cut's, and its cost the published table's round trip at its company's value on its buy. A
// rebalance waits for a night the index's members hold the sessions its readings need, and one reading no lead, or no
// beta where it reads one, waits the same way, selling and buying nothing.
// see: The 400 and 600 each sweep two heavyweight designs and keep the stronger after costs
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
// see: A heavyweight leaving the index is sold at its last session's close as a member
// see: An S&P 400 or 600 heavyweights holding keeps the lead it was bought on
public static class IndexHeavyweights
{
    // Design (a) within an index at the S&P 500's frozen settings, a sector's return its members' mean.
    public static HeavyweightSetting Provisional { get; } = HeavyweightSweep.Frozen with { Sector = HeavyweightSectorReturn.Members };

    public const string NoLongerTheLeader = "no longer the leader";

    public const string LeftTheIndex = "left the index";

    const string LastRebalance = "SELECT MAX(session_date) FROM index_family_night WHERE index_code = $index AND rebalanced = 1 AND session_date < $night;";

    const string Companies = @"
        SELECT c.ticker, c.cik, c.sector FROM company c
        WHERE c.fetched_at = (SELECT MAX(fetched_at) FROM company WHERE ticker = c.ticker);
    ";

    const string Counts = @"
        SELECT r.ticker, r.period_end, r.filing_date, r.shares, r.basis_session FROM reported_quarter r
        WHERE r.shares IS NOT NULL AND r.filing_date IS NOT NULL AND r.basis_session IS NOT NULL;
    ";

    const string FundCloses = "SELECT session_date, close FROM market_bar WHERE series = $series AND session_date <= $night ORDER BY session_date;";

    const string OpenHoldings = @"
        SELECT ticker, entered_on, sector, entry_close, growth, cut, through FROM index_heavyweight_holding
        WHERE index_code = $index AND ended_on IS NULL AND entered_on < $night;
    ";

    const string UndoTheNight = @"
        DELETE FROM index_heavyweight_holding WHERE index_code = $index AND entered_on = $night;
        UPDATE index_heavyweight_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL, cost = NULL
        WHERE index_code = $index AND ended_on = $night;
    ";

    const string Carry = @"
        UPDATE index_heavyweight_holding SET growth = $growth, cut = $cut, through = $through
        WHERE index_code = $index AND ticker = $ticker AND entered_on = $entered_on;
    ";

    const string End = @"
        UPDATE index_heavyweight_holding
        SET ended_on = $ended_on, exit_close = $exit_close, reason = $reason, result = $result, cut_return = $cut_return, cost = $cost
        WHERE index_code = $index AND ticker = $ticker AND entered_on = $entered_on;
    ";

    const string Buy = @"
        INSERT INTO index_heavyweight_holding (index_code, ticker, entered_on, sector, entry_close, growth, cut, through, lead)
        VALUES ($index, $ticker, $night, $sector, $entry_close, 1.0, $cut, $night, $lead);
    ";

    // A stock's close on a session as the bar table holds it, whatever the night's members.
    const string CloseOnSession = "SELECT close FROM bar WHERE ticker = $ticker AND session_date = $session;";

    // Every holding of the index books sold on leaving the index.
    const string Leavers = "SELECT index_code, ticker, entered_on, ended_on, entry_close, exit_close FROM index_heavyweight_holding WHERE reason = $left ORDER BY index_code, ended_on, ticker;";

    const string SoldAgain = @"
        UPDATE index_heavyweight_holding SET exit_close = $exit_close, cost = $cost
        WHERE index_code = $index AND ticker = $ticker AND entered_on = $entered_on;
    ";

    // One stock of a size cut, its growth carried as a holding's is and the session it was carried to.
    sealed record CutMember(string Ticker, double Growth, string Through);

    public static async Task<IndexHeavyweightsOutcome> RunAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        DateOnly night,
        IReadOnlyList<SweepName> names,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        CancellationToken cancellation)
    {
        await ExecuteAsync(connection, transaction, UndoTheNight, [("$index", index), ("$night", Stamp(night))], cancellation);

        var bars = names.ToDictionary(name => name.Ticker, name => name.Bars.ToDictionary(bar => bar.Session), StringComparer.Ordinal);
        var members = names.Where(name => name.MemberOn(night)).Select(name => name.Ticker).ToHashSet(StringComparer.Ordinal);
        var (entered, ended) = (0, 0);

        decimal? CloseOn(string ticker, DateOnly session) => bars.TryGetValue(ticker, out var held) && held.TryGetValue(session, out var bar) ? bar.Close : null;

        // Each open holding and its size cut carried by tonight's closes, and one whose stock left the index sold at the
        // session it was last carried to, its last close as a member, which the night's members hold no bar of.
        var open = new List<(string Ticker, DateOnly Entered, string Sector, decimal Entry, double Growth, List<CutMember> Cut, DateOnly Through)>();

        await foreach (var row in RowsAsync(connection, transaction, OpenHoldings, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            open.Add((row.GetString(0), Date(row.GetString(1)), row.GetString(2), Money.FromStorage(row.GetString(3)), row.GetDouble(4), JsonSerializer.Deserialize<List<CutMember>>(row.GetString(5)) ?? [], Date(row.GetString(6))));
        }

        var held = new Dictionary<string, (DateOnly Entered, string Sector, decimal Entry, double Growth, List<CutMember> Cut)>(StringComparer.Ordinal);

        foreach (var holding in open)
        {
            if (!members.Contains(holding.Ticker))
            {
                var member = await ScalarAsync(connection, transaction, CloseOnSession, [("$ticker", holding.Ticker), ("$session", Stamp(holding.Through))], cancellation) is string close ? Money.FromStorage(close) : holding.Entry;

                await EndAsync(connection, transaction, index, holding.Ticker, holding.Entered, holding.Through, member, LeftTheIndex, holding.Growth, holding.Cut, holding.Entry, cancellation);
                ended++;

                continue;
            }

            var growth = CloseOn(holding.Ticker, holding.Through) is { } then && then > 0m && CloseOn(holding.Ticker, night) is { } now
                ? holding.Growth * Statistic.FromRatio(now / then)
                : holding.Growth;
            var cut = holding.Cut
                .Select(one => CloseOn(one.Ticker, Date(one.Through)) is { } then && then > 0m && CloseOn(one.Ticker, night) is { } now
                    ? one with { Growth = one.Growth * Statistic.FromRatio(now / then), Through = Stamp(night) }
                    : one)
                .ToList();

            await ExecuteAsync(connection, transaction, Carry, [("$index", index), ("$ticker", holding.Ticker), ("$entered_on", Stamp(holding.Entered)), ("$growth", growth), ("$cut", JsonSerializer.Serialize(cut)), ("$through", Stamp(night))], cancellation);
            held[holding.Ticker] = (holding.Entered, holding.Sector, holding.Entry, growth, cut);
        }

        var last = await ScalarAsync(connection, transaction, LastRebalance, [("$index", index), ("$night", Stamp(night))], cancellation) is string stored ? Date(stored) : (DateOnly?)null;

        if (!HeavyweightRule.Rebalances(night, last))
        {
            return new IndexHeavyweightsOutcome(false, 0, ended, held.Count);
        }

        // The rebalance: the sweep's own reading of the night's session within the index, the members clearing the floors
        // and the gate, which waits where it cannot read.
        var rebalance = await ReadAsync(connection, transaction, index, night, names, Provisional, name => Clears(index, name, night, income.GetValueOrDefault(name.Ticker) ?? []), cancellation);

        if (rebalance.Waits is { } waits)
        {
            return new IndexHeavyweightsOutcome(false, 0, ended, held.Count, waits);
        }

        var leaders = rebalance.Leaders.ToDictionary(one => one.Ticker, StringComparer.Ordinal);

        foreach (var (ticker, holding) in held.ToArray())
        {
            if (!leaders.ContainsKey(ticker))
            {
                await EndAsync(connection, transaction, index, ticker, holding.Entered, night, CloseOn(ticker, night) ?? holding.Entry, NoLongerTheLeader, holding.Growth, holding.Cut, holding.Entry, cancellation);
                held.Remove(ticker);
                ended++;
            }
        }

        foreach (var leader in rebalance.Leaders.Where(leader => !held.ContainsKey(leader.Ticker)))
        {
            if (CloseOn(leader.Ticker, night) is not { } close)
            {
                continue;
            }

            var cut = leader.Cut.Select(ticker => new CutMember(ticker, 1.0, Stamp(night))).ToList();

            await ExecuteAsync(connection, transaction, Buy, [("$index", index), ("$ticker", leader.Ticker), ("$night", Stamp(night)), ("$sector", leader.Sector), ("$entry_close", Money.ToStorage(close)), ("$cut", JsonSerializer.Serialize(cut)), ("$lead", rebalance.Leads.TryGetValue(leader.Ticker, out var lead) ? lead : DBNull.Value)], cancellation);
            held[leader.Ticker] = (night, leader.Sector, close, 1.0, cut);
            entered++;
        }

        return new IndexHeavyweightsOutcome(true, entered, ended, held.Count);
    }

    // The leaders a rebalance on the night buys within the index at a setting, each with its sector and the size cut it
    // was chosen from, and the lead of every company ranked in a sector's size cut: the sweep's tape laid over the
    // members' year with each company's value from the newest count filed before the session, a beta against the index's
    // fund, and the members failing the floors or the quality read by none; or why the rebalance waits, the members
    // holding no close for the night, fewer sessions to it than the setting's readings need, or reading no lead, or no
    // beta where the setting reads one.
    static async Task<(IReadOnlyList<(string Ticker, string Sector, IReadOnlyList<string> Cut)> Leaders, IReadOnlyDictionary<string, double> Leads, int Read, string? Waits)> ReadAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        DateOnly night,
        IReadOnlyList<SweepName> names,
        HeavyweightSetting setting,
        Func<SweepName, bool> clears,
        CancellationToken cancellation)
    {
        var companies = new Dictionary<string, (string? Cik, string? Sector)>(StringComparer.Ordinal);
        var counts = new Dictionary<string, List<FiledCount>>(StringComparer.Ordinal);
        var fund = new List<(DateOnly Session, double Close)>();

        await foreach (var row in RowsAsync(connection, transaction, Companies, [], cancellation))
        {
            companies[row.GetString(0)] = (row.IsDBNull(1) ? null : row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2));
        }

        await foreach (var row in RowsAsync(connection, transaction, Counts, [], cancellation))
        {
            if (!counts.TryGetValue(row.GetString(0), out var list))
            {
                counts[row.GetString(0)] = list = [];
            }

            list.Add(new FiledCount(Date(row.GetString(1)), Date(row.GetString(2)), Money.FromStorage(row.GetString(3)), Date(row.GetString(4))));
        }

        await foreach (var row in RowsAsync(connection, transaction, FundCloses, [("$series", IndexSweepFunds[index]), ("$night", Stamp(night))], cancellation))
        {
            fund.Add((Date(row.GetString(0)), Statistic.FromPrice(Money.FromStorage(row.GetString(1)))));
        }

        var calendar = names.SelectMany(name => name.Bars.Select(bar => bar.Session)).Where(session => session <= night).Distinct().Order().ToArray();
        var at = Array.IndexOf(calendar, night);

        if (at < 0)
        {
            return ([], NoLeads, 0, FormattableString.Invariant($"the rebalance waits, since no member of the index holds a close on {night:yyyy-MM-dd}"));
        }

        if (HeavyweightRule.WaitsForSessions(setting.Reading, calendar.Length, night) is { } few)
        {
            return ([], NoLeads, 0, few);
        }

        var inputs = new SweepHistoryInputs(night, calendar, names, 0, 0, 0, 0, string.Empty);
        var history = new HeavyweightHistory(
            companies,
            counts.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<FiledCount>)pair.Value, StringComparer.Ordinal),
            new Dictionary<string, IReadOnlyList<FiledSplit>>(StringComparer.Ordinal),
            [],
            []);
        var (tape, sessions) = HeavyweightSweep.Lay(inputs, history, fund.Count > 0 ? new SweepMarketSeries(IndexSweepFunds[index], fund, string.Empty) : null, new HashSet<int> { at }, 0);

        if (!sessions.TryGetValue(at, out var session))
        {
            return ([], NoLeads, 0, FormattableString.Invariant($"the rebalance waits, since no member of the index holds a close on {night:yyyy-MM-dd}"));
        }

        var kept = session.Members
            .Where(candidate => clears(names[candidate.Name]))
            .ToArray();
        var sectors = HeavyweightSweep.Sectors(session with { Members = kept }, setting);
        var betas = kept.ToDictionary(candidate => candidate.Member.Ticker, candidate => candidate.Member.Beta, StringComparer.Ordinal);

        if (HeavyweightRule.ReadNothing(sectors, setting.Reading, ticker => betas.GetValueOrDefault(ticker), night) is { } unread)
        {
            return ([], NoLeads, kept.Length, unread);
        }

        return (
            [.. sectors.SelectMany(sector => sector.Leaders.Select(leader => (leader, sector.Sector, (IReadOnlyList<string>)[.. sector.Largest.Select(ranked => ranked.Ticker)])))],
            sectors.SelectMany(sector => sector.Largest).Where(ranked => ranked.Lead is not null).ToDictionary(ranked => ranked.Ticker, ranked => ranked.Lead!.Value, StringComparer.Ordinal),
            kept.Length,
            null);
    }

    static IReadOnlyDictionary<string, double> NoLeads { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

    // The fund each index's beta is read against.
    static IReadOnlyDictionary<string, string> IndexSweepFunds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [FundHoldings.MidCapIndex] = "IJH",
        [FundHoldings.SmallCapIndex] = "IJR",
    };

    // Whether a member clears the price and dollar volume floors and the profit gate on the night.
    static bool Clears(string index, SweepName name, DateOnly night, IReadOnlyList<FiledIncome> income)
    {
        var upTo = name.Bars.Where(bar => bar.Session <= night).ToArray();

        if (upTo.Length == 0 || upTo[^1].Session != night)
        {
            return false;
        }

        var close = upTo[^1];
        var window = upTo[Math.Max(0, upTo.Length - MemberReadings.DollarVolumeSessions)..].Select(bar => (bar.Close, bar.Volume)).ToArray();

        return MemberReadings.ClearsTheFloors(index, close.RawClose > 0m ? close.RawClose : close.Close, MemberReadings.DollarVolume(window))
            && MemberReadings.Profit(income, night);
    }

    // The words a registered rule's holding ends on beside the book's own.
    public const string UnderTheAverage = "a close under its 200-day average";

    public const string NoLongerBought = "no longer among those it buys";

    const string UndoTheRuleNight = @"
        DELETE FROM index_heavyweight_rule_holding WHERE candidate = $candidate AND entered_on = $night;
        UPDATE index_heavyweight_rule_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL, cost = NULL
        WHERE candidate = $candidate AND ended_on = $night;
        DELETE FROM index_heavyweight_rule_night WHERE candidate = $candidate AND session_date = $night;
    ";

    const string RuleOpenHoldings = @"
        SELECT ticker, entered_on, sector, entry_close, growth, cut, through FROM index_heavyweight_rule_holding
        WHERE candidate = $candidate AND ended_on IS NULL AND entered_on < $night;
    ";

    const string RuleLastRebalance = "SELECT MAX(session_date) FROM index_heavyweight_rule_night WHERE candidate = $candidate AND session_date < $night;";

    const string RuleCarry = @"
        UPDATE index_heavyweight_rule_holding SET growth = $growth, cut = $cut, through = $through
        WHERE candidate = $candidate AND ticker = $ticker AND entered_on = $entered_on;
    ";

    const string RuleEnd = @"
        UPDATE index_heavyweight_rule_holding
        SET ended_on = $ended_on, exit_close = $exit_close, reason = $reason, result = $result, cut_return = $cut_return, cost = $cost
        WHERE candidate = $candidate AND ticker = $ticker AND entered_on = $entered_on;
    ";

    const string RuleBuy = @"
        INSERT INTO index_heavyweight_rule_holding (candidate, index_code, ticker, entered_on, sector, entry_close, growth, cut, through, lead)
        VALUES ($candidate, $index, $ticker, $night, $sector, $entry_close, 1.0, $cut, $night, $lead);
    ";

    const string RuleNight = @"
        INSERT INTO index_heavyweight_rule_night (candidate, index_code, session_date, bought, sold)
        VALUES ($candidate, $index, $night, $bought, $sold);
    ";

    // Every industry's S&P 500 members' return over a month and a quarter as the member readings stored them on the night,
    // read off every index's rows since each row of an industry carries the same.
    const string IndustryReturns = @"
        SELECT industry, MAX(industry_month), MAX(industry_quarter) FROM member_reading
        WHERE session_date = $night AND industry IS NOT NULL
        GROUP BY industry;
    ";

    // Each member's industry as the member readings stored it on the night under its index.
    const string MemberIndustries = "SELECT ticker, industry FROM member_reading WHERE index_code = $index AND session_date = $night;";

    const string SpyCloses = "SELECT session_date, close FROM market_bar WHERE series = 'SPY' AND session_date IN ($from, $night);";

    // A company's value on a holding's own night as the member readings read it under the index, none where they read
    // none.
    const string ValueOn = "SELECT company_value FROM member_reading WHERE index_code = $index AND session_date = $session AND ticker = $ticker;";

    // The index's fund's close on the night, which a beta is read against.
    const string FundCloseOn = "SELECT close FROM market_bar WHERE series = $series AND session_date = $night;";

    // Each registered heavyweights rule of the index keeping a book of its own on the night, as the index's own book is
    // kept: every open holding carried by tonight's closes, one whose stock left the index sold at its last close as a
    // member, read from the bar table whatever the night's members, one closing under its 200-day average sold there where
    // its rule reads that exit, and on a night its rule rebalances, its first or the first of a month, each holding the
    // rule would no longer buy sold where its rule sells on that, and each stock it buys and does not hold bought at
    // tonight's close: design (a)'s leaders read by the sweep's own code at the rule's setting with its size cut, each with
    // its lead, and design (b)'s followers with their sector's members in the index as theirs. A night run again deletes
    // what each rule bought that night, opens again what each sold that night and writes its rebalance again.
    // see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
    // see: A heavyweight leaving the index is sold at its last session's close as a member
    // see: An S&P 400 or 600 heavyweights holding keeps the lead it was bought on
    public static async Task<IndexHeavyweightsOutcome> RulesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        DateOnly night,
        IReadOnlyList<SweepName> names,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        IndexLevels levels,
        IReadOnlyList<IndexRule> rules,
        CancellationToken cancellation)
    {
        var (entered, ended, held, rebalanced) = (0, 0, 0, false);
        var waiting = new List<string>();
        var bars = names.ToDictionary(name => name.Ticker, name => name.Bars.ToDictionary(bar => bar.Session), StringComparer.Ordinal);
        var byTicker = names.ToDictionary(name => name.Ticker, StringComparer.Ordinal);
        var members = names.Where(name => name.MemberOn(night)).Select(name => name.Ticker).ToHashSet(StringComparer.Ordinal);

        decimal? CloseOn(string ticker, DateOnly session) => bars.TryGetValue(ticker, out var own) && own.TryGetValue(session, out var bar) ? bar.Close : null;

        foreach (var rule in rules)
        {
            var p = rule.Parameters;
            var designA = p[Core.Candidates.IndexHeavyweightCandidate.DesignParameter] == Core.Candidates.IndexHeavyweightCandidate.DesignA;
            var soldOnLeading = !designA || p[Core.Candidates.IndexHeavyweightCandidate.LeadingExitParameter] == 1;
            var soldUnderAverage = designA && p[Core.Candidates.IndexHeavyweightCandidate.AverageExitParameter] == 1;
            var quality = (IndexQuality)(int)p[Core.Candidates.IndexRuleCandidate.QualityParameter];
            var multiple = p[Core.Candidates.IndexRuleCandidate.FloorsParameter] == 2 ? 2m : 1m;
            var key = new (string, object)[] { ("$candidate", rule.Candidate), ("$night", Stamp(night)) };

            bool Clears(SweepName name) => ClearsAt(index, name, night, income.GetValueOrDefault(name.Ticker) ?? [], quality, multiple, levels.Coverage.GetValueOrDefault(name.Ticker));

            await ExecuteAsync(connection, transaction, UndoTheRuleNight, key, cancellation);

            var open = new List<(string Ticker, DateOnly Entered, string Sector, decimal Entry, double Growth, List<CutMember> Cut, DateOnly Through)>();

            await foreach (var row in RowsAsync(connection, transaction, RuleOpenHoldings, key, cancellation))
            {
                open.Add((row.GetString(0), Date(row.GetString(1)), row.GetString(2), Money.FromStorage(row.GetString(3)), row.GetDouble(4), JsonSerializer.Deserialize<List<CutMember>>(row.GetString(5)) ?? [], Date(row.GetString(6))));
            }

            var holding = new Dictionary<string, (DateOnly Entered, decimal Entry, double Growth, List<CutMember> Cut)>(StringComparer.Ordinal);

            foreach (var one in open)
            {
                if (!members.Contains(one.Ticker))
                {
                    var member = await ScalarAsync(connection, transaction, CloseOnSession, [("$ticker", one.Ticker), ("$session", Stamp(one.Through))], cancellation) is string closed ? Money.FromStorage(closed) : one.Entry;

                    await RuleEndAsync(connection, transaction, index, rule.Candidate, one.Ticker, one.Entered, one.Through, member, LeftTheIndex, one.Growth, one.Cut, one.Entry, cancellation);
                    ended++;

                    continue;
                }

                var growth = CloseOn(one.Ticker, one.Through) is { } then && then > 0m && CloseOn(one.Ticker, night) is { } now
                    ? one.Growth * Statistic.FromRatio(now / then)
                    : one.Growth;
                var cut = one.Cut
                    .Select(member => CloseOn(member.Ticker, Date(member.Through)) is { } then && then > 0m && CloseOn(member.Ticker, night) is { } now
                        ? member with { Growth = member.Growth * Statistic.FromRatio(now / then), Through = Stamp(night) }
                        : member)
                    .ToList();

                await ExecuteAsync(connection, transaction, RuleCarry, [.. key, ("$ticker", one.Ticker), ("$entered_on", Stamp(one.Entered)), ("$growth", growth), ("$cut", JsonSerializer.Serialize(cut)), ("$through", Stamp(night))], cancellation);

                if (soldUnderAverage && CloseOn(one.Ticker, night) is { } close && HeavyweightRule.Broken(Statistic.FromPrice(close), Average200(byTicker[one.Ticker], night)))
                {
                    await RuleEndAsync(connection, transaction, index, rule.Candidate, one.Ticker, one.Entered, night, close, UnderTheAverage, growth, cut, one.Entry, cancellation);
                    ended++;

                    continue;
                }

                holding[one.Ticker] = (one.Entered, one.Entry, growth, cut);
            }

            var last = await ScalarAsync(connection, transaction, RuleLastRebalance, key, cancellation) is string stored ? Date(stored) : (DateOnly?)null;

            // A rebalance waits for a night holding what its design reads, so a series or a reading the night could not
            // store moves no holding: the index's fund's close where design (a) reads a beta, the sessions its readings
            // need and a lead, or a beta where it reads one, and the sessions of its window, SPY's closes and the
            // industries' returns design (b) reads.
            var setting = designA ? IndexRules.HeavyweightSettingOf(p) : null;
            IReadOnlyList<(string Ticker, string Sector, IReadOnlyList<string> Cut)>? buys = null;
            var leads = NoLeads;

            if (HeavyweightRule.Rebalances(night, last))
            {
                if (setting is null)
                {
                    buys = await FollowersAsync(connection, transaction, index, night, names, p, Clears, cancellation);
                }
                else if (!setting.HighBeta || await ScalarAsync(connection, transaction, FundCloseOn, [("$series", IndexSweepFunds[index]), ("$night", Stamp(night))], cancellation) is not null)
                {
                    var read = await ReadAsync(connection, transaction, index, night, names, setting, Clears, cancellation);

                    if (read.Waits is { } waits)
                    {
                        waiting.Add($"'{rule.Candidate}' {waits}");
                    }
                    else
                    {
                        (buys, leads) = (read.Leaders, read.Leads);
                    }
                }
            }

            if (buys is not null)
            {
                rebalanced = true;

                var buying = buys.Select(buy => buy.Ticker).ToHashSet(StringComparer.Ordinal);
                var (bought, sold) = (0, 0);

                foreach (var (ticker, one) in holding.ToArray())
                {
                    if (soldOnLeading && !buying.Contains(ticker))
                    {
                        await RuleEndAsync(connection, transaction, index, rule.Candidate, ticker, one.Entered, night, CloseOn(ticker, night) ?? one.Entry, designA ? NoLongerTheLeader : NoLongerBought, one.Growth, one.Cut, one.Entry, cancellation);
                        holding.Remove(ticker);
                        sold++;
                    }
                }

                foreach (var buy in buys.Where(buy => !holding.ContainsKey(buy.Ticker)))
                {
                    if (CloseOn(buy.Ticker, night) is not { } close)
                    {
                        continue;
                    }

                    var cut = buy.Cut.Select(ticker => new CutMember(ticker, 1.0, Stamp(night))).ToList();

                    await ExecuteAsync(connection, transaction, RuleBuy, [.. key, ("$index", index), ("$ticker", buy.Ticker), ("$sector", buy.Sector), ("$entry_close", Money.ToStorage(close)), ("$cut", JsonSerializer.Serialize(cut)), ("$lead", leads.TryGetValue(buy.Ticker, out var lead) ? lead : DBNull.Value)], cancellation);
                    holding[buy.Ticker] = (night, close, 1.0, cut);
                    bought++;
                }

                await ExecuteAsync(connection, transaction, RuleNight, [.. key, ("$index", index), ("$bought", bought), ("$sold", sold)], cancellation);
                (entered, ended) = (entered + bought, ended + sold);
            }

            held += holding.Count;
        }

        return new IndexHeavyweightsOutcome(rebalanced, entered, ended, held, waiting.Count > 0 ? string.Join("; ", waiting) : null);
    }

    // Whether a member clears the price floor and the dollar volume floor at a multiple of it, and the quality on the
    // night: none, the profit gate, or the gate and the interest cover as the member readings stored it.
    static bool ClearsAt(string index, SweepName name, DateOnly night, IReadOnlyList<FiledIncome> income, IndexQuality quality, decimal multiple, bool? coverage)
    {
        var upTo = name.Bars.Where(bar => bar.Session <= night).ToArray();

        if (upTo.Length == 0 || upTo[^1].Session != night)
        {
            return false;
        }

        var close = upTo[^1];
        var window = upTo[Math.Max(0, upTo.Length - MemberReadings.DollarVolumeSessions)..].Select(bar => (bar.Close, bar.Volume)).ToArray();

        return MemberReadings.ClearsTheFloors(index, close.RawClose > 0m ? close.RawClose : close.Close, MemberReadings.DollarVolume(window) / multiple)
            && quality switch
            {
                IndexQuality.Off => true,
                IndexQuality.Profit => MemberReadings.Profit(income, night),
                _ => MemberReadings.Profit(income, night) && coverage == true,
            };
    }

    // A member's 200-session average on the night over its own bars, none where it holds too few.
    static double? Average200(SweepName name, DateOnly night)
    {
        var series = name.Bars.Where(bar => bar.Session <= night).ToArray();

        if (series.Length == 0 || series[^1].Session != night)
        {
            return null;
        }

        var (_, twoHundred) = SweepColumns.Averages(series);

        return double.IsNaN(twoHundred[^1]) ? null : twoHundred[^1];
    }

    // Design (b)'s buys on the night: the industries leading SPY over the rule's window as the member readings and the
    // fund's closes read it, and in each the members the rule buys, read by the followers' own reading; none where the
    // night holds too few sessions, SPY's close on it or the window's sessions before, or no industry's return.
    static async Task<IReadOnlyList<(string Ticker, string Sector, IReadOnlyList<string> Cut)>?> FollowersAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        DateOnly night,
        IReadOnlyList<SweepName> names,
        IReadOnlyDictionary<string, double> p,
        Func<SweepName, bool> clears,
        CancellationToken cancellation)
    {
        var window = (int)p[Core.Candidates.IndexHeavyweightCandidate.WindowParameter];
        var calendar = names.SelectMany(name => name.Bars.Select(bar => bar.Session)).Where(session => session <= night).Distinct().Order().ToArray();
        var at = Array.IndexOf(calendar, night);

        if (at < 0 || HeavyweightRule.WaitsForSessions(new HeavyweightSettings(1, window, 1), at + 1, night) is not null)
        {
            return null;
        }

        var from = calendar[at - window];
        var returns = new Dictionary<string, double>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, IndustryReturns, [("$night", Stamp(night))], cancellation))
        {
            var column = window == MemberReadings.IndustryWindows[0] ? 1 : 2;

            if (!row.IsDBNull(column))
            {
                returns[row.GetString(0)] = row.GetDouble(column);
            }
        }

        var spy = new Dictionary<DateOnly, decimal>();

        await foreach (var row in RowsAsync(connection, transaction, SpyCloses, [("$from", Stamp(from)), ("$night", Stamp(night))], cancellation))
        {
            spy[Date(row.GetString(0))] = Money.FromStorage(row.GetString(1));
        }

        var industries = new Dictionary<string, string?>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, MemberIndustries, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            industries[row.GetString(0)] = row.IsDBNull(1) ? null : row.GetString(1);
        }

        var sectors = new Dictionary<string, string?>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, Companies, [], cancellation))
        {
            sectors[row.GetString(0)] = row.IsDBNull(2) ? null : row.GetString(2);
        }

        double? spyReturn = spy.TryGetValue(night, out var now) && spy.TryGetValue(from, out var then) && then > 0m ? Statistic.FromRatio(now / then) - 1.0 : null;

        if (spyReturn is null || returns.Count == 0)
        {
            return null;
        }

        IndexFollower Follower(SweepName name)
        {
            var own = name.Bars.ToDictionary(bar => bar.Session);
            double? gained = own.TryGetValue(night, out var last) && own.TryGetValue(from, out var first) && first.Close > 0m ? Statistic.FromRatio(last.Close / first.Close) - 1.0 : null;

            return new IndexFollower(name.Ticker, industries.GetValueOrDefault(name.Ticker), sectors.GetValueOrDefault(name.Ticker), gained, clears(name));
        }

        return Followers(
            returns,
            spyReturn,
            [.. names.Where(name => name.MemberOn(night)).Select(Follower)],
            (int)p[Core.Candidates.IndexHeavyweightCandidate.IndustriesParameter],
            (int)p[Core.Candidates.IndexHeavyweightCandidate.MembersParameter]);
    }

    // Design (b)'s reading as its sweep reads it: the industries whose S&P 500 members' return over the window leads
    // SPY's over the same sessions, the strongest lead first and the industry's name settling a tie, at most the count
    // stated, nought reading every one leading; in each, the members clearing the floors and the quality with a return over
    // the window, the strongest first and the ticker settling a tie, at most the count stated; each bought with its
    // sector, none filed reading as none, and the sector's members in the index as its size cut. No industry leads where
    // SPY's return cannot be read.
    // see: The 400 and 600 each sweep two heavyweight designs and keep the stronger after costs
    public static IReadOnlyList<(string Ticker, string Sector, IReadOnlyList<string> Cut)> Followers(
        IReadOnlyDictionary<string, double> industryReturns,
        double? spyReturn,
        IReadOnlyList<IndexFollower> members,
        int industries,
        int perIndustry)
    {
        if (spyReturn is not { } market)
        {
            return [];
        }

        var kept = industryReturns
            .Select(pair => (Industry: pair.Key, Lead: pair.Value - market))
            .Where(pair => pair.Lead > 0)
            .OrderByDescending(pair => pair.Lead)
            .ThenBy(pair => pair.Industry, StringComparer.Ordinal)
            .Take(industries == 0 ? int.MaxValue : industries)
            .Select(pair => pair.Industry)
            .ToArray();

        static string SectorOf(IndexFollower member) => member.Sector ?? "none";

        return
        [
            .. kept.SelectMany(industry => members
                .Where(member => member.Industry == industry && member.Return is not null && member.Clears)
                .OrderByDescending(member => member.Return)
                .ThenBy(member => member.Ticker, StringComparer.Ordinal)
                .Take(perIndustry))
                .Select(member => (member.Ticker, SectorOf(member), (IReadOnlyList<string>)[.. members.Where(other => SectorOf(other) == SectorOf(member)).Select(other => other.Ticker)])),
        ];
    }

    static async Task RuleEndAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string candidate, string ticker, DateOnly entered, DateOnly on, decimal exit, string reason, double growth, List<CutMember> cut, decimal entry, CancellationToken cancellation)
    {
        double? cutReturn = cut.Count > 0 ? cut.Average(one => one.Growth) - 1.0 : null;
        decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", index), ("$session", Stamp(entered)), ("$ticker", ticker)], cancellation) is string stored ? Money.FromStorage(stored) : null;
        var cost = TradeCost.InPercent(value, entry, exit) / 100.0;

        await ExecuteAsync(connection, transaction, RuleEnd,
        [
            ("$candidate", candidate), ("$ticker", ticker), ("$entered_on", Stamp(entered)), ("$ended_on", Stamp(on)), ("$exit_close", Money.ToStorage(exit)),
            ("$reason", reason), ("$result", growth - 1.0), ("$cut_return", (object?)cutReturn ?? DBNull.Value), ("$cost", double.IsFinite(cost) ? cost : DBNull.Value),
        ], cancellation);
    }

    // Each holding of the index books sold on leaving the index whose close stands other than its stock's close on the
    // session it was sold, as the bar table holds it, written again at that close with its round trip from it; each
    // written named with the close it stood at and the close it was written at.
    public static async Task<IReadOnlyList<string>> SellLeaversAgainAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellation)
    {
        var leavers = new List<(string Index, string Ticker, DateOnly Entered, DateOnly Sold, decimal Entry, decimal Exit)>();

        await foreach (var row in RowsAsync(connection, transaction, Leavers, [("$left", LeftTheIndex)], cancellation))
        {
            leavers.Add((row.GetString(0), row.GetString(1), Date(row.GetString(2)), Date(row.GetString(3)), Money.FromStorage(row.GetString(4)), Money.FromStorage(row.GetString(5))));
        }

        var written = new List<string>();

        foreach (var leaver in leavers)
        {
            if (await ScalarAsync(connection, transaction, CloseOnSession, [("$ticker", leaver.Ticker), ("$session", Stamp(leaver.Sold))], cancellation) is not string stored)
            {
                continue;
            }

            var close = Money.FromStorage(stored);

            if (close == leaver.Exit)
            {
                continue;
            }

            decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", leaver.Index), ("$session", Stamp(leaver.Entered)), ("$ticker", leaver.Ticker)], cancellation) is string held ? Money.FromStorage(held) : null;

            await ExecuteAsync(connection, transaction, SoldAgain,
            [
                ("$index", leaver.Index), ("$ticker", leaver.Ticker), ("$entered_on", Stamp(leaver.Entered)),
                ("$exit_close", Money.ToStorage(close)), ("$cost", TradeCost.InPercent(value, leaver.Entry, close) / 100.0),
            ], cancellation);
            written.Add(FormattableString.Invariant($"{leaver.Index} {leaver.Ticker} bought {leaver.Entered:yyyy-MM-dd} and sold {leaver.Sold:yyyy-MM-dd}, at {Money.ToStorage(leaver.Exit)} and now at {Money.ToStorage(close)}"));
        }

        return written;
    }

    // A holding of the index's book sold, its round trip at the published table at its company's value on its buy.
    // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
    static async Task EndAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string ticker, DateOnly entered, DateOnly on, decimal exit, string reason, double growth, List<CutMember> cut, decimal entry, CancellationToken cancellation)
    {
        double? cutReturn = cut.Count > 0 ? cut.Average(one => one.Growth) - 1.0 : null;
        decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", index), ("$session", Stamp(entered)), ("$ticker", ticker)], cancellation) is string stored ? Money.FromStorage(stored) : null;
        var cost = TradeCost.InPercent(value, entry, exit) / 100.0;

        await ExecuteAsync(connection, transaction, End,
        [
            ("$index", index), ("$ticker", ticker), ("$entered_on", Stamp(entered)), ("$ended_on", Stamp(on)), ("$exit_close", Money.ToStorage(exit)),
            ("$reason", reason), ("$result", growth - 1.0), ("$cut_return", (object?)cutReturn ?? DBNull.Value), ("$cost", cost),
        ], cancellation);
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

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
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

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
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

// Each registered rule of an index's swing families keeping its own list, written by the index families in their step:
// every trade a rule kept that has not ended walked over its stock's closes since and ended at its stop, its target, its
// trail or its cap's last close, with its result and its round trip at the published table beside it and never in it;
// a trade whose cap's sessions have passed given the benchmark of the same plan on every member of the index that night;
// then each rule's members passing tonight kept in its family's order, five at most, none whose stock it holds a trade
// on. A night run again replaces the trades it kept that night.
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
// see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
public static class IndexRuleTrades
{
    // What a night did for an index's registered rules: the trades they kept, ended and benchmarked.
    public sealed record Kept(int Trades, int Ended, int Benchmarked);

    const string ClearTheNight = "DELETE FROM index_rule_trade WHERE index_code = $index AND session_date = $night;";

    // Every trade from before the night not yet ended or not yet benchmarked.
    const string Pending = @"
        SELECT candidate, ticker, session_date, entry, stop, target, trail, cap, risk_moves, reward_to_risk, ended_on, exit
        FROM index_rule_trade
        WHERE index_code = $index AND session_date < $night AND (ended_on IS NULL OR members IS NULL)
        ORDER BY session_date, candidate, ticker;
    ";

    const string BarsFrom = "SELECT session_date, close, raw_close FROM bar WHERE ticker = $ticker AND session_date >= $from AND session_date <= $night ORDER BY session_date;";

    // The company's value on a trade's own night as the member readings read it, none where they read none.
    const string ValueOn = "SELECT company_value FROM member_reading WHERE index_code = $index AND session_date = $session AND ticker = $ticker;";

    const string End = "UPDATE index_rule_trade SET ended_on = $ended_on, result = $result, cost = $cost WHERE candidate = $candidate AND ticker = $ticker AND session_date = $session;";

    const string Benchmarked = "UPDATE index_rule_trade SET benchmark = $benchmark, members = $members WHERE candidate = $candidate AND ticker = $ticker AND session_date = $session;";

    // The stocks a rule holds a trade on through the night, a trade ended on the night holding its stock until the next.
    const string Holding = "SELECT ticker FROM index_rule_trade WHERE candidate = $candidate AND (ended_on IS NULL OR ended_on >= $night);";

    const string Keep = @"
        INSERT INTO index_rule_trade (candidate, index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, risk_moves, reward_to_risk, exit)
        VALUES ($candidate, $index, $family, $ticker, $night, $place, $entry, $stop, $target, $trail, $cap, $risk_moves, $reward_to_risk, $exit);
    ";

    public static async Task<Kept> KeepAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string index,
        DateOnly night,
        IndexNightInputs? inputs,
        IReadOnlyList<(IndexRule Rule, IReadOnlyList<IndexAnswer> Answers)> rules,
        CancellationToken cancellation,
        Func<string, IReadOnlyList<double?>?>? readings = null)
    {
        await ExecuteAsync(connection, transaction, ClearTheNight, [("$index", index), ("$night", Stamp(night))], cancellation);

        if (inputs is null)
        {
            return new Kept(0, 0, 0);
        }

        var (ended, benchmarked) = await WalkAsync(connection, transaction, inputs, cancellation);
        var kept = 0;

        foreach (var (rule, answers) in rules)
        {
            var holding = new HashSet<string>(StringComparer.Ordinal);

            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = Holding;
                command.Parameters.AddWithValue("$candidate", rule.Candidate);
                command.Parameters.AddWithValue("$night", Stamp(night));

                await using var reader = await command.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    holding.Add(reader.GetString(0));
                }
            }

            var place = 0;

            // The rule's hooks as its registration states them: its list kept and ordered by them where they read the
            // night's readings, a rule reading them on a night that supplied none listing nothing; and the exit its
            // trades are walked under.
            // see: Every engine's settings hooks land together and all default off, so the families' pins move once
            var hooks = RuleHooks.Of(rule.Parameters);

            if (hooks.ReadsReadings && readings is null)
            {
                continue;
            }

            foreach (var answer in hooks.Order([.. answers.Where(one => one.Passed && !holding.Contains(one.Ticker)).OrderBy(one => one.Place)], one => readings?.Invoke(one.Ticker)).Take(IndexRules.PerNight))
            {
                var name = Array.FindIndex(inputs.Series, one => string.Equals(one.Name.Ticker, answer.Ticker, StringComparison.Ordinal));
                var move = inputs.Series[name].Atr[IndexNightRead.BarOf(inputs.Series[name], inputs.At)];
                var (entry, stop) = (answer.Entry!.Value, answer.Stop!.Value);
                var risk = Statistic.FromPrice(entry - stop);

                await ExecuteAsync(connection, transaction, Keep,
                [
                    ("$candidate", rule.Candidate), ("$index", index), ("$family", rule.Family), ("$ticker", answer.Ticker), ("$night", Stamp(night)),
                    ("$place", ++place), ("$entry", Money.ToStorage(entry)), ("$stop", Money.ToStorage(stop)), ("$target", Stored(answer.Target)), ("$trail", Stored(answer.Trail)),
                    ("$cap", answer.Cap!.Value), ("$risk_moves", move > 0 && risk > 0 ? risk / move : DBNull.Value),
                    ("$reward_to_risk", answer.Target is { } target && risk > 0 ? Statistic.FromPrice(target - entry) / risk : DBNull.Value),
                    ("$exit", hooks.Exit == 0 ? DBNull.Value : hooks.Exit),
                ], cancellation);
                kept++;
            }
        }

        return new Kept(kept, ended, benchmarked);
    }

    static async Task<(int Ended, int Benchmarked)> WalkAsync(SqliteConnection connection, SqliteTransaction transaction, IndexNightInputs inputs, CancellationToken cancellation)
    {
        var pending = new List<(string Candidate, string Ticker, DateOnly Session, decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, double? RiskMoves, double? RewardToRisk, bool Ended, int Exit)>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = Pending;
            command.Parameters.AddWithValue("$index", inputs.Index);
            command.Parameters.AddWithValue("$night", Stamp(inputs.Night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                pending.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    Date(reader.GetString(2)),
                    Money.FromStorage(reader.GetString(3)),
                    Money.FromStorage(reader.GetString(4)),
                    reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                    reader.IsDBNull(6) ? null : Money.FromStorage(reader.GetString(6)),
                    reader.GetInt32(7),
                    reader.IsDBNull(8) ? null : reader.GetDouble(8),
                    reader.IsDBNull(9) ? null : reader.GetDouble(9),
                    !reader.IsDBNull(10),
                    reader.IsDBNull(11) ? 0 : reader.GetInt32(11)));
            }
        }

        var (ended, benchmarked) = (0, 0);
        double[][]? closes = null;

        foreach (var trade in pending)
        {
            if (!trade.Ended && await EndAsync(connection, transaction, inputs, trade.Candidate, trade.Ticker, trade.Session, trade.Entry, trade.Stop, trade.Target, trade.Trail, trade.Cap, cancellation, trade.Exit, trade.RiskMoves))
            {
                ended++;
            }

            // The benchmark once every member's trade of the same plan has had its cap's sessions, through the sweeps' own
            // benchmark of the plan's shape: the breakout's for a stop that trails, and the drift's for a stop and a
            // target, which a pullback's plan is and its trade is walked as.
            var session = Array.IndexOf(inputs.Calendar, trade.Session);

            if (session < 0 || inputs.At - session < trade.Cap || trade.RiskMoves is not { } moves)
            {
                continue;
            }

            closes ??= [.. inputs.Series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];

            // A trade kept under an exit of the menu is benchmarked under the same exit, its trail its stop's distance as
            // the breakout's is.
            var (average, members) = ExitMenu.Of(trade.Exit) is { } exit
                ? ExitMenu.Benchmark(closes, inputs.Members.Names[session], inputs.Members.Bars[session], (name, bar) => inputs.Series[name].Atr[bar], moves, trade.Trail is null ? trade.RewardToRisk ?? 0 : null, trade.Trail is null ? null : 1, trade.Cap, exit)
                : trade.Trail is not null
                    ? BreakoutSweep.BenchmarkCounted(inputs.Series, closes, inputs.Members, session, moves, trade.Cap)
                    : DriftSweep.BenchmarkCounted(inputs.Series, closes, inputs.Members, session, moves, trade.RewardToRisk ?? 0, trade.Cap);

            await ExecuteAsync(connection, transaction, Benchmarked,
            [
                ("$candidate", trade.Candidate), ("$ticker", trade.Ticker), ("$session", Stamp(trade.Session)),
                ("$benchmark", double.IsNaN(average) ? DBNull.Value : average), ("$members", members),
            ], cancellation);
            benchmarked++;
        }

        return (ended, benchmarked);
    }

    // A trade walked over its stock's closes from its night to tonight at the scale the series holds tonight, ended where
    // its walk ends with its result and its round trip in multiples of its risk, its company valued as the member
    // readings read it on the trade's night; nothing where the closes do not reach an end.
    static async Task<bool> EndAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        IndexNightInputs inputs,
        string candidate,
        string ticker,
        DateOnly listed,
        decimal storedEntry,
        decimal storedStop,
        decimal? storedTarget,
        decimal? storedTrail,
        int cap,
        CancellationToken cancellation,
        int exitNumber = 0,
        double? riskMoves = null)
    {
        var bars = new List<(DateOnly Session, decimal Close, decimal Raw)>();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = BarsFrom;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$from", Stamp(listed));
            command.Parameters.AddWithValue("$night", Stamp(inputs.Night));

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                bars.Add((Date(reader.GetString(0)), Money.FromStorage(reader.GetString(1)), reader.IsDBNull(2) ? 0m : Money.FromStorage(reader.GetString(2))));
            }
        }

        if (bars.Count == 0 || bars[0].Session != listed || bars[0].Close <= 0m)
        {
            return false;
        }

        // The series' scale now over the scale its night stored the trade at.
        var scale = Statistic.FromRatio(bars[0].Close / storedEntry);
        var closes = bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray();
        var entry = Statistic.FromPrice(storedEntry) * scale;
        var stop = Statistic.FromPrice(storedStop) * scale;
        int sessions;
        double? result;

        // A trade kept under an exit of the menu is walked under it; one kept under its rule's own exit as it always was.
        if (ExitMenu.Of(exitNumber) is { } exit)
        {
            var anchor = new SetupAnchor(listed, entry, stop, storedTrail is null ? Statistic.FromPrice(storedTarget ?? storedEntry) * scale : null, storedTrail is { } trailing ? Statistic.FromPrice(trailing) * scale : null, cap, riskMoves);
            var outcome = ExitMenu.Replay(closes, 0, anchor, riskMoves is > 0 ? (entry - stop) / riskMoves.Value : double.NaN, exit);

            (result, sessions) = (outcome.Result, outcome.Sessions);
        }
        else
        {
            result = storedTrail is { } trail
                ? FamilyWalks.Trailing(closes, 0, entry, stop, Statistic.FromPrice(trail) * scale, cap, out sessions)
                : FamilyWalks.Fixed(closes, 0, entry, stop, Statistic.FromPrice(storedTarget ?? storedEntry) * scale, cap, out sessions);
        }

        if (result is not { } multiple)
        {
            return false;
        }

        var sale = bars[sessions];
        decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", inputs.Index), ("$session", Stamp(listed)), ("$ticker", ticker)], cancellation) is string stored ? Money.FromStorage(stored) : null;
        var cost = TradeCost.InPercent(value, bars[0].Raw > 0m ? bars[0].Raw : bars[0].Close, sale.Raw > 0m ? sale.Raw : sale.Close) / 100.0 / ((entry - stop) / entry);

        await ExecuteAsync(connection, transaction, End,
        [
            ("$candidate", candidate), ("$ticker", ticker), ("$session", Stamp(listed)),
            ("$ended_on", Stamp(sale.Session)), ("$result", multiple), ("$cost", double.IsFinite(cost) ? cost : DBNull.Value),
        ], cancellation);

        return true;
    }

    static object Stored(decimal? value) => value is { } price ? Money.ToStorage(price) : DBNull.Value;

    static string Stamp(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

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

    static async Task<object?> ScalarAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
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
}
