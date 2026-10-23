using System.Globalization;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.Providers;

// The operator's TipRanks key and the month's calls their subscription allows, stated once.
//
// Both live in the worker's appsettings.Secrets.json beside the provider's key, gitignored and never committed. The
// key goes to TipRanks alone, in the request's header and in no address, and anything about to be written down passes
// through `Redact` first, so a failure's words reach a row, the console or the page with the key taken out.
// see: TipRanks' analyst ratings are asked only on a press, once a stock a day and never past the month's limit
public sealed class TipRanksCredentials(string apiKey)
{
    public const string ApiKeyName = "EquityBrief:Providers:TipRanks:ApiKey";

    // The month's calls as the secrets file states them, read from the number it opens with, "100 Calls/Month" as 100.
    public const string LimitName = "EquityBrief:Providers:TipRanks:Limit";

    public string ApiKey { get; } = string.IsNullOrWhiteSpace(apiKey)
        ? throw new InvalidOperationException(
            $"No TipRanks key. Set '{ApiKeyName}' in the worker's appsettings.Secrets.json, or in the environment.")
        : apiKey.Trim();

    // Never rendered, for the reason the provider's key is not.
    public override string ToString() => Withheld;

    public const string Withheld = "TipRanksCredentials(key withheld)";

    // Anything about to be written down, with the key and any address taken out of it.
    public string Redact(string text) =>
        string.IsNullOrEmpty(text)
            ? text
            : Addresses.Replace(text.Replace(ApiKey, ProviderCredentials.KeyWithheld, StringComparison.Ordinal), ProviderCredentials.AddressWithheld);

    static readonly Regex Addresses = new(@"[a-zA-Z][a-zA-Z0-9+.\-]*://\S*", RegexOptions.Compiled);

    // The limit the secrets file states, read from the whole number it opens with; none where it states none.
    public static int? Limit(string? stated)
    {
        var digits = Regex.Match(stated ?? string.Empty, @"^\s*(\d+)");

        return digits.Success && int.TryParse(digits.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var calls) && calls > 0 ? calls : null;
    }
}
