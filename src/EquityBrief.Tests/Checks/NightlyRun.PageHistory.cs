using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Indicators;
using EquityBrief.Worker.Moves;
using EquityBrief.Worker.Swings;

namespace EquityBrief.Tests.Checks;

// nightly-run, 18.3: the chart averages step reads the typical earnings moves and the long views after the chart's
// averages and before the swings, a row of each a name holding bars with no model call and no request, and a reader that
// cannot finish keeps the rows an earlier night wrote, names why on its own row and lets the night go on to the rules.
// see: A stock's typical earnings move is the median size of its reaction over every print the store holds, read through the history pulled before its year
public partial class NightlyRun
{
    [Fact]
    public async Task ThePageHistoryReaderRunsAfterTheChartsAveragesAndBeforeTheSwingsAndWritesARowOfEachTableANameWithNoCall()
    {
        using var store = new TemporaryStore();

        var (code, _, error) = await NightAsync(store, runId: "night-page-history");

        Assert.True(code == 0, error);

        var stages = RunLog(store, "night-page-history").Select(row => row.Stage).ToList();
        var averages = stages.IndexOf(ChartAverager.Stage);
        var history = stages.IndexOf(PageHistoryReader.Stage);

        Assert.True(averages >= 0 && history == averages + 1, string.Join(", ", stages));
        Assert.True(stages.IndexOf(SwingFinder.Stage) > history, string.Join(", ", stages));

        // One row of each table a name holding bars, and the row of its own carrying no call.
        var names = Scalar(store, "SELECT COUNT(DISTINCT ticker) FROM bar;");

        Assert.True(names > 0);
        Assert.Equal(names, Scalar(store, "SELECT COUNT(*) FROM earnings_move;"));
        Assert.Equal(names, Scalar(store, "SELECT COUNT(*) FROM long_view;"));
        Assert.Equal(0, Scalar(store, $"SELECT model_calls + network_requests FROM run_log WHERE run_id = 'night-page-history' AND stage = '{PageHistoryReader.Stage}';"));
        Assert.Equal("ok", RunLog(store, "night-page-history").Single(row => row.Stage == PageHistoryReader.Stage).Outcome);
    }

    [Fact]
    public async Task APageHistoryReaderThatCannotFinishKeepsTheEarlierNightsRowsNamesWhyAndTheNightGoesOnToTheRules()
    {
        using var store = new TemporaryStore().Migrated();

        // An earlier night's row for a name, and a store refusing every row of the typical earnings moves, which the
        // reader writes in its one write after reading every name.
        store.Execute("INSERT INTO earnings_move (ticker, night, prints, unread, first_print, median_move, pull, reason) VALUES ('AAPL', '2026-09-04', 4, 0, '2025-10-30', 1.852, NULL, 'no pulled history before its first stored session');");
        store.Execute("CREATE TRIGGER refuse_moves BEFORE INSERT ON earnings_move BEGIN SELECT RAISE(ABORT, 'the store refused the row'); END;");

        var (code, output, error) = await NightAsync(store, runId: "night-page-history-refused");

        // The night finished, its swings and every step after them run.
        Assert.True(code == 0, error);
        Assert.Contains("the earnings moves and long views not read tonight, ", output, StringComparison.Ordinal);

        var log = RunLog(store, "night-page-history-refused");
        var reader = log.Single(row => row.Stage == PageHistoryReader.Stage);

        Assert.Equal(PageHistoryReader.NotRead, reader.Outcome);
        Assert.StartsWith("not read tonight, each name keeping the rows an earlier night wrote: ", reader.Detail, StringComparison.Ordinal);
        Assert.Contains("the store refused the row", reader.Detail, StringComparison.Ordinal);
        Assert.Contains(log, row => row.Stage == SwingFinder.Stage && row.Outcome == "ok");
        Assert.Contains(log, row => row.Stage == EquityBrief.Worker.Nights.NightClose.Stage);

        // The earlier night's row stands, and the long views the same write would have replaced are none written.
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM earnings_move WHERE ticker = 'AAPL' AND night = '2026-09-04';"));
        Assert.Equal(1, Scalar(store, "SELECT COUNT(*) FROM earnings_move;"));
        Assert.Equal(0, Scalar(store, "SELECT COUNT(*) FROM long_view;"));
    }
}
