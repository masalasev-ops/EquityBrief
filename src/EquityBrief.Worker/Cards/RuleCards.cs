using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Cards;

// One index's rule rows on a night: the standing rules written, the picks a rule's own table kept and the members
// forming a breakout, or the failure that left the index unwritten.
public sealed record RuleCardsIndex(string Index, int Rules, int Picks, int Forming, string? Fault = null);

public sealed record RuleCardsOutcome(DateOnly? Session, IReadOnlyList<RuleCardsIndex> Indices)
{
    public int Rules => Indices.Sum(index => index.Rules);

    public int Picks => Indices.Sum(index => index.Picks);

    public int Forming => Indices.Sum(index => index.Forming);
}

// One standing rule's night, as its row stores it.
public sealed record RuleNightRow(string Index, string Family, string Rule, bool Evaluated, int Listed, IReadOnlyList<(string Gate, int Passed)>? Gates, StretchReading? Stretch);

// The cards' rule rows. After the families have drawn each index's list, and for every rule standing on each index, live
// or variant, the stage writes the rule's night: whether the night evaluated it, how many it listed with zeros, how many
// members passed each of its gates and every gate before, and its empty stretch against the mark counted over its own
// past empty nights, replayed from the pulled history by the record command and carried on by the night. For a rule whose
// list no other table keeps, the swing filter's variants, it keeps the rule's own picks, five a night in the list's own
// order with one open trade a stock, each walked on the closes since as the family's walk ends a trade. For each
// breakout rule it keeps the members forming a breakout, at most the stated rows with the whole count on each, the
// nearest misses first. A failure in one index's part leaves that index's rows of the night unwritten and named on the
// stage's row, and the step goes on; the stage makes no request and calls no model. A night run again replaces its own
// rows.
// see: A variant's picks are shown on its card when chosen and its results only under its tests
// see: The forming list advises and never lists a stock
// see: A card's stretch line counts its mark over past empty nights and draws none under 30 completed stretches
// see: The nightly run is arithmetic only
public sealed class RuleCards : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.HeavyweightRuleHolding, Touch.Read),
            new StoreTouch(Store.IndexFamilyNight, Touch.Read),
            new StoreTouch(Store.IndexFamilyResult, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.IndexRuleTrade, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightRuleHolding, Touch.Read),
            new StoreTouch(Store.RuleNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RulePick, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.FormingRow, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "rule-cards";

    public const string ProvisionalRule = RuleRows.ProvisionalRule;

    public const string FromTheNight = RuleRows.FromTheNight;

    public const string FromTheHistory = RuleRows.FromTheHistory;

    public const string Ok = DecisionCards.Ok;

    public const string NotComputed = DecisionCards.NotComputed;

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string ClearTheNight = @"
        DELETE FROM rule_night WHERE index_code = $index AND session_date = $night AND source = 'night';
        DELETE FROM rule_pick WHERE index_code = $index AND session_date = $night;
        DELETE FROM forming_row WHERE index_code = $index AND session_date = $night;
    ";

    const string ClearTheHistory = "DELETE FROM rule_night WHERE index_code = $index AND rule = $rule AND source = 'history';";

    // A rule's listed counts on the nights before, oldest first, which its stretch is read over.
    const string ListedBefore = @"
        SELECT listed FROM rule_night
        WHERE index_code = $index AND rule = $rule AND evaluated = 1 AND session_date < $night
        ORDER BY session_date;
    ";

    const string InsertNight = @"
        INSERT INTO rule_night (index_code, session_date, family, rule, evaluated, listed, gates, stretch, mark, flagged, completed, sessions, source)
        VALUES ($index, $night, $family, $rule, $evaluated, $listed, $gates, $stretch, $mark, $flagged, $completed, $sessions, $source);
    ";

    const string InsertPick = @"
        INSERT INTO rule_pick (index_code, rule, ticker, session_date, family, place, entry, stop, target, reward_to_risk, cap, why)
        VALUES ($index, $rule, $ticker, $night, $family, $place, $entry, $stop, $target, $reward_to_risk, $cap, $why);
    ";

    const string EndPick = "UPDATE rule_pick SET ended_on = $ended_on, result = $result WHERE index_code = $index AND rule = $rule AND ticker = $ticker AND session_date = $session;";

    // A rule's picks still open before the night, which the night walks and which hold their stock off its list.
    const string OpenPicks = @"
        SELECT rule, ticker, session_date, entry, stop, target, cap
        FROM rule_pick
        WHERE index_code = $index AND ended_on IS NULL AND session_date < $night;
    ";

    const string InsertForming = @"
        INSERT INTO forming_row (index_code, session_date, rule, place, ticker, close, high, moves_under, volume_needed, volume, range_ratio, missing, next_earnings, forming)
        VALUES ($index, $night, $rule, $place, $ticker, $close, $high, $moves_under, $volume_needed, $volume, $range_ratio, $missing, $next_earnings, $forming);
    ";

    // The S&P 500's rows on the night: the page's list a family, each registered rule's own list, each book's buys, and
    // the swing filter's rows with each variant's verdicts and the plans a pick is kept on.
    const string LargeListed = "SELECT family, COUNT(*) FROM family_pick WHERE session_date = $night AND state = 'listed' GROUP BY family;";

    const string LargeRuleListed = "SELECT candidate, COUNT(*) FROM family_trade WHERE session_date = $night GROUP BY candidate;";

    const string LargeBought = "SELECT candidate, COUNT(*) FROM heavyweight_rule_holding WHERE entered_on = $night GROUP BY candidate;";

    const string FilterRows = @"
        SELECT ticker, shadow, gates, strength, band_strength, swing_entry, swing_stop, swing_target, swing_reward_to_risk, clear_stop, clear_target, clear_reward_to_risk, exclusions
        FROM gate_result
        WHERE session_date = $night AND version <> '" + ReplayedResults.Version + @"'
        ORDER BY ticker;
    ";

    // The S&P 400's and 600's rows on the night: the index's night, every member's answer under each family and each
    // registered rule's own list and book.
    const string IndexNightRow = "SELECT fault, market_open FROM index_family_night WHERE index_code = $index AND session_date = $night;";

    const string IndexAnswers = "SELECT family, passed, reason FROM index_family_result WHERE index_code = $index AND session_date = $night;";

    const string IndexListed = "SELECT family, COUNT(*) FROM index_family_pick WHERE index_code = $index AND session_date = $night AND state = 'listed' GROUP BY family;";

    const string IndexRuleListed = "SELECT candidate, COUNT(*) FROM index_rule_trade WHERE index_code = $index AND session_date = $night GROUP BY candidate;";

    const string IndexBought = "SELECT candidate, COUNT(*) FROM index_heavyweight_rule_holding WHERE index_code = $index AND entered_on = $night GROUP BY candidate;";

    const string IndexOwnBought = "SELECT COUNT(*) FROM index_heavyweight_holding WHERE index_code = $index AND entered_on = $night;";

    // Each member's next earnings date on the calendar after the night.
    const string NextEarnings = @"
        SELECT ticker, MIN(event_date) FROM calendar
        WHERE kind = 'earnings' AND event_date > $night
        GROUP BY ticker;
    ";

    const string SessionsAfter = "SELECT DISTINCT session_date FROM bar WHERE session_date > $night ORDER BY session_date;";

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
    readonly FormingSettings forming;

    public RuleCards(IClock clock, string databaseFile, FormingSettings? forming = null)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
        this.forming = forming ?? FormingSettings.Defaults;
    }

    // The night's rows for every index, the register read as the night started.
    public async Task<RuleCardsOutcome> RunAsync(string runId, IReadOnlyList<RegisterRow> register, DateTimeOffset nightStartedAt, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var written = new List<RuleCardsIndex>();

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
            {
                await AppendAsync(connection, runId, startedAt, 0, Ok, "no session: the store holds no bar", cancellation);

                return new RuleCardsOutcome(null, []);
            }

            var night = Date(newest);
            var standing = ShadowColumn.StandingAt(register, nightStartedAt);

            foreach (var index in DecisionCards.Indices)
            {
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

                try
                {
                    await ExecuteAsync(connection, transaction, ClearTheNight, [("$index", index), ("$night", Stamp(night))], cancellation);

                    var one = index == IndexFamilies.LargeIndex
                        ? await LargeAsync(connection, transaction, night, standing, cancellation)
                        : await IndexAsync(connection, transaction, index, night, standing, cancellation);

                    await transaction.CommitAsync(cancellation);
                    written.Add(one);
                }
                catch (Exception failure) when (!cancellation.IsCancellationRequested)
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    written.Add(new RuleCardsIndex(index, 0, 0, 0, IndexFamilies.Cause(failure)));
                }
            }

            await AppendAsync(connection, runId, startedAt, written.Sum(index => index.Rules + index.Picks + index.Forming), written.Any(index => index.Fault is not null) ? NotComputed : Ok, Detail(written), cancellation);

            return new RuleCardsOutcome(night, written);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            RuleCardsIndex[] all =
            [
                .. written,
                .. DecisionCards.Indices.Where(index => written.All(one => one.Index != index)).Select(index => new RuleCardsIndex(index, 0, 0, 0, IndexFamilies.Cause(failure))),
            ];

            await AppendAfterAFailureAsync(runId, startedAt, all.Sum(index => index.Rules + index.Picks + index.Forming), Detail(all) + "; the step stopped on " + IndexFamilies.Cause(failure), cancellation);

            return new RuleCardsOutcome(null, all);
        }
    }

    static string Detail(IReadOnlyList<RuleCardsIndex> indices) =>
        string.Join("; ", indices.Select(index => index.Fault is null
            ? FormattableString.Invariant($"{index.Rules} rule(s), {index.Picks} pick(s) of their own and {index.Forming} forming on the {DecisionCards.NameOf(index.Index)}")
            : $"the {DecisionCards.NameOf(index.Index)}'s rule rows not computed tonight: {index.Fault}"));

    // A rule's history replayed by the record command: its listed count on each session the replay evaluated it on,
    // oldest first, written in place of what an earlier replay wrote, each night carrying the stretch and the mark read
    // to it. The night's own rows stand beside them and are read after them.
    public async Task<int> WriteHistoryAsync(string index, string family, string rule, IReadOnlyList<(DateOnly Session, int Listed)> nights, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheHistory, [("$index", index), ("$rule", rule)], cancellation);

        var ordered = nights.OrderBy(night => night.Session).ToArray();
        var readings = RuleStretch.ReadEach([.. ordered.Select(night => night.Listed)]);

        for (var at = 0; at < ordered.Length; at++)
        {
            await InsertNightAsync(connection, transaction, index, ordered[at].Session, new RuleNightRow(index, family, rule, true, ordered[at].Listed, null, readings[at]), FromTheHistory, cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return ordered.Length;
    }

    // The S&P 500's rules on the night.
    async Task<RuleCardsIndex> LargeAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, IReadOnlyList<RegisterRow> standing, CancellationToken cancellation)
    {
        var index = IndexFamilies.LargeIndex;
        var listed = await CountsAsync(connection, transaction, LargeListed, [("$night", Stamp(night))], cancellation);
        var ruleListed = await CountsAsync(connection, transaction, LargeRuleListed, [("$night", Stamp(night))], cancellation);
        var bought = await CountsAsync(connection, transaction, LargeBought, [("$night", Stamp(night))], cancellation);
        var filter = await FilterRowsAsync(connection, transaction, night, cancellation);
        var rules = new List<RuleNightRow>();
        var picks = 0;
        var formingRows = 0;

        // The swing filter's rules: the live filter's list is the page's, and each variant keeps its own here.
        var filterRules = standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is SwingFilterRule).ToArray();
        var open = await OpenPicksAsync(connection, transaction, index, night, cancellation);
        var walked = await WalkOpenPicksAsync(connection, transaction, index, night, open, cancellation);

        foreach (var row in filterRules)
        {
            var verdicts = filter.Select(member => (member, Verdict: member.Shadow.FirstOrDefault(outcome => outcome.Candidate == row.Candidate))).Where(pair => pair.Verdict is not null).ToArray();
            var evaluated = filter.Count > 0 && verdicts.Length > 0;
            var gates = evaluated ? Funnel(SwingGates.Order, verdicts.Select(pair => pair.Verdict!.Values).ToArray()) : null;
            int count;

            if (FamilyRecords.IsLive(row.Candidate))
            {
                count = listed.GetValueOrDefault(SetupFamilies.Pullback);
            }
            else
            {
                var held = walked.Where(pick => pick.Rule == row.Candidate).Select(pick => pick.Ticker).ToHashSet(StringComparer.Ordinal);
                var kept = KeepPullbackPicks(verdicts.Where(pair => pair.Verdict!.Fired).Select(pair => (pair.member, pair.Verdict!)), held);

                foreach (var pick in kept)
                {
                    await InsertPickAsync(connection, transaction, index, row.Candidate, night, pick, cancellation);
                }

                picks += kept.Count;
                count = kept.Count;
            }

            rules.Add(new RuleNightRow(index, SetupFamilies.Pullback, row.Candidate, evaluated, count, gates, null));
        }

        // The breakout's and the drift's rules, each evaluated over every member at its own settings for its funnel, and
        // each breakout rule's forming members.
        var familyRules = standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is FamilyRuleEvaluator and not IndexRuleCandidate).ToArray();

        if (familyRules.Length > 0)
        {
            var inputs = await FamilyEvaluator.InputsAsync(connection, night, FamilyRuleShadow.For(standing, DateTimeOffset.MaxValue).DriftWindowReach, cancellation);
            var earnings = await NextEarningsAsync(connection, transaction, night, cancellation);

            foreach (var row in familyRules)
            {
                var evaluator = (FamilyRuleEvaluator)CandidateEvaluators.Find(row.Evaluator)!;
                var evaluated = inputs is not null && evaluator.Version == row.EvaluatorVersion;
                var parameters = CandidateEvaluator.Read(row.Parameters);
                IReadOnlyList<(string Gate, int Passed)>? gates = null;

                if (evaluated)
                {
                    var results = inputs!.Members.Where(member => member.Withheld is null).Select(member => evaluator.EvaluateMember(member.Inputs, parameters)).ToArray();
                    var order = evaluator.Family == BreakoutRule.Name ? BreakoutRule.Order : DriftRule.Order;

                    gates = Funnel(order, results.Select(result => result.Gates.ToDictionary(gate => gate.Name, gate => gate.Passed ? Passed : Failed, StringComparer.Ordinal)).ToArray());

                    if (evaluator.Family == BreakoutRule.Name)
                    {
                        var settings = BreakoutCandidate.SettingsOf(parameters);
                        var members = FormingList.Order(inputs.Members.Where(member => member.Withheld is null).Select(member => FormingList.Read(member.Inputs.Breakout, settings, forming)).OfType<FormingMember>());

                        formingRows += await InsertFormingAsync(connection, transaction, index, night, row.Candidate, members, earnings, cancellation);
                    }
                }

                rules.Add(new RuleNightRow(index, evaluator.Family, row.Candidate, evaluated, ruleListed.GetValueOrDefault(row.Candidate), gates, null));
            }
        }

        // The sector heavyweights' rules: what each book bought on the night, no gates and no stretch.
        foreach (var row in standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is BookEvaluator))
        {
            rules.Add(new RuleNightRow(index, HeavyweightRule.Name, row.Candidate, true, bought.GetValueOrDefault(row.Candidate), null, null));
        }

        await InsertNightsAsync(connection, transaction, index, night, rules, cancellation);

        return new RuleCardsIndex(index, rules.Count, picks, formingRows);
    }

    // The S&P 400's or 600's rules on the night: each family's list drawn by its live rule where one stands and the
    // provisional rule otherwise, its funnel the parts of the rule its members failed in order, each other registered
    // rule's own list, each book's buys, and the breakout's forming members at the rule drawing the list.
    async Task<RuleCardsIndex> IndexAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, IReadOnlyList<RegisterRow> standing, CancellationToken cancellation)
    {
        // An index the night did not read at all, holding no night row, has no rules' night to write; one whose part
        // failed has its rows written as not evaluated, which its cards say.
        if (await IndexNightAsync(connection, transaction, index, night, cancellation) is not var (fault, open))
        {
            return new RuleCardsIndex(index, 0, 0, 0);
        }

        var computed = fault is null;
        var answers = computed ? await AnswersAsync(connection, transaction, index, night, cancellation) : [];
        var listed = await CountsAsync(connection, transaction, IndexListed, [("$index", index), ("$night", Stamp(night))], cancellation);
        var ruleListed = await CountsAsync(connection, transaction, IndexRuleListed, [("$index", index), ("$night", Stamp(night))], cancellation);
        var bought = await CountsAsync(connection, transaction, IndexBought, [("$index", index), ("$night", Stamp(night))], cancellation);
        var own = standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate rule && rule.Index == index).ToArray();
        var rules = new List<RuleNightRow>();
        var formingRows = 0;

        foreach (var family in IndexNightRead.Families)
        {
            var live = own.FirstOrDefault(row => CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate rule && rule.SetupFamily == family && FamilyRecords.IsLive(row.Candidate));
            var drawnBy = live?.Candidate ?? ProvisionalRule;
            var reasons = answers.Where(answer => answer.Family == family).ToArray();

            rules.Add(new RuleNightRow(index, family, drawnBy, computed, listed.GetValueOrDefault(family), computed ? IndexFunnel(reasons) : null, null));

            foreach (var variant in own.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate rule && rule.SetupFamily == family && row != live))
            {
                rules.Add(new RuleNightRow(index, family, variant.Candidate, computed, ruleListed.GetValueOrDefault(variant.Candidate), null, null));
            }

            if (family == BreakoutRule.Name && computed)
            {
                formingRows += await IndexFormingAsync(connection, transaction, index, night, drawnBy, live, open, cancellation);
            }
        }

        // The index's sector heavyweights: its own book where no live rule stands, and each registered rule's book.
        var heavyweights = standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexHeavyweightCandidate rule && rule.Index == index).ToArray();
        var liveBook = heavyweights.FirstOrDefault(row => FamilyRecords.IsLive(row.Candidate));
        var ownBought = Convert.ToInt32(await ScalarAsync(connection, IndexOwnBought, [("$index", index), ("$night", Stamp(night))], cancellation, transaction), CultureInfo.InvariantCulture);

        rules.Add(new RuleNightRow(index, HeavyweightRule.Name, liveBook?.Candidate ?? ProvisionalRule, computed, liveBook is null ? ownBought : bought.GetValueOrDefault(liveBook.Candidate), null, null));

        foreach (var row in heavyweights.Where(row => row != liveBook))
        {
            rules.Add(new RuleNightRow(index, HeavyweightRule.Name, row.Candidate, computed, bought.GetValueOrDefault(row.Candidate), null, null));
        }

        await InsertNightsAsync(connection, transaction, index, night, rules, cancellation);

        return new RuleCardsIndex(index, rules.Count, 0, formingRows);
    }

    // The members forming a breakout on the S&P 400 or 600 under the rule drawing its list: those passing the index's
    // floors and profit gate as the provisional rule applies them, read through the breakout rule's own gates over the
    // member's year of bars, the market check the index's own breadth.
    async Task<int> IndexFormingAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, string rule, RegisterRow? live, bool open, CancellationToken cancellation)
    {
        var (names, income) = await IndexFamilies.InputsAsync(connection, index, night, cancellation);

        if (IndexNightRead.Prepare(index, night, names, income) is not { } inputs)
        {
            return 0;
        }

        var settings = live is null ? BreakoutRule.Live : BreakoutCandidate.SettingsOf(CandidateEvaluator.Read(live.Parameters));
        var market = new Gate(FamilyRule.Market, open, open ? "the index's breadth at or above the floor" : "the index's breadth under the floor", FamilyRule.Values());
        var members = new List<FormingMember>();

        foreach (var name in inputs.Held)
        {
            if (IndexNightRead.FailsOn(inputs, name, 1m, MemberReadings.LowestPrice, IndexQuality.Profit) is not null)
            {
                continue;
            }

            var one = inputs.Series[name];
            var bar = IndexNightRead.BarOf(one, inputs.At);
            var bars = one.Bars.Take(bar + 1).Select(day => new FamilyBar(day.Session, day.High, day.Low, day.Close, day.Volume)).ToArray();
            var window = bars.Length > 1 ? bars[Math.Max(0, bars.Length - 1 - MemberReadings.DollarVolumeSessions)..(bars.Length - 1)] : [];
            double? average = window.Length == MemberReadings.DollarVolumeSessions ? window.Average(day => Statistic.FromVolume(day.Volume)) : null;
            var breakout = new BreakoutInputs(one.Name.Ticker, market, bars, average, one.Atr[bar] > 0 ? one.Atr[bar] : null, []);

            if (FormingList.Read(breakout, settings, forming) is { } member)
            {
                members.Add(member);
            }
        }

        var earnings = await NextEarningsAsync(connection, transaction, night, cancellation);

        return await InsertFormingAsync(connection, transaction, index, night, rule, FormingList.Order(members), earnings, cancellation);
    }

    // The members passing each gate and every gate before it, in the rule's order, read off each member's answers by
    // gate name.
    public static IReadOnlyList<(string Gate, int Passed)> Funnel(IReadOnlyList<string> order, IReadOnlyList<IReadOnlyDictionary<string, string>> answers)
    {
        var counts = new List<(string, int)>();

        foreach (var (gate, at) in order.Select((gate, at) => (gate, at)))
        {
            counts.Add((gate, answers.Count(member => order.Take(at + 1).All(before => member.GetValueOrDefault(before) == Passed))));
        }

        return counts;
    }

    // The members passing each part of an index rule and every part before, read off the first part each failed.
    public static IReadOnlyList<(string Gate, int Passed)> IndexFunnel(IReadOnlyList<(string Family, bool Passed, string? Reason)> answers)
    {
        string[] parts = [IndexNightRead.MarketClosed, IndexNightRead.NoSetup, IndexNightRead.UnderTheFloors, IndexNightRead.NoProfit, IndexNightRead.NoCover];
        var counts = new List<(string, int)>();

        foreach (var (part, at) in parts.Select((part, at) => (part, at)))
        {
            counts.Add((part, answers.Count(answer => answer.Passed || (answer.Reason is { } reason && Array.IndexOf(parts, reason) > at))));
        }

        return counts;
    }

    public const string Passed = "passed";

    public const string Failed = "failed";

    // A pullback variant's picks on the night from its own verdicts: the members it fired on in the list's own order,
    // reward to risk on the plan the variant reads, then strength, then band strength and then the ticker, at most five,
    // none whose stock it holds a trade on still open and none whose plan places no stop below the buy.
    public static IReadOnlyList<RulePick> KeepPullbackPicks(IEnumerable<(FilterRow Member, ShadowOutcome Verdict)> fired, IReadOnlySet<string> held)
    {
        var kept = new List<RulePick>();

        foreach (var (member, verdict) in fired
            .Select(pair => (pair.Member, pair.Verdict, Plan: PlanOf(pair.Member, pair.Verdict)))
            .Where(pair => pair.Plan is not null)
            .OrderByDescending(pair => pair.Plan!.Value.RewardToRisk ?? double.MinValue)
            .ThenByDescending(pair => pair.Member.Strength ?? double.MinValue)
            .ThenByDescending(pair => pair.Member.BandStrength ?? int.MinValue)
            .ThenBy(pair => pair.Member.Ticker, StringComparer.Ordinal)
            .Select(pair => (pair.Member, pair.Verdict)))
        {
            if (kept.Count == SetupFamilies.ListedANight)
            {
                break;
            }

            if (held.Contains(member.Ticker))
            {
                continue;
            }

            var plan = PlanOf(member, verdict)!.Value;

            kept.Add(new RulePick(member.Ticker, kept.Count + 1, plan.Entry, plan.Stop, plan.Target, plan.RewardToRisk, SetupFamilies.Pullbacks.CapSessions, member.Why));
        }

        return kept;
    }

    // The plan a pullback variant reads for a member: the swing plan at the nearest bands where its verdict names it and
    // the plan clear of the noise otherwise, none where the row holds no stop below the buy.
    static (decimal Entry, decimal Stop, decimal? Target, double? RewardToRisk)? PlanOf(FilterRow member, ShadowOutcome verdict)
    {
        var swing = verdict.Values.GetValueOrDefault("plan") == "swing";
        var stop = swing ? member.SwingStop : member.ClearStop;

        if (member.Entry is not { } entry || stop is not { } placed || placed >= entry || placed <= 0)
        {
            return null;
        }

        return (entry, placed, swing ? member.SwingTarget : member.ClearTarget, swing ? member.SwingRewardToRisk : member.ClearRewardToRisk);
    }

    // One pick a rule of its own keeps.
    public sealed record RulePick(string Ticker, int Place, decimal Entry, decimal Stop, decimal? Target, double? RewardToRisk, int Cap, string Why);

    // One member's swing filter row on the night as the stage reads it.
    public sealed record FilterRow(
        string Ticker,
        IReadOnlyList<ShadowOutcome> Shadow,
        double? Strength,
        int? BandStrength,
        decimal? Entry,
        decimal? SwingStop,
        decimal? SwingTarget,
        double? SwingRewardToRisk,
        decimal? ClearStop,
        decimal? ClearTarget,
        double? ClearRewardToRisk,
        string Why);

    sealed record OpenPick(string Rule, string Ticker, DateOnly Session, decimal Entry, decimal Stop, decimal? Target, int Cap);

    // The open picks walked on the closes to the night: each ended as the family's walk ends it, and the ones still
    // open after, which hold their stock off their rule's list.
    async Task<IReadOnlyList<OpenPick>> WalkOpenPicksAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, IReadOnlyList<OpenPick> open, CancellationToken cancellation)
    {
        if (open.Count == 0)
        {
            return [];
        }

        var from = open.Min(pick => pick.Session);
        var calendar = await FamilyRecorder.SessionsAsync(connection, from, cancellation);
        var closes = await FamilyRecorder.ClosesAsync(connection, from, cancellation);
        var at = calendar.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);
        var still = new List<OpenPick>();

        foreach (var pick in open)
        {
            if (FamilyRecorder.Walk(pick.Session, pick.Entry, pick.Stop, pick.Target, pick.Cap, closes.GetValueOrDefault(pick.Ticker), calendar, at) is { } end && end.On <= night)
            {
                await ExecuteAsync(connection, transaction, EndPick,
                [
                    ("$ended_on", Stamp(end.On)), ("$result", end.Result is { } made ? made : DBNull.Value),
                    ("$index", index), ("$rule", pick.Rule), ("$ticker", pick.Ticker), ("$session", Stamp(pick.Session)),
                ], cancellation);
            }
            else
            {
                still.Add(pick);
            }
        }

        return still;
    }

    async Task InsertNightsAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, IReadOnlyList<RuleNightRow> rules, CancellationToken cancellation)
    {
        foreach (var rule in rules)
        {
            // The stretch over the rule's evaluated nights before and the night's own listed count, read where the night
            // evaluated the rule and it is a rule a stretch is read for, which the sector heavyweights' are not.
            var stretch = rule.Evaluated && rule.Family != HeavyweightRule.Name
                ? RuleStretch.Read([.. await ListedBeforeAsync(connection, transaction, index, rule.Rule, night, cancellation), rule.Listed])
                : null;

            await InsertNightAsync(connection, transaction, index, night, rule with { Stretch = stretch }, FromTheNight, cancellation);
        }
    }

    async Task InsertNightAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, RuleNightRow rule, string source, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, InsertNight,
        [
            ("$index", index),
            ("$night", Stamp(night)),
            ("$family", rule.Family),
            ("$rule", rule.Rule),
            ("$evaluated", rule.Evaluated ? 1 : 0),
            ("$listed", rule.Listed),
            ("$gates", rule.Gates is null ? DBNull.Value : RuleRows.GatesJson(rule.Gates)),
            ("$stretch", rule.Stretch?.Stretch is { } stretch ? stretch : DBNull.Value),
            ("$mark", rule.Stretch?.Mark is { } mark ? mark : DBNull.Value),
            ("$flagged", rule.Stretch?.Flagged == true ? 1 : 0),
            ("$completed", rule.Stretch?.CompletedStretches is { } completed ? completed : DBNull.Value),
            ("$sessions", rule.Stretch?.Sessions is { } sessions ? sessions : DBNull.Value),
            ("$source", source),
        ], cancellation);

    async Task InsertPickAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string rule, DateOnly night, RulePick pick, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, InsertPick,
        [
            ("$index", index),
            ("$rule", rule),
            ("$ticker", pick.Ticker),
            ("$night", Stamp(night)),
            ("$family", SetupFamilies.Pullback),
            ("$place", pick.Place),
            ("$entry", Money.ToStorage(pick.Entry)),
            ("$stop", Money.ToStorage(pick.Stop)),
            ("$target", pick.Target is { } target ? Money.ToStorage(target) : DBNull.Value),
            ("$reward_to_risk", pick.RewardToRisk is { } reward ? reward : DBNull.Value),
            ("$cap", pick.Cap),
            ("$why", pick.Why),
        ], cancellation);

    async Task<int> InsertFormingAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, string rule, IReadOnlyList<FormingMember> members, IReadOnlyDictionary<string, DateOnly> earnings, CancellationToken cancellation)
    {
        var sessions = members.Count > 0 ? await SessionsAfterAsync(connection, transaction, night, cancellation) : [];
        var place = 0;

        foreach (var member in members.Take(forming.Rows))
        {
            // The next earnings date within the stated sessions after the night, read off the exchange's sessions the
            // store holds after it and the calendar's dates where it holds none that far.
            DateOnly? next = earnings.TryGetValue(member.Ticker, out var date) && WithinSessions(night, date, sessions, forming.EarningsWithinSessions) ? date : null;

            await ExecuteAsync(connection, transaction, InsertForming,
            [
                ("$index", index),
                ("$night", Stamp(night)),
                ("$rule", rule),
                ("$place", ++place),
                ("$ticker", member.Ticker),
                ("$close", Money.ToStorage(member.Close)),
                ("$high", Money.ToStorage(member.High)),
                ("$moves_under", member.MovesUnder),
                ("$volume_needed", member.VolumeNeeded),
                ("$volume", member.Volume),
                ("$range_ratio", member.RangeRatio),
                ("$missing", JsonSerializer.Serialize(member.Missing)),
                ("$next_earnings", next is { } within ? Stamp(within) : DBNull.Value),
                ("$forming", members.Count),
            ], cancellation);
        }

        return place;
    }

    // Whether a date falls within the stated sessions after the night: counted over the sessions the store holds after
    // the night where they reach it, and over calendar days at five sessions a week where they do not.
    public static bool WithinSessions(DateOnly night, DateOnly date, IReadOnlyList<DateOnly> sessionsAfter, int sessions)
    {
        if (date <= night)
        {
            return false;
        }

        var counted = sessionsAfter.Count(session => session <= date);

        if (sessionsAfter.Count > 0 && sessionsAfter[^1] >= date)
        {
            return counted <= sessions;
        }

        var days = date.DayNumber - (sessionsAfter.Count > 0 ? sessionsAfter[^1] : night).DayNumber;

        return counted + ((days * 5) + 6) / 7 <= sessions;
    }

    async Task<IReadOnlyList<int>> ListedBeforeAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string rule, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, ListedBefore, [("$index", index), ("$rule", rule), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var listed = new List<int>();

        while (await reader.ReadAsync(cancellation))
        {
            listed.Add(reader.GetInt32(0));
        }

        return listed;
    }

    async Task<IReadOnlyList<OpenPick>> OpenPicksAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, OpenPicks, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var open = new List<OpenPick>();

        while (await reader.ReadAsync(cancellation))
        {
            open.Add(new OpenPick(
                reader.GetString(0),
                reader.GetString(1),
                Date(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3)),
                Money.FromStorage(reader.GetString(4)),
                reader.IsDBNull(5) ? null : Money.FromStorage(reader.GetString(5)),
                reader.GetInt32(6)));
        }

        return open;
    }

    async Task<IReadOnlyList<FilterRow>> FilterRowsAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, FilterRows, [("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var rows = new List<FilterRow>();

        while (await reader.ReadAsync(cancellation))
        {
            rows.Add(new FilterRow(
                reader.GetString(0),
                FamilyRuleShadow.Read(reader.IsDBNull(1) ? null : reader.GetString(1)),
                reader.IsDBNull(3) ? null : reader.GetDouble(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                Price(reader, 5),
                Price(reader, 6),
                Price(reader, 7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                Price(reader, 9),
                Price(reader, 10),
                reader.IsDBNull(11) ? null : reader.GetDouble(11),
                WhyOf(reader.IsDBNull(2) ? null : reader.GetString(2))));
        }

        return rows;
    }

    // The figures behind a member's listing as the swing filter stored them: each gate's values, which a pick's card draws.
    static string WhyOf(string? gates)
    {
        if (string.IsNullOrEmpty(gates))
        {
            return "{}";
        }

        using var document = JsonDocument.Parse(gates);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        if (document.RootElement.TryGetProperty("gates", out var list))
        {
            foreach (var gate in list.EnumerateArray())
            {
                if (gate.TryGetProperty("values", out var held))
                {
                    foreach (var value in held.EnumerateObject())
                    {
                        values.TryAdd(value.Name, value.Value.GetString() ?? string.Empty);
                    }
                }
            }
        }

        return JsonSerializer.Serialize(values);
    }

    static decimal? Price(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? null : Money.FromStorage(reader.GetString(column));

    async Task<(string? Fault, bool Open)?> IndexNightAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, IndexNightRow, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        if (!await reader.ReadAsync(cancellation))
        {
            return null;
        }

        return (reader.IsDBNull(0) ? null : reader.GetString(0), !reader.IsDBNull(1) && reader.GetInt32(1) == 1);
    }

    async Task<IReadOnlyList<(string Family, bool Passed, string? Reason)>> AnswersAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, IndexAnswers, [("$index", index), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var answers = new List<(string, bool, string?)>();

        while (await reader.ReadAsync(cancellation))
        {
            answers.Add((reader.GetString(0), reader.GetInt32(1) == 1, reader.IsDBNull(2) ? null : reader.GetString(2)));
        }

        return answers;
    }

    async Task<IReadOnlyDictionary<string, DateOnly>> NextEarningsAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, NextEarnings, [("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var next = new Dictionary<string, DateOnly>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            next[reader.GetString(0)] = Date(reader.GetString(1));
        }

        return next;
    }

    async Task<IReadOnlyList<DateOnly>> SessionsAfterAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, SessionsAfter, [("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var sessions = new List<DateOnly>();

        while (await reader.ReadAsync(cancellation))
        {
            sessions.Add(Date(reader.GetString(0)));
        }

        return sessions;
    }

    static async Task<IReadOnlyDictionary<string, int>> CountsAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        await using var reader = await command.ExecuteReaderAsync(cancellation);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellation))
        {
            counts[reader.GetString(0)] = reader.GetInt32(1);
        }

        return counts;
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int rows, string outcome, string detail, CancellationToken cancellation) =>
        await ExecuteAsync(connection, null, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Stage),
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
            await AppendAsync(connection, runId, startedAt, rows, NotComputed, detail, cancellation);
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

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = Command(connection, transaction, sql, parameters);

        return await command.ExecuteScalarAsync(cancellation);
    }

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
