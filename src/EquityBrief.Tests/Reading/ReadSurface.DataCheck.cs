using System.Globalization;
using System.Net;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Worker.History;
using Xunit;

namespace EquityBrief.Tests.Reading;

// 23.1's History page: the data check's counts by class and era and the findings a person reads, read back off the page over a
// store the check wrote, and the page over a store it has not run over.
// see: The data check classifies every stock's pulled sessions by stated rules into a table of its own, and moves no price row
public partial class ReadSurface
{
    internal static string[] DataCheckPageRows =>
    [
        CheckReach.Key("15.25 History", "Counts by class and era"),
        CheckReach.Key("15.25 History", "To read against the filings"),
    ];

    static void PulledOf(TemporaryStore store, string ticker, IEnumerable<(DateOnly Session, decimal Close, long Volume)> rows)
    {
        using var connection = store.Open();

        foreach (var (session, close, volume) in rows)
        {
            using var command = connection.CreateCommand();
            command.CommandText = @"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull)
                VALUES ($ticker, $session, $close, $close, $close, $close, $close, $volume, 'pull-1');";
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$close", close.ToString(CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$volume", volume);
            command.ExecuteNonQuery();
        }
    }

    [Fact]
    public async Task TheHistoryPageDrawsTheDataChecksCountsByClassAndEraAndTheFindingsToRead()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 6, 23, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var empty = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/history"));

            Assert.Contains("The data check has not run.", empty, StringComparison.Ordinal);
        }

        // Two stocks over the same eight days of 2019 and 2020: FLAT holds no volume on its fifth, liquid; JUMP falls from 100 to 60 on
        // its fifth and stays, on its usual volume, with nothing to explain it.
        var days = Enumerable.Range(0, 8).Select(at => new DateOnly(2019, 12, 26).AddDays(at)).ToArray();

        PulledOf(store, "FLAT", days.Select((day, at) => (day, at == 4 ? 101m : 100m, at == 4 ? 0L : 200_000L)));
        PulledOf(store, "JUMP", days.Select((day, at) => (day, at >= 4 ? 60m : 100m, 200_000L)));

        var outcome = await new DataChecker(clock, store.DatabaseFile).RunAsync("data-check-1");

        Assert.Equal(2, outcome.Findings.Count);

        using (var host = new Host(store.Root))
        {
            using var client = host.CreateClient();
            var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/history"));

            // The two findings fall on 2019-12-30, so in 2015 to 2019, and each class's row and its kind's carry them.
            Assert.Contains("<th class=\"r\">2005 to 2009</th><th class=\"r\">2010 to 2014</th><th class=\"r\">2015 to 2019</th><th class=\"r\">2020 on</th><th class=\"r\">Every era</th>", page, StringComparison.Ordinal);
            Assert.Contains("<tr data-class=\"no volume\" data-kind=\"liquid\"><td class=\"kind\">liquid</td><td class=\"n\">0</td><td class=\"n\">0</td><td class=\"n\">1 <span class=\"on\">on 1</span></td><td class=\"n\">0</td><td class=\"n\">1 <span class=\"on\">on 1</span></td></tr>", page, StringComparison.Ordinal);
            Assert.Contains("<tr class=\"cls\" data-class=\"jump\"><td>Jump</td><td class=\"n\">0</td><td class=\"n\">0</td><td class=\"n\">1 <span class=\"on\">on 1</span></td>", page, StringComparison.Ordinal);
            Assert.Contains("2 stock(s) and 16 session(s) read from 2019-12-26 to 2020-01-02, 2 finding(s)", page, StringComparison.Ordinal);

            // The unexplained jump is listed to read, linking to its chart; the four other folds count none.
            Assert.Contains("<summary>Jump, unexplained: 1</summary>", page, StringComparison.Ordinal);
            Assert.Contains($"<a href=\"{SinglePageApp.ChartRoute}JUMP\">JUMP</a>", page, StringComparison.Ordinal);
            Assert.Contains("On 2019-12-30 the close fell 40.0% in a day, from 100.00 to 60.00, on no more than usual volume", page, StringComparison.Ordinal);
            Assert.Contains("<summary>When-issued start: 0</summary>", page, StringComparison.Ordinal);

            // The shell reaches it from the Records menu.
            var shell = await client.GetStringAsync("/");
            Assert.Contains($"<a href=\"{SinglePageApp.HistoryRoute}\" data-view=\"history\">History</a>", shell, StringComparison.Ordinal);
            Assert.Contains("fetch('/screens/history')", shell, StringComparison.Ordinal);
        }
    }
}
