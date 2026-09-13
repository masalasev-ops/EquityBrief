namespace EquityBrief.Core.Providers;

// Which of the archive's documents one request is for.
//
// A request carries the kind as well as the address, because the two
// implementations key on different halves of it: the live one composes a URL and
// the recorded one finds a capture. A transport that only saw an address would
// make the recorded side guess a kind out of a path, which is the prefix matcher
// this repository has had to repair four times.
public enum ArchiveDocument
{
    // The company's own filing index, which is where every route starts.
    Submissions,

    // One filing's index page, which is the only place a document's kind is
    // stated. The directory listing beside it carries a display icon.
    FilingIndex,

    // The filing's list of rendered reports, which is how a segment table is
    // found without fetching every page of the filing.
    ReportList,

    // One rendered report page.
    SegmentReport,

    // The earnings release, filed as an exhibit to the results announcement.
    ReleaseExhibit,

    // Every figure the archive holds for the company, under the concept each was
    // filed against.
    CompanyFacts,
}

// One document the archive is asked for.
//
// Two hosts rather than one: the structured endpoints answer on the data host and
// the filing documents on the main one. Carried on the request rather than fixed
// on a client, because one read crosses between them and a client per host would
// make the request count two numbers to add up.
public sealed record ArchiveRequest(ArchiveDocument Document, string Host, string Path);

// How a document is fetched. The live feed sends it and the recorded one finds
// the capture, and both hand the body to one reader.
//
// Null is the archive saying the document is not there, which is a case the live
// side has and the recorded side does not: a replay runs over a captured set that
// should be complete, so a capture nobody committed is a fault in the fixture and
// refuses by name rather than reading as an absence in the archive.
public delegate Task<string?> ArchiveFetch(ArchiveRequest request, CancellationToken cancellation);

// One filing as the company's own index lists it.
//
// `Items` is the 8-K's item numbers, which is the only thing that says a filing
// is a results announcement. Item 2.02 is results of operations, and it is what
// the earnings release is an exhibit to.
public sealed record IndexedFiling(
    string Form,
    string Accession,
    DateOnly FilingDate,
    DateOnly? PeriodEnd,
    IReadOnlyList<string> Items,
    string PrimaryDocument);

// One document a filing indexes, as the filing's own index page types it.
//
// The sequence is what orders two documents of the same type. A filing carrying
// two EX-99 exhibits states the release first and something else after it, so the
// rule that picks the release is stated by sequence rather than by taking the only
// match: taking the only match works until a filer files two.
public sealed record FiledDocument(int Sequence, string Type, string Description, string FileName);

// A period column of a rendered report: how long it covers and when it ended.
//
// Both halves, because one table carries the same end date under two spans. A
// reader keyed on the date alone takes the three-month figure and the nine-month
// figure as the same quarter.
public sealed record ReportPeriod(int Months, DateOnly Ended);

// One figure of a segment table.
//
// `Concept` is the accounting concept the renderer names in the row's own markup,
// which is what makes two rows with the same label comparable across filers.
//
// `Unit` is null where the figure is money in the table's stated currency, and
// names the unit otherwise. One captured filer states a count of segments in a
// money table, marked by a unit after its label, and a reader applying the table's
// scale to it records two million segments.
public sealed record SegmentFigure(
    string Concept,
    string LineItem,
    string? Unit,
    ReportPeriod Period,
    decimal? Value);

// One grouping of a segment table.
//
// The label is what separates two groups and the dimension is not. Four groups of
// one captured table share a single axis and member in the markup while their
// labels differ, and two labels appear twice, so neither is a unique key: the
// groups are a sequence in the order the table states them and never a set.
public sealed record SegmentGroup(string Label, string? Dimension, IReadOnlyList<SegmentFigure> Figures);

// A filing's segment table as the archive renders it.
//
// `Report` is which page it was read from, kept because the route chooses between
// candidates and a choice nobody records is a choice nobody can check.
//
// `Scale` is what the table's own title states its figures are in, as a
// multiplier. Stated once for the whole table, so a reader taking the figures at
// face value is out by a factor of a million.
//
// `Consolidated` is the rows above the first grouping, which are the company as a
// whole rather than a segment.
public sealed record SegmentBreakdown(
    string Report,
    string Title,
    int Scale,
    IReadOnlyList<ReportPeriod> Periods,
    IReadOnlyList<SegmentFigure> Consolidated,
    IReadOnlyList<SegmentGroup> Groups);

// Management's own forecast, as filed.
//
// Stored as the passage rather than as figures, and `Located` is the part that
// took a measurement. Over twelve filers, five state guidance under a heading and
// at least two more state it inside an ordinary paragraph, so a heading finds it
// for fewer than half. A passage nobody located is not a company that gave no
// guidance, and the two are never collapsed.
// see: Guidance is stored as management's own prose and never parsed into a figure
public sealed record FiledGuidance(
    string Document,
    DateOnly FiledOn,
    string? Heading,
    string? Passage)
{
    public bool Located => Passage is { Length: > 0 };
}

// One figure the archive holds, under the concept it was filed against.
//
// `Frame` is the calendar quarter the archive assigns, present only on the facts
// it treats as canonical for a period, and absent on a cumulative one. `Start`
// and `End` are what actually say how long a fact covers, and a nine-month figure
// sits beside a three-month one under the same fiscal period label.
//
// `Accession` is which filing stated it. A period appears twice where a later
// filing restated it, so a reader that does not take the later filing counts one
// quarter twice.
public sealed record ArchiveFact(
    string Concept,
    string Unit,
    DateOnly Start,
    DateOnly End,
    DateOnly Filed,
    string? Frame,
    string Accession,
    decimal Value);

// What one name's filings archive holds, as one read of it.
//
// `PartsNotCarried` is the same statement `CompanyFundamentals` makes and for the
// same reason: a part the archive does not serve is named, so the numbers section
// marks it absent rather than drawing a blank, and a blank cell reads as a zero.
//
// `SegmentReportsRead` is how many report pages the route fetched before one held
// figures by segment. Two captured filings need two and one, so the figure is
// carried rather than assumed.
public sealed record ArchiveFilings(
    string Ticker,
    string Cik,
    IReadOnlyList<IndexedFiling> Periodic,
    IReadOnlyList<IndexedFiling> Results,
    SegmentBreakdown? Segments,
    FiledGuidance? Guidance,
    IReadOnlyList<ArchiveFact> Facts,
    FiledDocument? Transcript,
    IReadOnlyList<string> PartsNotCarried,
    int SegmentReportsRead);

// One name's filings, from the archive, on demand.
//
// Per name and off the nightly path, which is the rule the company financials
// feed follows for the same reason: the night's per-name request count is zero.
// This provider is free and needs no key, so what bounds a read here is the
// archive's own fair-access policy rather than an allowance.
// see: The nightly run is arithmetic only
// see: Everything expensive happens when a name is opened
//
// The CIK is passed in rather than resolved here. The archive is addressed by CIK
// and nothing else, and the caller already holds one: the company financials
// payload carries it, zero-padded, for every name. Resolving it here would mean
// either a second whole-index file or a per-name lookup this feed would make on
// every read, and a feed that guessed at an identifier would address the wrong
// company's filings while looking like it worked.
public interface IFilingsArchiveFeed
{
    Task<ArchiveFilings> FilingsAsync(string ticker, string cik, CancellationToken cancellation = default);

    int Requests { get; }
}
