using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Dips;

// What the dip's step did on one index for the rule its card draws: the members it read, how many passed, how many it
// listed, how many it held back for a trade still open or for a stock another family listed that night, the trades it
// kept and ended, or the failure that stopped its part; and from 22.3 the rules it kept on the index, the live rule and
// its versions where a freeze stands.
public sealed record DipIndexOutcome(string Index, int Members, int Passed, int Listed, int HeldByATrade, int UnderAnother, int TradesKept, int TradesEnded, string? Fault = null, int Rules = 1);

// What the dip's step did on the night, an index at a time.
public sealed record DipOutcome(DateOnly? Night, IReadOnlyList<DipIndexOutcome> Indices);

// The short-term dip's part of the night, a step of its own after the index families' and before the setup ledger's,
// over every member of the S&P 500, 400 and 600 holding a bar on the night: every member's answer stored with the
// values that decided it, the family's list drawn last among the swing families, its trades kept and each open one
// walked to its exit at the next open. It reads the members and their stored year with its own loader, none of the
// history pulled before the year, and every other family's list and open trades through their own readers, and
// writes only its own tables, so no other family's code, list or row moves. From 22.3 it keeps every rule of the
// family standing registered on an index when the night started, the live rule first: the live rule's answers, list,
// night and trades as the card draws them, and each version's trades under its own name beside them, each at its
// registration's setting with its market setting read at its level on the night, a rule whose evaluator moved named
// and not kept; the provisional rule only where none stands. Each index's part is one transaction, so a failure in it
// leaves none of its writes and is named on its row while the other indices go on.
// see: The short-term dip buys a stock in an uptrend after a sharp drop no report caused and sells the bounce within days
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
// see: The dip buys at the next session's open and sells at the next open after its exit's close or its stop's, and at its last session's close
// see: A dip rule is registered as the family on its index and kept by the dip's own step alone
public sealed class DipFamily : IComponent
{
    // The members and their stored year through its own loader, which series are marked suspect, the other families'
    // lists and open trades through their own readers, the company's value for a trade's cost, and its own four tables,
    // which it alone writes; and from 22.3 the register its rules stand in, the S&P 500's breadth the market reading
    // stored and the market series a market setting reads.
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.SeriesState, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FilterVersion, Touch.Read),
            new StoreTouch(Store.ListRule, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.IndexFamilyTrade, Touch.Read),
            new StoreTouch(Store.MemberReading, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.MarketReading, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.DipNight, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.DipResult, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.DipPick, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.DipTrade, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "dip-family";

    public const string Ok = "ok";

    public const string NotComputed = "not computed";

    // The rule a trade on the page's list is kept under before a freeze registers one.
    public const string ProvisionalRule = "provisional";

    // Why a registered rule was not kept on a night: its evaluator's code moved since it was registered.
    public const string Moved = "its evaluator's code moved since it was registered, so it is kept no further until it is registered again";

    // The indices the step reads, in the page's order of indices.
    public static IReadOnlyList<string> AllIndices { get; } = [IndexFamilies.LargeIndex, .. IndexFamilies.Indices];

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string ReadRegister = @"
        SELECT id, candidate, rule, test, evaluator, parameters, evaluator_version, event, retires, registered_at, evidence
        FROM candidate_register
        ORDER BY id;
    ";

    const string ClearTheNight = @"
        DELETE FROM dip_night WHERE index_code = $index AND session_date = $night;
        DELETE FROM dip_result WHERE index_code = $index AND session_date = $night;
        DELETE FROM dip_pick WHERE index_code = $index AND session_date = $night;
        DELETE FROM dip_trade WHERE rule IN (SELECT value FROM json_each($rules)) AND index_code = $index AND session_date = $night;
    ";

    const string InsertNight = @"
        INSERT INTO dip_night (index_code, session_date, members, market, market_open, settings, passed, listed, fault)
        VALUES ($index, $night, $members, $market, $open, $settings, $passed, $listed, NULL);
    ";

    const string InsertNotComputed = @"
        INSERT OR IGNORE INTO dip_night (index_code, session_date, members, market, market_open, settings, passed, listed, fault)
        VALUES ($index, $night, 0, $market, 0, $settings, 0, 0, $fault);
    ";

    const string InsertResult = @"
        INSERT INTO dip_result (index_code, session_date, ticker, passed, place, entry, stop, time_limit, rsi, reason, gates)
        VALUES ($index, $night, $ticker, $passed, $place, $entry, $stop, $time_limit, $rsi, $reason, $gates);
    ";

    const string InsertPick = @"
        INSERT INTO dip_pick (index_code, session_date, ticker, state, place, listed_under, held_index, held_family, held_night)
        VALUES ($index, $night, $ticker, $state, $place, $listed_under, $held_index, $held_family, $held_night);
    ";

    const string InsertTrade = @"
        INSERT INTO dip_trade (rule, index_code, ticker, session_date, place, entry, stop, time_limit, exit_average, risk_moves, end_reason)
        VALUES ($rule, $index, $ticker, $night, $place, $entry, $stop, $time_limit, $exit_average, $risk_moves, 'open');
    ";

    const string OpenTradesOf = @"
        SELECT rule, ticker, session_date, entry, stop, time_limit, exit_average FROM dip_trade
        WHERE index_code = $index AND end_reason = 'open' AND session_date < $night
        ORDER BY session_date, ticker;
    ";

    const string WalkTrade = @"
        UPDATE dip_trade SET bought_on = $bought_on, fill = $fill, past = $past, ended_on = $ended_on, sale = $sale,
            end_reason = $end, sessions = $sessions, result = $result, cost = $cost
        WHERE rule = $rule AND index_code = $index AND ticker = $ticker AND session_date = $session;
    ";

    // Every trade a set of the dip's rules keeps on any index that has not ended before the night, which a rule's own
    // list reads to hold a stock back, since a stock is free the night after its trade ends: the page's lists' on every
    // index for the rules the cards draw, and a version's own for a version.
    const string DipTradesHeld = @"
        SELECT index_code, ticker, session_date, ended_on FROM dip_trade
        WHERE rule IN (SELECT value FROM json_each($rules)) AND session_date < $night AND (ended_on IS NULL OR ended_on >= $night)
        ORDER BY session_date DESC;
    ";

    // The stocks another family listed on the night on the index: on the S&P 500 its own families' list and the
    // fundamentals-first family's S&P 500 list, and on the S&P 400 and 600 the index's own families' lists.
    const string LargeListedTonight = @"
        SELECT ticker, family FROM family_pick WHERE session_date = $night AND state = 'listed'
        UNION ALL
        SELECT ticker, family FROM index_family_pick WHERE index_code = $index AND session_date = $night AND state = 'listed';
    ";

    const string IndexListedTonight = "SELECT ticker, family FROM index_family_pick WHERE index_code = $index AND session_date = $night AND state = 'listed';";

    const string BarsOf = @"
        SELECT session_date, open, close, raw_close FROM bar
        WHERE ticker = $ticker AND session_date <= $night ORDER BY session_date;
    ";

    // The members of the index on the night, each one's spans in it, its stored year to the night and its report dates,
    // as the index families' loader reads them and none of the history pulled before the year.
    const string MembersOn = @"
        SELECT DISTINCT ticker FROM membership
        WHERE index_code = $index AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string SpansOf = @"SELECT joined, ""left"" FROM membership WHERE index_code = $index AND ticker = $ticker;";

    const string YearOf = @"
        SELECT session_date, high, low, close, volume, open, raw_close FROM bar
        WHERE ticker = $ticker AND session_date <= $night ORDER BY session_date;
    ";

    const string ReportsOn = "SELECT event_date FROM calendar WHERE ticker = $ticker AND kind = 'earnings' ORDER BY event_date;";

    // The series a corporate action's refetch has not repaired, which the swing filter refuses too.
    const string Suspects = "SELECT ticker FROM series_state WHERE state = 'suspect';";

    const string ValueOn = "SELECT company_value FROM member_reading WHERE index_code = $index AND session_date = $session AND ticker = $ticker;";

    // From 22.3 what a market setting reads on the night: the S&P 500's breadth its market reading stored, and a market
    // series' closes to the night, the index's own giving the exchange's sessions a fund's and the VIX's are read on.
    const string LargeBreadthOn = "SELECT breadth FROM market_reading WHERE session_date = $night;";

    const string SeriesCloses = "SELECT session_date, close FROM market_bar WHERE series = $series AND session_date <= $night ORDER BY session_date;";

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

    public DipFamily(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // The rules the step keeps on an index on the night: where a rule of the family stands registered on it, every rule
    // of the family standing at the instant the night reads the register at, the live rule first and the versions in the
    // order they were registered, each at its registration's setting and one whose evaluator moved since named as not
    // kept; and otherwise the provisional rule at the provisional setting.
    // see: A dip rule is registered as the family on its index and kept by the dip's own step alone
    public static IReadOnlyList<(string Rule, DipSetting Setting, string? Fault)> RulesOn(string index, IReadOnlyList<RegisterRow> register, DateTimeOffset standingAt)
    {
        var family = ShadowColumn.StandingAt(register, standingAt)
            .Where(row => CandidateEvaluators.Find(row.Evaluator) is DipCandidate rule && rule.Index == index)
            .OrderBy(row => FamilyRecords.IsLive(row.Candidate) ? 0 : 1)
            .ThenBy(row => row.Id)
            .ToArray();

        return family.Length == 0
            ? [(ProvisionalRule, DipRule.Provisional, null)]
            : [.. family.Select(row => CandidateEvaluators.Find(row.Evaluator) is DipCandidate rule
                ? (row.Candidate, rule.SettingOf(CandidateEvaluator.Read(row.Parameters)), rule.Version == row.EvaluatorVersion ? (string?)null : Moved)
                : (row.Candidate, DipRule.Provisional, Moved))];
    }

    // The step over the three indices, each index's failure named on its own row while the others go on, and a failure
    // outside every index's part named on the step's row: the dip never stops the night. The register is read at the
    // instant given, the night's start or the instant a night run again names, and as it stands where none is given.
    public async Task<DipOutcome> RunAsync(string runId, CancellationToken cancellation = default, DateTimeOffset? registerAt = null)
    {
        var startedAt = clock.UtcNow;
        var outcomes = new List<DipIndexOutcome>();
        DateOnly? read = null;
        var rows = 0;

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            if (await ScalarAsync(connection, null, NewestSession, [], cancellation) is not string newest)
            {
                await AppendAsync(connection, runId, startedAt, 0, Ok, "no session: the store holds no bar", cancellation);

                return new DipOutcome(null, []);
            }

            var night = Date(newest);
            var register = await RegisterAsync(connection, cancellation);
            var rulesOf = AllIndices.ToDictionary(index => index, index => RulesOn(index, register, registerAt ?? startedAt), StringComparer.Ordinal);

            // The rules the cards draw on every index, whose trades on any index hold a stock back from the page's lists.
            var cardRules = AllIndices.Select(index => rulesOf[index][0].Rule).Distinct(StringComparer.Ordinal).ToArray();

            read = night;

            foreach (var index in AllIndices)
            {
                try
                {
                    var (outcome, written) = await IndexNightAsync(connection, index, night, rulesOf[index], cardRules, cancellation);

                    outcomes.Add(outcome);
                    rows += written;
                }
                catch (Exception failure) when (!cancellation.IsCancellationRequested)
                {
                    var fault = IndexFamilies.Cause(failure);
                    var (card, setting, _) = rulesOf[index][0];

                    rows += await NotComputedAsync(connection, index, night, fault, card, setting, cancellation);
                    outcomes.Add(new DipIndexOutcome(index, 0, 0, 0, 0, 0, 0, 0, fault, rulesOf[index].Count));
                }
            }

            await AppendAsync(connection, runId, startedAt, rows, outcomes.Any(outcome => outcome.Fault is not null) ? NotComputed : Ok, Detail(outcomes), cancellation);

            return new DipOutcome(night, outcomes);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            var fault = IndexFamilies.Cause(failure);
            DipIndexOutcome[] all =
            [
                .. outcomes,
                .. AllIndices.Where(index => outcomes.All(outcome => outcome.Index != index)).Select(index => new DipIndexOutcome(index, 0, 0, 0, 0, 0, 0, 0, fault)),
            ];

            await AppendAfterAFailureAsync(runId, startedAt, rows, Detail(all) + "; the step stopped on " + fault, cancellation);

            return new DipOutcome(read, all);
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

    // What the step's row says for each index.
    public static string Detail(IReadOnlyList<DipIndexOutcome> outcomes) =>
        string.Join("; ", outcomes.Select(outcome => outcome.Fault is { } fault
            ? $"{outcome.Index}: not computed tonight, {fault}"
            : FormattableString.Invariant(
                $"{outcome.Index}: {outcome.Members} member(s) read, {outcome.Passed} passed, {outcome.Listed} listed, {outcome.HeldByATrade} held back by a trade still open, {outcome.UnderAnother} listed under another family, {outcome.TradesKept} trade(s) kept, {outcome.TradesEnded} ended{(outcome.Rules > 1 ? FormattableString.Invariant($", {outcome.Rules} rules kept") : string.Empty)}")));

    // The settings the night read the dip by on an index, the rule its card draws and the words its card states the rule
    // in, its market setting in the index's words.
    public static string Settings(DipSetting setting, string rule = ProvisionalRule, string? index = null) => JsonSerializer.Serialize(new
    {
        rule,
        setting = setting.Short,
        words = setting.WordsOn(index),
        floors = "the price and dollar volume floors the index families read",
    });

    // What a market setting reads on an index on the night, read once for every rule: the S&P 500's breadth, and the
    // exchange's sessions to the night with the index's own fund's and the VIX's closes on them.
    sealed record MarketInputs(double? LargeBreadth, IReadOnlyList<DateOnly> Calendar, IReadOnlyList<(DateOnly Session, double Close)> Fund, IReadOnlyList<(DateOnly Session, double Close)> Vix);

    // One index's part of the night: what every rule reads read once, then every rule's answers, its list held back by
    // every other family's list and open trade and by its own, the card's rule's answers, list and night stored and every
    // rule's trades kept, in one transaction. A rule whose evaluator moved is not kept, and where the card's rule's moved
    // its night names why. Its outcome is the card's rule's, with the rules kept beside it counted.
    async Task<(DipIndexOutcome Outcome, int Rows)> IndexNightAsync(
        SqliteConnection connection,
        string index,
        DateOnly night,
        IReadOnlyList<(string Rule, DipSetting Setting, string? Fault)> rules,
        IReadOnlyList<string> cardRules,
        CancellationToken cancellation)
    {
        var names = await NamesAsync(connection, index, night, cancellation);
        var inputs = IndexNightRead.Prepare(index, night, names, new Dictionary<string, IReadOnlyList<FiledIncome>>(StringComparer.Ordinal));
        var suspects = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, Suspects, [], cancellation))
        {
            suspects.Add(row.GetString(0));
        }

        var market = rules.Any(rule => rule.Setting.Market != DipRule.MarketOff) ? await MarketAsync(connection, index, night, cancellation) : null;
        var read = rules
            .Select(rule =>
            {
                var gate = MarketGateOn(index, rule.Setting.Market, inputs, market, night);
                var answers = inputs is null || rule.Fault is not null ? [] : Answers(inputs, rule.Setting, suspects, gate);

                return (rule.Rule, rule.Setting, rule.Fault, Gate: gate, Answers: answers, Ranked: DipRule.Ranked(answers));
            })
            .ToArray();
        var tickers = read.SelectMany(rule => rule.Ranked).Select(answer => answer.Ticker).Distinct(StringComparer.Ordinal).ToArray();

        // Every trade a stock still holds on any card of any index: the S&P 500's own lists', the index families' lists',
        // and the dip's own on every index, read after tonight's walk so a trade it ends before the night frees its stock.
        var (indexTrades, heldIn) = await IndexFamilies.OpenIndexTradesAsync(connection, night, tickers, cancellation);
        var others = new Dictionary<string, (HeldTrade Trade, string Index)>(StringComparer.Ordinal);

        foreach (var (ticker, held) in await FamilyLister.OpenTradesAsync(connection, night, tickers, cancellation))
        {
            others.TryAdd(ticker, (held, IndexFamilies.LargeIndex));
        }

        foreach (var (ticker, held) in indexTrades)
        {
            others.TryAdd(ticker, (held, heldIn[ticker]));
        }

        var listedTonight = new Dictionary<string, string>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, null, index == IndexFamilies.LargeIndex ? LargeListedTonight : IndexListedTonight, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            listedTonight.TryAdd(row.GetString(0), row.GetString(1));
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheNight, [("$index", index), ("$night", Stamp(night)), ("$rules", JsonSerializer.Serialize(rules.Select(rule => rule.Rule)))], cancellation);

        var ended = await WalkAsync(connection, transaction, index, night, inputs?.Calendar ?? [], cancellation);
        DipIndexOutcome? card = null;
        var written = ended;

        foreach (var (rule, setting, fault, gate, answers, ranked) in read)
        {
            var first = card is null;

            if (fault is not null)
            {
                if (first)
                {
                    await ExecuteAsync(connection, transaction, InsertNotComputed,
                    [
                        ("$index", index), ("$night", Stamp(night)), ("$market", setting.Market), ("$settings", Settings(setting, rule, index)), ("$fault", fault),
                    ], cancellation);
                    card = new DipIndexOutcome(index, 0, 0, 0, 0, 0, 0, ended, fault, rules.Count);
                    written++;
                }

                continue;
            }

            // The card's rule gives way to the page's lists' trades on every index, and a version to its own trades alone,
            // both to every other family's list and open trade.
            var open = new Dictionary<string, (HeldTrade Trade, string Index)>(others, StringComparer.Ordinal);

            await foreach (var row in RowsAsync(connection, transaction, DipTradesHeld, [("$rules", JsonSerializer.Serialize(first ? cardRules : [rule])), ("$night", Stamp(night))], cancellation))
            {
                open.TryAdd(row.GetString(1), (new HeldTrade(DipRule.Name, Date(row.GetString(2))), row.GetString(0)));
            }

            var picks = Draw(ranked, open, listedTonight);
            var byTicker = ranked.ToDictionary(answer => answer.Ticker, StringComparer.Ordinal);
            var kept = 0;

            if (first)
            {
                var places = ranked.Select((answer, place) => (answer.Ticker, Place: place + 1)).ToDictionary(pair => pair.Ticker, pair => pair.Place, StringComparer.Ordinal);

                await ExecuteAsync(connection, transaction, InsertNight,
                [
                    ("$index", index), ("$night", Stamp(night)), ("$members", answers.Count), ("$market", setting.Market), ("$open", gate.Passed ? 1 : 0),
                    ("$settings", Settings(setting, rule, index)), ("$passed", ranked.Count), ("$listed", picks.Count(pick => pick.State == FamilyList.Listed)),
                ], cancellation);

                foreach (var answer in answers)
                {
                    await ExecuteAsync(connection, transaction, InsertResult,
                    [
                        ("$index", index), ("$night", Stamp(night)), ("$ticker", answer.Ticker), ("$passed", answer.Passed ? 1 : 0),
                        ("$place", places.TryGetValue(answer.Ticker, out var place) ? place : DBNull.Value),
                        ("$entry", Stored(answer.Entry)), ("$stop", Stored(answer.Stop)), ("$time_limit", (object?)answer.TimeLimit ?? DBNull.Value),
                        ("$rsi", (object?)answer.Rsi ?? DBNull.Value), ("$reason", (object?)answer.Reason ?? DBNull.Value), ("$gates", FamilyRule.GatesJson(answer.Gates)),
                    ], cancellation);
                }

                foreach (var pick in picks)
                {
                    await ExecuteAsync(connection, transaction, InsertPick,
                    [
                        ("$index", index), ("$night", Stamp(night)), ("$ticker", pick.Ticker), ("$state", pick.State),
                        ("$place", (object?)pick.Place ?? DBNull.Value), ("$listed_under", (object?)pick.ListedUnder ?? DBNull.Value),
                        ("$held_index", (object?)pick.HeldIndex ?? DBNull.Value), ("$held_family", (object?)pick.HeldBy?.Family ?? DBNull.Value),
                        ("$held_night", pick.HeldBy is { } held ? Stamp(held.Listed) : DBNull.Value),
                    ], cancellation);
                }

                written += 1 + answers.Count + picks.Count;
            }

            foreach (var pick in picks.Where(pick => pick.State == FamilyList.Listed))
            {
                if (byTicker[pick.Ticker] is { Entry: { } entry, Stop: { } stop, TimeLimit: { } limit })
                {
                    kept++;
                    await ExecuteAsync(connection, transaction, InsertTrade,
                    [
                        ("$rule", rule), ("$index", index), ("$ticker", pick.Ticker), ("$night", Stamp(night)), ("$place", pick.Place!.Value),
                        ("$entry", Money.ToStorage(entry)), ("$stop", Money.ToStorage(stop)), ("$time_limit", limit), ("$exit_average", setting.ExitLevel),
                        ("$risk_moves", setting.StopMoves),
                    ], cancellation);
                }
            }

            written += kept;

            card ??= new DipIndexOutcome(
                index,
                answers.Count,
                ranked.Count,
                picks.Count(pick => pick.State == FamilyList.Listed),
                picks.Count(pick => pick.State == FamilyList.OpenTrade),
                picks.Count(pick => pick.State == FamilyList.UnderAnother),
                kept,
                ended,
                null,
                rules.Count);
        }

        await transaction.CommitAsync(cancellation);

        return (card!, written);
    }

    // What a market setting reads on the night, read once an index: the S&P 500's breadth its market reading stored on
    // the S&P 400 and 600, and the S&P 500 index's sessions to the night with the index's own fund's closes and the VIX's.
    static async Task<MarketInputs> MarketAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        double? large = index == IndexFamilies.LargeIndex
            ? null
            : await ScalarAsync(connection, null, LargeBreadthOn, [("$night", Stamp(night))], cancellation) is double breadth ? breadth : null;

        async Task<IReadOnlyList<(DateOnly Session, double Close)>> ClosesAsync(string series)
        {
            var closes = new List<(DateOnly, double)>();

            await foreach (var row in RowsAsync(connection, null, SeriesCloses, [("$series", series), ("$night", Stamp(night))], cancellation))
            {
                closes.Add((Date(row.GetString(0)), Statistic.FromPrice(Money.FromStorage(row.GetString(1)))));
            }

            return closes;
        }

        return new MarketInputs(
            large,
            [.. (await ClosesAsync(MarketCloses.Index)).Select(one => one.Session)],
            await ClosesAsync(DipRule.FundOf(index)),
            await ClosesAsync(MarketCloses.Vix));
    }

    // A rule's market gate on the night, every member reading the same: none where the setting reads none; the index's
    // own breadth or the S&P 500's at the level's floor; or the index's own fund above its average, or the VIX above its
    // median, on the night's session as the exchange's sessions to it read them; closed where the setting names a level
    // the index does not hold or the night reads nothing for it.
    // see: Each index's dip is frozen at the setting its sweep would run with the neighbours meeting the floors and the market variant as versions
    static Gate MarketGateOn(string index, string key, IndexNightInputs? inputs, MarketInputs? market, DateOnly night)
    {
        if (key == DipRule.MarketOff)
        {
            return DipRule.NoMarket;
        }

        if (DipRule.MarketLevels(index).FirstOrDefault(level => level.Key == key) is not { } level || market is null)
        {
            return new Gate(DipRule.MarketGate, false, $"its market setting {key} is no level the index reads", FamilyRule.Values(("setting", key)));
        }

        var at = market.Calendar.ToList().IndexOf(night);

        return level.Reads switch
        {
            DipRule.ReadsOwnBreadth => DipRule.MarketOn(level, inputs?.Breadth >= level.Floor, "the index's own breadth at " + FamilyRule.Figure(inputs?.Breadth)),
            DipRule.ReadsLargeBreadth => DipRule.MarketOn(level, market.LargeBreadth >= level.Floor, "the S&P 500's breadth at " + FamilyRule.Figure(market.LargeBreadth)),
            DipRule.ReadsFund => at >= 0 && DipRule.FundAboveItsAverage(market.Calendar, market.Fund)[at] is var above
                ? DipRule.MarketOn(level, above, $"{DipRule.FundOf(index)} {(above ? "above" : "not above")} its average on the night")
                : DipRule.MarketOn(level, false, "no session of the night in the market series"),
            _ => at >= 0 && DipRule.VixAboveItsMedian(market.Calendar, market.Vix)[at] is var over
                ? DipRule.MarketOn(level, over, $"the VIX {(over ? "above" : "not above")} its median on the night")
                : DipRule.MarketOn(level, false, "no session of the night in the market series"),
        };
    }

    // The register as it stands, every row in the order it was written.
    static async Task<IReadOnlyList<RegisterRow>> RegisterAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var rows = new List<RegisterRow>();

        await foreach (var row in RowsAsync(connection, null, ReadRegister, [], cancellation))
        {
            rows.Add(new RegisterRow(
                row.GetInt64(0),
                row.GetString(1),
                row.GetString(2),
                row.GetString(3),
                row.GetString(4),
                row.GetString(5),
                row.GetString(6),
                row.GetString(7),
                row.IsDBNull(8) ? null : row.GetString(8),
                DateTimeOffset.ParseExact(row.GetString(9), "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal),
                row.IsDBNull(10) ? null : row.GetString(10)));
        }

        return rows;
    }

    // The index's members on the night, each with its spans in the index, its stored year to the night and its report
    // dates, read as the index families' loader reads them and with none of the history pulled before the year.
    static async Task<IReadOnlyList<SweepName>> NamesAsync(SqliteConnection connection, string index, DateOnly night, CancellationToken cancellation)
    {
        var tickers = new List<string>();

        await foreach (var row in RowsAsync(connection, null, MembersOn, [("$index", index), ("$session", Stamp(night))], cancellation))
        {
            tickers.Add(row.GetString(0));
        }

        var names = new List<SweepName>();

        foreach (var ticker in tickers)
        {
            var spans = new List<(DateOnly?, DateOnly?)>();
            var bars = new List<SweepBar>();
            var prints = new List<DateOnly>();

            await foreach (var row in RowsAsync(connection, null, SpansOf, [("$index", index), ("$ticker", ticker)], cancellation))
            {
                spans.Add((row.IsDBNull(0) ? null : Date(row.GetString(0)), row.IsDBNull(1) ? null : Date(row.GetString(1))));
            }

            await foreach (var row in RowsAsync(connection, null, YearOf, [("$ticker", ticker), ("$night", Stamp(night))], cancellation))
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

            await foreach (var row in RowsAsync(connection, null, ReportsOn, [("$ticker", ticker)], cancellation))
            {
                prints.Add(Date(row.GetString(0)));
            }

            names.Add(new SweepName(ticker, [.. bars], spans, [.. prints]));
        }

        return names;
    }

    // Each member holding a bar on the night read under the dip at a setting: its stored closes to the night and none
    // before the store's year, and from 22.3 its volumes beside them, its typical move on the night, the floors the index
    // families read on it, each report on file by the sessions it falls from the night, the night's own sessions counted
    // back and the exchange's counted ahead, the first session of the year behind the night its stored series misses,
    // whether its series is marked suspect, and the market gate the setting's level read on the night.
    // see: Bars are never interpolated
    public static IReadOnlyList<DipAnswer> Answers(IndexNightInputs inputs, DipSetting setting, IReadOnlySet<string> suspects, Gate? market = null)
    {
        var answers = new List<DipAnswer>();

        foreach (var name in inputs.Held)
        {
            var series = inputs.Series[name];
            var bar = IndexNightRead.BarOf(series, inputs.At);
            var closes = series.Bars[..(bar + 1)].Select(day => Statistic.FromPrice(day.Close)).ToArray();
            var move = series.Atr[bar];
            var floors = IndexNightRead.PartsOn(inputs, name, 1m, MemberReadings.LowestPrice, IndexQuality.Off)[0];
            var (reports, next) = ReportsOf(series.Name.Prints, inputs.Calendar, inputs.At, inputs.Night);

            answers.Add(DipRule.Read(
                new DipMember(
                    series.Name.Ticker, closes, double.IsNaN(move) ? null : move, floors.Passed, floors.Words ?? floors.Reason, reports, next, market ?? DipRule.NoMarket,
                    series.Gap[bar] ? Missing(series, bar, inputs.Calendar) : null,
                    suspects.Contains(series.Name.Ticker),
                    setting.VolumeUnder is null ? null : [.. series.Bars[..(bar + 1)].Select(day => day.Volume)]),
                setting));
        }

        return answers;
    }

    // The first session of the year behind a bar, from the series' own first session on, that the series does not
    // hold, as the sweep's columns mark a gap; the year's first session where the mark stands and none is found.
    public static DateOnly Missing(SweepSeries series, int bar, IReadOnlyList<DateOnly> calendar)
    {
        var start = Math.Max(series.SessionAt[0], series.SessionAt[bar] - SweepColumns.GapSessions + 1);
        var expected = start;

        for (var at = 0; at <= bar; at++)
        {
            if (series.SessionAt[at] < start)
            {
                continue;
            }

            if (series.SessionAt[at] > expected)
            {
                return calendar[expected];
            }

            expected = series.SessionAt[at] + 1;
        }

        return calendar[start];
    }

    // Each report date on file by the sessions it falls from the night: one on or before it at the first session of the
    // calendar on or after it, counted back from the night, and one after it at the first session of the exchange's own
    // calendar on or after it, counted ahead, so a report dated on a day the exchange is closed falls on the session
    // after it on either side of the night, none where that calendar does not reach it; and the next report ahead, none
    // where none is on file.
    public static (IReadOnlyList<int> Reports, DateOnly? Next) ReportsOf(IReadOnlyList<DateOnly> prints, IReadOnlyList<DateOnly> calendar, int at, DateOnly night)
    {
        var reports = new List<int>();
        DateOnly? next = null;

        foreach (var print in prints)
        {
            if (print <= night)
            {
                var session = calendar.Count == 0 ? -1 : FirstOnOrAfter(calendar, print);

                if (session >= 0 && session <= at)
                {
                    reports.Add(session - at);
                }

                continue;
            }

            if (next is null || print < next)
            {
                next = print;
            }

            if (ExchangeClosures.SessionsUntil(night, print) is { } ahead)
            {
                reports.Add(ExchangeClosures.IsSession(print) ? ahead : ahead + 1);
            }
        }

        return (reports, next);
    }

    static int FirstOnOrAfter(IReadOnlyList<DateOnly> calendar, DateOnly day)
    {
        var low = 0;
        var high = calendar.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (calendar[middle] < day)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < calendar.Count ? low : -1;
    }

    // A stock on the dip's list on the night: listed with its place, or held back for a trade still open on any card of
    // any index, for a stock another family listed on the index that night, or past the family's five.
    public sealed record DipPick(string Ticker, string State, int? Place, string? ListedUnder, HeldTrade? HeldBy, string? HeldIndex);

    // The dip's list drawn last among the swing families: each member passing in the family's order, a stock with an
    // open trade held back, one another family listed that night held back naming that family, and the first five of the
    // rest listed.
    public static IReadOnlyList<DipPick> Draw(IReadOnlyList<DipAnswer> ranked, IReadOnlyDictionary<string, (HeldTrade Trade, string Index)> open, IReadOnlyDictionary<string, string> listedTonight)
    {
        var picks = new List<DipPick>();
        var place = 0;

        foreach (var answer in ranked)
        {
            if (open.TryGetValue(answer.Ticker, out var held))
            {
                picks.Add(new DipPick(answer.Ticker, FamilyList.OpenTrade, null, null, held.Trade, held.Index));
            }
            else if (listedTonight.TryGetValue(answer.Ticker, out var family))
            {
                picks.Add(new DipPick(answer.Ticker, FamilyList.UnderAnother, null, family, null, null));
            }
            else if (place >= SetupFamilies.ListedANight)
            {
                picks.Add(new DipPick(answer.Ticker, FamilyList.PastFive, null, null, null, null));
            }
            else
            {
                picks.Add(new DipPick(answer.Ticker, FamilyList.Listed, ++place, null, null, null));
            }
        }

        return picks;
    }

    // Each of the index's dip trades not yet ended, every rule's, walked over its stock's bars since its listing at the
    // scale the series has now: bought at the next session's open through the core's one pricing function, and sold at
    // the next open after its exit's close or its stop's, its exit read by the level its rule stored, or at its last
    // session's close; a buy or a sale waiting where the stock's bars skip a session of the index's calendar or the store
    // holds none yet; the count ended.
    async Task<int> WalkAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, IReadOnlyList<DateOnly> calendar, CancellationToken cancellation)
    {
        var trades = new List<(string Rule, string Ticker, DateOnly Session, decimal Entry, decimal Stop, int Limit, int Exit)>();

        await foreach (var row in RowsAsync(connection, transaction, OpenTradesOf, [("$index", index), ("$night", Stamp(night))], cancellation))
        {
            trades.Add((row.GetString(0), row.GetString(1), Date(row.GetString(2)), Money.FromStorage(row.GetString(3)), Money.FromStorage(row.GetString(4)), row.GetInt32(5), row.GetInt32(6)));
        }

        var ended = 0;

        foreach (var trade in trades)
        {
            var bars = new List<(DateOnly Session, double Open, double Close, decimal Raw)>();

            await foreach (var row in RowsAsync(connection, transaction, BarsOf, [("$ticker", trade.Ticker), ("$night", Stamp(night))], cancellation))
            {
                var close = Money.FromStorage(row.GetString(2));

                bars.Add((Date(row.GetString(0)), Statistic.FromPrice(Money.FromStorage(row.GetString(1))), Statistic.FromPrice(close), row.IsDBNull(3) ? close : Money.FromStorage(row.GetString(3))));
            }

            var listing = bars.FindIndex(bar => bar.Session == trade.Session);

            if (listing < 0 || !(bars[listing].Close > 0))
            {
                continue;
            }

            // The series' scale now over the scale its night stored the trade at.
            var scale = bars[listing].Close / Statistic.FromPrice(trade.Entry);
            var close0 = bars[listing].Close;
            var stop = Statistic.FromPrice(trade.Stop) * scale;
            var path = DipRule.Walk(close0, stop, trade.Limit, DipRule.Sessions([.. bars.Select(bar => (bar.Session, bar.Open, bar.Close))], listing, trade.Exit, calendar));

            if (path.BoughtOn is null)
            {
                continue;
            }

            var risk = close0 - stop;
            var fill = path.Open!.Value;
            var past = TradeFills.PastOf(fill, stop, null);
            double? result = path.ResultAtClose is { } atClose ? TradeFills.AtOpen(atClose, close0, risk, fill) : null;
            double? cost = null;

            if (path.End != DipEnd.Open && path.Sale is { } sale)
            {
                decimal? value = await ScalarAsync(connection, transaction, ValueOn, [("$index", index), ("$session", Stamp(trade.Session)), ("$ticker", trade.Ticker)], cancellation) is string stored ? Money.FromStorage(stored) : null;
                var trip = TradeCost.RoundTrip(value, Statistic.ToPrice(fill / scale), Statistic.ToPrice(sale / scale));

                cost = trade.Entry > trade.Stop ? Statistic.FromRatio(trip / (trade.Entry - trade.Stop)) : null;
                ended++;
            }

            await ExecuteAsync(connection, transaction, WalkTrade,
            [
                ("$rule", trade.Rule), ("$index", index), ("$ticker", trade.Ticker), ("$session", Stamp(trade.Session)),
                ("$bought_on", Stamp(path.BoughtOn.Value)), ("$fill", Money.ToStorage(Statistic.ToPrice(fill / scale))), ("$past", (object?)past ?? DBNull.Value),
                ("$ended_on", path.EndedOn is { } on ? Stamp(on) : DBNull.Value), ("$sale", path.Sale is { } sold ? Money.ToStorage(Statistic.ToPrice(sold / scale)) : DBNull.Value),
                ("$end", path.End), ("$sessions", path.Sessions), ("$result", path.End == DipEnd.Open ? DBNull.Value : (object?)result ?? DBNull.Value),
                ("$cost", (object?)cost ?? DBNull.Value),
            ], cancellation);
        }

        return ended;
    }

    // An index whose part of the night failed: a night row naming the failure under the rule its card draws where no
    // earlier try of the night wrote one, and none where the store will not take it, since recording the failure is never
    // what stops the night.
    async Task<int> NotComputedAsync(SqliteConnection connection, string index, DateOnly night, string fault, string rule, DipSetting setting, CancellationToken cancellation)
    {
        try
        {
            await using var command = connection.CreateCommand();

            command.CommandText = InsertNotComputed;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$night", Stamp(night));
            command.Parameters.AddWithValue("$market", setting.Market);
            command.Parameters.AddWithValue("$settings", Settings(setting, rule, index));
            command.Parameters.AddWithValue("$fault", fault);

            return await command.ExecuteNonQueryAsync(cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
            return 0;
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
