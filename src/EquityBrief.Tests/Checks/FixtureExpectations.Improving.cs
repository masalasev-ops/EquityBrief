using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Improving;

namespace EquityBrief.Tests.Checks;

// The Fundamentals family, from 22.13: the rule's bar, its rise over the filing before, its sales growth speeding up, its
// hold and its flag worked by hand on both sides of each threshold; a filing first read on the night told from one read
// before; the step's buys, flags and improvers over a constructed store, a buy waiting for its session, a sale on the score
// falling, one at its 126th session and one on leaving the index; a trade's result, benchmark, round trip and edge worked
// by hand; a night run again writing what it wrote; and no source a rule's version pins one 22.13 writes.
// see: The Fundamentals family buys a company whose newly read filing lifts its scorecard to eight in nine, and flags one whose release reads better
// see: A Fundamentals trade is held while its score stays at seven in nine, and judged against an equal-weighted hold of its index's members after its round trip
public partial class FixtureExpectations
{
    // The rows 22.13 adds that this check reaches: section 17's starting settings and section 18's two.
    internal static readonly string[] ImprovingClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The Fundamentals family's starting settings"),
        CheckReach.Key(Scope.FailureTable, "A filing the Fundamentals family cannot weigh"),
        CheckReach.Key(Scope.FailureTable, "A Fundamentals buy whose next session is not stored"),
    ];

    [Fact]
    public void TheFundamentalsRulesBarRiseSalesGrowthHoldAndFlagAreWorkedByHandOnBothSidesOfEachThreshold()
    {
        var starting = ImprovingRule.Starting;

        // Eight in nine is 72 against 8 times 9, at the bar; seven in nine is 63, under it. As a share, seven of eight is 63
        // against 64, under it, and eight of eight clears it.
        Assert.Equal(new ImprovingAnswer(true, null), ImprovingRule.Answer(8, 9, 5, 9, 0.01, starting));
        Assert.Equal(ImprovingRule.UnderTheBar, ImprovingRule.Answer(7, 9, 5, 9, 0.01, starting).Reason);
        Assert.Equal(ImprovingRule.UnderTheBar, ImprovingRule.Answer(7, 8, 5, 9, 0.01, starting).Reason);
        Assert.True(ImprovingRule.Answer(8, 8, 5, 9, 0.01, starting).Bought);

        // A financial company's seven of seven is 63 against 56, over the bar, though seven is fewer than eight.
        Assert.True(ImprovingRule.Answer(7, 7, 5, 9, 0.01, starting).Bought);

        // Above the score before, read as shares: eight of nine over eight of nine is no rise, over seven of eight it is.
        Assert.Equal(ImprovingRule.NotHigher, ImprovingRule.Answer(8, 9, 8, 9, 0.01, starting).Reason);
        Assert.Equal(ImprovingRule.NotHigher, ImprovingRule.Answer(8, 9, 9, 9, 0.01, starting).Reason);
        Assert.True(ImprovingRule.Answer(8, 9, 7, 8, 0.01, starting).Bought);

        // Its sales growth faster than in the quarter before: a change above nought buys, nought and below do not, and none
        // read buys nothing.
        Assert.Equal(ImprovingRule.NotFaster, ImprovingRule.Answer(8, 9, 5, 9, 0, starting).Reason);
        Assert.Equal(ImprovingRule.NotFaster, ImprovingRule.Answer(8, 9, 5, 9, -0.01, starting).Reason);
        Assert.True(ImprovingRule.Answer(8, 9, 5, 9, 0.0001, starting).Bought);
        Assert.Equal(ImprovingRule.GrowthNotRead, ImprovingRule.Answer(8, 9, 5, 9, null, starting).Reason);

        // Four checks read give no score whatever they passed, and a filing whose filer held no quarter before it rises
        // from a score of none.
        Assert.Equal(ImprovingRule.NoWord, ImprovingRule.Answer(4, 4, 0, 0, 0.01, starting).Reason);
        Assert.True(ImprovingRule.Answer(9, 9, 0, 0, 0.01, starting).Bought);

        // Held at seven in nine and not at six; as a share six of seven is 54 against 49 and holds, five of seven 45 and
        // does not, and four read hold nothing.
        Assert.True(ImprovingRule.Holds(7, 9, starting));
        Assert.False(ImprovingRule.Holds(6, 9, starting));
        Assert.True(ImprovingRule.Holds(6, 7, starting));
        Assert.False(ImprovingRule.Holds(5, 7, starting));
        Assert.False(ImprovingRule.Holds(4, 4, starting));

        // A flag: results better with the outlook raised or kept; lowered or withdrawn, none given, or results in line, none.
        Assert.True(ImprovingRule.Flags(new ReleaseAnswer("better", "raised", "confident", "stronger")));
        Assert.True(ImprovingRule.Flags(new ReleaseAnswer("better", "kept", "neutral", "not stated")));
        Assert.False(ImprovingRule.Flags(new ReleaseAnswer("better", "lowered or withdrawn", "neutral", "not stated")));
        Assert.False(ImprovingRule.Flags(new ReleaseAnswer("better", "none given", "neutral", "not stated")));
        Assert.False(ImprovingRule.Flags(new ReleaseAnswer("in line", "raised", "neutral", "not stated")));
    }

    // A filing is first read on the first night whose session follows its filing day, the window opening on the session the
    // family last read; its day is the earliest filing stating the newest quarter's net income; and a release confirmed by a
    // filing is one accepted after the quarter ended and on or before the filing's day.
    [Fact]
    public void AFilingIsFirstReadOnTheNightAfterItsDayAndAReleaseIsConfirmedOnlyBetweenItsQuarterAndItsFiling()
    {
        var friday = new DateOnly(2026, 10, 23);
        var monday = new DateOnly(2026, 10, 26);
        var quarter = new DateOnly(2026, 9, 30);

        Assert.Equal(friday, ImprovingRule.SessionBefore(monday));
        Assert.True(ImprovingRule.FirstRead(friday, friday, monday));
        Assert.True(ImprovingRule.FirstRead(new DateOnly(2026, 10, 25), friday, monday));
        Assert.False(ImprovingRule.FirstRead(new DateOnly(2026, 10, 22), friday, monday));
        Assert.False(ImprovingRule.FirstRead(monday, friday, monday));

        // An amendment filed after it and a balance sheet's day move nothing, and a filing made on the session is not read.
        FiledFactRow[] facts =
        [
            new("NetIncomeLoss", new DateOnly(2026, 7, 1), quarter, 10m, friday, "10-Q", "a1"),
            new("NetIncomeLoss", new DateOnly(2026, 7, 1), quarter, 11m, new DateOnly(2026, 10, 24), "10-Q/A", "a2"),
            new("Assets", quarter, quarter, 100m, new DateOnly(2026, 10, 20), "10-Q", "a0"),
            new("NetIncomeLoss", new DateOnly(2026, 4, 1), new DateOnly(2026, 6, 30), 9m, new DateOnly(2026, 7, 24), "10-Q", "a3"),
        ];

        Assert.Equal(friday, ImprovingRule.FiledOn(facts, quarter, monday));
        Assert.Null(ImprovingRule.FiledOn(facts, quarter, friday));
        Assert.Null(ImprovingRule.FiledOn(facts, null, monday));

        Assert.Equal(new DateOnly(2026, 10, 21), ImprovingRule.Confirmed(new DateOnly(2026, 10, 21), quarter, friday));
        Assert.Equal(friday, ImprovingRule.Confirmed(friday, quarter, friday));
        Assert.Null(ImprovingRule.Confirmed(new DateOnly(2026, 10, 24), quarter, friday));
        Assert.Null(ImprovingRule.Confirmed(quarter, quarter, friday));

        // A release accepted on its filing's own day is not after it, and the day after is; with no filing every release is.
        Assert.False(ImprovingRule.After(friday, friday));
        Assert.True(ImprovingRule.After(new DateOnly(2026, 10, 24), friday));
        Assert.True(ImprovingRule.After(friday, null));
    }

    // From the 22.13 correction of 2026-10-27, on the operator's word: a release reports a quarter newer than the newest
    // stored filing's only once accepted after the end of the quarter that follows it, and a flag reads the night's session
    // and the 62 before it, the sessions counted by hand off the exchange's closures of 2026-07-03 and 2026-09-07: the
    // 63rd back from 2026-10-26 is 2026-07-29 and the 64th 2026-07-28.
    // see: A Fundamentals flag stands only for a release reporting a quarter newer than the member's newest stored filing, accepted within the last 63 sessions
    [Fact]
    public void AFlagsReleaseReportsANewerQuarterOnlyAfterTheFollowingQuarterEndsAndStandsWithinSixtyThreeSessions()
    {
        var june = new DateOnly(2026, 6, 30);
        var night = new DateOnly(2026, 10, 26);
        var better = new ReleaseAnswer("better", "raised", "confident", "stronger");

        Assert.False(ImprovingRule.Newer(new DateOnly(2026, 9, 30), june));
        Assert.True(ImprovingRule.Newer(new DateOnly(2026, 10, 1), june));
        Assert.False(ImprovingRule.Newer(new DateOnly(2026, 8, 7), june));
        Assert.True(ImprovingRule.Newer(new DateOnly(2026, 8, 7), null));

        Assert.True(ImprovingRule.Recent(new DateOnly(2026, 7, 29), night));
        Assert.False(ImprovingRule.Recent(new DateOnly(2026, 7, 28), night));
        Assert.Equal(63, ImprovingRule.SessionsFrom(new DateOnly(2026, 7, 29), night));

        // The newest stored filing's quarter is the latest span ending before the night's filings, a balance sheet's day
        // and a filing made on the night itself passed over.
        FiledFactRow[] facts =
        [
            new("NetIncomeLoss", new DateOnly(2026, 4, 1), june, 1m, new DateOnly(2026, 8, 5), "10-Q", "q2"),
            new("Revenues", new DateOnly(2026, 1, 1), june, 2m, new DateOnly(2026, 8, 5), "10-Q", "q2"),
            new("Assets", new DateOnly(2026, 9, 30), new DateOnly(2026, 9, 30), 3m, new DateOnly(2026, 10, 20), "10-Q", "q3"),
            new("NetIncomeLoss", new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), 4m, night, "10-Q", "q3"),
        ];

        Assert.Equal(june, ImprovingRule.NewestQuarter(facts, night));
        Assert.Null(ImprovingRule.NewestQuarter([], night));

        // Every part of a flag together: a check read, after the scorecard's filing, a newer quarter, recent and better.
        Assert.True(ImprovingRule.FlagStands(9, new DateOnly(2026, 10, 21), new DateOnly(2026, 8, 5), june, night, better));
        Assert.False(ImprovingRule.FlagStands(0, new DateOnly(2026, 10, 21), new DateOnly(2026, 8, 5), june, night, better));
        Assert.False(ImprovingRule.FlagStands(9, new DateOnly(2026, 8, 7), new DateOnly(2026, 8, 5), june, night, better));
        Assert.False(ImprovingRule.FlagStands(9, new DateOnly(2026, 7, 28), new DateOnly(2026, 5, 1), new DateOnly(2026, 3, 31), night, better));
        Assert.True(ImprovingRule.FlagStands(9, new DateOnly(2026, 7, 29), new DateOnly(2026, 5, 1), new DateOnly(2026, 3, 31), night, better));
    }

    // The family's step over five S&P 400 members, each 8 of 9 with a release reading better and its outlook raised: J01's
    // release of 2026-10-21 follows its June quarter by more than a quarter and flags; J02's of 2026-08-07 reports the
    // quarter its filing of 2026-08-05 stores; J03's, its March quarter's next, stands 64 sessions back and J04's 63; and
    // J05's scorecard read no check.
    [Fact]
    public async Task AFlagStandsOnlyForAReleaseOfANewerQuarterWithinSixtyThreeSessionsAndNeverForAScorecardReadingNoCheck()
    {
        using var store = new TemporaryStore().Migrated();
        var growthAt = LedgerReadings.IndexOf("revenue_growth");
        var changeAt = LedgerReadings.IndexOf("growth_change");

        store.Execute("INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', '2026-10-19T00:00:00Z', 'constructed');");

        foreach (var (number, quarter, filed, released, applied) in new[]
        {
            (1, "2026-06-30", "2026-08-05", "2026-10-21", 9),
            (2, "2026-06-30", "2026-08-05", "2026-08-07", 9),
            (3, "2026-03-31", "2026-05-01", "2026-07-28", 9),
            (4, "2026-03-31", "2026-05-01", "2026-07-29", 9),
            (5, "2026-06-30", "2026-08-05", "2026-10-21", 0),
        })
        {
            var ticker = FormattableString.Invariant($"J{number:00}");
            var cik = FormattableString.Invariant($"00000008{number:00}");
            var start = quarter == "2026-06-30" ? "2026-04-01" : "2026-01-01";
            var readings = Enumerable.Repeat<double?>(null, LedgerReadings.Count).ToArray();

            readings[growthAt] = 0.1;
            readings[changeAt] = 0.02;
            store.Execute(FormattableString.Invariant($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-08-01T23:40:00.000Z', '{cik}');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO member_reading (index_code, session_date, ticker, company_value) VALUES ('MID', '{ImprovingFirst}', '{ticker}', '3000000000');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{cik}', 'NetIncomeLoss', '{start}', '{quarter}', '1000000', '{filed}', '10-Q', 'g{number:00}', 'constructed');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO fundamental_score (index_code, session_date, ticker, quarter, financial, passed, applied, checks) VALUES ('MID', '{ImprovingFirst}', '{ticker}', '{quarter}', 0, {(applied == 0 ? 0 : 8)}, {applied}, '[]');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('MID', '{ImprovingFirst}', '{ticker}', '{System.Text.Json.JsonSerializer.Serialize(readings)}', 'test');"));
            QualityBar(store, ticker, ImprovingFirst, 100m + number, 100m + number);
            ImprovingRelease(store, cik, FormattableString.Invariant($"s{number:00}"), released + "T12:00:00Z", "raised");
        }

        await new ImprovingFamily(QualityClock(ImprovingFirst), store.DatabaseFile).RunAsync("improving-flags");

        Assert.Equal(
            [$"J01|{ImprovingRule.Flagged}|1", "J02|null|0", "J03|null|0", $"J04|{ImprovingRule.Flagged}|1", "J05|null|0"],
            FamilyRows(store, $"SELECT ticker, status, flagged FROM improving_result WHERE index_code = 'MID' AND session_date = '{ImprovingFirst}' ORDER BY ticker;"));
        Assert.Equal(["2"], FamilyRows(store, $"SELECT flagged FROM improving_night WHERE index_code = 'MID' AND session_date = '{ImprovingFirst}';"));
    }

    // The improvers' order: the score's rise, the larger first and none last; then how much faster its sales grew, none
    // last; then the ticker.
    [Fact]
    public void TheImproversAreOrderedByTheScoresRiseThenBySalesGrowthSpeedingUpThenByTheTicker()
    {
        (double? Rise, double? Faster, string Ticker)[] rows =
        [
            (0.2, 0.01, "BBB"),
            (0.3, -0.02, "AAA"),
            (0.2, 0.05, "CCC"),
            (null, 0.09, "DDD"),
            (0.2, null, "EEE"),
            (0.2, 0.01, "ABA"),
        ];

        Assert.Equal(["AAA", "CCC", "ABA", "BBB", "EEE", "DDD"], ImprovingRule.Ranked(rows, row => row).Select(row => row.Ticker));
    }

    const string ImprovingFirst = "2026-10-26";
    const string ImprovingNext = "2026-10-27";
    const string ImprovingSold = "2026-10-28";

    // Eight S&P 400 members, I01 to I08, each closing at 100 plus its number on the first night. I01, I02, I03, I07 and I08
    // filed their newest quarter on the session before the first night, I04 three sessions before it, and I05 and I06 the
    // quarter before in the summer. On the first night I01 and I08 score 8 of 9 with their sales growing faster, I02 8 of 9
    // with its sales slower, I03 7 of 9, I04 9 of 9, I07 4 of 4 and I05 and I06 6 of 9; I01 scores 6 of 9 on the third
    // night. I01's release, before its filing, and I05's, after its, read results better with the outlook raised, and I06's,
    // after its, with it lowered or withdrawn.
    static TemporaryStore ImprovingStore()
    {
        var store = new TemporaryStore().Migrated();
        var growthAt = LedgerReadings.IndexOf("revenue_growth");
        var changeAt = LedgerReadings.IndexOf("growth_change");

        store.Execute("INSERT INTO reading_pin (model, model_file, sha256, size, modified, runtime_name, runtime_version, prompt_version, temperature, seed, answer_tokens, context, cutoff, written_at, history_completed_at, run_id) VALUES ('m', 'f', 'h', 1, '2026-10-18T00:00:00.0000000Z', 'r', 'v', 'p', 0, 1, 1, 1, '2025-01-31', '2026-10-18T00:00:00Z', '2026-10-19T00:00:00Z', 'constructed');");

        for (var number = 1; number <= 8; number++)
        {
            var ticker = FormattableString.Invariant($"I{number:00}");
            var cik = FormattableString.Invariant($"00000009{number:00}");
            var (start, quarter, filed) = number switch
            {
                4 => ("2026-07-01", "2026-09-30", "2026-10-20"),
                5 or 6 => ("2026-04-01", "2026-06-30", "2026-08-05"),
                _ => ("2026-07-01", "2026-09-30", "2026-10-23"),
            };
            var (passed, applied) = number switch { 1 or 2 or 8 => (8, 9), 3 => (7, 9), 4 => (9, 9), 7 => (4, 4), _ => (6, 9) };
            var change = number switch { 2 => -0.01, 3 => 0.03, 8 => 0.05, _ => 0.02 };

            store.Execute(FormattableString.Invariant($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-08-01T23:40:00.000Z', '{cik}');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO member_reading (index_code, session_date, ticker, company_value) VALUES ('MID', '{ImprovingFirst}', '{ticker}', '3000000000');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('{cik}', 'NetIncomeLoss', '{start}', '{quarter}', '1000000', '{filed}', '10-Q', 'f{number:00}', 'constructed');"));
            QualityBar(store, ticker, ImprovingFirst, 100m + number, 100m + number);

            foreach (var night in new[] { ImprovingFirst, ImprovingNext, ImprovingSold })
            {
                var (scored, of) = number == 1 && night == ImprovingSold ? (6, 9) : (passed, applied);
                var readings = Enumerable.Repeat<double?>(null, LedgerReadings.Count).ToArray();

                readings[growthAt] = 0.1;
                readings[changeAt] = change;
                store.Execute(FormattableString.Invariant($"INSERT INTO fundamental_score (index_code, session_date, ticker, quarter, financial, passed, applied, checks) VALUES ('MID', '{night}', '{ticker}', '{quarter}', 0, {scored}, {of}, '[]');"));
                store.Execute(FormattableString.Invariant($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('MID', '{night}', '{ticker}', '{System.Text.Json.JsonSerializer.Serialize(readings)}', 'test');"));
            }
        }

        ImprovingRelease(store, "0000000901", "r01", "2026-10-21T12:00:00Z", "raised");
        ImprovingRelease(store, "0000000905", "r05", "2026-10-22T12:00:00Z", "raised");
        ImprovingRelease(store, "0000000906", "r06", "2026-10-22T12:00:00Z", "lowered or withdrawn");

        return store;
    }

    static void ImprovingRelease(TemporaryStore store, string cik, string accession, string accepted, string outlook)
    {
        store.Execute($"INSERT INTO release_document (accession, cik, form, filed, accepted_at, exhibit, url, text_gzip, text_sha256, text_length, run_id, release) VALUES ('{accession}', '{cik}', '8-K', '{accepted[..10]}', '{accepted}', 'ex991.htm', 'https://www.sec.gov/x.htm', zeroblob(4), 'x', 4, 'constructed', 1);");
        store.Execute($"INSERT INTO release_reading (accession, exhibit, pin, outcome, cause, results, outlook, tone, demand, seconds, prompt_tokens, read_at, run_id) VALUES ('{accession}', 'ex991.htm', 1, 'read', NULL, 'better', '{outlook}', 'confident', 'stronger', 3, 5000, '{accepted}', 'constructed');");
    }

    static async Task ImprovingNightsAsync(TemporaryStore store, int nights)
    {
        await new ImprovingFamily(QualityClock(ImprovingFirst), store.DatabaseFile).RunAsync("improving-1");

        if (nights < 2)
        {
            return;
        }

        for (var number = 1; number <= 7; number++)
        {
            QualityBar(store, FormattableString.Invariant($"I{number:00}"), ImprovingNext, 101m + number, 102m + number);
        }

        await new ImprovingFamily(QualityClock(ImprovingNext), store.DatabaseFile).RunAsync("improving-2");

        if (nights < 3)
        {
            return;
        }

        for (var number = 1; number <= 8; number++)
        {
            QualityBar(store, FormattableString.Invariant($"I{number:00}"), ImprovingSold, 102m + number, number == 1 ? 100m : 104m + number);
        }

        await new ImprovingFamily(QualityClock(ImprovingSold), store.DatabaseFile).RunAsync("improving-3");
    }

    // The first night chooses I01 and I08, whose filings of the session before lift them to 8 of 9 with their sales growing
    // faster, at their close; I02 and I03 improved short of the bar, its sales slower and its score under it; I07 read too few
    // checks; I04's filing was read on an earlier night; I05 is flagged from its release since its filing and I06 is not,
    // its outlook lowered; and nothing is bought before the next open.
    [Fact]
    public async Task TheFundamentalsFamilysFirstNightChoosesTheNewlyReadFilingsOverTheBarAndFlagsABetterRelease()
    {
        using var store = ImprovingStore();

        await ImprovingNightsAsync(store, 1);

        Assert.Equal(
            ["I01|101|2026-10-23|2026-10-21|8|9|0|0|null|open", "I08|108|2026-10-23|null|8|9|0|0|null|open"],
            FamilyRows(store, $"SELECT ticker, buy_close, filed, released, passed_checks, applied, before_passed, before_applied, bought_on, end_reason FROM improving_trade WHERE index_code = 'MID' AND chosen_on = '{ImprovingFirst}' ORDER BY ticker;"));
        Assert.Equal(
            [
                $"I01|1|{ImprovingRule.Bought}|null|0",
                $"I02|1|{ImprovingRule.Improved}|{ImprovingRule.NotFaster}|0",
                $"I03|1|{ImprovingRule.Improved}|{ImprovingRule.UnderTheBar}|0",
                "I04|0|null|null|0",
                $"I05|0|{ImprovingRule.Flagged}|null|1",
                "I06|0|null|null|0",
                $"I07|1|{ImprovingRule.Improved}|{ImprovingRule.NoWord}|0",
                $"I08|1|{ImprovingRule.Bought}|null|0",
            ],
            FamilyRows(store, $"SELECT ticker, first_read, status, reason, flagged FROM improving_result WHERE index_code = 'MID' AND session_date = '{ImprovingFirst}' ORDER BY ticker;"));
        Assert.Equal(["2026-10-22|raised"], FamilyRows(store, $"SELECT released, outlook FROM improving_result WHERE index_code = 'MID' AND session_date = '{ImprovingFirst}' AND ticker = 'I05';"));
        Assert.Equal(["8|8|5|5|2|1|2"], FamilyRows(store, $"SELECT members, scored, first_read, improved, bought, flagged, held FROM improving_night WHERE index_code = 'MID' AND session_date = '{ImprovingFirst}';"));
    }

    // The session after, I01 is bought at its open, 102 over its close of 101; I08, whose stock holds no bar that session,
    // waits; and no filing is read for the first time, the window opening on the first night.
    [Fact]
    public async Task AFundamentalsTradeIsBoughtAtTheNextOpenAndOneWhoseNextSessionIsNotStoredWaits()
    {
        using var store = ImprovingStore();

        await ImprovingNightsAsync(store, 2);

        Assert.Equal([$"{ImprovingNext}|102|1"], FamilyRows(store, "SELECT bought_on, fill, sessions FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"));
        Assert.Equal(1.0 / 101, QualityDouble(store, "SELECT gap FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"), 12);
        Assert.Equal(["null|null|open"], FamilyRows(store, "SELECT bought_on, fill, end_reason FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I08';"));
        Assert.Equal(["0|0|0|2"], FamilyRows(store, $"SELECT first_read, bought, flagged - 1, held FROM improving_night WHERE index_code = 'MID' AND session_date = '{ImprovingNext}';"));
        Assert.Contains("1 waiting for their next session", Assert.Single(FamilyRows(store, $"SELECT detail FROM run_log WHERE run_id = 'improving-2' AND stage = '{ImprovingFamily.Stage}';")), StringComparison.Ordinal);
    }

    // On the third night I01's score stands at 6 of 9, under the hold of 7, so it is sold at that close; its result, its
    // benchmark against the members answered on its night, its round trip and its edge are the ones improving.json derives
    // by hand. I08 is bought at the open of the first session after its choice the store holds.
    [Fact]
    public async Task AFundamentalsTradeIsSoldAtTheCloseItsScoreFallsUnderTheHoldWithItsEdgeWorkedByHand()
    {
        using var store = ImprovingStore();

        await ImprovingNightsAsync(store, 3);

        var expected = Expected("improving");

        Assert.Equal(ImprovingFirst, expected.GetProperty("first").GetString());
        Assert.Equal(ImprovingSold, expected.GetProperty("sold").GetString());
        Assert.Equal([$"{ImprovingSold}|100|{ImprovingRule.ScoreFell}"], FamilyRows(store, "SELECT ended_on, sale, end_reason FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"));
        Assert.Equal([$"{ImprovingSold}|110|open"], FamilyRows(store, "SELECT bought_on, fill, end_reason FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I08';"));

        var result = (100.0 / 101) - 1 - (1.0 / 101);
        var growth = new[] { 100.0 / 101 }.Concat(Enumerable.Range(2, 6).Select(number => (104.0 + number) / (100 + number))).Append(112.0 / 108).Average();
        var benchGap = Enumerable.Range(1, 7).Average(number => 1.0 / (100 + number));
        var benchmark = growth - 1.0 - benchGap;
        var trip = ((0.072 / 200 * 102) + (0.072 / 200 * 100)) / 102 * 100;
        var i01 = expected.GetProperty("i01");

        Assert.Equal(i01.GetProperty("result").GetDouble(), result, 12);
        Assert.Equal(i01.GetProperty("benchmark").GetDouble(), benchmark, 12);
        Assert.Equal(i01.GetProperty("cost").GetDouble(), trip, 12);
        Assert.Equal(i01.GetProperty("edge").GetDouble(), ((result - benchmark) * 100) - trip, 10);
        Assert.Equal(expected.GetProperty("i01Gap").GetDouble(), QualityDouble(store, "SELECT gap FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"), 12);
        Assert.Equal(i01.GetProperty("result").GetDouble(), QualityDouble(store, "SELECT result FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"), 10);
        Assert.Equal(i01.GetProperty("benchmark").GetDouble(), QualityDouble(store, "SELECT benchmark FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"), 10);
        Assert.Equal(i01.GetProperty("cost").GetDouble(), QualityDouble(store, "SELECT cost FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"), 10);
        Assert.Equal(i01.GetProperty("edge").GetDouble(), QualityDouble(store, "SELECT edge FROM improving_trade WHERE index_code = 'MID' AND ticker = 'I01';"), 10);
    }

    // A night run again writes what it wrote: the trade it sold sold once, the stock it bought bought once, its answers and
    // its row its own.
    [Fact]
    public async Task AFundamentalsNightRunAgainWritesWhatItWrote()
    {
        using var store = ImprovingStore();

        await ImprovingNightsAsync(store, 3);

        var before = FamilyRows(store, "SELECT * FROM improving_trade ORDER BY ticker, chosen_on;").Concat(FamilyRows(store, "SELECT * FROM improving_result ORDER BY session_date, ticker;")).ToArray();

        await new ImprovingFamily(QualityClock(ImprovingSold), store.DatabaseFile).RunAsync("improving-3-again");

        Assert.Equal(before, FamilyRows(store, "SELECT * FROM improving_trade ORDER BY ticker, chosen_on;").Concat(FamilyRows(store, "SELECT * FROM improving_result ORDER BY session_date, ticker;")));
        Assert.Equal(["3"], FamilyRows(store, "SELECT COUNT(*) FROM improving_night WHERE index_code = 'MID';"));
    }

    // A trade is held through its 125th session and sold at the close of its 126th, its sessions counted from its buy's own
    // as the store holds the stock's bars; and one whose stock left the index is sold at its last close as a member.
    [Fact]
    public async Task AFundamentalsTradeIsSoldAtItsLastSessionsCloseAndAtItsLastCloseAsAMember()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = new List<DateOnly>();

        for (var day = new DateOnly(2026, 3, 31); sessions.Count < 127; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'T01', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', 'T02', '2024-01-02', '{Stamp(sessions[^1])}', '2024-01-02T00:00:00Z');");
        store.Execute($"INSERT INTO improving_trade (rule, index_code, ticker, chosen_on, filed, released, passed_checks, applied, before_passed, before_applied, buy_close, bought_on, fill, gap, bench_gap, members, growth, through, sessions, end_reason) VALUES ('provisional', 'MID', 'T01', '{Stamp(sessions[0])}', '{Stamp(sessions[0])}', NULL, 8, 9, 5, 9, '100', '{Stamp(sessions[1])}', '100', 0.0, 0.0, 1, 1.0, '{Stamp(sessions[1])}', 1, 'open');");
        store.Execute($"INSERT INTO improving_trade (rule, index_code, ticker, chosen_on, filed, released, passed_checks, applied, before_passed, before_applied, buy_close, bought_on, fill, gap, bench_gap, members, growth, through, sessions, end_reason) VALUES ('provisional', 'MID', 'T02', '{Stamp(sessions[0])}', '{Stamp(sessions[0])}', NULL, 8, 9, 5, 9, '100', '{Stamp(sessions[1])}', '100', 0.0, 0.0, 1, 1.0, '{Stamp(sessions[1])}', 1, 'open');");

        foreach (var session in sessions.Take(126))
        {
            QualityBar(store, "T01", Stamp(session), 100m, 100m);
            QualityBar(store, "T02", Stamp(session), 100m, session == sessions[125] ? 104m : 100m);
        }

        // The night of its 125th session, the 126th of the sessions from its choice: held, and T02 still a member.
        await new ImprovingFamily(QualityClock(Stamp(sessions[125])), store.DatabaseFile).RunAsync("improving-125");

        Assert.Equal([$"open|125"], FamilyRows(store, "SELECT end_reason, sessions FROM improving_trade WHERE ticker = 'T01';"));
        Assert.Equal(["open"], FamilyRows(store, "SELECT end_reason FROM improving_trade WHERE ticker = 'T02';"));

        // The night of its 126th: T01 sold at that close; T02, no member on it, sold at its last close as one, 104.
        QualityBar(store, "T01", Stamp(sessions[126]), 100m, 101m);
        QualityBar(store, "T02", Stamp(sessions[126]), 100m, 99m);

        await new ImprovingFamily(QualityClock(Stamp(sessions[126])), store.DatabaseFile).RunAsync("improving-126");

        Assert.Equal([$"{Stamp(sessions[126])}|101|{ImprovingRule.TimeLimit}|126"], FamilyRows(store, "SELECT ended_on, sale, end_reason, sessions FROM improving_trade WHERE ticker = 'T01';"));
        Assert.Equal([$"{Stamp(sessions[125])}|104|{ImprovingRule.LeftTheIndex}"], FamilyRows(store, "SELECT ended_on, sale, end_reason FROM improving_trade WHERE ticker = 'T02';"));
    }

    // The sources 22.13 writes or edits.
    static readonly string[] ImprovingSources =
    [
        "src/EquityBrief.Core/Families/ImprovingRule.cs",
        "src/EquityBrief.Worker/Improving/ImprovingFamily.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Improving.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Improving.cs",
        "src/EquityBrief.Api/Reading/PaperScreen.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Improving.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Tonight.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Families.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Tonight.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.TonightTop.cs",
        "src/EquityBrief.Api/Reading/NameScreen.cs",
        "src/EquityBrief.Api/Reading/BriefScreen.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Families.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Indices.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Dips.cs",
    ];

    // No source any rule's version pins is one 22.13 writes, so no existing rule's version moves, the reader shown to find a
    // source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOne22Point13Writes()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(ImprovingSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(ImprovingSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Core/Families/SetupFamilies.cs", "src/EquityBrief.Worker/Quality/QualityFamily.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }
}
