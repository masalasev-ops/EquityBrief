using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Filter;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 14.6: the freezes and registrations. The sector heavyweights' four registered at one instant or
// none; the page's book and each registered rule's own over two constructed nights, each variant worked by hand on both
// sides of the setting it moves, a sector's return read from its fund and each leader's beta read against the index's
// closes the night stored; a rebalance waiting for a fund's close; a heavyweights rule registered again named as not
// replayed; and the analysts' estimates the night asks for, read off an answer's earnings trend, stored, read back and
// asked for no member the night does not need.
// see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants
// see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
// see: The night asks for the market series' daily closes once a series, and keeps them apart from the members' bars
// see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night
// see: A member's estimates are raised where its current fiscal year's consensus earnings estimate stands above its level 30 days before
public partial class FixtureExpectations
{
    // The rows the freeze adds that this check reaches: section 17's two and section 18's two.
    internal static readonly string[] HeavyweightFreezeClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Heavyweights' registrations"),
        CheckReach.Key(Scope.LimitsTable, "Estimates raised"),
        CheckReach.Key(Scope.FailureTable, "A sector fund or the index holds no close for a heavyweights' rebalance night"),
        CheckReach.Key(Scope.FailureTable, "The provider does not serve a member's estimates on the night"),
    ];

    // The night the constructed books first read, October's first session, and the session after it.
    static readonly DateOnly FreezeNight = new(2026, 10, 1);
    static readonly DateOnly FreezeNext = new(2026, 10, 2);

    // The 252 sessions ending on the night, the first of them 251 before it, and the next.
    static readonly DateOnly[] FreezeSessions =
        [.. ExchangeClosures.SessionsBetween(FreezeNight.AddDays(-400), FreezeNight.AddDays(1)).TakeLast(HeavyweightRule.BetaReturns + 1), FreezeNext];

    // Eleven Information Technology companies, largest first by value: each one's return over the 251 sessions to the
    // night, the multiple of the index's two moves its own two follow, and its share count. C01 leads its fund by 8
    // points with a beta near 1.5; C02 leads by 3 with one near 0.5; C11, the eleventh by value, leads by 28.
    static readonly (string Ticker, double Return, double Follows, int Shares)[] FreezeMembers =
    [
        ("C01", 0.30, 1.5, 9_500),
        ("C02", 0.25, 0.5, 9_000),
        ("C03", 0.20, 1.5, 8_500),
        ("C04", 0.18, 1.5, 8_000),
        ("C05", 0.05, 1.5, 7_500),
        ("C06", 0.05, 1.5, 7_000),
        ("C07", 0.05, 1.5, 6_500),
        ("C08", 0.05, 1.5, 6_000),
        ("C09", 0.05, 1.5, 5_500),
        ("C10", 0.05, 1.5, 5_000),
        ("C11", 0.50, 1.5, 1_000),
    ];

    // The fund holding Information Technology returns 22% over the same sessions, every other fund nothing.
    const decimal FreezeFundReturn = 0.22m;

    // The session the index rises a tenth and the one it falls back on, and the session each member's return lands.
    const int IndexRises = 100;
    const int ReturnLands = 247;

    // Each member's close at a place in the sessions: 100, its index move times its multiple on the session the index
    // rises and back on the next, and its return from the session it lands; C01 at the close it is handed on the
    // session after the night.
    static decimal FreezeClose((string Ticker, double Return, double Follows, int Shares) member, int at, decimal c01Next) =>
        at == FreezeSessions.Length - 1 && member.Ticker == "C01"
            ? c01Next
            : at == IndexRises
                ? 100m * (1m + (0.1m * (decimal)member.Follows))
                : at >= ReturnLands
                    ? 100m * (1m + (decimal)member.Return)
                    : 100m;

    static decimal IndexClose(int at) => at == IndexRises ? 1100m : 1000m;

    // The store the freeze's books read: the eleven members with their bars to the night, companies and counts, their
    // averages on the night and the next, the index's closes and every fund's to the next, one fund's closes held back
    // from the night where asked. The next session's bars are added once the night has been read.
    static TemporaryStore FreezeStore(string? fundMissingOnTheNight = null)
    {
        var store = new TemporaryStore().Migrated();

        using var connection = store.Open();
        using var transaction = connection.BeginTransaction();

        void Run(string sql, params (string Name, object Value)[] parameters)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = sql;

            foreach (var (name, value) in parameters)
            {
                command.Parameters.AddWithValue(name, value);
            }

            command.ExecuteNonQuery();
        }

        static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        foreach (var member in FreezeMembers)
        {
            Run($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{member.Ticker}', NULL, NULL, '2025-01-01T00:00:00Z');");
            Run($"INSERT INTO company (ticker, fetched_at, cik, sector, industry_group, industry, sub_industry) VALUES ('{member.Ticker}', '2026-09-01T23:50:00Z', '{member.Ticker}0', '{GicsSectors.InformationTechnology}', NULL, NULL, NULL);");
            Run(
                "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, basis_session, basis_close, shares) VALUES " +
                $"('{member.Ticker}', '2026-09-01T23:50:00Z', '2026-09-01', '2026-06-30', '2026-08-01', '{Day(FreezeSessions[0])}', '100', '{member.Shares}');");

            for (var at = 0; at < FreezeSessions.Length - 1; at++)
            {
                Run(
                    "INSERT INTO bar (ticker, session_date, open, high, low, close, raw_close, volume, source, observed_at) VALUES ($ticker, $session, $close, $close, $close, $close, $close, 1000, 'constructed', '2025-01-01T00:00:00Z');",
                    ("$ticker", member.Ticker), ("$session", Day(FreezeSessions[at])), ("$close", FreezeClose(member, at, 0m).ToString(CultureInfo.InvariantCulture)));
            }

            foreach (var night in new[] { FreezeNight, FreezeNext })
            {
                Run(
                    "INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ($ticker, $session, $fifty, 90.0, 200), ($ticker, $session, $twoHundred, 80.0, 200);",
                    ("$ticker", member.Ticker), ("$session", Day(night)), ("$fifty", Core.Indicators.IndicatorSeries.Sma50), ("$twoHundred", Core.Indicators.IndicatorSeries.Sma200));
            }
        }

        for (var at = 0; at < FreezeSessions.Length; at++)
        {
            Run(
                "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES ($series, $session, $close, $close, $close, $close, 'constructed');",
                ("$series", MarketCloses.Index), ("$session", Day(FreezeSessions[at])), ("$close", IndexClose(at).ToString(CultureInfo.InvariantCulture)));

            foreach (var fund in GicsSectors.Funds.Values.Where(fund => !(fund == fundMissingOnTheNight && FreezeSessions[at] == FreezeNight)))
            {
                var close = fund == GicsSectors.Funds[GicsSectors.InformationTechnology] && at >= ReturnLands ? 100m * (1m + FreezeFundReturn) : 100m;

                Run(
                    "INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES ($series, $session, $close, $close, $close, $close, 'constructed');",
                    ("$series", fund), ("$session", Day(FreezeSessions[at])), ("$close", close.ToString(CultureInfo.InvariantCulture)));
            }
        }

        transaction.Commit();

        return store;
    }

    static IClock FreezeClock(DateOnly night) => FixedClock.At(new DateTimeOffset(night.ToDateTime(new TimeOnly(23, 40)), TimeSpan.Zero), SessionZones.UnitedStates);

    // The next session's bars, C01 closing at the close it is handed and every other member where it stood.
    static void FreezeNextBars(TemporaryStore store, decimal c01Next)
    {
        foreach (var member in FreezeMembers)
        {
            var close = FreezeClose(member, FreezeSessions.Length - 1, c01Next).ToString(CultureInfo.InvariantCulture);

            store.Execute(
                "INSERT INTO bar (ticker, session_date, open, high, low, close, raw_close, volume, source, observed_at) VALUES " +
                $"('{member.Ticker}', '{FreezeNext.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}', '{close}', '{close}', '{close}', '{close}', '{close}', 1000, 'constructed', '2025-01-01T00:00:00Z');");
        }
    }

    static readonly DateTimeOffset FreezeRegisteredAt = new(2026, 9, 30, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TheHeavyweightsFreezeWritesItsFourRulesAtOneInstantOrNone()
    {
        using var store = new TemporaryStore().Migrated();

        // The proposal and its three passing neighbours, the live rule first, each the live settings with one dial moved.
        var live = TheSetupFamilies.Heavyweights[0];

        Assert.Equal(4, TheSetupFamilies.Heavyweights.Count);
        Assert.Equal(
            [
                "the live sector heavyweights rule at the 10 largest, 251 sessions against the sector's fund, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
                "the sector heavyweights rule at the 10 largest, 251 sessions against the sector's fund, 2 leaders, a beta of at least 1, monthly, sold on no longer leading or a close under the 200-day average",
                "the sector heavyweights rule at the 10 largest, 251 sessions against the sector's members, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
                "the sector heavyweights rule at every company, 251 sessions against the sector's fund, 2 leaders, a beta of at least 1, monthly, sold on no longer leading",
            ],
            TheSetupFamilies.Heavyweights.Select(one => one.Candidate));
        Assert.Equal(HeavyweightRule.Live, new SectorHeavyweightCandidate().SettingsOf(live.Parameters));
        Assert.Equal(
            "{\"fundReturn\": 1, \"highBeta\": 1, \"largest\": 10, \"leaders\": 2, \"lookBack\": 251, \"soldOnLeading\": 1, \"soldUnderAverage\": 0, \"weekly\": 0}",
            CandidateEvaluator.Write(live.Parameters));
        Assert.Equal(0.0, TheSetupFamilies.Heavyweights[3].Parameters[SectorHeavyweightCandidate.LargestParameter]);
        Assert.Equal(HeavyweightRule.EveryCompany, new SectorHeavyweightCandidate().SettingsOf(TheSetupFamilies.Heavyweights[3].Parameters).Largest);
        Assert.Throws<InvalidOperationException>(() => new SectorHeavyweightCandidate().SettingsOf(SectorHeavyweightCandidate.ParametersOf(HeavyweightRule.Live with { SoldOnLeading = false, SoldUnderAverage = false })));

        // The four at one instant, the family of four counted alone, on the evaluator the code carries at its version.
        var (code, said) = await RegisterVerbAt(store, FreezeRegisteredAt, RegisterVerb.Family, HeavyweightRule.Name);

        Assert.Equal(0, code);
        Assert.Contains("registered 4 at one instant, family of 4 of 9", said, StringComparison.Ordinal);
        Assert.Equal(
            [.. TheSetupFamilies.Heavyweights.Select(one => $"{one.Candidate}|{SectorHeavyweightCandidate.EvaluatorName}|{new SectorHeavyweightCandidate().Version}|2026-09-30T22:00:00Z")],
            TextRows(store, "SELECT candidate || '|' || evaluator || '|' || evaluator_version || '|' || registered_at FROM candidate_register ORDER BY id;"));
        Assert.Equal([TheSetupFamilies.HeavyweightTest], TextRows(store, "SELECT DISTINCT test FROM candidate_register;"));
        Assert.Equal(HeavyweightRule.Name, CandidateFamily.SetupFamilyOf(SectorHeavyweightCandidate.EvaluatorName));

        // The rule each stores names the session a rebalance is read on as the book reads it.
        Assert.StartsWith(
            "the sector heavyweights' rule at every setting stated: on the first session of each month, or of each week where stated, or the first after it whose stored year holds the closes the setting's readings need, each sector's stated count of largest companies",
            Assert.Single(TextRows(store, "SELECT DISTINCT rule FROM candidate_register;")),
            StringComparison.Ordinal);

        // A second freeze is refused whole, each rule standing already, and writes nothing.
        var (again, refused) = await RegisterVerbAt(store, FreezeRegisteredAt.AddHours(1), RegisterVerb.Family, HeavyweightRule.Name);

        Assert.Equal(1, again);
        Assert.Contains("was refused, so none of the 4 was registered", refused, StringComparison.Ordinal);
        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM candidate_register;"));

        // On a store where one of the four stands already, the freeze is refused whole and writes none of the other three.
        using var one = new TemporaryStore().Migrated();

        var every = TheSetupFamilies.Heavyweights[3];

        await new CandidateRegistrar(FixedClock.At(FreezeRegisteredAt, SessionZones.UnitedStates), one.DatabaseFile)
            .RegisterAsync(every.Candidate, every.Rule, every.Test, every.Evaluator, every.Parameters, "register-every");

        var (partly, why) = await RegisterVerbAt(one, FreezeRegisteredAt.AddHours(1), RegisterVerb.Family, HeavyweightRule.Name);

        Assert.Equal(1, partly);
        Assert.Contains($"'{every.Candidate}' was refused, so none of the 4 was registered", why, StringComparison.Ordinal);
        Assert.Equal(1, Scalar(one, "SELECT COUNT(*) FROM candidate_register;"));
    }

    [Fact]
    public async Task EachHeavyweightsVariantIsWorkedByHandOnBothSidesOfTheSettingItMoves()
    {
        using var store = FreezeStore();

        Assert.Equal(0, (await RegisterVerbAt(store, FreezeRegisteredAt, RegisterVerb.Family, HeavyweightRule.Name)).Code);

        var register = await new CandidateRegistrar(FreezeClock(FreezeNight), store.DatabaseFile).RowsAsync();
        var rules = HeavyweightBook.Standing(register, new DateTimeOffset(2026, 10, 1, 23, 30, 0, TimeSpan.Zero));
        var live = TheSetupFamilies.Heavyweights[0].Candidate;
        var bothExits = TheSetupFamilies.Heavyweights[1].Candidate;
        var members = TheSetupFamilies.Heavyweights[2].Candidate;
        var every = TheSetupFamilies.Heavyweights[3].Candidate;

        // The four in the register's order of their names, the live rule's first.
        Assert.Equal([live, every, bothExits, members], rules.Select(rule => rule.Candidate));

        // The night, October's first session and the first the books read, a rebalance for every one of them.
        var first = await new HeavyweightBook(FreezeClock(FreezeNight), store.DatabaseFile).RunAsync("GSPC", "night-freeze-1", default, rules);

        IReadOnlyList<string> HeldBy(string candidate) =>
            TextRows(store, $"SELECT ticker FROM heavyweight_rule_holding WHERE candidate = '{candidate.Replace("'", "''", StringComparison.Ordinal)}' AND ended_on IS NULL ORDER BY ticker;");

        // Worked by hand. The ten largest by value are C01 to C10. Against the fund's 22%, C01 leads by 8 points and C02
        // by 3, the rest by nothing or less; C02 follows the index at half its moves, a beta of about 0.51, under the
        // floor, so the live rule buys C01 alone, as the page's book does.
        Assert.True(first.Rebalanced);
        Assert.Equal(["C01"], first.Entered);
        Assert.Equal(["C01"], TextRows(store, "SELECT ticker FROM heavyweight_holding WHERE ended_on IS NULL;"));
        Assert.Equal(["C01"], HeldBy(live));

        // The members' mean in the fund's place: the eleven's mean return is 1.73 / 11, 15.7%, so C01 leads by 14.3 points
        // and C03 by 4.3 where it trails the fund by 2, C02 still under the beta's floor: the variant buys C01 and C03.
        Assert.Equal(["C01", "C03"], HeldBy(members));

        // Every company in the size cut: C11, the eleventh by value, leads the fund by 28 points, so the variant buys it
        // beside C01 where the live rule's ten leave it out.
        Assert.Equal(["C01", "C11"], HeldBy(every));
        Assert.Equal(["C01"], HeldBy(bothExits));

        // Each book read its own rebalance, the live rule's its ten and the variant reading every company its eleven,
        // each sector's return its fund's or its members' mean, and each company's beta read against the index's closes
        // on the same sessions, C01's about 1.47 and C02's about 0.51.
        Assert.Equal(10, Scalar(store, $"SELECT COUNT(*) FROM heavyweight_rule_night WHERE candidate = '{live.Replace("'", "''", StringComparison.Ordinal)}';"));
        Assert.Equal(11, Scalar(store, $"SELECT COUNT(*) FROM heavyweight_rule_night WHERE candidate = '{every.Replace("'", "''", StringComparison.Ordinal)}';"));
        Assert.Equal(0.22, Figure(store, $"SELECT DISTINCT sector_return FROM heavyweight_night;"), 9);
        Assert.Equal(1.73 / 11, Figure(store, $"SELECT DISTINCT sector_return FROM heavyweight_rule_night WHERE candidate = '{members.Replace("'", "''", StringComparison.Ordinal)}';"), 9);

        static double? BetaOf(double follows)
        {
            var member = FreezeMembers.First(one => one.Follows == follows);

            return HeavyweightRule.Beta([.. Enumerable.Range(0, HeavyweightRule.BetaReturns + 1).Select(at => ((double)FreezeClose(member, at, 75m), (double)IndexClose(at)))]);
        }

        Assert.Equal(BetaOf(1.5)!.Value, Figure(store, "SELECT beta FROM heavyweight_night WHERE ticker = 'C01';"), 9);
        Assert.Equal(BetaOf(0.5)!.Value, Figure(store, "SELECT beta FROM heavyweight_night WHERE ticker = 'C02';"), 9);
        Assert.InRange(BetaOf(1.5)!.Value, 1.4, 1.5);
        Assert.InRange(BetaOf(0.5)!.Value, 0.5, 0.6);
        Assert.StartsWith(
            $"a rebalance, 11 member(s) valued, 1 bought, 0 ended, 1 held; bought: C01; the registered rules: '{live}' a rebalance, 1 bought, 0 ended, 1 held; bought: C01; '{every}' a rebalance, 2 bought, 0 ended, 2 held; bought: C11, C01",
            HeavyweightBook.Detail(first),
            StringComparison.Ordinal);
        Assert.Equal(4, first.Rules!.Count(rule => rule.Rebalanced && rule.Fault is null));

        // The session after, inside the month: C01 closes at 75, under its 200-day average of 80. The variant selling on
        // either exit sells it at that close; the live rule, the members' variant and every company's keep it, and so
        // does the page's book, which holds at the live setting.
        FreezeNextBars(store, 75m);

        var next = await new HeavyweightBook(FreezeClock(FreezeNext), store.DatabaseFile).RunAsync("GSPC", "night-freeze-2", default, rules);

        Assert.False(next.Rebalanced);
        Assert.Empty(next.Ended);
        Assert.Equal(["C01"], HeldBy(live));
        Assert.Equal(["C01", "C03"], HeldBy(members));
        Assert.Equal(["C01", "C11"], HeldBy(every));
        Assert.Empty(HeldBy(bothExits));
        Assert.Equal(
            [$"C01|2026-10-02|75|{HeavyweightBook.UnderTheAverage}"],
            TextRows(store, $"SELECT ticker || '|' || ended_on || '|' || exit_close || '|' || reason FROM heavyweight_rule_holding WHERE candidate = '{bothExits.Replace("'", "''", StringComparison.Ordinal)}';"));

        // Its result is 75 over 130 less one, and its size cut's the mean of the ten's growths over the same session.
        Assert.Equal((75.0 / 130.0) - 1.0, Figure(store, $"SELECT result FROM heavyweight_rule_holding WHERE candidate = '{bothExits.Replace("'", "''", StringComparison.Ordinal)}';"), 9);
        Assert.Equal((((75.0 / 130.0) + 9.0) / 10.0) - 1.0, Figure(store, $"SELECT cut_return FROM heavyweight_rule_holding WHERE candidate = '{bothExits.Replace("'", "''", StringComparison.Ordinal)}';"), 9);
    }

    [Fact]
    public async Task ABetaUnderItsFloorAndALeadUnderItsFundLeaveAStockUnboughtAndBothAreReadAtTheirEdges()
    {
        // The page's book at the live setting with the beta read and not: C02, leading its fund by 3 points with a beta
        // near 0.51, is bought only where no beta is asked, beside C01.
        using (var store = FreezeStore())
        {
            var withBeta = await new HeavyweightBook(FreezeClock(FreezeNight), store.DatabaseFile).RunAsync("GSPC", "night-beta");

            Assert.Equal(["C01"], withBeta.Entered);
        }

        using (var store = FreezeStore())
        {
            var withoutBeta = await new HeavyweightBook(FreezeClock(FreezeNight), store.DatabaseFile, HeavyweightRule.Live with { HighBeta = false }).RunAsync("GSPC", "night-no-beta");

            Assert.Equal(["C01", "C02"], withoutBeta.Entered);
        }

        // The rule's own edges: a lead of nothing buys nothing, and a beta of exactly one is at the floor and bought.
        HeavyweightMember Member(string ticker, double lead, double? beta) =>
            new(ticker, ticker, GicsSectors.InformationTechnology, 100m, 1_000m, 0.10 + lead, 100.0, 99.0, 98.0, beta);

        var sectors = HeavyweightRule.Read(
            [Member("AT", 0.05, 1.0), Member("UNDER", 0.06, 0.999), Member("NONE", 0.0, 1.5), Member("NOBETA", 0.07, null)],
            HeavyweightRule.Live,
            new Dictionary<string, double?> { [GicsSectors.InformationTechnology] = 0.10 });

        Assert.Equal(["AT"], Assert.Single(sectors).Leaders);
    }

    [Fact]
    public async Task ARebalanceReadingAFundTheNightHoldsNoCloseForWaitsForANightItDoes()
    {
        using var store = FreezeStore(fundMissingOnTheNight: GicsSectors.Funds[GicsSectors.Utilities]);

        Assert.Equal(0, (await RegisterVerbAt(store, FreezeRegisteredAt, RegisterVerb.Family, HeavyweightRule.Name)).Code);

        var rules = HeavyweightBook.Standing(
            await new CandidateRegistrar(FreezeClock(FreezeNight), store.DatabaseFile).RowsAsync(),
            new DateTimeOffset(2026, 10, 1, 23, 30, 0, TimeSpan.Zero));
        var members = TheSetupFamilies.Heavyweights[2].Candidate;

        // The night holds no close for the Utilities fund: every book reading the funds waits, reading and buying
        // nothing and saying why, and the members' variant, which reads no fund, rebalances.
        var waited = await new HeavyweightBook(FreezeClock(FreezeNight), store.DatabaseFile).RunAsync("GSPC", "night-wait", default, rules);

        Assert.False(waited.Rebalanced);
        Assert.Equal("the rebalance waits for a night the store holds a close for XLU on, since none is stored for 2026-10-01", waited.Waits);
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM heavyweight_night;"));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM heavyweight_holding;"));
        Assert.Equal(
            [members],
            [.. waited.Rules!.Where(rule => rule.Rebalanced).Select(rule => rule.Candidate)]);
        Assert.Equal(3, waited.Rules!.Count(rule => rule.Waits is not null));
        Assert.Contains("the rebalance waits for a night the store holds a close for XLU on", HeavyweightBook.Detail(waited), StringComparison.Ordinal);

        // The next night holds every fund's close and C01 where it stood: the books that waited rebalance on it, still
        // October's, and the members' variant, the last in the register's order, does not rebalance again.
        FreezeNextBars(store, 130m);

        var read = await new HeavyweightBook(FreezeClock(FreezeNext), store.DatabaseFile).RunAsync("GSPC", "night-read", default, rules);

        Assert.True(read.Rebalanced);
        Assert.Equal(["C01"], read.Entered);
        Assert.Equal(["2026-10-02"], TextRows(store, "SELECT DISTINCT session_date FROM heavyweight_night;"));
        Assert.Equal(
            [true, true, true, false],
            [.. read.Rules!.Select(rule => rule.Rebalanced)]);
    }

    [Fact]
    public async Task AHeavyweightsRuleRegisteredAgainIsNamedAsNotReplayedAndItsRecordRestartsThere()
    {
        using var store = new TemporaryStore().Migrated();

        Assert.Equal(0, (await RegisterVerbAt(store, FreezeRegisteredAt, RegisterVerb.Family, HeavyweightRule.Name)).Code);

        // The replay the family registered again runs before it registers: each heavyweights rule's row says its book is
        // not replayed and its record restarts, under the version the code carries.
        var replayed = await new FamilyReplay(FixedClock.At(FreezeRegisteredAt.AddDays(1), SessionZones.UnitedStates), store.DatabaseFile).FamilyAsync(HeavyweightRule.Name);

        Assert.Equal(4, replayed.Count);
        Assert.All(replayed, one => Assert.Equal((false, FamilyReplay.NotReplayed, new SectorHeavyweightCandidate().Version), (one.Reproduced, one.Said, one.Version)));
        Assert.Equal(
            4,
            Scalar(store, $"SELECT COUNT(*) FROM run_log WHERE stage LIKE '{FamilyRecords.ReplayStage}: %' AND outcome = '{FamilyRecords.ReplayDiffers}';"));
    }

    [Fact]
    public void TheCurrentFiscalYearsEstimateIsReadOffAnAnswersTrend()
    {
        // The fixture's MSFT answer files its fiscal year to 2027-06-30 as the current one, its estimate 19.7531 against
        // 19.7221 thirty days before, raised.
        var msft = RecordedFundamentalsFeed.Parse(File.ReadAllText(Path.Combine(Folder(), "fundamentals-MSFT.json")), "MSFT");

        Assert.Equal(new EstimateReading(new DateOnly(2027, 6, 30), 19.7531m, 19.7221m), msft.Estimates);
        Assert.True(msft.Estimates!.Raised);
        Assert.Equal(("epsTrendCurrent", "epsTrend30daysAgo"), (EstimateReading.CurrentField, EstimateReading.DaysAgoField));

        // Over constructed trends: of two periods filed as the current year, the one ending later; a trend filing none,
        // or an answer filing no trend, read as none with why; a figure filed as none named; and level is not raised.
        static EstimateReading Read(string json)
        {
            using var document = JsonDocument.Parse(json);

            return EstimateReading.FromTrend(document.RootElement.Clone());
        }

        Assert.Equal(
            new EstimateReading(new DateOnly(2026, 12, 31), 5.10m, 5.00m),
            Read("{\"2025-12-31\":{\"date\":\"2025-12-31\",\"period\":\"0y\",\"epsTrendCurrent\":\"4.00\",\"epsTrend30daysAgo\":\"4.50\"},\"2026-12-31\":{\"date\":\"2026-12-31\",\"period\":\"0y\",\"epsTrendCurrent\":\"5.10\",\"epsTrend30daysAgo\":\"5.00\"},\"2027-12-31\":{\"date\":\"2027-12-31\",\"period\":\"+1y\",\"epsTrendCurrent\":\"9\",\"epsTrend30daysAgo\":\"1\"}}"));
        Assert.Equal(
            EstimateReading.Unread("the answer files no estimate for the current fiscal year"),
            Read("{\"2027-12-31\":{\"date\":\"2027-12-31\",\"period\":\"+1y\",\"epsTrendCurrent\":\"9\",\"epsTrend30daysAgo\":\"1\"}}"));
        Assert.Equal(EstimateReading.Unread("the answer files no earnings trend"), Read("[]"));
        Assert.Equal(
            "the answer files the current fiscal year's estimate 30 days before as none",
            Read("{\"2026-12-31\":{\"date\":\"2026-12-31\",\"period\":\"0y\",\"epsTrendCurrent\":\"5.10\",\"epsTrend30daysAgo\":null}}").NotRead);
        Assert.False(new EstimateReading(new DateOnly(2026, 12, 31), 5.00m, 5.00m).Raised);
        Assert.Null(new EstimateReading(new DateOnly(2026, 12, 31), 5.00m, null).Raised);

        // The committed capture of AAPL's trend files no current year, the next year and an old quarter alone.
        using var aapl = JsonDocument.Parse(File.ReadAllText(Path.Combine(Folder(), "phase14-trend-AAPL.json")));

        Assert.Equal("the answer files no estimate for the current fiscal year", EstimateReading.FromTrend(aapl.RootElement).NotRead);
        Assert.Equal(30, EstimateReading.Days);
    }

    // A fundamentals feed answering each member's estimate as it is handed, refusing the members it is told to, and
    // counting what it was asked.
    sealed class EstimatesAnswered(IReadOnlyDictionary<string, EstimateReading?> readings, IReadOnlySet<string> refused) : IFundamentalsFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<CompanyFundamentals> FundamentalsAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;
            Asked.Add(ticker);

            if (refused.Contains(ticker))
            {
                throw new ProviderRefusal("the provider answered 404", transient: false);
            }

            return Task.FromResult(new CompanyFundamentals(
                ticker, "USD", "0", [], null, new EpsBases(null, null, null), new ValuationRatios(null, null), new MarketValue(null),
                new AnalystRatings(null, null, null, null, null, null, null), [], 0)
            {
                Estimates = readings.GetValueOrDefault(ticker),
            });
        }
    }

    [Fact]
    public async Task TheNightAsksForEachMembersEstimatesOnceStoresEachAnswerAndAsksNoneItHolds()
    {
        using var store = new TemporaryStore().Migrated();

        var night = new DateOnly(2026, 10, 1);
        var clock = FreezeClock(night);
        var raised = new EstimateReading(new DateOnly(2026, 12, 31), 5.01m, 5.00m);
        var feed = new EstimatesAnswered(new Dictionary<string, EstimateReading?> { ["AA"] = raised, ["BB"] = null }, new HashSet<string> { "CC" });
        var fetcher = new EstimatesFetcher(feed, () => 0, clock, store.DatabaseFile);

        // Each member asked once, AA's answer stored, BB's answer filing no earnings stored as read as none, and CC refused,
        // named and storing nothing; one request each at the fundamentals' weight.
        var read = await fetcher.ReadAsync(night, ["AA", "BB", "CC", "AA"], "night-estimates");

        Assert.Equal(["AA", "BB", "CC"], feed.Asked);
        Assert.Equal((raised, true), (read["AA"], read["AA"].Raised == true));
        Assert.Equal("the answer files no earnings at all", read["BB"].NotRead);
        Assert.StartsWith("the provider did not serve it: ", read["CC"].NotRead, StringComparison.Ordinal);
        Assert.Equal(
            ["AA|2026-10-01|2026-12-31|5.01|5.00|-|night-estimates", "BB|2026-10-01|-|-|-|the answer files no earnings at all|night-estimates"],
            TextRows(store, "SELECT ticker || '|' || session_date || '|' || IFNULL(year_end, '-') || '|' || IFNULL(current_estimate, '-') || '|' || IFNULL(days_ago_estimate, '-') || '|' || IFNULL(not_read, '-') || '|' || run_id FROM estimate_reading ORDER BY ticker;"));

        await fetcher.RecordAsync("night-estimates");

        Assert.Equal(
            ["estimates|ok|2|3"],
            TextRows(store, "SELECT stage || '|' || outcome || '|' || rows_written || '|' || network_requests FROM run_log WHERE run_id = 'night-estimates';"));
        Assert.Contains("CC: nothing was stored for it, the provider answered 404", TextRows(store, "SELECT detail FROM run_log WHERE run_id = 'night-estimates';").Single(), StringComparison.Ordinal);

        // A night run again reads back what it stored and asks for none of it; CC, which stored nothing, is asked again.
        var again = new EstimatesAnswered(new Dictionary<string, EstimateReading?> { ["CC"] = raised }, new HashSet<string>());
        var rerun = await new EstimatesFetcher(again, () => 0, clock, store.DatabaseFile).ReadAsync(night, ["AA", "BB", "CC"], "night-estimates-2");

        Assert.Equal(["CC"], again.Asked);
        Assert.Equal((raised, raised), (rerun["AA"], rerun["CC"]));

        // A night run again for an earlier session is handed no feed and asks for nothing, reading what it holds; and an
        // ask that would take the night past the day's allowance is not made.
        var earlier = await new EstimatesFetcher(null, () => 0, clock, store.DatabaseFile).ReadAsync(new DateOnly(2026, 9, 30), ["AA"], "night-earlier");

        Assert.Equal("none asked for, since this night was run again for an earlier session", earlier["AA"].NotRead);

        var spent = new EstimatesAnswered(new Dictionary<string, EstimateReading?>(), new HashSet<string>());
        var atTheAllowance = await new EstimatesFetcher(spent, () => ProviderWeights.DailyAllowance - ProviderWeights.Fundamentals + 1, clock, store.DatabaseFile).ReadAsync(night, ["DD"], "night-allowance");

        Assert.Empty(spent.Asked);
        Assert.StartsWith("none asked for, since its ask would take the night past the day's allowance", atTheAllowance["DD"].NotRead, StringComparison.Ordinal);

        // A night no rule asked about writes its row all the same, saying so.
        using var quiet = new TemporaryStore().Migrated();

        await new EstimatesFetcher(spent, () => 0, clock, quiet.DatabaseFile).RecordAsync("night-quiet");

        Assert.Equal(
            ["estimates|0|0|no member was passed by a rule reading analysts' estimates, so none was asked for"],
            TextRows(quiet, "SELECT stage || '|' || rows_written || '|' || network_requests || '|' || detail FROM run_log WHERE run_id = 'night-quiet';"));
    }
}
