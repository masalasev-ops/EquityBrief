using System.Globalization;
using System.Xml.Linq;

namespace EquityBrief.Core.Providers;

// One filing of a fund's quarter-end holdings: its accession, the day it was filed and its form.
public sealed record FundFiling(string Accession, DateOnly Filed, string Form);

// One holding a snapshot lists, as the fund filed it: its name, its CUSIP and its ISIN where it carries them, and the
// asset category it filed, common equity being the one a member of the index is.
public sealed record FiledHolding(string Name, string? Cusip, string? Isin, string? Category);

// A fund's holdings as of a quarter's end, read off one filing.
public sealed record FundSnapshot(string Series, DateOnly Period, string Accession, IReadOnlyList<FiledHolding> Holdings)
{
    public IEnumerable<FiledHolding> Equity => Holdings.Where(holding => holding.Category == FundSnapshots.CommonEquity);
}

// The funds' quarter-end holdings as the archive files them, one filing list a fund and one document a snapshot.
// see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
public interface IFundSnapshotFeed
{
    // How many documents the feed has asked for.
    int Requests { get; }

    // The fund's filings of its quarter-end holdings, newest first as the archive lists them.
    Task<IReadOnlyList<FundFiling>> FilingsAsync(string series, CancellationToken cancellation = default);

    // One filing's holdings.
    Task<FundSnapshot> SnapshotAsync(string accession, CancellationToken cancellation = default);

    // One document of a filing, as the archive serves it.
    // see: The funds' holdings before their first public N-PORT are read from their N-Q of 2018-12-31 and their annual report of 2019-03-31
    Task<string> DocumentAsync(string accession, string document, CancellationToken cancellation = default);
}

// Reads the archive's list of a fund's filings and a filing's holdings. The S&P 400's fund, IJH, and the S&P 600's, IJR,
// are two series of one trust, so a filing is found under the trust's own number and its series named inside it.
public static class FundSnapshots
{
    // The trust the two funds' filings sit under.
    public const string Trust = "1100663";

    // The form a fund files its quarter-end holdings on, public since the quarter to 2019-09-30.
    public const string Form = "NPORT-P";

    // The asset category a fund files a company's common stock under.
    public const string CommonEquity = "EC";

    // Each wider index's fund's series.
    public static IReadOnlyDictionary<string, string> Series { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MID"] = "S000004307",
        ["SML"] = "S000004313",
    };

    // The archive's list of a fund's filings of one form, from its company browser's feed, each entry's accession, filing
    // date and form. An answer carrying no entry is refused, since a fund that has filed nothing lists nobody.
    public static IReadOnlyList<FundFiling> ParseFilings(string atom, string series)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(atom);
        }
        catch (System.Xml.XmlException failure)
        {
            throw new FormatException($"The archive's list of {series}'s filings is not a feed: {failure.Message}", failure);
        }

        var filings = document.Descendants()
            .Where(element => element.Name.LocalName == "entry")
            .Select(entry => (
                Accession: Deep(entry, "accession-number"),
                Filed: Deep(entry, "filing-date"),
                Form: Deep(entry, "filing-type")))
            .Where(entry => entry.Accession is not null && entry.Filed is not null && entry.Form is not null)
            .Select(entry => new FundFiling(
                entry.Accession!,
                DateOnly.ParseExact(entry.Filed!, "yyyy-MM-dd", CultureInfo.InvariantCulture),
                entry.Form!))
            .ToArray();

        return filings.Length > 0
            ? filings
            : throw new FormatException($"The archive's list of {series}'s filings carries no filing.");
    }

    // A filing's holdings: the series it is for, the quarter's end it reports, and every holding with its name, CUSIP,
    // ISIN and category. A document naming no series or no quarter's end is refused, since a snapshot dated nowhere places
    // no member on any session.
    public static FundSnapshot ParseSnapshot(string xml, string accession)
    {
        XDocument document;

        try
        {
            document = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException failure)
        {
            throw new FormatException($"The filing {accession} is not a holdings document: {failure.Message}", failure);
        }

        var info = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "genInfo");
        var series = info is null ? null : Text(info, "seriesId");
        var period = info is null ? null : Text(info, "repPdDate");

        if (series is null || period is null || !DateOnly.TryParseExact(period, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var reported))
        {
            throw new FormatException($"The filing {accession} names no series or no quarter's end its holdings are as of.");
        }

        var holdings = document.Descendants()
            .Where(element => element.Name.LocalName == "invstOrSec")
            .Select(holding => new FiledHolding(
                Text(holding, "name") ?? Text(holding, "title") ?? string.Empty,
                Cusip(Text(holding, "cusip")),
                holding.Descendants().FirstOrDefault(element => element.Name.LocalName == "isin")?.Attribute("value")?.Value is { Length: 12 } isin ? isin.ToUpperInvariant() : null,
                Text(holding, "assetCat")))
            .ToArray();

        return new FundSnapshot(series, reported, accession, holdings);
    }

    // The ISIN a holding is read under: the one filed, and where none is filed, the one its CUSIP makes, the country's
    // letters and the CUSIP's nine characters closed by the check digit ISO 6166 computes.
    public static string? IsinOf(FiledHolding holding) =>
        holding.Isin ?? (holding.Cusip is { } cusip ? FromCusip(cusip) : null);

    public static string FromCusip(string cusip)
    {
        var body = "US" + cusip.ToUpperInvariant();
        var digits = string.Concat(body.Select(character => char.IsDigit(character)
            ? character.ToString()
            : (character - 'A' + 10).ToString(CultureInfo.InvariantCulture)));
        var total = 0;

        for (var at = 0; at < digits.Length; at++)
        {
            var digit = digits[digits.Length - 1 - at] - '0';

            if (at % 2 == 0)
            {
                digit *= 2;
            }

            total += (digit / 10) + (digit % 10);
        }

        return body + ((10 - (total % 10)) % 10).ToString(CultureInfo.InvariantCulture);
    }

    // A CUSIP as filed, none where the fund filed none or a placeholder of noughts.
    static string? Cusip(string? filed) =>
        filed is { Length: 9 } nine && nine != "000000000" && nine.All(char.IsLetterOrDigit) ? nine.ToUpperInvariant() : null;

    static string? Text(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } text ? text : null;

    // An entry's field at any depth, since the archive's feed files each inside the entry's content.
    static string? Deep(XElement parent, string name) =>
        parent.Descendants().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } text ? text : null;
}
