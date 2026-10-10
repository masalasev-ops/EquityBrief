using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// The provider's delayed quote for one listing: the instant it is as of, the price, the previous session's close and the
// change on it, each as the provider states it.
public sealed record ProviderQuote(DateTimeOffset QuotedAt, decimal Price, decimal? PreviousClose, decimal? Change, double? ChangePercent);

// The delayed quote, one request a listing.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
public interface IQuoteFeed
{
    // How many network requests this feed has made.
    int Requests { get; }

    // The listing's quote, none where the provider holds no price for it.
    Task<ProviderQuote?> QuoteAsync(string ticker, CancellationToken cancellation = default);
}

public static class ProviderQuotes
{
    // A quote as the provider sends it: an object whose timestamp is the quote's own instant in seconds since the epoch, its
    // close the price and its previous close, change and change in per cent beside it. A price the provider does not hold
    // comes as the string "NA", which reads as no quote; an answer that is no object, or holds no timestamp, is refused.
    public static ProviderQuote? Parse(string json)
    {
        JsonDocument document;

        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException failure)
        {
            throw new FormatException($"The provider's quote is not JSON: {failure.Message}", failure);
        }

        using (document)
        {
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException($"The provider's quote is a {root.ValueKind} where an object was expected.");
            }

            if (!root.TryGetProperty("timestamp", out var stamp) || stamp.ValueKind != JsonValueKind.Number || !stamp.TryGetInt64(out var seconds))
            {
                return Money(root, "close") is null
                    ? null
                    : throw new FormatException("The provider's quote carries a price and no timestamp it can be dated by.");
            }

            if (Money(root, "close") is not { } price)
            {
                return null;
            }

            return new ProviderQuote(
                DateTimeOffset.FromUnixTimeSeconds(seconds),
                price,
                Money(root, "previousClose"),
                Money(root, "change"),
                root.TryGetProperty("change_p", out var percent) && percent.ValueKind == JsonValueKind.Number ? percent.GetDouble() : null);
        }
    }

    // A figure as the provider wrote it, read from its own digits so none is lost to a double on the way, and none where
    // the provider writes "NA" or nothing.
    static decimal? Money(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number
            ? decimal.Parse(value.GetRawText(), NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture)
            : null;
}
