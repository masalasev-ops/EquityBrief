using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 19.7: the remaining screens over the fixture. The Run page of the fixture's whole night counts no
// trade of the live list in its one line and says no monthly run has started; and the researched list over the fixture's
// research pass draws KEYS's report written by hand on the night, holding seven sections of ten, standing, and moved
// nothing since.
// see: Research goes stale on an event dated after the section was written, and a news spike is dated by the session it began
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheFixturesRunPageCountsNoTradeAndNoMonthlyRunAndItsReportStandsWrittenByHand()
    {
        var expected = Expected("remaining-screens");

        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Equal(["research_section"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()!));

        var night = expected.GetProperty("night").GetString()!;
        var trades = expected.GetProperty("trades");

        var (store, code, _, _) = await FixtureReplay.NightAsync();
        using var held = store;

        Assert.Equal(0, code);

        // The derivation's ground: the night's live list kept no stock and no monthly run wrote a step.
        Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM gate_result WHERE session_date = '{night}' AND passed = 1;"));
        Assert.Equal(["0"], Query(store, $"SELECT COUNT(*) FROM run_log WHERE run_id LIKE '{Api.Reading.RunScreen.MonthlyPrefix}%';"));

        using (var host = new Reading.ReadSurface.Host(store.Root))
        {
            using var client = host.CreateClient();

            var run = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{night}"));

            Assert.Contains(
                FormattableString.Invariant($"<p class=\"run-trades\" data-listed=\"{trades.GetProperty("listed").GetInt32()}\" data-open=\"{trades.GetProperty("open").GetInt32()}\" data-decided=\"{trades.GetProperty("decided").GetInt32()}\" data-needed=\"{trades.GetProperty("needed").GetInt32()}\">The live list's trades: {trades.GetProperty("listed").GetInt32()} listed, {trades.GetProperty("open").GetInt32()} still open, {trades.GetProperty("decided").GetInt32()} decided of the {trades.GetProperty("needed").GetInt32()} a share waits on."),
                run,
                StringComparison.Ordinal);
            Assert.Equal("none", expected.GetProperty("monthly").GetString());
            Assert.Contains("<div class=\"monthly-run\" data-state=\"not read\" data-month=\"none\" data-families=\"0\"><p class=\"mr-state degraded\" data-started=\"none\">No monthly run has started yet.", run, StringComparison.Ordinal);
        }

        // The researched list over the fixture's research pass.
        var researched = expected.GetProperty("researched");
        var ticker = researched.GetProperty("ticker").GetString()!;

        using var written = await FixtureReplay.ResearchedAsync();

        // The derivation's ground: no request names the report, and its newest accepted sections are the ones
        // research-record.json's versions leave standing, each dated by the night.
        Assert.Equal(["0"], Query(written, "SELECT COUNT(*) FROM research_request;"));
        Assert.Equal(
            [researched.GetProperty("sections").GetInt32().ToString(CultureInfo.InvariantCulture)],
            Query(written, $"SELECT COUNT(*) FROM research_section s WHERE s.ticker = '{ticker}' AND s.status = 'accepted' AND s.version = (SELECT MAX(n.version) FROM research_section n WHERE n.ticker = s.ticker AND n.section = s.section);"));
        Assert.Equal([researched.GetProperty("written").GetString()!], Query(written, $"SELECT DISTINCT as_of FROM research_section WHERE ticker = '{ticker}' AND status = 'accepted';"));

        using var surface = new Reading.ReadSurface.Host(written.Root);
        using var browser = surface.CreateClient();

        var list = WebUtility.HtmlDecode(await browser.GetStringAsync("/screens/researched"));
        var row = Regex.Match(list, $"<tr data-ticker=\"{ticker}\" [^>]*>.*?</tr>", RegexOptions.Singleline).Value;

        Assert.StartsWith(
            $"<tr data-ticker=\"{ticker}\" data-written=\"{researched.GetProperty("written").GetString()}\" data-sections=\"{researched.GetProperty("sections").GetInt32()}\" data-stale=\"{(researched.GetProperty("stale").GetBoolean() ? "true" : "false")}\" data-why=\"{researched.GetProperty("why").GetString()}\" data-move=\"{researched.GetProperty("move").GetString()}\">",
            row,
            StringComparison.Ordinal);
        Assert.Contains($"data-of=\"{researched.GetProperty("of").GetInt32()}\">{researched.GetProperty("sections").GetInt32()} of {researched.GetProperty("of").GetInt32()}</td>", row, StringComparison.Ordinal);
        Assert.Contains("<span class=\"status-pill live\">stands</span>", row, StringComparison.Ordinal);
    }
}
