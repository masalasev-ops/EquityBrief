using EquityBrief.Core.Chart;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Charts;
using EquityBrief.Worker.Sweep;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.10: the sweep's series read past a bad print and from a stock's start, worked over a constructed
// store's marks; a rule's read of the pull under the marks worked by hand over the same rows; and no pulled row moving.
// see: From phase 23's restart the sweep's history and every rule's read of the pull read the marks, each stock from its start and past each bad print
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheSweepsSeriesReadsEachStockFromItsStartPastABadPrintAndNoPulledRowMoves()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(NightAt(FaultNight), SessionZones.UnitedStates);

        // PRG's print of 3 on 6 April 2021 between 31 and 32, and CHRD's older equity at 900 before its history starts on the 5th.
        PulledRows(store, "PRG", "pull-1", ("2021-04-01", 30m), ("2021-04-05", 31m), ("2021-04-06", 3m), ("2021-04-07", 32m));
        PulledRows(store, "CHRD", "pull-1", ("2021-04-01", 900m), ("2021-04-05", 50m), ("2021-04-06", 51m));

        var marking = new SessionMarks(clock, store.DatabaseFile);

        Assert.Equal(1, await marking.MarkAsync("PRG", new DateOnly(2021, 4, 6), SessionMarking.BadPrint, "A bad print.", "mark-prg"));
        Assert.Equal(1, await marking.MarkAsync("CHRD", new DateOnly(2021, 4, 5), SessionMarking.StartsHere, "Its history starts here.", "mark-chrd"));

        using var connection = store.Open();

        var marks = await SweepHistory.MarksAsync(connection, default);

        static SweepBar Bar(int day, decimal close) => new(new DateOnly(2021, 4, day), close, close, close, 1000, close);

        static string[] Days(IEnumerable<SweepBar> series) => [.. series.Select(bar => bar.Session.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture))];

        Assert.Equal(["2021-04-01", "2021-04-05", "2021-04-07"], Days(SweepHistory.UnderMarks([Bar(1, 30m), Bar(5, 31m), Bar(6, 3m)], [Bar(7, 32m)], marks.GetValueOrDefault("PRG"))));
        Assert.Equal(["2021-04-05", "2021-04-06"], Days(SweepHistory.UnderMarks([Bar(1, 900m), Bar(5, 50m), Bar(6, 51m)], [], marks.GetValueOrDefault("CHRD"))));
        Assert.Equal(["2021-04-01", "2021-04-05"], Days(SweepHistory.UnderMarks([Bar(1, 10m)], [Bar(5, 11m)], marks.GetValueOrDefault("NONE"))));

        // A rule's read of the pull under the marks, worked by hand over the same rows.
        IReadOnlyList<string> Read(string ticker)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT session_date FROM pulled_bar WHERE ticker = $ticker AND " + SessionMarking.AsMarked + " ORDER BY session_date;";
            command.Parameters.AddWithValue("$ticker", ticker);

            var rows = new List<string>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }

            return rows;
        }

        Assert.Equal(["2021-04-01", "2021-04-05", "2021-04-07"], Read("PRG"));
        Assert.Equal(["2021-04-05", "2021-04-06"], Read("CHRD"));

        // No pulled row moved.
        using var count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM pulled_bar;";

        Assert.Equal(7L, (long)count.ExecuteScalar()!);
    }
}
