using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The buy range's widths on the Improvement loop page, read back over a constructed store: the newest test on the index the
// page reads with the width its early years chose; every width beside the buy at the open over 2005 to 2018 and from 2019,
// what each bought, did not buy on each side, made a pick, missed and kept out of; each setup folded beneath; how a card's
// plan reads at the chosen width; another index's and an older run's rows absent; and a store holding no test saying when it
// runs. The table takes inserts alone.
// see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
public partial class ReadSurface
{
    internal static string[] BuyRangeRows =>
    [
        CheckReach.Key("15.20 The Loop page", "Buy ranges"),
    ];

    static string RangeRow(string run, string index, string family, string width, string ceiling, string floor, string years, int offered, int bought, int above, int below, string edge, string missed, string saved, int chosen) =>
        $"('{run}', '{index}', '{family}', '{width}', {ceiling}, {floor}, '{years}', {offered}, {bought}, {above}, {below}, {edge}, {missed}, {saved}, {chosen}, '2026-12-01T10:00:00Z')";

    [Fact]
    public async Task TheLoopPageDrawsTheNewestWidthsTestWithItsChosenWidthBesideTheOpenAndHowACardsPlanReads()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO buy_range_width (run_id, index_code, family, width, ceiling, floor, years, offered, bought, above, below, edge, missed, saved, chosen, written_at) VALUES "
            + string.Join(", ",
                RangeRow("ranges-2", "SML", "all", "the open", "NULL", "NULL", "early", 1200, 1200, 0, 0, "0.05", "0", "0", 0),
                RangeRow("ranges-2", "SML", "all", "the open", "NULL", "NULL", "late", 800, 800, 0, 0, "0.04", "0", "0", 0),
                RangeRow("ranges-2", "SML", "all", "1 / 2", "1", "2", "early", 1200, 1000, 150, 50, "0.07", "0.01", "0.03", 1),
                RangeRow("ranges-2", "SML", "all", "1 / 2", "1", "2", "late", 800, 690, 80, 30, "0.06", "0.012", "0.025", 1),
                RangeRow("ranges-2", "SML", "all", "0.5 / none", "0.5", "NULL", "early", 1200, 900, 300, 0, "0.06", "0.02", "0.03", 0),
                RangeRow("ranges-2", "SML", "all", "0.5 / none", "0.5", "NULL", "late", 800, 600, 200, 0, "0.09", "0.02", "0.05", 0),
                RangeRow("ranges-2", "SML", "pullback", "1 / 2", "1", "2", "early", 700, 600, 70, 30, "0.08", "0.01", "0.03", 0),
                RangeRow("ranges-2", "GSPC", "all", "2 / none", "2", "NULL", "early", 900, 880, 20, 0, "0.11", "0.0", "0.01", 1),
                RangeRow("ranges-1", "SML", "all", "2 / 1", "2", "1", "early", 1100, 1000, 10, 90, "0.5", "0", "0", 1)) + ";");
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES "
            + "('ranges-1', 'buy-range-widths', '2026-11-30T10:00:00Z', '2026-11-30T11:00:00Z', 'ok', 1, 0, 0, '0', 'older'), "
            + "('ranges-2', 'buy-range-widths', '2026-12-01T09:00:00Z', '2026-12-01T10:00:00Z', 'ok', 8, 0, 0, '0', 'newer');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var ranges = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600")), "<section class=\"card\" data-card=\"loop-ranges\">.*?</section>").Single();

        Assert.Contains("<h2>How far from the last close a buy may open</h2>", ranges, StringComparison.Ordinal);
        Assert.Contains("<p data-summary data-ranges=\"chosen\">The test of 1 Dec 2026 on the S&P 600 chose a ceiling 1 typical day above the last close and a floor 2 typical days under it: the highest edge a pick over 2005 to 2018. It waits for your approval.</p>", ranges, StringComparison.Ordinal);

        // Each years' table: the open first, then the widths, the chosen one marked; bought of offered, each side, and the
        // three figures a pick.
        Assert.Equal(["the open", "0.5 / none", "1 / 2", "the open", "0.5 / none", "1 / 2", "1 / 2"], [.. Regex.Matches(ranges, "<tr data-width=\"([^\"]+)\"").Select(match => match.Groups[1].Value)]);
        Assert.Equal(2, Regex.Matches(ranges, "<tr data-width=\"1 / 2\" data-chosen>").Count);
        Assert.Contains("<td data-h=\"Width\">1 / 2 <b>chosen</b></td><td data-h=\"Bought\" class=\"r num\">1,000 of 1,200</td><td data-h=\"Opened above\" class=\"r num\">150</td><td data-h=\"Opened below\" class=\"r num\">50</td><td data-h=\"Edge a pick\" class=\"r num\">+0.070</td><td data-h=\"Winners missed\" class=\"r num\">+0.010</td><td data-h=\"Losses kept out\" class=\"r num\">+0.030</td>", ranges, StringComparison.Ordinal);
        Assert.Contains("<td data-h=\"Width\">At the open, no range</td><td data-h=\"Bought\" class=\"r num\">800 of 800</td>", ranges, StringComparison.Ordinal);
        Assert.Contains("<summary>Each setup on its own</summary><p class=\"lm-p\"><b>", ranges, StringComparison.Ordinal);

        // A card's plan at 1 / 2 on a last close of $100.00 and a typical move of $2.00, in the operator's words.
        Assert.Contains("<li>Buys between $96.00 and $102.00</li><li>Opens above $102.00: waits up to 2 days for the price to come back to $102.00, then not bought.</li><li>Opens below $96.00: not bought.</li>", ranges, StringComparison.Ordinal);
        Assert.DoesNotContain("2 / none", ranges, StringComparison.Ordinal);
        Assert.DoesNotContain("2 / 1", ranges, StringComparison.Ordinal);

        // The table takes inserts alone.
        Assert.ThrowsAny<Exception>(() => store.Execute("UPDATE buy_range_width SET chosen = 0;"));
        Assert.ThrowsAny<Exception>(() => store.Execute("DELETE FROM buy_range_width;"));

        // A store holding no test says when it runs.
        using var empty = new TemporaryStore().Migrated();
        using var emptyHost = new Host(empty.Root);
        using var emptyClient = emptyHost.CreateClient();

        Assert.Contains("The widths test has not run on this index yet. It runs by hand over the long ledger once the ledger has been rebuilt through the fixed reader.", WebUtility.HtmlDecode(await emptyClient.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }

    // see: The picker's buy range width is chosen first over the price families' setups under its menu and then over every member on every session
    [Fact]
    public async Task APickerWidthsRunReadsProvisionalAndNamesEachPlanOfTheMenuWhereARulesRunReadsNeither()
    {
        using var store = new TemporaryStore().Migrated();
        var plan = EquityBrief.Core.Loop.BuyRanges.PickerFamily("1 week", EquityBrief.Core.Judge.JudgeMenu.Whole);

        store.Execute(
            "INSERT INTO buy_range_width (run_id, index_code, family, width, ceiling, floor, years, offered, bought, above, below, edge, missed, saved, chosen, written_at) VALUES "
            + string.Join(", ",
                RangeRow("ranges-3", "GSPC", "all", "the open", "NULL", "NULL", "early", 1200, 1200, 0, 0, "0.05", "0", "0", 0),
                RangeRow("ranges-3", "GSPC", "all", "1 / 2", "1", "2", "early", 1200, 1000, 150, 50, "0.07", "0.01", "0.03", 1),
                RangeRow("ranges-3", "GSPC", plan, "1 / 2", "1", "2", "early", 150, 125, 20, 5, "0.06", "0.01", "0.02", 0)) + ";");
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES "
            + $"('ranges-3', 'buy-range-widths', '2026-11-24T05:00:00Z', '2026-11-24T06:00:00Z', 'ok', 3, 0, 0, '0', '{EquityBrief.Core.Loop.BuyRanges.PickerRun.Replace("'", "''", StringComparison.Ordinal)}; chosen on 2005 to 2018: GSPC 1 / 2');");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var ranges = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")), "<section class=\"card\" data-card=\"loop-ranges\">.*?</section>").Single();

        Assert.Contains("<p class=\"lm-small\" data-ranges=\"provisional\">Provisional: walked as the AI stock picker buys, under each plan of its menu, over every stock and session the four price families flagged, since the picker has no history of its own. It runs again over every member on every session once that table is rebuilt, and that width replaces this one from the next night.</p>", ranges, StringComparison.Ordinal);
        Assert.Contains("<summary>Each plan of the picker's menu on its own</summary><p class=\"lm-p\"><b>1 week, the whole at 2 times the risk</b>, over 2005 to 2018</p>", ranges, StringComparison.Ordinal);

        // A run of the rules' setups that came after reads as before.
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES "
            + "('ranges-4', 'buy-range-widths', '2026-11-25T05:00:00Z', '2026-11-25T06:00:00Z', 'ok', 1, 0, 0, '0', 'chosen on 2005 to 2018: GSPC 1 / 2');");
        store.Execute(
            "INSERT INTO buy_range_width (run_id, index_code, family, width, ceiling, floor, years, offered, bought, above, below, edge, missed, saved, chosen, written_at) VALUES "
            + RangeRow("ranges-4", "GSPC", "all", "1 / 2", "1", "2", "early", 1200, 1000, 150, 50, "0.07", "0.01", "0.03", 1) + ";");

        var rules = Blocks(WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500")), "<section class=\"card\" data-card=\"loop-ranges\">.*?</section>").Single();

        Assert.DoesNotContain("data-ranges=\"provisional\"", rules, StringComparison.Ordinal);
        Assert.Contains("<summary>Each setup on its own</summary>", rules, StringComparison.Ordinal);
    }
}
