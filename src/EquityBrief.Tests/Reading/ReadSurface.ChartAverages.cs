using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Indicators;

namespace EquityBrief.Tests.Reading;

// read-surface, the 3.1 correction: the chart's averages drawn on the sessions the indicators leave empty from what the
// night read through the sessions before the store's year, with the line beneath naming the pull or why none was read.
// see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step only the chart reads
public partial class ReadSurface
{
    // A constant, since the scope's own tables read it while they are built.
    internal const string ChartAveragesPart =
        "15.9 Name" + CheckReach.Joiner + "The chart, its averages drawn on the sessions the indicators leave empty from what the night read through the sessions before the store's year with a line beneath naming the pull or why none was read";

    // The page over the fixture's night: a name given a pull of every session the exchange's calendar covers before its
    // first stored bar draws its 50-day line on every session the chart draws, and its 200-day line on all but the ones
    // that pull is too short for, and names the pull beneath the chart; a name with none draws the 200-day line from its
    // 200th session and says why.
    [Fact]
    public async Task TheNamePageDrawsItsAveragesThroughThePulledSessionsAndSaysWhereTheyCameFrom()
    {
        var (store, code, _, _) = await FixtureReplay.NightAsync();

        using (store)
        {
            Assert.Equal(0, code);

            string Scalar(string sql)
            {
                using var connection = store.Open();
                using var command = connection.CreateCommand();

                command.CommandText = sql;

                return Convert.ToString(command.ExecuteScalar(), CultureInfo.InvariantCulture)!;
            }

            static string Day(DateOnly session) => session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            var earliest = Scalar("SELECT MIN(session_date) FROM bar WHERE ticker = 'AAPL';");
            var first = DateOnly.ParseExact(earliest, "yyyy-MM-dd", CultureInfo.InvariantCulture);
            var close = Scalar($"SELECT close FROM bar WHERE ticker = 'AAPL' AND session_date = '{Day(first)}';");
            var sessions = new List<DateOnly>();

            for (var day = first.AddDays(-1); sessions.Count < 199 && day >= ExchangeClosures.CoveredFrom; day = day.AddDays(-1))
            {
                if (ExchangeClosures.IsSession(day))
                {
                    sessions.Add(day);
                }
            }

            // The fixture's year begins inside the calendar's first year, so the pull is shorter than the longest window.
            Assert.InRange(sessions.Count, 50, 198);

            foreach (var session in sessions.Append(first))
            {
                store.Execute($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('AAPL', '{Day(session)}', '{close}', '{close}', '{close}', '{close}', '{close}', 1000, 'history-pull-page');");
            }

            await new ChartAverager(FixedClock.At(new DateTimeOffset(2026, 9, 8, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile)
                .RunAsync("chart-averages-page");

            using var host = new Host(store.Root);
            using var client = host.CreateClient();

            var warmed = await client.GetStringAsync("/screens/name/AAPL");
            var bare = await client.GetStringAsync("/screens/name/MSFT");
            var aapl = int.Parse(Scalar("SELECT COUNT(*) FROM bar WHERE ticker = 'AAPL';"), CultureInfo.InvariantCulture);
            var msft = int.Parse(Scalar("SELECT COUNT(*) FROM bar WHERE ticker = 'MSFT';"), CultureInfo.InvariantCulture);

            Assert.Contains($"<g class=\"moving-average\" data-average=\"sma50\" data-values=\"{aapl}\">", warmed, StringComparison.Ordinal);
            Assert.Contains($"<g class=\"moving-average\" data-average=\"sma200\" data-values=\"{aapl - (199 - sessions.Count)}\">", warmed, StringComparison.Ordinal);
            Assert.Contains("<p class=\"averages-from\" data-pull=\"history-pull-page\">", warmed, StringComparison.Ordinal);
            Assert.Contains($"<g class=\"moving-average\" data-average=\"sma200\" data-values=\"{msft - 199}\">", bare, StringComparison.Ordinal);
            Assert.Contains($"<p class=\"averages-from\" data-reason=\"{ChartAverager.NoPull}\">The averages start partway across the chart: {ChartAverager.NoPull}.</p>", bare, StringComparison.Ordinal);
        }
    }
}
