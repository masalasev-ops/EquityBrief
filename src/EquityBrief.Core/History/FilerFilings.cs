using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.History;

// One filing of a filer as the archive's filing index lists it, with the text of its document where the read kept it.
public sealed record FilerFiling(
    string Accession,
    string Form,
    DateOnly Filed,
    DateOnly? Reported,
    IReadOnlyList<string> Items,
    string PrimaryDocument,
    string? Document = null);

// A filer's filing index read. The company's index holds its newest filings under `filings.recent` and names its older pages under
// `filings.files`; each older page holds the same parallel arrays at its top, one array a field and every array the same positions.
public static class FilerFilings
{
    public const string DocumentsFolder = "documents";

    // The older pages the company's index names, in the order it names them.
    public static IReadOnlyList<string> Pages(string index)
    {
        using var document = JsonDocument.Parse(index);

        return document.RootElement.TryGetProperty("filings", out var filings) && filings.TryGetProperty("files", out var files)
            ? [.. files.EnumerateArray().Select(file => file.GetProperty("name").GetString()!)]
            : [];
    }

    public static IReadOnlyList<FilerFiling> Read(IEnumerable<string> pages) => [.. pages.SelectMany(ReadPage).DistinctBy(filing => filing.Accession, StringComparer.Ordinal).OrderBy(filing => filing.Filed)];

    // A folder a read kept: the index, each older page it names, and the documents beside them.
    public static IReadOnlyList<FilerFiling> Read(string folder)
    {
        var index = File.ReadAllText(Path.Combine(folder, "submissions.json"));
        var pages = Pages(index).Select(page => Path.Combine(folder, page)).Where(File.Exists).Select(File.ReadAllText).Prepend(index);
        var documents = Path.Combine(folder, DocumentsFolder);

        return
        [
            .. Read(pages).Select(filing => Path.Combine(documents, DocumentFile(filing)) is var file && File.Exists(file)
                ? filing with { Document = File.ReadAllText(file) }
                : filing),
        ];
    }

    // A document is kept under its accession and its own name. A form filed as structured data is listed under the path of the
    // archive's rendering of it, a folder named for the stylesheet, and the file itself sits beside it without that folder.
    public static string DocumentFile(FilerFiling filing) => filing.Accession + "-" + Path.GetFileName(filing.PrimaryDocument);

    public static string AsFiled(string primaryDocument) =>
        primaryDocument.Split('/') is [var folder, var file] && folder.StartsWith("xsl", StringComparison.Ordinal) ? file : primaryDocument;

    static IEnumerable<FilerFiling> ReadPage(string page)
    {
        using var document = JsonDocument.Parse(page);
        var root = document.RootElement;
        var arrays = root.TryGetProperty("filings", out var filings) ? filings.GetProperty("recent") : root;

        string[] Column(string name) => arrays.TryGetProperty(name, out var column)
            ? [.. column.EnumerateArray().Select(value => value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty)]
            : [];

        var (accessions, forms, filed, reported, items, primary) = (Column("accessionNumber"), Column("form"), Column("filingDate"), Column("reportDate"), Column("items"), Column("primaryDocument"));
        var read = new List<FilerFiling>(accessions.Length);

        for (var at = 0; at < accessions.Length; at++)
        {
            read.Add(new(
                accessions[at],
                forms[at],
                Date(filed[at])!.Value,
                at < reported.Length ? Date(reported[at]) : null,
                at < items.Length ? [.. items[at].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)] : [],
                at < primary.Length ? primary[at] : string.Empty));
        }

        return read;
    }

    static DateOnly? Date(string text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : null;
}
