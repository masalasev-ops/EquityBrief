using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Report;

// The concepts a page draws one value of, each marked where it is drawn with its key and the value as drawn, and the check
// that reads a drawn page back and names every concept drawn with two different values.
// see: Each concept a page draws holds one value worked at the stored close beside the provider's own marked stale where struck far from it, and a check flags any concept drawn twice
public static partial class ConceptCheck
{
    public const string MarketValue = "market-value";
    public const string TrailingMultiple = "trailing-multiple";
    public const string ForwardMultiple = "forward-multiple";
    public const string YieldAtTheClose = "yield-at-the-close";
    public const string YearsHigh = "years-high";
    public const string IndustryMedian = "industry-median";

    public static IReadOnlyList<string> Concepts { get; } = [MarketValue, TrailingMultiple, ForwardMultiple, YieldAtTheClose, YearsHigh, IndustryMedian];

    // A yield as every place draws it: in per cent to two places.
    public static string Yield(double fraction) => (fraction * 100).ToString("0.00", CultureInfo.InvariantCulture) + "%";

    // An industry's median multiple is a concept of that industry: a median over another group, an override's among them,
    // is another concept.
    public static string MedianOf(string industry) => IndustryMedian + " of " + industry;

    // Each concept the page marks with the values it is drawn with, in the order first met.
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> Drawn(string html)
    {
        var drawn = new Dictionary<string, List<string>>(StringComparer.Ordinal);

        foreach (Match mark in Marked().Matches(html))
        {
            var concept = mark.Groups[1].Value;
            var value = System.Net.WebUtility.HtmlDecode(mark.Groups[2].Value);

            if (!drawn.TryGetValue(concept, out var values))
            {
                drawn[concept] = values = [];
            }

            if (!values.Contains(value, StringComparer.Ordinal))
            {
                values.Add(value);
            }
        }

        return drawn.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value, StringComparer.Ordinal);
    }

    // The concepts drawn with more than one value.
    public static IReadOnlyList<(string Concept, IReadOnlyList<string> Values)> Clashes(string html) =>
        [.. Drawn(html).Where(pair => pair.Value.Count > 1).Select(pair => (pair.Key, pair.Value))];

    [GeneratedRegex("data-concept=\"([^\"]+)\" data-concept-value=\"([^\"]*)\"")]
    private static partial Regex Marked();
}
