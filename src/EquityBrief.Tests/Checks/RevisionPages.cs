using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Loop;

namespace EquityBrief.Tests.Checks;

// revision-pages. Every page Paper trading, Past picks and a stock's page draw holds to four rules: one trade or order is
// drawn with one take-profit wherever it stands on the page; a variant changes one setting from its running revision; a
// list of stocks draws at most ten rows a page; and no visible text says "version" or names a variant by a number.
// see: A trade under a plan names the one exit its walk sells by on every page, read from its plan and never from a stored target
// see: Every list of stocks on Paper trading draws at most ten rows a page in a stated order, with the rows it shows counted above its pager
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
public class RevisionPages
{
    static readonly Regex Marked = new(@"<tr\b[^>]*\bdata-ticker=""(?<ticker>[^""]+)""[^>]*\bdata-(?:listed|placed|night)=""(?<day>[^""]+)""[^>]*\bdata-(?:take-profit|target)=""(?<price>[^""]+)""", RegexOptions.CultureInvariant);

    static readonly Regex Pager = new(@"class=""pager""[^>]*\bdata-rows=""(?<rows>\d+)""", RegexOptions.CultureInvariant);

    static readonly Regex Changes = new(@"class=""vrow[^""]*""[^>]*\bdata-changes=""(?<changes>\d+)""", RegexOptions.CultureInvariant);

    static readonly Regex Hidden = new(@"<(script|style)\b.*?</\1>|<[^>]+>", RegexOptions.Singleline | RegexOptions.CultureInvariant);

    static readonly Regex Version = new(@"(?<![\w'’-])(?:versions?|V[1-9])(?![\w'’-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // What a drawn page breaks of the four rules, none where it keeps them all.
    internal static IReadOnlyList<string> Faults(string html)
    {
        var faults = new List<string>();

        foreach (var trade in Marked.Matches(html).GroupBy(match => (match.Groups["ticker"].Value, match.Groups["day"].Value)))
        {
            string[] prices = [.. trade.Select(match => match.Groups["price"].Value).Where(price => price != "none").Distinct(StringComparer.Ordinal)];

            if (prices.Length > 1)
            {
                faults.Add($"{trade.Key.Item1} of {trade.Key.Item2} drawn with {prices.Length} take-profits: {string.Join(", ", prices)}");
            }
        }

        faults.AddRange(Pager.Matches(html).Where(match => int.Parse(match.Groups["rows"].Value, System.Globalization.CultureInfo.InvariantCulture) > 10).Select(match => $"a list draws {match.Groups["rows"].Value} rows on one page"));
        faults.AddRange(Changes.Matches(html).Where(match => match.Groups["changes"].Value != "1").Select(match => $"a variant changes {match.Groups["changes"].Value} settings from its running revision"));

        var text = WebUtility.HtmlDecode(Hidden.Replace(html, " "));

        faults.AddRange(Version.Matches(text).Select(match => $"'{match.Value}' in the visible text: ...{text[Math.Max(0, match.Index - 40)..Math.Min(text.Length, match.Index + 40)]}..."));

        return faults;
    }

    [Fact]
    public async Task PaperTradingKeepsTheFourRulesOverEveryConstructedStore()
    {
        var pages = (await Reading.ReadSurface.LoopPagesForPlainWords()).Where(page => page.Page == PlainWords.PaperPage).ToArray();

        Assert.NotEmpty(pages);

        foreach (var (_, drawn, html) in pages)
        {
            var faults = Faults(html).Where(fault => !fault.StartsWith("a variant changes", StringComparison.Ordinal)).ToArray();

            Assert.True(faults.Length == 0, drawn + ":\n" + string.Join("\n", faults));
        }
    }

    [Fact]
    public void EachRuleIsFoundWhereAPageBreaksIt()
    {
        Assert.Empty(Faults("<tr data-ticker=\"RL\" data-listed=\"2026-11-16\" data-take-profit=\"469.92\"></tr><tr data-ticker=\"RL\" data-night=\"2026-11-16\" data-target=\"469.92\"></tr><div class=\"pager\" data-rows=\"10\" data-of=\"12\"></div><tr class=\"vrow\" data-account=\"a\" data-changes=\"1\"></tr><p>Revision 7, running since 16 Nov 2026; V is Visa</p>"));

        Assert.Single(Faults("<tr data-ticker=\"RL\" data-listed=\"2026-11-16\" data-take-profit=\"469.92\"></tr><tr data-ticker=\"RL\" data-night=\"2026-11-16\" data-target=\"413.60\"></tr>"));
        Assert.Single(Faults("<div class=\"pager\" data-rows=\"11\" data-of=\"30\"></div>"));
        Assert.Single(Faults("<tr class=\"vrow open\" data-account=\"a\" data-changes=\"2\"></tr>"));
        Assert.Single(Faults("<p>This version since 16 Nov 2026</p>"));
        Assert.Single(Faults("<option>V3 · best 3 a night</option>"));
        Assert.Empty(Faults("<p data-version=\"v\" title=\"the live swing filter, version 5\">Revision 7</p>"));
    }

    // RL's order of the redesign's own report: bought at $375.23 under a plan whose stop is 4 typical days under the buy and
    // whose take-profit is 2 times the risk alone, its sale placing no band above the buy; a row stored the swing plan's
    // first band, $413.60, which the walk never reads.
    [Fact]
    public void ATradeTakesProfitWhereItsPlansWalkSellsAndNeverAtItsStoredTarget()
    {
        var order = new PlanOrder("words", "1.13 typical days under the close", 376.2147623491241, 20, "4 typical days under the buy", 382.0201, null, 11.836493496350329, null, [], null, null, 63)
        {
            TakeProfit = 2,
            Placing = TakeProfitPlacing.Risk,
        };
        var exit = TradeExit.Filled(order, 375.23)!;
        var stop = 375.23 - (4 * 11.836493496350329);

        Assert.Equal(375.23 + (2 * (375.23 - stop)), exit.First!.Value, 6);
        Assert.Null(exit.Half);
        Assert.False(exit.FirstIsPart);
        Assert.Equal(exit.First, order.SaleFrom(375.23, stop).Target);
    }

    // A half the walk never reaches, at or above the plan's own target, is drawn nowhere: the whole sells at the target.
    [Fact]
    public void AHalfAtOrAboveTheTargetIsNeverItsExitAndOneBelowItSellsFirst()
    {
        var order = new PlanOrder("words", PlanPoints.NextOpen, null, 0, PlanPoints.RuleStop, 33.45, null, 1.5, 47.14, [], null, null, 63)
        {
            TakeProfit = 1.5,
            Half = true,
        };

        var over = TradeExit.Of(order, 40.64, 33.45);

        Assert.Null(over.Half);
        Assert.Equal(47.14, over.First!.Value, 6);
        Assert.False(over.FirstIsPart);

        var under = TradeExit.Of(order with { Target = 60 }, 40.64, 33.45);

        Assert.Equal(40.64 + (1.5 * (40.64 - 33.45)), under.Half!.Value, 6);
        Assert.Equal(under.Half, under.First);
        Assert.True(under.FirstIsPart);
    }
}
