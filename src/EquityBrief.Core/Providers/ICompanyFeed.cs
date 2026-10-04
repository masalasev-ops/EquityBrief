using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Providers;

// One quarterly balance sheet's share count: the quarter it closes, the day it was filed and the shares outstanding,
// which the provider restates to the split basis of the day it is asked.
public sealed record SheetCount(DateOnly PeriodEnd, DateOnly FilingDate, decimal Shares);

// One company as the provider files it: its CIK, its GICS sector, industry group, industry and sub-industry, the day
// it was delisted, its quarterly share counts carrying a filing date, and how many of its quarterly balance sheets
// carry no count or no filing date and so cannot be read as they stood.
public sealed record CompanyAnswer(
    string Ticker,
    string? Cik,
    string? Sector,
    string? IndustryGroup,
    string? Industry,
    string? SubIndustry,
    DateOnly? DelistedOn,
    IReadOnlyList<SheetCount> Counts,
    int SheetsUncounted);

// One company's classification, filer and quarterly share counts, in one request a name.
//
// Asked by the history pull on the operator's command and by no night, through the fundamentals endpoint at its
// weight of ten with a filter naming the fields the pull stores, so a name costs what the whole answer costs and
// carries a fraction of it. Departed companies answer as current ones do.
// see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
public interface ICompanyFeed
{
    int Requests { get; }

    Task<CompanyAnswer> CompanyAsync(string ticker, CancellationToken cancellation = default);
}

// The company answer, read.
//
// Written against two captured answers, a current member and one delisted in 2023. Asked with a filter, the provider
// answers one flat object keyed by each path the filter names rather than the nested object it answers without one,
// so a reader of the unfiltered answer finds none of these fields. A field it holds nothing for is the string NA, and
// the CIK arrives padded to ten digits as text where another of the provider's answers sends it as a number.
public static class CompanyAnswers
{
    public const string Code = "General::Code";
    public const string Cik = "General::CIK";
    public const string Sector = "General::GicSector";
    public const string IndustryGroup = "General::GicGroup";
    public const string Industry = "General::GicIndustry";
    public const string SubIndustry = "General::GicSubIndustry";
    public const string IsDelisted = "General::IsDelisted";
    public const string DelistedDate = "General::DelistedDate";
    public const string Sheets = "Financials::Balance_Sheet::quarterly";

    // The filter the request sends, each path a key of the answer.
    public static IReadOnlyList<string> Filter { get; } = [Code, Cik, Sector, IndustryGroup, Industry, SubIndustry, IsDelisted, DelistedDate, Sheets];

    public static CompanyAnswer Parse(string json, string ticker)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind is not JsonValueKind.Object)
        {
            throw new FormatException(
                $"The company answer for {ticker} is not one object keyed by the filter's paths, so it cannot be read as "
                + "a company, and reading it as one with nothing filed would store a company that is not there.");
        }

        var counts = new List<SheetCount>();
        var uncounted = 0;

        if (root.TryGetProperty(Sheets, out var sheets) && sheets.ValueKind is JsonValueKind.Object)
        {
            foreach (var sheet in sheets.EnumerateObject())
            {
                if (sheet.Value.ValueKind is JsonValueKind.Object
                    && Date(Text(sheet.Value, "date") ?? sheet.Name) is { } periodEnd
                    && Date(Text(sheet.Value, "filing_date")) is { } filed
                    && Number(sheet.Value, "commonStockSharesOutstanding") is { } shares
                    && shares > 0m)
                {
                    counts.Add(new SheetCount(periodEnd, filed, shares));
                }
                else
                {
                    uncounted++;
                }
            }
        }

        return new CompanyAnswer(
            ticker,
            Filer(root),
            Text(root, Sector),
            Text(root, IndustryGroup),
            Text(root, Industry),
            Text(root, SubIndustry),
            Date(Text(root, DelistedDate)),
            [.. counts.OrderBy(count => count.PeriodEnd)],
            uncounted);
    }

    // The CIK padded to the ten digits the archive is addressed by, from text or a number, none where none is filed.
    static string? Filer(JsonElement root)
    {
        var filed = !root.TryGetProperty(Cik, out var value) ? null
            : value.ValueKind is JsonValueKind.Number ? value.GetRawText()
            : value.ValueKind is JsonValueKind.String ? value.GetString()
            : null;
        var digits = new string([.. (filed ?? string.Empty).Where(char.IsAsciiDigit)]).TrimStart('0');

        return digits.Length == 0 ? null : digits.PadLeft(10, '0');
    }

    // A field's text, none where the field is absent, not text, empty or the provider's NA.
    static string? Text(JsonElement holder, string name) =>
        holder.TryGetProperty(name, out var value)
        && value.ValueKind is JsonValueKind.String
        && value.GetString()?.Trim() is { Length: > 0 } text
        && !string.Equals(text, "NA", StringComparison.Ordinal)
            ? text
            : null;

    // A count the provider sends as text or as a number.
    static decimal? Number(JsonElement holder, string name) =>
        !holder.TryGetProperty(name, out var value) ? null
        : value.ValueKind is JsonValueKind.Number && value.TryGetDecimal(out var number) ? number
        : value.ValueKind is JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) ? parsed
        : null;

    static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;
}
