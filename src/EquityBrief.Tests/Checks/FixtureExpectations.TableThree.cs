using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction on the operator's orders of 2026-11-24 on the check of 4:40 pm: the table's years
// since the price stood this high read as the fraction of years the ledger reads, held to the ledger's own rebuild where both
// read the same history; the weekdays to the next report counted to each quarter's results 8-K, a preliminary left out,
// worked by hand from the sampled cases; and a rebuild over the ledger's members reading them on the sessions it names.
// see: The every-session table reads four readings at the traded price and three over a fixed length of history, the night's own
// see: The every-session table counts the weekdays to the next report from the results 8-K each quarter's 10-Q or 10-K closes
public partial class FixtureExpectations
{
    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

    // Over a history far shorter than the table's 2,792 sessions the two read the same bars, so on every session the table's
    // years since the price stood this high are the ledger's own, a fraction of a year and never cut to a whole number.
    [Fact]
    public void TheTablesYearsSinceThePriceStoodThisHighAreTheLedgersFractionWhereBothReadTheSameHistory()
    {
        var (calendar, inputs, market) = PointInTimeHistory();
        var fractions = 0;

        for (var at = 30; at < calendar.Count; at++)
        {
            var cut = LedgerPointInTime.Cut(inputs, calendar[at]);
            var cutMarket = LedgerPointInTime.Cut(market, calendar[at]);
            var ledger = LedgerPointInTime.Rebuilt(Worker.Indices.IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, "AAA")!;
            var table = LedgerPointInTime.Rebuilt(Worker.Indices.IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, "AAA", table: true)!;
            var column = LedgerReadings.IndexOf("highest_since_years");

            Assert.Equal(ledger[column], table[column]);
            fractions += table[column] is { } years && years != Math.Floor(years) ? 1 : 0;
        }

        Assert.True(fractions > 0, "No session read a fraction of a year.");
    }

    // From the sampled differences: SWKS filed a results 8-K on 8 Jan 2019 and another on 5 Feb, its 10-Q on 6 Feb, so the
    // first was a preliminary and the quarter's report the second, 21 weekdays after 7 Jan, as the provider's calendar holds;
    // WMT's results 8-K of 15 Aug 2024 is its report, the provider's 30 Aug being its 10-Q's day, so 20 Aug counts to the next
    // quarter's 8-K of 19 Nov, 65 weekdays on; and a code the filings hold nothing for reads the calendars' days whole. The calendars' days
    // before the first 8-K and after the report closing the last stand.
    [Fact]
    public void TheTableCountsTheWeekdaysToEachQuartersResults8KAPreliminaryLeftOut()
    {
        var swks = TableReadings.ReportDays([Day("2019-01-08"), Day("2019-02-05"), Day("2019-04-25")], [Day("2019-02-06"), Day("2019-05-03")], [Day("2018-11-01"), Day("2019-02-05"), Day("2019-03-01"), Day("2019-07-25")]);

        Assert.Equal([Day("2018-11-01"), Day("2019-02-05"), Day("2019-04-25"), Day("2019-07-25")], swks);
        Assert.Equal(21, LedgerSetups.EarningsSessions(swks, [Day("2019-01-07")], 0));

        var wmt = TableReadings.ReportDays([Day("2024-08-15"), Day("2024-11-19")], [Day("2024-08-30"), Day("2024-12-06")], [Day("2024-08-30"), Day("2024-12-06")]);

        Assert.Equal([Day("2024-08-15"), Day("2024-11-19")], wmt);
        Assert.Equal(24, LedgerSetups.EarningsSessions(wmt, [Day("2024-07-12")], 0));
        Assert.Equal(65, LedgerSetups.EarningsSessions(wmt, [Day("2024-08-20")], 0));

        Assert.Equal([Day("2024-08-30")], TableReadings.ReportDays([], [Day("2024-08-30")], [Day("2024-08-30")]));

        // The table's row reads the weekdays to the report days given: from Tuesday 3 Jan 2012 to Tuesday 10 Jan, five.
        SweepBar[] bars = [.. Enumerable.Range(0, 30).Select(day => Quiet(day, 20m + (day % 3), 1000, 20m + (day % 3)))];
        var sessionAt = bars.Select((bar, at) => (bar.Session, at)).ToDictionary(pair => pair.Session, pair => pair.at);
        var series = SweepColumns.Series(new SweepName("AAA", bars, [(null, null)], []), sessionAt);
        var members = new SweepBenchmark.Members([.. bars.Select(_ => new[] { 0 })], [.. bars.Select((_, at) => new[] { at })]);
        var readings = new double?[LedgerReadings.All.Count];

        TableOwnReadings.Apply(readings, new TradedRanks([series], members), 0, series, 0, [Day("2012-01-10")]);

        Assert.Equal(5.0, readings[LedgerReadings.IndexOf("earnings_sessions")]);
    }

    // A rebuild over the ledger's members reads a code a member on the last sessions where the set names it and on none where
    // it does not, and keeps the cut's own spans before them and on a session the set does not hold.
    [Fact]
    public void ARebuildOverTheLedgersMembersReadsThemOnTheSessionsItNames()
    {
        var (calendar, inputs, _) = PointInTimeHistory();
        var cut = LedgerPointInTime.Cut(inputs, calendar[40]);
        var set = new Dictionary<DateOnly, HashSet<string>> { [calendar[40]] = [inputs.Names[0].Ticker], [calendar[39]] = [] };
        var over = LedgerPointInTime.OverMembers(cut, set, 3);
        var first = over.Names[0];
        var other = over.Names[1];

        Assert.True(first.MemberOn(calendar[40]));
        Assert.False(first.MemberOn(calendar[39]));
        Assert.Equal(cut.Names[0].MemberOn(calendar[38]), first.MemberOn(calendar[38]));
        Assert.True(cut.Names[0].MemberOn(calendar[40]));
        Assert.True(cut.Names[1].MemberOn(calendar[40]));
        Assert.Equal(cut.Names[0].MemberOn(calendar[10]), first.MemberOn(calendar[10]));
        Assert.False(other.MemberOn(calendar[40]));
        Assert.Equal(cut.Names[1].MemberOn(calendar[10]), other.MemberOn(calendar[10]));
    }
}
