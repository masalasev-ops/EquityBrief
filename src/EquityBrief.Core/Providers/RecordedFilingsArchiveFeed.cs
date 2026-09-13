using System.Globalization;

namespace EquityBrief.Core.Providers;

// One name's filings, answered from captured archive responses.
//
// It runs the same route the live feed runs, over the same reader, and differs in
// one place: where the live one composes a URL from the request, this one finds the
// capture the request is for. So the route's own derivations are exercised by a
// replay rather than asserted separately and hoped to be what the live feed does.
//
// It serves by document kind and by the report a request names, and not by the
// path. The path is how the archive is addressed and a fixture is a folder of
// files, so a mapping from one to the other would be a table of six rules with a
// fall-through, which is the prefix matcher this repository has repaired four
// times. The kind is on the request because both sides need it.
//
// A capture this feed does not hold refuses by name rather than answering with
// nothing. A replay runs over a set that should be complete, so a document the
// route asked for and nobody committed is a fault in the fixture; answering it as
// an absence would read as the archive not holding the document, which is the
// fault 6.1 found in the fundamentals double's own unknown-name path.
public sealed class RecordedFilingsArchiveFeed(string folder) : IFilingsArchiveFeed
{
    // Which file name serves which kind. The ticker follows the prefix and
    // anything after it is the filer's own detail, which is why the match is on
    // the prefix and the extension and the count of matches is asserted rather
    // than the first one taken.
    public static readonly (ArchiveDocument Document, string Prefix, string Extension)[] Captures =
    [
        (ArchiveDocument.Submissions, "filings-", ".json"),
        (ArchiveDocument.FilingIndex, "filing-types-", ".htm"),
        (ArchiveDocument.ReportList, "report-list-", ".xml"),
        (ArchiveDocument.SegmentReport, "segment-report-", ".htm"),
        (ArchiveDocument.ReleaseExhibit, "release-", ".htm"),
        (ArchiveDocument.CompanyFacts, "company-facts-", ".json"),
    ];

    public int Requests { get; private set; }

    // The names this feed can answer at all, which is the names a filing index was
    // captured for. Read off what was committed rather than listed anywhere.
    public IReadOnlyList<string> Names =>
    [
        .. Directory
            .GetFiles(folder, "filings-*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)["filings-".Length..])
            .OrderBy(ticker => ticker, StringComparer.Ordinal),
    ];

    // Every request this feed was asked for, in order. Kept because the paths the
    // route composed are the half a replay cannot otherwise show, and a test reads
    // them against the addresses the manifest records for the same captures.
    public IReadOnlyList<ArchiveRequest> Asked => asked;

    readonly List<ArchiveRequest> asked = [];

    public Task<ArchiveFilings> FilingsAsync(
        string ticker,
        string cik,
        CancellationToken cancellation = default)
    {
        Requests++;

        return SecEdgarArchive.ReadAsync(
            (request, _) =>
            {
                asked.Add(request);

                return Task.FromResult<string?>(File.ReadAllText(Held(request, ticker)));
            },
            ticker,
            cik,
            cancellation);
    }

    // Which committed file answers one request.
    //
    // The report a request names is part of the file name for the one kind where a
    // filing has several: two of one filing's reports are captured, because the
    // route reads one and rejects it before reading the other.
    public string Held(ArchiveRequest request, string ticker)
    {
        var (_, prefix, extension) = Captures.Single(capture => capture.Document == request.Document);
        var report = request.Document == ArchiveDocument.SegmentReport
            ? "-" + Path.GetFileNameWithoutExtension(request.Path)
            : string.Empty;

        var matches = Directory
            .GetFiles(folder, prefix + ticker + report + "*" + extension)
            .Where(path => Path.GetFileName(path) is { } name
                && name.StartsWith(prefix + ticker + report, StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        // Exactly one, in both directions. None is a capture nobody committed, and
        // more than one is a prefix that answers about two files, which is the
        // shape that has to be refused rather than resolved by the order a
        // directory happened to yield.
        return matches.Length == 1
            ? matches[0]
            : throw new InvalidOperationException(string.Format(
                CultureInfo.InvariantCulture,
                "{0} captured file(s) in '{1}' answer '{2}{3}*{4}', which is the {5} for {6}. A replay "
                + "runs over a captured set that should be complete, so neither none nor several is "
                + "resolved here: none would read as the archive not holding the document and several "
                + "would be answered by whichever the folder listed first.",
                matches.Length, folder, prefix, ticker + report, extension, request.Document, ticker));
    }
}
