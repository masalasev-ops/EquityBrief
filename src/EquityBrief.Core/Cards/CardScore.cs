using System.Text.Json;
using System.Text.Json.Serialization;

namespace EquityBrief.Core.Cards;

// The learned score's part of a pick's card as the night stored it: the setups like the pick under its rule on its index,
// the readings matched on, how many were matched and their median distance, their mean edge with its ninety per cent
// interval and the mean of every finished setup the rule passed on the index; or why none were matched.
// see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
public sealed record CardSimilar(
    [property: JsonPropertyName("readings")] IReadOnlyList<string>? Readings = null,
    [property: JsonPropertyName("count")] int? Count = null,
    [property: JsonPropertyName("distance")] double? Distance = null,
    [property: JsonPropertyName("mean")] double? Mean = null,
    [property: JsonPropertyName("low")] double? Low = null,
    [property: JsonPropertyName("high")] double? High = null,
    [property: JsonPropertyName("rule_mean")] double? RuleMean = null,
    [property: JsonPropertyName("rule_setups")] int? RuleSetups = null,
    [property: JsonPropertyName("unmatched")] string? Unmatched = null)
{
    // Whether the interval holds the rule's own mean, where the part is not told from the rule's record.
    public bool Distinguishable => RuleMean is { } rule && Low is { } low && High is { } high && (rule < low || rule > high);

    static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string Json() => JsonSerializer.Serialize(this, Options);

    public static CardSimilar Read(string json) => JsonSerializer.Deserialize<CardSimilar>(json, Options) ?? new CardSimilar(Unmatched: "the part could not be read");

    // The sentences the part is drawn with: under the rule's heading, never as the stock's own, and where its interval
    // holds the rule's own mean, that it is not told from the rule's record.
    public static string Heading(string index) => $"Setups like this one under this rule on the {index}, not this stock's chance.";

    public const string NotDistinguishable = "Not distinguishable from the rule's record.";

    // The words drawn on a card of a family a score reaches until the score has passed the tester on the card's index.
    public const string NotValidated = "Score not yet validated on this index";
}
