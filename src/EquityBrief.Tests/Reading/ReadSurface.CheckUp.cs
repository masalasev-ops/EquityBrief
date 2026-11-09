using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.61: the Monthly check-up read off its rendered pages over a constructed store. The opening page draws
// no change's verdict and links every strategy's page and the three further parts; a change that reached the operator
// draws its four checks and its presses there; a strategy's page draws every change tried once, under its kind or in the
// count of those that found nothing, with the workings folded beneath; and a word given stops the page saying it waits.
// see: The Monthly check-up opens on what needs the reader and draws each setup's changes on a page of its own
public partial class ReadSurface
{
    static TemporaryStore CheckUpStore()
    {
        var store = new TemporaryStore().Migrated();

        Run(store, OctoberRun, "MID", "2026-10");
        Tested(store, OctoberRun, "MID", "pullback", "winners against losers, ranked 1", 0.0001, true, Condition("rsi", 60));
        Tested(store, OctoberRun, "MID", "pullback", "the learned score, ordering the list", 0.3, false, null);
        LoopProposalRow(store, "pullback", "the autopsy's exit, ranked 1", null, "the pullback rule today", "risks", 351, "1", 0, 0, 2, 0, "0", 1, "0.141", 0);
        LoopProposalRow(store, "pullback", "the autopsy's exit, ranked 2", null, "the pullback rule today", "risks", 351, "1", 0, 0, 2, 0, "0", 1, "0.141", 0);
        Tested(store, OctoberRun, "MID", "breakout", "the grid", 0.05, false, Exit(7), better: 2, counted: 5);
        LoopTestRow(store, "pullback", "winners against losers, ranked 1", 2024, 1, "a", 60, 55, 2.5, 4.0);
        LoopTestRow(store, "pullback", "winners against losers, ranked 1", 2026, 0, "a", 20, 20, -1.0, 1.0);

        return store;
    }

    [Fact]
    public async Task TheOpeningPageDrawsNoVerdictAndLinksEachStrategysPageAndTheThreeFurtherParts()
    {
        using var store = CheckUpStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var opening = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));

        // Nothing of a change's workings stands on the opening page.
        foreach (var part in new[] { "class=\"loop-row\"", "class=\"loop-proposal\"", "class=\"loop-rule\"", "class=\"cu-group\"", "class=\"cu-workings\"", "p-meter" })
        {
            Assert.DoesNotContain(part, opening, StringComparison.Ordinal);
        }

        // Every strategy's page and each further part is one link away, the month kept in the address.
        Assert.Equal(
            LoopDecisions.LoopFamilies.Select(family => $"#/loop?universe=400&month=2026-10&strategy={family}"),
            Regex.Matches(opening, "href=\"([^\"]+)\" data-family-page=").Select(match => match.Groups[1].Value));

        foreach (var part in new[] { SinglePageApp.LoopDecisionsPart, SinglePageApp.LoopAlarmPart, SinglePageApp.LoopLongPart })
        {
            Assert.Contains($"href=\"#/loop?universe=400&month=2026-10&part={part}\"", opening, StringComparison.Ordinal);
        }

        // The change put to you: its four checks in order, each met and with what it needed, its years and its presses.
        var change = Assert.Single(Blocks(opening, "<div class=\"cu-change\" data-family=\"pullback\".*?</form><form.*?</form>"));

        Assert.Equal(["gate", "stable", "trimmed", "counts"], Regex.Matches(change, "<li data-part=\"([a-z]+)\" data-met=\"true\"").Select(match => match.Groups[1].Value));
        Assert.Contains("It had to be under about 1 in 240.", change, StringComparison.Ordinal);
        Assert.Contains("It needed 3 of 5, with the latest one better.", change, StringComparison.Ordinal);
        Assert.Contains("It needed 300.", change, StringComparison.Ordinal);
        Assert.Contains("<g data-year=\"2024\" data-complete=\"1\">", change, StringComparison.Ordinal);
        Assert.Contains("A year still running does not count.", change, StringComparison.Ordinal);
        Assert.Contains("name=\"decision\" value=\"approve\"", change, StringComparison.Ordinal);
        Assert.Contains("name=\"decision\" value=\"decline\"", change, StringComparison.Ordinal);

        // A part or a strategy not asked for is not drawn on another's page, and a name no strategy holds opens the page itself.
        var quality = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400&strategy=quality"));

        Assert.Contains("data-card=\"loop-quality\"", quality, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card=\"loop-pullback\"", quality, StringComparison.Ordinal);
        Assert.DoesNotContain("data-card=\"loop-summary\"", quality, StringComparison.Ordinal);
        Assert.Contains("data-card=\"loop-summary\"", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400&strategy=nothing")), StringComparison.Ordinal);
        Assert.Contains("data-card=\"loop-decisions\"", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400&part=decisions")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AStrategysPageDrawsEveryChangeTriedOnceUnderItsKindOrInTheCountThatFoundNothing()
    {
        using var store = CheckUpStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400&strategy=pullback"));

        // Four tried: one condition and one score drawn, and both exits that chose nothing counted under their kind.
        var groups = Regex.Matches(page, "data-group=\"([^\"]+)\" data-tried=\"(\\d+)\" data-passed=\"(\\d+)\"");

        Assert.Equal(4, groups.Sum(group => int.Parse(group.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture)));
        Assert.Equal(2, Regex.Count(page, "<tr data-proposal=\""));
        Assert.Contains("<p class=\"cu-quiet\" data-quiet=\"2\">All 2 found nothing better than today's rule.</p>", page, StringComparison.Ordinal);

        // The kind holding a pass stands open and the others shut, and the workings stand folded beneath every kind.
        Assert.Matches("<details class=\"cu-group\" data-group=\"What the winning picks had in common\" data-tried=\"1\" data-passed=\"1\" open>", page);
        Assert.Matches("<details class=\"cu-group\" data-group=\"Different selling rules\" data-tried=\"2\" data-passed=\"0\"><summary>", page);
        Assert.True(page.LastIndexOf("class=\"cu-group\"", StringComparison.Ordinal) < page.IndexOf("<details class=\"cu-workings\">", StringComparison.Ordinal));
        Assert.True(page.IndexOf("<details class=\"cu-workings\">", StringComparison.Ordinal) < page.IndexOf("class=\"loop-row\"", StringComparison.Ordinal));

        // The change put to you reads as waiting, and its presses stand on the opening page alone.
        Assert.Contains("data-result=\"Passed, waiting for you\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("class=\"loop-press\"", page, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AWordGivenStopsThePageSayingTheChangeWaitsAndTheOpeningSaysSo()
    {
        using var store = CheckUpStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        Assert.Contains("One change is waiting for you.", await LoopWhole(client, "universe=400"), StringComparison.Ordinal);
        Assert.Contains("one beat luck and waits for your word", await LoopWhole(client, "universe=400"), StringComparison.Ordinal);

        Decided(store, OctoberRun, "MID", "pullback", "winners against losers, ranked 1", LoopDecisions.Approved);

        var whole = await LoopWhole(client, "universe=400");

        Assert.DoesNotContain("waits for your word", whole, StringComparison.Ordinal);
        Assert.DoesNotContain("Waiting for you", whole, StringComparison.Ordinal);
        Assert.Contains("Nothing needs you this month.", whole, StringComparison.Ordinal);
        Assert.Contains("You approved one change on October 9, 2026. It goes live with the next night's run.", whole, StringComparison.Ordinal);
        Assert.Contains("one beat luck and you have decided on it", whole, StringComparison.Ordinal);
        Assert.Contains("one passed, and you have decided on it.", whole, StringComparison.Ordinal);
        Assert.Contains("data-result=\"changed\">Changed</td>", whole, StringComparison.Ordinal);
        Assert.Contains("Approved, goes live with the next night's run", whole, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChangeToAStrategyHoldingCompaniesReadsInPointsAndMonthsWorkedByHand()
    {
        using var store = new TemporaryStore().Migrated();

        Run(store, OctoberRun, "MID", "2026-10");
        Proposed(store, OctoberRun, "MID", "dip", "the grid", true, Exit(7));

        // A book's total is stored as a fraction of the buy, so 0.8801 is 88.01 points and a year's 0.1515 is 15.2 points.
        store.Execute("UPDATE loop_proposal SET unit = 'points', units = 58, trimmed = 0.8801;");
        LoopTestRow(store, "dip", "the grid", 2022, 1, "a", 12, 12, -0.0813, 0.1515);

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var opening = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));

        Assert.Contains("+88.01</span> points ahead of today's rule with the 5 biggest results on each side taken out.", opening, StringComparison.Ordinal);
        Assert.Contains("58 months of results. It needed 36.", opening, StringComparison.Ordinal);
        Assert.Contains(">+15.2</text>", opening, StringComparison.Ordinal);
        Assert.Contains(">-8.1</text>", opening, StringComparison.Ordinal);
        Assert.Contains("Points ahead of the easy alternative in each year.", opening, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheChangeSaysTodaysRuleRunsBesideItOnlyWhereAnApprovalRegistersIt()
    {
        using var store = new TemporaryStore().Migrated();

        Run(store, LargeOctober, "GSPC", "2026-10");
        Run(store, OctoberRun, "MID", "2026-10");
        Proposed(store, LargeOctober, "GSPC", "drift", "the autopsy's exit, ranked 1", true, Exit(7));
        Proposed(store, LargeOctober, "GSPC", "pullback", "winners against losers, ranked 1", true, Condition("rsi", 60));
        Proposed(store, OctoberRun, "MID", "drift", "the autopsy's exit, ranked 1", true, Exit(7));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        const string Beside = "Today's rule keeps running beside the new one for 63 trading days";

        var large = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=500"));

        // On the S&P 500 a change registers the family's rule again, but the pullback's opens the swing filter's next version.
        Assert.Contains(Beside, Assert.Single(Blocks(large, "<div class=\"cu-change\" data-family=\"drift\".*?class=\"loop-decision\"")), StringComparison.Ordinal);
        Assert.DoesNotContain(Beside, Assert.Single(Blocks(large, "<div class=\"cu-change\" data-family=\"pullback\".*?class=\"loop-decision\"")), StringComparison.Ordinal);

        // On the S&P 400 a family with no registered rule is changed through its stored setting, with nothing beside it.
        Assert.DoesNotContain(Beside, WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400")), StringComparison.Ordinal);
    }

    [Fact]
    public void ACardsFirstLineDrawsThePagesNameWhereTheNightStoredTheOldOne()
    {
        var card = new DecisionCardView(
            "SML", new DateOnly(2026, 10, 2), "breakout", "S1", "The breakout on the S&P 600", 50m, 47m, null,
            [new CardLineView(1, EquityBrief.Core.Cards.CardLines.TrendName, "tick", "breakout: its rule, the setting; its change approved on the Loop page, exit: the seventh")], null);
        var read = Regex.Replace(WebUtility.HtmlDecode(new MarkRenderer().DecisionCard(card)), "<[^>]+>", " ");

        Assert.Contains("approved on the Monthly check-up", read, StringComparison.Ordinal);
        Assert.DoesNotContain("the Loop page", read, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThePageSaysWhatTheThreeIndexesTogetherSaidAboutTodaysRulesAndNotTheChange()
    {
        using var store = CheckUpStore();

        store.Execute(PooledRow("loop-pooled-1", "pullback", PooledJudge.Own, "GSPC MID SML", "[]", 900, 0.2163, 4, 2, false, null).Replace("'2026-11'", "'2026-10'", StringComparison.Ordinal));

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var opening = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/loop?universe=400"));

        Assert.Contains("Today's rules for this strategy, added up across the S&P 500, S&P 400 and S&P 600 together, did not beat luck: about 1 in 5, better in 2 of 4 years. That is a test of today's rules, not of this change, and it cannot stop a change", opening, StringComparison.Ordinal);
    }
}
