using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace EquityBrief.Core.History;

// The end a filer's filings give its common stock's history: the history's last regular session is the last session before the
// day named, read from the filing named; none where no such filing is found, which the operator rules on in one list.
public sealed record HistoryEndRead(DateOnly? EndsBefore, FilerFiling? ReadFrom, string Says);

// A stock's history ends on its company's last regular session, read from the SEC's filings by rule: the earlier of the session
// before its Form 25 removes its common stock from listing and the session before the 8-K reporting its merger complete is filed.
// A removal takes effect ten days after it is filed. An 8-K reports a merger complete where it carries the item reporting a change
// in control of the company with the item completing an acquisition or the item giving notice of delisting. A Form 25 is an end
// only where the class it removes is the common stock, read from the class its document names, and the exchange is the stock's
// own, the New York Stock Exchange, Nasdaq or the American exchange by any of its names; a Form 25 removing another class, a
// preferred share, a note or a security linked to another's stock, one from a regional exchange the stock was listed on beside its
// own, and one moving the listing, the company registering its common stock on an exchange within 30 days of it, are no end.
// see: A stock's history ends by rule on its company's last regular session read from its Form 25 or the 8-K completing its merger
public static partial class HistoryEnd
{
    // A Form 25 is filed by the exchange or by the company, and either may be amended.
    public static IReadOnlyList<string> Removals { get; } = ["25", "25-NSE", "25/A", "25-NSE/A"];

    // A class registered on an exchange.
    public static IReadOnlyList<string> Registrations { get; } = ["8-A12B", "8-A12B/A"];

    public const string MergerForm = "8-K";
    public const string ChangesControl = "5.01";
    public static IReadOnlyList<string> CompletesOrDelists { get; } = ["2.01", "3.01"];

    public const int RemovalTakesEffect = 10;
    public const int ListingMoveDays = 30;

    public static bool NeedsItsDocument(string form) => Removals.Contains(form) || Registrations.Contains(form);

    public static bool CompletesAMerger(FilerFiling filing) =>
        filing.Form == MergerForm && filing.Items.Contains(ChangesControl) && filing.Items.Any(CompletesOrDelists.Contains);

    // The class a removal or a registration names: the structured form's own field, else the line after the caption naming each
    // class, else the line before the caption describing the class. None where the document was not kept or names none.
    public static string? ClassOf(string? document)
    {
        if (document is null)
        {
            return null;
        }

        if (DescribedClass().Match(document) is { Success: true } described)
        {
            return Spaced(described.Groups[1].Value);
        }

        var text = Spaced(document);

        if (EachClassCaption().Match(text) is { Success: true } caption)
        {
            var after = text[(caption.Index + caption.Length)..];

            return Clip(NextCaption().Match(after) is { Success: true } next ? after[..next.Index] : after, 200);
        }

        if (DescriptionCaption().Match(text) is { Success: true } description)
        {
            var before = text[..description.Index];
            var cut = before.LastIndexOf("offices)", StringComparison.Ordinal);

            return (cut >= 0 ? before[(cut + "offices)".Length)..] : before[Math.Max(0, before.Length - 200)..]).Trim();
        }

        return null;
    }

    // The exchange a removal names: the structured form's own field, else the name after the issuer's on its first line.
    public static string? ExchangeOf(string? document)
    {
        if (document is null)
        {
            return null;
        }

        if (ExchangeField().Match(document) is { Success: true } field)
        {
            return Spaced(field.Groups[1].Value);
        }

        var text = Spaced(document);

        return IssuerAndExchange().Match(text) is { Success: true } named ? named.Groups[1].Value.Trim() : null;
    }

    public static bool IsTheCommonStock(string? described) =>
        described is { } text && CommonStock().IsMatch(text) && !AnotherClass().IsMatch(PurchaseRights().Replace(text, " "));

    public static bool IsAnOwnExchange(string? exchange) => exchange is { } text && OwnExchange().IsMatch(text);

    // The history's last regular session on the days the pulled history reads as sessions, none where the filings give no end.
    public static DateOnly? LastSession(HistoryEndRead read, Bars.PulledCalendar calendar) => read.EndsBefore is { } before ? calendar.SessionBefore(before) : null;

    public static HistoryEndRead Of(IReadOnlyList<FilerFiling> filings)
    {
        var merger = filings.Where(CompletesAMerger).Select(filing => (Filing: filing, Before: filing.Filed)).OrderBy(read => read.Before).FirstOrDefault();
        var registered = filings.Where(filing => Registrations.Contains(filing.Form) && IsTheCommonStock(ClassOf(filing.Document))).ToArray();
        var removal = filings
            .Where(filing => Removals.Contains(filing.Form) && IsTheCommonStock(ClassOf(filing.Document)) && IsAnOwnExchange(ExchangeOf(filing.Document)))
            .Where(filing => !registered.Any(registration => Math.Abs(registration.Filed.DayNumber - filing.Filed.DayNumber) <= ListingMoveDays))
            .Select(filing => (Filing: filing, Before: filing.Filed.AddDays(RemovalTakesEffect)))
            .OrderBy(read => read.Before)
            .FirstOrDefault();

        (FilerFiling Filing, DateOnly Before)? end = (merger.Filing, removal.Filing) switch
        {
            (null, null) => null,
            (null, _) => removal,
            (_, null) => merger,
            _ => merger.Before <= removal.Before ? merger : removal,
        };

        if (end is not { } read)
        {
            var unread = filings.Count(filing => Removals.Contains(filing.Form) && filing.Document is null);

            return new(null, null, unread == 0
                ? "No Form 25 removing its common stock from its own exchange and no 8-K completing a merger is filed, so the operator rules on its end."
                : Invariant($"No filing read gives its end, and {unread} Form 25 document(s) were not kept to read, so the operator rules on its end."));
        }

        var day = read.Before.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var filed = read.Filing.Filed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        return new(read.Before, read.Filing, read.Filing.Form == MergerForm
            ? Invariant($"Its history ends on the last session before {day}, the day its 8-K reporting its merger complete was filed ({read.Filing.Accession}).")
            : Invariant($"Its history ends on the last session before {day}, the day the Form {read.Filing.Form} filed {filed} ({read.Filing.Accession}) removes its common stock from listing."));
    }

    // A document's words with its markup taken out, lower case and on one line.
    static string Spaced(string document) =>
        Whitespace().Replace(WebUtility.HtmlDecode(Markup().Replace(document, " ")), " ").Trim().ToLowerInvariant();

    static string Clip(string text, int most) => (text.Length <= most ? text : text[..most]).Trim();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex Markup();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"<descriptionClassSecurity>(.*?)</descriptionClassSecurity>", RegexOptions.Singleline)]
    private static partial Regex DescribedClass();

    [GeneratedRegex(@"<exchange>.*?<entityName>(.*?)</entityName>", RegexOptions.Singleline)]
    private static partial Regex ExchangeField();

    [GeneratedRegex(@"name of (each )?exchange on which each class is to be (registered|withdrawn)")]
    private static partial Regex EachClassCaption();

    [GeneratedRegex(@"if this form relates|securities to be registered pursuant to section 12\(g\)|securities act registration statement|pursuant to the requirements")]
    private static partial Regex NextCaption();

    [GeneratedRegex(@"\(description of class of securities\)")]
    private static partial Regex DescriptionCaption();

    [GeneratedRegex(@"commission file number:?\s*[\d-]+\s*[^/]+/\s*([^(]+)\(exact name of issuer")]
    private static partial Regex IssuerAndExchange();

    [GeneratedRegex(@"\b(common (stock|shares)|ordinary shares|shares of beneficial interest)\b")]
    private static partial Regex CommonStock();

    [GeneratedRegex(@"\b(preferred|preference)\s+(stock|share)s?\s+purchase\s+rights?\b")]
    private static partial Regex PurchaseRights();

    [GeneratedRegex(@"\b(preferred|preference|notes?|debentures?|depositary|warrants?|rights?|units?|bonds?|securities|linked|exchangeable|convertible|guarantor|due|maturing|trust)\b|%")]
    private static partial Regex AnotherClass();

    [GeneratedRegex(@"\b(new york stock exchange|nasdaq|american stock exchange|nyse american|nyse alternext|nyse mkt|nyse amex)\b")]
    private static partial Regex OwnExchange();

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
