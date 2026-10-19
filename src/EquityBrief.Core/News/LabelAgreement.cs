using System.Text.Json;
using System.Text.Json.Nodes;

namespace EquityBrief.Core.News;

// The local model's labels of the articles a paid model labelled, set beside the paid labels: the articles compared,
// those the local model labelled and those it could not with each cause, how many of its labels agree with the paid one
// on the kind, on the direction and on both, and how many of the reasons its answers stated held no digit, every answer
// it gave counted, a retry's among them.
// see: A comparison of the local model against stored paid work writes no store row and switches nothing
public sealed record LabelAgreement(
    int Compared,
    int Labelled,
    IReadOnlyDictionary<string, int> Unreadable,
    int Kind,
    int Direction,
    int Both,
    int Reasons,
    int ReasonsWithoutDigit)
{
    // One article: the paid model's label, the local model's or the cause it has none, and every answer the local model
    // gave for it.
    public sealed record Pair(NewsLabel Paid, NewsLabel? Local, string? Cause, IReadOnlyList<string> Answers);

    public static LabelAgreement Of(IReadOnlyList<Pair> pairs)
    {
        var labelled = pairs.Where(pair => pair.Local is not null).ToArray();
        var reasons = pairs.SelectMany(pair => pair.Answers).Select(ReasonOf).OfType<string>().ToArray();

        return new LabelAgreement(
            pairs.Count,
            labelled.Length,
            pairs
                .Where(pair => pair.Local is null)
                .GroupBy(pair => pair.Cause ?? NewsInstruction.NotJson, StringComparer.Ordinal)
                .ToDictionary(cause => cause.Key, cause => cause.Count(), StringComparer.Ordinal),
            labelled.Count(pair => string.Equals(pair.Local!.Kind, pair.Paid.Kind, StringComparison.Ordinal)),
            labelled.Count(pair => string.Equals(pair.Local!.Direction, pair.Paid.Direction, StringComparison.Ordinal)),
            labelled.Count(pair => string.Equals(pair.Local!.Kind, pair.Paid.Kind, StringComparison.Ordinal)
                && string.Equals(pair.Local.Direction, pair.Paid.Direction, StringComparison.Ordinal)),
            reasons.Length,
            reasons.Count(reason => !reason.Any(char.IsDigit)));
    }

    // The reason an answer states, its object found as the instruction's own reader finds it, or none where the answer
    // holds no object or no reason in words.
    public static string? ReasonOf(string answer)
    {
        var trimmed = answer.Trim();
        var start = trimmed.IndexOf('{', StringComparison.Ordinal);
        var end = trimmed.LastIndexOf('}');

        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            return JsonNode.Parse(trimmed[start..(end + 1)]) is JsonObject held
                && held["reason"] is JsonValue value
                && value.TryGetValue<string>(out var reason)
                && reason.Trim().Length > 0
                    ? reason.Trim()
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
