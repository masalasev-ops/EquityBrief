using System.Globalization;
using System.IO.Compression;
using System.Text.RegularExpressions;
using EquityBrief.Core.Insiders;

namespace EquityBrief.Core.Providers;

// The SEC's quarterly insider transactions data sets, of Forms 3, 4 and 5: a zip a quarter of tab-separated files, the
// filings, their reporting owners and their tables of shares among them, read for the issuers asked for and the forms the
// nightly reader reads, 4 and 4/A, into the same filings it gives.
//
// Written against the quarters 2018q1, 2026q2 and 2026q3 captured on 2026-10-16 before this was written. Every row opens
// with its accession and carries its header's columns, so a column is read by its name and never by its place: the box
// is a column of its own from the quarter it joined the form, 2018q1's carrying none, and is written 0, 1, false or true.
// The issuer's CIK is padded to ten digits and the days are written as 31-AUG-2026. The shares, the prices and the
// shares held after are held to two places where a filing states as many as it states, so the nightly reader's rows for
// a filing both hold may differ past the second place. The rows of a filing's table of shares are not in the filing's
// order in the file, and their key's order is, so that order is the sequence. A row ending in empty fields may carry
// fewer fields than its header, and a field it does not carry is read as empty. The listing page names each quarter's
// file, and the path the files sit under moved between quarters, so the quarters are read off the page.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
public static class SecEdgarInsiderDataSet
{
    // The page listing every quarter's file, on the archive's document host.
    public const string ListingPath = "data-research/sec-markets-data/insider-transactions-data-sets";

    public const string Filings = "SUBMISSION.tsv";

    public const string Owners = "REPORTINGOWNER.tsv";

    public const string Shares = "NONDERIV_TRANS.tsv";

    public static IReadOnlyList<string> Files { get; } = [Filings, Owners, Shares];

    // The first quarter the history reads, four quarters before the ledger's first session so its first setups' ninety
    // days are read whole.
    public const string FirstQuarter = "2018q1";

    // Each quarter's file the listing page links, by its quarter as written there, 2026q3, to its path under the host.
    public static IReadOnlyDictionary<string, string> Quarters(string listingPage)
    {
        var quarters = new SortedDictionary<string, string>(StringComparer.Ordinal);

        foreach (Match link in QuarterLink.Matches(listingPage))
        {
            quarters.TryAdd(link.Groups["quarter"].Value, link.Groups["path"].Value.TrimStart('/'));
        }

        return quarters;
    }

    // A quarter's first and last day, from its name as the page writes it.
    public static (DateOnly First, DateOnly Last) Span(string quarter)
    {
        if (QuarterName.Match(quarter) is not { Success: true } name)
        {
            throw new FormatException($"'{quarter}' names no quarter, as 2026q3 does.");
        }

        var first = new DateOnly(
            int.Parse(name.Groups["year"].Value, CultureInfo.InvariantCulture),
            ((name.Groups["quarter"].Value[0] - '1') * 3) + 1,
            1);

        return (first, first.AddMonths(3).AddDays(-1));
    }

    // The quarter's Forms 4 and 4/A whose issuer is among those asked for, each with its owners and its table of shares, in
    // the file's order of filings.
    public static IReadOnlyList<InsiderFiling> Read(Stream zip, IReadOnlySet<string> issuers)
    {
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read, leaveOpen: true);

        Table Of(string name)
        {
            var entry = archive.GetEntry(name) ?? throw new FormatException($"The data set holds no {name}.");

            using var reader = new StreamReader(entry.Open());

            return Table.Read(reader.ReadToEnd(), name);
        }

        var filings = Of(Filings);
        var kept = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var row in filings.Rows)
        {
            if (InsiderFiling.Forms.Contains(filings.Get(row, "DOCUMENT_TYPE"), StringComparer.Ordinal)
                && issuers.Contains(SecEdgarArchive.Padded(filings.Get(row, "ISSUERCIK")))
                && kept.TryAdd(filings.Get(row, "ACCESSION_NUMBER"), row))
            {
                order.Add(filings.Get(row, "ACCESSION_NUMBER"));
            }
        }

        var owners = new Dictionary<string, List<InsiderOwner>>(StringComparer.Ordinal);
        var ownerTable = Of(Owners);

        foreach (var row in ownerTable.Rows)
        {
            var accession = ownerTable.Get(row, "ACCESSION_NUMBER");

            if (!kept.ContainsKey(accession))
            {
                continue;
            }

            var roles = ownerTable.Get(row, "RPTOWNER_RELATIONSHIP").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            var title = ownerTable.Get(row, "RPTOWNER_TITLE");

            if (!owners.TryGetValue(accession, out var list))
            {
                owners[accession] = list = [];
            }

            list.Add(new InsiderOwner(
                SecEdgarArchive.Padded(ownerTable.Get(row, "RPTOWNERCIK")),
                ownerTable.Get(row, "RPTOWNERNAME"),
                roles.Contains("Director", StringComparer.Ordinal),
                roles.Contains("Officer", StringComparer.Ordinal),
                title.Length == 0 ? null : title,
                roles.Contains("TenPercentOwner", StringComparer.Ordinal)));
        }

        var transactions = new Dictionary<string, List<(long Key, InsiderTransaction Row)>>(StringComparer.Ordinal);
        var shareTable = Of(Shares);

        foreach (var row in shareTable.Rows)
        {
            var accession = shareTable.Get(row, "ACCESSION_NUMBER");

            if (!kept.ContainsKey(accession))
            {
                continue;
            }

            var price = shareTable.Get(row, "TRANS_PRICEPERSHARE");
            var held = shareTable.Get(row, "SHRS_OWND_FOLWNG_TRANS");
            var nature = shareTable.Get(row, "NATURE_OF_OWNERSHIP");

            if (!transactions.TryGetValue(accession, out var list))
            {
                transactions[accession] = list = [];
            }

            list.Add((
                long.Parse(shareTable.Get(row, "NONDERIV_TRANS_SK"), CultureInfo.InvariantCulture),
                new InsiderTransaction(
                    0,
                    shareTable.Get(row, "SECURITY_TITLE"),
                    Day(shareTable.Get(row, "TRANS_DATE"), accession),
                    shareTable.Get(row, "TRANS_CODE"),
                    Number(shareTable.Get(row, "TRANS_SHARES"), accession),
                    price.Length == 0 ? null : Number(price, accession),
                    shareTable.Get(row, "TRANS_ACQUIRED_DISP_CD") == "A",
                    held.Length == 0 ? null : Number(held, accession),
                    shareTable.Get(row, "DIRECT_INDIRECT_OWNERSHIP") != "I",
                    nature.Length == 0 ? null : nature)));
        }

        return
        [
            .. order.Select(accession =>
            {
                var row = kept[accession];
                var original = filings.Get(row, "DATE_OF_ORIG_SUB");
                var symbol = filings.Get(row, "ISSUERTRADINGSYMBOL");

                return new InsiderFiling(
                    accession,
                    Day(filings.Get(row, "FILING_DATE"), accession),
                    filings.Get(row, "DOCUMENT_TYPE"),
                    SecEdgarArchive.Padded(filings.Get(row, "ISSUERCIK")),
                    symbol.Length == 0 ? null : symbol,
                    original.Length == 0 ? null : Day(original, accession),
                    owners.GetValueOrDefault(accession) ?? [],
                    filings.Has("AFF10B5ONE") && filings.Get(row, "AFF10B5ONE") is { Length: > 0 } box ? SecEdgarOwnership.Truth(box) : null,
                    [.. (transactions.GetValueOrDefault(accession) ?? []).OrderBy(one => one.Key).Select((one, at) => one.Row with { Sequence = at + 1 })]);
            }),
        ];
    }

    // One file of the data set: its columns by name and its rows, a line each.
    sealed class Table(IReadOnlyDictionary<string, int> places, string file, IReadOnlyList<string[]> rows)
    {
        public IReadOnlyList<string[]> Rows => rows;

        public bool Has(string name) => places.ContainsKey(name);

        public string Get(string[] row, string name) =>
            places.TryGetValue(name, out var at)
                ? at < row.Length ? row[at].Trim() : string.Empty
                : throw new FormatException($"The data set's {file} carries no column {name}.");

        public static Table Read(string text, string file)
        {
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');

            if (lines.Length == 0 || lines[0].Trim().Length == 0)
            {
                throw new FormatException($"The data set's {file} carries no header.");
            }

            var places = new Dictionary<string, int>(StringComparer.Ordinal);
            var header = lines[0].Split('\t');

            for (var at = 0; at < header.Length; at++)
            {
                places.TryAdd(header[at].Trim(), at);
            }

            return new Table(places, file, [.. lines.Skip(1).Where(line => line.Trim().Length > 0).Select(line => line.Split('\t'))]);
        }
    }

    static DateOnly Day(string text, string accession) =>
        DateOnly.TryParseExact(text, "dd-MMM-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
            ? day
            : throw new FormatException($"The data set states '{text}' as a day of the filing {accession}, which is no day.");

    static decimal Number(string text, string accession) =>
        decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? number
            : throw new FormatException($"The data set states '{text}' as a number of the filing {accession}, which is no number.");

    static readonly Regex QuarterLink = new(@"href=""(?<path>[^""]*/(?<quarter>\d{4}q[1-4])_form345\.zip)""", RegexOptions.CultureInvariant);

    static readonly Regex QuarterName = new(@"^(?<year>\d{4})q(?<quarter>[1-4])$", RegexOptions.CultureInvariant);
}
