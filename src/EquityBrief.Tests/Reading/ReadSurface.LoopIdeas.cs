using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.9: the Loop page draws the idea generator's proposals as every proposal is drawn, each with its change
// and its finding naming the idea the local model answered beneath the change, read back off the rendered page over a
// constructed store, a proposal naming no change drawing no finding.
// see: The idea generator proposes conditions in the winners' grammar by closed labels code turns into levels, judged as a procedure inside each fold
public partial class ReadSurface
{
    // The row of section 15.20 this check reaches from 20.9.
    internal static readonly string[] LoopIdeaClaims =
    [
        CheckReach.Key(Scope.LoopPage, "The idea generator's proposals"),
    ];

    [Fact]
    public async Task TheLoopPageDrawsTheIdeaGeneratorsProposalsWithTheirFindingReadBackAgainstTheStore()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute($"INSERT INTO loop_run (run_id, month, index_code, through, started_at, ended_at, folds) VALUES ('{OctoberRun}', '2026-10', 'MID', '2026-10-08', '2026-10-09T12:00:00Z', '2026-10-09T12:01:00Z', 5);");

        // The drift's first idea, its change and its finding as the tester writes them, and its second, which all finished
        // data answered no idea for.
        const string Finding = "the local model named rsi above the upper edge of its tenth 7 over the 1200 finished listings; rsi at or above 61.5 kept 360 of them at +0.120 risks a listing against the rule's +0.020";

        LoopProposalRow(store, "drift", "the idea generator, ranked 1", "also requires rsi at or above 61.5", "the drift rule today", "risks", 900, "0.3", 0, 0, 4, 2, "4.1", 1, "0.09", 3, Finding);
        LoopProposalRow(store, "drift", "the idea generator, ranked 2", null, "the drift rule today", "risks", 1000, "0.9", 0, 0, 4, 1, "0", 1, "0.09", 0);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));

        Assert.Contains("<div class=\"loop-proposal\" data-family=\"drift\" data-proposal=\"the idea generator, ranked 1\"", page, StringComparison.Ordinal);
        Assert.Contains("<h4 class=\"loop-name\">The idea generator, ranked 1: did not pass the tester</h4>", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"loop-change\" data-words=\"also requires rsi at or above 61.5\">", page, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"loop-finding\" data-finding=\"{Finding}\"><b>Why.</b> The local model named rsi above the upper edge of its tenth 7 over the 1200 finished listings", page, StringComparison.Ordinal);
        Assert.True(page.IndexOf("data-words=\"also requires rsi at or above 61.5\"", StringComparison.Ordinal) < page.IndexOf("class=\"loop-finding\"", StringComparison.Ordinal));

        // The second states no change and draws no finding.
        Assert.Contains("<div class=\"loop-proposal\" data-family=\"drift\" data-proposal=\"the idea generator, ranked 2\"", page, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(page, "class=\"loop-finding\""));
    }
}
