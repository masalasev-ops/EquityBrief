using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.5: the Loop page over a constructed run proposing for each of the seven families the loop covers, each
// family a row of the summary and a card under its own label in Tonight's order of their cards, the fundamentals-first
// family's proposal drawn nowhere, the dip's exit shown with why it is not put and no press, and every table the page draws
// read in a box of its own, so a phone's narrow column holds the page.
// see: The monthly loop proposes for seven families on every index where their rule stands, and the fundamentals-first family leaves it with the night
// see: Every change the engines propose is one its family's night reads, so each is put to the operator
public partial class ReadSurface
{
    [Fact]
    public async Task TheLoopPageDrawsARowAndACardForEachOfTheSevenFamiliesEachTableInABoxOfItsOwn()
    {
        using var store = new TemporaryStore().Migrated();
        const string run = "loop-test-SML-20261107T120000Z";
        var dipExit = "exit: a target at 1.5 risks";

        Run(store, run, "SML", "2026-11");
        Tested(store, run, "SML", "pullback", "winners against losers, ranked 1", 0.2, false, Condition("rsi", 60));
        Tested(store, run, "SML", "breakout", "the autopsy's exit, ranked 1", 0.3, false, Exit(7));
        Tested(store, run, "SML", "drift", "the learned score, ordering the list", 0.4, false, Condition("rsi", 55));
        Tested(store, run, "SML", ImprovingRule.Name, "the grid", 0.5, false, new LoopChange(null, ImprovingGrid.Settings[1].Key, null, LoopChange.NoHooks).Json);
        Tested(store, run, "SML", DipRule.Name, "the autopsy's exit, ranked 1", 0.0001, true, Exit(7));
        Tested(store, run, "SML", HeavyweightRule.Name, "the autopsy's exit, ranked 1", 0.6, false, null);
        Tested(store, run, "SML", QualityRule.Name, "the autopsy's exit, ranked 1", 0.7, false, null);
        Tested(store, run, "SML", FundamentalsRule.Name, "the grid", 0.0001, true, LoopChange.OfSetting([0, 0, 2], "growth 0|margins either|cash 1.2", 0).Json);
        store.Execute($"UPDATE loop_proposal SET words = '{dipExit}', finding = 'a target at 1.5 risks read more' WHERE family = '{DipRule.Name}';");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=600"));
        var summary = Card(page, "loop-summary");

        // A row and a card for each of the seven, in Tonight's order of their cards, each card under its family's label.
        string[] seven = ["pullback", "breakout", "drift", ImprovingRule.Name, DipRule.Name, HeavyweightRule.Name, QualityRule.Name];

        Assert.Equal(seven, Regex.Matches(summary, "<tr data-family=\"([^\"]+)\" data-tested").Select(match => match.Groups[1].Value));
        Assert.Equal(seven, Regex.Matches(page, "data-card=\"loop-([a-z]+)\"").Select(match => match.Groups[1].Value).Where(card => seven.Contains(card)));

        foreach (var (family, label) in new[] { (ImprovingRule.Name, ImprovingRule.Label), (DipRule.Name, SetupFamilies.Dips.Label), (QualityRule.Name, QualityRule.Label), (HeavyweightRule.Name, "Sector heavyweights") })
        {
            Assert.Contains(">" + label + "<", Card(page, "loop-" + family), StringComparison.Ordinal);
        }

        // The fundamentals-first family's proposal, passing with a change, is drawn nowhere and put to no one.
        Assert.DoesNotContain("data-family=\"fundamentals\"", page, StringComparison.Ordinal);

        // From 21.16 the dip's exit passed and states its change, which the dip's night reads: its finding is drawn with no
        // words saying it is not put.
        var dip = Card(page, "loop-dip");

        Assert.Contains("a target at 1.5 risks read more", dip, StringComparison.Ordinal);
        Assert.DoesNotContain("not put to you, since", dip, StringComparison.Ordinal);

        // Every table the page draws sits in the box that scrolls it.
        var tables = Regex.Matches(page, "(.{0,24})<table(?: class=\"([a-z-]+)\")?[ >]");

        Assert.NotEmpty(tables);
        Assert.All(tables, table => Assert.EndsWith("<div class=\"tbl-wrap\">", table.Groups[1].Value, StringComparison.Ordinal));
    }
}
