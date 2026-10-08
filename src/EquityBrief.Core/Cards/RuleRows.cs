using System.Text.Json;

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

    public static string GatesJson(IReadOnlyList<(string Gate, int Passed)> gates) =>
        JsonSerializer.Serialize(gates.Select(gate => new { gate = gate.Gate, passed = gate.Passed }).ToArray());

    public static IReadOnlyList<(string Gate, int Passed)> ReadGates(string? json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);

        return [.. document.RootElement.EnumerateArray().Select(gate => (gate.GetProperty("gate").GetString()!, gate.GetProperty("passed").GetInt32()))];
    }
}
