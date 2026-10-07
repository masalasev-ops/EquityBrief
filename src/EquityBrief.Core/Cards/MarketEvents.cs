using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Cards;

// One market event a hold can carry: its date, its kind, its name in words and the page it was read from.
public sealed record MarketEvent(DateOnly Date, string Kind, string Name, string Source);

// The FOMC decisions and CPI releases a pick's card reads inside a hold, from the table committed at the repository's
// root and carried in the build, asked of no provider. Each kind's last date is where the table ends for it, so a hold
// running past that date says so rather than reading the absence as no event.
// see: Market events inside a hold are read from a committed table of the Fed's and the BLS's own dates, and asked of no provider
public static class MarketEvents
{
    public const string ResourceName = "market-events.json";

    public static IReadOnlyList<MarketEvent> All { get; } = Read(typeof(MarketEvents).Assembly.GetManifestResourceStream(ResourceName)
        ?? throw new InvalidOperationException($"The build carries no {ResourceName}."));

    // The events dated inside a hold, from the session after the night through the hold's last session, and each kind
    // whose table ends before that last session.
    public static (IReadOnlyList<MarketEvent> Inside, IReadOnlyList<string> PastTheTable) Within(DateOnly from, DateOnly through, IReadOnlyList<MarketEvent>? table = null)
    {
        var events = table ?? All;

        return (
            [.. events.Where(one => one.Date >= from && one.Date <= through).OrderBy(one => one.Date)],
            [.. events.GroupBy(one => one.Name).Where(kind => kind.Max(one => one.Date) < through).Select(kind => kind.Key).Order(StringComparer.Ordinal)]);
    }

    public static IReadOnlyList<MarketEvent> Read(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;
        var kinds = root.GetProperty("kinds");
        var events = new List<MarketEvent>();

        foreach (var row in root.GetProperty("events").EnumerateArray())
        {
            var kind = row.GetProperty("kind").GetString()!;
            var described = kinds.GetProperty(kind);

            events.Add(new MarketEvent(
                DateOnly.ParseExact(row.GetProperty("date").GetString()!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                kind,
                described.GetProperty("name").GetString()!,
                described.GetProperty("source").GetString()!));
        }

        return events;
    }
}
