using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

// What one night's book did: the night, whether it rebalanced, how many members it valued, the holdings it entered
// and ended with why, why it read nothing where it did not, why its rebalance waits where it waits, and what each
// registered rule's own book did.
public sealed record HeavyweightBookOutcome(
    DateOnly? Night,
    bool Rebalanced,
    int Valued,
    IReadOnlyList<string> Entered,
    IReadOnlyList<string> Ended,
    int Held,
    string? ReadNothing = null,
    string? Waits = null,
    IReadOnlyList<HeavyweightRuleOutcome>? Rules = null);

// What one registered rule's own book did on the night, or why it was not kept: the rule, whether it rebalanced, what
// it bought and ended, what it holds, why its rebalance waits, and the fault that kept it where the code carries no
// evaluator for it at the version it stands at.
public sealed record HeavyweightRuleOutcome(
    string Candidate,
    bool Rebalanced,
    IReadOnlyList<string> Entered,
    IReadOnlyList<string> Ended,
    int Held,
    string? Waits = null,
    string? Fault = null);

// The sector heavyweights' book, the page's at the settings the family's freeze registered and each registered rule's
// own at its registration's. Every night it carries each open holding's growth, and its size cut's, from the session it
// was last carried to tonight by tonight's closes over that session's, as the store holds both tonight; ends a holding
// whose stock is no longer a member at its last session as one, and, where the settings read it, one closing under its
// 200-session average tonight. On a rebalance, the first night of a month the book reads, or of a week where the
// settings rebalance weekly, it reads every sector's largest companies, values each member from its newest fetch,
// stores each sector's reading, ends the holdings the rule would not buy where the settings sell on that, and buys the
// leaders it does not hold, each with the sector's largest as its size cut. A rebalance reading the sector funds or the
// betas waits for a night the store holds the fund's or the index's close for, so a series the night could not fetch
// moves no holding.
//
// It runs in the swing filter's step after the family recorder, reads what the night and the quarters step stored,
// makes no request and calls no model, and no market check closes it. A night run again replaces what it wrote for
// that night, and a night before one it has read is read for nothing.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month
// see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it
// see: A heavyweight leaving the index is sold at its last session's close as a member
// see: A heavyweight's result is the product of its daily close ratios since its buy, carried each night
// see: The market check closes every swing family's list together, and the sector heavyweights read none
// see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants
// see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
// see: The nightly run is arithmetic only
public sealed class HeavyweightBook : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.ReportedQuarter, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.HeavyweightNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.HeavyweightRuleNight, Touch.Read | Touch.Insert | Touch.Delete),
            new StoreTouch(Store.HeavyweightRuleHolding, Touch.Read | Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "heavyweights";

    // Why a holding ended, as the book stores it and the pages draw it.
    public const string NoLongerTheLeader = "no longer the leader";
    public const string UnderTheAverage = "a close under its 200-day average";
    public const string LeftTheIndex = "left the index";

    const string Sessions = "SELECT DISTINCT session_date FROM bar ORDER BY session_date;";

    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string MembersOn = @"
        SELECT DISTINCT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session);
    ";

    const string Bars = "SELECT ticker, session_date, close, raw_close, volume FROM bar ORDER BY ticker, session_date;";

    // The index's closes and each sector fund's, as the night's market series fetch stored them, to the night.
    const string SeriesCloses = "SELECT series, session_date, close FROM market_bar WHERE session_date <= $night ORDER BY series, session_date;";

    const string Averages = "SELECT ticker, name, value FROM indicator WHERE session_date = $night AND name IN ($fifty, $twoHundred) AND value IS NOT NULL;";

    // Each member's company as its newest storing fetch answered for it.
    const string Companies = @"
        SELECT c.ticker, c.cik, c.sector
        FROM company c
        WHERE c.fetched_at = (SELECT MAX(fetched_at) FROM company WHERE ticker = c.ticker);
    ";

    // The counts of each member's newest fetch storing any, with the day each sheet was filed and the fetch's basis.
    const string Counts = @"
        SELECT r.ticker, r.period_end, r.filing_date, r.shares, r.basis_session, r.basis_close
        FROM reported_quarter r
        WHERE r.shares IS NOT NULL AND r.filing_date IS NOT NULL AND r.basis_session IS NOT NULL AND r.basis_close IS NOT NULL
          AND r.fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = r.ticker AND shares IS NOT NULL);
    ";

    const string NewestRead = @"
        SELECT MAX(at) FROM (
            SELECT MAX(session_date) AS at FROM heavyweight_night
            UNION ALL SELECT MAX(through) FROM heavyweight_holding
            UNION ALL SELECT MAX(session_date) FROM heavyweight_rule_night
            UNION ALL SELECT MAX(through) FROM heavyweight_rule_holding);
    ";

    // The page's book.
    const string LastRebalance = "SELECT MAX(session_date) FROM heavyweight_night WHERE session_date < $night;";

    const string Open = "SELECT ticker, entered_on, sector, company, entry_close, growth, cut, through FROM heavyweight_holding WHERE ended_on IS NULL ORDER BY ticker;";

    // A night run again: what it entered goes, what it ended opens again, and its reading is written again.
    const string ClearTheNight = @"
        DELETE FROM heavyweight_night WHERE session_date = $night;
        DELETE FROM heavyweight_holding WHERE entered_on = $night;
        UPDATE heavyweight_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL WHERE ended_on = $night;
    ";

    const string InsertRead = @"
        INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader, beta)
        VALUES ($night, $sector, $place, $ticker, $company, $company_value, $look_back, $sector_return, $lead, $trend, $leader, $beta);
    ";

    const string Carry = "UPDATE heavyweight_holding SET growth = $growth, cut = $cut, through = $through WHERE ticker = $ticker AND entered_on = $entered_on;";

    const string End = @"
        UPDATE heavyweight_holding
        SET ended_on = $ended_on, exit_close = $exit_close, reason = $reason, result = $result, cut_return = $cut_return
        WHERE ticker = $ticker AND entered_on = $entered_on;
    ";

    const string Enter = @"
        INSERT INTO heavyweight_holding (ticker, entered_on, sector, company, entry_close, growth, cut, through)
        VALUES ($ticker, $night, $sector, $company, $entry_close, 1.0, $cut, $night);
    ";

    // Each registered rule's book, the same statements over the rule's own rows.
    const string LastRuleRebalance = "SELECT MAX(session_date) FROM heavyweight_rule_night WHERE candidate = $candidate AND session_date < $night;";

    const string OpenRule = "SELECT ticker, entered_on, sector, company, entry_close, growth, cut, through FROM heavyweight_rule_holding WHERE candidate = $candidate AND ended_on IS NULL ORDER BY ticker;";

    const string ClearTheRulesNight = @"
        DELETE FROM heavyweight_rule_night WHERE session_date = $night;
        DELETE FROM heavyweight_rule_holding WHERE entered_on = $night;
        UPDATE heavyweight_rule_holding SET ended_on = NULL, exit_close = NULL, reason = NULL, result = NULL, cut_return = NULL WHERE ended_on = $night;
    ";

    const string InsertRuleRead = @"
        INSERT INTO heavyweight_rule_night (candidate, session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader, beta)
        VALUES ($candidate, $night, $sector, $place, $ticker, $company, $company_value, $look_back, $sector_return, $lead, $trend, $leader, $beta);
    ";

    const string CarryRule = "UPDATE heavyweight_rule_holding SET growth = $growth, cut = $cut, through = $through WHERE candidate = $candidate AND ticker = $ticker AND entered_on = $entered_on;";

    const string EndRule = @"
        UPDATE heavyweight_rule_holding
        SET ended_on = $ended_on, exit_close = $exit_close, reason = $reason, result = $result, cut_return = $cut_return
        WHERE candidate = $candidate AND ticker = $ticker AND entered_on = $entered_on;
    ";

    const string EnterRule = @"
        INSERT INTO heavyweight_rule_holding (candidate, ticker, entered_on, sector, company, entry_close, growth, cut, through)
        VALUES ($candidate, $ticker, $night, $sector, $company, $entry_close, 1.0, $cut, $night);
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
    readonly HeavyweightSettings page;

    // The page's book holds at the family's live settings, and a test may hand it another to hold at.
    public HeavyweightBook(IClock clock, string databaseFile, HeavyweightSettings? page = null)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
        this.page = page ?? HeavyweightRule.Live;
    }

    // One session's bar as the book reads it.
    sealed record Bar(DateOnly Session, decimal Close, decimal RawClose, long Volume);

    // A size cut member's growth since the buy and the session it was carried to.
    public sealed record CutMember(string Ticker, double Growth, DateOnly On);

    sealed record Holding(string Ticker, DateOnly EnteredOn, string Sector, string Company, decimal EntryClose, double Growth, IReadOnlyList<CutMember> Cut, DateOnly Through);

    // A member as every book reads it on a rebalance, before a setting's look-back picks its return: its company and
    // sector, its value, its dollars, its close and averages, its beta, and its bars to the night.
    sealed record Valued(string Ticker, string Company, string? Sector, decimal? Value, decimal DollarVolume, double Close, double? Average50, double? Average200, double? Beta, Bar[] UpTo)
    {
        public HeavyweightMember At(int lookBack) =>
            new(Ticker, Company, Sector, Value, DollarVolume, ReturnOver(UpTo, lookBack), Close, Average50, Average200, Beta);
    }

    // A book the night keeps: the page's or a registered rule's, the settings it holds at, and its rule where it is one.
    sealed record Ledger(string? Candidate, HeavyweightSettings Settings);

    // The registered rules a night keeps a book for: the heavyweights' rules standing when the night started.
    public static IReadOnlyList<RegisterRow> Standing(IReadOnlyList<RegisterRow> register, DateTimeOffset nightStartedAt) =>
        [.. ShadowColumn.StandingAt(register, nightStartedAt).Where(row => CandidateEvaluators.Find(row.Evaluator) is BookEvaluator)];

    public async Task<HeavyweightBookOutcome> RunAsync(string indexCode, string runId, CancellationToken cancellation = default, IReadOnlyList<RegisterRow>? rules = null)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var sessions = await ColumnAsync(connection, Sessions, [], cancellation);

        if (sessions.Count == 0)
        {
            return await RecordAsync(connection, runId, startedAt, new HeavyweightBookOutcome(null, false, 0, [], [], 0, "the store holds no session"), cancellation);
        }

        var night = Day(sessions[^1]);

        if (await ScalarAsync(connection, NewestRead, [], cancellation) is string newest && Day(newest) > night)
        {
            return await RecordAsync(connection, runId, startedAt, new HeavyweightBookOutcome(night, false, 0, [], [], 0, FormattableString.Invariant($"the book has read {newest}, a later session than {night:yyyy-MM-dd}, and reads no earlier one")), cancellation);
        }

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await ExecuteAsync(connection, transaction, ClearTheNight, [("$night", Stamp(night))], cancellation);
        await ExecuteAsync(connection, transaction, ClearTheRulesNight, [("$night", Stamp(night))], cancellation);

        var members = (await ColumnAsync(connection, MembersOn, [("$index", indexCode), ("$session", Stamp(night))], cancellation, transaction)).ToHashSet(StringComparer.Ordinal);
        var bars = await BarsAsync(connection, transaction, cancellation);
        var averages = await AveragesAsync(connection, transaction, night, cancellation);
        var series = await SeriesAsync(connection, transaction, night, cancellation);
        IReadOnlyList<Valued>? valued = null;

        decimal? CloseOn(string ticker, DateOnly session) =>
            bars.TryGetValue(ticker, out var held) && held.TryGetValue(session, out var bar) ? bar.Close : null;

        // Each member valued once a night, the first time a book rebalances, its beta read against the index's closes.
        IReadOnlyDictionary<DateOnly, double>? indexCloses = series.TryGetValue(MarketCloses.Index, out var stored)
            ? stored.ToDictionary(pair => pair.Key, pair => Statistic.FromPrice(pair.Value))
            : null;

        async Task<IReadOnlyList<Valued>> ValuedAsync() =>
            valued ??= await ValueAsync(connection, transaction, night, members, bars, averages, indexCloses, cancellation);

        var outcomes = new List<HeavyweightRuleOutcome>();
        var faults = new List<string>();
        var written = 0;

        // Each book in turn, the page's first.
        async Task<(HeavyweightRuleOutcome Outcome, int Valued, int Written)> KeepAsync(Ledger ledger)
        {
            var holdings = await OpenAsync(connection, transaction, ledger, cancellation);
            var ended = new List<string>();
            var entered = new List<string>();
            var rows = 0;
            var open = new List<Holding>();

            // Each open holding carried to tonight, then ended where its stock left the index, or broke its average
            // where the settings sell on that.
            foreach (var holding in holdings)
            {
                if (!members.Contains(holding.Ticker))
                {
                    await EndAsync(connection, transaction, ledger, holding, holding.Through, CloseOn(holding.Ticker, holding.Through) ?? holding.EntryClose, LeftTheIndex, cancellation);
                    ended.Add($"{holding.Ticker}: {LeftTheIndex}");

                    continue;
                }

                var carried = Carried(holding, night, CloseOn);

                await ExecuteAsync(connection, transaction, ledger.Candidate is null ? Carry : CarryRule,
                [
                    .. Owned(ledger),
                    ("$growth", carried.Growth),
                    ("$cut", Cut(carried.Cut)),
                    ("$through", Stamp(carried.Through)),
                    ("$ticker", carried.Ticker),
                    ("$entered_on", Stamp(carried.EnteredOn)),
                ], cancellation);

                if (ledger.Settings.SoldUnderAverage
                    && CloseOn(carried.Ticker, night) is { } close
                    && HeavyweightRule.Broken(Statistic.FromPrice(close), averages.TryGetValue((carried.Ticker, IndicatorSeries.Sma200), out var average) ? average : null))
                {
                    await EndAsync(connection, transaction, ledger, carried, night, close, UnderTheAverage, cancellation);
                    ended.Add($"{carried.Ticker}: {UnderTheAverage}");

                    continue;
                }

                open.Add(carried);
            }

            var last = await ScalarAsync(connection, ledger.Candidate is null ? LastRebalance : LastRuleRebalance, [.. Owned(ledger), ("$night", Stamp(night))], cancellation, transaction) is string stored ? Day(stored) : (DateOnly?)null;

            if (!HeavyweightRule.Rebalances(night, last, ledger.Settings.Weekly))
            {
                return (new HeavyweightRuleOutcome(ledger.Candidate ?? string.Empty, false, entered, ended, open.Count), 0, rows + ended.Count);
            }

            // A rebalance reading what the store holds no close for tonight waits for a night it does.
            if (Waits(ledger.Settings, series, night) is { } waits)
            {
                return (new HeavyweightRuleOutcome(ledger.Candidate ?? string.Empty, false, entered, ended, open.Count, waits), 0, rows + ended.Count);
            }

            var read = await ValuedAsync();
            var sectors = HeavyweightRule.Read(
                [.. read.Select(member => member.At(ledger.Settings.LookBack))],
                ledger.Settings,
                ledger.Settings.FundReturn ? FundReturns(series, night, ledger.Settings.LookBack) : null);
            var buys = HeavyweightRule.Buys(sectors);
            var betas = read.ToDictionary(member => member.Ticker, member => member.Beta, StringComparer.Ordinal);

            foreach (var sector in sectors)
            {
                foreach (var ranked in sector.Largest)
                {
                    rows += await ExecuteAsync(connection, transaction, ledger.Candidate is null ? InsertRead : InsertRuleRead,
                    [
                        .. Owned(ledger),
                        ("$night", Stamp(night)),
                        ("$sector", sector.Sector),
                        ("$place", ranked.Place),
                        ("$ticker", ranked.Ticker),
                        ("$company", ranked.Company),
                        ("$company_value", Money.ToStorage(ranked.Value)),
                        ("$look_back", (object?)ranked.Return ?? DBNull.Value),
                        ("$sector_return", (object?)sector.Return ?? DBNull.Value),
                        ("$lead", (object?)ranked.Lead ?? DBNull.Value),
                        ("$trend", ranked.Trend ? 1 : 0),
                        ("$leader", ranked.Leader ? 1 : 0),
                        ("$beta", (object?)betas.GetValueOrDefault(ranked.Ticker) ?? DBNull.Value),
                    ], cancellation);
                }
            }

            if (ledger.Settings.SoldOnLeading)
            {
                foreach (var holding in open.Where(holding => !buys.Contains(holding.Ticker)).ToArray())
                {
                    await EndAsync(connection, transaction, ledger, holding, night, CloseOn(holding.Ticker, night) ?? holding.EntryClose, NoLongerTheLeader, cancellation);
                    ended.Add($"{holding.Ticker}: {NoLongerTheLeader}");
                    open.Remove(holding);
                }
            }

            foreach (var sector in sectors)
            {
                foreach (var leader in sector.Leaders.Where(leader => open.All(holding => holding.Ticker != leader)))
                {
                    if (CloseOn(leader, night) is not { } close)
                    {
                        continue;
                    }

                    var ranked = sector.Largest.Single(one => one.Ticker == leader);

                    rows += await ExecuteAsync(connection, transaction, ledger.Candidate is null ? Enter : EnterRule,
                    [
                        .. Owned(ledger),
                        ("$ticker", leader),
                        ("$night", Stamp(night)),
                        ("$sector", sector.Sector),
                        ("$company", ranked.Company),
                        ("$entry_close", Money.ToStorage(close)),
                        ("$cut", Cut([.. sector.Largest.Select(one => new CutMember(one.Ticker, 1.0, night))])),
                    ], cancellation);
                    entered.Add(leader);
                }
            }

            return (new HeavyweightRuleOutcome(ledger.Candidate ?? string.Empty, true, entered, ended, open.Count + entered.Count), read.Count(member => member.Value is not null), rows + ended.Count);
        }

        var (book, valuedOnPage, pageRows) = await KeepAsync(new Ledger(null, page));

        written += pageRows;

        foreach (var rule in rules ?? [])
        {
            // A rule the code carries no evaluator for at the version it stands at is kept by no book, and fails the stage.
            if (CandidateEvaluators.Find(rule.Evaluator) is not BookEvaluator evaluator || evaluator.Version != rule.EvaluatorVersion)
            {
                var fault = CandidateEvaluators.Find(rule.Evaluator) is null
                    ? $"the code carries no evaluator named '{rule.Evaluator}'"
                    : $"registered under {rule.Evaluator} at {rule.EvaluatorVersion} and the code carries {CandidateEvaluators.Find(rule.Evaluator)!.Version}";

                faults.Add($"'{rule.Candidate}' {fault}");
                outcomes.Add(new HeavyweightRuleOutcome(rule.Candidate, false, [], [], 0, Fault: fault));

                continue;
            }

            var (kept, _, ruleRows) = await KeepAsync(new Ledger(rule.Candidate, evaluator.SettingsOf(CandidateEvaluator.Read(rule.Parameters))));

            outcomes.Add(kept);
            written += ruleRows;
        }

        var outcome = new HeavyweightBookOutcome(night, book.Rebalanced, valuedOnPage, book.Entered, book.Ended, book.Held, Waits: book.Waits, Rules: outcomes);

        await AppendAsync(connection, transaction, runId, startedAt, outcome, written, faults.Count == 0 ? "ok" : "failed", cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    // Why a rebalance at a setting waits tonight: a setting reading the sector funds where a fund holds no close for
    // the night, and one reading the betas where the index holds none, each named; none where nothing it reads is
    // missing.
    // see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
    static string? Waits(HeavyweightSettings settings, IReadOnlyDictionary<string, SortedDictionary<DateOnly, decimal>> series, DateOnly night)
    {
        bool Holds(string name) => series.TryGetValue(name, out var closes) && closes.ContainsKey(night);

        string[] missing =
        [
            .. settings.HighBeta && !Holds(MarketCloses.Index) ? [MarketCloses.Index] : Array.Empty<string>(),
            .. settings.FundReturn ? GicsSectors.Funds.Values.Order(StringComparer.Ordinal).Where(fund => !Holds(fund)) : [],
        ];

        return missing.Length == 0
            ? null
            : FormattableString.Invariant($"the rebalance waits for a night the store holds a close for {string.Join(", ", missing)} on, since none is stored for {night:yyyy-MM-dd}");
    }

    // Each sector's fund's return over a look-back on the fund's own sessions to the night, none for a fund holding no
    // close for the night or fewer sessions than the look-back reads.
    static IReadOnlyDictionary<string, double?> FundReturns(IReadOnlyDictionary<string, SortedDictionary<DateOnly, decimal>> series, DateOnly night, int lookBack) =>
        GicsSectors.Funds.ToDictionary<KeyValuePair<string, string>, string, double?>(
            pair => pair.Key,
            pair => series.TryGetValue(pair.Value, out var closes) && closes.ContainsKey(night) && closes.Values.ToArray() is var held && held.Length > lookBack && held[^(lookBack + 1)] > 0m
                ? Statistic.FromRatio(held[^1] / held[^(lookBack + 1)]) - 1.0
                : null,
            StringComparer.Ordinal);

    // A stock's return over a look-back to the newest of its bars, none where it holds fewer.
    static double? ReturnOver(Bar[] upTo, int lookBack) =>
        upTo.Length > lookBack && upTo[^(lookBack + 1)].Close is var then && then > 0m
            ? Statistic.FromRatio(upTo[^1].Close / then) - 1.0
            : null;

    // A stock's beta on the night: its newest 252 closes beside the index's on the same sessions, none where the index
    // holds no close on one of them or the stock holds fewer.
    // see: A heavyweight's beta is read over 251 daily returns against the index
    static double? BetaOf(Bar[] upTo, IReadOnlyDictionary<DateOnly, double>? index)
    {
        if (index is null || upTo.Length < HeavyweightRule.BetaReturns + 1)
        {
            return null;
        }

        var pairs = new (double Stock, double Index)[HeavyweightRule.BetaReturns + 1];

        for (var at = 0; at < pairs.Length; at++)
        {
            var bar = upTo[upTo.Length - pairs.Length + at];

            if (!index.TryGetValue(bar.Session, out var market))
            {
                return null;
            }

            pairs[at] = (Statistic.FromPrice(bar.Close), market);
        }

        return HeavyweightRule.Beta(pairs);
    }

    // A holding carried to a night: its own growth by its close tonight over its close on the session it was carried
    // to, and each of its size cut's the same way, a stock with no close on either session kept where it stood, which
    // is a member that left the index sold at its last close.
    static Holding Carried(Holding holding, DateOnly night, Func<string, DateOnly, decimal?> closeOn)
    {
        (double Growth, DateOnly On) Grown(string ticker, double growth, DateOnly on) =>
            on < night && closeOn(ticker, on) is { } then && then > 0m && closeOn(ticker, night) is { } now
                ? (growth * Statistic.FromRatio(now / then), night)
                : (growth, on);

        var own = Grown(holding.Ticker, holding.Growth, holding.Through);

        return holding with
        {
            Growth = own.Growth,
            Through = own.On,
            Cut = [.. holding.Cut.Select(member => Grown(member.Ticker, member.Growth, member.On) is var grown ? member with { Growth = grown.Growth, On = grown.On } : member)],
        };
    }

    // The parameter naming a rule's own rows, none for the page's.
    static IReadOnlyList<(string Name, object Value)> Owned(Ledger ledger) =>
        ledger.Candidate is { } candidate ? [("$candidate", candidate)] : [];

    // A holding ended on a session at a close: its result its growth less one, and its size cut's return the mean of
    // its members' growths less one.
    static async Task EndAsync(SqliteConnection connection, SqliteTransaction transaction, Ledger ledger, Holding holding, DateOnly on, decimal close, string reason, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, ledger.Candidate is null ? End : EndRule,
        [
            .. Owned(ledger),
            ("$ended_on", Stamp(on)),
            ("$exit_close", Money.ToStorage(close)),
            ("$reason", reason),
            ("$result", holding.Growth - 1.0),
            ("$cut_return", holding.Cut.Count > 0 ? holding.Cut.Average(member => member.Growth) - 1.0 : (object)DBNull.Value),
            ("$ticker", holding.Ticker),
            ("$entered_on", Stamp(holding.EnteredOn)),
        ], cancellation);

    // Every member on a rebalance session as every book reads it: its company and sector as its newest fetch answered,
    // its value from that fetch's counts, the dollars it traded over the fifty sessions to the session, its close with
    // its averages, its beta against the index and its bars to the night, from which each setting reads its return.
    // see: The night values a member from its newest fetch, tonight's close brought to the count's basis by the fetch's own close
    // see: A company's sector on a session is the GICS sector the provider files, with the fourteen moves of 2023-03-17 read by date
    // see: Companies are ranked by CIK with one listing held, the class that traded the more dollars over fifty sessions
    static async Task<IReadOnlyList<Valued>> ValueAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DateOnly night,
        IReadOnlySet<string> members,
        IReadOnlyDictionary<string, SortedDictionary<DateOnly, Bar>> bars,
        IReadOnlyDictionary<(string Ticker, string Name), double> averages,
        IReadOnlyDictionary<DateOnly, double>? index,
        CancellationToken cancellation)
    {
        var companies = new Dictionary<string, (string? Cik, string? Sector)>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, Companies, [], cancellation))
        {
            companies[row.GetString(0)] = (row.IsDBNull(1) ? null : row.GetString(1), row.IsDBNull(2) ? null : row.GetString(2));
        }

        var counts = new Dictionary<string, (List<FiledCount> Counts, DateOnly Basis, decimal BasisClose)>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, Counts, [], cancellation))
        {
            var ticker = row.GetString(0);
            var basis = Day(row.GetString(4));

            if (!counts.TryGetValue(ticker, out var held))
            {
                counts[ticker] = held = ([], basis, Money.FromStorage(row.GetString(5)));
            }

            held.Counts.Add(new FiledCount(Day(row.GetString(1)), Day(row.GetString(2)), Money.FromStorage(row.GetString(3)), basis));
        }

        var read = new List<Valued>();

        foreach (var ticker in members.Order(StringComparer.Ordinal))
        {
            if (!bars.TryGetValue(ticker, out var series) || !series.TryGetValue(night, out var tonight))
            {
                continue;
            }

            var company = companies.TryGetValue(ticker, out var filed) ? filed : (null, null);
            decimal? value = counts.TryGetValue(ticker, out var held)
                ? CompanyValue.Tonight(night, tonight.Close, held.Counts, held.BasisClose, series.TryGetValue(held.Basis, out var basisBar) ? basisBar.Close : null)
                : null;
            var upTo = series.Values.Where(bar => bar.Session <= night).ToArray();

            read.Add(new Valued(
                ticker,
                CompanyRank.CompanyOf(ticker, company.Cik),
                GicsSectors.On(ticker, company.Sector, night),
                value,
                CompanyRank.DollarVolume(upTo.Select(bar => (bar.RawClose, bar.Volume))),
                Statistic.FromPrice(tonight.Close),
                averages.TryGetValue((ticker, IndicatorSeries.Sma50), out var fifty) ? fifty : null,
                averages.TryGetValue((ticker, IndicatorSeries.Sma200), out var twoHundred) ? twoHundred : null,
                BetaOf(upTo, index),
                upTo));
        }

        return read;
    }

    static async Task<IReadOnlyDictionary<string, SortedDictionary<DateOnly, Bar>>> BarsAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellation)
    {
        var bars = new Dictionary<string, SortedDictionary<DateOnly, Bar>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, Bars, [], cancellation))
        {
            if (!bars.TryGetValue(row.GetString(0), out var series))
            {
                bars[row.GetString(0)] = series = [];
            }

            var session = Day(row.GetString(1));

            series[session] = new Bar(session, Money.FromStorage(row.GetString(2)), row.IsDBNull(3) ? Money.FromStorage(row.GetString(2)) : Money.FromStorage(row.GetString(3)), row.GetInt64(4));
        }

        return bars;
    }

    // The index's and each fund's closes to the night, by series and session.
    static async Task<IReadOnlyDictionary<string, SortedDictionary<DateOnly, decimal>>> SeriesAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        var series = new Dictionary<string, SortedDictionary<DateOnly, decimal>>(StringComparer.Ordinal);

        await foreach (var row in RowsAsync(connection, transaction, SeriesCloses, [("$night", Stamp(night))], cancellation))
        {
            if (!series.TryGetValue(row.GetString(0), out var closes))
            {
                series[row.GetString(0)] = closes = [];
            }

            closes[Day(row.GetString(1))] = Money.FromStorage(row.GetString(2));
        }

        return series;
    }

    static async Task<IReadOnlyDictionary<(string Ticker, string Name), double>> AveragesAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        var averages = new Dictionary<(string, string), double>();

        await foreach (var row in RowsAsync(connection, transaction, Averages, [("$night", Stamp(night)), ("$fifty", IndicatorSeries.Sma50), ("$twoHundred", IndicatorSeries.Sma200)], cancellation))
        {
            averages[(row.GetString(0), row.GetString(1))] = row.GetDouble(2);
        }

        return averages;
    }

    static async Task<IReadOnlyList<Holding>> OpenAsync(SqliteConnection connection, SqliteTransaction transaction, Ledger ledger, CancellationToken cancellation)
    {
        var holdings = new List<Holding>();

        await foreach (var row in RowsAsync(connection, transaction, ledger.Candidate is null ? Open : OpenRule, Owned(ledger), cancellation))
        {
            holdings.Add(new Holding(
                row.GetString(0),
                Day(row.GetString(1)),
                row.GetString(2),
                row.GetString(3),
                Money.FromStorage(row.GetString(4)),
                row.GetDouble(5),
                CutOf(row.GetString(6)),
                Day(row.GetString(7))));
        }

        return holdings;
    }

    // A size cut as the book stores it: each member's ticker, growth and the session it was carried to.
    public static string Cut(IReadOnlyList<CutMember> cut) =>
        JsonSerializer.Serialize(cut.Select(member => new { ticker = member.Ticker, growth = member.Growth, on = Stamp(member.On) }));

    public static IReadOnlyList<CutMember> CutOf(string stored)
    {
        using var document = JsonDocument.Parse(stored);

        return
        [
            .. document.RootElement.EnumerateArray().Select(member => new CutMember(
                member.GetProperty("ticker").GetString()!,
                member.GetProperty("growth").GetDouble(),
                Day(member.GetProperty("on").GetString()!))),
        ];
    }

    async Task<HeavyweightBookOutcome> RecordAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, HeavyweightBookOutcome outcome, CancellationToken cancellation)
    {
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        await AppendAsync(connection, transaction, runId, startedAt, outcome, 0, "ok", cancellation);
        await transaction.CommitAsync(cancellation);

        return outcome;
    }

    async Task AppendAsync(SqliteConnection connection, SqliteTransaction transaction, string runId, DateTimeOffset startedAt, HeavyweightBookOutcome outcome, int rows, string state, CancellationToken cancellation) =>
        await ExecuteAsync(connection, transaction, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Stage),
            ("$started_at", startedAt.ToString("O", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("O", CultureInfo.InvariantCulture)),
            ("$outcome", state),
            ("$rows_written", rows),
            ("$detail", Detail(outcome)),
        ], cancellation);

    // The book's line for the night, as its run log row holds it: the page's book, then each registered rule's.
    public static string Detail(HeavyweightBookOutcome outcome) =>
        outcome.ReadNothing is { } why
            ? why
            : Line(outcome.Rebalanced, outcome.Valued, outcome.Entered, outcome.Ended, outcome.Held, outcome.Waits)
              + (outcome.Rules is { Count: > 0 } rules
                  ? "; the registered rules: " + string.Join("; ", rules.Select(rule => rule.Fault is { } fault
                      ? $"'{rule.Candidate}' FAILURE: kept by no book, {fault}"
                      : $"'{rule.Candidate}' " + Line(rule.Rebalanced, null, rule.Entered, rule.Ended, rule.Held, rule.Waits)))
                  : string.Empty);

    static string Line(bool rebalanced, int? valued, IReadOnlyList<string> entered, IReadOnlyList<string> ended, int held, string? waits) =>
        (waits is { } waiting
            ? waiting
            : rebalanced
                ? (valued is { } count ? FormattableString.Invariant($"a rebalance, {count} member(s) valued, {entered.Count} bought") : FormattableString.Invariant($"a rebalance, {entered.Count} bought"))
                : "no rebalance")
        + FormattableString.Invariant($", {ended.Count} ended, {held} held")
        + (entered.Count > 0 ? "; bought: " + string.Join(", ", entered) : string.Empty)
        + (ended.Count > 0 ? "; ended: " + string.Join(", ", ended) : string.Empty);

    static async Task<int> ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var value1 = await command.ExecuteScalarAsync(cancellation);

        return value1 is DBNull ? null : value1;
    }

    static async Task<IReadOnlyList<string>> ColumnAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        var values = new List<string>();

        await foreach (var row in RowsAsync(connection, transaction, sql, parameters, cancellation))
        {
            values.Add(row.GetString(0));
        }

        return values;
    }

    static async IAsyncEnumerable<SqliteDataReader> RowsAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string sql,
        IReadOnlyList<(string Name, object Value)> parameters,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellation)
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

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
