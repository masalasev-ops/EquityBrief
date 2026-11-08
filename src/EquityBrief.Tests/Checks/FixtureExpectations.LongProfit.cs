using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.8 correction: the profit check over the long history reads each filer's quarters off the SEC's
// facts as first filed, a fiscal fourth quarter the year less its first nine months, worked by hand; its first year is the
// first whose first session sees 90 in every 100 members read it; and a session before that year reads the floors alone.
// see: The profit check over the long history reads the long facts from the first year they cover 90 in every 100 members, the years before it unchecked
public partial class FixtureExpectations
{
    // Three quarters of 2009 at 10, 20 and -5 and the year at 40, filed as stated: the fourth is 40 less the 25 of nine
    // months, 15, first filed the day the year was. Four quarters filed before 2010-03-15 read the check, three do not.
    [Fact]
    public void TheLongQuartersAreReadAsFirstFiledWithTheFourthFromTheYear()
    {
        FiledFactRow Fact(DateOnly start, DateOnly end, decimal value, DateOnly filed) => new("NetIncomeLoss", start, end, value, filed, "10-Q", "a");

        FiledFactRow[] facts =
        [
            Fact(new(2009, 1, 1), new(2009, 3, 31), 10, new(2009, 5, 1)),
            Fact(new(2009, 4, 1), new(2009, 6, 30), 20, new(2009, 8, 1)),
            Fact(new(2009, 7, 1), new(2009, 9, 30), -5, new(2009, 11, 1)),
            Fact(new(2009, 1, 1), new(2009, 9, 30), 25, new(2009, 11, 1)),
            Fact(new(2009, 1, 1), new(2009, 12, 31), 40, new(2010, 3, 1)),
        ];

        var quarters = LongProfit.Quarters(facts);

        Assert.Equal([10m, 20m, -5m, 15m], quarters.Select(quarter => quarter.NetIncome!.Value));
        Assert.Equal(new DateOnly(2010, 3, 1), quarters[^1].FilingDate);
        Assert.True(LongProfit.Reads(quarters, new DateOnly(2010, 3, 15)));
        Assert.False(LongProfit.Reads(quarters, new DateOnly(2010, 2, 15)));
        Assert.True(MemberReadings.Profit(quarters, new DateOnly(2010, 3, 15)));
    }

    // 85 of 100 in 2010, 90 of 100 in 2011 and 95 in 2012: the check is read from 2011, and from no year where none reaches it.
    [Fact]
    public void TheProfitChecksFirstYearIsTheFirstNinetyInEveryHundredMembersRead()
    {
        Assert.Equal(2011, LongProfit.FirstCheckedYear([(2010, 100, 85), (2011, 100, 90), (2012, 100, 95)]));
        Assert.Equal(2012, LongProfit.FirstCheckedYear([(2011, 100, 89), (2012, 20, 18)]));
        Assert.Null(LongProfit.FirstCheckedYear([(2009, 100, 3), (2010, 0, 0)]));
    }

    // A member with no quarters clears the S&P 400's floors at 20 on a million shares: before the first checked session it
    // reads the floors alone and clears, from it the profit gate refuses it, and with no first session it is refused as before.
    [Fact]
    public void ASessionBeforeTheFirstCheckedYearReadsTheFloorsAlone()
    {
        var calendar = Enumerable.Range(0, 80).Select(at => new DateOnly(2010, 11, 1).AddDays(at)).ToArray();
        var bars = calendar.Select(session => new SweepBar(session, 21m, 19m, 20m, 1_000_000, 20m, 20m)).ToArray();
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = SweepColumns.Series(new SweepName("AAA", bars, [(null, null)], []), sessionAt);
        var late2010 = Array.FindIndex(calendar, day => day == new DateOnly(2010, 12, 30));
        var early2011 = Array.FindIndex(calendar, day => day == new DateOnly(2011, 1, 10));

        Assert.True(IndexSweepRunner.Clears("MID", series, late2010, [], checkedFrom: new DateOnly(2011, 1, 1)));
        Assert.False(IndexSweepRunner.Clears("MID", series, early2011, [], checkedFrom: new DateOnly(2011, 1, 1)));
        Assert.False(IndexSweepRunner.Clears("MID", series, late2010, []));
    }
}
