using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
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

// One standing rule's night, as its row stores it, with how many members the night read for the rule where it counted
// them, each of which reached its first gate.
public sealed record RuleNightRow(string Index, string Family, string Rule, bool Evaluated, int Listed, IReadOnlyList<(string Gate, int Passed)>? Gates, StretchReading? Stretch, int? Reached = null);

// The cards' rule rows. After the families have drawn each index's list, and for every rule standing on each index, live
// or variant, the stage writes the rule's night: whether the night evaluated it, how many it listed with zeros, how many
// members reached its first gate and passed each of its gates and every gate before, and its empty stretch against the
// mark counted over its own past empty nights, replayed from the pulled history by the record command and carried on by
// the night. For a rule whose list no other table keeps, the swing filter's variants, it keeps the rule's own picks, five
// a night in the list's own order with one open trade a stock, each walked on the closes since as the family's walk ends
// a trade. For each breakout rule it keeps the members forming a breakout, at most the stated rows with the whole count
// on each, the nearest misses first. A failure in one index's part leaves that index's rows of the night unwritten and
// named on the stage's row, and the step goes on; the stage makes no request and calls no model. A night run again
// replaces its own rows.
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
// see: The forming list advises and never lists a stock
// see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
// see: The nightly run is arithmetic only
public sealed class RuleCards : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.PulledBar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.ReleaseDocument, Touch.Read),
            new StoreTouch(Store.ReleaseReading, Touch.Read),
            new StoreTouch(Store.ReadingPin, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FamilyPick, Touch.Read),
            new StoreTouch(Store.FamilyTrade, Touch.Read),
            new StoreTouch(Store.HeavyweightRuleHolding, Touch.Read),
            new StoreTouch(Store.IndexFamilyNight, Touch.Read),
            new StoreTouch(Store.IndexFamilyResult, Touch.Read),
            new StoreTouch(Store.ProvisionalSetting, Touch.Read),
            new StoreTouch(Store.IndexFamilyPick, Touch.Read),
            new StoreTouch(Store.IndexRuleTrade, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read),
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

    // A night run again replaces its own rows: its rule rows, its picks, its forming rows, and the end it wrote on an
    // earlier pick at its own close, which the walk writes again.
    const string ClearTheNight = @"
        DELETE FROM rule_night WHERE index_code = $index AND session_date = $night AND source = 'night';
        DELETE FROM rule_pick WHERE index_code = $index AND session_date = $night;
        UPDATE rule_pick SET ended_on = NULL, result = NULL WHERE index_code = $index AND ended_on = $night;
        DELETE FROM forming_row WHERE index_code = $index AND session_date = $night;
    ";

    const string ClearTheHistory = "DELETE FROM rule_night WHERE index_code = $index AND family = $family AND rule = $rule AND source = 'history';";

    // The replay's row of a rule on the night's own session, which the night's own row stands in place of.
    const string ClearTheReplayedNight = "DELETE FROM rule_night WHERE index_code = $index AND session_date = $night AND family = $family AND rule = $rule AND source = 'history';";

    // The sessions a night wrote a rule's own row on, which a replay of its history leaves standing.
    const string NightsWritten = "SELECT session_date FROM rule_night WHERE index_code = $index AND family = $family AND rule = $rule AND source = 'night';";

    // A rule's listed counts on the nights before, oldest first, which its stretch is read over.
    const string ListedBefore = @"
        SELECT listed FROM rule_night
        WHERE index_code = $index AND family = $family AND rule = $rule AND evaluated = 1 AND session_date < $night
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

    // Every part of one member's rule under a family as the night stored them, the market check first, the same for every
    // member of the family.
    const string FamilyGates = @"
        SELECT gates FROM index_family_result
        WHERE index_code = $index AND session_date = $night AND family = $family AND gates IS NOT NULL
        ORDER BY ticker LIMIT 1;
    ";

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
    // to it. The night's own rows stand beside them and are read after them, a session a night wrote the rule's own row
    // on keeping that row, and the rows written are counted.
    public async Task<int> WriteHistoryAsync(string index, string family, string rule, IReadOnlyList<(DateOnly Session, int Listed)> nights, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheHistory, [("$index", index), ("$family", family), ("$rule", rule)], cancellation);

        var written = new HashSet<string>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = NightsWritten;
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$family", family);
            command.Parameters.AddWithValue("$rule", rule);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                written.Add(reader.GetString(0));
            }
        }

        var ordered = nights.OrderBy(night => night.Session).ToArray();
        var readings = RuleStretch.ReadEach([.. ordered.Select(night => night.Listed)]);
        var rows = 0;

        for (var at = 0; at < ordered.Length; at++)
        {
            if (written.Contains(Stamp(ordered[at].Session)))
            {
                continue;
            }

            await InsertNightAsync(connection, transaction, index, ordered[at].Session, new RuleNightRow(index, family, rule, true, ordered[at].Listed, null, readings[at]), FromTheHistory, cancellation);
            rows++;
        }

        await transaction.CommitAsync(cancellation);

        return rows;
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

        // Every pick open before the night holds its stock off its rule's list tonight, one ending at tonight's close
        // among them, which frees the stock from the night after; the walk ends those the closes since have ended.
        await WalkOpenPicksAsync(connection, transaction, index, night, open, cancellation);

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
                var held = open.Where(pick => pick.Rule == row.Candidate).Select(pick => pick.Ticker).ToHashSet(StringComparer.Ordinal);
                var kept = KeepPullbackPicks(verdicts.Where(pair => pair.Verdict!.Fired).Select(pair => (pair.member, pair.Verdict!)), held);

                foreach (var pick in kept)
                {
                    await InsertPickAsync(connection, transaction, index, row.Candidate, night, pick, cancellation);
                }

                picks += kept.Count;
                count = kept.Count;
            }

            rules.Add(new RuleNightRow(index, SetupFamilies.Pullback, row.Candidate, evaluated, count, gates, null, evaluated ? verdicts.Length : null));
        }

        // The breakout's and the drift's rules, each evaluated over every member at its own settings for its funnel, and
        // each breakout rule's forming members.
        var familyRules = standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is FamilyRuleEvaluator and not IndexRuleCandidate).ToArray();

        if (familyRules.Length > 0)
        {
            var shadow = FamilyRuleShadow.For(standing, DateTimeOffset.MaxValue);
            var inputs = await FamilyEvaluator.InputsAsync(connection, night, shadow.DriftWindowReach, cancellation, shadow.ReadingVersions);
            var earnings = await NextEarningsAsync(connection, transaction, night, cancellation);

            foreach (var row in familyRules)
            {
                var evaluator = (FamilyRuleEvaluator)CandidateEvaluators.Find(row.Evaluator)!;
                var evaluated = inputs is not null && evaluator.Version == row.EvaluatorVersion;
                var parameters = CandidateEvaluator.Read(row.Parameters);
                IReadOnlyList<(string Gate, int Passed)>? gates = null;
                int? reached = null;

                if (evaluated)
                {
                    var results = inputs!.Members.Where(member => member.Withheld is null).Select(member => evaluator.EvaluateMember(member.Inputs, parameters)).ToArray();
                    IReadOnlyList<string> order = evaluator.Family == BreakoutRule.Name ? BreakoutRule.Order : DriftRule.Order;

                    // From 20.4 a rule reading a release reading counts its print's release read and then its tone after
                    // its own gates, so a night holding no reading says on its card where its members stopped.
                    // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
                    if (ReleaseTone.VersionOf(parameters) > 0)
                    {
                        order = [.. order, ReleaseTone.ReadStep, ReleaseTone.Gate];
                    }

                    gates = Funnel(order, results.Select(Answers).ToArray());
                    reached = results.Length;

                    if (evaluator.Family == BreakoutRule.Name)
                    {
                        var settings = BreakoutCandidate.SettingsOf(parameters);
                        var members = FormingList.Order(inputs.Members.Where(member => member.Withheld is null).Select(member => FormingList.Read(member.Inputs.Breakout, settings, forming)).OfType<FormingMember>());

                        formingRows += await InsertFormingAsync(connection, transaction, index, night, row.Candidate, members, earnings, cancellation);
                    }
                }

                rules.Add(new RuleNightRow(index, evaluator.Family, row.Candidate, evaluated, ruleListed.GetValueOrDefault(row.Candidate), gates, null, reached));
            }
        }

        // The sector heavyweights' rules: what each book bought on the night, no gates and no stretch.
        foreach (var row in standing.Where(row => CandidateEvaluators.Find(row.Evaluator) is BookEvaluator))
        {
            rules.Add(new RuleNightRow(index, HeavyweightRule.Name, row.Candidate, true, bought.GetValueOrDefault(row.Candidate), null, null));
        }

        // The fundamentals-first family's provisional rule, which the index families' step reads on the S&P 500 under its
        // own rows: its listed count and its funnel off the parts of the rule its members failed, where that step read it.
        // see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
        if (await IndexNightAsync(connection, transaction, index, night, cancellation) is var (fault, _))
        {
            var computed = fault is null;
            var indexListed = await CountsAsync(connection, transaction, IndexListed, [("$index", index), ("$night", Stamp(night))], cancellation);
            var answers = computed ? await AnswersAsync(connection, transaction, index, night, cancellation) : [];

            foreach (var family in IndexNightRead.LargeFamilies)
            {
                (string Family, bool Passed, string? Reason)[] read = [.. answers.Where(answer => answer.Family == family)];

                rules.Add(new RuleNightRow(index, family, ProvisionalRule, computed, indexListed.GetValueOrDefault(family), computed ? IndexFunnel(read, family) : null, null, computed ? read.Length : null));
            }
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

            rules.Add(new RuleNightRow(index, family, drawnBy, computed, listed.GetValueOrDefault(family), computed ? IndexFunnel(reasons, family) : null, null, computed ? reasons.Length : null));

            foreach (var variant in own.Where(row => CandidateEvaluators.Find(row.Evaluator) is IndexRuleCandidate rule && rule.SetupFamily == family && row != live))
            {
                var tone = computed ? await ToneFunnelAsync(connection, index, night, variant, cancellation) : null;

                rules.Add(new RuleNightRow(index, family, variant.Candidate, computed, ruleListed.GetValueOrDefault(variant.Candidate), tone?.Gates, null, tone?.Reached));
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

    // From 20.4 an S&P 400's or 600's drift variant naming a reading version read again over the night for its funnel, the
    // parts its members failed in order with its print's release read and its tone after the provisional rule's own, so a
    // night holding no reading says on its card where its members stopped, with how many members it read; none for every
    // other variant, whose funnel the night does not keep, and none for a variant the night did not read.
    // see: The drift's tone variant lists only where its print's release reads better than expected and its outlook was not lowered or withdrawn
    static async Task<(IReadOnlyList<(string Gate, int Passed)> Gates, int Reached)?> ToneFunnelAsync(SqliteConnection connection, string index, DateOnly night, RegisterRow row, CancellationToken cancellation)
    {
        var parameters = CandidateEvaluator.Read(row.Parameters);

        if (CandidateEvaluators.Find(row.Evaluator) is not IndexDriftCandidate drift
            || ReleaseTone.VersionOf(parameters) is not (var version and > 0)
            || drift.Version != row.EvaluatorVersion
            || IndexRules.Refusal(drift, parameters) is not null)
        {
            return null;
        }

        var (names, income) = await IndexFamilies.InputsAsync(connection, index, night, cancellation);

        if (IndexNightRead.Prepare(index, night, names, income) is not { } inputs)
        {
            return null;
        }

        // A variant reading the revenue level reads each member's quarters as first filed and stored before the night, as
        // the night read it.
        // see: A drift's revenue level on the S&P 400 and 600 reads a quarter only once its filing is stored, on the night and in its sweep alike
        var levels = await IndexFamilies.LevelsAsync(connection, inputs, cancellation, [version], parameters.GetValueOrDefault(IndexDriftCandidate.RevenueGrewParameter) == 1);
        var answers = IndexNightRead.Answers(inputs, DriftRule.Name, IndexRules.Read(inputs, new IndexRule(row.Candidate, drift, parameters, row.RegisteredAt), levels));

        return (IndexFunnel([.. answers.Select(answer => (answer.Family, answer.Passed, answer.Reason))], DriftRule.Name, tone: true), answers.Count);
    }

    // The members forming a breakout on the S&P 400 or 600 under the rule drawing its list: those passing the index's
    // floors and profit gate as the provisional rule applies them, read through the breakout rule's own gates over the
    // member's year of bars, with the sessions before it the night read from the pulled history where the year holds fewer
    // than its high reads, the market check the one the rule drawing the list read on the night, as the night stored it
    // among each member's parts, and the index's own check on a night that stored none.
    // see: A night's reading needing more closes than the store's year holds reads the sessions before it from the pulled history at the store's scale
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    async Task<int> IndexFormingAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, string rule, RegisterRow? live, bool open, CancellationToken cancellation)
    {
        var (names, income) = await IndexFamilies.InputsAsync(connection, index, night, cancellation);

        if (IndexNightRead.Prepare(index, night, names, income) is not { } inputs)
        {
            return 0;
        }

        // The live rule's settings where one stands, the setting an approval stored for the provisional breakout where one
        // did, and the provisional setting otherwise.
        // see: An approved change is applied by the night's own build on the index it was approved on alone
        var stored = live is null ? await IndexFamilies.StoredSettingsAsync(connection, index, cancellation, transaction) : null;
        var settings = live is not null ? BreakoutCandidate.SettingsOf(CandidateEvaluator.Read(live.Parameters))
            : stored?.GetValueOrDefault(BreakoutRule.Name)?.Places is { } places ? BreakoutAt(places)
            : BreakoutRule.Live;
        var market = MarketOf(await ScalarAsync(connection, FamilyGates, [("$index", index), ("$night", Stamp(night)), ("$family", BreakoutRule.Name)], cancellation, transaction) as string)
            ?? new Gate(FamilyRule.Market, open, open ? "the index's breadth at or above the floor" : "the index's breadth under the floor", FamilyRule.Values());
        var members = new List<FormingMember>();

        foreach (var name in inputs.Held)
        {
            if (IndexNightRead.FailsOn(inputs, name, 1m, MemberReadings.LowestPrice, IndexQuality.Profit) is not null)
            {
                continue;
            }

            var one = inputs.Series[name];
            var bar = IndexNightRead.BarOf(one, inputs.At);
            var bars = (one.Name.Before ?? []).Concat(one.Bars.Take(bar + 1)).Select(day => new FamilyBar(day.Session, day.High, day.Low, day.Close, day.Volume)).ToArray();
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

    // The market check a family's parts stored on the night hold, as the gate the breakout rule reads its market by; none
    // where the night stored no part.
    public static Gate? MarketOf(string? stored) =>
        stored is not null && FamilyRule.GatesOf(stored).FirstOrDefault(gate => gate.Name == RuleRows.Market) is { } market
            ? market with { Name = FamilyRule.Market }
            : null;

    // The breakout's settings at a setting of its sweep's grid.
    static BreakoutSettings BreakoutAt(IReadOnlyList<int> places)
    {
        int[] setting = [.. places];

        return new BreakoutSettings((int)BreakoutSweep.Grid.Value(setting, 0), BreakoutSweep.Grid.Value(setting, 1), BreakoutSweep.Grid.Value(setting, 2), BreakoutSweep.Grid.Value(setting, 3));
    }

    // A member's answer by gate name, a tone gate's release read beside it as a step of its own.
    static IReadOnlyDictionary<string, string> Answers(FamilyResult result)
    {
        var answers = result.Gates.ToDictionary(gate => gate.Name, gate => gate.Passed ? Passed : Failed, StringComparer.Ordinal);

        if (result.Gates.FirstOrDefault(gate => gate.Name == ReleaseTone.Gate) is { } tone)
        {
            answers[ReleaseTone.ReadStep] = ReleaseTone.IsRead(tone) ? Passed : Failed;
        }

        return answers;
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

    // The parts past the profit and the cover a registered rule's levels read, in the order its night reads them, each by
    // the reason a member failing it carries: the industry's fall, the breakout's three levels, the drift's peers and its
    // revenue, the switches, the tone's read and its answer, and a condition an approved setting's hooks add.
    static readonly string[] LevelReasons =
    [
        IndexRules.IndustryFell,
        IndexRules.NotNearItsHigh,
        IndexRules.HighNotAsStated,
        IndexRules.VolumeUnder,
        IndexRules.PeersNotAbove,
        IndexRules.RevenueNotAbove,
        IndexRules.SwitchClosed,
        ReleaseTone.NotHeldOnTheIndex,
        ReleaseTone.FailedOnTheIndex,
        IndexNightRead.NotByItsHooks,
    ];

    // The members passing each part of an index rule and every part before, read off the first part each failed and
    // named as the part, not as the reason a member fails it; the fundamentals-first family's parts its own, the
    // business's after the floors; and from 20.4 a drift rule's tone after the swing families' parts. From 20.11 a level
    // a registered rule reads is a part of its own after the cover, in the order the night reads it, where a member failed
    // it, so a member a frozen rule's level kept off is never counted as stopping at the market check.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    public static IReadOnlyList<(string Gate, int Passed)> IndexFunnel(IReadOnlyList<(string Family, bool Passed, string? Reason)> answers, string? family = null, bool tone = false)
    {
        string[] reasons = family == FundamentalsRule.Name
            ? [IndexNightRead.MarketClosed, IndexNightRead.NoSetup, IndexNightRead.UnderTheFloors, FundamentalsRule.NoReadings, FundamentalsRule.NoProfit, FundamentalsRule.NoRevenue, FundamentalsRule.NoMargin, FundamentalsRule.NoCash, FundamentalsRule.NoTrend]
            :
            [
                IndexNightRead.MarketClosed,
                IndexNightRead.NoSetup,
                IndexNightRead.UnderTheFloors,
                IndexNightRead.NoProfit,
                IndexNightRead.NoCover,
                .. LevelReasons.Where(reason =>
                    (tone && reason is ReleaseTone.NotHeldOnTheIndex or ReleaseTone.FailedOnTheIndex)
                    || answers.Any(answer => !answer.Passed && answer.Reason == reason)),
            ];
        var counts = new List<(string, int)>();

        foreach (var (reason, at) in reasons.Select((reason, at) => (reason, at)))
        {
            counts.Add((RuleRows.PartOf(reason), answers.Count(answer => answer.Passed || (answer.Reason is { } failed && Array.IndexOf(reasons, failed) > at))));
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

    // The open picks walked on the closes to the night: each ended as the family's walk ends it, at the first close
    // through its stop, at its target or at its cap's close, and left open otherwise.
    async Task WalkOpenPicksAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, IReadOnlyList<OpenPick> open, CancellationToken cancellation)
    {
        if (open.Count == 0)
        {
            return;
        }

        var from = open.Min(pick => pick.Session);
        var calendar = await FamilyRecorder.SessionsAsync(connection, from, cancellation);
        var closes = await FamilyRecorder.ClosesAsync(connection, from, cancellation);
        var at = calendar.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);

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
        }
    }

    async Task InsertNightsAsync(SqliteConnection connection, SqliteTransaction transaction, string index, DateOnly night, IReadOnlyList<RuleNightRow> rules, CancellationToken cancellation)
    {
        foreach (var rule in rules)
        {
            // The stretch over the rule's evaluated nights before and the night's own listed count, read where the night
            // evaluated the rule and it is a rule a stretch is read for, which the sector heavyweights' are not.
            var stretch = rule.Evaluated && rule.Family != HeavyweightRule.Name
                ? RuleStretch.Read([.. await ListedBeforeAsync(connection, transaction, index, rule.Family, rule.Rule, night, cancellation), rule.Listed])
                : null;

            await ExecuteAsync(connection, transaction, ClearTheReplayedNight, [("$index", index), ("$night", Stamp(night)), ("$family", rule.Family), ("$rule", rule.Rule)], cancellation);
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
            ("$gates", rule.Gates is null ? DBNull.Value : RuleRows.GatesJson(rule.Gates, rule.Reached)),
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

    async Task<IReadOnlyList<int>> ListedBeforeAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string family, string rule, DateOnly night, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, ListedBefore, [("$index", index), ("$family", family), ("$rule", rule), ("$night", Stamp(night))]);
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
