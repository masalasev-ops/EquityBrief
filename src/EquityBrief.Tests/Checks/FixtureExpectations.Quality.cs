using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Quality;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// Quality at a fair price, from 22.9: the rule's floor, valuation and order worked by hand on both sides of each
// threshold; the step's first holdings and the next month's reshuffle over a constructed store, a holding sold where it
// no longer qualifies, one still qualifying kept, one sold at its last close as a member, the places left filled down the
// ranking and a buy waiting for the session the store does not hold yet; a holding's result, its benchmark against an
// equal-weighted hold of the members answered on its night and its edge after its round trip, each worked by hand; a
// night run again writing what it wrote; and no source a rule's version pins one 22.9 writes.
// see: Quality at a fair price holds the companies the scorecard rates strongest that are not dear against their own history, reshuffled on each month's first session
// see: A quality holding is sold at a reshuffle where it no longer qualifies and at its last close as a member, and is bought at the next open
// see: A quality holding's edge is its return less an equal-weighted hold of its index's members over the same sessions, after its round trip
public partial class FixtureExpectations
{
    // The rows 22.9 adds that this check reaches: section 17's starting settings and section 18's two.
    internal static readonly string[] QualityClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Quality at a fair price's starting settings"),
        CheckReach.Key(Scope.FailureTable, "A member the quality family cannot score or value"),
        CheckReach.Key(Scope.FailureTable, "A quality buy whose next session is not stored"),
    ];

    // The floor read as a share of the checks that applied, never under five read; the valuation at its own median and
    // in its cheapest third; and the trend, each on both sides of its threshold and exactly at it.
    [Fact]
    public void TheQualityRulesFloorValuationAndTrendAreWorkedByHandOnBothSidesOfEachThreshold()
    {
        var starting = QualityRule.Starting;

        // Seven in nine is 63 against 7 times 9, at the floor; six in nine is 54, under it. Read as a share: four of five
        // is 36 against 35, over it, and three of five 27, under it; seven of eight, a financial company's, is 63 against
        // 56 and six of eight 54, under it. Four read gives no word whatever it passed.
        Assert.Equal(new QualityAnswer(true, null), QualityRule.Answer(7, 9, 0.5, null, starting));
        Assert.Equal(new QualityAnswer(false, QualityRule.UnderTheFloor), QualityRule.Answer(6, 9, 0.9, null, starting));
        Assert.True(QualityRule.Answer(4, 5, 0.9, null, starting).Passed);
        Assert.Equal(QualityRule.UnderTheFloor, QualityRule.Answer(3, 5, 0.9, null, starting).Reason);
        Assert.True(QualityRule.Answer(7, 8, 0.9, null, starting).Passed);
        Assert.Equal(QualityRule.UnderTheFloor, QualityRule.Answer(6, 8, 0.9, null, starting).Reason);
        Assert.Equal(QualityRule.NoWord, QualityRule.Answer(4, 4, 0.9, null, starting).Reason);

        // At its own median, a place of a half, it qualifies, and a hair under it is dearer than its median; no place read
        // qualifies nothing while the valuation is read, and everything when it is not.
        Assert.True(QualityRule.Answer(9, 9, 0.5, null, starting).Passed);
        Assert.Equal(QualityRule.TooDear, QualityRule.Answer(9, 9, 0.4999, null, starting).Reason);
        Assert.Equal(QualityRule.NotValued, QualityRule.Answer(9, 9, null, null, starting).Reason);
        Assert.True(QualityRule.Answer(9, 9, null, null, starting with { Valuation = QualityValuation.Off }).Passed);

        // In its cheapest third at two thirds and not a hair under; the trend read only where the setting reads it.
        var cheapest = starting with { Valuation = QualityValuation.CheapestThird };

        Assert.True(QualityRule.Answer(9, 9, 2.0 / 3, null, cheapest).Passed);
        Assert.Equal(QualityRule.NotCheapest, QualityRule.Answer(9, 9, 0.666, null, cheapest).Reason);
        Assert.Equal(QualityRule.UnderTheAverage, QualityRule.Answer(9, 9, 0.9, false, starting with { Trend = true }).Reason);
        Assert.Equal(QualityRule.UnderTheAverage, QualityRule.Answer(9, 9, 0.9, null, starting with { Trend = true }).Reason);
        Assert.True(QualityRule.Answer(9, 9, 0.9, true, starting with { Trend = true }).Passed);
        Assert.True(QualityRule.Answer(9, 9, 0.9, false, starting).Passed);
    }

    // The order: the score's share, the higher first, a financial company's seven of seven before eight of nine; then the
    // place, the cheaper first, none last; then the ticker.
    [Fact]
    public void TheQualityOrderIsTheScoresShareThenThePlaceThenTheTicker()
    {
        (int, int, double?, string)[] members =
        [
            (8, 9, 0.9, "AAA"),
            (7, 7, 0.6, "BBB"),
            (8, 9, 0.95, "CCC"),
            (8, 9, null, "DDD"),
            (8, 9, 0.9, "ABA"),
        ];

        Assert.Equal(["BBB", "CCC", "AAA", "ABA", "DDD"], QualityRule.Ranked(members, one => one).Select(one => one.Item4));
    }

    // A night reshuffles where it is the family's first or the first it reads in a month; the next reshuffle is the next
    // session on the exchange's calendar in a month none was read in.
    [Fact]
    public void AQualityReshuffleFallsOnTheFirstNightAndTheFirstNightOfEachMonthAfter()
    {
        Assert.True(QualityRule.Reshuffles(new DateOnly(2026, 10, 26), null));
        Assert.False(QualityRule.Reshuffles(new DateOnly(2026, 10, 27), new DateOnly(2026, 10, 26)));
        Assert.True(QualityRule.Reshuffles(new DateOnly(2026, 11, 2), new DateOnly(2026, 10, 26)));
        Assert.True(QualityRule.Reshuffles(new DateOnly(2026, 11, 3), new DateOnly(2026, 10, 26)));

        // From the first book on Monday 26 October 2026, the next is Monday 2 November, the month's first session, and from
        // that the next is Tuesday 1 December.
        Assert.Equal(new DateOnly(2026, 11, 2), QualityRule.NextReshuffle(new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 26)));
        Assert.Equal(new DateOnly(2026, 11, 2), QualityRule.NextReshuffle(new DateOnly(2026, 10, 30), new DateOnly(2026, 10, 26)));
        Assert.Equal(new DateOnly(2026, 12, 1), QualityRule.NextReshuffle(new DateOnly(2026, 11, 2), new DateOnly(2026, 11, 2)));
    }

    const string QualityFirst = "2026-10-26";
    const string QualityNext = "2026-10-27";
    const string QualityReshuffle = "2026-11-02";

    // Twelve S&P 400 members, Q01 to Q12, Q05 leaving the index on 2 November. Each closes at 100 plus its number on
    // 26 October, opens a dollar above that close on 27 October and closes two above it, and closes four above it on
    // 2 November, but Q03, which falls to 100. Q10 holds no bar on 27 October. Q01 to Q05 score 8 of 9 and Q06 to Q11 7 of
    // 9, each a place of 0.95 less a hundredth its number, and Q12 6 of 9; on 2 November Q03 scores 6 of 9.
    static TemporaryStore QualityStore()
    {
        var store = new TemporaryStore().Migrated();
        var at = LedgerReadings.IndexOf("value_place");

        for (var number = 1; number <= 12; number++)
        {
            var ticker = FormattableString.Invariant($"Q{number:00}");
            var close = 100m + number;

            store.Execute(FormattableString.Invariant($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', {(number == 5 ? "'2026-11-02'" : "NULL")}, '2024-01-02T00:00:00Z');"));
            store.Execute(FormattableString.Invariant($"INSERT INTO member_reading (index_code, session_date, ticker, company_value) VALUES ('MID', '{QualityFirst}', '{ticker}', '3000000000');"));
            QualityBar(store, ticker, QualityFirst, close, close);

            foreach (var night in new[] { QualityFirst, QualityNext, QualityReshuffle })
            {
                var passed = number == 12 || (number == 3 && night == QualityReshuffle) ? 6 : number <= 5 ? 8 : 7;
                var readings = Enumerable.Repeat<double?>(null, LedgerReadings.Count).ToArray();

                readings[at] = 0.95 - (0.01 * number);
                store.Execute(FormattableString.Invariant($"INSERT INTO fundamental_score (index_code, session_date, ticker, financial, passed, applied, checks) VALUES ('MID', '{night}', '{ticker}', 0, {passed}, 9, '[]');"));
                store.Execute(FormattableString.Invariant($"INSERT INTO ledger_reading (index_code, session_date, ticker, readings, pin) VALUES ('MID', '{night}', '{ticker}', '{System.Text.Json.JsonSerializer.Serialize(readings)}', 'test');"));
            }
        }

        return store;
    }

    static void QualityBar(TemporaryStore store, string ticker, string session, decimal open, decimal close) =>
        store.Execute(FormattableString.Invariant(
            $"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('{ticker}', '{session}', '{open}', '{Math.Max(open, close)}', '{Math.Min(open, close)}', '{close}', 1000, 'test', '{session}T21:00:00Z', '{close}');"));

    static IClock QualityClock(string night) =>
        FixedClock.At(DateTimeOffset.ParseExact(night, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddHours(23), SessionZones.UnitedStates);

    static double QualityDouble(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return Convert.ToDouble(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    static async Task QualityNightsAsync(TemporaryStore store, int nights)
    {
        await new QualityFamily(QualityClock(QualityFirst), store.DatabaseFile).RunAsync("quality-1");

        if (nights < 2)
        {
            return;
        }

        for (var number = 1; number <= 12; number++)
        {
            if (number != 10)
            {
                QualityBar(store, FormattableString.Invariant($"Q{number:00}"), QualityNext, 101m + number, 102m + number);
            }
        }

        await new QualityFamily(QualityClock(QualityNext), store.DatabaseFile).RunAsync("quality-2");

        if (nights < 3)
        {
            return;
        }

        for (var number = 1; number <= 12; number++)
        {
            QualityBar(store, FormattableString.Invariant($"Q{number:00}"), QualityReshuffle, 102m + number, number == 3 ? 100m : 104m + number);
        }

        await new QualityFamily(QualityClock(QualityReshuffle), store.DatabaseFile).RunAsync("quality-3");
    }

    // The first night chooses the ten the ranking puts first at its close: Q01 to Q05 at 8 of 9 by place, then Q06 to Q10
    // at 7 of 9, Q11 next in line and Q12 under the floor; none is bought until the session after.
    [Fact]
    public async Task TheQualityFamilysFirstNightChoosesTheTopTenAtItsCloseAndBuysNoneBeforeTheNextOpen()
    {
        using var store = QualityStore();

        await QualityNightsAsync(store, 1);

        Assert.Equal(
            ["Q01|101|null|open", "Q02|102|null|open", "Q03|103|null|open", "Q04|104|null|open", "Q05|105|null|open", "Q06|106|null|open", "Q07|107|null|open", "Q08|108|null|open", "Q09|109|null|open", "Q10|110|null|open"],
            FamilyRows(store, $"SELECT ticker, buy_close, bought_on, end_reason FROM quality_holding WHERE index_code = 'MID' AND chosen_on = '{QualityFirst}' ORDER BY ticker;"));
        Assert.Equal(
            ["Q11|1|11|7|9|null", $"Q12|0|null|6|9|{QualityRule.UnderTheFloor}"],
            FamilyRows(store, $"SELECT ticker, passed, place, passed_checks, applied, reason FROM quality_result WHERE index_code = 'MID' AND session_date = '{QualityFirst}' AND ticker IN ('Q11', 'Q12') ORDER BY ticker;"));
        Assert.Equal(["12|12|11|10|1"], FamilyRows(store, $"SELECT members, scored, qualified, held, reshuffle FROM quality_night WHERE index_code = 'MID' AND session_date = '{QualityFirst}';"));
    }

    // The session after, each holding is bought at its open: Q01 at 102 over its close of 101, a gap of a hundred-and-first;
    // Q10, whose stock holds no bar that session, waits; and the benchmark's gap is the mean of the members' own, each a
    // dollar over its close, over the eleven holding an open.
    [Fact]
    public async Task AQualityHoldingIsBoughtAtTheNextOpenAndOneWhoseNextSessionIsNotStoredWaits()
    {
        using var store = QualityStore();

        await QualityNightsAsync(store, 2);

        Assert.Equal([$"{QualityNext}|102"], FamilyRows(store, "SELECT bought_on, fill FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q01';"));
        Assert.Equal(1.0 / 101, QualityDouble(store, "SELECT gap FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q01';"), 12);

        var benchGap = Enumerable.Range(1, 12).Where(number => number != 10).Average(number => 1.0 / (100 + number));

        Assert.Equal(benchGap, QualityDouble(store, "SELECT bench_gap FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q01';"), 12);
        Assert.Equal(["null|null|open"], FamilyRows(store, "SELECT bought_on, fill, end_reason FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q10';"));
        Assert.Equal(["0"], FamilyRows(store, $"SELECT reshuffle FROM quality_night WHERE index_code = 'MID' AND session_date = '{QualityNext}';"));
        Assert.Equal((102.0 + 1) / (100 + 1), QualityDouble(store, $"SELECT carried FROM quality_result WHERE index_code = 'MID' AND session_date = '{QualityNext}' AND ticker = 'Q01';"), 12);
    }

    // On 2 November, the month's first night: Q03's score fell to 6 of 9, so it is sold at that close; Q05 left the index
    // that day, so it is sold at its last close as a member, 107 on 27 October; Q10 is bought at the open of the first
    // session after its choice the store holds; Q01 still qualifies and is kept; and the two places left go down the
    // ranking to Q11, the one qualifying stock not held.
    [Fact]
    public async Task TheMonthsFirstNightSellsAHoldingThatNoLongerQualifiesKeepsOneThatDoesAndFillsThePlacesDownTheRanking()
    {
        using var store = QualityStore();

        await QualityNightsAsync(store, 3);

        Assert.Equal(
            [$"Q03|{QualityReshuffle}|100|{QualityFamily.NoLongerQualifies}", $"Q05|{QualityNext}|107|{QualityFamily.LeftTheIndex}"],
            FamilyRows(store, "SELECT ticker, ended_on, sale, end_reason FROM quality_holding WHERE index_code = 'MID' AND end_reason <> 'open' ORDER BY ticker;"));
        Assert.Equal(
            ["Q01", "Q02", "Q04", "Q06", "Q07", "Q08", "Q09", "Q10", "Q11"],
            FamilyRows(store, "SELECT ticker FROM quality_holding WHERE index_code = 'MID' AND end_reason = 'open' ORDER BY ticker;"));
        Assert.Equal([$"{QualityReshuffle}|115"], FamilyRows(store, "SELECT chosen_on, buy_close FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q11';"));
        Assert.Equal([$"{QualityReshuffle}|112"], FamilyRows(store, "SELECT bought_on, fill FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q10';"));
        Assert.Equal(["11|9|1"], FamilyRows(store, $"SELECT members, held, reshuffle FROM quality_night WHERE index_code = 'MID' AND session_date = '{QualityReshuffle}';"));
    }

    // A member whose scorecard read four checks is answered with no word and one whose valuation the ledger read none for is
    // answered as not valued, each stored with its reason, and the first night chooses neither: the nine others qualifying.
    [Fact]
    public async Task AMemberTheQualityFamilyCannotScoreOrValueIsStoredWithItsReasonAndNeverChosen()
    {
        using var store = QualityStore();
        var unread = System.Text.Json.JsonSerializer.Serialize(Enumerable.Repeat<double?>(null, LedgerReadings.Count));

        store.Execute($"UPDATE fundamental_score SET passed = 4, applied = 4 WHERE ticker = 'Q02' AND session_date = '{QualityFirst}';");
        store.Execute($"UPDATE ledger_reading SET readings = '{unread}' WHERE ticker = 'Q07' AND session_date = '{QualityFirst}';");

        await QualityNightsAsync(store, 1);

        Assert.Equal(
            [$"Q02|0|4|4|{QualityRule.NoWord}", $"Q07|0|7|9|{QualityRule.NotValued}"],
            FamilyRows(store, $"SELECT ticker, passed, passed_checks, applied, reason FROM quality_result WHERE index_code = 'MID' AND session_date = '{QualityFirst}' AND ticker IN ('Q02', 'Q07') ORDER BY ticker;"));
        Assert.Equal(
            ["Q01", "Q03", "Q04", "Q05", "Q06", "Q08", "Q09", "Q10", "Q11"],
            FamilyRows(store, $"SELECT ticker FROM quality_holding WHERE index_code = 'MID' AND chosen_on = '{QualityFirst}' ORDER BY ticker;"));
    }

    // A holding whose stock holds no close on a reshuffle night is not answered that night and is kept, carried no further
    // until its close arrives, while the one whose score fell is sold as before.
    [Fact]
    public async Task AHoldingWithNoCloseStoredOnAReshuffleNightIsNotAnsweredAndIsKept()
    {
        using var store = QualityStore();

        await QualityNightsAsync(store, 2);

        for (var number = 1; number <= 12; number++)
        {
            if (number != 4)
            {
                QualityBar(store, FormattableString.Invariant($"Q{number:00}"), QualityReshuffle, 102m + number, number == 3 ? 100m : 104m + number);
            }
        }

        await new QualityFamily(QualityClock(QualityReshuffle), store.DatabaseFile).RunAsync("quality-3");

        Assert.Equal([$"open|{QualityNext}"], FamilyRows(store, "SELECT end_reason, through FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q04';"));
        Assert.Equal(["0"], FamilyRows(store, $"SELECT COUNT(*) FROM quality_result WHERE index_code = 'MID' AND session_date = '{QualityReshuffle}' AND ticker = 'Q04';"));
        Assert.Equal([QualityFamily.NoLongerQualifies], FamilyRows(store, "SELECT end_reason FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q03';"));
        Assert.Equal(["9"], FamilyRows(store, $"SELECT held FROM quality_night WHERE index_code = 'MID' AND session_date = '{QualityReshuffle}';"));
    }

    // Q03's result, benchmark, round trip and edge, worked by hand. Bought at 104 on its close of 103 and sold at 100: its
    // growth 100 over 103 less one less its gap of a hundred-and-third, (100 - 104) / 103. Its benchmark is the members
    // answered on 26 October, each its growth to 2 November less one less its own gap: four over its close for most, Q03
    // to 100, Q05's to its last answer on 27 October, Q10's from 26 October; less the mean gap of the eleven holding an
    // open. Its round trip is half the published spread of a company over $2 billion at a price over $40, 0.072 per cent,
    // at 104 and at 100, over 104. Its edge is its result less its benchmark, in points, less its round trip.
    [Fact]
    public async Task AQualityHoldingsResultBenchmarkRoundTripAndEdgeAreWorkedByHand()
    {
        using var store = QualityStore();

        await QualityNightsAsync(store, 3);

        var result = (100.0 - 104) / 103;
        var growth = Enumerable.Range(1, 12).Average(number => number switch
        {
            3 => 100.0 / 103,
            5 => 107.0 / 105,
            _ => (104.0 + number) / (100 + number),
        });
        var benchGap = Enumerable.Range(1, 12).Where(number => number != 10).Average(number => 1.0 / (100 + number));
        var benchmark = growth - 1.0 - benchGap;
        var trip = ((0.072 / 200 * 104) + (0.072 / 200 * 100)) / 104 * 100;

        // The figures the fixture's expectations derive by hand, the same arithmetic, over the nights it names.
        Assert.Equal(QualityFirst, Expected("quality").GetProperty("first").GetString());
        Assert.Equal(QualityReshuffle, Expected("quality").GetProperty("reshuffle").GetString());

        var expected = Expected("quality").GetProperty("q03");

        Assert.Equal(expected.GetProperty("result").GetDouble(), result, 12);
        Assert.Equal(expected.GetProperty("benchmark").GetDouble(), benchmark, 12);
        Assert.Equal(expected.GetProperty("cost").GetDouble(), trip, 12);
        Assert.Equal(expected.GetProperty("edge").GetDouble(), ((result - benchmark) * 100) - trip, 10);

        Assert.Equal(expected.GetProperty("result").GetDouble(), QualityDouble(store, "SELECT result FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q03';"), 10);
        Assert.Equal(expected.GetProperty("benchmark").GetDouble(), QualityDouble(store, "SELECT benchmark FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q03';"), 10);
        Assert.Equal(expected.GetProperty("cost").GetDouble(), QualityDouble(store, "SELECT cost FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q03';"), 10);
        Assert.Equal(expected.GetProperty("edge").GetDouble(), QualityDouble(store, "SELECT edge FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q03';"), 10);
        Assert.Equal(Expected("quality").GetProperty("q05Result").GetDouble(), QualityDouble(store, "SELECT result FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q05';"), 10);
        Assert.Equal(Expected("quality").GetProperty("q01Gap").GetDouble(), QualityDouble(store, "SELECT gap FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q01';"), 12);

        // Q05, sold on 27 October at 107 on its buy of 106: a result of a hundred-and-fifth, and its benchmark each
        // member's growth to that night less one less its gap, two over its close less one, so the mean of a hundredth-odd
        // each, Q10's none since it held no answer on that night past its first.
        var leftGrowth = Enumerable.Range(1, 12).Average(number => number == 10 ? 1.0 : (102.0 + number) / (100 + number));

        Assert.Equal(1.0 / 105, QualityDouble(store, "SELECT result FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q05';"), 10);
        Assert.Equal(leftGrowth - 1.0 - benchGap, QualityDouble(store, "SELECT benchmark FROM quality_holding WHERE index_code = 'MID' AND ticker = 'Q05';"), 10);
    }

    // A night run again writes what it wrote: the holdings it sold sold once, the stock it chose chosen once, its answers
    // and its row its own.
    [Fact]
    public async Task AQualityNightRunAgainWritesWhatItWrote()
    {
        using var store = QualityStore();

        await QualityNightsAsync(store, 3);

        var before = FamilyRows(store, "SELECT * FROM quality_holding ORDER BY ticker, chosen_on;").Concat(FamilyRows(store, "SELECT * FROM quality_result ORDER BY session_date, ticker;")).ToArray();

        await new QualityFamily(QualityClock(QualityReshuffle), store.DatabaseFile).RunAsync("quality-3-again");

        Assert.Equal(before, FamilyRows(store, "SELECT * FROM quality_holding ORDER BY ticker, chosen_on;").Concat(FamilyRows(store, "SELECT * FROM quality_result ORDER BY session_date, ticker;")));
        Assert.Equal(["3"], FamilyRows(store, "SELECT COUNT(*) FROM quality_night WHERE index_code = 'MID';"));
    }

    // The sources 22.9 writes or edits.
    static readonly string[] QualitySources =
    [
        "src/EquityBrief.Core/Families/QualityRule.cs",
        "src/EquityBrief.Worker/Quality/QualityFamily.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Quality.cs",
        "src/EquityBrief.Api/Reading/TonightScreen.Quality.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Quality.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/RunScreen.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/App/SinglePageApp.Tonight.cs",
        "src/EquityBrief.Web/App/Stylesheet.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Families.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Tonight.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.TonightTop.cs",
    ];

    // No source a rule's version pins is one 22.9 writes, so no existing rule's version moves, the reader shown to find a
    // source a rule pins. From 22.11 the quality rules pin the family's rule and its step, being rules of it, and no rule
    // registered before them did.
    // see: A quality rule is registered as the family on its index and kept by the family's own step alone
    [Fact]
    public void NoSourceARulesVersionPinsIsOne22Point9Writes()
    {
        var pinned = EquityBrief.Core.Candidates.CandidateEvaluators.All
            .Where(evaluator => evaluator is not EquityBrief.Core.Candidates.QualityCandidate)
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(EquityBrief.Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(EquityBrief.Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(QualitySources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(QualitySources.Intersect(pinned, StringComparer.Ordinal));
        Assert.NotEmpty(new[] { "src/EquityBrief.Worker/Indices/IndexFamilies.cs" }.Intersect(pinned, StringComparer.Ordinal));
    }
}
