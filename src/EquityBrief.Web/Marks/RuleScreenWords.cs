using EquityBrief.Core.Cards;

namespace EquityBrief.Web.Marks;

// The words a card's selector, band, stretch line and forming list carry, held once so the read surface that chooses
// a rule and the renderer that draws it state the same sentence.
// see: A variant's picks are shown on its card when chosen and its results only under its tests
// see: The forming list advises and never lists a stock
public static class RuleScreenWords
{
    // The link's value for the live rule, so a card with no choice in the link and one choosing the live rule read the
    // same, and the default returns to it.
    public const string LiveSlug = "live";

    public static string Band(int variant) => FormattableString.Invariant($"Variant {variant}: scored in the background, not the live rule. Its picks are not tonight's list.");

    // The share the stretch mark is counted at, in whole per cent, from the mark's own share.
    public static int SharePercent => (int)Math.Round(RuleStretch.Share * 100);

    // The forming list's closing line: what the rule would read of a member that broke out, which is every other gate,
    // as on any night, and never a listing from this list.
    public const string FormingClosingLine =
        "These have not broken out. If one closes above its breakout price on the volume shown, with its range under the rule's ceiling and the market check open, the rule reads its other gates that night as on any other, and lists five at most.";
}
