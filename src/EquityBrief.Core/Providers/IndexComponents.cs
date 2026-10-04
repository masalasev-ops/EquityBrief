using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One of an index's members today, as the index's fundamentals answer lists it under its components: the listing's code
// and exchange, and the company's name, sector and industry as filed beside it, each null where none is filed. It
// carries no date, since the components are today's snapshot: an index answered with them alone, as the S&P 400 and the
// S&P 600 are on this key, says nothing of who was a member before today, so every name read here is a survivor.
// see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
public sealed record IndexComponent(string Ticker, string Exchange, string? Name, string? Sector, string? Industry);

// Today's components of an index the night does not keep, asked once an index by the history pull and never by a night.
public interface IIndexComponentsFeed
{
    // How many network requests this feed has made.
    int Requests { get; }

    Task<IReadOnlyList<IndexComponent>> ComponentsAsync(string indexCode, CancellationToken cancellation = default);
}

// Reads today's components off an index's fundamentals answer. The answer keys each component by its place, and the
// listing's code is what a pull asks for, so a component filing no code is passed over and one filed twice is read
// once. An answer carrying no components, or none with a code, is refused, since an empty index lists nobody to pull.
public static class IndexComponents
{
    public const string Snapshot = "Components";

    public static IReadOnlyList<IndexComponent> Parse(string json, string indexCode)
    {
        using var document = JsonDocument.Parse(json);

        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.TryGetProperty(Snapshot, out var components)
            || components.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException(
                $"The answer for {indexCode} carries no {Snapshot} object, so it lists no member today.");
        }

        var read = components
            .EnumerateObject()
            .Select(entry => entry.Value)
            .Where(entry => entry.ValueKind == JsonValueKind.Object && Text(entry, "Code") is not null)
            .Select(entry => new IndexComponent(Text(entry, "Code")!, Text(entry, "Exchange") ?? string.Empty, Text(entry, "Name"), Text(entry, "Sector"), Text(entry, "Industry")))
            .GroupBy(component => component.Ticker, StringComparer.Ordinal)
            .Select(listing => listing.First())
            .OrderBy(component => component.Ticker, StringComparer.Ordinal)
            .ToArray();

        return read.Length > 0
            ? read
            : throw new FormatException($"The answer for {indexCode} lists no component with a code, so it names nobody to pull.");
    }

    static string? Text(JsonElement entry, string field) =>
        entry.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text
            ? text
            : null;
}
