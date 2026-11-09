using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Families;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using Xunit;

namespace EquityBrief.Tests.Reading;

// The 22.16 correction of 2026-11-09's next: a traded plan that buys at a zone, which none does before 23.10, draws on Home's
// card and on a pick's card the price it waits for, how long and tonight's close, worked here over a constructed zone plan.
// see: A plan that buys at a zone draws the price it waits for and how long it waits beside tonight's close on every card
public partial class ReadSurface
{
    internal static string[] ZonePlanRows => [CheckReach.Key("15.18 The decision card", "A plan that buys at a zone")];

    [Fact]
    public void AZonePlanDrawsThePriceItWaitsForHowLongAndTonightsCloseOnEveryCard()
    {
        const string Waits = "Tonight's close was $51.30. The buy waits for the price to dip to about $49.20 within 20 trading days; if it never does, there is no trade.";
        var zone = new ZoneBuy(49.20m, 20, 51.30m);

        // Home's card, as the mockup's state after the restart draws it: the four prices in its words and the line beneath them,
        // before the entry check line.
        var home = new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [new HomeIdea(SetupFamilies.Pullback, "BMY", null, "A dip.", 49.20m, 47.50m, 58m, false, null, EntryCheck: EntryFlagged(), Zone: zone)], IdeasCount = 1 }, string.Empty);
        var card = Regex.Match(home, "<article class=\"home-idea.*?</article>", RegexOptions.Singleline).Value;

        Assert.Contains("Buy if it dips to about$49.20Sell if it closes below$47.50Aim to sell at$58.00", Read(card), StringComparison.Ordinal);
        Assert.Equal(Waits, Read(Regex.Match(card, "<p class=\"home-cond\">.*?</p>").Value));
        Assert.True(card.IndexOf("home-cond", StringComparison.Ordinal) < card.IndexOf("entry-card-line", StringComparison.Ordinal));

        // A plan with no target names its time limit in the target's place.
        var dip = Read(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [new HomeIdea(DipRule.Name, "IFF", null, "A fall.", 80m, 76m, null, false, null, Zone: new ZoneBuy(80m, 10, 82.5m, 10))], IdeasCount = 1 }, string.Empty));

        Assert.Contains("Otherwise sell after10 trading days", dip, StringComparison.Ordinal);
        Assert.Contains("within 10 trading days; if it never does, there is no trade.", dip, StringComparison.Ordinal);

        // A pick's card and the read-only copy's: the buy tile waits for the zone and the line follows the plan's tiles.
        var row = new DecisionCardRow("GSPC", EntryNight, SetupFamilies.Pullback, "BMY", 1, 51.30m, 47.50m, 58m, "the rule", "{}", "[]", null, Cap: 20);
        var view = CardScreen.View(row, CardContext.None) with { Zone = zone };

        foreach (var drawn in new[] { new MarkRenderer().DecisionCard(view), new MarkRenderer { Copying = true }.DecisionCard(view) })
        {
            Assert.Contains("Buy if it dips to about", Read(drawn), StringComparison.Ordinal);
            Assert.Contains(Waits, Read(drawn), StringComparison.Ordinal);
        }

        // With no zone every card reads as it did.
        Assert.DoesNotContain("Buy if it dips", Read(new MarkRenderer().DecisionCard(CardScreen.View(row, CardContext.None))), StringComparison.Ordinal);
        Assert.Contains("Buy at about", Read(new SinglePageApp().HomeRegion(new MarkRenderer(), Day() with { Ideas = [new HomeIdea(SetupFamilies.Pullback, "BMY", null, "A dip.", 49.20m, 47.50m, 58m, false, null)], IdeasCount = 1 }, string.Empty)), StringComparison.Ordinal);
    }
}
