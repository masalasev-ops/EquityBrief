using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.1: the levels the S&P 400's and 600's breakouts and drift add to their second stage. A close
// in the top quarter of the day's range at its edge, and the entry on the first pullback within ten sessions worked by
// hand over constructed series for each family, the first session that does read alone.
// see: The S&P 400's and 600's breakouts and drift read an entry on the first pullback within ten sessions as a level of their second stage
public partial class FixtureExpectations
{
    [Fact]
    public void ACloseInTheTopQuarterOfTheDaysRangeIsThreeQuartersOfItAboveTheLow()
    {
        var day = new DateOnly(2019, 2, 1);

        // A range of 6 to 10: a close at 9 is three quarters up it, a cent under is not, and a day with no range closes
        // at its high.
        Assert.True(SweepEntries.InTheTopQuarter(new SweepBar(day, 10m, 6m, 9m, 1_000)));
        Assert.False(SweepEntries.InTheTopQuarter(new SweepBar(day, 10m, 6m, 8.99m, 1_000)));
        Assert.True(SweepEntries.InTheTopQuarter(new SweepBar(day, 7m, 7m, 7m, 1_000)));
    }

    [Fact]
    public void TheLastTwoYearsEdgeIsEachYearsWeightedByItsTradesAndAYearWithNoneCountsInNeither()
    {
        // Eight years, the last two 10 trades at 0.1 and 20 at -0.02: 1.0 less 0.4 over 30, 0.02; the sixth year's 100
        // trades at -5 are not read.
        int[] trades = [5, 5, 5, 5, 5, 100, 10, 20];
        double?[] edges = [1, 1, 1, 1, 1, -5, 0.1, -0.02];

        Assert.Equal(0.02, IndexSweepRunner.LastTwoYears(trades, edges)!.Value, 12);

        // The last year with no trades counts in neither the sum nor the count, so the edge is the seventh's alone, and
        // both empty read none.
        trades[7] = 0;
        edges[7] = null;

        Assert.Equal(0.1, IndexSweepRunner.LastTwoYears(trades, edges)!.Value, 12);

        trades[6] = 0;

        Assert.Null(IndexSweepRunner.LastTwoYears(trades, edges));
    }

    [Fact]
    public void ABreakoutsFirstPullbackToItsLevelWithinTenSessionsIsBoughtAtItsClose()
    {
        // Three members over the weekdays from 2018, a point either side of each close and rising a hundredth a session;
        // A breaks out 5 above the session before on 2019-02-01, rises a point a session for three and falls 6 on the
        // fourth, then holds. The level it broke is the session before's high, a point over that close; the fourth
        // session's low, a point under its close, sits on the level and the close a point above it.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var breakout = Array.IndexOf(calendar, new DateOnly(2019, 2, 1));
        var a = new decimal[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < breakout ? 100m + (0.01m * day)
                : day == breakout ? a[day - 1] + 5
                : day <= breakout + 3 ? a[day - 1] + 1
                : day == breakout + 4 ? a[day - 1] - 6
                : a[day - 1];
        }

        var rising = Enumerable.Range(0, calendar.Length).Select(day => 100m + (0.01m * day)).ToArray();
        SweepSeries[] series =
        [
            SweepColumns.Series(Constructed("A", calendar, a, 1_000), sessionAt),
            SweepColumns.Series(Constructed("B", calendar, rising, 1_000), sessionAt),
            SweepColumns.Series(Constructed("C", calendar, rising, 1_000), sessionAt),
        ];
        var sessions = SweepColumns.Sessions(series, calendar);
        var one = series[0];
        var level = BreakoutSweep.HighsBefore(one.Bars, BreakoutSweep.ShortHighSessions)[breakout];
        var listing = new FamilyListing(0, breakout, breakout, 3, 0, Statistic.FromPrice(a[breakout]), 0, double.NaN, 4, BreakoutRule.CapSessions, 2);

        Assert.Equal(Statistic.FromPrice(a[breakout - 1] + 1), level);

        var bought = Assert.Single(SweepEntries.BreakoutPullback(listing, one, level, 2, sessions));
        var fourth = breakout + 4;
        var risk = 2 * one.Atr[fourth];

        Assert.Equal((fourth, fourth), (bought.Bar, bought.Session));
        Assert.Equal(Statistic.FromPrice(a[breakout - 1] + 2), bought.Entry);
        Assert.Equal((bought.Entry - risk, risk, one.Atr[fourth]), (bought.Stop, bought.Trail, bought.Move));
        Assert.Equal((listing.Order, listing.Cap), (bought.Order, bought.Cap));

        // The market check closed on that session leaves no trade, and no later session is read; a level the lows never
        // reach within ten sessions, or none, buys nothing.
        var closed = sessions.Select((held, at) => at == fourth ? held with { Breadth = 0 } : held).ToArray();

        Assert.Empty(SweepEntries.BreakoutPullback(listing, one, level, 2, closed));
        Assert.Empty(SweepEntries.BreakoutPullback(listing, one, level - 10, 2, sessions));
        Assert.Empty(SweepEntries.BreakoutPullback(listing, one, double.NaN, 2, sessions));

        // Ten sessions are read and the eleventh is not: listed six sessions before the breakout, the fourth session after
        // it is the tenth after the listing and is bought, and listed seven before it is the eleventh.
        Assert.Single(SweepEntries.BreakoutPullback(listing with { Bar = fourth - 10 }, one, level, 2, sessions));
        Assert.Empty(SweepEntries.BreakoutPullback(listing with { Bar = fourth - 11 }, one, level, 2, sessions));
    }

    [Fact]
    public void ADriftsFirstPullbackAfterItsReactionWithinTenSessionsIsBoughtAtItsCloseWithTheReactionsLowItsStop()
    {
        // The drift's own constructed history falls under the reaction's low on its first lower close, so no trade.
        var (driftCalendar, driftSeries, driftReaction) = DriftHistory();
        var driftSessions = SweepColumns.Sessions(driftSeries, driftCalendar);
        var held = new FamilyListing(0, driftReaction, driftReaction, 10, 0, Statistic.FromPrice(driftSeries[0].Bars[driftReaction].Close), Statistic.FromPrice(driftSeries[0].Bars[driftReaction].Low), double.NaN, double.NaN, DriftRule.CapSessions, 2);

        Assert.Empty(SweepEntries.DriftPullback(held, driftSeries[0], driftReaction, 2.5, driftSessions));

        // Here A reacts 4 above the session before on 2019-02-01, rises 3 more and then falls 1, a close 2 above the
        // reaction's and 3 above its low: bought there, the stop still the reaction's low and the target the setting's
        // 2.5 times the new risk of 3 or a band nearer.
        var calendar = Weekdays(new DateOnly(2018, 1, 1), 360);
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var reaction = Array.IndexOf(calendar, new DateOnly(2019, 2, 1));
        var a = new decimal[calendar.Length];

        for (var day = 0; day < calendar.Length; day++)
        {
            a[day] = day < reaction ? 100m + (0.01m * day)
                : day == reaction ? a[day - 1] + 4
                : day == reaction + 1 ? a[day - 1] + 3
                : a[reaction] + 2;
        }

        var rising = Enumerable.Range(0, calendar.Length).Select(day => 100m + (0.01m * day)).ToArray();
        SweepSeries[] series =
        [
            SweepColumns.Series(Constructed("A", calendar, a, 1_000), sessionAt),
            SweepColumns.Series(Constructed("B", calendar, rising, 1_000), sessionAt),
            SweepColumns.Series(Constructed("C", calendar, rising, 1_000), sessionAt),
        ];
        var sessions = SweepColumns.Sessions(series, calendar);
        var stop = Statistic.FromPrice(a[reaction] - 1);
        var listing = new FamilyListing(0, reaction, reaction, 10, 0, Statistic.FromPrice(a[reaction]), stop, double.NaN, double.NaN, DriftRule.CapSessions, 2);
        var bought = Assert.Single(SweepEntries.DriftPullback(listing, series[0], reaction, 2.5, sessions));
        var entry = Statistic.FromPrice(a[reaction] + 2);

        Assert.Equal((reaction + 2, reaction + 2), (bought.Bar, bought.Session));
        Assert.Equal((entry, stop), (bought.Entry, bought.Stop));
        Assert.InRange(bought.Target, entry + 0.0001, entry + (2.5 * 3));
        Assert.Equal((listing.Order, listing.Cap), (bought.Order, bought.Cap));

        // A listing on the session after the reaction reads the same first fall, and the market check closed on it
        // leaves no trade with no later session read.
        Assert.Equal(reaction + 2, Assert.Single(SweepEntries.DriftPullback(listing with { Bar = reaction + 1, Session = reaction + 1 }, series[0], reaction, 2.5, sessions)).Bar);
        Assert.Empty(SweepEntries.DriftPullback(listing, series[0], reaction, 2.5, sessions.Select((one, at) => at == reaction + 2 ? one with { Breadth = 0 } : one).ToArray()));
    }
}
