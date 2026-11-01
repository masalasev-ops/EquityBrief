using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.18: a pick's card drawn for a rule at the target standing as a sell order, rendered so its plan's
// words are read off the page.
// see: The target as a sell order standing from the buy is an exit of the menu, and its variant registers where its family has room
public partial class ReadSurface
{
    // A drift pick's card on the S&P 400 under the rule named, bought at 100 with its stop at 95 and its target at 110.
    internal static string StandingCardPlan(string rule) =>
        new MarkRenderer().DecisionCard(new DecisionCardView("MID", new DateOnly(2026, 11, 2), "drift", "KEY", rule, 100m, 95m, 110m, [], null));
}
