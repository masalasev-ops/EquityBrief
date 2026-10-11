using System.Text.Json;
using EquityBrief.Core.Families;

namespace EquityBrief.Core.Cards;

// The words a rule's rows carry that the night's stage writes and the pages read: the rule a family's list is drawn by
// on an index no freeze has registered a rule on, where a row came from, and the funnel's form, each gate with how many
// members passed it and every gate before it.
// see: A variant's picks are shown on its card when chosen and its results only under its tests
public static class RuleRows
{
    public const string ProvisionalRule = "provisional";

    public const string FromTheNight = "night";

    public const string FromTheHistory = "history";

    // The parts of an index rule its funnel names, in the rule's order: the market check, the setup and the price and
    // dollar volume floors, then the profit and the interest cover on the swing families, or the fundamentals-first
    // family's readings, profit, revenue, margin, cash and trend.
    public const string Market = "market";

    public const string Setup = "setup";

    public const string Floors = "floors";

    public const string Profit = "profit";

    public const string Cover = "interest cover";

    public const string Readings = "readings";

    public const string Revenue = "revenue";

    public const string Margin = "margin";

    public const string Cash = "cash";

    public const string Trend = "trend";

    // The reason a member fails each part, as the index families' step names it, against the part.
    static readonly Dictionary<string, string> PartOfReason = new(StringComparer.Ordinal)
    {
        ["the market check closed"] = Market,
        ["no setup"] = Setup,
        ["under the price or dollar volume floor"] = Floors,
        ["the profit check"] = Profit,
        ["the interest cover check"] = Cover,
        [FundamentalsRule.NoReadings] = Readings,
        [FundamentalsRule.NoProfit] = Profit,
        [FundamentalsRule.NoRevenue] = Revenue,
        [FundamentalsRule.NoMargin] = Margin,
        [FundamentalsRule.NoCash] = Cash,
        [FundamentalsRule.NoTrend] = Trend,
    };

    // The part a stored funnel names: a part stored under the reason a member fails it is read as the part.
    public static string PartOf(string stored) => PartOfReason.GetValueOrDefault(stored, stored);

    public static string GatesJson(IReadOnlyList<(string Gate, int Passed)> gates) =>
        JsonSerializer.Serialize(gates.Select(gate => new { gate = gate.Gate, passed = gate.Passed }).ToArray());

    public static IReadOnlyList<(string Gate, int Passed)> ReadGates(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray().Select(gate => (PartOf(gate.GetProperty("gate").GetString()!), gate.GetProperty("passed").GetInt32()))];
    }
}
