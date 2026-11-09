using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 23.7 correction: the long test lays out the S&P 400's and 600's own sweep readings and the dip's
// over the long history, reading the market series from the long pull with the standing pull's closes after it, worked over
// a constructed store; and no long run is held off the own readings or the dip any longer.
// see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
public partial class FixtureExpectations
{
    // SPY in the long pull for three sessions of 2005 and the standing pull holding the third again and two after it: the long
    // series reads the long pull's three and the standing pull's two after its last, the overlap read once off the long pull;
    // HYG held by the standing pull alone reads its own; a series neither holds is left out.
    [Fact]
    public async Task TheLongMarketSeriesReadTheLongPullThenTheStandingPullAfterIt()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES
                ('SPY', '2005-01-03', '100', '100', '100', '100', 'long-prices-1'),
                ('SPY', '2005-01-04', '101', '101', '101', '101', 'long-prices-1'),
                ('SPY', '2005-01-05', '102', '102', '102', '102', 'long-prices-1');
            INSERT INTO pulled_market_bar (series, session_date, open, high, low, close, pull) VALUES
                ('SPY', '2005-01-05', '999', '999', '999', '999', 'pull-1'),
                ('SPY', '2005-01-06', '103', '103', '103', '103', 'pull-1'),
                ('SPY', '2005-01-07', '104', '104', '104', '104', 'pull-1'),
                ('HYG', '2005-01-06', '50', '50', '50', '50', 'pull-1');");

        var series = await WalkForwardTester.LongSeriesAsync(new LongHistoryReader(store.DatabaseFile), new SweepHistory(store.DatabaseFile), ["SPY", "HYG", "IJR"], new DateOnly(2005, 1, 7));

        Assert.Equal(["SPY", "HYG"], series.Select(one => one.Series));
        Assert.Equal([100.0, 101.0, 102.0, 103.0, 104.0], series[0].Closes.Select(one => one.Close));
        Assert.Equal([50.0], series[1].Closes.Select(one => one.Close));
    }

    // The long run reads the dip and the own sweep readings where a rule needs them, as the standing run does: neither is held
    // off a long run any longer, and the own readings are handed the long series.
    [Fact]
    public void TheLongTestLaysOutTheOwnReadingsAndTheDipOverTheLongHistory()
    {
        var tester = File.ReadAllText(Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "Loop", "WalkForwardTester.cs"));

        Assert.DoesNotContain("!standingOnly && ReadsDip", tester, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!large && !standingOnly)", tester, StringComparison.Ordinal);
        Assert.Contains("OwnSwingRead.ReadAsync(read, await LargeReadAsync(), family, history, through, stopFloor, cancellation, seriesOf)", tester, StringComparison.Ordinal);
        Assert.Contains("OwnPullbackRead.ReadAsync(read, await LargeReadAsync(), history, through, output.WriteLine, cancellation, seriesOf)", tester, StringComparison.Ordinal);
        Assert.Contains("await WithIncomeAsync(read, longHistory, cancellation)", tester, StringComparison.Ordinal);
    }
}
