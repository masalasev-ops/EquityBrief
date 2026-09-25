using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Checks;

namespace EquityBrief.Tests.Reading;

// read-surface, 12.2: a name's gates on its page and the run page's funnel, each read back off the page
// against the rows the swing filter stored over the fixture's two-night store.
public partial class ReadSurface
{
    const string GatesNight = "2026-09-04";

    [Fact]
    public async Task EachNamesGatesAreDrawnOnItsPageAsTheFilterStoredThem()
    {
        using var store = await FixtureExpectations.WithTwoNights();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var stored = SwingRows(
            store,
            "SELECT ticker, gates, family, trigger_event, ladder_reward_to_risk, ladder_stop_moves, swing_entry, swing_stop, swing_target, " +
            $"swing_reward_to_risk, swing_stop_moves, exclusions, passed, version FROM gate_result WHERE session_date = '{GatesNight}' ORDER BY ticker;");

        Assert.Equal(4, stored.Count);

        foreach (var row in stored)
        {
            var ticker = row[0];
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{ticker}"));
            var card = Assert.Single(Blocks(page, "<section class=\"card\"[^>]* data-card=\"gates\">.*?</section>"));

            Assert.Contains($"<div class=\"gates\" data-ticker=\"{ticker}\" data-session=\"{GatesNight}\" data-version=\"{row[13]}\" data-passed=\"{(row[12] == "1" ? "yes" : "no")}\" data-rank=\"none\">", card, StringComparison.Ordinal);

            // Each of the five gates in order, whether it passed and why, as the row stores them.
            var drawn = Regex.Matches(card, "<tr data-gate=\"([^\"]+)\" data-passed=\"(yes|no)\"><td>[^<]*</td><td>(passed|failed)</td><td>(.*?)</td></tr>");
            var gates = JsonDocument.Parse(row[1]).RootElement.GetProperty("gates").EnumerateArray().ToArray();

            Assert.Equal(gates.Select(gate => gate.GetProperty("gate").GetString()), drawn.Select(match => match.Groups[1].Value));
            Assert.Equal(gates.Select(gate => gate.GetProperty("passed").GetBoolean() ? "yes" : "no"), drawn.Select(match => match.Groups[2].Value));
            Assert.Equal(gates.Select(gate => gate.GetProperty("reason").GetString()), drawn.Select(match => match.Groups[4].Value));

            // The family and the trigger's event.
            Assert.Contains(
                $"data-family=\"{(row[2].Length == 0 ? "none" : row[2])}\" data-trigger-event=\"{(row[3] == "1" ? "yes" : row[3] == "0" ? "no" : "none")}\"",
                card,
                StringComparison.Ordinal);

            // The trade read both ways, each figure whole on its row.
            Assert.Contains($"<tr data-plan=\"ladder\" data-reward-to-risk=\"{WholeOf(row[4])}\" data-stop-moves=\"{WholeOf(row[5])}\">", card, StringComparison.Ordinal);
            Assert.Contains(
                $"<tr data-plan=\"swing\" data-entry=\"{Plain(row[6])}\" data-stop=\"{Plain(row[7])}\" data-target=\"{Plain(row[8])}\" data-reward-to-risk=\"{WholeOf(row[9])}\" data-stop-moves=\"{WholeOf(row[10])}\">",
                card,
                StringComparison.Ordinal);

            // The exclusions, and the key closing on what to take from the gates.
            Assert.Contains($"data-exclusions=\"{string.Join(",", JsonSerializer.Deserialize<string[]>(row[11])!)}\"", card, StringComparison.Ordinal);
            Assert.Matches("<div class=\"key\">.*?How to read it\\..*?<p class=\"take\"><b>What to take from it\\.</b> A failed gate names what it read and why it failed\\.", card.Replace("\n", " ", StringComparison.Ordinal));
        }

        static string Plain(string stored) => stored.Length == 0 ? "none" : decimal.Parse(stored, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }

    [Fact]
    public async Task TheRunPagesFunnelCountsTheStoredRowsGateByGate()
    {
        using var store = await FixtureExpectations.WithTwoNights();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var rows = SwingRows(store, $"SELECT market, trend, setup, trigger_pass, trade, family, exclusions, passed FROM gate_result WHERE session_date = '{GatesNight}';");
        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/run/{GatesNight}"));
        var card = Assert.Single(Blocks(page, "<section class=\"card\"[^>]* data-card=\"funnel\">.*?</section>"));

        // The test's own counts off the stored flags: each gate passing it and every one before it, and
        // what it removed from the count before.
        var gates = new[] { "market", "trend and strength", "setup", "trigger", "trade" };
        var before = rows.Count;

        Assert.Contains($"<tr data-step=\"members\" data-passed=\"{rows.Count}\" data-removed=\"0\">", card, StringComparison.Ordinal);

        for (var at = 0; at < gates.Length; at++)
        {
            var through = at;
            var passed = rows.Count(row => Enumerable.Range(0, through + 1).All(gate => row[gate] == "1"));

            Assert.Contains($"<tr data-step=\"{gates[at]}\" data-passed=\"{passed}\" data-removed=\"{before - passed}\">", card, StringComparison.Ordinal);

            before = passed;
        }

        var pullbacks = rows.Count(row => row[0] == "1" && row[1] == "1" && row[5] == "pullback");

        Assert.Contains($"data-pullbacks=\"{pullbacks}\" data-breakouts=\"0\"", card, StringComparison.Ordinal);
        Assert.Contains($"<tr data-step=\"excluded\" data-passed=\"{rows.Count(row => row[7] == "1")}\" data-removed=\"0\">", card, StringComparison.Ordinal);
        Assert.Contains("No filter version is open, so the night ran on section 17's proposed values.", card, StringComparison.Ordinal);
        Assert.Contains("The six reasons drew this evening's list, and these counts decided nothing on it.", card, StringComparison.Ordinal);

        // A night the filter stored nothing for says so rather than drawing a funnel of zeroes.
        var earlier = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-02"));

        Assert.Contains("no swing filter results are stored for this night", Assert.Single(Blocks(earlier, "<section class=\"card\"[^>]* data-card=\"funnel\">.*?</section>")), StringComparison.Ordinal);
    }
}
