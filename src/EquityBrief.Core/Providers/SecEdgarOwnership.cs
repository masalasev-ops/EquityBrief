using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using EquityBrief.Core.Insiders;

namespace EquityBrief.Core.Providers;

// An insider's Form 4 or 4/A as the archive serves it whole, one request a filing: the submission's own header in the
// archive's envelope, then the filing's ownership document in XML between `<XML>` and `</XML>`.
//
// Written against twelve filings captured on 2026-10-16 before this was written: an open-market purchase, a sale under
// a Rule 10b5-1 plan, an award with its tax withholding, filings holding only an option's or a unit's own table, and two
// amendments with their originals, one restating its sale with the day corrected and one restating its option's table
// alone. The accession and the day filed are read from the header and the rest from the document. The box is the
// filing's and not a transaction's, and a flag or the box is written 0 or 1 by some filers' software and false or true by
// others; a document carrying no box is none, and a relationship carrying no flag is no. A price stated only by a
// footnote is none, and a transaction stating no code is kept with none. A holding reports no transaction and is not
// read, and nor is the table of derivatives, whose exercises and vestings reach the shares as rows of the table of shares.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public static class SecEdgarOwnership
{
    // The archive's own path for a filing's whole submission, under the filer its index line names.
    public static string PathOf(string cik, string accession) =>
        "Archives/edgar/data/" + cik.TrimStart('0') + "/" + accession + ".txt";

    public static InsiderFiling Parse(string submission)
    {
        var accession = Header(submission, AccessionLine, "accession number");
        var filedText = Header(submission, FiledLine, "day filed");

        if (!DateOnly.TryParseExact(filedText, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var filed))
        {
            throw new FormatException($"The filing {accession} states its day filed as '{filedText}', which is no day.");
        }

        var open = submission.IndexOf("<XML>", StringComparison.Ordinal);
        var close = open < 0 ? -1 : submission.IndexOf("</XML>", open, StringComparison.Ordinal);

        if (close < 0)
        {
            throw new FormatException($"The filing {accession} carries no ownership document between <XML> and </XML>.");
        }

        XElement document;

        try
        {
            document = XDocument.Parse(submission[(open + "<XML>".Length)..close].Trim()).Root
                ?? throw new FormatException($"The filing {accession}'s ownership document is empty.");
        }
        catch (XmlException failure)
        {
            throw new FormatException($"The filing {accession}'s ownership document is not XML it can read: {failure.Message}", failure);
        }

        var form = Text(document, "documentType") ?? throw Missing(accession, "documentType");
        var issuer = document.Element("issuer") ?? throw Missing(accession, "issuer");
        var issuerCik = Text(issuer, "issuerCik") ?? throw Missing(accession, "issuerCik");
        var original = Text(document, "dateOfOriginalSubmission");

        InsiderOwner Owner(XElement owner)
        {
            var id = owner.Element("reportingOwnerId") ?? throw Missing(accession, "reportingOwnerId");
            var relationship = owner.Element("reportingOwnerRelationship");

            return new InsiderOwner(
                SecEdgarArchive.Padded(Text(id, "rptOwnerCik") ?? throw Missing(accession, "rptOwnerCik")),
                Text(id, "rptOwnerName") ?? throw Missing(accession, "rptOwnerName"),
                Flag(relationship, "isDirector"),
                Flag(relationship, "isOfficer"),
                Text(relationship, "officerTitle"),
                Flag(relationship, "isTenPercentOwner"));
        }

        InsiderTransaction Transaction(XElement row, int sequence)
        {
            var amounts = row.Element("transactionAmounts") ?? throw Missing(accession, "transactionAmounts");
            var after = row.Element("postTransactionAmounts");
            var nature = row.Element("ownershipNature");

            return new InsiderTransaction(
                sequence,
                Value(row, "securityTitle") ?? string.Empty,
                Day(Value(row, "transactionDate") ?? throw Missing(accession, "transactionDate"), accession),
                Text(row.Element("transactionCoding"), "transactionCode") ?? string.Empty,
                Number(Value(amounts, "transactionShares") ?? throw Missing(accession, "transactionShares"), accession),
                Value(amounts, "transactionPricePerShare") is { } price ? Number(price, accession) : null,
                (Value(amounts, "transactionAcquiredDisposedCode") ?? throw Missing(accession, "transactionAcquiredDisposedCode")) == "A",
                after is not null && Value(after, "sharesOwnedFollowingTransaction") is { } held ? Number(held, accession) : null,
                (nature is null ? null : Value(nature, "directOrIndirectOwnership")) != "I",
                nature is null ? null : Value(nature, "natureOfOwnership"));
        }

        return new InsiderFiling(
            accession,
            filed,
            form,
            SecEdgarArchive.Padded(issuerCik),
            Text(issuer, "issuerTradingSymbol"),
            original is null ? null : Day(original, accession),
            [.. document.Elements("reportingOwner").Select(Owner)],
            Text(document, "aff10b5One") is { } box ? Truth(box) : null,
            [.. (document.Element("nonDerivativeTable")?.Elements("nonDerivativeTransaction") ?? []).Select((row, at) => Transaction(row, at + 1))]);
    }

    // A box or a flag as the filers' software writes it, 1 or true and 0 or false.
    public static bool Truth(string text) => text.Trim().ToLowerInvariant() switch
    {
        "1" or "true" => true,
        "0" or "false" => false,
        _ => throw new FormatException($"'{text}' is neither 0, 1, false nor true."),
    };

    static bool Flag(XElement? within, string name) => Text(within, name) is { } text && Truth(text);

    // An element's own text, none where it is absent or blank.
    static string? Text(XElement? within, string name) =>
        within?.Element(name)?.Value.Trim() is { Length: > 0 } text ? text : null;

    // The text of an element's `value`, which a footnote may stand in for, none where it states none.
    static string? Value(XElement within, string name) => Text(within.Element(name), "value");

    static DateOnly Day(string text, string accession) =>
        DateOnly.TryParseExact(text.Length > 10 ? text[..10] : text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new FormatException($"The filing {accession} states '{text}' as a day, which is no day.");

    static decimal Number(string text, string accession) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new FormatException($"The filing {accession} states '{text}' as a number, which is no number.");

    static string Header(string submission, Regex line, string what) =>
        line.Match(submission) is { Success: true } match
            ? match.Groups["value"].Value
            : throw new FormatException($"The submission's header states no {what}.");

    static FormatException Missing(string accession, string element) =>
        new($"The filing {accession}'s ownership document carries no {element}.");

    static readonly Regex AccessionLine = new(@"^ACCESSION NUMBER:\s+(?<value>\d{10}-\d{2}-\d{6})\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);

    static readonly Regex FiledLine = new(@"^FILED AS OF DATE:\s+(?<value>\d{8})\s*$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
}
