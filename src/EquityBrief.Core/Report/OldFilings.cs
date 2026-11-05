using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Report;

// What a stored filing row carries from the archive against the row's own filing date: the archive's fetch took the filer
// the provider named, for a company that changed its legal entity an old one's, and that filer's newest filing, so a part
// can be years older than the row it sits on. Anything older by more than the limit is neither drawn nor handed to a writer.
// see: A part filed more than 200 days before the page's own filing is neither drawn nor handed to a writer, and a fetch takes the filer the SEC names
public static class OldFilings
{
    public const int MostDays = 200;

    public static bool TooOld(DateOnly stated, DateOnly filed) => filed.DayNumber - stated.DayNumber > MostDays;

    // A stored table's newest period's end, a balance date among them.
    public static DateOnly? TableEnded(JsonElement table) =>
        table.ValueKind == JsonValueKind.Object && table.TryGetProperty("periods", out var periods) && periods.ValueKind == JsonValueKind.Array
            ? periods.EnumerateArray()
                .Select(period => Day(period, "ended"))
                .Max()
            : null;

    public static bool TableTooOld(JsonElement table, DateOnly filed) => TableEnded(table) is { } ended && TooOld(ended, filed);

    // The day a row's stored guidance was filed.
    public static DateOnly? GuidanceFiled(JsonElement root) =>
        root.TryGetProperty("guidance", out var guidance) && guidance.ValueKind == JsonValueKind.Object ? Day(guidance, "filedOn") : null;

    public static bool GuidanceTooOld(JsonElement root, DateOnly filed) => GuidanceFiled(root) is { } on && TooOld(on, filed);

    // What the page says of a part left out.
    public static string Why(string what, DateOnly stated, DateOnly? filed) =>
        FormattableString.Invariant($"{what} {stated:yyyy-MM-dd}, more than {MostDays} days before this filing{(filed is { } on ? FormattableString.Invariant($" of {on:yyyy-MM-dd}") : string.Empty)}, so it is left out until the company next files");

    static DateOnly? Day(JsonElement element, string name) =>
        element.TryGetProperty(name, out var day) && day.ValueKind == JsonValueKind.String
        && DateOnly.TryParseExact(day.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var read)
            ? read
            : null;
}
