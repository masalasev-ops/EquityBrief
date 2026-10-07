using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations: a sector heavyweights rebalance waits for a night the store's year holds the closes its
// readings need, and one reading no lead, or no beta where it reads one, waits the same way, reading, selling and
// buying nothing and storing no row of its month; a month whose rows read no lead is read again; the S&P 400's books
// wait the same way; and the store's year holds the sessions the exchange's calendar gives it, worked by hand from the
// closure table.
// see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
public partial class FixtureExpectations
{
    // The row the wait adds that this check reaches: section 18's row for a rebalance whose store's year or readings
    // fall short.
    internal static readonly string[] HeavyweightWaitClaims =
    [
        CheckReach.Key(Scope.FailureTable, "The store's year holds fewer closes than a heavyweights' rebalance reads, or the rebalance reads no lead or no beta"),
    ];

    // Every session the constructed stores are drawn from, a calendar year and more before the nights they are read on.
    static readonly IReadOnlyList<DateOnly> WaitSessions = ExchangeClosures.SessionsBetween(new DateOnly(2025, 8, 31), new DateOnly(2026, 11, 4));

    // A setting reading a twelve-month return and no beta or fund, which needs the 251 sessions of its look-back and
    // the close before them.
    static readonly HeavyweightSettings YearLong = HeavyweightRule.Provisional with { LookBack = HeavyweightRule.LookBack };

    // The three members of one sector the stores hold: W1 rising a tenth a session and W2 and W3 flat at 100, so over any
    // look-back W1 leads the sector's mean and the other two trail it.
    static readonly (string Ticker, int Shares)[] WaitMembers = [("W1", 1000), ("W2", 900), ("W3", 800)];

    static readonly DateOnly WaitBasis = new(2026, 9, 30);

    static decimal WaitClose(string ticker, DateOnly session) =>
        ticker == "W1" ? 100m + (0.1m * WaitAt(session)) : 100m;

    static int WaitAt(DateOnly session)
    {
        var at = WaitSessions.ToList().IndexOf(session);

        Assert.True(at >= 0, $"{session:yyyy-MM-dd} is no session the stores are drawn from.");

        return at;
    }

    // A store holding the members, their companies and their counts, filed before every night read on the basis of the
    // store's own close, and their bars over the sessions given.
    static TemporaryStore WaitStore(DateOnly from, DateOnly through)
    {
        var store = new TemporaryStore().Migrated();

        foreach (var (ticker, shares) in WaitMembers)
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2025-01-02T00:00:00Z');");
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES ('{ticker}', '2026-09-30T23:50:00Z', 'CIK{ticker}', '{TechSector}', NULL, NULL, NULL);");
            store.Execute(
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, basis_session, basis_close, shares) VALUES " +
                FormattableString.Invariant($"('{ticker}', '2026-09-30T23:50:00Z', '2026-09-30', '2026-06-30', '2026-08-01', '{Day(WaitBasis)}', '{WaitClose(ticker, WaitBasis)}', '{shares}');"));
        }

        WaitBars(store, from, through);

        return store;
    }

    static void WaitBars(TemporaryStore store, DateOnly from, DateOnly through)
    {
        foreach (var (ticker, _) in WaitMembers)
        {
            StoreYear(store, ticker, [.. WaitSessions.Where(session => session >= from && session <= through).Select(session => new FamilyBar(session, WaitClose(ticker, session), WaitClose(ticker, session), WaitClose(ticker, session), 1000))]);
        }
    }

    // Each member's averages on a night: W1's close above its 50-day and that above its 200-day, the others' close under
    // their 50-day and at their 200-day, passing no trend gate and broken by none.
    static void WaitAverages(TemporaryStore store, DateOnly night)
    {
        foreach (var (ticker, _) in WaitMembers)
        {
            var close = WaitClose(ticker, night);
            var (fifty, twoHundred) = ticker == "W1" ? (close - 1m, close - 2m) : (101m, 100m);

            store.Execute(FormattableString.Invariant(
                $"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('{ticker}', '{Day(night)}', 'sma50', {fifty}, 200), ('{ticker}', '{Day(night)}', 'sma200', {twoHundred}, 200);"));
        }
    }

    // The bulk file of a session: every member's bar on it.
    sealed class WaitFeed : IBulkPriceFeed
    {
        public int Requests { get; private set; }

        public IReadOnlyList<string> NotSessions => [];

        public Task<IReadOnlyList<BulkBar>> RowsAsync(string exchange, DateOnly session, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<BulkBar>>(
            [
                .. WaitMembers.Select(member => new BulkBar(
                    member.Ticker,
                    new ProviderBar(session, WaitClose(member.Ticker, session), WaitClose(member.Ticker, session), WaitClose(member.Ticker, session), WaitClose(member.Ticker, session), WaitClose(member.Ticker, session), 1000))),
            ]);
        }
    }

    static DateTimeOffset At(DateOnly session, int hour, int minute) => new(session.ToDateTime(new TimeOnly(hour, minute)), TimeSpan.Zero);

    // A night as the night runs it: the fetch storing the session's bars and cutting the store to its year, then the book.
    static async Task<(FetchOutcome Fetched, HeavyweightBookOutcome Book)> WaitNight(TemporaryStore store, DateOnly night, string runId)
    {
        var fetched = await new BarFetcher(new WaitFeed(), FixedClock.At(At(night, 21, 10), SessionZones.UnitedStates), store.DatabaseFile).RunAsync("GSPC", runId);

        WaitAverages(store, night);

        var book = await new HeavyweightBook(FixedClock.At(At(night, 23, 40), SessionZones.UnitedStates), store.DatabaseFile, YearLong).RunAsync("GSPC", runId);

        return (fetched, book);
    }

    [Fact]
    public async Task ARebalanceWaitsOnANightTheStoresOwnYearHoldsOneCloseShortAndReadsTheNextSession()
    {
        using var store = WaitStore(new DateOnly(2025, 9, 2), new DateOnly(2026, 9, 30));

        // The book's first night, a Thursday whose date a year back was a session: the fetch keeps the 252 sessions from
        // that date and the book buys W1, the sector's leader.
        var (cut, first) = await WaitNight(store, new DateOnly(2026, 10, 1), "night-first");

        Assert.Equal(new DateOnly(2025, 10, 1), cut.Oldest);
        Assert.Equal(252, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));
        Assert.True(first.Rebalanced);
        Assert.Equal(["W1"], first.Entered);

        // A month's first session on a Monday, its date a year back a Sunday: the fetch keeps 251 sessions, one short of
        // the 252 the twelve-month return reads. The rebalance waits: nothing read, sold or bought, no row of the month
        // stored, W1 carried and held, and the row naming the sessions held and needed.
        WaitBars(store, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 30));

        var monday = new DateOnly(2026, 11, 2);
        var (shortCut, waited) = await WaitNight(store, monday, "night-waits");
        const string Waits = "the rebalance waits for a night the store holds the 252 sessions its readings need, since it holds 251 to 2026-11-02";

        Assert.Equal(DayOfWeek.Sunday, shortCut.Oldest.DayOfWeek);
        Assert.Equal(251, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));
        Assert.Equal((false, Waits), (waited.Rebalanced, waited.Waits));
        Assert.Empty(waited.Entered);
        Assert.Empty(waited.Ended);
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM heavyweight_night WHERE session_date = '2026-11-02';"));
        Assert.Equal(["W1|2026-10-01|2026-11-02|open"], TextRows(store, "SELECT ticker || '|' || entered_on || '|' || through || '|' || IFNULL(ended_on, 'open') FROM heavyweight_holding;"));
        Assert.Equal(Waits + ", 0 ended, 1 held", Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-waits' AND stage = 'heavyweights';"));

        // The session after holds 252, and the month's rebalance is read on it: every member ranked, W1 still the leader
        // and still held.
        var (_, read) = await WaitNight(store, new DateOnly(2026, 11, 3), "night-reads");

        Assert.Equal(252, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));
        Assert.Equal((true, 0, 0, 1), (read.Rebalanced, read.Entered.Count, read.Ended.Count, read.Held));
        Assert.Equal(["1|W1|1", "2|W2|0", "3|W3|0"], TextRows(store, "SELECT place || '|' || ticker || '|' || leader FROM heavyweight_night WHERE session_date = '2026-11-03' ORDER BY place;"));
    }

    [Fact]
    public void ASettingNeedsItsLookBackAndTheCloseBeforeItAndTheBetasWhereItReadsOne()
    {
        var night = new DateOnly(2026, 11, 2);

        // Six months of 126 sessions with no beta needs 127 closes and waits on none of 251; the same setting reading a
        // beta over 251 daily returns needs 252 and waits on 251. At its edge the setting with no beta waits on 126.
        Assert.Equal(127, HeavyweightRule.SessionsNeeded(HeavyweightRule.Provisional));
        Assert.Null(HeavyweightRule.WaitsForSessions(HeavyweightRule.Provisional, 251, night));
        Assert.Equal(
            "the rebalance waits for a night the store holds the 127 sessions its readings need, since it holds 126 to 2026-11-02",
            HeavyweightRule.WaitsForSessions(HeavyweightRule.Provisional, 126, night));
        Assert.Equal(252, HeavyweightRule.SessionsNeeded(HeavyweightRule.Provisional with { HighBeta = true }));
        Assert.Equal(
            "the rebalance waits for a night the store holds the 252 sessions its readings need, since it holds 251 to 2026-11-02",
            HeavyweightRule.WaitsForSessions(HeavyweightRule.Provisional with { HighBeta = true }, 251, night));
        Assert.Null(HeavyweightRule.WaitsForSessions(HeavyweightRule.Provisional with { HighBeta = true }, 252, night));

        // The live setting and every rule its freeze registered need 252.
        Assert.Equal(252, HeavyweightRule.SessionsNeeded(HeavyweightRule.Live));
        Assert.All(TheSetupFamilies.Heavyweights, rule => Assert.Equal(252, HeavyweightRule.SessionsNeeded(new SectorHeavyweightCandidate().SettingsOf(rule.Parameters))));
    }

    [Fact]
    public async Task ARebalanceReadingNoBetaWaitsAndSellsNothingWhereTheIndexMissesOneCloseInsideTheBetasWindow()
    {
        // The 252 sessions to a month's rebalance, the index's closes on each but one inside the beta's window, W1 bought
        // at the month before's rebalance and held.
        var night = new DateOnly(2026, 11, 3);

        using var store = WaitStore(new DateOnly(2025, 11, 3), night);

        var held = WaitSessions.Where(session => session >= new DateOnly(2025, 11, 3) && session <= night).ToArray();
        var missing = new DateOnly(2026, 6, 1);

        Assert.Equal(252, held.Length);
        Assert.Contains(missing, held);
        store.Execute(
            "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
            string.Join(", ", held.Where(session => session != missing).Select(session => WaitAt(session) % 2 == 0 ? (session, 5010m) : (session, 4990m)).Select(one => FormattableString.Invariant($"('GSPC', '{Day(one.session)}', '{one.Item2}', '{one.Item2}', '{one.Item2}', '{one.Item2}', 'test')"))) + ";");
        store.Execute("INSERT INTO heavyweight_night (session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader) VALUES ('2026-10-01', 'Information Technology', 1, 'W1', 'CIK CIKW1', '100000', 0.2, 0.1, 0.1, 1, 1);");
        store.Execute($"INSERT INTO heavyweight_holding (ticker, entered_on, sector, company, entry_close, growth, cut, through) VALUES ('W1', '2026-10-01', '{TechSector}', 'CIK CIKW1', '{WaitClose("W1", new DateOnly(2026, 10, 1)).ToString(CultureInfo.InvariantCulture)}', 1.0, '[]', '2026-10-30');");
        WaitAverages(store, night);

        var reading = HeavyweightRule.Provisional with { HighBeta = true };
        var book = new HeavyweightBook(FixedClock.At(At(night, 23, 40), SessionZones.UnitedStates), store.DatabaseFile, reading);

        // Every lead is read and every beta is none: the rebalance waits and says why, sells nothing and stores no row.
        var waited = await book.RunAsync("GSPC", "night-no-beta");

        Assert.Equal((false, "the rebalance waits, since no sector's largest companies read a beta on 2026-11-03"), (waited.Rebalanced, waited.Waits));
        Assert.Empty(waited.Ended);
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM heavyweight_night WHERE session_date = '2026-11-03';"));
        Assert.Equal(["W1|open"], TextRows(store, "SELECT ticker || '|' || IFNULL(ended_on, 'open') FROM heavyweight_holding;"));

        // With the missing close stored, the same night reads every beta and rebalances.
        store.Execute($"INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES ('GSPC', '{Day(missing)}', '5000', '5000', '5000', '5000', 'test');");

        var read = await book.RunAsync("GSPC", "night-every-beta");

        Assert.True(read.Rebalanced);
        Assert.Equal(3, Scalar(store, "SELECT COUNT(*) FROM heavyweight_night WHERE session_date = '2026-11-03' AND beta IS NOT NULL;"));
    }

    [Fact]
    public async Task ARegisteredRulesMonthWhoseRowsReadNoLeadIsReadAgainOnTheNextNightThatCan()
    {
        // A rule's book stored a month's rebalance on a night its store held one session short, every return, beta and
        // lead none and no leader; the store now holds the 252 sessions to the next night of that month.
        var night = new DateOnly(2026, 10, 6);

        using var store = WaitStore(new DateOnly(2025, 10, 6), night);

        var evaluator = CandidateEvaluators.Find(SectorHeavyweightCandidate.EvaluatorName)!;
        var rule = new RegisterRow(1, "the rule at twelve months", "rule", "test", evaluator.Name, CandidateEvaluator.Write(SectorHeavyweightCandidate.ParametersOf(YearLong)), evaluator.Version, "registered", null, At(new DateOnly(2026, 10, 1), 12, 0), "test");

        store.Execute(
            "INSERT INTO heavyweight_rule_night (candidate, session_date, sector, place, ticker, company, company_value, look_back, sector_return, lead, trend, leader, beta) VALUES " +
            string.Join(", ", WaitMembers.Select((member, at) => $"('{rule.Candidate}', '2026-10-05', '{TechSector}', {at + 1}, '{member.Ticker}', 'CIK CIK{member.Ticker}', '1000', NULL, NULL, NULL, 0, 0, NULL)")) + ";");
        WaitAverages(store, night);

        // The month counts as read only where a row of it read a lead, so the book reads it on this night and buys W1,
        // the rows that read nothing kept as they were written.
        var outcome = await new HeavyweightBook(FixedClock.At(At(night, 23, 40), SessionZones.UnitedStates), store.DatabaseFile, YearLong).RunAsync("GSPC", "night-again", rules: [rule]);
        var kept = Assert.Single(outcome.Rules!);

        Assert.True(kept.Rebalanced);
        Assert.Equal(["W1"], kept.Entered);
        Assert.Equal(3, Scalar(store, "SELECT COUNT(*) FROM heavyweight_rule_night WHERE session_date = '2026-10-05' AND lead IS NULL;"));
        Assert.Equal(3, Scalar(store, "SELECT COUNT(*) FROM heavyweight_rule_night WHERE session_date = '2026-10-06' AND lead IS NOT NULL;"));
    }

    [Fact]
    public async Task TheSAndP400sBookAndARegisteredRuleBookWaitOnANightTheirMembersHoldOneSessionShort()
    {
        var (store, closes) = HeavyweightIndexStore();

        using (store)
        {
            var freezeAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
            var heavyweights = HeavyweightIndexEvaluator("heavyweight-400");

            Assert.Equal(0, (await RegisterVerbAt(store, freezeAt, RegisterVerb.IndexFamily, "heavyweight", "--index", "MID", "--parameters", Typed(IndexRules.Provisional(heavyweights)))).Code);

            var register = await new CandidateRegistrar(FixedClock.At(freezeAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

            async Task<IndexNightOutcome> Night(DateOnly session, string run) =>
                (await new IndexFamilies(FixedClock.At(At(session, 23, 40), SessionZones.UnitedStates), store.DatabaseFile).RunAsync(run, default, register, freezeAt.AddHours(1)))
                    .Nights.Single(night => night.Index == "MID");

            // The books' first night, 261 sessions held: the index's book and the registered rule's each buy L1 and L2.
            var first = await Night(new DateOnly(2026, 10, 1), "first");

            Assert.Equal(new IndexHeavyweightsOutcome(true, 2, 0, 2), first.Heavyweights);
            Assert.Equal((true, 2), (first.HeavyweightRules.Rebalanced, first.HeavyweightRules.Held));

            // A calendar session a day to the next month's first night, each member and the fund flat at its close of the
            // session after the first night, and the members' bars older than 250 days before that night gone, so they
            // hold 251 sessions to it.
            var monthFirst = new DateOnly(2026, 11, 2);

            foreach (var ticker in closes.Keys)
            {
                StoreYear(store, ticker, [.. Enumerable.Range(1, 32).Select(day => new DateOnly(2026, 10, 1).AddDays(day)).Select(day => new FamilyBar(day, closes[ticker][261], closes[ticker][261], closes[ticker][261], 1_000_000))]);
            }

            store.Execute(
                "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES " +
                string.Join(", ", Enumerable.Range(2, 32).Select(day => $"('IJH', '{Day(new DateOnly(2026, 10, 1).AddDays(day))}', '100', '100', '100', '100', 'test')")) + ";");
            store.Execute($"DELETE FROM bar WHERE session_date < '{Day(monthFirst.AddDays(-250))}';");

            Assert.Equal(251, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));

            // Both books wait, naming the sessions held and needed: nothing bought or sold, and no rebalance stored.
            const string Waits = "the rebalance waits for a night the store holds the 252 sessions its readings need, since it holds 251 to 2026-11-02";
            var waited = await Night(monthFirst, "waits");

            Assert.Equal(new IndexHeavyweightsOutcome(false, 0, 0, 2, Waits), waited.Heavyweights);
            Assert.Equal((false, 0, 0, 2), (waited.HeavyweightRules.Rebalanced, waited.HeavyweightRules.Entered, waited.HeavyweightRules.Ended, waited.HeavyweightRules.Held));
            Assert.EndsWith(Waits, waited.HeavyweightRules.Waits, StringComparison.Ordinal);
            Assert.Equal(["L1|open", "L2|open"], TextRows(store, "SELECT ticker || '|' || IFNULL(ended_on, 'open') FROM index_heavyweight_holding ORDER BY ticker;"));
            Assert.Equal(["L1|open", "L2|open"], TextRows(store, "SELECT ticker || '|' || IFNULL(ended_on, 'open') FROM index_heavyweight_rule_holding ORDER BY ticker;"));
            Assert.Equal(["2026-11-02|0"], FamilyRows(store, "SELECT session_date, rebalanced FROM index_family_night WHERE index_code = 'MID' AND session_date = '2026-11-02';"));
            Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM index_heavyweight_rule_night WHERE session_date = '2026-11-02';"));

            // The night after holds 252 and both books read the month's rebalance.
            foreach (var ticker in closes.Keys)
            {
                StoreYear(store, ticker, [new FamilyBar(monthFirst.AddDays(1), closes[ticker][261], closes[ticker][261], closes[ticker][261], 1_000_000)]);
            }

            var read = await Night(monthFirst.AddDays(1), "reads");

            Assert.True(read.Heavyweights.Rebalanced);
            Assert.True(read.HeavyweightRules.Rebalanced);
        }
    }

    [Fact]
    public void TheStoresYearHoldsTheSessionsTheClosureTableGivesItAndTheLiveBookReadsEachMonthOnTheFirstHoldingItsNeed()
    {
        // Worked by hand from the closure table and a one-year cut, a session at a time: each month's first session, the
        // sessions the year to it holds, and the session the live setting's 252 closes are first held on. Eight of the
        // fifteen months wait, seven of them a session and the April of the second year three.
        (DateOnly First, int Held, DateOnly Reads)[] months =
        [
            (new(2026, 10, 1), 252, new(2026, 10, 1)),
            (new(2026, 11, 2), 251, new(2026, 11, 3)),
            (new(2026, 12, 1), 252, new(2026, 12, 1)),
            (new(2027, 1, 4), 251, new(2027, 1, 5)),
            (new(2027, 2, 1), 251, new(2027, 2, 2)),
            (new(2027, 3, 1), 251, new(2027, 3, 2)),
            (new(2027, 4, 1), 251, new(2027, 4, 6)),
            (new(2027, 5, 3), 251, new(2027, 5, 4)),
            (new(2027, 6, 1), 252, new(2027, 6, 1)),
            (new(2027, 7, 1), 252, new(2027, 7, 1)),
            (new(2027, 8, 2), 251, new(2027, 8, 3)),
            (new(2027, 9, 1), 252, new(2027, 9, 1)),
            (new(2027, 10, 1), 252, new(2027, 10, 1)),
            (new(2027, 11, 1), 251, new(2027, 11, 2)),
            (new(2027, 12, 1), 252, new(2027, 12, 1)),
        ];

        foreach (var (first, held, reads) in months)
        {
            Assert.Equal(held, BarRetention.SessionsTo(first));

            // The night before the month's first session, with the month before read.
            var before = ExchangeClosures.SessionsBetween(first.AddDays(-10), first)[^1];

            Assert.Equal(reads, HeavyweightRule.NextRebalance(before, new DateOnly(before.Year, before.Month, 1), HeavyweightRule.Live));
        }

        Assert.Equal(8, months.Count(month => month.Reads != month.First));
        Assert.Equal(3, ExchangeClosures.SessionsBetween(new DateOnly(2027, 3, 31), new DateOnly(2027, 4, 6)).Count);

        // The year to a session falls to 250 where its date a year back and the day after were no sessions, and three
        // sessions in a row across a weekend hold 252, 251 and 252.
        Assert.Equal(250, BarRetention.SessionsTo(new DateOnly(2027, 3, 29)));
        Assert.Equal([252, 251, 252], new[] { new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6) }.Select(day => BarRetention.SessionsTo(day)!.Value));

        // The year is the bar fetch's own cut.
        Assert.Equal(BarRetention.Years, BarFetcher.RetentionYears);
    }
}
