using System.Text.Json;

namespace EquityBrief.Core.Providers;

// What OpenFIGI answers for one identifier: the tickers it traded under on the US venues, the placeholders a delisted
// security is given left out, the name it files the security under, and the warning where it found none.
public sealed record IdentifierMapping(string Identifier, IReadOnlyList<string> Tickers, string? Name, string? Warning);

// OpenFIGI's mapping of identifiers to tickers, at most ten identifiers a request, asked by the history pull alone and
// never on a night, for the holdings the provider's symbol lists carry under no code.
// see: A holding the symbol lists carry under no code is mapped to the tickers it traded under through OpenFIGI, each held to the checks a name's code is
public interface IOpenFigiMappingFeed
{
    // How many network requests this feed has made.
    int Requests { get; }

    // The answer, as sent, to one request mapping the identifiers given, ISINs, in their order.
    Task<string> MapAsync(IReadOnlyList<string> isins, CancellationToken cancellation = default);
}

public static class OpenFigiMappings
{
    // How many identifiers one keyless request may carry, and how many requests a minute the keyless limit allows,
    // both off the answer's own rate limit headers of 2026-10-08.
    public const int IdentifiersARequest = 10;
    public const int RequestsAMinute = 25;

    // The venue codes OpenFIGI answered the 27 identifiers of 2026-10-08 under for the US market, the composite among
    // them; a ticker on any other venue is a listing elsewhere. Read off the capture rather than off a published list.
    public static readonly IReadOnlyList<string> UnitedStatesVenues =
    [
        "UA", "UB", "UC", "UD", "UF", "UI", "UJ", "UL", "UM", "UN", "UO", "UP", "UQ", "UR", "US", "UT", "UU", "UV", "UW", "UX",
    ];

    // The identifier kind the pull asks under, and the flag without which a delisted security's listings are not
    // answered, which every one of the 27 was found to need.
    public const string IsinKind = "ID_ISIN";

    // One request's body: the identifiers as mapping jobs, each asking for the unlisted listings as well.
    public static string Request(IReadOnlyList<string> isins)
    {
        if (isins.Count is 0 or > IdentifiersARequest)
        {
            throw new ArgumentOutOfRangeException(nameof(isins), isins.Count, $"A request maps one to {IdentifiersARequest} identifiers.");
        }

        return JsonSerializer.Serialize(isins.Select(isin => new { idType = IsinKind, idValue = isin, includeUnlistedEquities = true }));
    }

    // An answer as OpenFIGI sends it: a list with one entry an identifier asked, in the order asked, each a list of
    // listings under `data` or a `warning`. An answer that is not such a list, or whose count differs from the
    // identifiers asked, is refused, since a shorter list read in order would hand one identifier another's tickers.
    public static IReadOnlyList<IdentifierMapping> Parse(string json, IReadOnlyList<string> asked)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            throw new FormatException($"OpenFIGI's answer is not JSON: {failure.Message}", failure);
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not JsonValueKind.Array)
            {
                throw new FormatException("OpenFIGI's answer is not a list.");
            }

            var entries = document.RootElement.EnumerateArray().ToArray();

            if (entries.Length != asked.Count)
            {
                throw new FormatException($"OpenFIGI answered {entries.Length} entries for {asked.Count} identifiers asked.");
            }

            return [.. asked.Select((isin, at) => Read(isin, entries[at]))];
        }
    }

    static IdentifierMapping Read(string isin, JsonElement entry)
    {
        if (entry.ValueKind is not JsonValueKind.Object)
        {
            return new(isin, [], null, "The entry is not an object.");
        }

        if (entry.TryGetProperty("data", out var data) && data.ValueKind is JsonValueKind.Array)
        {
            var listings = data.EnumerateArray()
                .Where(listing => listing.ValueKind is JsonValueKind.Object
                    && Field(listing, "exchCode") is { } venue && UnitedStatesVenues.Contains(venue, StringComparer.Ordinal)
                    && Field(listing, "ticker") is { } ticker && !IsPlaceholder(ticker))
                .ToArray();

            return new(
                isin,
                [.. listings.Select(listing => Field(listing, "ticker")!).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
                listings.Select(listing => Field(listing, "name")).FirstOrDefault(name => name is not null),
                null);
        }

        return new(isin, [], null, Field(entry, "warning") ?? Field(entry, "error") ?? "The entry carries neither listings nor a warning.");
    }

    // The ticker a delisted security is given in place of the one it traded under: digits and a closing D, as
    // 9990620D for GrubHub, which no provider files a price under.
    public static bool IsPlaceholder(string ticker) =>
        ticker.Length > 1 && ticker[^1] == 'D' && ticker[..^1].All(char.IsAsciiDigit);

    static string? Field(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;
}

// OpenFIGI's answers from recorded files, keyed by the identifiers a request carries in their order, read by the same
// reader as the live answers; a request the recording does not hold is refused.
public sealed class RecordedOpenFigiMappingFeed(IReadOnlyDictionary<string, string> answers) : IOpenFigiMappingFeed
{
    public int Requests { get; private set; }

    public static string Key(IReadOnlyList<string> isins) => string.Join(',', isins);

    public Task<string> MapAsync(IReadOnlyList<string> isins, CancellationToken cancellation = default)
    {
        Requests++;

        return answers.TryGetValue(Key(isins), out var answer)
            ? Task.FromResult(answer)
            : throw new ProviderRefusal($"No recorded OpenFIGI answer for {Key(isins)}.", transient: false);
    }
}
