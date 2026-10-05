using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One symbol the provider lists on the US exchanges: its code, which every other request names it by, its name, its
// exchange and type as filed, its ISIN where it carries one, and whether it is on the list of those since delisted.
public sealed record ListedSymbol(string Code, string Name, string? Exchange, string? Type, string? Isin, bool Delisted);

// The provider's symbol lists for the US exchanges, the listed and the delisted, one request a list.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
public interface ISymbolListFeed
{
    // How many network requests this feed has made.
    int Requests { get; }

    Task<IReadOnlyList<ListedSymbol>> SymbolsAsync(bool delisted, CancellationToken cancellation = default);
}

public static class ProviderSymbols
{
    // The exchange code the lists are asked under.
    public const string Exchange = "US";

    // The type the provider files a company's common stock under.
    public const string CommonStock = "Common Stock";

    // A list as the provider sends it, every entry with a code; an answer that is not a list, or lists nothing, is
    // refused, since an empty list would match no holding and read as every holding unmatched.
    public static IReadOnlyList<ListedSymbol> Parse(string json, bool delisted)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            throw new FormatException($"The provider's {(delisted ? "delisted" : "listed")} symbol list is not JSON: {failure.Message}", failure);
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                throw new FormatException($"The provider's {(delisted ? "delisted" : "listed")} symbol list is not a list.");
            }

            var symbols = document.RootElement.EnumerateArray()
                .Where(entry => entry.ValueKind is JsonValueKind.Object && Field(entry, "Code") is not null)
                .Select(entry => new ListedSymbol(
                    Field(entry, "Code")!,
                    Field(entry, "Name") ?? string.Empty,
                    Field(entry, "Exchange"),
                    Field(entry, "Type"),
                    Field(entry, "Isin") is { Length: 12 } isin ? isin.ToUpperInvariant() : null,
                    delisted))
                .ToArray();

            return symbols.Length > 0
                ? symbols
                : throw new FormatException($"The provider's {(delisted ? "delisted" : "listed")} symbol list lists no symbol.");
        }
    }

    static string? Field(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;
}

// The symbol lists answered from recorded files, read by the same reader as the provider's answers.
public sealed class RecordedSymbolListFeed(string listed, string gone) : ISymbolListFeed
{
    public int Requests { get; private set; }

    public Task<IReadOnlyList<ListedSymbol>> SymbolsAsync(bool delisted, CancellationToken cancellation = default)
    {
        Requests++;

        return Task.FromResult(ProviderSymbols.Parse(delisted ? gone : listed, delisted));
    }
}
