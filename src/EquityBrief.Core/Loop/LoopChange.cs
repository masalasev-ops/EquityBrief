using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Loop;

// The change a proposal makes to the rule it was tested against, in the form an approval applies it: a swing family's
// setting on its sweep's grid by its places and its key, the stop floor a drift reads beside it, and the hooks a
// registration states. A part the change does not move is not stated.
// see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
public sealed record LoopChange(IReadOnlyList<int>? Places, string? Key, double? StopFloor, IReadOnlyDictionary<string, double> Hooks)
{
    public static IReadOnlyDictionary<string, double> NoHooks { get; } = new Dictionary<string, double>(StringComparer.Ordinal);

    public static LoopChange OfSetting(IReadOnlyList<int> places, string key, double stopFloor) =>
        new([.. places], key, stopFloor > 0 ? stopFloor : null, NoHooks);

    public static LoopChange OfHooks(IReadOnlyDictionary<string, double> hooks) =>
        new(null, null, null, hooks.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));

    // Whether the change moves anything at all.
    public bool Moves => Places is not null || Hooks.Count > 0;

    public string Json =>
        JsonSerializer.Serialize(new
        {
            places = Places,
            key = Key,
            floor = StopFloor,
            hooks = new SortedDictionary<string, double>(Hooks.ToDictionary(pair => pair.Key, pair => pair.Value), StringComparer.Ordinal),
        });

    // A change read back from its stored text, none where none is stored or the text is not a change.
    public static LoopChange? Read(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return null;
        }

        try
        {
            using var read = JsonDocument.Parse(stored);
            var root = read.RootElement;
            IReadOnlyList<int>? places = root.TryGetProperty("places", out var held) && held.ValueKind == JsonValueKind.Array
                ? [.. held.EnumerateArray().Select(place => place.GetInt32())]
                : null;
            var key = root.TryGetProperty("key", out var named) && named.ValueKind == JsonValueKind.String ? named.GetString() : null;
            double? floor = root.TryGetProperty("floor", out var stop) && stop.ValueKind == JsonValueKind.Number ? stop.GetDouble() : null;
            var hooks = root.TryGetProperty("hooks", out var stated) && stated.ValueKind == JsonValueKind.Object
                ? stated.EnumerateObject().ToDictionary(pair => pair.Name, pair => pair.Value.GetDouble(), StringComparer.Ordinal)
                : new Dictionary<string, double>(StringComparer.Ordinal);

            return new LoopChange(places, key, floor, hooks);
        }
        catch (Exception unread) when (unread is JsonException or InvalidOperationException or FormatException)
        {
            return null;
        }
    }

    // This change made to a rule standing at a setting and hooks: its setting and floor where it states a setting, the
    // rule's otherwise; its exit in place of the rule's; its conditions beside the rule's, one on the same reading and
    // side replacing it; and its score, where it states one, in place of the rule's whole score.
    public LoopChange OnTopOf(LoopChange? standing)
    {
        var hooks = new Dictionary<string, double>(standing?.Hooks ?? NoHooks, StringComparer.Ordinal);

        if (Hooks.Keys.Any(IsScore))
        {
            foreach (var score in hooks.Keys.Where(IsScore).ToArray())
            {
                hooks.Remove(score);
            }
        }

        foreach (var (name, value) in Hooks)
        {
            hooks[name] = value;
        }

        return Places is not null
            ? new LoopChange(Places, Key, StopFloor, hooks)
            : new LoopChange(standing?.Places, standing?.Key, standing?.StopFloor, hooks);
    }

    static bool IsScore(string name) =>
        name == RuleHooks.ScoreFloorParameter || name.StartsWith(RuleHooks.ScorePrefix, StringComparison.Ordinal);

    // The change in words: its setting by its key with its floor, and its hooks' own words.
    public string Words()
    {
        var parts = new List<string>();

        if (Key is { } key)
        {
            parts.Add("the setting " + key.Replace("|", ", ", StringComparison.Ordinal).Replace("=", " ", StringComparison.Ordinal)
                + (StopFloor is { } floor ? FormattableString.Invariant($", its stop at least {floor.ToString("0.##", CultureInfo.InvariantCulture)} typical move under the buy") : string.Empty));
        }

        if (RuleHooks.Of(Hooks).Words() is { } hooked)
        {
            parts.Add(hooked);
        }

        return parts.Count == 0 ? "no change" : string.Join("; ", parts);
    }
}
