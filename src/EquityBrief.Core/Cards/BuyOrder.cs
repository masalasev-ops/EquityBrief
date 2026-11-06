using EquityBrief.Core.Families;

namespace EquityBrief.Core.Cards;

// Tonight's order of a night's buys by family: the swing families' buy points in the families' page order but the dip's,
// the Fundamentals family's buys, the dip's points, then the monthly books' holdings, the sector heavyweights' before
// Quality at a fair price's; a family the order does not name stands after the swing families', before the Fundamentals.
// see: The combined account holds every live rule's trades in one pot with one position a stock and three a sector, each book family a fifth of it
public static class BuyOrder
{
    public static IReadOnlyList<string> Families { get; } =
        [.. SetupFamilies.OnEveryIndex.Select(family => family.Name), ImprovingRule.Name, DipRule.Name, HeavyweightRule.Name, QualityRule.Name];

    // A family's place in the order, counted from nought.
    public static int Of(string family)
    {
        for (var at = 0; at < Families.Count; at++)
        {
            if (string.Equals(Families[at], family, StringComparison.Ordinal))
            {
                return at;
            }
        }

        return SetupFamilies.OnEveryIndex.Count;
    }
}
