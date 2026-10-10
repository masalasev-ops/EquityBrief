using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Ladders;

// The plan the ladder builder stored, read back as the ladder's own records: its tranches, its exits and its
// invalidation. One reader, which the name page and the research pass both call.
public static class StoredPlan
{
    public static (IReadOnlyList<Tranche> Tranches, IReadOnlyList<Exit> Exits, decimal? Invalidation) Read(string plan)
    {
        using var document = JsonDocument.Parse(plan);
        var root = document.RootElement;

        static decimal Edge(JsonElement holder, string name) =>
            decimal.Parse(holder.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

        Tranche[] tranches =
        [
            .. root.GetProperty("tranches").EnumerateArray().Select(tranche => new Tranche(
                Edge(tranche, "lowEdge"),
                Edge(tranche, "highEdge"),
                Enum.Parse<TrancheCondition>(tranche.GetProperty("condition").GetString()!),
                tranche.GetProperty("stop").GetString() is { } stop ? decimal.Parse(stop, CultureInfo.InvariantCulture) : null)),
        ];
        Exit[] exits =
        [
            .. root.GetProperty("exits").EnumerateArray().Select(exit => new Exit(
                Edge(exit, "lowEdge"),
                Edge(exit, "highEdge"),
                exit.GetProperty("traded").GetBoolean(),
                exit.GetProperty("trailing").GetBoolean(),
                exit.GetProperty("fraction").GetString() ?? "0",
                exit.GetProperty("reason").GetString())),
        ];
        decimal? invalidation = root.TryGetProperty("invalidation", out var stored) && stored.GetString() is { } price
            ? decimal.Parse(price, CultureInfo.InvariantCulture)
            : null;

        return (tranches, exits, invalidation);
    }
}
