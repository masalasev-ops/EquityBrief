using EquityBrief.Core.Families;

namespace EquityBrief.Core.Paper;

// A setup in a few plain words, as every page names it and the night's order changes name who placed an order: one map in the
// core, which the pages and the worker read alike.
public static class SetupNames
{
    public static IReadOnlyDictionary<string, string> Short { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [SetupFamilies.Pullback] = "Bounce in an uptrend",
        [BreakoutRule.Name] = "New high",
        [DriftRule.Name] = "Strong earnings",
        [FundamentalsRule.Name] = "Growing and profitable",
        [ImprovingRule.Name] = "Getting stronger as a business",
        [DipRule.Name] = "Short-term dip",
        [HeavyweightRule.Name] = "Sector heavyweights",
        [QualityRule.Name] = "Quality at a fair price",
    };

    // The setup's plain words, or its family's own name where none is kept.
    public static string ShortOf(string family) => Short.GetValueOrDefault(family) ?? family;

    // Who placed an order, as the order changes name it: the setup in plain words and its index.
    public static string WhoOf(string family, string index) => ShortOf(family) + " on the " + index;
}
