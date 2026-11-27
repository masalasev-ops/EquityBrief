using System.Globalization;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction on the operator's orders of 2026-11-27: the per-rule test over the long history
// orders a rule by the expected value of the setups like each listing over a pool of the long ledger to 2018 and the
// standing one from 2019, a ridge score fitted for each long fold on the family's finished setups of all three indices that
// ended before the fold's year, so the breakout ordered by it holds a verdict; a fold too few setups ended before stands
// with no score, and nothing that ended inside or after a fold's year is matched in it.
// see: The long test orders a rule by the expected value over the long ledger, a score fitted for each long fold
public partial class FixtureExpectations
{
    static readonly int RsiAt = LedgerReadings.All.ToList().FindIndex(reading => reading.Column == "rsi");

    static readonly int VolumeAt = LedgerReadings.All.ToList().FindIndex(reading => reading.Column == "volume_ratio");

    static void LongExpectedRow(Microsoft.Data.Sqlite.SqliteConnection connection, string table, string index, string ticker, DateOnly session, double result, int livePass = 1)
    {
        using var command = connection.CreateCommand();
        var day = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var ended = session.AddDays(5).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var rsi = 30 + (session.DayNumber % 40);
        var volume = 1 + (session.DayNumber % 7 / 10.0);

        command.CommandText = table == "long_setup"
            ? "INSERT INTO long_setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, cap, result, cost, edge, risk_moves, end, ended_on, settled, source, pin, last_end, last_settled, run_id, rsi, volume_ratio) VALUES ($index, 'breakout', $ticker, $day, 'r', $live, 1, '100', '95', 63, $result, 0, $result, 1, 'target', $ended, 1, 'long', 'p', 'target', 1, 'r', $rsi, $volume);"
            : "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, target, trail, cap, result, cost, edge, risk_moves, end, ended_on, settled, source, pin, rsi, volume_ratio) VALUES ($index, 'breakout', $ticker, $day, 'r', $live, 1, '100', '95', NULL, NULL, 63, $result, 0, $result, 1, 'target', $ended, 1, 'night', 'p', $rsi, $volume);";
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$day", day);
        command.Parameters.AddWithValue("$ended", ended);
        command.Parameters.AddWithValue("$live", livePass);
        command.Parameters.AddWithValue("$result", result);
        command.Parameters.AddWithValue("$rsi", rsi);
        command.Parameters.AddWithValue("$volume", volume);
        command.ExecuteNonQuery();
    }

    // A constructed S&P 600 breakout ledger: 5 setups of 2005 to 2007, fewer than the score learns on; then setups enough
    // for it from 2008 to 2014, each ending with +1 or +2 risks; then 40 of 2015 to 2018 and 10 of the standing ledger's from
    // 2019 each ending with -1,000 risks, so a matched one would pull the mean under 1. The standing ledger's setup of 2017,
    // one its live gates did not pass and the S&P 400's are left out of the S&P 600's pool, the S&P 400's read by the score.
    [Fact]
    public async Task TheLongTestsPoolReadsBothLedgersAndFitsAScoreForEachLongFoldWithEnoughEndedBefore()
    {
        using var store = new TemporaryStore().Migrated();
        var through = new DateOnly(2020, 12, 31);
        var enough = RidgeScore.MinimumRowsOf(new ScoreRows(0, through)) + 20;
        var calendar = Weekdays(new DateOnly(2005, 1, 3), through);
        var early = calendar.Where(day => day.Year <= 2007).ToArray();
        var middle = calendar.Where(day => day.Year is >= 2008 and <= 2014 && day < new DateOnly(2014, 12, 15)).ToArray();
        var late = calendar.Where(day => day.Year is >= 2015 and <= 2018).ToArray();
        var standing = calendar.Where(day => day.Year >= 2019).ToArray();

        using (var connection = store.Open())
        using (var transaction = connection.BeginTransaction())
        {
            for (var at = 0; at < 5; at++)
            {
                LongExpectedRow(connection, "long_setup", "SML", $"E{at}", early[at * 100], 1 + (at % 2));
            }

            for (var at = 0; at < enough; at++)
            {
                LongExpectedRow(connection, "long_setup", "SML", $"M{at}", middle[at * middle.Length / enough], 1 + (at % 2));
            }

            for (var at = 0; at < 40; at++)
            {
                LongExpectedRow(connection, "long_setup", "SML", $"L{at}", late[at * 20], -1000);
            }

            for (var at = 0; at < 10; at++)
            {
                LongExpectedRow(connection, "setup", "SML", $"S{at}", standing[at * 40], -1000);
            }

            LongExpectedRow(connection, "setup", "SML", "OLD", new DateOnly(2017, 6, 1), 5);
            LongExpectedRow(connection, "long_setup", "SML", "GATE", new DateOnly(2010, 6, 1), 5, livePass: 0);
            LongExpectedRow(connection, "long_setup", "MID", "MIDX", new DateOnly(2010, 6, 2), 1);
            transaction.Commit();
        }

        var folds = LoopFolds.Of(calendar, through, 2008);

        LikePool pool;

        using (var connection = store.Open())
        {
            pool = await LikePool.ReadLongAsync(connection, "SML", [Core.Families.BreakoutRule.Name], folds, through, CancellationToken.None);
        }

        // The S&P 600's live-gated setups of the long ledger and the standing one's from 2019: 5, enough, 40 and 10.
        Assert.Equal(5 + enough + 40 + 10, pool.Count);

        // 2008's fold learns on the 5 of 2005 to 2007 alone and holds no score; 2015's learns on enough and holds one.
        Assert.Null(pool.FoldOf(Core.Families.BreakoutRule.Name, 2008));
        Assert.NotNull(pool.FoldOf(Core.Families.BreakoutRule.Name, 2015));
        Assert.Null(pool.FoldOf(Core.Families.DriftRule.Name, 2015));

        // A listing of 2015 is matched among what ended before 2015 alone, each +1 or +2, so its figure stands from 1 to 2;
        // one setup ending at -1,000 among at most 250 matched would take it under 1.
        var readings = new double?[LedgerReadings.Count];

        readings[RsiAt] = 45;
        readings[VolumeAt] = 1.3;

        var expected = pool.ExpectedAt(Core.Families.BreakoutRule.Name, 2015, "LISTING", readings);

        Assert.NotNull(expected);
        Assert.InRange(expected!.Value, 1, 2);
        Assert.Null(pool.ExpectedAt(Core.Families.BreakoutRule.Name, 2008, "LISTING", readings));
    }

    // The families a long run's pool is read for: those a registered rule on the index orders by the expected value, the
    // S&P 400's breakout frozen with a variant ordered by it, and none on the S&P 400 frozen without one nor on an index
    // whose rules order by none.
    [Fact]
    public async Task TheLongTestsPoolIsReadForTheFamiliesARuleOnTheIndexOrdersByTheExpectedValue()
    {
        var frozenAt = new DateTimeOffset(2026, 10, 21, 11, 15, 0, TimeSpan.Zero);

        async Task<IReadOnlyList<Core.Candidates.RegisterRow>> FrozenAsync(TemporaryStore store, string variants)
        {
            Assert.Equal(0, (await RegisterVerbAt(store, frozenAt, RegisterVerb.IndexFamily, Core.Families.BreakoutRule.Name, "--index", "MID", MidBreakoutFrozen[0], MidBreakoutFrozen[1], "--variants", variants)).Code);
            Assert.Equal(0, (await RegisterVerbAt(store, frozenAt.AddMinutes(1), RegisterVerb.IndexFamily, Core.Families.QualityRule.Name, "--index", "GSPC", "--parameters", QualityFrozen)).Code);

            return await StandingRowsAsync(store, frozenAt.AddMinutes(2));
        }

        using var without = new TemporaryStore().Migrated();
        using var with = new TemporaryStore().Migrated();
        var none = await FrozenAsync(without, MidBreakoutFrozen[3]);
        var ordered = await FrozenAsync(with, RuleHooks.ExpectedOrderParameter + "=1");

        Assert.Empty(WalkForwardTester.ExpectedFamilies(none, "MID"));
        Assert.Equal([Core.Families.BreakoutRule.Name], WalkForwardTester.ExpectedFamilies(ordered, "MID"));
        Assert.Empty(WalkForwardTester.ExpectedFamilies(ordered, "GSPC"));
    }
}
