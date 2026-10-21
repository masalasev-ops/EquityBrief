using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.11's ruling of the evening of 2026-10-20, clause 2 as amended: on a night whose store's year
// holds fewer sessions than a reading needs, the S&P 400 breakout's year's high and the sector heavyweights' closes read
// the sessions before the year from the pulled history at the store's scale, and a member the pulled history holds no
// session for reads none and is named; each worked by hand over a night holding 251 sessions.
// see: A night's reading needing more closes than the store's year holds reads the sessions before it from the pulled history at the store's scale
public partial class FixtureExpectations
{
    static readonly DateOnly BeforeNight = new(2026, 10, 2);

    // The exchange's sessions to the night, the given count of the newest.
    static DateOnly[] BeforeSessions(int count) =>
        [.. ExchangeClosures.SessionsBetween(BeforeNight.AddDays(-400), BeforeNight.AddDays(1)).TakeLast(count)];

    // A member's bar at its place among the 252 sessions to the night, the shape the index families' store gives a year:
    // highs of 100 and closes of 99 for the oldest 211, highs of 101 for the twenty after, the twenty after those at
    // ranges half as wide, and the night closing at 102 on twice the million shares a day.
    static FamilyBar BeforeBar(DateOnly session, int at) => at switch
    {
        < 211 => new FamilyBar(session, 100m, 98m, 99m, 1_000_000),
        < 231 => new FamilyBar(session, 101m, 99m, 100m, 1_000_000),
        < 251 => new FamilyBar(session, 100.5m, 99.5m, 100m, 1_000_000),
        _ => new FamilyBar(session, 102.5m, 101m, 102m, 2_000_000),
    };

    static string PulledBar(string ticker, DateOnly session, decimal high, decimal low, decimal close) =>
        FormattableString.Invariant($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('{ticker}', '{Day(session)}', '{close}', '{high}', '{low}', '{close}', '{close}', 1000000, 'pull-before-the-year');");

    // Three S&P 400 members over the 251 sessions to 2026-10-02 the store's year holds, the session before it held by the
    // pull alone, each with four profitable quarters, and the S&P 500's breadth of a half on the night. The pull holds IA's
    // and IB's bars at twice the store's prices, the first stored session at a close of 198 against the store's 99, and
    // the session before it at highs of 203 and 206; ID it holds none of.
    [Fact]
    public async Task TheSAndP400BreakoutsYearsHighOverANightHolding251SessionsReadsTheSessionBeforeFromThePulledHistoryAtTheStoresScale()
    {
        using var store = new TemporaryStore().Migrated();
        var sessions = BeforeSessions(252);

        foreach (var ticker in new[] { "IA", "IB", "ID" })
        {
            StoreYear(store, ticker, [.. sessions.Select((session, at) => BeforeBar(session, at)).Skip(1)]);
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
            Quarters(store, ticker, 10m, 10m, 10m, 10m);
        }

        store.Execute(
            "INSERT INTO market_reading (session_date, members, counted, above, breadth, counted_context, above_context, breadth_context, volume_counted, median_volume_ratio) "
            + $"VALUES ('{Day(BeforeNight)}', 2, 2, 1, 0.5, 2, 1, 0.5, 2, 1.0);");

        foreach (var (ticker, high) in new[] { ("IA", 203m), ("IB", 206m) })
        {
            store.Execute(PulledBar(ticker, sessions[0], high, 196m, 198m));
            store.Execute(PulledBar(ticker, sessions[1], 200m, 196m, 198m));
        }

        Assert.Equal(251, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));

        var frozenAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

        await FrozenRegister(store, frozenAt, BreakoutRule.Name, FrozenBreakout());

        var register = await new CandidateRegistrar(FixedClock.At(frozenAt, SessionZones.UnitedStates), store.DatabaseFile).RowsAsync();

        await new IndexFamilies(FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync("night-before-the-year", default, register, frozenAt.AddHours(1));

        // The frozen breakout's high over the 251 sessions before the night: the store holds 250 of them, its highest 101,
        // and the pull the one before at the store's scale, the store's 99 over the pull's 198 a half, IA's 203 reading
        // 101.5 and IB's 206 reading 103. IA's close of 102 stands above its year's high on twice its volume and breaks
        // out; IB's stands under it; ID reads no year's high and lists nothing.
        Assert.Equal(
            ["IA|1", "IB|0", "ID|0"],
            FamilyRows(store, $"SELECT ticker, passed FROM index_family_result WHERE index_code = 'MID' AND family = 'breakout' AND session_date = '{Day(BeforeNight)}' ORDER BY ticker;"));
        Assert.Contains(
            "the store's year holds 251 of the 252 sessions the year's high and the heavyweights' closes read, the sessions before it read from the pulled history for 2 member(s), which holds too few for 1, each reading none of them: ID",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'index-families';"),
            StringComparison.Ordinal);
    }

    // The S&P 500's heavyweights over the store the wait reads, its year to Monday 2026-11-02 holding 251 sessions from
    // 2025-11-03, one short of the 252 the twelve-month return reads: the pull holds W1's and W2's closes at twice the
    // store's on 2025-11-03 and on 2025-10-31, the session before it, and none of W3's.
    [Fact]
    public async Task TheSAndP500sHeavyweightsReadTheCloseBeforeAYearOf251SessionsFromThePulledHistoryAndRebalanceInPlaceOfWaiting()
    {
        using var store = WaitStore(new DateOnly(2025, 9, 2), new DateOnly(2026, 9, 30));

        var (_, first) = await WaitNight(store, new DateOnly(2026, 10, 1), "night-first");

        Assert.Equal(["W1"], first.Entered);

        WaitBars(store, new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 30));

        var monday = new DateOnly(2026, 11, 2);
        var before = new DateOnly(2025, 10, 31);
        var firstStored = new DateOnly(2025, 11, 3);

        foreach (var ticker in new[] { "W1", "W2" })
        {
            store.Execute(PulledBar(ticker, before, 2 * WaitClose(ticker, before), 2 * WaitClose(ticker, before), 2 * WaitClose(ticker, before)));
            store.Execute(PulledBar(ticker, firstStored, 2 * WaitClose(ticker, firstStored), 2 * WaitClose(ticker, firstStored), 2 * WaitClose(ticker, firstStored)));
        }

        var (cut, read) = await WaitNight(store, monday, "night-before-the-year");

        // The store keeps 251 sessions from 2025-11-03; the book reads W1's and W2's close of 2025-10-31 from the pull at the
        // store's scale and rebalances on the month's first session in place of waiting, W1 its leader and held.
        Assert.Equal(Day(firstStored), Text(store, "SELECT MIN(session_date) FROM bar;"));
        Assert.Equal(251, Scalar(store, "SELECT COUNT(DISTINCT session_date) FROM bar;"));
        Assert.Equal((true, 0, 0, 1), (read.Rebalanced, read.Entered.Count, read.Ended.Count, read.Held));

        // W1's return over the 251 sessions worked by hand, its close on the night over its close on 2025-10-31: the pull's
        // twice its close brought back by the store's close over the pull's on 2025-11-03, a half. W2 flat at 100 returns
        // nothing, and W3, which the pull holds no close for, reads none.
        var w1 = Statistic.FromRatio(WaitClose("W1", monday) / WaitClose("W1", before)) - 1.0;

        Assert.Equal(w1, double.Parse(Text(store, $"SELECT printf('%.12f', look_back) FROM heavyweight_night WHERE session_date = '{Day(monday)}' AND ticker = 'W1';"), CultureInfo.InvariantCulture), 9);
        Assert.Equal(0.0, double.Parse(Text(store, $"SELECT printf('%.12f', look_back) FROM heavyweight_night WHERE session_date = '{Day(monday)}' AND ticker = 'W2';"), CultureInfo.InvariantCulture), 9);
        Assert.Equal(["W1|1|read", "W2|0|read", "W3|0|none"], TextRows(store, $"SELECT ticker || '|' || leader || '|' || CASE WHEN look_back IS NULL THEN 'none' ELSE 'read' END FROM heavyweight_night WHERE session_date = '{Day(monday)}' ORDER BY ticker;"));
        Assert.EndsWith(
            "; the store's year holds 251 of the 252 sessions the heavyweights' closes read, the sessions before it read from the pulled history for 2 member(s), which holds too few for 1, each reading no return and no beta: W3",
            Text(store, "SELECT detail FROM run_log WHERE run_id = 'night-before-the-year' AND stage = 'heavyweights';"),
            StringComparison.Ordinal);
    }
}
