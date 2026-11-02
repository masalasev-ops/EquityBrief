using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.18: a breakout's pick card says it has no fixed target and why, its give-up price rising and never
// moving down, the close past which it can no longer lose and its rule's record under the rule's name, with no bar and no
// reward against risk; the record's two forms and its count off a rule's paper trades worked by hand; and the copy's plain
// card saying the same.
// see: A breakout idea says it sells on a rising give-up price with no fixed target and names the price at which it can no longer lose
public partial class ReadSurface
{
    // A breakout bought at 50 with its give-up price 4 beneath and no target, its rule holding 3 finished trades.
    static DecisionCardView BreakoutCard(PaperWinners? winners) => new(
        "GSPC",
        new DateOnly(2026, 10, 30),
        BreakoutRule.Name,
        "BBB",
        "the live breakout rule at a 126-session high",
        50m,
        46m,
        null,
        [],
        null,
        Cap: 63,
        Trail: 4m,
        Winners: winners);

    [Fact]
    public void ABreakoutsCardSaysItHasNoFixedTargetHowItsGiveUpPriceRisesAndThePricePastWhichItCanNoLongerLose()
    {
        var card = new MarkRenderer().DecisionCard(BreakoutCard(new PaperWinners(3, 300, null)));

        Assert.Contains(">Sell if it falls to</span><b class=\"loss\">$46.00</b><span class=\"tile-under\">rising as the stock makes new highs; it never moves down.</span>", card, StringComparison.Ordinal);
        Assert.Contains(">Aim to sell at</span><b>No fixed target</b><span class=\"tile-under\">Breakouts earn most from the few that keep running, so the rule lets a winner run and sells only when the price closes below a give-up price that rises behind it.</span>", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"card-noloss\" data-no-loss=\"54\">The trade can no longer lose once the stock reaches about $54.00.</p>", card, StringComparison.Ordinal);
        Assert.Contains("<p class=\"card-winners\" data-finished=\"3\" data-needed=\"300\">The breakout rule has 3 of the 300 finished trades its record needs.</p>", card, StringComparison.Ordinal);

        // No bar, no reward against risk and nothing that reads as a target.
        Assert.DoesNotContain("card-reward", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Rising stop", card, StringComparison.Ordinal);
        Assert.DoesNotContain("Sell if it reaches", card, StringComparison.Ordinal);

        // The copy's plain card says the same.
        var copy = new MarkRenderer { Copying = true }.DecisionCard(BreakoutCard(new PaperWinners(3, 300, null)));

        Assert.Contains("The trade can no longer lose once the stock reaches about $54.00.", copy, StringComparison.Ordinal);
        Assert.Contains("rising as the stock makes new highs; it never moves down.", copy, StringComparison.Ordinal);
        Assert.Contains(MarkRenderer.RunsWords, copy, StringComparison.Ordinal);

        // A card with a target draws none of it.
        var targeted = new MarkRenderer().DecisionCard(BreakoutCard(null) with { Family = SetupFamilies.Pullback, Target = 58m, Trail = null });

        Assert.DoesNotContain("card-noloss", targeted, StringComparison.Ordinal);
        Assert.Contains("Sell if it reaches", targeted, StringComparison.Ordinal);
    }

    [Fact]
    public void ARulesWinningTradesAreItsRecordOnceItHoldsTheCountItNeedsAndTheirCountBeforeIt()
    {
        Assert.Equal("The breakout rule has 299 of the 300 finished trades its record needs.", MarkRenderer.WinnersLine(BreakoutRule.Name, new PaperWinners(299, 300, null)));
        Assert.Equal("The breakout rule's winning trades have made about $1.87 per $1 risked so far, over its 312 finished trades. That is the rule's record, not this stock's chance.", MarkRenderer.WinnersLine(BreakoutRule.Name, new PaperWinners(312, 300, 1.874)));
        Assert.Equal("The breakout rule has made no winning trade over its 300 finished trades. That is the rule's record, not this stock's chance.", MarkRenderer.WinnersLine(BreakoutRule.Name, new PaperWinners(300, 300, null)));

        // Off a rule's paper trades: two finished, one at +2.0 and one at -1.0, and one open, is two of the 300 and no mean.
        static (PaperTrade, double?) Trade(int at, bool ended, double? result) =>
            (new PaperTrade("T" + at, new DateOnly(2026, 10, 1), 50m, 46m, ended ? new DateOnly(2026, 10, 20) : null, ended ? 55m : null, 0.1m, 1), result);

        Assert.Equal(new PaperWinners(2, 300, null), PaperAccount.Winners([Trade(1, true, 2.0), Trade(2, true, -1.0), Trade(3, false, null)]));

        // 300 finished, 150 at +2.5 and 150 at -1.0: the winning trades' mean is 2.5, the losers counted among the finished.
        (PaperTrade, double?)[] three = [.. Enumerable.Range(0, 300).Select(at => Trade(at, true, at % 2 == 0 ? 2.5 : -1.0))];

        Assert.Equal(new PaperWinners(300, 300, 2.5), PaperAccount.Winners(three));
    }
}
