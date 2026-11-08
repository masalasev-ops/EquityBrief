using System.Collections.Concurrent;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.8 correction: the long search walks the dip's own grid at its first stage through the same
// every-third-year procedure as the breakout's and the drift's, against its live rule, over a constructed grid of two
// settings worked by hand; and it names what blocks each pullback's search on each index.
// see: The long search walks the dip's own sweep's grid at its first stage and names what blocks each pullback's search
public partial class FixtureExpectations
{
    // A grid of two drops over 2005 to 2012, a trade every second session on each: the drop of 5 makes 1 risk over its benchmark on
    // every trade and the drop of 10 loses 1, the live rule at the drop of 10. Every fold from 2008 chooses the drop of 5 on
    // what ended before it, the proposal on all finished data is the drop of 5, and its trades a test year are the
    // drop of 5's; the folds search at 2008 and 2011 and carry between.
    [Fact]
    public void TheDipsOwnGridIsSearchedOverTheLongHistoryAgainstItsLiveRule()
    {
        var calendar = Enumerable.Range(0, (new DateOnly(2012, 12, 31).DayNumber - new DateOnly(2005, 1, 3).DayNumber) + 1)
            .Select(day => new DateOnly(2005, 1, 3).AddDays(day))
            .Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            .ToArray();
        var read = new LoopRead("GSPC", null!, null!, null!, null!, null!, null!, calendar, 0, LoopFolds.Of(calendar, calendar[^1], LongTest.PriceFirstTestYear), Long: true);
        var own = new TwoDrops(calendar);

        var searched = LongSearch.SearchOwn(read, own, [1], [], "the dip rule today", 2008, _ => { });

        Assert.Equal(DipRule.Name, searched.Family);
        Assert.Equal([0], searched.Proposal!);
        Assert.Equal("the dip rule at drop=5", searched.Words);
        Assert.Equal("the dip rule today", searched.Current);
        Assert.Equal([2008, 2011], searched.Folds.Where(fold => fold.Searched).Select(fold => fold.Year));
        Assert.All(searched.Folds, fold => Assert.Equal([0], fold.Chosen!));
        Assert.Equal(own.Trades(0).Count(trade => calendar[trade.Listing.Session].Year >= 2008), searched.Verdict.Units);
        Assert.True(searched.Verdict.Better > 0);

        Assert.Contains("the swing filter", LongSearch.PullbackBlocked("GSPC"), StringComparison.Ordinal);
        Assert.Contains("eight scored years", LongSearch.PullbackBlocked("MID"), StringComparison.Ordinal);
        Assert.Equal(LongSearch.PullbackBlocked("MID"), LongSearch.PullbackBlocked("SML"));
        Assert.Equal([DipRule.Name], LongSearch.SearchedOwn);
        Assert.DoesNotContain(DipRule.Name, LongSearch.StandingAlone);
    }

    // The dip's grid as the search reads it: two drops, each trade ended ten sessions after it lists.
    sealed class TwoDrops(DateOnly[] calendar) : OwnProcedures.IOwnSwing
    {
        public string Family => DipRule.Name;

        public FamilyGrid Grid { get; } = new([("drop", [5, 10])], [1]);

        public bool LastTwo => false;

        public bool Floored => false;

        public IReadOnlyList<DialLevel<FamilyListing>> Levels => [];

        public IReadOnlyList<FamilyTrade> Trades(int drop) =>
        [
            .. Enumerable.Range(0, calendar.Length - 10).Where(at => at % 2 == 0)
                .Select(at => new FamilyTrade(new FamilyListing(at % 7, at, at, 0, 0, 20, 18, double.NaN, double.NaN, 10), 0, drop == 0 ? 1.1 : -0.9, 0.1)),
        ];

        public IReadOnlyList<FamilyTrade> TradesUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together, ConcurrentDictionary<(int Name, int Session), int>? held = null)
        {
            var trades = Trades(setting[0]);

            foreach (var trade in trades)
            {
                held?.TryAdd((trade.Listing.Name, trade.Listing.Session), 10);
            }

            return trades;
        }

        public IReadOnlyList<FamilyTrade> NightsRule(ConcurrentDictionary<(int Name, int Session), int>? held = null) => TradesUnder([1], [], held);

        public int[][] Strongest(IReadOnlyList<(int[] Setting, FamilyFigures Figures)> read, int tradesHeld = 10) => [];
    }
}
